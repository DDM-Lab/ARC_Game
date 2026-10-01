"""propose_choices packages of typed calls resolve to action_indices INTO filtered_actions.

No Unity, no LLM, no network. Each package's calls resolve (cora.executor) to positions in
filtered_actions, the exact list the client renders and executes against, so the outbound
frame keeps its shape. Checks:
  1. _calls_to_indices maps build/hire/transfer/staff calls onto the right positions (staff via
     a STRUCTURAL (building, qty) match to the enumerated assignment).
  2. Unresolvable calls yield [] plus a reason (never a guessed index).
  3. _continuous_propose's frame has available_actions == filtered_actions and packages whose
     action_indices resolve to the intended actions.
  4. All-dropped packages return an honest ERROR, not a silent empty proposal.
"""
import asyncio
import os
import tempfile

import agent_router
from agent_router import Session
from router.config import load_config


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
        # Full staffing of a 4-unit building = 2 trained workers (buildings only run fully staffed).
        {"action_id": "assign_Kitchen Alpha_2", "action_type": "worker_assignment", "cost": 0,
         "description": "Assign 2 trained workers to Kitchen Alpha",
         "assignment": {"building_name": "Kitchen Alpha", "worker_type": "trained", "quantity": 2}},
        {"action_id": "assign_Shelter Beta_1", "action_type": "worker_assignment", "cost": 0,
         "description": "Assign 1 trained worker to Shelter Beta",
         "assignment": {"building_name": "Shelter Beta", "worker_type": "trained", "quantity": 1}},
        {"action_id": "xfer_food_1", "action_type": "resource_transfer", "cost": 0,
         "description": "Move 5 FoodPacks Kitchen Alpha->Shelter Beta",
         "transfer": {"resource_type": "FoodPacks", "quantity": 5,
                      "source_facility": "Kitchen Alpha", "destination_facility": "Shelter Beta"}},
    ]


def state():
    # Kitchen Alpha is built + still needing workers, so staff(site="Kitchen Alpha") resolves
    # (need > 0) and the executor synthesizes a worker_assignment.
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


def test_calls_to_indices():
    cfg = load_config("config/continuous_agents_domain.json")
    with tempfile.TemporaryDirectory() as td:
        sess = new_session(cfg, td, "sess-unit")
        fa = menu()
        gs = state()

        # single-category resolutions
        c2i = sess._calls_to_indices
        idx, reasons = c2i([("build", {"type": "kitchen", "site_id": 1})], fa, gs)
        assert idx == [0], f"build -> {idx} (reasons={reasons})"
        idx, _ = c2i([("hire", {"kind": "untrained", "count": 1})], fa, gs)
        assert idx == [2], f"hire -> {idx}"
        idx, r = c2i([("transfer", {"resource": "food", "source": "Kitchen Alpha",
                                    "dest": "Shelter Beta", "qty": 5})], fa, gs)
        assert idx == [5], f"transfer -> {idx} (reasons={r})"

        # staff's synthesized assignment structurally matches the enumerated (building, qty) one
        idx, r = c2i([("staff", {"site": "Kitchen Alpha"})], fa, gs)
        assert idx == [3], f"staff -> {idx} (reasons={r})"

        # combined package: a second build on the same site is dropped (the site is taken)
        idx, r = c2i([("build", {"type": "kitchen", "site_id": 1}), ("hire", {"kind": "untrained", "count": 1}),
                      ("build", {"type": "kitchen", "site_id": 1})], fa, gs)
        assert idx == [0, 2], f"combined -> {idx} (reasons={r})"

        # all-unresolvable -> [] + a logged reason (never a guessed index)
        idx, r = c2i([("build", {"type": "kitchen", "site_id": 99})], fa, gs)
        assert idx == [] and r, f"bad-site should drop with reason; got {idx}, {r}"

        # a partial staffing count is refused with the reason (buildings only run fully staffed)
        idx, r = c2i([("staff", {"site": "Kitchen Alpha", "count": 3})], fa, gs)
        assert idx == [] and any("would be partial" in x for x in r), \
            f"partial staffing should drop with reason; got {idx}, {r}"

        print("[1] _calls_to_indices ok: build=0 hire=2 transfer=5 staff=3, "
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
                 "calls": [{"tool": "build", "args": {"type": "kitchen", "site_id": 1}},
                           {"tool": "staff", "args": {"site": "Kitchen Alpha"}}]},
                {"label": "Grow", "description": "Hire and move food",
                 "calls": [{"tool": "hire", "args": {"kind": "untrained", "count": 1}},
                           {"tool": "transfer", "args": {"resource": "food", "source": "Kitchen Alpha",
                                                         "dest": "Shelter Beta", "qty": 5}}]},
                {"label": "Junk", "description": "unresolvable",
                 "calls": [{"tool": "build", "args": {"type": "kitchen", "site_id": 99}}]},
            ],
        }
        body, gs2, all2, fa2, executed, superseded, _rows = await sess._continuous_propose(
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

        # resolution: filtered_actions[i] for each action_index == the intended actions
        feed_ids = [fa[i]["action_id"] for i in pkgs[0]["action_indices"]]
        grow_ids = [fa[i]["action_id"] for i in pkgs[1]["action_indices"]]
        assert feed_ids == ["build_Kitchen_1", "assign_Kitchen Alpha_2"], feed_ids
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
            {"label": "Bad", "calls": [{"tool": "build", "args": {"type": "kitchen", "site_id": 99}}]},
            {"label": "Empty", "calls": []},
        ]}
        body, *_ = await sess._continuous_propose(agent, args, state(), menu(), menu())
        assert body.startswith("ERROR:"), f"all-dropped should ERROR, got: {body[:80]}"
        # No proposal frame should have gone out.
        assert not [p for p in sent if p.get("type") == "agent_message_with_choices"], \
            "sent a proposal frame despite zero valid packages"
        print("[3] all-dropped ok: honest ERROR, no proposal frame sent")


async def main():
    test_calls_to_indices()
    await test_outbound_parity()
    await test_all_dropped_errors()
    print("\nALL STEP-6 PROPOSE TESTS PASSED ✓")


if __name__ == "__main__":
    asyncio.run(main())
