"""Per-order ledger, Unity against the port, so a missing delivery attaches to a ROW.

Round 6 is one missing delivery: Unity moves Community01 -> Motel twice (rounds 5 and 6,
100 real people each) and the port moves it once. This prints every order both sides
created and what became of it, rather than leaving me to compare two tables by eye -- which
I got wrong twice in a row before adopting the rule that every claim comes from a script.

    ./.venv/bin/python -m cora_sim.diag_orders <trace.json>
"""
import collections
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import cora_sim.roads as roads
import cora_sim.sim as S
import cora_sim.tasks as T
from cora_sim.floodmap import FloodMap
from cora_sim.rng import UnityRandom
from cora_sim.test_replay_forward import (action_from_id, apply_action, from_game_state,
                                          seed_state)

_CELL_NAME = {v: k for k, v in roads.BUILDING_CELL.items()}
_DISPLAY = {v: k for k, v in roads.FACILITY_CELL.items()}


def _unity(log):
    """Every order Unity queued, dispatched and unloaded, keyed by round."""
    rows = collections.defaultdict(list)
    for line in open(log, errors="ignore"):
        m = re.search(r"\[RNG(?:MARK|CTX)\] s(\d+)d\d+r\d+f\d+ delivery:(queue|unload) "
                      r"\{.*?\} (\{.*\})", line)
        if not m:
            continue
        d = json.loads(m.group(3))
        rnd = int(m.group(1)) - 1
        if m.group(2) == "queue":
            rows[rnd].append(("queued ", d.get("cargo", "")[:4], d.get("src", "")[:12],
                              d.get("dst", "")[:12], d.get("qty"), None))
        else:
            rows[rnd].append(("UNLOAD ", d.get("cargo", "")[:4], d.get("src", "")[:12],
                              d.get("dst", "")[:12], d.get("nominal"), d.get("actual")))
    return rows


def _port(path, trace):
    rows = collections.defaultdict(list)
    w = S.World(rng=UnityRandom(state=seed_state(path.replace(".json", ".log"))),
                weather="Sunny", fmap=FloodMap.load())
    w.use_generation = True
    w.economy = from_game_state(trace[0]["before"])
    w.economy.buildings = type(w.economy).default_prebuilts()
    w.tasks.has_supplier = lambda tag: True

    def name(cell):
        return _CELL_NAME.get(cell) or _DISPLAY.get(cell) or str(cell)

    orig_run = roads.Fleet.run_round

    def run(self, pending, flooded=frozenset(), load=None):
        for _seq, payload, src, dst, qty in pending:
            rows[w.round_index].append(("queued ", "", name(src)[:12], name(dst)[:12], qty, None))
        landed, rest, dropped = orig_run(self, pending, flooded, load)
        for p in landed:
            zombie = len(p) > 3 and p[3] == "zombie"
            rows[w.round_index].append(("UNLOAD ", "", "", str(p[2])[:12], p[1],
                                        0 if zombie else p[1]))
        for p in dropped:
            rows[w.round_index].append(("dropped", "", "", str(p[2])[:12], p[1], None))
        return landed, rest, dropped

    roads.Fleet.run_round = run
    try:
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
    finally:
        roads.Fleet.run_round = orig_run
    return rows


def main(path=None):
    path = path or (sys.argv[1] if len(sys.argv) > 1 else None)
    if not path or not os.path.exists(path):
        print("usage: python -m cora_sim.diag_orders <staff_capture.json>")
        return 2
    trace = json.load(open(path))
    u = _unity(path.replace(".json", ".log"))
    p = _port(path, trace)
    lo, hi = 4, 8
    for rnd in range(lo, hi):
        ur, pr = u.get(rnd, []), p.get(rnd, [])
        if not ur and not pr:
            continue
        print(f"\n--- round {rnd} ---")
        for i in range(max(len(ur), len(pr))):
            a = ("%-7s %-4s %-12s->%-12s nom=%-4s act=%-4s" % ur[i]) if i < len(ur) else ""
            b = ("%-7s %-4s %-12s->%-12s nom=%-4s act=%-4s" % pr[i]) if i < len(pr) else ""
            print(f"  UNITY {a:<58}")
            print(f"  PORT  {b:<58}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
