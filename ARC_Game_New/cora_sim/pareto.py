"""Upper bounds, Pareto frontier, and strategy clusters from an evolve.py log.

    python -m cora_sim.pareto runs/evo.jsonl [--k 5] [--objectives satisfaction cost_efficiency]

Upper bound: the best score found per seed (what the search proved reachable; the true
optimum is at least this). Frontier: trajectories no other trajectory beats on every
objective (default: the game's two headline components -- satisfaction and cost
efficiency -- higher is better for both). Clusters: k-means on standardised strategy
features (what the plan did: build counts and timing, hiring, training) so that runs with
the same shape land together regardless of score; each cluster is described by its centroid
in the original units and its best member.
"""
from __future__ import annotations

import argparse
import json
import sys

import numpy as np

STRATEGY_KEYS = ["builds_kitchen", "builds_shelter", "builds_casework", "hire_untrained",
                 "hire_trained", "train", "first_build_round", "first_hire_round", "menu_turns"]


def load(path):
    with open(path) as f:
        return [json.loads(l) for l in f if l.strip()]


def pareto_front(rows, objectives):
    pts = np.array([[r["features"][o] for o in objectives] for r in rows])
    keep = np.ones(len(rows), bool)
    for i, p in enumerate(pts):
        if not keep[i]:
            continue
        dominated = np.all(pts >= p, axis=1) & np.any(pts > p, axis=1)
        if dominated.any():
            keep[i] = False
    return [r for r, k in zip(rows, keep) if k]


def dedupe(rows):
    seen, out = set(), []
    for r in rows:
        key = json.dumps(r["plan"], sort_keys=True) + str(r["seed_state"])
        if key not in seen:
            seen.add(key); out.append(r)
    return out


def kmeans(X, k, rng, iters=100):
    C = X[rng.choice(len(X), k, replace=False)]
    for _ in range(iters):
        lab = np.argmin(((X[:, None, :] - C[None]) ** 2).sum(-1), axis=1)
        newC = np.array([X[lab == j].mean(0) if (lab == j).any() else C[j] for j in range(k)])
        if np.allclose(newC, C):
            break
        C = newC
    return lab, C


def describe_plan(plan):
    out = []
    for r, g in enumerate(plan):
        for a in g["menu"]:
            out.append(f"r{r}:{a}")
    return " ".join(out) if out else "(idle -- answers tasks only)"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("log")
    ap.add_argument("--k", type=int, default=5)
    ap.add_argument("--objectives", nargs="+", default=["satisfaction", "cost_efficiency"])
    ap.add_argument("--seed", type=int, default=0)
    args = ap.parse_args()
    rows = dedupe(load(args.log))
    print(f"{len(rows)} distinct trajectories, {len({tuple(r['seed_state']) for r in rows})} seeds")

    print("\nUPPER BOUNDS (best score found per seed)")
    by_seed = {}
    for r in rows:
        by_seed.setdefault(tuple(r["seed_state"]), []).append(r)
    for s, rs in by_seed.items():
        best = max(rs, key=lambda r: r["score"])
        idle = [r for r in rs if r["features"]["menu_turns"] == 0]
        base = max(idle, key=lambda r: r["score"])["score"] if idle else float("nan")
        print(f"  seed {list(s)}: best {best['score']:.4f}  (idle-policy {base:.4f}, n={len(rs)})"
              f"\n      {describe_plan(best['plan'])}")

    front = sorted(pareto_front(rows, args.objectives), key=lambda r: -r["features"][args.objectives[0]])
    print(f"\nPARETO FRONTIER over {args.objectives}: {len(front)} trajectories")
    for r in front[:25]:
        f = r["features"]
        print("  " + "  ".join(f"{o}={f[o]:.4f}" for o in args.objectives) + f"  score={r['score']:.4f}"
              f"  builds K/S/C={f['builds_kitchen']}/{f['builds_shelter']}/{f['builds_casework']}"
              f" hire={f['hire_untrained']}+{f['hire_trained']}t train={f['train']}")

    X = np.array([[r["features"][k] for k in STRATEGY_KEYS] for r in rows], float)
    mu, sd = X.mean(0), X.std(0); sd[sd == 0] = 1
    lab, C = kmeans((X - mu) / sd, min(args.k, len(rows)), np.random.default_rng(args.seed))
    print(f"\nSTRATEGY CLUSTERS (k-means on {STRATEGY_KEYS})")
    order = sorted(range(len(C)), key=lambda j: -max(r["score"] for r, l in zip(rows, lab) if l == j))
    for j in order:
        members = [r for r, l in zip(rows, lab) if l == j]
        cen = C[j] * sd + mu
        scores = np.array([r["score"] for r in members])
        print(f"  cluster {j}: n={len(members)}  score best {scores.max():.4f} mean {scores.mean():.4f}"
              f"  sat mean {np.mean([r['features']['satisfaction'] for r in members]):.3f}"
              f"  cost_eff mean {np.mean([r['features']['cost_efficiency'] for r in members]):.3f}")
        print("      centroid: " + ", ".join(f"{k}={v:.1f}" for k, v in zip(STRATEGY_KEYS, cen)))
        print("      best:     " + describe_plan(max(members, key=lambda r: r["score"])["plan"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
