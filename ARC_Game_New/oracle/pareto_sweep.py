"""Pareto frontier of score vs final budget over the policy family (cora.policy_family).

Each member is played by the benchmark's own pareto baseline (bench/baselines/pareto.py) on the
exact surrogate (oracle.rollout), so a member's sweep score is the score it gets in the game.
The decisions that move the score are a handful of standing commitments -- how many of each
building, when to start, which delivery rule, whether to answer casework -- so searching the
family is better conditioned than per-decision search, and every frontier point is a policy you
can state in one line.

The frontier maximises BOTH score and final budget: its shape answers how much score a solvency
requirement costs. Frontier members are re-evaluated on held-out seeds so the curve is not a fit
to the search seeds.

    python -m oracle.pareto_sweep --seeds 20 --sample 2000 --jobs 8
"""
from __future__ import annotations

import argparse
import json
import os
import random
import statistics as st

from cora import policy_family
from oracle import RESULTS_ROOT
from oracle.rollout import FIRST_SEED, evaluate, pareto_front

_SEEDS = None


def _init(seeds):
    global _SEEDS
    _SEEDS = seeds


def _score(cfg, seeds=None):
    """(mean score, mean final budget) of one member over the seeds (common random numbers)."""
    res = evaluate({"name": "pareto", "cfg": cfg}, seeds if seeds is not None else _SEEDS)
    return st.mean(r.score for r in res), st.mean(r.budget for r in res)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--seeds", type=int, default=20)
    ap.add_argument("--sample", type=int, default=0, help="evaluate a random sample of the grid (0 = all)")
    ap.add_argument("--jobs", type=int, default=1)
    ap.add_argument("--out", default=os.path.join(RESULTS_ROOT, "pareto"))
    a = ap.parse_args(argv)
    os.makedirs(a.out, exist_ok=True)
    seeds = list(range(FIRST_SEED, FIRST_SEED + a.seeds))
    held = list(range(FIRST_SEED + 10_000, FIRST_SEED + 10_000 + a.seeds))

    members = policy_family.grid_members()
    if a.sample and a.sample < len(members):
        members = random.Random(0).sample(members, a.sample)
    print(f"evaluating {len(members)} members x {a.seeds} seeds on {a.jobs} workers", flush=True)
    if a.jobs > 1:
        import multiprocessing as mp
        with mp.Pool(a.jobs, initializer=_init, initargs=(seeds,)) as pool:
            res = pool.map(_score, members, chunksize=8)
    else:
        _init(seeds)
        res = [_score(c) for c in members]
    rows = [(sc, bud, cfg) for (sc, bud), cfg in zip(res, members)]
    out = []
    for _sc, _bud, cfg in pareto_front(rows):
        hsc, hbud = _score(cfg, held)
        out.append({"score": hsc, "budget": hbud, "cfg": cfg})
    out.sort(key=lambda d: d["budget"])
    with open(os.path.join(a.out, "pareto.json"), "w") as f:
        json.dump({"n_evaluated": len(rows), "seeds": seeds, "held_out": held, "frontier": out,
                   "all_points": [{"score": sc, "budget": bud, "cfg": cfg} for sc, bud, cfg in rows]},
                  f, indent=1)
    print(f"\nPARETO FRONTIER ({len(out)} members, held-out on {a.seeds} unseen seeds)")
    print(f"{'budget':>12s} {'score':>8s}  shelters kitchens casework start space food      reloc    cw switch")
    for d in out:
        c = d["cfg"]
        print(f"{d['budget']:+12,.0f} {d['score']:+8.3f}  {c['n_shelter']:8d} {c['n_kitchen']:8d} "
              f"{c['n_casework']:8d} {c['start']:5d} {c['spacing']:5d} {c['food']:9s} {c['reloc']:8s} "
              f"{c['answer_cw']} {c['switch']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
