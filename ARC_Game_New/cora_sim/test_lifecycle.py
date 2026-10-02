"""Building lifecycle equivalence -- the region no other capture ever visited.

Every earlier episode either never built or built and never staffed, so
UnderConstruction -> NeedWorker -> InUse and everything gated on IsOperational() was
transcribed from C# and checked only by unit tests. This replays a capture that reaches all
three states and requires the port to reproduce the timeline round by round.

TWO THINGS THIS PINNED THAT THE SOURCE DID NOT SAY CLEARLY:

  * A worker_assignment's quantity is a HEAD COUNT, not workforce points. Requesting 4
    workers for a building needing 4 workforce produced assignedWorkforce = 8.
  * The system takes TRAINED workers first (free trained went 5 -> 1, untrained untouched),
    so asking for `requiredWorkforce` workers over-staffs 2x AND burns the scarce pool.
"""
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.economy import (STATUS_IN_USE, STATUS_NEED_WORKER,          # noqa: E402
                              STATUS_UNDER_CONSTRUCTION, Economy,
                              action_from_id, apply_action, from_game_state,
                              step_round)

_DEFAULT = ("/private/tmp/claude-501/-Users-cpulling-Work-CORA/"
            "b762a1aa-9f0c-4053-9897-bfd6aeeb9623/scratchpad/staff_*.json")


def unity_status(state, btype):
    for f in ((state.get("mapState") or {}).get("facilities") or []):
        if f.get("buildingType") == btype:
            return f.get("buildingStatus"), (f.get("assignedWorkforce") or 0)
    return None, 0


def main(pattern=None):
    paths = sorted(glob.glob(pattern or os.environ.get("STAFF_TRACES", _DEFAULT)))
    if not paths:
        print("  no build-and-staff capture found -- this suite needs one, and no other "
              "capture in the corpus ever staffs a building")
        print("\nRESULT: SKIPPED")
        return 0

    print("cora_sim building lifecycle vs Unity")
    ok = True
    for path in paths:
        trace = json.load(open(path))
        econ = from_game_state(trace[0]["before"])
        econ.buildings = Economy.default_prebuilts()
        mismatches, checked, seen = [], 0, set()

        for step in trace:
            for act in step["taken"]:
                if act.get("error") or act.get("ok") is False:
                    continue
                if act.get("kind") == "menu":
                    apply_action(econ, act.get("payload")
                                 or action_from_id(act.get("action_id"), act.get("cost") or 0))
                elif act.get("kind") == "staff":
                    a = (act.get("payload") or {}).get("assignment") or {}
                    idx = next((i for i, b in enumerate(econ.buildings)
                                if b["status"] in (STATUS_NEED_WORKER, STATUS_IN_USE)
                                and b["status"] != "Prebuilt"), -1)
                    econ.staff(idx, count=int(a.get("quantity") or 0))
            b_day = step["before"]["sessionInfo"]["currentDay"]
            a_day = step["after"]["sessionInfo"]["currentDay"]
            step_round(econ, a_day != b_day, a_day)

            want_status, want_wf = unity_status(step["after"], "Kitchen")
            if not want_status:
                continue
            built = [b for b in econ.buildings if b["type"] == "Kitchen"]
            got_status = built[0]["status"] if built else STATUS_UNDER_CONSTRUCTION
            got_wf = built[0]["assigned"] if built else 0
            seen.add(want_status)
            checked += 1
            if got_status != want_status or got_wf != want_wf:
                mismatches.append((step["round"], want_status, want_wf, got_status, got_wf))

        name = os.path.basename(path)
        if mismatches:
            ok = False
            r, ws, ww, gs_, gw = mismatches[0]
            print(f"  {name}: {len(mismatches)}/{checked} rounds differ; first at round {r}: "
                  f"unity={ws}({ww}wf) port={gs_}({gw}wf)")
        else:
            print(f"  {name}: status and workforce match on all {checked} rounds "
                  f"(states reached: {', '.join(sorted(seen))})")
    print("\nRESULT:", "ALL PASS" if ok else "FAILURES PRESENT")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1] if len(sys.argv) > 1 else None))
