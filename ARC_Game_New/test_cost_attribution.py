#!/usr/bin/env python3
"""Per-turn cost attribution: queued_cost / attempted_cost + the committed-spend line.

Why this exists: `budget_delta` is structurally ~always 0 for a continuous officer. The
officer acts inside a FROZEN paused phase where actions are queued rather than resolved, so
`budget_after` reads the same snapshot as `budget_before`. Verified live on 2026-08-13: an
officer built a $1,000 kitchen and was then shown a budget of 5000 seventeen consecutive
times. That made per-turn spend unrecoverable from the corpus and let the officer reason
about affordability against a number that never moved.

Two fixes, both asserted here:
  1. `queued_cost` / `attempted_cost` on the agent_turn record (cost at COMMIT time).
  2. A "Committed spend this phase: $N" line in the ledger the officer reads.

Hermetic: no Unity, no LLM, no network.

Run: ./.venv/bin/python test_cost_attribution.py
"""
from __future__ import annotations

import sys

FAILS: list[str] = []



SCRATCH = "/private/tmp/claude-501/-Users-cpulling-Work-CORA/b762a1aa-9f0c-4053-9897-bfd6aeeb9623/scratchpad"


def emit(results, budget_before=5000, budget_after=5000):
    """Run one real log_turn against a temp file and return the parsed record.

    Uses the real writer rather than a stub so the test exercises the same code path
    production does — including the record dict actually being JSON-serializable.
    """
    import json, os, episode_logger
    os.makedirs(SCRATCH, exist_ok=True)
    path = os.path.join(SCRATCH, "cost_turn.jsonl")
    if os.path.exists(path):
        os.remove(path)
    lg = episode_logger.EpisodeLogger.__new__(episode_logger.EpisodeLogger)
    lg.log_path = path
    episode_logger.EpisodeLogger.log_turn(
        lg, episode_id="e", round_num=1, day=1, segment=0,
        agent_name="Food", role="subagent", actor_type="continuous",
        subobservation={}, subactions_available=len(results), proposed_packages=[],
        selected_package_index=None, execution_results=results,
        satisfaction_before=50, satisfaction_after=50,
        budget_before=budget_before, budget_after=budget_after,
        game_state_after={}, llm_raw_response="", conv_history_length=0,
        tokens_used=0, session_id="s")
    with open(path) as f:
        return json.loads(f.read().strip())


def check(label: str, cond: bool, detail: str = "") -> None:
    print(f"  {'PASS' if cond else 'FAIL'}  {label}" + (f"   -- {detail}" if not cond else ""))
    if not cond:
        FAILS.append(label)


def test_turn_record_costs() -> None:
    """queued_cost counts only what succeeded; attempted_cost counts everything tried."""
    print("\n[1] agent_turn carries per-turn cost")
    results = [
        {"action_id": "build_Kitchen_0", "action_type": "construction",
         "description": "Build Kitchen", "cost": 1000, "success": True},
        {"action_id": "hire_0", "action_type": "worker",
         "description": "Hire 5 untrained", "cost": 500, "success": True},
        {"action_id": "build_Shelter_9", "action_type": "construction",
         "description": "Build Shelter", "cost": 1000, "success": False,
         "error": "insufficient funds"},
    ]
    captured = emit(results)

    check("budget_delta is 0 (the frozen-phase artifact this replaces)",
          captured.get("budget_delta") == 0, f"got {captured.get('budget_delta')}")
    check("queued_cost = 1500 (succeeded only)", captured.get("queued_cost") == 1500,
          f"got {captured.get('queued_cost')}")
    check("attempted_cost = 2500 (includes the failure)",
          captured.get("attempted_cost") == 2500, f"got {captured.get('attempted_cost')}")


def test_missing_and_bad_costs() -> None:
    """A record with no/garbage cost must not crash or poison the sum."""
    print("\n[2] absent or malformed cost degrades to 0")
    captured = emit([
        {"action_id": "a", "success": True},                     # no cost key
        {"action_id": "b", "success": True, "cost": None},
        {"action_id": "c", "success": True, "cost": "not-a-number"},
        {"action_id": "d", "success": True, "cost": 250},
    ])
    check("bad costs ignored, good one counted", captured.get("queued_cost") == 250,
          f"got {captured.get('queued_cost')}")


def test_proposals_not_counted() -> None:
    """A propose_choices summary row is not an executed action and carries no spend."""
    print("\n[3] proposal rows excluded from cost")
    captured = emit([{"kind": "propose_choices", "cost": 9999, "success": True},
                     {"action_id": "x", "cost": 100, "success": True}])
    check("proposal cost excluded", captured.get("queued_cost") == 100,
          f"got {captured.get('queued_cost')}")


def test_ledger_shows_spend() -> None:
    """The officer's ledger states the committed spend as a number."""
    print("\n[4] committed-spend line reaches the officer")
    import agent_router

    s = agent_router.Session.__new__(agent_router.Session)
    s._committed_this_phase = []
    s._committed_spend_this_phase = 0.0

    check("no ledger before any commit", s._committed_ledger_text() == "")

    agent_router.Session._record_committed(
        s, {"action_type": "construction", "description": "Build Kitchen", "cost": 1000})
    agent_router.Session._record_committed(
        s, {"action_type": "worker", "description": "Hire 5 untrained", "cost": 500})
    txt = agent_router.Session._committed_ledger_text(s)

    check("spend tallied", s._committed_spend_this_phase == 1500,
          f"got {s._committed_spend_this_phase}")
    check("ledger states the figure", "Committed spend this phase: $1,500" in txt,
          f"ledger=...{txt[:160]}")
    check("ledger still lists the actions", "Build Kitchen" in txt and "Hire 5" in txt)

    # Dedup must not double-count.
    agent_router.Session._record_committed(
        s, {"action_type": "construction", "description": "Build Kitchen", "cost": 1000})
    check("re-committing the same action does not double-count",
          s._committed_spend_this_phase == 1500, f"got {s._committed_spend_this_phase}")

    # A zero-cost phase should not print a misleading "$0" line.
    s2 = agent_router.Session.__new__(agent_router.Session)
    s2._committed_this_phase = []
    s2._committed_spend_this_phase = 0.0
    agent_router.Session._record_committed(
        s2, {"action_type": "task_choice", "description": "answer task", "cost": 0})
    check("no spend line when nothing cost anything",
          "Committed spend" not in agent_router.Session._committed_ledger_text(s2))


def main() -> int:
    print("=" * 72)
    print("per-turn cost attribution")
    print("=" * 72)
    test_turn_record_costs()
    test_missing_and_bad_costs()
    test_proposals_not_counted()
    test_ledger_shows_spend()
    print("\n" + "=" * 72)
    if FAILS:
        print(f"FAILED ({len(FAILS)}): " + "; ".join(FAILS))
        return 1
    print("all passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
