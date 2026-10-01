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
