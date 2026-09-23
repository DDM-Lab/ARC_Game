#!/usr/bin/env python3
"""Per-component ranked charts for the CORA benchmark, in the house figure style.

WHY THIS EXISTS
`components_and_spend.png` puts every spend category on one shared axis. Lodging runs
$200-330k while food is ~$5k and worker/casework ~$2k, so three of the four categories
render as slivers: unreadable, and impossible to rank. Every component here gets its OWN
graph with its OWN axis, plus a share-of-spend view where lodging cannot dominate by
construction.

Style is matched to analysis_assets_v2/fig_all_models.png and fig_score_breakdown.png
(palette sampled from their pixels): teal ramp by prompt version, rust for Anthropic,
grey for v1-only runs, dotted x-grid, monospace value + n annotations, no top/right spine.

USAGE
  python analysis/plot_components.py                      # defaults below
  python analysis/plot_components.py --components X.csv --spend Y.csv --out DIR
  python analysis/plot_components.py --by-model           # collapse to best cfg per model
Works on any future CSV with the same columns; nothing is keyed to today's run names.
"""
import argparse, csv, json, os, re

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle

# ── palette, sampled from the existing figures ────────────────────────────────────────
V3    = "#2C807D"   # runs on the current (v3) prompt
V2    = "#5FA9A4"   # runs on v2
V1    = "#A7BCC5"   # v1-only runs — not score-comparable to v3
RUST  = "#C07048"   # Anthropic models
INK   = "#101820"
MUTE  = "#5A6B78"
GRID  = "#D7DEE2"
SPINE = "#B9C4CB"
CI95 = 1.96   # matches the deck: ci95 == 1.96 * SEM, verified to 4 dp
TEAL_RAMP  = ["#0E6E6B", "#2E8B84", "#7FBFB8", "#B9DBD7"]
RUST_RAMP  = ["#B85C2E", "#C97C4E", "#DBA47E", "#E8CDB4"]

plt.rcParams.update({
    "font.family": "sans-serif",
    "axes.edgecolor": SPINE,
    "text.color": INK,
})


# ── row helpers ───────────────────────────────────────────────────────────────────────
def prompt_version(cfg):
    """cfg looks like 'min_v3·H1·t·low'; a bare 'min·...' prefix is the original v1 prompt."""
    m = re.match(r"^[a-z]+(?:_(v\d))?", cfg or "")
    return (m.group(1) if m and m.group(1) else "v1")


def series(row):
    """(colour, legend label) — prompt version, for every run.

    Colour used to mean prompt version for open-weight runs but vendor for Anthropic ones,
    which put "on v2 / on v3 / Anthropic" in one legend as if they were three comparable
    categories. They are not: an Anthropic bar's colour said nothing about which prompt it
    ran. One axis only now. Vendor is still on every bar's row label, and RUST stays in use
    for the cost segments of the score decomposition.
    """
    return {"v3": (V3, "on v3 (current prompt)"),
            "v2": (V2, "on v2"),
            "v1": (V1, "on v1 only — not comparable to v3")}[prompt_version(row["cfg"])]


_DATES = None


def release_date(model):
    """YYYY-MM for a model, or '' if unknown. Table: analysis/model_release_dates.json."""
    global _DATES
    if _DATES is None:
        try:
            path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "model_release_dates.json")
            _DATES = {k: v for k, v in json.load(open(path)).items()
                      if isinstance(v, dict) and v.get("date")}
        except Exception:
            _DATES = {}
    d = _DATES.get(short(model), {}).get("date", "")
    return d[:7] if d else ""


def short(model):
    """Qwen/Qwen3.8-27B -> Qwen3.8-27B ; claude-haiku-4-5-20251001 -> claude-haiku-4-5"""
    return re.sub(r"-\d{8}$", "", (model or "").split("/")[-1])


def run_tag(d):
    """cluster_api_v3think2000_haiku45 -> v3think2000 ; cluster_local_bf3_Qwen3.5-27B -> bf3

    The part of the run directory that is neither the harness prefix nor the model name is
    the only place settings like the thinking budget survive -- cfg does not encode them.
    """
    t = re.sub(r"^cluster_(api|local)_", "", d or "")
    return re.sub(r"_(Qwen[\w.\-]+|haiku45|sonnet5|gpt-oss[\w\-]*)$", "", t) or t


def assign_labels(rows):
    """model · cfg, plus the run tag ONLY where that pair would otherwise collide.

    16 of 49 (model, cfg) pairs cover more than one run -- e.g. all four Haiku thinking-budget
    sweeps render as 'claude-haiku-4-5 · min_v3·H1·t·low'. Un-disambiguated they are four
    indistinguishable bars.
    """
    groups = {}
    for r in rows:
        groups.setdefault((r["model"], r["cfg"]), []).append(r)
    for (model, cfg), grp in groups.items():
        for r in grp:
            rd = release_date(model)
            base = f"{short(model)}{f' ({rd})' if rd else ''}  ·  {cfg}"
            r["_label"] = f'{base}  ·  {run_tag(r["dir"])}' if len(grp) > 1 else base
    return rows


def label(row):
    return row.get("_label") or f'{short(row["model"])}  ·  {row["cfg"]}'


def f(row, key):
    try:
        return float(row[key])
    except (TypeError, ValueError, KeyError):
        return None


def read(path):
    with open(path) as fh:
        rows = [r for r in csv.DictReader(fh) if r.get("model")]
    for r in rows:
        r["_v"] = prompt_version(r["cfg"])
    return assign_labels(rows)


def best_per_model(rows, key):
    """Collapse to the single best-scoring configuration per model (what fig_all_models shows)."""
    best = {}
    for r in rows:
        v = f(r, key)
        if v is None:
            continue
        cur = best.get(r["model"])
        if cur is None or v > f(cur, key):
            best[r["model"]] = r
    return list(best.values())


def house_axes(ax):
    ax.xaxis.grid(True, linestyle=":", color=GRID, lw=0.9)
    ax.set_axisbelow(True)
    for s in ("top", "right"):
        ax.spines[s].set_visible(False)
    for s in ("left", "bottom"):
        ax.spines[s].set_color(SPINE)
    ax.tick_params(axis="x", colors=MUTE, labelsize=9)
    ax.tick_params(axis="y", colors=INK, length=3, color=SPINE)


def legend_for(ax, rows, loc="lower right", anchor=None, ncol=1):
    seen, handles = [], []
    for r in rows:
        c, lab = series(r)
        if lab not in seen:
            seen.append(lab)
            handles.append(Rectangle((0, 0), 1, 1, color=c, label=lab))
    order = ["on v3 (current prompt)", "on v2", "on v1 only — not comparable to v3", "Anthropic"]
    handles.sort(key=lambda h: order.index(h.get_label()) if h.get_label() in order else 9)
    if len(handles) > 1:
        ax.legend(handles=handles, loc=loc, frameon=False, fontsize=9,
                  handlelength=1.1, handleheight=1.0, labelspacing=0.55,
                  ncol=ncol, bbox_to_anchor=anchor)


# ── the ranked chart ──────────────────────────────────────────────────────────────────
def ranked(rows, mean_key, sem_key, title, xlabel, out,
           fmt="{:.3f}", descending=True, ref=None, ref_label=None):
    data = [(r, f(r, mean_key), (f(r, sem_key) or 0.0) * CI95) for r in rows]
    data = [d for d in data if d[1] is not None]
    if not data:
        print(f"  skip {os.path.basename(out)} — no data for {mean_key}")
        return
    data.sort(key=lambda t: t[1], reverse=descending)
    data.reverse()                       # best row ends up at the top

    n = len(data)
    fig, ax = plt.subplots(figsize=(13.0, max(3.2, 0.285 * n + 2.4)), dpi=150)
    ys = list(range(n))
    vals = [d[1] for d in data]
    errs = [d[2] for d in data]

    ax.barh(ys, vals, xerr=errs, height=0.70,
            color=[series(d[0])[0] for d in data],
            error_kw=dict(ecolor=INK, lw=0.9, capsize=2.6))

    ax.set_yticks(ys)
    ax.set_yticklabels([label(d[0]) for d in data], fontsize=8.0)
    ax.set_ylim(-0.9, n - 0.1)

    lo = min(0.0, min(v - e for v, e in zip(vals, errs)))
    hi = max(v + e for v, e in zip(vals, errs))
    if ref is not None:
        hi = max(hi, ref)
    span = (hi - lo) or 1.0
    # Annotations track each bar's end at fixed offsets, as in fig_all_models.
    for y, (r, v, e) in zip(ys, data):
        x = max(v + e, 0) + span * 0.015
        ax.text(x, y, fmt.format(v), va="center", ha="left",
                fontsize=8.2, family="monospace", color=MUTE)
        ax.text(x + span * 0.085, y, r["_v"], va="center", ha="left",
                fontsize=8.2, family="monospace", color=MUTE)
        ax.text(x + span * 0.125, y, f'n={r.get("n","?")}', va="center", ha="left",
                fontsize=8.2, family="monospace", color=MUTE)

    ax.set_xlim(lo - span * 0.02, hi + span * 0.30)
    if lo < 0:
        ax.axvline(0, color=INK, lw=1.0)
    if ref is not None:
        ax.axvline(ref, color=TEAL_RAMP[0], lw=1.6, linestyle="--")
        ax.text(ref + span * 0.006, n - 0.35, ref_label or f"{ref:.3f}",
                color=TEAL_RAMP[0], fontsize=9, va="bottom")

    ax.set_xlabel(xlabel, fontsize=11)
    ax.set_title(title, fontsize=14, fontweight="bold", loc="left", pad=14)
    house_axes(ax)
    # Rows are reversed so rank 1 is at the top: descending -> long bars at the top,
    # ascending -> long bars at the bottom. Park the legend at the short end either way.
    # Below ~18 rows there is no empty corner left, so the legend goes under the figure --
    # as a FIGURE legend added after tight_layout, because an out-of-axes axes-legend makes
    # tight_layout shrink the axes to nothing trying to fit it.
    if n > 18:
        legend_for(ax, [d[0] for d in data],
                   loc="lower right" if descending else "upper right")
    fig.tight_layout()
    if n <= 18:
        seen, handles = [], []
        for r, _, _ in data:
            c, lab = series(r)
            if lab not in seen:
                seen.append(lab); handles.append(Rectangle((0, 0), 1, 1, color=c, label=lab))
        if len(handles) > 1:
            fig.legend(handles=handles, loc="lower center", ncol=len(handles),
                       frameon=False, fontsize=9, handlelength=1.1,
                       bbox_to_anchor=(0.5, -0.55 / fig.get_figheight()))
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"  {os.path.basename(out):<38} {n} rows")


# ── small multiples: every spend category on its own axis, one shared run order ────────
def spend_small_multiples(rows, out, order_key="total_mean"):
    cats = [("food", "food"), ("lodging", "lodging"),
            ("worker", "worker"), ("casework", "casework")]
    data = sorted([r for r in rows if f(r, order_key) is not None],
                  key=lambda r: f(r, order_key))
    n = len(data)
    fig, axes = plt.subplots(1, len(cats), sharey=True, dpi=150,
                             figsize=(15.5, max(3.6, 0.285 * n + 2.6)))
    ys = list(range(n))
    for ax, (key, nice) in zip(axes, cats):
        vals = [(f(r, f"{key}_mean") or 0) / 1000.0 for r in data]
        errs = [(f(r, f"{key}_sem") or 0) / 1000.0 * CI95 for r in data]
        ax.barh(ys, vals, xerr=errs, height=0.70,
                color=[series(r)[0] for r in data],
                error_kw=dict(ecolor=INK, lw=0.7, capsize=1.8))
        ax.set_title(nice, fontsize=11, fontweight="bold", pad=8)
        ax.set_xlabel("$k", fontsize=9.5, color=MUTE)
        ax.set_xlim(0, max(v + e for v, e in zip(vals, errs)) * 1.10)
        house_axes(ax)
    axes[0].set_yticks(ys)
    axes[0].set_yticklabels([label(r) for r in data], fontsize=8.0)
    axes[0].set_ylim(-0.9, n - 0.1)
    fig.suptitle("Spend by category — each category on its own axis"
                 "   (runs ordered by total spend)",
                 fontsize=14, fontweight="bold", x=0.008, ha="left", y=0.995)
    # Figure-level legend below the panels; inside the last axes it sat on top of bars.
    seen, handles = [], []
    for r in data:
        c, lab = series(r)
        if lab not in seen:
            seen.append(lab); handles.append(Rectangle((0, 0), 1, 1, color=c, label=lab))
    fig.legend(handles=handles, loc="lower center", ncol=len(handles), frameon=False,
               fontsize=9.5, handlelength=1.1,
               bbox_to_anchor=(0.5, -0.9 / fig.get_figheight()))
    fig.tight_layout(rect=(0, 0, 1, 0.985))
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"  {os.path.basename(out):<38} {n} rows x {len(cats)} panels")


# ── share of spend: normalised, so lodging cannot eclipse anything ────────────────────
def spend_share(rows, out):
    cats = [("food", "food"), ("lodging", "lodging"),
            ("worker", "worker"), ("casework", "casework")]
    data = []
    for r in rows:
        parts = [f(r, f"{k}_mean") or 0.0 for k, _ in cats]
        tot = sum(parts)
        if tot > 0:
            data.append((r, [p / tot for p in parts], tot))
    data.sort(key=lambda t: t[1][1])          # by lodging share
    n = len(data)
    fig, ax = plt.subplots(figsize=(13.6, max(3.2, 0.285 * n + 2.6)), dpi=150)
    ys = list(range(n))
    left = [0.0] * n
    for i, (key, nice) in enumerate(cats):
        w = [d[1][i] for d in data]
        ax.barh(ys, w, left=left, height=0.72, color=TEAL_RAMP[i], label=nice)
        for y, (x0, wi) in enumerate(zip(left, w)):
            if wi >= 0.045:
                ax.text(x0 + wi / 2, y, f"{wi*100:.0f}%", va="center", ha="center",
                        fontsize=7.6, family="monospace",
                        color="white" if i < 2 else INK)
        left = [a + b for a, b in zip(left, w)]

    ax.set_yticks(ys)
    ax.set_yticklabels([label(d[0]) for d in data], fontsize=8.0)
    ax.set_ylim(-0.9, n - 0.1)
    ax.set_xlim(0, 1.14)
    ax.set_xticks([0, .2, .4, .6, .8, 1.0])
    ax.set_xticklabels(["0%", "20%", "40%", "60%", "80%", "100%"])
    for y, d in enumerate(data):
        ax.text(1.02, y, f"${d[2]/1000:6.1f}k", va="center", ha="left",
                fontsize=8.2, family="monospace", color=MUTE)
    ax.text(1.02, n - 0.35, "total", va="bottom", ha="left", fontsize=8.6, color=MUTE)
    ax.set_xlabel("share of total spend   ·   trailing figure is that run's total spend",
                  fontsize=11)
    ax.set_title("Where the money goes — spend as a share, so no category is eclipsed",
                 fontsize=14, fontweight="bold", loc="left", pad=14)
    house_axes(ax)
    # An in-axes legend anchored in axes coordinates lands on top of the x-label as soon as
    # the row count is small (at n=10 the anchor sits at y=-0.14, exactly where the label is).
    # Same fix as the small multiples: a FIGURE legend placed below, after tight_layout, so
    # it clears the label at any n instead of only at the row count it was tuned on.
    handles = [Rectangle((0, 0), 1, 1, color=TEAL_RAMP[i], label=nice)
               for i, (_, nice) in enumerate(cats)]
    fig.tight_layout()
    fig.legend(handles=handles, loc="lower center", ncol=len(handles), frameon=False,
               fontsize=9.5, handlelength=1.1,
               bbox_to_anchor=(0.5, -0.62 / fig.get_figheight()))
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"  {os.path.basename(out):<38} {n} rows")


# ── score decomposition per run, in the fig_score_breakdown style ─────────────────────
def score_breakdown(rows, out):
    POS = [("sat_food", "+ food"), ("sat_lodging", "+ lodging"),
           ("sat_worker_use", "+ worker use"), ("casework_processing_sat", "+ casework")]
    NEG = [("cost_food", "− food cost"), ("cost_lodging", "− lodging cost"),
           ("cost_worker", "− worker cost"), ("casework_efficiency", "− casework cost")]
    data = sorted([r for r in rows if f(r, "score_mean") is not None],
                  key=lambda r: f(r, "score_mean"))
    n = len(data)
    fig, ax = plt.subplots(figsize=(14.5, max(3.2, 0.30 * n + 3.0)), dpi=150)
    ys = list(range(n))

    # Components are normalised by 4 so the stacked bar sums to the reported score.
    for side, keys, ramp, sign in (("pos", POS, TEAL_RAMP, 1.0), ("neg", NEG, RUST_RAMP, -1.0)):
        left = [0.0] * n
        for i, (key, nice) in enumerate(keys):
            w = [abs(f(r, f"{key}_mean") or 0.0) / 4.0 * sign for r in data]
            ax.barh(ys, w, left=left, height=0.72, color=ramp[i], label=nice)
            for y, (x0, wi, r) in enumerate(zip(left, w, data)):
                raw = f(r, f"{key}_mean")
                if abs(wi) >= 0.028 and raw is not None:
                    ax.text(x0 + wi / 2, y, f"{abs(raw):.2f}", va="center", ha="center",
                            fontsize=7.2, color="white" if i < 2 else INK)
            left = [a + b for a, b in zip(left, w)]

    ax.set_yticks(ys)
    ax.set_yticklabels([f'{label(r)}  ({f(r,"score_mean")/4:.3f})' for r in data], fontsize=8.0)
    ax.set_ylim(-0.9, n - 0.1)
    ax.axvline(0, color=INK, lw=1.1)
    xr = ax.get_xlim()
    ax.set_xlim(xr[0], xr[1] + (xr[1] - xr[0]) * 0.10)
    for y, r in enumerate(data):
        pos = sum(abs(f(r, f"{k}_mean") or 0) for k, _ in POS) / 4.0
        ax.text(pos + (xr[1] - xr[0]) * 0.02, y, f'= {f(r,"score_mean")/4:.3f}',
                va="center", ha="left", fontsize=8.2, family="monospace", fontweight="bold")
    ax.set_xlabel("normalised score contribution (component / 4)"
                  "   ·   in-bar figures are raw component values", fontsize=11)
    ax.set_title(f"Score decomposition — {SCOPE}", fontsize=14, fontweight="bold", loc="left", pad=14)
    house_axes(ax)
    ax.legend(loc="upper center", bbox_to_anchor=(0.5, -0.055 - 1.6 / max(n, 8)),
              ncol=4, frameon=False, fontsize=9.5, handlelength=1.1)
    fig.tight_layout()
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"  {os.path.basename(out):<38} {n} rows")


# ── driver ────────────────────────────────────────────────────────────────────────────
SAT = [("sat_food", "Food satisfaction"),
       ("sat_lodging", "Lodging satisfaction"),
       ("sat_worker_use", "Worker-use satisfaction"),
       ("casework_processing_sat", "Casework satisfaction"),
       ("satisfaction", "Total satisfaction")]
COST = [("cost_food", "Food cost penalty"),
        ("cost_lodging", "Lodging cost penalty"),
        ("cost_worker", "Worker cost penalty"),
        ("casework_efficiency", "Casework cost penalty"),
        ("cost_efficiency", "Total cost penalty")]
SCOPE = "every run ranked"

SPEND = [("food", "Food spend"), ("lodging", "Lodging spend"),
         ("worker", "Worker spend"), ("casework", "Casework spend"),
         ("total", "Total spend")]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--components", default="analysis_four_panel/components_per_run.csv")
    p.add_argument("--spend", default="analysis_four_panel/spend_per_run.csv")
    p.add_argument("--out", default="analysis/figs")
    p.add_argument("--ref", type=float, default=0.7901,
                   help="scripted-policy reference (measured build-potential; the "
                        "deck rounds this to 0.800)")
    p.add_argument("--by-model", action="store_true",
                   help="collapse to the best-scoring configuration per model")
    p.add_argument("--top", type=int, default=None,
                   help="keep only the N highest-scoring runs (ranking is by score, so the "
                        "same N runs appear in every component chart and stay comparable)")
    p.add_argument("--min-n", type=int, default=None,
                   help="drop runs with fewer than N episodes; the deck's top-10 uses 24")
    p.add_argument("--with-v1", action="store_true",
                   help="include v1-prompt runs (excluded by default: the v1 prompt never states the "
                        "Motel per-resident daily cost, which drives ~95%% of spend)")
    a = p.parse_args()
    os.makedirs(a.out, exist_ok=True)
    o = lambda name: os.path.join(a.out, name)
    # Titles must say what the rows ARE, so the two output sets are never confused.
    global SCOPE
    if a.top:
        SCOPE = f"top {a.top} runs by score"
    elif a.by_model:
        SCOPE = "best configuration per model"
    else:
        SCOPE = "every run ranked"
    if a.min_n:
        SCOPE += f", $n\\geq${a.min_n}".replace("$n\\geq$", "n\u2265")

    comp, spend = read(a.components), read(a.spend)
    if not a.with_v1:
        drop = {r['dir'] for r in comp if r['_v'] == 'v1'}
        comp = [r for r in comp if r['dir'] not in drop]
        spend = [r for r in spend if r['dir'] not in drop]
        print(f'excluded {len(drop)} v1-prompt runs')
    # Selection happens ONCE, on score, and is then applied to both CSVs. Selecting per
    # component would put a different set of runs in every chart and make them unreadable
    # side by side.
    if a.min_n:
        comp = [r for r in comp if int(r.get("n") or 0) >= a.min_n]
    if a.by_model:
        comp = best_per_model(comp, "score_mean")
    if a.top:
        comp = sorted(comp, key=lambda r: -(f(r, "score_mean") or -9))[:a.top]
    if a.by_model or a.top or a.min_n:
        keep = {(r["dir"], r["model"], r["cfg"]) for r in comp}
        spend = [r for r in spend if (r["dir"], r["model"], r["cfg"]) in keep]
    print(f"components: {len(comp)} runs   spend: {len(spend)} runs   ->  {a.out}")

    # score_mean in the CSV is the RAW cumulative score; every published figure reports it
    # normalised by the 4 components, which is also the scale the scripted-policy
    # reference (0.790) lives on. Normalise here so the two are directly comparable.
    for r in comp:
        for suf in ("mean", "sem"):
            v = f(r, f"score_{suf}")
            if v is not None:
                r[f"score_norm_{suf}"] = str(v / 4.0)

    print("\nscore")
    ranked(comp, "score_norm_mean", "score_norm_sem", f"Net score — {SCOPE}",
           "normalised score  =  cumulative score / 4   (mean, 95% CI)",
           o("rank_score.png"), ref=a.ref,
           ref_label=f"best scripted policy {a.ref:.3f}")

    print("\nsatisfaction components (higher is better)")
    for k, nice in SAT:
        ranked(comp, f"{k}_mean", f"{k}_sem", f"{nice} — {SCOPE}",
               f"{nice.lower()}  (mean, 95% CI)", o(f"rank_sat_{k}.png"))

    print("\ncost components (lower penalty is better)")
    for k, nice in COST:
        ranked(comp, f"{k}_mean", f"{k}_sem",
               f"{nice} — {SCOPE}, least penalty first",
               f"{nice.lower()}  (mean, 95% CI; closer to 0 is better)",
               o(f"rank_cost_{k}.png"), descending=False)

    print("\nspend, each category on its own axis")
    for k, nice in SPEND:
        for r in spend:
            for suf in ("mean", "sem"):
                v = f(r, f"{k}_{suf}")
                if v is not None:
                    r[f"{k}_{suf}_k"] = str(v / 1000.0)
        ranked(spend, f"{k}_mean_k", f"{k}_sem_k",
               f"{nice} — {SCOPE}, least spend first",
               f"{nice.lower()}  ($ thousands, mean, 95% CI)",
               o(f"rank_spend_{k}.png"), fmt="{:.1f}k", descending=False)

    print("\ncombined views")
    spend_small_multiples(spend, o("spend_small_multiples.png"))
    spend_share(spend, o("spend_share.png"))
    score_breakdown(comp, o("score_breakdown_per_run.png"))


if __name__ == "__main__":
    main()
