"""
Comparison graphs across ALL tiers on the current casework-fixed env:
  - non-learning baselines (noop / random / greedy / rules-based)
  - cheap LLM tier, BEFORE vs AFTER the enriched observation-prompt
  - mid-tier LLMs (new env / after prompt)
  - flagship LLMs (new env / after prompt)
All runs share identical reward score_components (verified), so totalReward is comparable.

Outputs (into OUT_DIR): comparison_bars.png, reward_trajectories.png, summary.csv, README.txt
Re-runnable: picks up mid/flagship dirs as they fill in; missing dirs are skipped.
"""
import json, csv, math, statistics as st
from pathlib import Path
from collections import defaultdict
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

ROOT = Path(__file__).parent
RES = ROOT / "benchmark_results"
OUT_DIR = RES / "cheap_vs_baselines_report"
OUT_DIR.mkdir(exist_ok=True)

# Non-learning baselines (single grey bar each).
BASELINES = [
    ("noop",        "v3_noop"),
    ("random",      "v3_random"),
    ("greedy",      "v3_greedy"),
    ("rules-based", "v3_rules-based"),
]
# Cheap tier: before/after pair. (label, model_substr)
CHEAP_MODELS = [
    ("haiku-4.5",    "claude-ha"),
    ("gpt-5-mini",   "gpt-5-mini"),
    ("gemini-flash", "gemini-2.5-flash"),
]
BEFORE_DIR, AFTER_DIR = "ab_before_cheap", "ab_after_cheap"
# After-only tiers (new env). (label, model_substr, dir)
MID_MODELS = [
    ("sonnet-4.6",   "sonnet-4-6",  "ab_after_mid"),
    ("gpt-5.4",      "gpt-5.4",     "ab_after_mid"),
    ("gemini-2.5pro","gemini-2.5-pro", "ab_after_mid"),
]
FLAGSHIP_MODELS = [
    ("opus-4.8",     "opus-4-8",    "ab_after_flagship"),
    ("gpt-5.5",      "gpt-5.5",     "ab_after_flagship"),
    ("gpt-5.4-pro",  "gpt-5.4-pro", "ab_after_flagship"),
    ("gemini-3.1pro","gemini-3.1",  "ab_after_flagship"),
]

C_BASE, C_BEF, C_AFT, C_MID, C_FLAG = "#7f8c8d", "#e67e22", "#27ae60", "#2980b9", "#8e44ad"


def load(d):
    f = RES / d / "episodes.jsonl"
    if not f.exists():
        return []
    out = []
    for line in f.open():
        r = json.loads(line)
        if r.get("summary") and not r.get("error"):
            out.append(r)
    return out


def filt(rs, sub):
    # gpt-5.4 must not also match gpt-5.4-pro
    if sub == "gpt-5.4":
        return [r for r in rs if "gpt-5.4" in r["model"] and "pro" not in r["model"]]
    return [r for r in rs if sub is None or sub in r["model"]]


def mean_se(xs):
    xs = [x for x in xs if x is not None]
    if not xs:
        return float("nan"), 0.0, 0
    m = st.mean(xs)
    se = (st.pstdev(xs) / math.sqrt(len(xs))) if len(xs) > 1 else 0.0
    return m, se, len(xs)


def metrics(rs):
    return {
        "reward":  mean_se([r["summary"].get("totalReward") for r in rs]),
        "budget":  mean_se([r["summary"].get("finalBudget") for r in rs]),
        "lodging": mean_se([(r["summary"].get("lodgingFulfillRate") or 0) * 100 for r in rs]),
        "sat":     mean_se([r["summary"].get("finalSat") for r in rs]),
    }


# ── assemble bars: list of (xlabel, [(metrics, color, condition_label), ...]) ──
before, after = load(BEFORE_DIR), load(AFTER_DIR)
bars = []
for lab, d in BASELINES:
    bars.append((lab, [(metrics(load(d)), C_BASE, "non-learning")]))
for lab, sub in CHEAP_MODELS:
    bars.append((lab, [(metrics(filt(before, sub)), C_BEF, "cheap·before"),
                       (metrics(filt(after, sub)),  C_AFT, "cheap·after")]))
for lab, sub, d in MID_MODELS:
    bars.append((lab, [(metrics(filt(load(d), sub)), C_MID, "mid·after")]))
for lab, sub, d in FLAGSHIP_MODELS:
    bars.append((lab, [(metrics(filt(load(d), sub)), C_FLAG, "flagship·after")]))

# group boundaries (for vertical separators): after baselines, cheap, mid
seps = [len(BASELINES) - 0.5,
        len(BASELINES) + len(CHEAP_MODELS) - 0.5,
        len(BASELINES) + len(CHEAP_MODELS) + len(MID_MODELS) - 0.5]

# ── CSV ──
with (OUT_DIR / "summary.csv").open("w", newline="") as fh:
    w = csv.writer(fh)
    w.writerow(["policy", "condition", "n", "reward_mean", "reward_se",
                "finalBudget_mean", "lodging%_mean", "finalSat_mean"])
    for lab, series in bars:
        for m, _c, cond in series:
            w.writerow([lab, cond, m["reward"][2], f"{m['reward'][0]:.3f}",
                        f"{m['reward'][1]:.3f}", f"{m['budget'][0]:.0f}",
                        f"{m['lodging'][0]:.1f}", f"{m['sat'][0]:.1f}"])

# ── Figure 1: grouped bars (reward / budget / lodging / sat) ──
panels = [("reward", "Total reward (mean ± SE)"),
          ("budget", "Final budget ($)"),
          ("lodging", "Lodging fulfillment (%)"),
          ("sat", "Final satisfaction")]
xlabels = [b[0] for b in bars]
fig, axes = plt.subplots(2, 2, figsize=(16, 10))
legend_seen = {}
for ax, (key, title) in zip(axes.flat, panels):
    for i, (lab, series) in enumerate(bars):
        k = len(series)
        width = 0.7 if k == 1 else 0.38
        offs = [0] if k == 1 else [-width/2, width/2]
        for off, (m, c, cond) in zip(offs, series):
            mv, se, n = m[key]
            if n == 0 or mv != mv:
                continue
            lbl = cond if cond not in legend_seen else None
            legend_seen[cond] = True
            ax.bar(i + off, mv, width, color=c, yerr=se, capsize=3, label=lbl)
            ax.annotate(f"{n}", (i + off, mv), ha="center",
                        va="bottom" if mv >= 0 else "top", fontsize=6)
    ax.set_title(title)
    ax.set_xticks(range(len(bars)))
    ax.set_xticklabels(xlabels, rotation=35, ha="right", fontsize=8)
    ax.axhline(0, color="k", lw=0.6)
    for s in seps:
        ax.axvline(s, color="k", ls=":", lw=0.8)
    ax.grid(axis="y", alpha=0.3)
handles, labels = axes.flat[0].get_legend_handles_labels()
fig.legend(handles, labels, fontsize=9, loc="lower center", ncol=5, bbox_to_anchor=(0.5, -0.01))
fig.suptitle("All tiers on current casework-fixed env — baselines | cheap (before/after) | mid | flagship\n"
             "n annotated on each bar; dotted lines separate tiers", fontsize=13)
fig.tight_layout(rect=[0, 0.05, 1, 0.95])
fig.savefig(OUT_DIR / "comparison_bars.png", dpi=140)
plt.close(fig)


# ── Figure 2: mean cumulative-reward trajectory vs round ──
def traj(rs):
    by_r = defaultdict(list)
    for r in rs:
        for rd in r.get("rounds", []):
            if rd.get("sumR") is not None:
                by_r[rd["r"]].append(rd["sumR"])
    xs = sorted(by_r)
    return xs, [st.mean(by_r[i]) for i in xs]

# one line PER EXACT policy/model (not collapsed into tiers). baselines dashed,
# LLM tiers solid; every line gets a unique color + exact label with its n.
# series: (label, episodes, tier_for_linestyle)
series = []
for lab, d in BASELINES:
    series.append((lab, load(d), "baseline"))
for lab, sub in CHEAP_MODELS:
    series.append((f"{lab}·after", filt(after, sub), "cheap"))
for lab, sub, d in MID_MODELS:
    series.append((f"{lab} (mid)", filt(load(d), sub), "mid"))
for lab, sub, d in FLAGSHIP_MODELS:
    series.append((f"{lab} (flagship)", filt(load(d), sub), "flagship"))

cmap = plt.get_cmap("tab20")
fig2, ax = plt.subplots(figsize=(13, 8))
ci = 0
for lab, rs, tier in series:
    xs, ys = traj(rs)
    if not xs:
        continue
    n = len(rs)
    ls = "--" if tier == "baseline" else "-"
    lw = 1.8 if tier == "baseline" else 2.2
    ax.plot(xs, ys, lw=lw, ls=ls, color=cmap(ci % 20),
            label=f"{lab}  (n={n})")
    ci += 1
ax.set_xlabel("round"); ax.set_ylabel("cumulative reward (mean across episodes)")
ax.set_title("Mean cumulative reward over an episode — one line per policy/model "
             "(baselines dashed; LLMs solid)")
ax.axhline(0, color="k", lw=0.6); ax.grid(alpha=0.3)
ax.legend(fontsize=8, ncol=2, loc="upper left")
fig2.tight_layout()
fig2.savefig(OUT_DIR / "reward_trajectories.png", dpi=140)
plt.close(fig2)

print("wrote:", *[p.name for p in (OUT_DIR/"comparison_bars.png", OUT_DIR/"reward_trajectories.png", OUT_DIR/"summary.csv")])
for lab, series in bars:
    for m, _c, cond in series:
        r, b, lo = m["reward"], m["budget"], m["lodging"]
        print(f"  {lab:14}{cond:16} n={r[2]:2}  reward={r[0]:6.2f}  bud={b[0]:9.0f}  lodg%={lo[0]:4.0f}")
