"""What the game exports to agents matches what it does (fixture: a seeded scripted game,
regenerated with analysis/diag_action_coverage.py --states after each build)."""
import gzip
import json
import os
import re

_FIXTURE = os.path.join(os.path.dirname(__file__), "fixtures", "game_states.jsonl.gz")
with gzip.open(_FIXTURE, "rt") as _f:
    STATES = [json.loads(line)["game_state"] for line in _f]

FOOD = [(t, c) for gs in STATES for t in gs.get("allActiveTasks") or [] for c in t.get("choices") or []
        if "meal" in (c.get("choiceText") or "").lower()]


def test_fixture_has_paid_and_hauled_food_choices():
    assert any(c.get("impacts") for _, c in FOOD) and any(not c.get("impacts") for _, c in FOOD)


def test_paid_food_reports_the_price_it_charges():
    """AgentChoice.ChargedBudget: $10/meal x the resolved quantity, not the authored placeholder."""
    for t, c in FOOD:
        budget = {i["type"]: i["value"] for i in c.get("impacts") or []}.get("Budget")
        if budget is not None:
            assert budget == -10 * c["deliveryQuantity"], (t["taskTitle"], c["choiceText"], budget)


def test_choice_text_states_the_quantity_delivered():
    """[food_amount] renders the live need, so the text never says 0 meals while delivering more."""
    for t, c in FOOD:
        n = [int(x) for x in re.findall(r"(\d+) (?:fast-food )?meals", c["choiceText"])]
        doubled = "2 x" in c["choiceText"] or "2x" in c["choiceText"]
        if n:
            assert n[0] * (2 if doubled else 1) == c["deliveryQuantity"], (t["taskTitle"], c["choiceText"])


def test_arriving_workers_are_exported():
    assert all("untrainedWorkersNotArrived" in gs["workforceState"] for gs in STATES)
