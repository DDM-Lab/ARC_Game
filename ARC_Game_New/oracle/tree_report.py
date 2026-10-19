"""Where a search spent its simulations: summarise oracle.search --tree-log files per run.

    python -m oracle.tree_report RUN_DIR [RUN_DIR ...] [--json out.json]

Each RUN_DIR holds tree_<seed>.jsonl (one line per decision). Reported per run:
    coverage   simulations, tree nodes and distinct turns per decision; the deepest node; the
               share of simulations that got past depth k (how far ahead the tree looked)
    root       children, effective branching (exp of the visit entropy), the top child's share
    where      nodes by game stage (decision index) and depth
    mix        per turn, how often expanded turns (tree, all depths) and played turns build each
               type, hire, staff, take the Motel or Rapid Response, or do nothing
    cost       model calls and seconds per decision
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import statistics as st
import sys
from collections import Counter, defaultdict

MIX = ["build_shelter", "build_kitchen", "build_casework", "hire_trained", "hire_untrained", "train",
       "staff", "motel", "rrv", "unanswered", "empty"]


def load(run):
    recs = []
    for p in sorted(glob.glob(os.path.join(run, "tree_*.jsonl"))):
        recs += [json.loads(l) for l in open(p) if l.strip()]
    return recs


def summarise(recs):
    if not recs:
        return None
    out = {"decisions": len(recs), "games": len({r["seed"] for r in recs})}
    m = lambda xs: st.mean(xs) if xs else float("nan")
    out["sims"] = m([r.get("sims", 0) for r in recs])
    out["nodes"] = m([sum((r.get("nodes_by_depth") or {}).values()) for r in recs])
    out["max_depth"] = m([r.get("max_depth", 0) for r in recs])
    reach = Counter()
    for r in recs:
        for dep, n in (r.get("visits_by_depth") or {}).items():
            reach[int(dep)] += n
    tot = sum(r.get("sims", 0) for r in recs) or 1
    out["reach"] = {k: reach[k] / tot for k in sorted(reach)}            # share of sims reaching depth k
    out["root_children"] = m([r.get("root_children", 0) for r in recs])
    out["root_eff_branching"] = m([r.get("root_eff_branching", 0) for r in recs if r.get("root_children")])
    out["top_share"] = m([r["pv"][0]["share"] for r in recs if r.get("pv")])
    where = defaultdict(Counter)
    for r in recs:
        stage = r["decision"] // 5 * 5
        for dep, n in (r.get("nodes_by_depth") or {}).items():
            where[stage][int(dep)] += n
    per_stage = Counter(r["decision"] // 5 * 5 for r in recs)
    out["where"] = {s: {d: c / per_stage[s] for d, c in sorted(cs.items())} for s, cs in sorted(where.items())}
    tree_mix, n_tree = Counter(), 0
    for r in recs:
        for dep, mx in (r.get("mix_by_depth") or {}).items():
            tree_mix.update(mx)
        n_tree += sum((r.get("nodes_by_depth") or {}).values())
    played = Counter()
    for r in recs:
        played.update(r.get("played") or {})
    out["mix_tree"] = {k: tree_mix[k] / max(1, n_tree) for k in MIX}
    out["mix_played"] = {k: played[k] / len(recs) for k in MIX}
    out["model_calls"] = m([r.get("model_calls", 0) for r in recs])
    out["seconds"] = m([r.get("seconds", 0) for r in recs])
    leaves = defaultdict(list)
    for r in recs:
        for dep, l in (r.get("leaves_by_depth") or {}).items():
            leaves[int(dep)].append(l["mean"])
    out["leaf_mean_by_depth"] = {d: m(v) for d, v in sorted(leaves.items())}
    return out


def show(name, s):
    print(f"\n== {name}: {s['games']} games, {s['decisions']} decisions")
    print(f"  per decision: {s['sims']:.0f} sims, {s['nodes']:.0f} nodes, max depth {s['max_depth']:.1f}, "
          f"{s['model_calls']:.0f} model calls, {s['seconds']:.1f}s")
    print("  share of sims reaching depth: " + "  ".join(f"{k}:{v:.2f}" for k, v in s["reach"].items()))
    print(f"  root: {s['root_children']:.1f} children, effective branching {s['root_eff_branching']:.2f}, "
          f"top child {s['top_share']:.2f} of visits")
    print("  nodes per decision by game stage (rows) and depth (cols):")
    for stage, ds in s["where"].items():
        print(f"    d{stage:02d}-{stage + 4:02d}  " + "  ".join(f"{d}:{c:5.1f}" for d, c in ds.items()))
    print(f"  {'per turn':14}" + "".join(f"{k[:9]:>10}" for k in MIX))
    print(f"  {'tree turns':14}" + "".join(f"{s['mix_tree'][k]:>10.2f}" for k in MIX))
    print(f"  {'played turns':14}" + "".join(f"{s['mix_played'][k]:>10.2f}" for k in MIX))
    print("  mean leaf value by depth: " + "  ".join(f"{d}:{v:.3f}" for d, v in s["leaf_mean_by_depth"].items()))


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("runs", nargs="+")
    ap.add_argument("--json")
    a = ap.parse_args(argv)
    allr = {}
    for run in a.runs:
        s = summarise(load(run))
        if s:
            allr[os.path.basename(run.rstrip("/"))] = s
            show(os.path.basename(run.rstrip("/")), s)
    if a.json:
        json.dump(allr, open(a.json, "w"), indent=1)


if __name__ == "__main__":
    main(sys.argv[1:])
