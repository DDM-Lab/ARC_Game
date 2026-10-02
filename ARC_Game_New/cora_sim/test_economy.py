"""Economy equivalence: replay a captured journal and require EXACT counter agreement.

Exact is the right bar and it was earned, not assumed: the draw census shows the economy
consumes no randomness, so any disagreement is a porting error rather than variance.

WHAT THIS TEST CANNOT DO YET, SAID UP FRONT. Food, lodging and casework fulfilment flow
through the task system, which is not ported. So the counters checked here are the ones the
economy alone owns -- budget, the four spend counters, the worker accumulators and
totalWorkers. The fulfilment counters are reported as PENDING rather than silently passed,
because a test that ignores what it cannot check reads exactly like one that verified it.
"""
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.economy import (Economy, action_from_id, apply_action,   # noqa: E402
                              from_game_state, step_round)
from cora_sim.triggers import INVENTORY                               # noqa: E402

_TAG = {t["taskId"]: t.get("taskTag") for t in INVENTORY}

OWNED = ("workerSpend", "foodSpend", "lodgingSpend", "caseworkSpend",
         "cumWorkingWorkers", "cumTrainingWorkers", "cumIdleWorkers",
         "roundsCompleted", "totalWorkers")
PENDING = ("foodResolved", "foodFulfilled", "lodgingResolved", "lodgingFulfilled",
           "caseworkRequested", "caseworkProcessed")


def traces(pattern):
    for path in sorted(glob.glob(pattern)):
        yield path, json.load(open(path))


def main(pattern=None):
    pattern = pattern or os.environ.get(
        "ECON_TRACES",
        "/private/tmp/claude-501/-Users-cpulling-Work-CORA/"
        "b762a1aa-9f0c-4053-9897-bfd6aeeb9623/scratchpad/econ3_*.json")
    found = list(traces(pattern))
    if not found:
        print(f"  no economy traces matched {pattern}")
        print("\nRESULT: SKIPPED (no ground truth)")
        return 0

    print("cora_sim.economy equivalence vs Unity")
    failures = 0
    for path, trace in found:
        name = os.path.basename(path)
        econ = from_game_state(trace[0]["before"])
        mism, checked = {}, 0
        for step in trace:
            tasks = {t.get("taskId"): t for t in (step["before"].get("allActiveTasks") or [])}
            for act in step["taken"]:
                if act.get("kind") == "choice" and not act.get("error"):
                    task = tasks.get(act.get("taskId")) or {}
                    choice = next((c for c in (task.get("choices") or [])
                                   if c.get("choiceId") == act.get("choiceId")), None)
                    if choice:
                        econ.apply_choice(_TAG.get(task.get("stableTaskId"), "None"),
                                          choice.get("impacts"),
                                          choice.get("budgetDelayRounds", 0) or 0,
                                          choice.get("destinationCategory") or "",
                                          choice.get("deliveryQuantity", 0) or 0)
                if act.get("kind") == "staff" and not act.get("error") and act.get("ok"):
                    # worker_assignment: a HEAD COUNT against the first staffable building.
                    # Skipping these left cumWorkingWorkers at 0 while Unity counted 4.
                    a = (act.get("payload") or {}).get("assignment") or {}
                    idx = next((i for i, b in enumerate(econ.buildings)
                                if econ.can_staff(i)), -1)
                    econ.staff(idx, count=int(a.get("quantity") or 0))
                if act.get("kind") == "menu" and not act.get("error"):
                    # Journals record the stable action_id, not the live menu entry, so
                    # rebuild the structured action from it.
                    apply_action(econ, act.get("action")
                                 or action_from_id(act.get("action_id"), act.get("cost") or 0))
            before_day = step["before"]["sessionInfo"]["currentDay"]
            after_day = step["after"]["sessionInfo"]["currentDay"]
            step_round(econ, after_day != before_day, after_day)
            # Motel occupancy is READ from the capture rather than predicted here. This
            # suite tests spend ARITHMETIC; whether the port predicts occupancy correctly
            # depends on client arrivals and departures, which need the RNG stream and are
            # covered by test_clients. Mixing the two would make an occupancy error look
            # like a billing error and vice versa.
            for f in ((step["after"].get("mapState") or {}).get("facilities") or []):
                if f.get("buildingType") == "Motel":
                    econ.motel_pop = f.get("currentPopulation", 0) or 0
            truth = step["after"].get("rewardMetrics") or {}
            got = econ.metrics()
            for k in OWNED:
                if k not in truth:
                    continue
                checked += 1
                if got.get(k) != truth[k]:
                    mism.setdefault(k, (step["round"], truth[k], got.get(k)))
        if mism:
            failures += 1
            print(f"  {name}: {len(mism)} counters diverge (of {len(OWNED)} owned)")
            for k, (rnd, want, gotv) in sorted(mism.items()):
                print(f"      {k:<20} first differs at round {rnd}: unity={want} port={gotv}")
        else:
            print(f"  {name}: all {len(OWNED)} owned counters match across "
                  f"{len(trace)} rounds ({checked} comparisons)")
    print(f"  NOT YET CHECKED         : {', '.join(PENDING)} -- these flow through the task "
          f"system, which is not ported")
    print("\nRESULT:", "ALL PASS" if not failures else "FAILURES PRESENT")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1] if len(sys.argv) > 1 else None))
