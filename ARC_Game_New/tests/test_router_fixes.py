"""Hermetic tests for the router/logger fixes (#5 reward via gym scorer, #6
propose_choices not counted as a failed action, #7b per-turn retry cap).

No Unity, no LLM, no network. Run:
  env -u ALL_PROXY -u HTTP_PROXY -u HTTPS_PROXY -u http_proxy -u https_proxy \
      ./.venv/bin/python test_router_fixes.py
"""
import asyncio
import json
import os
import tempfile

import agent_router
from agent_router import Session
from agent_config import load_config
from episode_logger import EpisodeLogger, compute_score_components


def _base_state(v=0):
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


def _last_record(path):
    with open(path) as f:
        return json.loads([ln for ln in f if ln.strip()][-1])


def test_reward_via_gym_scorer():
    """#5.3 — with rewardMetrics present, reward == gym scorer's `score`."""
    assert compute_score_components is not None, "gym scorer should import"
    with tempfile.TemporaryDirectory() as td:
        path = os.path.join(td, "log.jsonl")
        logger = EpisodeLogger(path)
        rm = {"foodFulfilled": 8, "foodResolved": 10,
              "lodgingFulfilled": 6, "lodgingResolved": 10,
              "daysCompleted": 1, "totalWorkers": 4, "cumWorkingWorkers": 2}
        gs_after = {"sessionInfo": {}, "rewardMetrics": rm}
        kw = dict(
            episode_id="ep", round_num=1, day=1, segment=0, agent_name="A", role="r",
            actor_type="llm_agent", subobservation={}, subactions_available=0,
            proposed_packages=[], selected_package_index=None, execution_results=[],
            satisfaction_before=10.0, satisfaction_after=20.0,
            budget_before=100.0, budget_after=200.0,
            llm_raw_response="x", conv_history_length=0, tokens_used=0,
        )
        logger.log_turn(game_state_after=gs_after, **kw)
        rec = _last_record(path)
        expected = compute_score_components(rm)["score"]
        assert rec["reward_components"] is not None, "components should be logged"
        assert abs(rec["reward"] - expected) < 1e-9, (rec["reward"], expected)
        # It must NOT be the legacy ad-hoc formula (0.7*10 + 0.0003*100 = 7.03).
        assert abs(rec["reward"] - 7.03) > 1e-6, "should not use legacy formula"

        # Fallback: no rewardMetrics -> legacy formula, components None.
        logger.log_turn(game_state_after={"sessionInfo": {}}, **kw)
        rec2 = _last_record(path)
        assert rec2["reward_components"] is None
        assert abs(rec2["reward"] - 7.03) < 1e-6, rec2["reward"]
    print("[A] REWARD-VIA-GYM-SCORER ok (score routed; legacy fallback intact)")


def test_propose_not_counted_failed():
    """#6.2 — a propose_choices summary row (no `success`) is excluded from the
    action tally; real per-action rows are counted."""
    with tempfile.TemporaryDirectory() as td:
        path = os.path.join(td, "log.jsonl")
        logger = EpisodeLogger(path)
        kw = dict(
            episode_id="ep", round_num=1, day=1, segment=0, agent_name="A", role="r",
            actor_type="llm_agent", subobservation={}, subactions_available=0,
            proposed_packages=[], selected_package_index=None,
            satisfaction_before=0.0, satisfaction_after=0.0,
            budget_before=0.0, budget_after=0.0,
            llm_raw_response="x", conv_history_length=0, tokens_used=0,
        )
        # Old opaque summary alone → 0 attempted (previously counted 1 attempted/0 ok = 1 failed).
        logger.log_turn(
            execution_results=[{"kind": "propose_choices", "executed": 1, "superseded": False}],
            **kw)
        rec = _last_record(path)
        assert rec["total_actions_attempted"] == 0, rec
        assert rec["failed_actions"] == 0, rec

        # Real rows counted; a propose summary mixed in is ignored.
        logger.log_turn(
            execution_results=[
                {"kind": "propose_choices", "executed": 1, "superseded": False},
                {"action_id": "a", "success": True, "error": ""},
                {"action_id": "b", "success": False, "error": "site not available"},
            ],
            **kw)
        rec2 = _last_record(path)
        assert rec2["total_actions_attempted"] == 2, rec2
        assert rec2["successful_actions"] == 1, rec2
        assert rec2["failed_actions"] == 1, rec2
    print("[B] PROPOSE-NOT-COUNTED-FAILED ok (summary excluded; real rows tallied)")


async def _run_retry_cap():
    """#7b — an identical failing tool call is dispatched only ONCE per turn."""
    cfg = load_config("config/continuous_agents_domain.json")
    with tempfile.TemporaryDirectory() as td:
        sess = Session(cfg, "sess-retry", "test", os.path.join(td, "log.jsonl"), websocket=None)
        agent = cfg.agents[0]

        dispatched = []

        async def fake_dispatch(a, tc, gs, all_actions, filtered_actions, brief_only=False):
            dispatched.append((tc["name"],
                               json.dumps(tc.get("arguments") or {}, sort_keys=True)))
            meta = {"executed": 0, "finish": False,
                    "results": [{"action_id": "x", "success": False, "error": "nope"}]}
            return "failed", gs, all_actions, filtered_actions, meta

        sess._dispatch_continuous_tool = fake_dispatch  # instance override

        steps = {"n": 0}

        def fake_step(messages, tools, agent_cfg, tool_mode):
            n = steps["n"]
            steps["n"] += 1
            if n < 2:
                # Same failing call twice — the second must be blocked by the cap.
                return {"content": "try",
                        "tool_calls": [{"id": f"c{n}", "name": "execute_commands",
                                        "arguments": {"commands": "<build>Kitchen,1</build>"}}]}
            return {"content": "done", "tool_calls": []}  # finish

        agent_router.run_tool_step = fake_step

        fs = sess._filter_state(_base_state(1), agent)
        fake_actions = [{"action_id": "build_Kitchen_1", "action_type": "construction",
                         "cost": 1000, "description": "Build Kitchen at Site1",
                         "construction": {"building_type": "Kitchen", "site_id": 1,
                                          "site_name": "Site1"}}]
        await sess._run_continuous_inner(
            agent, fs, fake_actions, _base_state(1), [], triggered_by_director=True)

        assert len(dispatched) == 1, \
            f"identical failing call re-dispatched: {dispatched}"
    print("[C] PER-TURN-RETRY-CAP ok (identical hard-fail dispatched once)")


async def main():
    test_reward_via_gym_scorer()
    test_propose_not_counted_failed()
    await _run_retry_cap()
    print("\nALL ROUTER-FIX TESTS PASSED ✓")


if __name__ == "__main__":
    asyncio.run(main())
