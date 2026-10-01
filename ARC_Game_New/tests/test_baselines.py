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


@pytest.mark.parametrize("name", sorted(POLICIES))
def test_policy_decides_on_every_state(name):
    for row in ROWS:
        env = _Env(row)
        dec = POLICIES[name](env, row["step"], 36)
        task_ids = {t["taskId"] for t in row["game_state"].get("allActiveTasks") or []}
        assert all(0 <= i < len(env.valid_actions) for i in dec["actions"])
        assert all(c["taskId"] in task_ids for c in dec["choices"])


@pytest.mark.parametrize("name", sorted(POLICIES))
def test_tool_calls_resolve_to_what_the_policy_picked(name):
    """Baselines act through cora.executor: every converted call must resolve to the menu action
    (or task answer) the policy chose. Staffing is the one intended difference: the executor
    staffs a building fully, so a staff call resolves to a synthesized full assignment."""
    from bench.baselines.common import tool_calls
    from cora.executor import plan_turn
    for row in ROWS:
        env = _Env(row)
        dec = POLICIES[name](env, row["step"], 36)
        calls = tool_calls(env, dec)
        results, _ = plan_turn(calls, env)
        assert len(results) == len(calls)
        # combined's two halves can each pick a building for the same site; the game refuses the
        # second, and the executor now catches it before anything is sent.
        clash = [r for r in results if r.status == "invalid" and "already being built on" in r.reason]
        clash_sites = [r.args["site_id"] for r in clash]
        picked = [i for i in dec["actions"]
                  if env.valid_actions[i]["action_type"] != "worker_assignment"]
        for site in clash_sites:      # the executor keeps the build the policy listed first
            on_site = [i for i in picked if env.valid_actions[i].get("construction", {}).get("site_id") == site]
            picked.remove(on_site[-1])
        resolved = [i for r in results if r.tool not in ("task", "staff") for i in r.action_indices]
        assert all(r.status == "resolved" for r in results if r.tool != "staff" and r not in clash), \
            [(r.tool, r.args, r.reason) for r in results if r.status != "resolved"]
        assert sorted(resolved) == sorted(picked), (row["step"], resolved, picked)
        assert sorted((r.choice["taskId"], r.choice["choiceId"]) for r in results if r.tool == "task") == \
            sorted((c["taskId"], c["choiceId"]) for c in dec["choices"])


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
