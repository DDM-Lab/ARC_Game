"""Observation diff: the port's end-of-round STATE against Unity's, field by field, per step.

    python -m cora_sim.obs_diff cora_sim/runs/merge_v4 5503            # one line per step
    python -m cora_sim.obs_diff cora_sim/runs/merge_v4 5503 --step 9   # every differing field
    python -m cora_sim.obs_diff cora_sim/runs/merge_v4 5503 --all      # full dump of each diff step

WHY THIS AND NOT THE DRAW-STREAM DIFF. The surrogate is a leaf evaluator for search. What
search needs is that the same state and the same actions give the same observation at the
end of the round -- budget, satisfaction, every facility's population/food/staff/status, the
worker pools, the task board, what is walking where, the reward counters. Bit-exact RNG
lockstep is a stronger property than that and a far more brittle one: one extra draw anywhere
shifts every later random, so only the first divergence carries information and every
upstream reorder of a subscriber invalidates it. This tool compares the observation itself.

The RNG column is still shown, because it tells you how to READ a diff: while the two streams
are aligned, every state difference is a deterministic mechanic bug and should be fixed;
once they diverge, differences in probability-driven state (which task fired, which community
lost food, casework rolls, the flood) are expected and only the deterministic fields --
budget deltas from actions, staffing, construction, walks, deliveries -- still mean anything.

Facilities are matched by (building type, site id) for built buildings and by display name
for prebuilts; Unity's internal names (Community01, Shelter_0) are mapped to the port's.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cora_sim.sim as S                                    # noqa: E402
from cora_sim.debug_lockstep import Session                 # noqa: E402

# Unity GameObject name -> display name, for the prebuilts (tasks name the GameObject, the
# facility list names the display). Positions from a capture: Community01 (1.3, 7.1) is
# Charleston, Community03 (9.3, 2.8) Trinity, Community02 (-9.6, -0.9) Amherst.
INTERNAL_TO_DISPLAY = {"Community01": "Community Charleston", "Community03": "Community Trinity",
                       "Community02": "Community Amherst", "Motel": "Motel"}

_COUNTERS = ("foodResolved", "foodFulfilled", "lodgingResolved", "lodgingFulfilled",
             "cumWorkingWorkers", "cumIdleWorkers", "cumTrainingWorkers", "totalWorkers",
             "caseworkRequested", "caseworkProcessed", "foodSpend", "lodgingSpend",
             "workerSpend", "caseworkSpend")

# Fields whose value depends on a random draw somewhere upstream. After the RNG streams part,
# a difference here is expected; before, it is a bug like any other.
STOCHASTIC = {"board", "walks", "fac.pop", "fac.food", "counters.caseworkRequested",
              "counters.caseworkProcessed", "counters.lodgingFulfilled", "counters.foodResolved",
              "counters.foodFulfilled", "counters.lodgingResolved"}


def _fac_key(btype, site, display):
    return display if btype in ("Community", "Motel") else f"{btype}@{site}"


def project_unity(after: dict) -> dict:
    """Unity's get_game_state -> the canonical observation."""
    out = {"budget": (after.get("satisfactionAndBudget") or {}).get("budget"),
           "satisfaction": (after.get("satisfactionAndBudget") or {}).get("satisfaction"),
           "fac": {}, "workers": {}, "board": [], "walks": [], "counters": {}}
    for f in (after.get("mapState") or {}).get("facilities") or []:
        res = f.get("resources") or {}
        key = _fac_key(f.get("buildingType"), f.get("originalSiteId"), f.get("facilityName"))
        out["fac"][key] = {"status": f.get("buildingStatus") or "Prebuilt",
                           "pop": res.get("population", f.get("currentPopulation")),
                           "food": res.get("foodPacks"),
                           "workforce": f.get("assignedWorkforce"),
                           "operational": bool(f.get("isOperational"))}
    ws = after.get("workforceState") or {}
    out["workers"] = {"free_trained": ws.get("freeTrainedWorkers"), "free_untrained": ws.get("freeUntrainedWorkers"),
                      "working_trained": ws.get("workingTrainedWorkers"), "working_untrained": ws.get("workingUntrainedWorkers"),
                      "arriving_trained": ws.get("trainedWorkersNotArrived"), "arriving_untrained": ws.get("untrainedWorkersNotArrived"),
                      "training": ws.get("untrainedWorkersInTraining")}
    for t in after.get("allActiveTasks") or []:
        title = str(t.get("taskTitle"))
        key = t.get("stableTaskId") or S.CODE_BUILT_TASKS.get(title, "") or ("Daily_Report" if "Start of Day Report" in title else title)
        fac = str(t.get("affectedFacility"))
        # Global tasks report their targetFacilityType ("Shelter") or "Daily Report" as the
        # facility; the port stores none. Only facility-scoped tasks compare on it.
        fac = "" if fac in ("Shelter", "Daily Report", "") else INTERNAL_TO_DISPLAY.get(fac, fac)
        out["board"].append((key, _norm_fac(fac), t.get("roundsRemaining")))
    out["board"].sort()
    for r in (after.get("logistics") or {}).get("pendingRelocations") or []:
        src, dst = str(r.get("source")), str(r.get("destination"))
        out["walks"].append((_norm_fac(INTERNAL_TO_DISPLAY.get(src, src)),
                             _norm_fac(INTERNAL_TO_DISPLAY.get(dst, dst)),
                             r.get("quantity"), r.get("roundsRemaining")))
    out["walks"].sort()
    rm = after.get("rewardMetrics") or {}
    out["counters"] = {k: rm.get(k) for k in _COUNTERS}
    return out


def _norm_fac(name: str) -> str:
    """Shelter_0 -> Shelter@0 (Unity names a built building by its site id)."""
    for prefix, btype in (("Shelter_", "Shelter"), ("Kitchen_", "Kitchen"), ("CaseworkSite_", "CaseworkSite")):
        if name.startswith(prefix) and name[len(prefix):].isdigit():
            return f"{btype}@{name[len(prefix):]}"
    return name


def project_port(w) -> dict:
    """The World -> the same canonical observation."""
    e = w.economy
    out = {"budget": e.budget, "satisfaction": int(round(e.satisfaction)),
           "fac": {}, "workers": {}, "board": [], "walks": [], "counters": {}}
    names = {}
    for b in e.buildings:
        res = b.get("resources") or {}
        key = _fac_key(b["type"], b.get("site_id"), b.get("name"))
        names[b.get("name")] = key
        out["fac"][key] = {"status": b["status"], "pop": res.get("population"), "food": res.get("foodPacks"),
                           "workforce": b.get("assigned", 0),
                           "operational": b["status"] in ("Prebuilt", "InUse")}
    out["workers"] = {"free_trained": e.free_trained, "free_untrained": e.free_untrained,
                      "working_trained": e.working_trained, "working_untrained": e.working_untrained,
                      "arriving_trained": sum(1 for d, k in e.arriving if k == "trained"),
                      "arriving_untrained": sum(1 for d, k in e.arriving if k == "untrained"),
                      "training": len(e.in_training)}
    for tid, t in list(w.tasks.active.items()) + list(w.tasks.awaiting.items()):
        if t.resolved:
            continue                      # off Unity's activeTasks; the port just never swept it
        entry = w.generated_specs.get(tid) or ("Repair" if tid in w.tasks.repair_for else "?", None, {})
        fac = entry[1]
        out["board"].append((entry[0], names.get(fac, fac) if fac else "", t.rounds_remaining))
    out["board"].sort()
    for rounds, src, dst, qty, _tid in w.walks:
        out["walks"].append((names.get(src, src), names.get(dst, dst), qty, rounds))
    out["walks"].sort()
    out["counters"] = {k: e.metrics().get(k) for k in _COUNTERS}
    return out


def diff(u: dict, p: dict) -> list:
    """[(field, unity, port)] for every differing field; field names double as categories."""
    out = []
    for k in ("budget", "satisfaction"):
        if u[k] != p[k]:
            out.append((k, u[k], p[k]))
    for key in sorted(set(u["fac"]) | set(p["fac"])):
        a, b = u["fac"].get(key), p["fac"].get(key)
        if a is None or b is None:
            out.append((f"fac.exists[{key}]", a is not None, b is not None))
            continue
        for f in ("status", "pop", "food", "workforce", "operational"):
            if a.get(f) != b.get(f):
                out.append((f"fac.{f}[{key}]", a.get(f), b.get(f)))
    for k in u["workers"]:
        if u["workers"][k] != p["workers"].get(k):
            out.append((f"workers.{k}", u["workers"][k], p["workers"].get(k)))
    if u["board"] != p["board"]:
        only_u = sorted(set(u["board"]) - set(p["board"]))
        only_p = sorted(set(p["board"]) - set(u["board"]))
        out.append(("board", only_u, only_p))
    if u["walks"] != p["walks"]:
        out.append(("walks", u["walks"], p["walks"]))
    for k in _COUNTERS:
        if u["counters"].get(k) != p["counters"].get(k):
            out.append((f"counters.{k}", u["counters"].get(k), p["counters"].get(k)))
    return out


def category(field: str) -> str:
    base = field.split("[")[0]
    return "stochastic" if base in STOCHASTIC else "deterministic"


def rng_divergence_step(sess: Session):
    """The port step in which the concatenated draw streams first part, or None."""
    if sess.draw_div is None or sess.draw_div[0] is None:
        return None
    k = sess.draw_div[0]
    seen = 0
    for i, ms in enumerate(sess.marks):
        seen += len(ms)
        if k < seen:
            return i
    return len(sess.marks) - 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("capture_dir")
    ap.add_argument("seed", type=int)
    ap.add_argument("--step", type=int, default=None, help="dump every differing field for this step")
    ap.add_argument("--all", action="store_true", help="dump every differing field for every diff step")
    args = ap.parse_args()

    sess = Session(args.seed, validate_dir=args.capture_dir)
    div = rng_divergence_step(sess)
    meta_path = os.path.join(args.capture_dir, f"staff_{args.seed}.meta.json")
    if os.path.exists(meta_path):
        meta = json.load(open(meta_path))
        try:
            from cora_sim.economy import C as _C
            here = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "corpus", "sim_constants.json"))).get("buildGUID")
        except Exception:
            here = None
        if meta.get("buildGUID") and here and meta["buildGUID"] != here:
            print(f"WARNING: capture is from build {meta['buildGUID'][:8]}, corpus from {here[:8]} -- different games")

    print(f"seed {args.seed}: {len(sess.trace)} steps; RNG streams "
          + ("aligned for the whole capture" if div is None else f"part inside step {div} (draw {sess.draw_div[0]})"))
    first_det = None
    hist = {"deterministic": 0, "stochastic": 0}
    for i, step in enumerate(sess.trace):
        u, p = project_unity(step["after"]), project_port(sess.after[i])
        d = diff(u, p)
        w = sess.after[i]
        tag = f"s{i + (sess.s0 or 1)}"
        rng = "ok " if div is None or i < div else "DIV"
        cats = [category(f) for f, _a, _b in d]
        n_det, n_sto = cats.count("deterministic"), cats.count("stochastic")
        if n_det and first_det is None:
            first_det = i
        hist["deterministic"] += n_det
        hist["stochastic"] += n_sto
        fields = " ".join(sorted({f.split("[")[0] for f, _a, _b in d}))
        print(f"  step {i:02d} {tag:>4} d{w.day}s{w.segment} rng={rng} budget U={u['budget']:>7} P={p['budget']:>7}"
              f"  diffs det={n_det} sto={n_sto}  {fields}")
        if d and (args.all or args.step == i):
            for f, a, b in d:
                print(f"        {category(f)[:3]} {f:<44} U={a!r:<30} P={b!r}")
    print(f"\nfirst deterministic diff: step {first_det}; RNG divergence: step {div}; "
          f"totals {hist}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
