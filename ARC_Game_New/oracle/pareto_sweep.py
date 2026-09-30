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
import sys, os, json, itertools, argparse
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from arc_surrogate import ArcSurrogate
import statistics as st


def make_plan(n_shelter, n_kitchen, n_casework, start, spacing, food, reloc, answer_cw,
              switch=None):
    """Standing commitments -> a per-round macro schedule.

    `switch` splits the episode: use the first rule before that round and the second after. Pure
    rules cluster at the two ends of the trade-off (motel routing runs the $200/person/day meter;
    shelter routing avoids it), which left a 250k-wide hole in the frontier that was an artefact of
    the grid rather than a real discontinuity. Mixed rules fill it.
    """
    order = (["CaseworkSite"] * n_casework + ["Shelter"] * n_shelter + ["Kitchen"] * n_kitchen)
    sched = {}
    r = start
    for b in order:
        sched[r] = b
        r += spacing
    def macro(rnd):
        b = sched.get(rnd)
        f, rl = food, reloc
        if switch is not None and rnd >= switch:
            f = "paid" if food == "kitchen10" else "kitchen10"
            rl = "motel" if reloc == "shelter" else "shelter"
        return (b, 4 if b else 0, f, rl, answer_cw)
    return macro


def evaluate(macro, seeds):
    from mcts_oracle import apply
    sc, bud = [], []
    for sd in seeds:
        s = ArcSurrogate(sd); r = 0
        while not s.done():
            s.step(apply(s, macro(r))); r += 1
        sc.append(s.score()); bud.append(s.budget)
    return st.mean(sc), st.mean(bud)


_SEEDS = None


def _init(seeds):
    global _SEEDS
    _SEEDS = seeds


def _one(cfg):
    return evaluate(make_plan(**cfg), _SEEDS)


def pareto_front(points):
    """Non-dominated on (score, budget): nothing else is >= on both and > on one."""
    pts = sorted(points, key=lambda t: (-t[0], -t[1]))
    front, best_bud = [], float("-inf")
    for p in pts:
        if p[1] > best_bud:
            front.append(p); best_bud = p[1]
    return front


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", type=int, default=60)
    ap.add_argument("--jobs", type=int, default=1)
    ap.add_argument("--out", default="/zfsauton/scratch/cpulling/arc_benchmarks/pareto")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    seeds = list(range(a.seeds))
    held = list(range(10_000, 10_000 + a.seeds))

    grid = dict(
        n_shelter=[0, 1, 2, 3, 4, 5, 6, 7, 8],
        n_kitchen=[0, 1, 2, 3, 4, 5],
        n_casework=[0, 1, 2, 3],
        start=[0, 2],
        spacing=[1, 2],
        food=["kitchen10", "paid"],
        reloc=["motel", "shelter"],
        answer_cw=[0, 1],
        switch=[None, 6, 10, 14, 18, 22],
    )
    keys = list(grid)
    combos = [dict(zip(keys, v)) for v in itertools.product(*(grid[k] for k in keys))]
    # 15 AbandonedSites on the map -- plans that ask for more are not buildable
    combos = [c for c in combos if c["n_shelter"] + c["n_kitchen"] + c["n_casework"] <= 15]
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
        hsc, hbud = evaluate(make_plan(**cfg), held)
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
