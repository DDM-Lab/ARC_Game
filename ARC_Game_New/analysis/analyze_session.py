#!/usr/bin/env python3
"""Separate model-performance vs game-misunderstanding vs can't-execute from a
session log, using the `outcome`/delta fields the router now stamps on every
continuous-agent action.

Usage:
    ./.venv/bin/python analyze_session.py logs/sessions/<user>/<session>.jsonl

Outcome taxonomy (engine truth, stamped in agent_router._outcome_fields):
    invalid   never reached the engine (bad index / out-of-scope / no such task)
    rejected  reached the engine, engine refused (success=False)
    ok        engine accepted (success=True)

Derived buckets (what the user actually wants to see):
    CANT_EXECUTE      outcome in {invalid, rejected}
    MISUNDERSTANDING  outcome==ok but the action was inert
                        - task_choice: task_closed is False (deferred/unfulfillable)
                        - other:       state_changed is False (nothing moved)
    EFFECTIVE         outcome==ok and it actually moved state; deltas show
                      whether the model's judgment helped or hurt
Actions with no `outcome` field are pre-instrumentation rows (counted as legacy).
"""
import json
import sys
from collections import defaultdict


def load(path):
    with open(path) as f:
        for line in f:
            line = line.strip()
            if line:
                yield json.loads(line)


def bucket(p):
    """Map a logged action payload to a derived bucket."""
    outcome = p.get("outcome")
    if outcome is None:
        return "legacy"
    if outcome in ("invalid", "rejected"):
        return "cant_execute"
    # outcome == ok
    if p.get("task_closed") is False:
        return "misunderstanding"
    if p.get("state_changed") is False:
        return "misunderstanding"
    return "effective"


def main(path):
    # per-actor: bucket -> count, plus delta sums and reason tallies
    per_actor = defaultdict(lambda: {
        "buckets": defaultdict(int),
        "reasons": defaultdict(int),      # error strings for cant_execute
        "budget_delta": 0.0,
        "sat_delta": 0.0,
        "n_effective": 0,
    })
    total = defaultdict(int)

    for row in load(path):
        if row.get("event_type") != "action":
            continue
        p = row.get("payload") or {}
        if "outcome" not in p and "success" not in p:
            continue  # not a continuous game action
        actor = (row.get("actor") or {}).get("name", "?")
        b = bucket(p)
        A = per_actor[actor]
        A["buckets"][b] += 1
        total[b] += 1
        if b == "cant_execute":
            A["reasons"][p.get("error") or "?"] += 1
        if b == "effective":
            A["n_effective"] += 1
            A["budget_delta"] += p.get("budget_delta") or 0
            A["sat_delta"] += p.get("satisfaction_delta") or 0

    order = ["effective", "misunderstanding", "cant_execute", "legacy"]
    label = {
        "effective": "EFFECTIVE (moved state)",
        "misunderstanding": "MISUNDERSTANDING (inert success)",
        "cant_execute": "CANT_EXECUTE (invalid/rejected)",
        "legacy": "legacy (no outcome field)",
    }

    print(f"=== {path} ===\n")
    grand = sum(total.values())
    print("OVERALL")
    for b in order:
        n = total.get(b, 0)
        if n:
            print(f"  {label[b]:38s} {n:4d}  ({100*n/grand:4.1f}%)")
    print(f"  {'TOTAL actions':38s} {grand:4d}\n")

    for actor in sorted(per_actor):
        A = per_actor[actor]
        n = sum(A["buckets"].values())
        print(f"── {actor}  ({n} actions)")
        for b in order:
            c = A["buckets"].get(b, 0)
            if c:
                print(f"     {label[b]:36s} {c:3d}")
        if A["reasons"]:
            reasons = ", ".join(f"{k}×{v}" for k, v in sorted(
                A["reasons"].items(), key=lambda kv: -kv[1]))
            print(f"     can't-execute reasons: {reasons}")
        if A["n_effective"]:
            print(f"     net Δbudget {A['budget_delta']:+.0f}  "
                  f"net Δsatisfaction {A['sat_delta']:+.1f}  "
                  f"(over {A['n_effective']} effective)")
        print()

    print("Read: high MISUNDERSTANDING → the model thinks it acted but the game "
          "didn't change (rules gap). High CANT_EXECUTE → it references actions "
          "outside its space or the engine refuses (action-space / feasibility "
          "gap). EFFECTIVE with negative net deltas → it executes fine but its "
          "judgment hurts. That three-way split is model-vs-mechanics.")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(1)
    main(sys.argv[1])
