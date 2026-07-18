"""Offline concurrency test for multi-agent continuous officers.

No Unity, no LLM, no network. Monkeypatches run_tool_step (scripted officer
behavior) and _enumerate_actions (fixed cross-category menu), and simulates Unity
by resolving _pending_action through the real _handle_action_result. Verifies:

  1. COMMIT LOCK: many concurrent _execute_action calls never cross results
     (each caller gets back exactly the action it sent), even though the fake
     Unity resolves asynchronously after a delay.
  2. CONCURRENT DISPATCH: _handle_begin_round runs the officers' tool-loops
     overlapping (max concurrency >= 2), not one-after-another.
  3. HARD SCOPING: each officer only ever executes actions inside its
     subaction_space (Food->Kitchen, Lodging->Shelter, External->transfer/Casework,
     Workforce->hire), never a peer's.
  4. NO STATE REGRESSION: _latest_game_state ends at the highest version any
     commit produced.

Run:
  env -u ALL_PROXY -u all_proxy -u HTTPS_PROXY -u https_proxy \
      -u HTTP_PROXY -u http_proxy ./.venv/bin/python test_concurrent_officers.py
"""
import asyncio
import os
import tempfile

import agent_router
from agent_router import Session
from agent_config import load_config


# ── Fixed action menu spanning every category + building type ────────────────
def fake_enumerate(game_state):
    v = game_state.get("_v", 0)  # carry a version so we can detect regressions
    return [
        {"action_id": "build_Kitchen_1", "action_type": "construction", "cost": 1000,
         "description": "Build Kitchen at Site1",
         "construction": {"building_type": "Kitchen", "site_id": 1, "site_name": "Site1"}},
        {"action_id": "build_Shelter_2", "action_type": "construction", "cost": 1000,
         "description": "Build Shelter at Site2",
         "construction": {"building_type": "Shelter", "site_id": 2, "site_name": "Site2"}},
        {"action_id": "build_CaseworkSite_3", "action_type": "construction", "cost": 1000,
         "description": "Build CaseworkSite at Site3",
         "construction": {"building_type": "CaseworkSite", "site_id": 3, "site_name": "Site3"}},
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


# Expected in-scope action_id sets per officer (from the domain config).
EXPECTED_SCOPE = {
    "Workforce Officer": {"hire_untrained_1"},
    "Lodging Officer": {"build_Shelter_2", "assign_Shelter Beta_1"},
    "Food Officer": {"build_Kitchen_1", "assign_Kitchen Alpha_1"},
    "External Relations Officer": {"build_CaseworkSite_3", "xfer_food_1"},
}


def base_state(v=0):
    return {
        "_v": v,
        "sessionInfo": {"currentDay": 1, "currentSegment": 0},
        "satisfactionAndBudget": {"satisfaction": 50.0, "budget": 100000,
                                  "overallSatisfaction": 50.0, "currentBudget": 100000},
        "dailyMetrics": {"currentBudget": 100000},
        "workforceState": {"freeTrainedWorkers": 4, "freeUntrainedWorkers": 4},
        "workers": {"free": 8},
        "buildings": {}, "tasks": [], "constructionState": {}, "mapState": {"facilities": []},
        "logistics": {},
    }


async def run_commit_lock_test():
    """Directly hammer _execute_action from many coroutines; assert no crossing."""
    cfg = load_config("config/continuous_agents_domain.json")
    with tempfile.TemporaryDirectory() as td:
        sess = Session(cfg, "sess-lock", "test", os.path.join(td, "log.jsonl"), websocket=None)

        max_in_flight = {"n": 0, "cur": 0}

        async def fake_send(payload):
            if payload.get("type") == "execute_action":
                max_in_flight["cur"] += 1
                max_in_flight["n"] = max(max_in_flight["n"], max_in_flight["cur"])
                action = payload["action"]

                async def resolve():
                    await asyncio.sleep(0.01)  # simulate Unity latency
                    max_in_flight["cur"] -= 1
                    # Mimic Unity+receive-loop: resolve the CURRENT pending slot.
                    await sess._handle_action_result({
                        "success": True, "action_id": action["action_id"],
                        "game_state": base_state(1),
                    })
                asyncio.create_task(resolve())
            # ignore other frames

        sess._send = fake_send

        actions = [{"action_id": f"act_{i}", "action_type": "worker", "description": f"a{i}"}
                   for i in range(12)]

        async def one(a):
            result, _ = await sess._execute_action("officer", a)
            return a["action_id"], result.get("action_id")

        pairs = await asyncio.gather(*[one(a) for a in actions])
        crossed = [(sent, got) for sent, got in pairs if sent != got]
        assert not crossed, f"COMMIT LOCK FAILED — crossed results: {crossed}"
        # The lock must serialize: never more than one execute in flight at once.
        assert max_in_flight["n"] == 1, \
            f"commit lock did not serialize; max in-flight={max_in_flight['n']}"
        print(f"[1] COMMIT LOCK ok: 12 concurrent executes, 0 crossed, "
              f"max in-flight={max_in_flight['n']}")


async def run_dispatch_test():
    """Drive _handle_begin_round; assert concurrent, scoped, no regression."""
    cfg = load_config("config/continuous_agents_domain.json")
    with tempfile.TemporaryDirectory() as td:
        sess = Session(cfg, "sess-disp", "test", os.path.join(td, "log.jsonl"), websocket=None)

        executed_by_officer = {}          # name -> list of action_ids executed
        concurrency = {"n": 0, "cur": 0}
        director_turn_state = {"v": None}

        async def fake_send(payload):
            t = payload.get("type")
            if t == "execute_action":
                action = payload["action"]

                async def resolve():
                    await asyncio.sleep(0.005)
                    await sess._handle_action_result({
                        "success": True, "action_id": action["action_id"],
                        "game_state": base_state(2),  # bump version on every commit
                    })
                asyncio.create_task(resolve())
            elif t == "director_turn":
                director_turn_state["v"] = payload.get("game_state", {}).get("_v")

        sess._send = fake_send

        # Scripted officer: step 0 executes its first in-scope action, step 1 finishes.
        step_counters = {}

        def fake_run_tool_step(messages, tools, agent_cfg, tool_mode):
            name = agent_cfg["subagent_name"]
            step = step_counters.get(name, 0)
            step_counters[name] = step + 1
            if step == 0:
                # Mark this officer active across an await boundary to expose overlap.
                concurrency["cur"] += 1
                concurrency["n"] = max(concurrency["n"], concurrency["cur"])
                import time as _t
                _t.sleep(0.02)  # run_tool_step is called via asyncio.to_thread → real overlap
                concurrency["cur"] -= 1
                return {"content": f"{name} acting",
                        "tool_calls": [{"id": f"{name}-0", "name": "execute_game_action",
                                        "arguments": {"index": 0, "note": "act"}}]}
            return {"content": f"{name} done", "tool_calls": []}  # finish (no tool call)

        agent_router.run_tool_step = fake_run_tool_step
        agent_router._enumerate_actions = fake_enumerate

        # Capture executions by tapping _execute_action.
        orig_exec = sess._execute_action

        async def tap_exec(agent_name, action):
            executed_by_officer.setdefault(agent_name, []).append(action["action_id"])
            return await orig_exec(agent_name, action)
        sess._execute_action = tap_exec

        await sess._handle_begin_round({"type": "begin_round", "day": 1, "segment": 0,
                                        "game_state": base_state(1)})

        # (2) concurrency
        assert concurrency["n"] >= 2, \
            f"officers did NOT overlap (max concurrent={concurrency['n']}) — dispatch is serial"
        print(f"[2] CONCURRENT DISPATCH ok: max {concurrency['n']} officers overlapping")

        # (3) scoping — each officer executed only in-scope actions
        for name, ids in executed_by_officer.items():
            scope = EXPECTED_SCOPE.get(name, set())
            bad = [i for i in ids if i not in scope]
            assert not bad, f"{name} executed OUT-OF-SCOPE actions {bad} (scope={scope})"
        print(f"[3] HARD SCOPING ok: {[(n, executed_by_officer.get(n)) for n in EXPECTED_SCOPE]}")

        # every officer with a non-empty scope should have executed its index-0 action
        acted = set(executed_by_officer.keys())
        assert acted == set(EXPECTED_SCOPE.keys()), \
            f"not all officers acted: {acted} vs {set(EXPECTED_SCOPE)}"

        # (4) no state regression — director_turn carried the bumped version (2)
        assert director_turn_state["v"] == 2, \
            f"_latest_game_state regressed: director_turn _v={director_turn_state['v']} (want 2)"
        print(f"[4] NO REGRESSION ok: director_turn carried freshest _v={director_turn_state['v']}")


async def main():
    await run_commit_lock_test()
    await run_dispatch_test()
    print("\nALL CONCURRENCY TESTS PASSED ✓")


if __name__ == "__main__":
    asyncio.run(main())
