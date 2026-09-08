"""Task board side by side per step: the port's live tasks against Unity's allActiveTasks
from the validation trace, driving the port exactly as diag_lockstep does.

    python -m cora_sim.diag_board runs/evo14.jsonl 5503 --from 12 --to 13
"""
import argparse, json, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cora_sim.sim as S                                    # noqa: E402
from cora_sim.actions import CoraActions                    # noqa: E402
from cora_sim.evolve import fresh_world                     # noqa: E402
from cora_sim.floodmap import FloodMap                      # noqa: E402
from cora_sim.diag_lockstep import best_row, drive_step                 # noqa: E402
from cora_sim.test_replay_forward import seed_state         # noqa: E402


def board(w):
    out = []
    for tid, t in w.tasks.active.items():
        e = w.generated_specs.get(tid) or ("Repair", "", {})
        out.append(f"A{tid} {e[0]}@{e[1]} r={t.rounds_remaining}")
    for tid, t in w.tasks.awaiting.items():
        e = w.generated_specs.get(tid) or ("Repair", "", {})
        out.append(f"W{tid} {e[0]}@{e[1]} r={t.rounds_remaining}{' resolved' if t.resolved else ''}")
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("log"); ap.add_argument("unity_seed", type=int)
    ap.add_argument("--from", dest="lo", type=int, default=0)
    ap.add_argument("--to", dest="hi", type=int, default=32)
    a = ap.parse_args()
    trace = f"runs/validate/staff_{a.unity_seed}.json"; ulog = trace.replace(".json", ".log")
    row = best_row(a.log, a.unity_seed)
    t = json.load(open(trace))
    w = fresh_world(seed_state(ulog), FloodMap.load())
    m = CoraActions(random.Random(0))
    for i, step in enumerate(t):
        gene = row["plan"][i] if i < len(row["plan"]) else {"choices": {}, "menu": []}
        if a.lo <= i <= a.hi:
            print(f"--- step {i} BEFORE answering (port day {w.day} seg {w.segment})")
            print("   port:", board(w))
            print("   unity:", [f"{x.get('taskId')} {x.get('stableTaskId') or x.get('taskTitle')}@{x.get('facilityName') or x.get('affectedFacility')} "
                                f"{x.get('status')} r={x.get('roundsRemaining')}" for x in (step["before"].get("allActiveTasks") or [])])
            print("   unity taken:", [(x.get("kind"), x.get("stableTaskId") or x.get("action_id"), x.get("choiceId")) for x in step["taken"]])
        drive_step(w, m, step, gene); S.step_round(w)
    return 0


if __name__ == "__main__":
    sys.exit(main())
