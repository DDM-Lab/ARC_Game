"""Rollouts: the policies that play Unity, played on the exact surrogate (oracle.sim.SimEnv).

A policy is a bench baseline -- policy(env, decision, decisions) -> {"choices", "actions"} --
acting through cora.executor exactly as it does on Unity, so a surrogate score IS the score that
policy gets in the game on that seed (tests/test_sim_env.py). Policies are named by a spec so
they can cross process boundaries and be rebuilt deterministically:

    {"name": "combined"}                                   a bench baseline
    {"name": "combined", "epsilon": 0.05}                  ... with random baskets on 5% of decisions
    {"name": "pareto", "cfg": {...}}                       a cora.policy_family member

Every rollout records its per-decision tool calls, the `capture.py --calls` format, so any result
can be replayed on Unity:

    python -m oracle.rollout --policy combined --epsilon 0.05 --seeds 200 --jobs 8 --out runs.jsonl
    python -m oracle.sim.capture <seed> --calls best_calls.json          # the best one, on Unity
"""
from __future__ import annotations

import argparse
import json
import statistics as st
import sys
from dataclasses import asdict, dataclass, field
from multiprocessing import Pool

from cora.env.game import DECISIONS  # the game's length; the drivers import it from here

FIRST_SEED = 5501


@dataclass
class Result:
    seed: int
    spec: dict
    score: float
    budget: float
    components: dict
    calls: list = field(default_factory=list)      # per decision: [[tool, args], ...]


def make_policy(spec: dict, seed: int):
    """The policy a spec names, with its random stream (if any) derived from the seed."""
    from bench.baselines import POLICIES
    name = spec["name"]
    if name == "pareto":
        cfg = spec.get("cfg")
        base = (lambda env, rnd, total: POLICIES["pareto"](env, rnd, total, cfg=cfg)) if cfg else POLICIES["pareto"]
    elif name == "noop":
        base = lambda env, rnd, total: {"choices": [], "actions": []}
    else:
        base = POLICIES[name]
    eps = float(spec.get("epsilon") or 0.0)
    if eps > 0:
        from bench.baselines.explore import explore
        return explore(base, eps, int(spec.get("explore_seed", seed)))
    return base


def play(spec: dict, seed: int, env=None) -> Result:
    """One game of the policy `spec` names on seed `seed` (or continue `env`, a SimEnv already
    part-way through a game)."""
    from bench.baselines.common import tool_calls
    from cora import executor
    from cora.scoring import score_components
    from oracle.sim.env import SimEnv
    if env is None:
        env = SimEnv(seed=seed, max_episode_steps=DECISIONS + 4, manual_transfers=False)
        env.reset()
    policy, calls = make_policy(spec, seed), []
    for i in range(env.current_step, DECISIONS):
        turn = tool_calls(env, policy(env, i, DECISIONS))
        calls.append([[t, a] for t, a in turn])
        _, (_, _r, terminated, truncated, _info) = executor.execute_turn(env, turn)
        if terminated or truncated:
            break
    comps = score_components(env.game_state.get("rewardMetrics"))
    return Result(seed, spec, comps["score"], float(env.game_state["satisfactionAndBudget"]["budget"]),
                  {k: v for k, v in comps.items() if isinstance(v, (int, float))}, calls)


def _play_args(args):
    return play(*args)


def evaluate(spec: dict, seeds, jobs: int = 1) -> list:
    """`spec` on every seed (common random numbers: compare specs on the same seed list)."""
    work = [(spec, s) for s in seeds]
    if jobs <= 1:
        return [play(*w) for w in work]
    with Pool(jobs) as pool:
        return pool.map(_play_args, work)


def summary(results) -> dict:
    scores = [r.score for r in results]
    return {"n": len(results), "mean_score": st.mean(scores), "sd_score": st.pstdev(scores),
            "mean_budget": st.mean(r.budget for r in results)}


def pareto_front(points):
    """Non-dominated (score, budget, ...) tuples, maximising both: nothing else is at least as
    good on both and strictly better on one. Best score first."""
    front, best_budget = [], float("-inf")
    for p in sorted(points, key=lambda t: (-t[0], -t[1])):
        if p[1] > best_budget:
            front.append(p)
            best_budget = p[1]
    return front


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--policy", default="combined")
    ap.add_argument("--epsilon", type=float, default=0.0, help="random-basket rate (bench.baselines.explore)")
    ap.add_argument("--cfg", help="JSON policy_family member, with --policy pareto")
    ap.add_argument("--seeds", type=int, default=20)
    ap.add_argument("--first-seed", type=int, default=FIRST_SEED)
    ap.add_argument("--jobs", type=int, default=1)
    ap.add_argument("--out", help="write every rollout (with its tool calls) as JSONL")
    ap.add_argument("--best-calls", help="write the best rollout's tool calls (capture.py --calls)")
    a = ap.parse_args(argv)
    spec = {"name": a.policy}
    if a.epsilon:
        spec["epsilon"] = a.epsilon
    if a.cfg:
        spec["cfg"] = json.loads(a.cfg)
    results = evaluate(spec, range(a.first_seed, a.first_seed + a.seeds), a.jobs)
    print(json.dumps(summary(results)))
    if a.out:
        with open(a.out, "w") as f:
            for r in results:
                f.write(json.dumps(asdict(r)) + "\n")
    if a.best_calls:
        best = max(results, key=lambda r: r.score)
        json.dump(best.calls, open(a.best_calls, "w"))
        print(f"best: seed {best.seed} score {best.score:.4f} -> {a.best_calls}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
