"""The surrogate is playable by the code that plays Unity, with the same results.

Two checks per captured game (tests/fixtures/sim_parity holds what Unity accepted per decision;
tests/fixtures/sim_env holds, per decision, digests of what Unity showed the policies):

  export   oracle.sim.export.game_state of the port's world at each decision gives the same
           action menu (cora.actions.enumerate_actions), the same observation text
           (cora.observation.user_message, compact, default ObsConfig) and the same combined and
           greedy decisions as Unity's own state did;
  native   the capture's policy (combined, or combined + random baskets with the capture's
           epsilon and seed) played natively on SimEnv through cora.executor sends the same
           answers and actions every decision and ends on the same score.

Rebuild the sim_env fixtures from captures (oracle/sim/capture.py) with
    python tests/test_sim_env.py build oracle/sim/runs/<dir> [...]
"""
import glob
import gzip
import hashlib
import json
import os
import re
import sys

import pytest

HERE = os.path.dirname(os.path.abspath(__file__))
PARITY = os.path.join(HERE, "fixtures", "sim_parity")
ENV = os.path.join(HERE, "fixtures", "sim_env")

# Need the sim changes reported with oracle/sim/export.py (tracker event order; Unity's
# facility lookup in sim.answer; a deconstruct of an existing building succeeds). Remove an
# entry once its fixture passes.
def _sha(obj) -> str:
    return hashlib.sha1(json.dumps(obj, sort_keys=True).encode()).hexdigest()[:16]


class _View:
    """A GameEnv-shaped view of one state, as the baselines read it (transfers off, as in the
    benchmark's default)."""
    def __init__(self, gs):
        from cora.actions import enumerate_actions
        self.game_state = gs
        self.valid_actions = [a for a in enumerate_actions(gs) if a.get("action_type") != "resource_transfer"]

    def get_valid_actions(self):
        return self.valid_actions


def digest(gs) -> dict:
    """What a policy sees of one state, reduced to comparable digests."""
    from bench.baselines import POLICIES
    from cora.actions import enumerate_actions
    from cora.observation import observe, user_message
    actions = enumerate_actions(gs)
    out = {"ids": _sha([a["action_id"] for a in actions]),
           "text": _sha(user_message(observe(gs, actions))),
           "tasks": [t["taskId"] for t in gs.get("allActiveTasks") or []]}
    for name in ("combined", "greedy"):
        view = _View(gs)
        dec = POLICIES[name](view, 0, 36)
        out[name] = [sorted([c["taskId"], c["choiceId"]] for c in dec.get("choices") or []),
                     [view.valid_actions[i]["action_id"] for i in dec.get("actions") or []]]
    return out


def _load(path):
    with gzip.open(path, "rt") as f:
        return json.load(f)


def _worlds(fixture):
    """The port's world at the start of each decision of a parity fixture's replay."""
    from oracle.sim.lockstep import replay_step
    from oracle.sim.sim import new_world
    from oracle.sim.floodmap import FloodMap
    import oracle.sim.sim as S
    w = new_world(tuple(fixture["seed_state"]), FloodMap.load())
    for step in fixture["steps"]:
        yield w
        w = w.clone()
        replay_step(w, step)
        S.step(w)


def _policy(source):
    from bench.baselines import POLICIES
    from bench.baselines.explore import explore
    m = re.match(r"policy:([\w-]+)(?:\+explore\(eps=([\d.]+),seed=(\d+)\))?", source or "")
    if not m:
        return None
    base = POLICIES[m.group(1)]
    return explore(base, float(m.group(2)), int(m.group(3))) if m.group(2) else base


NAMES = sorted(os.path.basename(p)[:-8] for p in glob.glob(os.path.join(ENV, "*.json.gz")))


@pytest.mark.parametrize("name", NAMES)
def test_export_matches_unity(name):
    from oracle.sim.export import game_state
    fixture, want = _load(os.path.join(PARITY, name + ".json.gz")), _load(os.path.join(ENV, name + ".json.gz"))
    bad = [i for i, (w, d) in enumerate(zip(_worlds(fixture), want["decisions"]))
           if digest(game_state(w)) != d]
    assert bad == []


@pytest.mark.parametrize("name", [n for n in NAMES
                                  if _policy(_load(os.path.join(PARITY, n + ".json.gz")).get("source"))])
def test_native_play_matches_capture(name):
    """The policy a capture recorded, playing the surrogate through SimEnv, takes Unity's actions
    and ends on Unity's score. Captures on the decision clock are played as the env plays (the
    end-of-day stop rolled through); older ones acted at every Unity stop."""
    from bench.baselines.common import tool_calls
    from cora import executor
    from cora.env import DECISIONS, UNITY_STOPS
    from cora.scoring import score_components
    from oracle.sim.env import SimEnv
    fixture = _load(os.path.join(PARITY, name + ".json.gz"))
    policy = _policy(fixture["source"])
    by_decision = fixture.get("clock") == "decisions"
    steps = ([s for s in fixture["steps"] if s.get("decision") is not None] if by_decision
             else fixture["steps"])
    if by_decision:      # an end-of-day stop sends nothing
        assert all(not s["taken"] for s in fixture["steps"] if s.get("decision") is None)
    env = SimEnv(seed=fixture["seed"], max_episode_steps=40, manual_transfers=False,
                 skip_end_of_day=by_decision)
    env.reset()
    total = DECISIONS if by_decision else UNITY_STOPS
    bad = []
    for i, step in enumerate(steps):
        results, (_, _r, term, trunc, info) = executor.execute_turn(env, tool_calls(env, policy(env, i, total)))
        mine = (sorted([r.choice["taskId"], r.choice["choiceId"]] for r in results
                       if r.choice is not None and r.status == "executed"),
                [a.get("action_id") for a, res in zip(info.get("dispatched") or [], info.get("execution_results") or [])
                 if res.get("success")])
        want = (sorted([a["taskId"], a["choiceId"]] for a in step["taken"] if a.get("kind") == "choice"),
                [a.get("action_id") for a in step["taken"] if a.get("kind") in ("menu", "staff") and a.get("ok")])
        if mine != want:
            bad.append(i)
        if term or trunc:
            break
    assert bad == []
    assert round(score_components(env.game_state["rewardMetrics"])["score"], 4) == fixture["expected"][-1]["score"]


def test_clone_is_independent():
    """A clone plays forward exactly as the original would, and playing it leaves the original as it was."""
    from oracle.rollout import play
    from oracle.sim.env import SimEnv
    from bench.baselines.common import tool_calls
    from bench.baselines import POLICIES
    from cora import executor
    from cora.env import DECISIONS
    env = SimEnv(seed=5503, max_episode_steps=40, manual_transfers=False)
    env.reset()
    for i in range(10):
        executor.execute_turn(env, tool_calls(env, POLICIES["combined"](env, i, DECISIONS)))
    before = digest(env.game_state)
    a, b = env.clone(), env.clone()
    ra, rb = play({"name": "combined"}, 5503, env=a), play({"name": "combined"}, 5503, env=b)
    assert (ra.score, ra.calls) == (rb.score, rb.calls)
    assert digest(env.game_state) == before and env.current_step == 10
    assert play({"name": "combined"}, 5503, env=env).score == ra.score


@pytest.mark.parametrize("skip", [True, False])
def test_rewards_sum_to_final_score(skip):
    """An episode's step rewards sum to its final score (the end-of-day rollover's change folds into
    the step that triggered it), and a game ends on its own after DECISIONS (or UNITY_STOPS) steps."""
    from bench.baselines import POLICIES
    from bench.baselines.common import tool_calls
    from bench.baselines.explore import explore
    from cora import executor
    from cora.env import DECISIONS, UNITY_STOPS
    from cora.scoring import score_components
    from oracle.sim.env import SimEnv
    for seed in (5501, 5502, 5503):
        policy = explore(POLICIES["combined"], 0.25, seed)
        env = SimEnv(seed=seed, max_episode_steps=40, manual_transfers=False, skip_end_of_day=skip)
        env.reset()
        total, steps = 0.0, 0
        while True:
            _, (_, reward, term, trunc, _i) = executor.execute_turn(env, tool_calls(env, policy(env, steps, DECISIONS)))
            total += reward; steps += 1
            if term or trunc:
                break
        assert term and steps == (DECISIONS if skip else UNITY_STOPS)
        assert abs(total - score_components(env.game_state["rewardMetrics"])["score"]) < 1e-9

def build(dirs):
    """sim_env fixtures from captures: per decision, digest() of Unity's own state."""
    from oracle.sim.parity import fixture_name
    os.makedirs(ENV, exist_ok=True)
    for d in dirs:
        for path in sorted(glob.glob(os.path.join(d, "staff_*.json"))):
            if path.endswith(".meta.json"):
                continue
            meta = json.load(open(path.replace(".json", ".meta.json")))
            trace = json.load(open(path))
            name = fixture_name(meta.get("source"), meta["seed"],
                                "decisions" if trace and "decision" in trace[0] else "stops")
            with gzip.open(os.path.join(ENV, name + ".json.gz"), "wt") as f:
                json.dump({"seed": meta["seed"], "decisions": [digest(s["before"]) for s in trace]},
                          f, separators=(",", ":"))
            print(name)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "build":
        sys.path.insert(0, os.path.dirname(HERE))
        build(sys.argv[2:])
