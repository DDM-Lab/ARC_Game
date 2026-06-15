"""
Cross-model comparison plots: pool all models from one or more result dirs and put
them on SHARED axes (one line/bar per model) so tiers/models compare directly.
Deliberately compact — two figures, no per-model subplot explosion.

Usage:
  python plot_compare.py <dir> [<dir> ...] --out DIR [--title T]

  compare_summary.png       per-model bars: reward, final satisfaction, food%/lodging%
  compare_trajectories.png  budget / satisfaction / cumulative-score, one mean line per model
"""
import sys, json, argparse, statistics as st
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def short(m):
    return (m.split("/")[-1].replace("us.anthropic.", "").replace("-20251001-v1:0", "")
            .replace(":0", ""))


def mean(xs):
    xs = [x for x in xs if x is not None]
    return st.mean(xs) if xs else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dirs", nargs="+")
    ap.add_argument("--out", required=True)
    ap.add_argument("--title", default="ARC gym — model comparison")
    args = ap.parse_args()

    # pool ok episodes by model across all dirs
    by = {}
    for d in args.dirs:
        for line in open(Path(d) / "episodes.jsonl"):
            r = json.loads(line)
            if r.get("summary") and r.get("rounds") and not r.get("error"):
                by.setdefault(r["model"], []).append(r)
    # order by mean reward (best first)
    models = sorted(by, key=lambda m: -(mean([r["summary"]["totalReward"] for r in by[m]]) or -9e9))
    labels = [short(m) for m in models]
    colors = plt.cm.tab10.colors
    cmap = {m: colors[i % 10] for i, m in enumerate(models)}
    outdir = Path(args.out); outdir.mkdir(parents=True, exist_ok=True)

    # ── Fig 1: per-model summary bars ───────────────────────────────────────
    fig, ax = plt.subplots(2, 2, figsize=(13, 9))
    x = range(len(models))
    def bars(a, vals, title, hline=None, pct=False):
        a.bar(x, [v if v is not None else 0 for v in vals], color=[cmap[m] for m in models])
        a.set_title(title); a.set_xticks(list(x)); a.set_xticklabels(labels, rotation=20, ha="right")
        if hline is not None: a.axhline(hline, color="grey", ls="--", lw=.7)
        if pct: a.set_ylim(0, 1.05)
    bars(ax[0, 0], [mean([r["summary"]["totalReward"] for r in by[m]]) for m in models],
         "Mean total reward", hline=0)
    bars(ax[0, 1], [mean([r["summary"]["finalSat"] for r in by[m]]) for m in models],
         "Mean final satisfaction (0-100)", hline=50)
    bars(ax[1, 0], [mean([r["summary"]["foodFulfillRate"] for r in by[m]]) for m in models],
         "Food fulfillment rate", pct=True)
    bars(ax[1, 1], [mean([r["summary"]["lodgingFulfillRate"] for r in by[m]]) for m in models],
         "Lodging fulfillment rate", pct=True)
    fig.suptitle(args.title + "  (bars sorted by reward; n per model in legend below)",
                 fontsize=14, weight="bold")
    fig.text(0.5, 0.005, "  |  ".join(f"{short(m)}: n={len(by[m])}" for m in models),
             ha="center", fontsize=8)
    fig.tight_layout(rect=[0, 0.02, 1, 0.97])
    fig.savefig(outdir / "compare_summary.png", dpi=130); plt.close(fig)

    # ── Fig 2: overlaid trajectories (one mean line per model) ──────────────
    fig, axs = plt.subplots(1, 3, figsize=(18, 5))
    specs = [("budget", "Budget", 0, axs[0]), ("sat", "Satisfaction", 50, axs[1]),
             ("sumR", "Cumulative score", 0, axs[2])]
    for key, title, hline, a in specs:
        for m in models:
            eps = by[m]
            maxr = max(len(r["rounds"]) for r in eps)
            means = [mean([r["rounds"][t][key] for r in eps if len(r["rounds"]) > t]) for t in range(maxr)]
            a.plot(range(len(means)), means, color=cmap[m], lw=2.2, label=short(m))
        a.axhline(hline, color="grey", ls="--", lw=.7)
        a.set_title(title + " over rounds (mean)"); a.set_xlabel("round")
    axs[0].legend(fontsize=8, loc="lower left")
    fig.suptitle(args.title + " — trajectories (one line per model)", fontsize=14, weight="bold")
    fig.tight_layout(rect=[0, 0, 1, 0.95])
    fig.savefig(outdir / "compare_trajectories.png", dpi=130); plt.close(fig)

    print(f"wrote comparison plots ({len(models)} models) to {outdir}/")
    for m in models:
        print(f"   {short(m):22} n={len(by[m])}")


if __name__ == "__main__":
    main()
