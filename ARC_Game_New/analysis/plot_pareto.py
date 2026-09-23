#!/usr/bin/env python3
"""Pareto frontier over the benchmark runs: score earned against dollars spent.

BENCHMARK RUNS ONLY. No scripted policies -- they are excluded deliberately, not by
oversight: the current policy sweep is not trusted, so mixing it in would put unvalidated
points on the frontier and change which models appear to be non-dominated.

v1-prompt runs are excluded by default for the same reason they are everywhere else: the v1
prompt never states the Motel's ~$200 per-resident-per-day charge, and lodging is 89-97%% of
all spend, so v1 spend is not comparable. --with-v1 restores them.

A run is on the frontier when no other run both scores at least as high AND spends no more,
with at least one of those strict. Frontier members are drawn solid and labelled; dominated
runs are faded, so the question "which models sit on the frontier" reads directly off the page.

USAGE
  python analysis/plot_pareto.py                       # all components -> analysis/figs_pareto
  python analysis/plot_pareto.py --min-n 24            # only well-sampled runs
"""
import argparse, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle

from plot_components import (read, assign_labels, label, series, f, house_axes,
                             CI95, INK, MUTE, GRID)
from make_tables import tex_label, longtable, pm, CI_NOTE, check_tex

# Default figure is 12.4x8.0in (1.55:1). A 16:9 slide with a caption band underneath leaves
# a 9.4x3.96in hole, so a 1.55:1 figure is height-limited to 6.15in wide -- scaled down to
# 0.50x, which renders the 8.2pt point labels at ~4pt on the slide. --slide renders at the
# hole's own aspect instead, so the figure lands on the slide at 1:1 and the labels keep
# their real point size.
FIGSIZE       = (12.4, 8.0)
FIGSIZE_SLIDE = (9.4, 4.70)   # 2:1 -- wide enough to fill most of the slide, tall
                              # enough that the label packer still has free rows to use

PAIRS = [("score_norm", "total", "Overall score"),
         ("sat_food", "food", "Food"),
         ("sat_lodging", "lodging", "Lodging"),
         ("sat_worker_use", "worker", "Worker use"),
         ("casework_processing_sat", "casework", "Casework")]


def frontier(points):
    """points: [(spend, score, row)] -> the non-dominated subset, sorted by spend ascending.

    Maximise score, minimise spend. Ties on both axes keep every tied run (none of them is
    strictly dominated), which matters here because several configs land on identical spend.
    """
    out = []
    for sp, sc, r in points:
        dominated = any(
            (osp <= sp and osc >= sc) and (osp < sp or osc > sc)
            for osp, osc, _ in points)
        if not dominated:
            out.append((sp, sc, r))
    return sorted(out, key=lambda t: t[0])


def pareto_chart(rows, sat_key, spend_key, nice, out):
    pts = [(f(r, f"{spend_key}_mean") / 1000.0, f(r, f"{sat_key}_mean"), r)
           for r in rows
           if f(r, f"{spend_key}_mean") is not None and f(r, f"{sat_key}_mean") is not None]
    if not pts:
        print(f"  skip {os.path.basename(out)} — no data")
        return []
    front = frontier(pts)
    onfront = {id(r) for _, _, r in front}

    fig, ax = plt.subplots(figsize=FIGSIZE, dpi=150)

    # Staircase: from any frontier point you can only improve by paying more.
    if len(front) > 1:
        sx, sy = [], []
        for i, (sp, sc, _) in enumerate(front):
            if i:
                sx.append(sp); sy.append(front[i - 1][1])
            sx.append(sp); sy.append(sc)
        ax.plot(sx, sy, color=INK, lw=1.3, linestyle="--", zorder=2, alpha=0.55)

    for sp, sc, r in pts:
        on = id(r) in onfront
        ax.errorbar(sp, sc,
                    xerr=(f(r, f"{spend_key}_sem") or 0) / 1000 * CI95,
                    yerr=(f(r, f"{sat_key}_sem") or 0) * CI95,
                    fmt="o", ms=9 if on else 5.5, color=series(r)[0],
                    alpha=1.0 if on else 0.30,
                    ecolor=GRID, elinewidth=0.8, capsize=0,
                    zorder=4 if on else 1)
    # Frontier points cluster tightly around the knee -- often a few pixels apart -- while the
    # labels are 30-50 characters wide, so a fixed offset overprints them. Stacking them in
    # OFFSET space does not work either: two points at different heights with different rungs
    # of the same ladder can still land on the same screen row. So collision detection happens
    # in screen space. Each label reserves a rectangle; the next one takes the first candidate
    # offset whose rectangle is clear. Candidates alternate above/below and widen outward, so
    # an isolated point keeps a label right next to its marker and only clusters fan out.
    xmax = max(sp for sp, _, _ in pts)
    x0, x1 = ax.get_xlim()
    y0, y1 = ax.get_ylim()
    # Positions are in points, so the data axes have to be measured in points too. 0.5 em per
    # character is the usual approximation for a proportional sans face; the axes occupy
    # roughly 78% of the figure width and 70% of its height once tight_layout has run.
    w_in, h_in = fig.get_size_inches()
    pts_per_x = (w_in * 72 * 0.78) / max(x1 - x0, 1e-9)
    pts_per_y = (h_in * 72 * 0.70) / max(y1 - y0, 1e-9)
    CAND = [9, -15, 22, -28, 35, -41, 48, -54, 61, -67]
    h_pts = h_in * 72 * 0.70          # usable height of the axes, same approximation
    placed = []        # (left, right, bottom, top), all in points
    for sp, sc, r in sorted(front, key=lambda t: (t[0], t[1])):
        text = label(r).replace("  ·  ", " · ")
        right = sp > 0.72 * xmax
        w = len(text) * 8.2 * 0.5
        ax_x, ax_y = (sp - x0) * pts_per_x, (sc - y0) * pts_per_y
        left, rt = (ax_x - w - 10, ax_x - 10) if right else (ax_x + 10, ax_x + 10 + w)
        box = None
        for dy in CAND:
            cand = (left - 4, rt + 4, ax_y + dy - 6, ax_y + dy + 7)
            # A candidate that leaves the axes is not usable -- above the top it prints over
            # the title, which is worse than the overlap it was avoiding.
            if not (0 <= cand[2] and cand[3] <= h_pts):
                continue
            if all(cand[1] < a or cand[0] > b or cand[3] < c or cand[2] > d
                   for a, b, c, d in placed):
                box = cand
                break
        if box is None:                # nothing clear and in-bounds: take the tightest slot
            dy = CAND[0]
            box = (left - 4, rt + 4, ax_y + dy - 6, ax_y + dy + 7)
        placed.append(box)
        # A label pushed well clear of its marker to avoid a collision needs a leader line,
        # or the reader attaches it to whichever dot it happens to have landed next to.
        arrow = (dict(arrowstyle="-", color=GRID, lw=0.7, shrinkA=0, shrinkB=3)
                 if abs(dy) > 20 else None)
        ax.annotate(text, (sp, sc),
                    textcoords="offset points", xytext=(-10 if right else 10, dy),
                    ha="right" if right else "left",
                    fontsize=8.2, color=INK, zorder=5, arrowprops=arrow,
                    bbox=dict(boxstyle="round,pad=0.18", fc="white", ec="none", alpha=0.78))
    ax.set_xlabel(f"{nice.lower()} spend  —  $ thousands (mean, 95% CI)", fontsize=11)
    ax.set_ylabel("normalised score" if sat_key == "score_norm"
                  else f"{nice.lower()} satisfaction", fontsize=11)
    ax.set_title(f"{nice}: Pareto frontier — {len(front)} of {len(pts)} runs non-dominated",
                 fontsize=14, fontweight="bold", loc="left", pad=14)
    ax.yaxis.grid(True, linestyle=":", color=GRID, lw=0.9)
    house_axes(ax)
    ax.margins(x=0.13, y=0.07)

    seen, handles = [], []
    for _, _, r in pts:
        c, lab = series(r)
        if lab not in seen:
            seen.append(lab); handles.append(Rectangle((0, 0), 1, 1, color=c, label=lab))
    handles.append(plt.Line2D([], [], color=INK, lw=1.3, ls="--", alpha=0.55,
                              label="Pareto frontier"))
    ax.legend(handles=handles, loc="lower right", frameon=False, fontsize=9)
    fig.tight_layout()
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"  {os.path.basename(out):<28} {len(front):>2} on frontier / {len(pts)} runs")
    return front


def pareto_table(front, sat_key, spend_key, nice, out):
    if not front:
        return None
    body = []
    for i, (sp, sc, r) in enumerate(sorted(front, key=lambda t: -t[1]), 1):
        body.append([str(i), tex_label(r), str(r.get("n", "")),
                     pm(sc, f(r, f"{sat_key}_sem"), 3),
                     pm(f(r, f"{spend_key}_mean"), f(r, f"{spend_key}_sem"), 1, 1e-3),
                     "--" if sc <= 0 else "$%.0f$" % (sp / sc)])
    with open(out, "w") as fh:
        longtable(fh, "@{}r>{\\raggedright\\arraybackslash}p{0.42\\linewidth}rrrr@{}",
                  "Rank & Configuration & $n$ & Score & Spend (\\$k) & \\$k per unit",
                  body,
                  "%s: the Pareto-non-dominated benchmark runs (no scripted policies), "
                  "ranked by score. No other run scores at least as high for no more money."
                  % nice,
                  "pareto-%s" % spend_key, CI_NOTE, size="\\small")
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--components", default="analysis_four_panel/components_per_run.csv")
    p.add_argument("--spend", default="analysis_four_panel/spend_per_run.csv")
    p.add_argument("--out", default="analysis/figs_pareto")
    p.add_argument("--min-n", type=int, default=None)
    p.add_argument("--with-v1", action="store_true")
    p.add_argument("--slide", action="store_true",
                   help="render at 16:9-slide aspect so the figure fills the slide 1:1")
    a = p.parse_args()
    os.makedirs(a.out, exist_ok=True)
    if a.slide:
        global FIGSIZE
        FIGSIZE = FIGSIZE_SLIDE

    comp, spend = read(a.components), read(a.spend)
    if not a.with_v1:
        drop = {r["dir"] for r in comp if r["_v"] == "v1"}
        comp = [r for r in comp if r["dir"] not in drop]
        spend = [r for r in spend if r["dir"] not in drop]
        print(f"excluded {len(drop)} v1-prompt runs")
    if a.min_n:
        before = len(comp)
        comp = [r for r in comp if int(r.get("n") or 0) >= a.min_n]
        print(f"excluded {before - len(comp)} runs with n < {a.min_n}")
    keep = {r["dir"] for r in comp}
    spend = [r for r in spend if r["dir"] in keep]

    for r in comp:
        for suf in ("mean", "sem"):
            v = f(r, f"score_{suf}")
            if v is not None:
                r[f"score_norm_{suf}"] = str(v / 4.0)
    assign_labels(comp); assign_labels(spend)

    by_dir = {r["dir"]: r for r in spend}
    merged = []
    for r in comp:
        s = by_dir.get(r["dir"])
        if s:
            m = dict(r)
            m.update({k: v for k, v in s.items() if k.endswith(("_mean", "_sem"))})
            m["_label"] = r.get("_label")
            merged.append(m)
    print(f"{len(merged)} benchmark runs (no policies)   ->  {a.out}\n")

    written = []
    for sat_key, spend_key, nice in PAIRS:
        fr = pareto_chart(merged, sat_key, spend_key, nice,
                          os.path.join(a.out, f"pareto_{spend_key}.png"))
        t = pareto_table(fr, sat_key, spend_key, nice,
                         os.path.join(a.out, f"tab_pareto_{spend_key}.tex"))
        if t:
            written.append(t)
    with open(os.path.join(a.out, "tables.tex"), "w") as fh:
        fh.write("% \\usepackage{longtable,booktabs,textcomp,array}\n")
        for w in written:
            fh.write("\\input{%s}\n" % os.path.splitext(os.path.basename(w))[0])
    bad = sum(len(check_tex(w)) for w in written)
    print(f"\n{len(written)} tables   escape check: "
          + ("clean" if not bad else f"{bad} problems"))


if __name__ == "__main__":
    main()
