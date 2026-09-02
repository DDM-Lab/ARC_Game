"""ProbabilityTrigger equivalence: draw placement, and the reversed range on a second mechanic.

TWO INDEPENDENT CLAIMS ARE CHECKED HERE.

1. DRAW PLACEMENT. Each fixture row is one task-generation pass and records the RNG state
   before every ProbabilityTrigger roll in it. Replaying the pass must reproduce those
   states exactly -- which pins the walk order across tasks AND across facilities, not just
   the total count. This is the check that would catch a port that short-circuits the
   trigger evaluation, or that rolls per task instead of per facility.

2. THE REVERSAL, ON A MECHANIC THAT IS NOT WEATHER. These tasks use requireAllTriggers, so
   every logged "Task triggered for X" line proves that task's probability roll PASSED.
   That is one-directional -- a task that did not fire tells us nothing, since another
   trigger may have vetoed it -- but it is enough, because the forward form of
   Random.Range(0f,1f) predicts the OPPOSITE on every one of those rows. Weather pinned the
   reversal on a wide range; this pins it on the [0,1) range that every trigger in the game
   uses.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.rng import UnityRandom, f32                    # noqa: E402
from cora_sim.triggers import INVENTORY, roll_pass, threshold  # noqa: E402

_FIXTURE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "corpus", "flood_rounds.json")
_BY_TITLE = {t["taskTitle"]: t for t in INVENTORY if t.get("taskTitle")}


def main():
    rows = json.load(open(_FIXTURE)).get("triggers", [])
    if not rows:
        print("no trigger rows in fixture -- rebuild it with cora_sim/build_corpus.py")
        return 1
    if not _BY_TITLE:
        print("sim_constants has no taskTitle -- re-export the trigger inventory")
        return 1

    bad_placement, checked_draws, passes = [], 0, 0
    firings = reversed_ok = forward_ok = 0

    for r in rows:
        draws, order = r["draws"], r["facilityOrder"]
        if not draws:
            continue
        passes += 1

        def facilities_for(task, _order=order):
            return _order.get(task.get("taskTitle"), [])

        st = draws[0]
        rng = UnityRandom(state=(st["s0"], st["s1"], st["s2"], st["s3"]))
        seen, results = [], []
        # Snapshot the state before each draw by rolling one trigger at a time.
        for task_id, facility, outcomes in roll_pass(
                _Recording(rng, seen), facilities_for):
            results.append((task_id, facility, outcomes))

        if len(seen) != len(draws):
            bad_placement.append(f"{r['source']}: pass rolled {len(seen)} draws, "
                                 f"Unity recorded {len(draws)}")
            continue
        for i, (got, want) in enumerate(zip(seen, draws)):
            checked_draws += 1
            if got != (want["s0"], want["s1"], want["s2"], want["s3"]):
                bad_placement.append(f"{r['source']}: draw {i} lands at the wrong stream "
                                     f"position (task/facility walk order differs)")
                break

        fired = {(f, t) for f, t in r["fired"]}
        for task_id, facility, outcomes in results:
            if (facility, task_id) not in fired:
                continue                  # non-firing proves nothing: another trigger may veto
            firings += 1
            reversed_ok += all(outcomes)
        # the forward form, on exactly those rows
        rng2 = UnityRandom(state=(st["s0"], st["s1"], st["s2"], st["s3"]))
        fwd = {}
        for task in INVENTORY:
            probs = task.get("probabilities") or []
            if not probs:
                continue
            targets = [None] if task.get("isGlobalTask") else facilities_for(task)
            for facility in targets:
                fwd[(facility, task["taskId"])] = all(
                    f32((rng2.next_uint() & 0x7FFFFF) / 8388607.0) < p for p in probs)
        for key in fired:
            if key in fwd:
                forward_ok += fwd[key]

    print("cora_sim.triggers equivalence vs Unity")
    if bad_placement:
        print(f"  draw placement          : {len(bad_placement)} FAILURES")
        for b in bad_placement[:5]:
            print("    " + b)
    else:
        print(f"  draw placement          : {checked_draws} draws across {passes} passes "
              f"land on Unity's exact stream positions")
    print(f"  reversed Range(0f,1f)   : {reversed_ok}/{firings} logged firings roll pass")
    print(f"  forward  (rejected)     : {forward_ok}/{firings}")

    ok = not bad_placement and firings and reversed_ok == firings
    if ok and forward_ok == firings:
        print("  forward form ALSO matches - this fixture no longer discriminates")
        ok = False
    print("\nRESULT:", "ALL PASS" if ok else "FAILURES PRESENT")
    return 0 if ok else 1


class _Recording:
    """Wraps the stream to capture the state before each draw, so the test can check WHERE
    each roll happens rather than only how many there are."""

    def __init__(self, rng, sink):
        self._rng, self._sink = rng, sink

    def range01_lt(self, t):
        self._sink.append(self._rng.get_state())
        return self._rng.range01_lt(t)


if __name__ == "__main__":
    raise SystemExit(main())
