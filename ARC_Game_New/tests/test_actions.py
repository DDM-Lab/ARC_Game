"""The action menu (cora.actions) on real game states. The fixture's action lists were recorded by
the gym with manual transfers off, so the menu minus resource transfers must reproduce them."""
import gzip
import json
import os

import pytest

from cora.actions import assignment_action, enumerate_actions, staffing_need

_FIXTURE = os.path.join(os.path.dirname(__file__), "fixtures", "game_states.jsonl.gz")
with gzip.open(_FIXTURE, "rt") as _f:
    STATES = [json.loads(line) for line in _f]


@pytest.mark.parametrize("row", STATES, ids=lambda r: f"step{r['step']}")
def test_menu_reproduces_the_recorded_gym_menu(row):
    menu = [a for a in enumerate_actions(row["game_state"]) if a["action_type"] != "resource_transfer"]
    assert menu == row["actions"]


def test_menu_order_is_fixed_by_action_type():
    order = ["construction", "worker", "resource_transfer", "worker_assignment", "deconstruction"]
    for row in STATES:
        ranks = [order.index(a["action_type"]) for a in enumerate_actions(row["game_state"])]
        assert ranks == sorted(ranks)


def test_prices_come_from_the_game_state():
    gs = json.loads(json.dumps(STATES[0]["game_state"]))
    gs["workforceState"]["trainedWorkerCost"] = 1234
    hire = next(a for a in enumerate_actions(gs) if a["action_id"] == "hire_trained_2")
    assert hire["cost"] == 2468


def test_staffing_need():
    assert staffing_need({"buildingStatus": "NeedWorker", "requiredWorkforce": 4, "assignedWorkforce": 1}) == 3
    assert staffing_need({"buildingStatus": "InUse", "requiredWorkforce": 4, "assignedWorkforce": 4}) == 0
    assert staffing_need({"buildingStatus": "UnderConstruction", "requiredWorkforce": 4}) == 0


def test_untyped_assignment_is_what_the_executor_synthesizes():
    a = assignment_action("Shelter Alpha", 2, need=4)
    assert a["assignment"] == {"building_name": "Shelter Alpha", "quantity": 2}
    assert "worker_type" not in a["assignment"]        # Unity assigns trained workers first
