"""Lockstep diff: the port driving an evolved plan against the headless run of that same
plan (validate_plan.py output), draw for draw and counter for counter.

    python -m oracle.sim.diag_lockstep oracle/sim/runs/evo14.jsonl 5901 [--trace oracle/sim/runs/validate/staff_5901.json]

Reports the first gym step whose draw stream differs, the first round each rewardMetrics
key differs, the first budget difference, and both final scores. The replay harness cannot
do this: it replays Unity's recorded actions, whereas here the port makes its own choices
and the question is whether Unity, fed those same choices, ends up in the same state."""
import argparse
import json
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from oracle.sim.actions import CoraActions           # noqa: E402
from oracle.sim.evolve import fresh_world, captured_seeds   # noqa: E402
from oracle.sim.floodmap import FloodMap             # noqa: E402
from oracle.sim.validate_plan import best_row        # noqa: E402
import oracle.sim.diag_marks as D                    # noqa: E402
import oracle.sim.sim as S                           # noqa: E402
from oracle.sim import paths as P

_KEYS = ("foodResolved", "foodFulfilled", "lodgingResolved", "lodgingFulfilled", "cumWorkingWorkers",
         "cumIdleWorkers", "cumTrainingWorkers", "totalWorkers", "caseworkRequested", "caseworkProcessed",
         "foodSpend", "lodgingSpend", "workerSpend", "caseworkSpend")


def main():
    import reward_scoring
    ap = argparse.ArgumentParser()
    ap.add_argument("log"); ap.add_argument("unity_seed", type=int)
    ap.add_argument("--trace", default=None)
    args = ap.parse_args()
    trace = args.trace or os.path.join(P.VALIDATE, f"staff_{args.unity_seed}.json")
    log = trace.replace(".json", ".log")
    row = best_row(args.log, args.unity_seed)
    t = json.load(open(trace))
    um = D.unity_marks(log) if os.path.exists(log) else {}
    s0 = D.seed_step(log) if os.path.exists(log) else None
    # The Xorshift state Unity logged at its first flood:enter -- from the validation log
    # itself when present, else the state the evolution row was evolved on.
    from oracle.sim.test_replay_forward import seed_state
    st = seed_state(log) if os.path.exists(log) else None
    if st is None:
        st = tuple(int(x) & 0xFFFFFFFF for x in row["seed_state"])
    w = fresh_world(st, FloodMap.load())
    m = CoraActions(random.Random(0))
    first, firstd, bd, port_marks = {}, None, None, []
    for i, step in enumerate(t):
        gene = row["plan"][i] if i < len(row["plan"]) else {"choices": {}, "menu": []}
        drive_step(w, m, step, gene)
        marks = []; S.step(w, marks=marks)
        um_, sm = step["after"]["rewardMetrics"], w.economy.metrics()
        for k in _KEYS:
            if um_.get(k) != sm.get(k) and k not in first:
                first[k] = (i, um_.get(k), sm.get(k))
        ub = step["after"]["satisfactionAndBudget"]["budget"]
        if ub != w.economy.budget and bd is None:
            bd = (i, ub, w.economy.budget)
        port_marks.extend((i + 1, x) for x in marks)
    # Compare the CONCATENATED streams. Unity labels choice-time draws (immediate
    # deliveries register their clients in the choice frame) with the step just finished,
    # the port emits them at the head of the next step; per-step lists disagree on labels
    # while the stream itself is identical.
    unity = [(s, x) for s in sorted(um) if s0 is not None and s > s0 for x in um[s]]
    k = next((j for j, (a, b) in enumerate(zip(unity, port_marks)) if a[1] != b[1]), min(len(unity), len(port_marks)))
    if k < max(len(unity), len(port_marks)):
        firstd = (k, unity[k] if k < len(unity) else "<end>", port_marks[k] if k < len(port_marks) else "<end>")
    print(f"draws: unity {len(unity)} port {len(port_marks)}; first divergence (index, unity(step, mark), port(step, mark)): {firstd}")
    print(f"metrics: first divergence per key (round, unity, port): {first or 'none'}")
    print(f"budget: first difference {bd}")
    print(f"score: unity {reward_scoring.compute_score(um_)[2]:.4f}  port {reward_scoring.compute_score(sm)[2]:.4f}")
    return 0


def drive_step(w, m, step, gene):
    """Apply one trace step's actions to the port, before step_round.

    Drives the port with what Unity was ACTUALLY sent that step (the trace's `taken`),
    not the gene: the surrogate that produced the trace may have offered a task the
    current one does not, or vice versa, and the fallback choice differs. Choices are
    replayed per task type in order, exactly as validate_plan mapped them onto Unity;
    menu actions by executed id; staffing by the model's own auto-staff, as recorded."""
    queues, menu = {}, []
    titles = {t.get("taskId"): t.get("taskTitle") for t in (step.get("before") or {}).get("allActiveTasks") or []}
    for a in step.get("taken") or []:
        if a.get("error"):
            continue
        if a.get("kind") == "choice":
            key = a.get("stableTaskId") or S.CODE_BUILT_TASKS.get(str(titles.get(a.get("taskId"))), "")
            queues.setdefault(key, []).append(a.get("choiceId"))
        elif a.get("kind") == "menu" and a.get("action_id"):
            menu.append(a["action_id"])
    offered = {}
    for tid, cid in S.open_choices(w):
        offered.setdefault(tid, []).append(cid)
    for tid, cids in offered.items():
        if tid in w.tasks.repair_for:
            key = "Repair"
        else:
            entry = w.generated_specs.get(tid)
            key = entry[0] if entry else ""
        q = queues.get(key)
        want = q.pop(0) if q else gene["choices"].get(key)
        if want is None:
            # DECLINING IS A MOVE. Falling back to cids[0] answered every task the officer
            # left alone, which is not what the capture did -- 5504's road blockage was
            # offered, declined, and expired for +20 satisfaction; forcing choice 0 bought
            # an emergency delivery Unity never bought.
            continue
        S.answer(w, tid, want if want in cids else cids[0])
    m.apply(w, ("turn", {"choices": {}, "menu": tuple(menu)}))


def replay_step(w, step):
    """Apply one tool-call capture step (oracle/sim/capture.py) to the port, before S.step.

    Pure replay: the task answers Unity accepted, matched to the port's open tasks by task
    type in offer order, then the game actions Unity accepted, in the order they were sent.
    Nothing else -- a task the policy left alone stays unanswered (declining is a move), and
    nothing is staffed that the policy did not staff."""
    # Answers match the port's open tasks by (task type, facility): two open requests of one
    # type for different communities are different tasks.
    from oracle.sim.parity import _with_facility
    taken = _with_facility(step) if step.get("before") else (step.get("taken") or [])
    import re as _re
    by_site = {(b["type"], b.get("site_id")): b["name"] for b in w.economy.buildings}

    def _port_name(fac):
        """Unity names a built building's GameObject <Type>_<site>; the port by display name."""
        m = _re.fullmatch(r"(\w+?)_(\d+)", fac or "")
        return by_site.get((m.group(1), int(m.group(2))), fac) if m else (fac or "")
    queues = {}
    for a in taken:
        if a.get("kind") == "choice":
            where = f"group:{a['group']}" if a.get("group") is not None else _port_name(a.get("facility"))
            queues.setdefault((a.get("stableTaskId") or "", where), []).append(a.get("choiceId"))
    offered = {}
    for tid, cid in S.open_choices(w):
        offered.setdefault(tid, []).append(cid)
    for tid, cids in offered.items():
        spec = w.generated_specs.get(tid) or ("", "", {})
        key = "Repair" if tid in w.tasks.repair_for else spec[0]
        gid = (spec[2] or {}).get("_gid")
        q = queues.get((key, f"group:{gid}" if gid is not None else str(spec[1] or "")))
        if not q and not spec[1]:   # global tasks: Unity reports a facility type, the port none
            q = next((v for (k, _f), v in queues.items() if k == key and v), None)
        if q:
            want = q.pop(0)
            S.answer(w, tid, want if want in cids else cids[0])
    for a in taken:
        if a.get("kind") not in ("menu", "staff") or not a.get("ok"):
            continue
        S.apply_menu_action(w, a.get("payload") or {})


if __name__ == "__main__":
    sys.exit(main())
