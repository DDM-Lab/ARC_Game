"""The observation (cora.observation) on real game states captured from a seeded scripted game
(tests/fixtures/game_states.jsonl.gz: Day 1 through game over, with the turn's action lists)."""
import gzip
import json
import os

import pytest

from cora.observation import (ObsConfig, observe, officer_text, render, task_group, task_officer,
                              task_token)

_FIXTURE = os.path.join(os.path.dirname(__file__), "fixtures", "game_states.jsonl.gz")
with gzip.open(_FIXTURE, "rt") as _f:
    STATES = [json.loads(line) for line in _f]


@pytest.mark.parametrize("row", STATES, ids=lambda r: f"step{r['step']}")
def test_render_is_complete_and_carries_live_numbers(row):
    gs, actions = row["game_state"], row["actions"]
    obs = observe(gs, actions)
    text = render(obs)
    assert text.startswith(f"day {gs['sessionInfo']['currentDay']} |")
    assert "available:" in text and "tasks" in text
    # prices come from the game state, never from Python
    assert obs["costs"]["build"] == gs["constructionState"]["buildingConstructionCost"]


def test_rounds_left_counts_simulated_rounds_from_the_game():
    start = observe(STATES[0]["game_state"])
    assert start["roundsLeft"] == 32           # Day 1 round 0 of an 8-day, 4-round game
    end = observe(STATES[-1]["game_state"])
    assert end["roundsLeft"] == 0


def test_rounds_left_omitted_when_build_does_not_export_the_horizon():
    gs = json.loads(json.dumps(STATES[0]["game_state"]))
    del gs["sessionInfo"]["roundsPerDay"]
    assert "roundsLeft" not in observe(gs)


def test_unavailable_choices_marked_only_when_configured():
    gs = json.loads(json.dumps(next(r for r in STATES if r["game_state"].get("allActiveTasks"))["game_state"]))
    gs["allActiveTasks"][0]["choices"][0].update(feasible=False, unavailableReason="No meals available")
    assert "UNAVAILABLE" not in render(observe(gs))
    assert "UNAVAILABLE now: No meals available" in render(observe(gs, config=ObsConfig(mark_unavailable_choices=True)))


def test_delta_render_reports_only_changed_facilities():
    a, b = observe(STATES[1]["game_state"]), observe(STATES[2]["game_state"])
    assert "facilities Δ" in render(b, prev=a)
    assert "(no change)" in render(a, prev=a)


def test_officer_sections():
    gs, actions = STATES[3]["game_state"], STATES[3]["actions"]
    assert officer_text(gs).startswith("day ")
    assert officer_text(gs, "facilities").startswith("facilities [")
    assert officer_text(gs, "workforce").startswith("day ")
    assert officer_text(gs, "logistics", actions).startswith("available:")


@pytest.mark.parametrize("title,affects,token,officer", [
    ("Daily Budget Allocation", "", "BUDGET_DAILY", "ExternalRelationship"),
    ("Community Trinity Food Request", "Community Trinity", "FOOD_CTRINITY", "FoodMassCare"),
    ("Population Relocation Request", "Community Amherst", "RELOC_CAMHERST", "LodgingMassCare"),
    ("Casework Request", "Shelter_0", "CASEWORK_S0", "LodgingMassCare"),
    ("Something New", "", "TASK_42", "DisasterOfficer"),
])
def test_task_identity(title, affects, token, officer):
    t = {"title": title, "affects": affects, "taskId": 42}
    assert task_token(t) == token
    assert task_officer(t) == officer
    assert task_group(t) in ("budget", "workforce", "food", "lodging", "disaster")
