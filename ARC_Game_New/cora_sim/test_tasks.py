"""Fulfilment-counter equivalence: does the port's arithmetic reproduce Unity's?

SCOPE, STATED PRECISELY. This drives resolution from the CAPTURE -- a task that leaves the
active list resolved, and the trace says when -- and asks only whether the port's
counter arithmetic then matches. It tests RecordTaskResolution's formulas and the
food/lodging unit asymmetry. It does NOT test expiry timing or delivery latency, which need
the full round loop; those are named as unchecked below rather than implied.

That split is deliberate. Two different things can be wrong -- WHEN a task resolves, and
WHAT it credits when it does -- and a test that mixes them tells you neither.
"""
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.tasks import Task, TaskBoard, demand_of, is_immediate  # noqa: E402
from cora_sim.triggers import INVENTORY                    # noqa: E402

_TAG = {t["taskId"]: t.get("taskTag") for t in INVENTORY}
_DEFAULT_PATTERN = ("/private/tmp/claude-501/-Users-cpulling-Work-CORA/"
                    "b762a1aa-9f0c-4053-9897-bfd6aeeb9623/scratchpad/econ3_*.json")


def tag_of(task_state):
    return _TAG.get(task_state.get("stableTaskId"), "None")


def main(pattern=None):
    paths = sorted(glob.glob(pattern or os.environ.get("ECON_TRACES", _DEFAULT_PATTERN)))
    if not paths:
        print("  no traces found")
        print("\nRESULT: SKIPPED")
        return 0

    print("cora_sim.tasks fulfilment counters vs Unity")
    worst = {}
    for path in paths:
        trace = json.load(open(path))
        counters = {k: 0 for k in ("foodResolved", "foodFulfilled",
                                   "lodgingResolved", "lodgingFulfilled")}
        # Lodging always has a source (people are already somewhere); food needs an
        # operational kitchen, and the captures never have one.
        def has_supplier(tag, _t=trace):
            if tag == "Lodging":
                return True
            return any(f.get("buildingType") == "Kitchen" and f.get("isOperational")
                       for st in _t
                       for f in ((st["before"].get("mapState") or {}).get("facilities") or []))
        board, seen = TaskBoard(has_supplier=has_supplier), {}
        for step in trace:
            before = {t["taskId"]: t for t in (step["before"].get("allActiveTasks") or [])}
            after = {t["taskId"] for t in (step["after"].get("allActiveTasks") or [])}
            for tid, ts in before.items():
                if tid not in seen:
                    tag = tag_of(ts)
                    seen[tid] = board.add(Task(tid, tag, demand_of(ts, tag),
                                               ts.get("roundsRemaining", 1) or 1))
            for act in step["taken"]:
                if act.get("kind") != "choice" or act.get("error"):
                    continue
                ts = before.get(act.get("taskId"))
                if not ts:
                    continue
                ch = next((c for c in (ts.get("choices") or [])
                           if c.get("choiceId") == act.get("choiceId")), None)
                if ch:
                    board.answer(act["taskId"], ch.get("deliveryQuantity") or 0,
                                 immediate=is_immediate(ch),
                                 destination=ch.get("destinationCategory") or "",
                                 counters=counters)
            board.tick_deliveries_only(counters)
            # Answering already removed a task from the board and set its resolution in
            # motion. Anything still on the board that Unity dropped expired unfulfilled.
            for tid in list(before):
                if tid not in after and tid in board.active:
                    board.resolve(board.active.pop(tid), False, counters)
        truth = trace[-1]["after"]["rewardMetrics"]
        name = os.path.basename(path)
        deltas = {k: counters[k] - truth.get(k, 0) for k in counters}
        line = "  ".join(f"{k}={counters[k]}/{truth.get(k,0)}" for k in counters)
        print(f"  {name}")
        print(f"      port/unity: {line}")
        for k, d in deltas.items():
            if d:
                worst[k] = max(worst.get(k, 0), abs(d))
    exact = [k for k in ("foodResolved", "foodFulfilled", "lodgingResolved",
                         "lodgingFulfilled") if k not in worst]
    print(f"  exact counters          : {', '.join(exact) if exact else 'none'}")
    if worst:
        print(f"  still diverging         : "
              + ", ".join(f"{k} (max |delta| {v})" for k, v in sorted(worst.items())))
    print("  NOT CHECKED HERE        : expiry timing and delivery latency -- resolution "
          "events come from the capture, so only the arithmetic is under test")
    print("\nRESULT:", "ALL PASS" if not worst else "PARTIAL")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1] if len(sys.argv) > 1 else None))
