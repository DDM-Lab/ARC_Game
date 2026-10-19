"""Tune bench.baselines.rules' PARAMS on the surrogate, by successive halving over random
settings, then confirm the finalists on held-out seeds.

    python -m bench.baselines.tune --out tune.json [--configs 120] [--seed 9000] [--workers 8]

Tuning seeds start at --seed (well clear of the benchmark's 7000-7031, so the policy is not
fitted to the games it is scored on); the finalists are re-scored on the next block of seeds.
Prints and writes every stage's scores; the winner is the finalist with the best held-out mean.
"""
from __future__ import annotations

import argparse
import json
import random
import statistics as st
from concurrent.futures import ProcessPoolExecutor

SPACE = {
    "kitchens": list(range(2, 9)),
    "casework_sites": [1, 2, 3, 4],
    "max_shelters": list(range(0, 11)),
    "shelter_per_motel": [0, 50, 100, 200],
    "shelters_first": [0, 1, 2, 3, 4],
    "rrv": ["never", "fallback", "always"],
    "motel": [True, False],
    "build_until": [2, 4, 6, 10],
    "hire_trained": [False, True],
    "partial_food": [0, 50],
    "budget_food": [False, True],
    "budget_casework": [False, True],
}


def play(args):
    """(cfg, seed) -> the final score of rules(cfg) on the surrogate."""
    cfg, seed = args
    from bench.baselines.common import tool_calls
    from bench.baselines.rules import rules
    from cora import executor
    from cora.scoring import score_components
    from oracle.rollout import DECISIONS
    from oracle.sim.env import SimEnv
    env = SimEnv(seed=seed, max_episode_steps=DECISIONS + 4, manual_transfers=False)
    env.reset()
    for d in range(DECISIONS):
        _, (_, _, term, trunc, _) = executor.execute_turn(env, tool_calls(env, rules(env, d, DECISIONS, cfg)))
        if term or trunc:
            break
    return score_components(env.game_state.get("rewardMetrics"))["score"]


def score(pool, cfgs, seeds):
    jobs = [(c, s) for c in cfgs for s in seeds]
    res = list(pool.map(play, jobs, chunksize=4))
    n = len(seeds)
    return [res[i * n:(i + 1) * n] for i in range(len(cfgs))]


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--configs", type=int, default=120)
    ap.add_argument("--seed", type=int, default=9000)
    ap.add_argument("--workers", type=int, default=8)
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)
    from bench.baselines.rules import PARAMS
    rng = random.Random(a.seed)
    cfgs = [dict(PARAMS)] + [{k: rng.choice(v) for k, v in SPACE.items()} for _ in range(a.configs - 1)]
    tune16 = list(range(a.seed, a.seed + 16))
    held = list(range(a.seed + 16, a.seed + 32))
    log = {"stages": []}
    with ProcessPoolExecutor(a.workers) as pool:
        for keep, seeds in ((30, tune16[:4]), (5, tune16)):
            scores = score(pool, cfgs, seeds)
            ranked = sorted(zip(cfgs, scores), key=lambda cs: -st.mean(cs[1]))
            log["stages"].append([{"cfg": c, "seeds": seeds, "mean": st.mean(s)} for c, s in ranked])
            print(f"stage on {len(seeds)} seeds: best {st.mean(ranked[0][1]):.3f}, "
                  f"{keep}th {st.mean(ranked[min(keep, len(ranked)) - 1][1]):.3f}", flush=True)
            cfgs = [c for c, _ in ranked[:keep]]
        held_scores = score(pool, cfgs, held)
    final = sorted(({"cfg": c, "held_mean": st.mean(s), "held_se": st.stdev(s) / len(s) ** 0.5}
                    for c, s in zip(cfgs, held_scores)), key=lambda r: -r["held_mean"])
    log["finalists"] = final
    for r in final:
        print(f"held-out {r['held_mean']:.3f} ± {r['held_se']:.3f}  {json.dumps(r['cfg'])}")
    with open(a.out, "w") as f:
        json.dump(log, f, indent=1)


if __name__ == "__main__":
    main()
