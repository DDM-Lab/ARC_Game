"""Choice parity: what Unity's harness answered against what the port answers.

Written after every round-5 fix had been attributed to delivery scheduling. It is not
delivery scheduling. The port and Unity are not answering the same board:

    UNITY, round 5, 9 tasks        PORT, round 5, 6 tasks
      Daily Budget Allocation        Lodging   <- answered choice 2 (-3000, immediate)
      Food Request From Community    Food
      Food Request From Community    Food
      Food Request From Community    Food
      Population Relocation          Lodging   <- answered choice 1 (free)
      Population Relocation          None
      Day 2 Start of Day Report
      Training Recommendation Alert
      Workforce Optimization Alert

Unity answers its two relocations with choice 1, which is FREE (no impacts). The port
answers one of its Lodging tasks with choice 2, which is -3000 and immediate. So the port
spends money on a choice Unity never took, and it is short three of Unity's four non-demand
tasks (budget allocation, day report, two advisories).

Every counter at round 5 is downstream of that. No fleet model, travel time or load rule
can reconcile boards that differ before a single delivery is created, which is why each fix
in that area traded one counter for another.

    ./.venv/bin/python -m cora_sim.diag_parity [trace.json]
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import cora_sim.sim as S
from cora_sim.floodmap import FloodMap
from cora_sim.rng import UnityRandom
from cora_sim.test_replay_forward import (action_from_id, apply_action, from_game_state,
                                          seed_state)


def main(path=None):
    path = path or (sys.argv[1] if len(sys.argv) > 1 else None)
    if not path or not os.path.exists(path):
        print("usage: python -m cora_sim.diag_parity <staff_capture.json>")
        return 2
    trace = json.load(open(path))
    st = seed_state(path.replace(".json", ".log"))
    w = S.World(rng=UnityRandom(state=st), weather="Sunny", fmap=FloodMap.load())
    w.use_generation = True
    w.economy = from_game_state(trace[0]["before"])
    w.economy.buildings = type(w.economy).default_prebuilts()
    w.tasks.has_supplier = lambda tag: True

    titles = {}
    for s in trace:
        for t in (s["before"].get("allActiveTasks") or []):
            titles[t["taskId"]] = t.get("taskTitle")

    for step in trace:
        for act in step["taken"]:
            if act.get("error") or act.get("ok") is False:
                continue
            if act.get("kind") == "menu":
                apply_action(w.economy, act.get("payload")
                             or action_from_id(act.get("action_id"), act.get("cost") or 0))
            elif act.get("kind") == "staff":
                a = (act.get("payload") or {}).get("assignment") or {}
                idx = next((i for i, b in enumerate(w.economy.buildings)
                            if w.economy.can_staff(i)), -1)
                w.economy.staff(idx, count=int(a.get("quantity") or 0))

        board = [str(x.get("taskTitle")) for x in (step["before"].get("allActiveTasks") or [])]
        unity = [(a.get("taskId"), a.get("choiceId"), str(titles.get(a.get("taskId")))[:32])
                 for a in step["taken"] if a.get("kind") == "choice"]
        seen, port = set(), []
        for tid, cid in S.open_choices(w):
            if tid in seen:
                continue
            seen.add(tid)
            tag = w.tasks.active[tid].tag if tid in w.tasks.active else "?"
            port.append((tid, cid, tag))
            S.answer(w, tid, cid)
        S.step_round(w)

        if unity or port:
            print(f"\nround {step['round']}  unity board={len(board)}  port board={len(port)}")
            print(f"  unity answered: {unity}")
            print(f"  port  answered: {port}")
            if len(board) != len(seen):
                print(f"  BOARD SIZE DIFFERS: unity {len(board)} vs port {len(seen)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
