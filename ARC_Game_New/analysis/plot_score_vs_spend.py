#!/usr/bin/env python3
"""Per component: the score earned AND the dollars spent to earn it, LLMs vs scripted policies.

The ranked charts in plot_components.py answer "who scored highest on food?" and, separately,
"who spent most on food?" -- but never both at once, so a run that buys its score and a run
that earns it look identical. Here every component gets:

  pair_<component>.png        two panels sharing one run order: satisfaction | dollars
  efficiency_<component>.png  dollars (x) against satisfaction (y) -- the frontier view

Scripted policies are included (see analysis/policy_baselines.py for where their dollars come
from) and drawn in a distinct colour, because they are the reference this whole benchmark is
measured against and they turn out to sit in a completely different region of the plane.

v1-PROMPT RUNS ARE EXCLUDED BY DEFAULT. The v1 prompt never states the Motel's ~$200 per
resident per day; v3 added it as "a recurring drain NOT shown on the choice". Lodging is
89-97% of every run's spend, so v1 agents were spending blind on the one line that dominates
the bill. Not a wrong number -- a missing one -- but it makes v1 spend incomparable. --with-v1
puts them back.

USAGE
  python analysis/plot_score_vs_spend.py                    # all v2/v3 runs + policies
  python analysis/plot_score_vs_spend.py --top 10
  python analysis/plot_score_vs_spend.py --with-v1 --no-policies
"""
import argparse, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle

from plot_components import (read, assign_labels, label, series, f, house_axes,
                             CI95, INK, MUTE, GRID, V3, V2, V1, RUST, TEAL_RAMP)
from policy_baselines import load_policies
from make_tables import tex_label, longtable, pm, CI_NOTE, check_tex

POLICY = "#5A4E8C"      # scripted policies -- deliberately outside the teal/rust family

# component -> (satisfaction key, spend key, human name)
PAIRS = [("sat_food", "food", "Food"),
         ("sat_lodging", "lodging", "Lodging"),
         ("sat_worker_use", "worker", "Worker use"),
         ("casework_processing_sat", "casework", "Casework"),
         ("score_norm", "total", "Overall")]


def colour(row):
    return POLICY if row["cfg"] == "scripted" else series(row)[0]


def legend_handles(rows):
    seen, out = [], []
    for r in rows:
        c = colour(r)
        lab = "scripted policy" if r["cfg"] == "scripted" else series(r)[1]
        if lab not in seen:
            seen.append(lab); out.append(Rectangle((0, 0), 1, 1, color=c, label=lab))
    order = ["on v3 (current prompt)", "on v2", "on v1 only — not comparable to v3",
             "Anthropic", "scripted policy"]
    out.sort(key=lambda h: order.index(h.get_label()) if h.get_label() in order else 9)
    return out


def pair_chart(rows, sat_key, spend_key, nice, out):
    """Satisfaction and dollars for one component, side by side, one shared run order."""
    data = [r for r in rows if f(r, f"{sat_key}_mean") is not None
            and f(r, f"{spend_key}_mean") is not None]
    if not data:
        print(f"  skip {os.path.basename(out)} — missing {sat_key} or {spend_key}")
        return
    data.sort(key=lambda r: f(r, f"{sat_key}_mean"))     # best at top after barh
    n = len(data)
    fig, (axl, axr) = plt.subplots(1, 2, sharey=True, dpi=150,
                                   figsize=(15.0, max(3.6, 0.30 * n + 2.8)))
    ys = list(range(n))
    cols = [colour(r) for r in data]

    sv = [f(r, f"{sat_key}_mean") for r in data]
    se = [(f(r, f"{sat_key}_sem") or 0) * CI95 for r in data]
    axl.barh(ys, sv, xerr=se, height=0.72, color=cols,
             error_kw=dict(ecolor=INK, lw=0.8, capsize=2.2))
    axl.set_title(f"{nice} — score earned", fontsize=12, fontweight="bold", pad=10)
    axl.set_xlabel("satisfaction component (mean, 95% CI)" if sat_key != "score_norm"
                   else "normalised score (mean, 95% CI)", fontsize=10)

    dv = [f(r, f"{spend_key}_mean") / 1000 for r in data]
    de = [(f(r, f"{spend_key}_sem") or 0) / 1000 * CI95 for r in data]
    axr.barh(ys, dv, xerr=de, height=0.72, color=cols,
             error_kw=dict(ecolor=INK, lw=0.8, capsize=2.2))
    axr.set_title(f"{nice} — dollars spent", fontsize=12, fontweight="bold", pad=10)
    axr.set_xlabel("$ thousands (mean, 95% CI)", fontsize=10)

    for ax, vals, errs, fmt in ((axl, sv, se, "{:.2f}"), (axr, dv, de, "{:.1f}k")):
        span = max(v + e for v, e in zip(vals, errs)) or 1.0
        for y, (v, e) in enumerate(zip(vals, errs)):
            ax.text(v + e + span * 0.02, y, fmt.format(v), va="center",
                    fontsize=7.6, family="monospace", color=MUTE)
        ax.set_xlim(0, span * 1.22)
        house_axes(ax)

    axl.set_yticks(ys)
    axl.set_yticklabels([label(r) for r in data], fontsize=7.8)
    axl.set_ylim(-0.9, n - 0.1)
    fig.suptitle(f"{nice}: score earned vs dollars spent   (runs ordered by score)",
                 fontsize=14, fontweight="bold", x=0.008, ha="left", y=0.997)
    h = legend_handles(data)
    fig.legend(handles=h, loc="lower center", ncol=len(h), frameon=False, fontsize=9.5,
               handlelength=1.1, bbox_to_anchor=(0.5, -0.55 / fig.get_figheight()))
    fig.tight_layout(rect=(0, 0, 1, 0.985))
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"  {os.path.basename(out):<34} {n} runs")


def efficiency_chart(rows, sat_key, spend_key, nice, out):
    """Dollars against satisfaction. Up-and-left is efficient; right is paying for nothing."""
    data = [r for r in rows if f(r, f"{sat_key}_mean") is not None
            and f(r, f"{spend_key}_mean") is not None]
    if not data:
        return
    fig, ax = plt.subplots(figsize=(11.0, 7.4), dpi=150)
    for r in data:
        x = f(r, f"{spend_key}_mean") / 1000
        y = f(r, f"{sat_key}_mean")
        pol = r["cfg"] == "scripted"
        ax.errorbar(x, y,
                    xerr=(f(r, f"{spend_key}_sem") or 0) / 1000 * CI95,
                    yerr=(f(r, f"{sat_key}_sem") or 0) * CI95,
                    fmt="D" if pol else "o", ms=9 if pol else 6,
                    color=colour(r), ecolor=GRID, elinewidth=0.8, capsize=0, zorder=3)
        if pol:
            ax.annotate(r["model"].replace("policy: ", ""), (x, y),
                        textcoords="offset points", xytext=(9, 4),
                        fontsize=8.5, color=POLICY, fontweight="bold")
    ax.set_xlabel(f"{nice.lower()} spend  —  $ thousands (mean, 95% CI)", fontsize=11)
    ax.set_ylabel("normalised score" if sat_key == "score_norm"
                  else f"{nice.lower()} satisfaction", fontsize=11)
    ax.set_title(f"{nice}: what each dollar buys",
                 fontsize=14, fontweight="bold", loc="left", pad=14)
    ax.yaxis.grid(True, linestyle=":", color=GRID, lw=0.9)
    house_axes(ax)
    h = legend_handles(data)
    ax.legend(handles=h, loc="lower right", frameon=False, fontsize=9)
    fig.tight_layout()
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"  {os.path.basename(out):<34} {len(data)} runs")


def pair_table(rows, sat_key, spend_key, nice, out):
    """The same pairing as pair_chart, plus dollars per unit of satisfaction earned."""
    data = [r for r in rows if f(r, f"{sat_key}_mean") is not None
            and f(r, f"{spend_key}_mean") is not None]
    if not data:
        return None
    data.sort(key=lambda r: -f(r, f"{sat_key}_mean"))
    body = []
    for i, r in enumerate(data, 1):
        sat = f(r, f"{sat_key}_mean")
        dol = f(r, f"{spend_key}_mean") / 1000.0
        # Undefined when nothing was earned -- noop scores 0 on every component.
        per = "--" if sat <= 0 else "$%.1f$" % (dol / sat)
        body.append([str(i), tex_label(r), str(r.get("n", "")),
                     pm(sat, f(r, f"{sat_key}_sem"), 3),
                     pm(f(r, f"{spend_key}_mean"), f(r, f"{spend_key}_sem"), 1, 1e-3),
                     per])
    with open(out, "w") as fh:
        longtable(fh, "@{}r>{\\raggedright\\arraybackslash}p{0.40\\linewidth}rrrr@{}",
                  "Rank & Configuration & $n$ & Score & Spend (\\$k) & \\$k per unit",
                  body,
                  "%s: score earned and dollars spent, ranked by score. The last column is "
                  "spend divided by score earned -- lower is more efficient." % nice,
                  "svs-%s" % spend_key, CI_NOTE, size="\\small")
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--components", default="analysis_four_panel/components_per_run.csv")
    p.add_argument("--spend", default="analysis_four_panel/spend_per_run.csv")
    p.add_argument("--out", default="analysis/figs_score_vs_spend")
    p.add_argument("--top", type=int, default=None)
    p.add_argument("--min-n", type=int, default=None)
    p.add_argument("--with-v1", action="store_true",
                   help="include v1-prompt runs (excluded by default: the v1 prompt never "
                        "states the Motel's per-resident daily cost)")
    p.add_argument("--no-policies", action="store_true")
    a = p.parse_args()
    os.makedirs(a.out, exist_ok=True)

    comp, spend = read(a.components), read(a.spend)
    if not a.with_v1:
        drop = {r["dir"] for r in comp if r["_v"] == "v1"}
        comp = [r for r in comp if r["dir"] not in drop]
        spend = [r for r in spend if r["dir"] not in drop]
        print(f"excluded {len(drop)} v1-prompt runs")
    if a.min_n:
        comp = [r for r in comp if int(r.get("n") or 0) >= a.min_n]
    if a.top:
        comp = sorted(comp, key=lambda r: -(f(r, "score_mean") or -9))[:a.top]
    keep = {r["dir"] for r in comp}
    spend = [r for r in spend if r["dir"] in keep]

    for r in comp:
        for suf in ("mean", "sem"):
            v = f(r, f"score_{suf}")
            if v is not None:
                r[f"score_norm_{suf}"] = str(v / 4.0)

    if not a.no_policies:
        pc, ps = load_policies()
        for r in pc:
            r["_v"] = "policy"
            for suf in ("mean", "sem"):
                v = f(r, f"score_{suf}")
                if v is not None:
                    r[f"score_norm_{suf}"] = str(v / 4.0)
        comp += pc
        spend += ps
        print(f"added {len(pc)} scripted policies")
    assign_labels(comp); assign_labels(spend)

    # One row per run carrying BOTH sides, joined on the run directory.
    by_dir = {r["dir"]: r for r in spend}
    merged = []
    for r in comp:
        s = by_dir.get(r["dir"])
        if s:
            m = dict(r)
            m.update({k: v for k, v in s.items() if k.endswith(("_mean", "_sem"))})
            m["_label"] = r.get("_label")
            merged.append(m)
    print(f"{len(merged)} runs with both score and spend   ->  {a.out}\n")

    written = []
    for sat_key, spend_key, nice in PAIRS:
        pair_chart(merged, sat_key, spend_key, nice,
                   os.path.join(a.out, f"pair_{spend_key}.png"))
        efficiency_chart(merged, sat_key, spend_key, nice,
                         os.path.join(a.out, f"efficiency_{spend_key}.png"))
        t = pair_table(merged, sat_key, spend_key, nice,
                       os.path.join(a.out, f"tab_score_vs_spend_{spend_key}.tex"))
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
