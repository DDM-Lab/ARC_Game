"""The baseline policies (bench.baselines) decide on real captured game states without a game."""
import gzip
import json
import os

import pytest

from bench.baselines import POLICIES

_FIXTURE = os.path.join(os.path.dirname(__file__), "fixtures", "game_states.jsonl.gz")
with gzip.open(_FIXTURE, "rt") as _f:
    ROWS = [json.loads(line) for line in _f]


class _Env:
    """What a baseline reads: the state, the round's action menu and the step counter."""
    def __init__(self, row):
        self.game_state, self.valid_actions, self.current_step = row["game_state"], row["actions"], row["step"]

    def get_valid_actions(self):
        return self.valid_actions


def test_greedy_needs_the_surrogate():
    """greedy-N looks ahead by simulating, so on a state without a simulator it refuses plainly."""
    with pytest.raises(ValueError, match="surrogate"):
        POLICIES["greedy-1"](_Env(ROWS[0]), ROWS[0]["step"], 36)


def test_greedy_basket_beats_doing_nothing():
    """greedy-1's basket scores at least as well one decision ahead as the empty turn, every action
    in it resolves, and it never builds (a build only costs within one decision)."""
    import random
    from cora.executor import plan_turn
    from oracle.search import Sim, greedy_move
    from oracle.sim.env import SimEnv
    env = SimEnv(seed=7000, max_episode_steps=40, manual_transfers=False)
    env.reset()
    from cora import executor
    for d in range(6):
        sim = Sim(env, d, "fair", random.Random(d))
        calls = greedy_move(sim, horizon=1)
        assert sim.root[0][2] >= 0 and not any(t == "build" for t, _ in calls)
        results, _ = plan_turn(calls, env)
        assert all(r.status == "resolved" for r in results if r.tool != "staff")
        executor.execute_turn(env, calls)


def test_rules_calls_pass_through_and_resolve():
    """rules emits typed tool calls directly: tool_calls passes them through unchanged, and the
    executor resolves every one it plans (a staff call may still be refused by the game)."""
    from bench.baselines.common import tool_calls
    from cora.executor import plan_turn
    for row in ROWS:
        env = _Env(row)
        dec = POLICIES["rules"](env, row["step"], 36)
        calls = tool_calls(env, dec)
        assert calls == [tuple(c) for c in dec["tool_calls"]]
        results, _ = plan_turn(calls, env)
        assert all(r.status == "resolved" for r in results if r.tool != "staff"), \
            [(r.tool, r.args, r.reason) for r in results if r.status != "resolved"]


def test_hired_workers_are_not_free_the_same_turn():
    """Hiring is a request: workers arrive a game day later (WorkerRequestSystem), so a staff call
    in the same turn cannot use them."""
    from cora.executor import plan_turn
    row = next(r for r in ROWS if any(f.get("buildingStatus") == "NeedWorker"
                                      for f in r["game_state"]["mapState"]["facilities"]))
    gs = json.loads(json.dumps(row["game_state"]))
    gs["workforceState"]["freeTrainedWorkers"] = gs["workforceState"]["freeUntrainedWorkers"] = 0
    env = _Env({**row, "game_state": gs})
    site = next(f["facilityName"] for f in gs["mapState"]["facilities"] if f.get("buildingStatus") == "NeedWorker")
    results, _ = plan_turn([("hire", {"kind": "untrained", "count": 5}), ("staff", {"site": site})], env)
    assert results[0].status == "resolved"
    assert results[1].status == "invalid" and "free" in results[1].reason
