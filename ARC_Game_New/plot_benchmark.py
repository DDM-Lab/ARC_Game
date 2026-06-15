"""
Visualize ARC gym benchmark results.

Usage:
  python plot_benchmark.py <results_dir> [<results_dir2> ...] [--out DIR] [--label A,B]

Each results_dir holds an episodes.jsonl from benchmark_models.py. Passing more than
one dir overlays them as conditions (e.g. no-impacts vs impacts ablation). Produces:
  - summary.png            per-model bars: reward, satisfaction, food%/lodging%, mistakes
  - budget_trajectories.png  per-model budget over rounds (the death-spiral)
  - satisfaction_trajectories.png
"""
import sys, json, argparse, statistics as st
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def short(m):
    return (m.split("/")[-1].replace("us.anthropic.", "").replace("-20251001-v1:0", "")
            .replace(":0", ""))


def load(d):
    recs = []
    p = Path(d) / "episodes.jsonl"
    for line in open(p):
        r = json.loads(line)
        if r.get("summary") and not r.get("error"):
            recs.append(r)
    return recs


def mean(xs):
    xs = [x for x in xs if x is not None]
    return st.mean(xs) if xs else 0.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dirs", nargs="+")
    ap.add_argument("--out", default=None)
    ap.add_argument("--labels", default=None, help="comma-separated condition labels")
    args = ap.parse_args()

    labels = (args.labels.split(",") if args.labels
              else [Path(d).name.replace("cheap_20ep_", "").replace("cheap_20ep", "run") for d in args.dirs])
    conds = [(labels[i], load(d)) for i, d in enumerate(args.dirs)]
    outdir = Path(args.out or args.dirs[0]) / "plots"
    outdir.mkdir(parents=True, exist_ok=True)

    # consistent model ordering/colors across plots
    models = []
    for _, recs in conds:
        for r in recs:
            if r["model"] not in models:
                models.append(r["model"])
    mlabels = [short(m) for m in models]

    # ── 1. Summary bars (2x2) ───────────────────────────────────────────────
    fig, axes = plt.subplots(2, 2, figsize=(13, 9))
    panels = [
        ("Mean total reward", lambda s: s["totalReward"], axes[0, 0]),
        ("Mean final satisfaction", lambda s: s["finalSat"], axes[0, 1]),
        ("Demand fulfillment rate", None, axes[1, 0]),     # special: food + lodging
        ("Failure modes (fraction of episodes)", None, axes[1, 1]),
    ]
    ncond = len(conds)
    x = range(len(models))
    w = 0.8 / max(ncond, 1)

    for ci, (clabel, recs) in enumerate(conds):
        by = {m: [r["summary"] for r in recs if r["model"] == m] for m in models}
        off = (ci - (ncond - 1) / 2) * w
        # reward
        axes[0, 0].bar([i + off for i in x], [mean([s["totalReward"] for s in by[m]]) for m in models],
                       w, label=clabel)
        # satisfaction
        axes[0, 1].bar([i + off for i in x], [mean([s["finalSat"] for s in by[m]]) for m in models],
                       w, label=clabel)

    axes[0, 0].set_title("Mean total reward"); axes[0, 0].axhline(0, color="k", lw=.5)
    axes[0, 1].set_title("Mean final satisfaction (0-100)"); axes[0, 1].axhline(50, color="grey", ls="--", lw=.7)
    for ax in (axes[0, 0], axes[0, 1]):
        ax.set_xticks(list(x)); ax.set_xticklabels(mlabels, rotation=15, ha="right")
        if ncond > 1: ax.legend(fontsize=8)

    # food + lodging fulfillment (grouped food/lodging, condition as hatch only if 1 cond shown plain)
    ax = axes[1, 0]
    gw = 0.35
    for ci, (clabel, recs) in enumerate(conds):
        by = {m: [r["summary"] for r in recs if r["model"] == m] for m in models}
        off = (ci - (ncond - 1) / 2) * (0.8 / ncond)
        food = [mean([s["foodFulfillRate"] for s in by[m]]) for m in models]
        lodg = [mean([s["lodgingFulfillRate"] for s in by[m]]) for m in models]
        base = [i + off for i in x]
        ax.bar([b - gw/2/ncond for b in base], food, gw/ncond, color="tab:green",
               alpha=0.5 + 0.5*ci/max(ncond-1, 1), label=f"food ({clabel})" if ncond > 1 else "food")
        ax.bar([b + gw/2/ncond for b in base], lodg, gw/ncond, color="tab:orange",
               alpha=0.5 + 0.5*ci/max(ncond-1, 1), label=f"lodging ({clabel})" if ncond > 1 else "lodging")
    ax.set_title("Demand fulfillment rate (green=food, orange=lodging)")
    ax.set_ylim(0, 1.05); ax.set_xticks(list(x)); ax.set_xticklabels(mlabels, rotation=15, ha="right")
    ax.legend(fontsize=8)

    # failure modes
    ax = axes[1, 1]
    fmw = 0.8 / (ncond * 2)
    for ci, (clabel, recs) in enumerate(conds):
        by = {m: [r["summary"] for r in recs if r["model"] == m] for m in models}
        neg = [mean([1.0 if s["wentNegative"] else 0.0 for s in by[m]]) for m in models]
        term = [mean([1.0 if s["terminated"] else 0.0 for s in by[m]]) for m in models]
        o = (ci - (ncond - 1) / 2) * (0.8 / ncond)
        ax.bar([i + o - fmw/2 for i in x], neg, fmw, color="tab:red",
               alpha=0.5 + 0.5*ci/max(ncond-1, 1), label=f"neg-budget ({clabel})" if ncond > 1 else "went budget-negative")
        ax.bar([i + o + fmw/2 for i in x], term, fmw, color="tab:purple",
               alpha=0.5 + 0.5*ci/max(ncond-1, 1), label=f"terminated ({clabel})" if ncond > 1 else "terminated (sat=0)")
    ax.set_title("Failure modes (fraction of episodes)")
    ax.set_ylim(0, 1.05); ax.set_xticks(list(x)); ax.set_xticklabels(mlabels, rotation=15, ha="right")
    ax.legend(fontsize=8)

    title = "ARC gym benchmark" + (f" — {' vs '.join(labels)}" if ncond > 1 else f" — {labels[0]}")
    fig.suptitle(title, fontsize=14, weight="bold")
    fig.tight_layout(rect=[0, 0, 1, 0.97])
    fig.savefig(outdir / "summary.png", dpi=130); plt.close(fig)

    # ── 2 & 3. Trajectories (budget, satisfaction) ──────────────────────────
    def traj_plot(key, title, fname, hline=None):
        fig, axs = plt.subplots(1, len(models), figsize=(5.5 * len(models), 4.5), squeeze=False)
        axs = axs[0]
        colors = plt.cm.tab10.colors
        for mi, m in enumerate(models):
            ax = axs[mi]
            for ci, (clabel, recs) in enumerate(conds):
                eps = [r for r in recs if r["model"] == m]
                col = colors[ci]
                for r in eps:
                    ys = [rd[key] for rd in r["rounds"]]
                    ax.plot(range(len(ys)), ys, color=col, alpha=0.18, lw=1)
                # mean curve over common rounds
                maxr = max((len(r["rounds"]) for r in eps), default=0)
                means = []
                for t in range(maxr):
                    vals = [r["rounds"][t][key] for r in eps if len(r["rounds"]) > t]
                    means.append(mean(vals))
                ax.plot(range(len(means)), means, color=col, lw=2.5,
                        label=f"{clabel} (n={len(eps)})")
            if hline is not None: ax.axhline(hline, color="grey", ls="--", lw=.7)
            ax.set_title(short(m)); ax.set_xlabel("round"); ax.set_ylabel(key)
            ax.legend(fontsize=8)
        fig.suptitle(title, fontsize=13, weight="bold")
        fig.tight_layout(rect=[0, 0, 1, 0.95])
        fig.savefig(outdir / fname, dpi=130); plt.close(fig)

    traj_plot("budget", "Budget over rounds (faint=episodes, bold=mean) — the death-spiral",
              "budget_trajectories.png", hline=0)
    traj_plot("sat", "Satisfaction over rounds (faint=episodes, bold=mean)",
              "satisfaction_trajectories.png", hline=50)

    # ── 4. Reward components over rounds (per model) ────────────────────────
    # Each component is cumulative-to-date; satisfaction terms are positive, cost
    # terms subtract (plotted negative), and `score` is the net. Only runs produced
    # after component logging was added carry rd["comps"]; skip otherwise.
    COMP = [("sat_food", "food (sat)", "tab:green", +1),
            ("sat_lodging", "lodging (sat)", "tab:olive", +1),
            ("sat_worker_use", "worker-use (sat)", "tab:blue", +1),
            ("cost_food", "food cost", "tab:red", -1),
            ("cost_lodging", "lodging cost", "tab:orange", -1),
            ("cost_worker", "worker cost", "tab:brown", -1)]
    has_comps = any(rd.get("comps") for _, recs in conds for r in recs for rd in r["rounds"])
    if has_comps and len(conds) >= 1:
        # one row per condition, one column per model
        nrow = len(conds)
        fig, axs = plt.subplots(nrow, len(models), figsize=(5.5 * len(models), 4.3 * nrow), squeeze=False)
        for ci, (clabel, recs) in enumerate(conds):
            for mi, m in enumerate(models):
                ax = axs[ci][mi]
                eps = [r for r in recs if r["model"] == m and r["rounds"] and r["rounds"][0].get("comps")]
                maxr = max((len(r["rounds"]) for r in eps), default=0)
                for key, klabel, col, sign in COMP:
                    means = []
                    for t in range(maxr):
                        vals = [sign * r["rounds"][t]["comps"].get(key, 0.0)
                                for r in eps if len(r["rounds"]) > t and r["rounds"][t].get("comps")]
                        means.append(mean(vals))
                    ax.plot(range(len(means)), means, color=col, lw=1.8, label=klabel)
                # net score (bold)
                score_means = []
                for t in range(maxr):
                    vals = [r["rounds"][t]["comps"].get("score", 0.0)
                            for r in eps if len(r["rounds"]) > t and r["rounds"][t].get("comps")]
                    score_means.append(mean(vals))
                ax.plot(range(len(score_means)), score_means, color="black", lw=2.6, label="net score")
                ax.axhline(0, color="grey", lw=.5)
                ax.set_title(f"{short(m)} — {clabel} (n={len(eps)})")
                ax.set_xlabel("round"); ax.set_ylabel("cumulative contribution")
                if mi == 0 and ci == 0: ax.legend(fontsize=7, ncol=2)
        fig.suptitle("Reward components over rounds (satisfaction terms +, cost terms −, bold=net score)",
                     fontsize=13, weight="bold")
        fig.tight_layout(rect=[0, 0, 1, 0.96])
        fig.savefig(outdir / "reward_components.png", dpi=130); plt.close(fig)

    print(f"wrote plots to {outdir}/")
    for f in sorted(outdir.glob("*.png")):
        print("  ", f)


if __name__ == "__main__":
    main()
