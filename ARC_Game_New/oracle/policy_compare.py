"""Engine agreement study: run the SAME 100 policies on Unity and on the surrogate.

WHY 100 POLICIES AND NOT MORE VALIDATION OF THREE
validate_surrogate.py gates on four hand-picked policies, three of which the model was fitted to.
That answers "is the surrogate wrong on the cases I looked at?" -- it cannot answer "is the
surrogate wrong in a way I have not looked at?", which is the question that matters before trusting
a frontier derived from 41,280 unseen policies. A wide sample of the SAME family the sweep searches
turns the second question into a measurement: if agreement holds across the family, the frontier is
trustworthy; if it degrades in a particular corner, that corner is where the frontier will lie to
us, and the residual pattern names the missing mechanic.

WHAT "AGREEMENT" MEANS HERE
Unity has no set_seed (see UNITY_SEED_SNAPSHOT.md), so a policy's Unity result and its surrogate
result are two samples of two different random processes; per-episode identity is impossible and
not the target. The target is that the two engines induce the same ORDERING and the same LEVELS
over the policy family, because that is all a search actually consumes. So we report:
  * bias and MAE on the mean score -- are the levels right?
  * Pearson/Spearman across policies -- is the ranking right? (the sweep only needs the ranking)
  * regret@k -- if you picked the top-k by surrogate, what do they really score in Unity?
The last is the decision-relevant one and the only one the frontier's validity rests on.
"""
from __future__ import annotations
import sys, os, json, argparse, random, itertools
import statistics as st
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

GRID = dict(
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


def sample_policies(n, seed=1234):
    """Stratified sample. Pure uniform sampling of a 9-dim grid leaves whole faces empty at n=100
    (with 6 switch levels and 9 shelter levels, ~1/3 of the (shelter, switch) cells would be
    unvisited), and an agreement study is only as good as its coverage of the corners. So the
    strata that drive the mechanics we corrected -- reloc rule, answer_cw, and shelter count -- are
    balanced by construction, and the remaining knobs are drawn uniformly."""
    rng = random.Random(seed)
    strata = list(itertools.product(GRID["reloc"], GRID["answer_cw"],
                                    [0, 1, 2, 3, 4, 5, 6, 7, 8]))
    rng.shuffle(strata)
    out, seen = [], set()
    while len(out) < n:
        for reloc, acw, nsh in strata:
            if len(out) >= n:
                break
            cfg = dict(n_shelter=nsh, reloc=reloc, answer_cw=acw,
                       n_kitchen=rng.choice(GRID["n_kitchen"]),
                       n_casework=rng.choice(GRID["n_casework"]),
                       start=rng.choice(GRID["start"]),
                       spacing=rng.choice(GRID["spacing"]),
                       food=rng.choice(GRID["food"]),
                       switch=rng.choice(GRID["switch"]))
            if cfg["n_shelter"] + cfg["n_kitchen"] + cfg["n_casework"] > 15:
                continue
            key = json.dumps(cfg, sort_keys=True)
            if key in seen:
                continue
            seen.add(key); out.append(cfg)
    return out


def eval_surrogate(cfg, seeds):
    from arc_surrogate import ArcSurrogate
    from mcts_oracle import apply
    from pareto_sweep import make_plan
    macro = make_plan(**cfg)
    sc, bud, comps = [], [], []
    for sd in seeds:
        s = ArcSurrogate(sd); r = 0
        while not s.done():
            s.step(apply(s, macro(r))); r += 1
        sc.append(s.score()); bud.append(s.budget); comps.append(s.components())
    keys = set().union(*[set(c) for c in comps])
    return dict(score=st.mean(sc), budget=st.mean(bud),
                comps={k: st.mean([c.get(k, 0.0) for c in comps]) for k in keys},
                sd=st.pstdev(sc))


def read_unity(path):
    """Aggregate one Unity run dir (episodes.jsonl). Episodes short of 32 rounds are dropped: a
    crashed episode is not a low score, and averaging it in would be a fabricated data point."""
    eps = [e for e in (json.loads(l) for l in open(path)) if len(e.get("rounds") or []) == 32]
    if not eps:
        return None
    sc, bud, comps = [], [], []
    for e in eps:
        last = e["rounds"][-1]
        sc.append(last.get("score", e["summary"]["totalReward"]))
        bud.append(last["budget"]); comps.append(last.get("comps") or {})
    keys = set().union(*[set(c) for c in comps]) if comps else set()
    return dict(score=st.mean(sc), budget=st.mean(bud), n=len(eps),
                comps={k: st.mean([c.get(k, 0.0) for c in comps]) for k in keys},
                sd=st.pstdev(sc) if len(sc) > 1 else 0.0)


def _rank(xs):
    order = sorted(range(len(xs)), key=lambda i: xs[i])
    r = [0.0] * len(xs)
    for pos, i in enumerate(order):
        r[i] = pos
    return r


def _pearson(a, b):
    ma, mb = st.mean(a), st.mean(b)
    num = sum((x - ma) * (y - mb) for x, y in zip(a, b))
    da = sum((x - ma) ** 2 for x in a) ** .5
    db = sum((y - mb) ** 2 for y in b) ** .5
    return num / (da * db) if da and db else float("nan")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["sample", "surrogate", "collect"])
    ap.add_argument("--n", type=int, default=100)
    ap.add_argument("--seeds", type=int, default=200)
    ap.add_argument("--dir", default="/zfsauton/scratch/cpulling/arc_benchmarks/agree100")
    ap.add_argument("--unity-root", default="/zfsauton/scratch/cpulling/arc_benchmarks/agree100/unity")
    a = ap.parse_args()
    os.makedirs(a.dir, exist_ok=True)
    pf = os.path.join(a.dir, "policies.json")

    if a.mode == "sample":
        pols = sample_policies(a.n)
        json.dump(pols, open(pf, "w"), indent=1)
        print(f"wrote {len(pols)} policies -> {pf}")
        for i, c in enumerate(pols[:10]):
            print(f"  {i:3d} {c}")
        return

    pols = json.load(open(pf))

    if a.mode == "surrogate":
        # held-out seeds: the sweep searches on 0..59, so scoring the study on those would measure
        # the fit rather than the model.
        seeds = list(range(50_000, 50_000 + a.seeds))
        res = [eval_surrogate(c, seeds) for c in pols]
        json.dump(res, open(os.path.join(a.dir, "surrogate.json"), "w"), indent=1)
        print(f"surrogate: {len(res)} policies x {a.seeds} held-out seeds")
        print(f"  score range {min(r['score'] for r in res):+.3f} .. {max(r['score'] for r in res):+.3f}")
        return

    # ---- collect ----
    sur = json.load(open(os.path.join(a.dir, "surrogate.json")))
    rows = []
    for i, cfg in enumerate(pols):
        p = os.path.join(a.unity_root, f"p{i:03d}", "episodes.jsonl")
        u = read_unity(p) if os.path.exists(p) else None
        if u:
            rows.append((i, cfg, sur[i], u))
    if not rows:
        print("no Unity results yet"); return
    us = [r[3]["score"] for r in rows]
    ss = [r[2]["score"] for r in rows]
    d = [s - u for s, u in zip(ss, us)]
    print(f"POLICIES COMPARED: {len(rows)}/{len(pols)}   "
          f"unity episodes/policy: {min(r[3]['n'] for r in rows)}-{max(r[3]['n'] for r in rows)}")
    print(f"  unity score    {min(us):+.3f} .. {max(us):+.3f}   mean {st.mean(us):+.3f}")
    print(f"  surrogate      {min(ss):+.3f} .. {max(ss):+.3f}   mean {st.mean(ss):+.3f}")
    print(f"  bias (sur-uni) {st.mean(d):+.3f}   MAE {st.mean([abs(x) for x in d]):.3f}   "
          f"max|Δ| {max(abs(x) for x in d):.3f}")
    print(f"  pearson  r={_pearson(ss, us):.3f}    spearman r={_pearson(_rank(ss), _rank(us)):.3f}")
    for k in (1, 3, 5, 10):
        top = sorted(range(len(rows)), key=lambda i: -ss[i])[:k]
        best_u = max(us)
        print(f"  regret@{k:<2d} picking top-{k} by surrogate -> best true Unity "
              f"{max(us[i] for i in top):+.3f}  (true best {best_u:+.3f}, "
              f"regret {best_u - max(us[i] for i in top):+.3f})")
    print("\nWORST 10 DISAGREEMENTS")
    for i in sorted(range(len(rows)), key=lambda i: -abs(d[i]))[:10]:
        idx, cfg, s, u = rows[i]
        print(f"  p{idx:03d} Δ{d[i]:+.3f}  sur {s['score']:+.3f}  uni {u['score']:+.3f}  "
              f"sh{cfg['n_shelter']} ki{cfg['n_kitchen']} cw{cfg['n_casework']} "
              f"{cfg['food']:9s} {cfg['reloc']:7s} acw{cfg['answer_cw']} sw{cfg['switch']}")
    json.dump([{"i": i, "cfg": c, "surrogate": s, "unity": u} for i, c, s, u in rows],
              open(os.path.join(a.dir, "compare.json"), "w"), indent=1)
    print(f"\nwrote {os.path.join(a.dir, 'compare.json')}")


if __name__ == "__main__":
    main()
