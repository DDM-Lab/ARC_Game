"""Pareto frontier of score vs final budget, over a parameterised policy family.

WHY NOT MCTS HERE
Per-round open-loop UCT has 64 macros x 32 rounds. Even with progressive widening and 200k
iterations the tree stays broad and shallow (measured: root_kids 64, best-plan depth 3), so the
"plans" it compares differ only in their first few rounds and inherit the same tail -- which
collapses the frontier to a couple of near-identical points. The decisions that actually move the
score are not per-round choices at all; they are a handful of standing commitments (how many of
each building, when to start, which delivery rule, whether to answer casework). Searching that
family directly is both cheaper and far better conditioned, and every point is a policy you can
state in one line rather than a 32-step action list.

The frontier maximises BOTH score and final budget. Its shape answers the operational question:
how much score does a solvency requirement actually cost?
"""
from __future__ import annotations

import argparse
import json
import os

from cora import policy_family
from oracle import RESULTS_ROOT
from oracle.plans import evaluate, pareto_front


def _evaluate(cfg, seeds):
    macro = policy_family.macro(cfg)
    return evaluate(lambda rnd, sim: macro(rnd), seeds)


_SEEDS = None


def _init(seeds):
    global _SEEDS
    _SEEDS = seeds


def _one(cfg):
    return _evaluate(cfg, _SEEDS)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", type=int, default=60)
    ap.add_argument("--jobs", type=int, default=1)
    ap.add_argument("--out", default=os.path.join(RESULTS_ROOT, "pareto"))
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    seeds = list(range(a.seeds))
    held = list(range(10_000, 10_000 + a.seeds))

    combos = policy_family.grid_members()
    print(f"evaluating {len(combos)} policies x {a.seeds} seeds on {a.jobs} workers", flush=True)
    global _SEEDS
    _SEEDS = seeds
    if a.jobs > 1:
        import multiprocessing as mp
        with mp.Pool(a.jobs, initializer=_init, initargs=(seeds,)) as pool:
            res = pool.map(_one, combos, chunksize=64)
    else:
        res = [_one(c) for c in combos]
    rows = [(sc, bud, cfg) for (sc, bud), cfg in zip(res, combos)]
    front = pareto_front(rows)
    # re-evaluate the frontier on HELD-OUT seeds so the curve is not a fit to the search seeds
    out = []
    for sc, bud, cfg in front:
        hsc, hbud = _evaluate(cfg, held)
        out.append({"score": hsc, "budget": hbud, "cfg": cfg})
    out.sort(key=lambda d: d["budget"])
    with open(os.path.join(a.out, "pareto.json"), "w") as f:
        json.dump({"n_evaluated": len(rows), "frontier": out,
                   "all_points": [{"score": sc, "budget": bud, "cfg": cfg} for sc, bud, cfg in rows]},
                  f, indent=1)
    print(f"\nPARETO FRONTIER ({len(out)} plans, held-out on {a.seeds} unseen seeds)")
    print(f"{'budget':>12s} {'score':>8s}  shelters kitchens casework start space food      reloc    cw")
    for d in out:
        c = d["cfg"]
        print(f"{d['budget']:+12,.0f} {d['score']:+8.3f}  {c['n_shelter']:8d} {c['n_kitchen']:8d} "
              f"{c['n_casework']:8d} {c['start']:5d} {c['spacing']:5d} {c['food']:9s} {c['reloc']:8s} {c['answer_cw']}")


if __name__ == "__main__":
    main()
