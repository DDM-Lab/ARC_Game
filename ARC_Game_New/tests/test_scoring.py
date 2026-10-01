"""The score (cora.scoring) read from the game's rewardMetrics on real captured states."""
import gzip
import json
import os

from cora.scoring import COMPONENTS, score_components

_FIXTURE = os.path.join(os.path.dirname(__file__), "fixtures", "game_states.jsonl.gz")
with gzip.open(_FIXTURE, "rt") as _f:
    STATES = [json.loads(line)["game_state"] for line in _f]


def test_score_is_the_games_satisfaction_plus_efficiency():
    for gs in STATES:
        rm = gs["rewardMetrics"]
        c = score_components(rm)
        assert c["scored"]
        assert abs(c["score"] - (rm["liveSatisfaction"] + rm["liveEfficiency"]) / 1000.0) < 1e-9


def test_unscored_state_is_all_zero():
    for rm in (None, {}, {"scoreAvailable": False, "liveSatisfaction": 500}):
        c = score_components(rm)
        assert not c["scored"] and all(c[k] == 0.0 for k in COMPONENTS)
