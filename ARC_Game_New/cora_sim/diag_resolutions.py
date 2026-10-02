"""Which task resolutions the port credits, round by round, against Unity's counters.

Uses diag_marks.replay_steps so the action handling (INCLUDING the staff branch) can never
drift from test_replay_forward -- two earlier hand-copied diagnostics dropped it and produced
numbers that contradicted the harness.
"""
import glob
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.diag_marks import replay_steps                     # noqa: E402
from cora_sim.tasks import TaskBoard                             # noqa: E402
from cora_sim.test_replay_forward import _DEFAULT                # noqa: E402

TRACK = ("lodgingResolved", "lodgingFulfilled")


def main():
    only = sys.argv[1] if len(sys.argv) > 1 else None
    paths = sorted(glob.glob(os.environ.get("STAFF_TRACES", _DEFAULT)))
    if only:
        paths = [p for p in paths if only in p]
    log = []
    orig_resolve, orig_late = TaskBoard.resolve, TaskBoard.late_delivery

    def resolve(self, task, fulfilled, counters):
        if task.tag == "Lodging" and not task.resolved:
            log.append(f"resolve id={task.task_id} demand={task.demand} "
                       f"delivered={task.delivered} fulfilled={fulfilled}")
        return orig_resolve(self, task, fulfilled, counters)

    def late(self, task, quantity, counters):
        if task.tag == "Lodging":
            log.append(f"LATE id={task.task_id} qty={quantity}")
        return orig_late(self, task, quantity, counters)

    TaskBoard.resolve, TaskBoard.late_delivery = resolve, late
    try:
        for path in paths:
            logp = path.replace(".json", ".log")
            if not os.path.exists(logp):
                continue
            print(os.path.basename(path))
            for step, w in replay_steps(path, logp):
                got, want = w.economy.metrics(), step["after"].get("rewardMetrics") or {}
                bad = [k for k in TRACK if k in want and got.get(k) != want[k]]
                if log or bad:
                    flag = "  <-- " + ", ".join(
                        f"{k} unity={want[k]} port={got.get(k)}" for k in bad) if bad else ""
                    print(f"  r{step['round']}{flag}")
                    for line in log:
                        print(f"      {line}")
                del log[:]
                if bad:
                    break
    finally:
        TaskBoard.resolve, TaskBoard.late_delivery = orig_resolve, orig_late
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
