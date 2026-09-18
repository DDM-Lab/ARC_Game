"""Hermetic test for Step 6: propose_choices under the tags-only vocabulary.

No Unity, no LLM, no network. Verifies that the model-authored `commands` (command
tags) on each proposal package are resolved to `action_indices` that point back INTO
filtered_actions — the exact list the Unity client renders and executes against — so
the outbound payload stays byte-identical (no client change) and the autonomous path
resolves the same actions the tags named.

Checks:
  1. _tags_to_indices maps build/hire/transfer/staff tags onto the right positions
     in filtered_actions (staff via STRUCTURAL (building,qty) match, since the synth
     prose diverges from the enumerated prose by construction).
  2. A package of only unresolvable tags yields [] + a logged reason (never mis-index);
     a <task> tag is dropped with a reason (tasks are answered via execute_commands).
  3. _continuous_propose's outbound frame has available_actions == filtered_actions
     and packages whose action_indices resolve (in filtered_actions) to the tagged
     actions — i.e. the autonomous [filtered_actions[i] ...] pick == the intended set.
  4. All-dropped packages return an honest ERROR, not a silent empty proposal.

Run:
  env -u ALL_PROXY -u all_proxy -u HTTPS_PROXY -u https_proxy \
      -u HTTP_PROXY -u http_proxy PYTHONPATH="$(pwd)" ./.venv/bin/python test_propose_tags.py
"""
import asyncio
import os
import tempfile

import agent_router
from agent_router import Session
from agent_config import load_config


def menu():
    """A cross-category action menu (same shape as the live enumerator)."""
    return [
        {"action_id": "build_Kitchen_1", "action_type": "construction", "cost": 1000,
         "description": "Build Kitchen at Site1",
         "construction": {"building_type": "Kitchen", "site_id": 1, "site_name": "Site1"}},
        {"action_id": "build_Shelter_2", "action_type": "construction", "cost": 1000,
         "description": "Build Shelter at Site2",
         "construction": {"building_type": "Shelter", "site_id": 2, "site_name": "Site2"}},
        {"action_id": "hire_untrained_1", "action_type": "worker", "cost": 100,
         "description": "Hire 1 untrained worker",
         "worker": {"worker_action_type": "hire_untrained", "quantity": 1}},
        {"action_id": "assign_Kitchen Alpha_1", "action_type": "worker_assignment", "cost": 0,
         "description": "Assign 1 trained worker to Kitchen Alpha",
         "assignment": {"building_name": "Kitchen Alpha", "worker_type": "trained", "quantity": 1}},
        {"action_id": "assign_Shelter Beta_1", "action_type": "worker_assignment", "cost": 0,
         "description": "Assign 1 trained worker to Shelter Beta",
         "assignment": {"building_name": "Shelter Beta", "worker_type": "trained", "quantity": 1}},
        {"action_id": "xfer_food_1", "action_type": "resource_transfer", "cost": 0,
         "description": "Move 5 FoodPacks Kitchen Alpha->Shelter Beta",
         "transfer": {"resource_type": "FoodPacks", "quantity": 5,
                      "source_facility": "Kitchen Alpha", "destination_facility": "Shelter Beta"}},
    ]


def state():
    # Kitchen Alpha is built + still needing workers, so <staff>Kitchen Alpha</staff>
    # resolves (need[...] > 0) and the parser synthesizes a worker_assignment.
    return {
        "_v": 1,
        "sessionInfo": {"currentDay": 1, "currentSegment": 0},
        "satisfactionAndBudget": {"satisfaction": 50.0, "budget": 100000,
                                  "overallSatisfaction": 50.0, "currentBudget": 100000},
        "dailyMetrics": {"currentBudget": 100000},
        "workforceState": {"freeTrainedWorkers": 4, "freeUntrainedWorkers": 4},
        "workers": {"free": 8},
        "buildings": {}, "tasks": [], "constructionState": {},
        "mapState": {"facilities": [
            {"facilityName": "Kitchen Alpha", "buildingStatus": "NeedWorker",
             "requiredWorkforce": 4, "assignedWorkforce": 0},
        ]},
        "logistics": {},
    }


def new_session(cfg, td, name):
    return Session(cfg, name, "test", os.path.join(td, "log.jsonl"), websocket=None)


def test_tags_to_indices():
    cfg = load_config("config/continuous_agents_domain.json")
    with tempfile.TemporaryDirectory() as td:
        sess = new_session(cfg, td, "sess-unit")
        fa = menu()
        gs = state()

        # single-category resolutions
        idx, reasons = sess._tags_to_indices("<build>Kitchen,1</build>", fa, gs)
        assert idx == [0], f"build -> {idx} (reasons={reasons})"
        idx, _ = sess._tags_to_indices("<hire>untrained,1</hire>", fa, gs)
        assert idx == [2], f"hire -> {idx}"
        idx, r = sess._tags_to_indices(
            "<transfer>food,Kitchen Alpha,Shelter Beta,5</transfer>", fa, gs)
        assert idx == [5], f"transfer -> {idx} (reasons={r})"

        # <staff> synth structurally matches the enumerated (building,qty) assignment
        idx, r = sess._tags_to_indices("<staff>Kitchen Alpha,1</staff>", fa, gs)
        assert idx == [3], f"staff -> {idx} (reasons={r})"

        # combined multi-tag package, deduped + order-preserving
        idx, r = sess._tags_to_indices(
            "<build>Kitchen,1</build>\n<hire>untrained,1</hire>\n<build>Kitchen,1</build>",
            fa, gs)
        assert idx == [0, 2], f"combined -> {idx} (reasons={r})"

        # all-unresolvable -> [] + a logged reason (never a guessed index)
        idx, r = sess._tags_to_indices("<build>Kitchen,99</build>", fa, gs)
        assert idx == [] and r, f"bad-site should drop with reason; got {idx}, {r}"

        # staffing more than any offered quantity -> honest drop
        idx, r = sess._tags_to_indices("<staff>Kitchen Alpha,3</staff>", fa, gs)
        assert idx == [] and any("not offered at that quantity" in x for x in r), \
            f"over-staff should drop with reason; got {idx}, {r}"

        print("[1] _tags_to_indices ok: build=0 hire=2 transfer=5 staff=3, "
              "dedupe+order kept, unresolved dropped-with-reason")


async def test_outbound_parity():
    cfg = load_config("config/continuous_agents_domain.json")
    with tempfile.TemporaryDirectory() as td:
        sess = new_session(cfg, td, "sess-parity")
        agent = cfg.get_subagents()[0]

        sent = []

        async def fake_send(payload):
            sent.append(payload)
        sess._send = fake_send

        # filter = identity so filtered_actions is the full menu (scope-independent test)
        agent_router.filter_actions = lambda actions, space: list(actions)
        agent_router._enumerate_actions = lambda gs: menu()

        captured = {}

        async def fake_await(packages, filtered_actions, game_state, reasoning):
            # Snapshot what the client would render + execute against.
            captured["packages"] = packages
            captured["filtered_actions"] = filtered_actions
            # Canned director pick of package 0; engine reports every bundled action ok.
            ai = packages[0]["action_indices"]
            exec_results = [{"success": True, "action_id": filtered_actions[i]["action_id"]}
                            for i in ai]
            return 0, exec_results, game_state, False
        sess._await_director_choice = fake_await

        fa = menu()
        gs = state()
        args = {
            "reasoning": "Two ways to spend today.",
            "packages": [
                {"label": "Feed", "description": "Build a kitchen and staff it",
                 "commands": "<build>Kitchen,1</build>\n<staff>Kitchen Alpha,1</staff>"},
                {"label": "Grow", "description": "Hire and move food",
                 "commands": "<hire>untrained,1</hire>\n"
                             "<transfer>food,Kitchen Alpha,Shelter Beta,5</transfer>"},
                {"label": "Junk", "description": "unresolvable", "commands": "<build>Kitchen,99</build>"},
            ],
        }
        body, gs2, all2, fa2, executed, superseded = await sess._continuous_propose(
            agent, args, gs, menu(), fa)

        # outbound frame parity: exactly one inline-proposal frame, available_actions IS fa
        frames = [p for p in sent if p.get("type") == "agent_message_with_choices"]
        assert len(frames) == 1, f"expected 1 inline proposal frame, got {len(frames)}"
        frame = frames[0]
        assert frame["available_actions"] == fa, "available_actions != filtered_actions"

        pkgs = frame["packages"]
        # Junk dropped -> only 2 packages survive, re-indexed 0..1
        assert len(pkgs) == 2, f"expected 2 surviving packages, got {len(pkgs)}"
        assert [p["package_index"] for p in pkgs] == [0, 1], "package_index not re-sequenced"

        # autonomous resolution: filtered_actions[i] for each action_index == tagged set
        feed_ids = [fa[i]["action_id"] for i in pkgs[0]["action_indices"]]
        grow_ids = [fa[i]["action_id"] for i in pkgs[1]["action_indices"]]
        assert feed_ids == ["build_Kitchen_1", "assign_Kitchen Alpha_1"], feed_ids
        assert grow_ids == ["hire_untrained_1", "xfer_food_1"], grow_ids

        assert not superseded and executed == 2, f"executed={executed} superseded={superseded}"
        print("[2] outbound parity ok: available_actions==filtered_actions; "
              f"Feed->{feed_ids}, Grow->{grow_ids}; junk package dropped")


async def test_all_dropped_errors():
    cfg = load_config("config/continuous_agents_domain.json")
    with tempfile.TemporaryDirectory() as td:
        sess = new_session(cfg, td, "sess-empty")
        agent = cfg.get_subagents()[0]
        sent = []

        async def fake_send(payload):
            sent.append(payload)
        sess._send = fake_send
        agent_router.filter_actions = lambda actions, space: list(actions)
        agent_router._enumerate_actions = lambda gs: menu()

        args = {"reasoning": "x", "packages": [
            {"label": "Bad", "commands": "<build>Kitchen,99</build>"},
            {"label": "Empty", "commands": ""},
        ]}
        body, *_ = await sess._continuous_propose(agent, args, state(), menu(), menu())
        assert body.startswith("ERROR:"), f"all-dropped should ERROR, got: {body[:80]}"
        # No proposal frame should have gone out.
        assert not [p for p in sent if p.get("type") == "agent_message_with_choices"], \
            "sent a proposal frame despite zero valid packages"
        print("[3] all-dropped ok: honest ERROR, no proposal frame sent")


async def main():
    test_tags_to_indices()
    await test_outbound_parity()
    await test_all_dropped_errors()
    print("\nALL STEP-6 PROPOSE TESTS PASSED ✓")


if __name__ == "__main__":
    asyncio.run(main())
