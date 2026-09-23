#!/usr/bin/env python3
"""Can the models OPERATE the game, or are they just choosing badly?

Four interface metrics per run, each against score:
  parse_ok_rate     rounds whose reply parsed into a usable action set
  exec_rate         requested actions the engine actually executed
  fail_rate         requested actions the engine rejected
  empty_round_rate  rounds where the model requested NOTHING -- no action, no task choice

The first three measure the interface. The fourth measures whether the model chose to act at
all, which is a decision, not a capability. If the interface were the bottleneck, parse_ok and
exec_rate would correlate POSITIVELY with score. They do not.

Data: analysis/validity_per_run.csv, reduced on the cluster from the raw episode logs
(they carry full observations and reasoning, far too large to copy).

USAGE
  python analysis/plot_validity.py --min-n 24
"""
import argparse, csv, json, os, sys, statistics as st

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle

from plot_components import (read, assign_labels, label, series, short, f,
                             house_axes, INK, MUTE, GRID, RUST)

METRICS = [("parse_ok_rate",    "Reply parsed into usable actions", "interface"),
           ("exec_rate",        "Requested actions the engine executed", "interface"),
           ("fail_rate",        "Requested actions the engine rejected", "interface"),
           ("empty_round_rate", "Rounds where the model did nothing", "decision")]


def corr(xs, ys):
    if len(xs) < 3:
        return float("nan")
    mx, my = st.mean(xs), st.mean(ys)
    num = sum((a - mx) * (b - my) for a, b in zip(xs, ys))
    den = (sum((a - mx) ** 2 for a in xs) * sum((b - my) ** 2 for b in ys)) ** 0.5
    return num / den if den else float("nan")


def grid(rows, out):
    fig, axes = plt.subplots(2, 2, figsize=(13.0, 9.4), dpi=150)
    for ax, (key, nice, kind) in zip(axes.flat, METRICS):
        pts = [(f(r, key), f(r, "score_norm_mean"), r) for r in rows if f(r, key) is not None]
        if not pts:
            continue
        r_ = corr([p[0] for p in pts], [p[1] for p in pts])
        for x, y, row in pts:
            ax.plot(x, y, "o", ms=6.5, color=series(row)[0], alpha=0.85)
        # All four are rates. Autoscaling each to its own tiny span (parse 0.83-1.00,
        # exec 0.95-1.00) made near-constant metrics look like they varied a lot. Pinning
        # every panel to the full [0,1] shows the truth: three are pegged at the ceiling
        # (or floor) and only the last one actually moves.
        ax.set_xlim(-0.02, 1.02)
        ax.set_xlabel(nice, fontsize=10)
        ax.set_ylabel("normalised score", fontsize=10)
        tag = "INTERFACE" if kind == "interface" else "DECISION"
        ax.set_title(f"{tag}   ·   r = {r_:+.3f}", fontsize=11.5, fontweight="bold",
                     loc="left", pad=8, color=INK if kind == "interface" else RUST)
        ax.yaxis.grid(True, linestyle=":", color=GRID, lw=0.9)
        house_axes(ax)
    fig.suptitle("Is score explained by the interface, or by what the models choose to do?",
                 fontsize=14, fontweight="bold", x=0.008, ha="left", y=0.996)
    seen, h = [], []
    for r in rows:
        c, lab = series(r)
        if lab not in seen:
            seen.append(lab); h.append(Rectangle((0, 0), 1, 1, color=c, label=lab))
    fig.legend(handles=h, loc="lower center", ncol=len(h), frameon=False, fontsize=9.5,
               handlelength=1.1, bbox_to_anchor=(0.5, -0.022))
    fig.tight_layout(rect=(0, 0.02, 1, 0.985))
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"  {os.path.basename(out)}")


def ranked(rows, key, nice, out, descending=True):
    data = sorted([r for r in rows if f(r, key) is not None],
                  key=lambda r: f(r, key), reverse=descending)
    data.reverse()
    n = len(data)
    fig, ax = plt.subplots(figsize=(12.6, max(3.2, 0.30 * n + 2.6)), dpi=150)
    ys = list(range(n))
    vals = [f(r, key) for r in data]
    ax.barh(ys, vals, height=0.72, color=[series(r)[0] for r in data])
    ax.set_yticks(ys); ax.set_yticklabels([label(r) for r in data], fontsize=8.0)
    ax.set_ylim(-0.9, n - 0.1)
    span = max(vals) or 1.0
    for y, (v, r) in enumerate(zip(vals, data)):
        ax.text(v + span * 0.012, y, f"{v:.3f}", va="center", fontsize=8.0,
                family="monospace", color=MUTE)
        ax.text(v + span * 0.075, y, f'score {f(r,"score_norm_mean"):.3f}', va="center",
                fontsize=7.6, family="monospace", color=MUTE)
    ax.set_xlim(0, span * 1.28)
    ax.set_xlabel(nice, fontsize=11)
    ax.set_title(f"{nice} — every run ranked", fontsize=14, fontweight="bold",
                 loc="left", pad=14)
    house_axes(ax)
    fig.tight_layout()
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"  {os.path.basename(out)}")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--components", default="analysis_four_panel/components_per_run.csv")
    p.add_argument("--validity", default="analysis/validity_per_run.csv")
    p.add_argument("--out", default="analysis/figs_validity")
    p.add_argument("--min-n", type=int, default=24)
    p.add_argument("--with-v1", action="store_true")
    a = p.parse_args()
    os.makedirs(a.out, exist_ok=True)

    v = {r["run"]: r for r in csv.DictReader(open(a.validity))}
    comp = read(a.components)
    if not a.with_v1:
        comp = [r for r in comp if r["_v"] != "v1"]
    if a.min_n:
        comp = [r for r in comp if int(r.get("n") or 0) >= a.min_n]
    rows = []
    for r in comp:
        d = v.get(r["dir"])
        if not d:
            continue
        m = dict(r)
        m.update({k: d[k] for k in
                  ("parse_ok_rate", "exec_rate", "fail_rate", "empty_round_rate",
                   "actions_requested", "episodes")})
        sc = f(r, "score_mean")
        m["score_norm_mean"] = str(sc / 4.0) if sc is not None else ""
        rows.append(m)
    assign_labels(rows)
    print(f"{len(rows)} runs with interface stats   ->  {a.out}\n")

    grid(rows, os.path.join(a.out, "interface_vs_score.png"))
    ranked(rows, "exec_rate", "Action execution rate",
           os.path.join(a.out, "rank_exec_rate.png"))
    ranked(rows, "parse_ok_rate", "Reply parse rate",
           os.path.join(a.out, "rank_parse_ok.png"))
    ranked(rows, "empty_round_rate", "Share of rounds with no action at all",
           os.path.join(a.out, "rank_empty_rounds.png"), descending=False)

    def rng(k):
        xs = [f(r, k) for r in rows if f(r, k) is not None]
        return min(xs), st.median(xs), max(xs)
    print("\n            min    median   max     r(score)")
    for k, nice, _ in METRICS:
        lo, md, hi = rng(k)
        r_ = corr([f(r, k) for r in rows], [f(r, "score_norm_mean") for r in rows])
        print(f"  {k:<17} {lo:.3f}  {md:.3f}  {hi:.3f}   {r_:+.3f}")


if __name__ == "__main__":
    main()
