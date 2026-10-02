"""Per-facility population, port against Unity, round by round.

Casework and motel occupancy key off people ACTUALLY moved; fulfilled keys off nominal
completion. caseworkRequested flips sides between traces (5502: unity 100 / port 0; 5601:
unity 0 / port 100), which can only mean the two sides are physically delivering DIFFERENT
orders. This prints where the people are, so that claim is checked by a script rather than
by reading two tables side by side -- which is how I got two comparisons wrong in a row.

    ./.venv/bin/python -m cora_sim.diag_population <trace.json>
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
        print("usage: python -m cora_sim.diag_population <staff_capture.json>")
        return 2
    trace = json.load(open(path))
    w = S.World(rng=UnityRandom(state=seed_state(path.replace(".json", ".log"))),
                weather="Sunny", fmap=FloodMap.load())
    w.use_generation = True
    w.economy = from_game_state(trace[0]["before"])
    w.economy.buildings = type(w.economy).default_prebuilts()
    w.tasks.has_supplier = lambda tag: True

    print(f"{'rd':>3}  {'facility':<24}{'unity':>7}{'port':>7}   delta")
    worst = 0
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
        seen = set()
        for tid, cid in S.open_choices(w):
            if tid in seen:
                continue
            seen.add(tid)
            S.answer(w, tid, cid)
        S.step_round(w)

        unity = {f["facilityName"]: (f.get("resources") or {}).get("population")
                 for f in ((step["after"].get("mapState") or {}).get("facilities") or [])}
        port = {b["name"]: (b.get("resources") or {}).get("population")
                for b in w.economy.buildings}
        for name in sorted(unity):
            u, p = unity[name], port.get(name)
            if u is None or p is None or u == p:
                continue
            worst = max(worst, abs(u - p))
            print(f"{step['round']:>3}  {name:<24}{u:>7}{p:>7}   {p - u:+d}")
    print(f"\nlargest population disagreement: {worst}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
