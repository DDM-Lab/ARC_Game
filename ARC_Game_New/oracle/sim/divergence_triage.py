"""Why does a plan score differently on the surrogate and on Unity? The first point of divergence.

For each captured plan (oracle.sim.capture --calls: plan_<seed>.json + replay_<seed>/staff_<seed>.json)
whose Unity score differs from the surrogate's, replays it on SimEnv decision by decision and
stops at the first decision where either
    call     a call's outcome differs (executed on one side, refused on the other), or
    state    every call agrees but the state at the next decision differs -- reported as the first
             differing family: score parts, budget, a facility's population / food / status,
             workers, vehicles, the open tasks.
The bucket (kind, tool or family, Unity's reason / the field) says where to look in sim.py.

    python -m oracle.sim.divergence_triage RUN_DIR [RUN_DIR ...] [--all] [--json out.json]
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import re
import sys
from collections import Counter, defaultdict


def _norm(reason: str) -> str:
    """A reason with its numbers and names blanked, so like refusals bucket together."""
    r = re.sub(r"\d+", "N", reason or "")
    r = re.sub(r"'[^']*'", "'X'", r)
    return r[:90]


def _sig(gs: dict) -> dict:
    """The state, by family, for comparing two games."""
    from cora.scoring import score_components
    parts = score_components(gs.get("rewardMetrics"))
    fac = {}
    for f in (gs.get("mapState") or {}).get("facilities") or []:
        res = f.get("resources") or {}
        fac[f.get("facilityName")] = (f.get("buildingStatus"), res.get("population"), res.get("foodPacks"),
                                      f.get("assignedWorkforce"))
    wf = gs.get("workforceState") or {}
    return {"score": {k: round(v, 4) for k, v in parts.items() if isinstance(v, float)},
            "budget": (gs.get("satisfactionAndBudget") or {}).get("budget"),
            "facilities": fac,
            "workers": {k: wf.get(k) for k in sorted(wf) if isinstance(wf.get(k), (int, float)) and "Today" not in k},
            "counters": {k: wf.get(k) for k in sorted(wf) if "Today" in k},
            "vehicles": sorted(str(v.get("status")) for v in (gs.get("mapState") or {}).get("vehicles") or []),
            "tasks": sorted(t.get("taskId") for t in gs.get("allActiveTasks") or [])}


# Dynamics, in the order a divergence is reported. The daily counters (newWorkersHiredToday...)
# are export-only -- they lag in the port and feed no score -- and are tallied apart.
FAMILIES = ("tasks", "facilities", "workers", "vehicles", "budget", "score")


def _first_diff(a: dict, b: dict, families=FAMILIES):
    for fam in families:
        x, y = a[fam], b[fam]
        if x == y:
            continue
        if isinstance(x, dict):
            keys = sorted(set(x) | set(y), key=str)
            k = next(k for k in keys if x.get(k) != y.get(k))
            return fam, str(k), x.get(k), y.get(k)
        return fam, "", x, y
    return None


COUNTERS = Counter()          # export-only daily counters: decisions where they disagree


def triage_plan(run, seed):
    from cora import executor
    from oracle.rollout import DECISIONS
    from oracle.sim.env import SimEnv
    plan = json.load(open(os.path.join(run, f"plan_{seed}.json")))
    trace = json.load(open(os.path.join(run, f"replay_{seed}", f"staff_{seed}.json")))
    steps = [st for st in trace if st.get("decision") is not None]
    env = SimEnv(seed=seed, max_episode_steps=DECISIONS + 4, manual_transfers=False)
    env.reset()
    for i, st in enumerate(steps):
        d = st["decision"]
        if d >= len(plan):
            return None
        if i == 0:
            # Vehicles excepted: some captures export the fleet before MapConfigApplier's last
            # vehicle registers (3 listed, "Spawned ... 4 vehicles" in the log, Vehicle 4 driving
            # by s13) -- an export artifact, not a different game.
            before = _first_diff(_sig(st["before"]), _sig(env.game_state),
                                 tuple(f for f in FAMILIES if f != "vehicles"))
            if before:                           # the games differ before any call: setup
                return {"decision": d, "kind": "state", "where": "start", "family": before[0], "key": before[1],
                        "unity": before[2], "sim": before[3]}
        results, (_, _, term, trunc, _) = executor.execute_turn(env, [(t, a) for t, a in plan[d]])
        ucalls = st.get("calls") or []
        for j, (uc, r) in enumerate(zip(ucalls, results)):
            us = uc.get("status")
            if (us == "executed") != (r.status == "executed"):
                return {"decision": d, "kind": "call", "tool": uc.get("tool"), "args": uc.get("args"),
                        "unity": [us, uc.get("reason", "")], "sim": [r.status, r.reason or ""]}
        if i + 1 >= len(steps):               # the capture's last state is mid-round: stop at the calls
            return None
        su, ss = _sig(steps[i + 1]["before"]), _sig(env.game_state)
        for k in set(su["counters"]) | set(ss["counters"]):
            if su["counters"].get(k) != ss["counters"].get(k):
                COUNTERS[k] += 1
        after = _first_diff(su, ss)
        if after:
            return {"decision": d, "kind": "state", "where": "after", "family": after[0], "key": after[1],
                    "unity": after[2], "sim": after[3]}
        if term or trunc:
            return None
    return None


def bucket(t):
    if t is None:
        return ("none",)
    if t["kind"] == "call":
        side = "sim executed, Unity refused" if t["sim"][0] == "executed" else "Unity executed, sim refused"
        return ("call", t["tool"], side, _norm(t["unity"][1] if t["sim"][0] == "executed" else t["sim"][1]))
    key = re.sub(r"\d+", "N", t["key"]) if t["family"] == "facilities" else t["key"]
    return ("state", t["where"], t["family"], key)


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("runs", nargs="+")
    ap.add_argument("--all", action="store_true", help="also triage plans whose scores agree")
    ap.add_argument("--json")
    a = ap.parse_args(argv)
    buckets, items = Counter(), defaultdict(list)
    n = 0
    for run in a.runs:
        for p in sorted(glob.glob(os.path.join(run, "result_*.json"))):
            r = json.load(open(p))
            if r.get("unity_score") is None:
                continue
            gap = abs(r["unity_score"] - r["surrogate_score"])
            if gap <= 1e-3 and not a.all:
                continue
            seed = r["seed"]
            if not os.path.exists(os.path.join(run, f"replay_{seed}", f"staff_{seed}.json")):
                continue
            try:
                t = triage_plan(run, seed)
            except Exception as e:                       # one broken capture must not stop the sweep
                t = {"kind": "error", "error": f"{type(e).__name__}: {e}"}
            n += 1
            b = bucket(t) if t is None or t.get("kind") != "error" else ("error", t["error"][:80])
            buckets[b] += 1
            items[b].append({"run": os.path.basename(run.rstrip("/")), "seed": seed, "gap": round(gap, 4), **(t or {})})
    print(f"{n} plans triaged; export-only counter mismatches (decisions): {dict(COUNTERS)}")
    for b, c in buckets.most_common():
        gaps = [x["gap"] for x in items[b]]
        print(f"{c:5}  mean gap {sum(gaps) / len(gaps):.4f}  {' | '.join(map(str, b))}")
        ex = items[b][0]
        print(f"         e.g. {ex['run']} {ex['seed']} d{ex.get('decision')}: "
              f"{json.dumps({k: ex.get(k) for k in ('tool', 'args', 'unity', 'sim', 'family', 'key') if k in ex}, default=str)[:300]}")
    if a.json:
        json.dump({" | ".join(map(str, b)): v for b, v in items.items()}, open(a.json, "w"), indent=1, default=str)


if __name__ == "__main__":
    main(sys.argv[1:])
