"""
Reward sub-component trajectories: one subplot PER reward sub-component, each with one line
PER policy/model, showing the mean cumulative value over the 18-round episode.

Reward = satisfaction - cost_efficiency, where (per Python scoring):
  satisfaction    = sat_food + sat_lodging + sat_worker_use + casework_processing_sat
  cost_efficiency = cost_food + cost_lodging + cost_worker + casework_efficiency
The per-round `comps` dict stores each of these cumulative-to-date, so these are trajectories.

Output: benchmark_results/cheap_vs_baselines_report/reward_components.png (+ .csv)
Re-runnable: picks up mid/flagship as they fill in; policies with no data are skipped.
"""
import json, csv
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

ROOT = Path(__file__).parent
RES = ROOT / "benchmark_results"
OUT = RES / "cheap_vs_baselines_report"
OUT.mkdir(exist_ok=True)
ROUNDS = 18

# (label, dir, model_substr, is_baseline) — same policy set as the reward trajectories.
POLICIES = [
    ("noop",            "v3_noop",          None, True),
    ("random",          "v3_random",        None, True),
    ("greedy",          "v3_greedy",        None, True),
    ("rules-based",     "v3_rules-based",   None, True),
    ("haiku-4.5·after", "ab_after_cheap",   "claude-ha", False),
    ("gpt-5-mini·after","ab_after_cheap",   "gpt-5-mini", False),
    ("gemini-flash·after","ab_after_cheap", "gemini-2.5-flash", False),
    ("sonnet-4.6 (mid)","ab_after_mid",     "sonnet-4-6", False),
    ("gpt-5.4 (mid)",   "ab_after_mid",     "gpt-5.4", False),
    ("gemini-2.5pro (mid)","ab_after_mid",  "gemini-2.5-pro", False),
    ("opus-4.8 (flag)", "ab_after_flagship","opus-4-8", False),
    ("gpt-5.5 (flag)",  "ab_after_flagship","gpt-5.5", False),
    ("gpt-5.4-pro (flag)","ab_after_flagship","gpt-5.4-pro", False),
    ("gemini-3.1pro (flag)","ab_after_flagship","gemini-3.1", False),
]
# component subplots, grouped: satisfaction leaves + aggregate | cost leaves + aggregate | score
COMPS = [
    ("sat_food", "satisfaction: food"),
    ("sat_lodging", "satisfaction: lodging"),
    ("sat_worker_use", "satisfaction: worker use"),
    ("casework_processing_sat", "satisfaction: casework"),
    ("satisfaction", "Σ satisfaction (sum of above)"),
    ("cost_food", "cost: food"),
    ("cost_lodging", "cost: lodging (incl. motel)"),
    ("cost_worker", "cost: worker"),
    ("casework_efficiency", "cost: casework"),
    ("cost_efficiency", "Σ cost_efficiency (sum of above)"),
    ("score", "SCORE = satisfaction − cost_efficiency"),
]


def episodes(d, sub):
    f = RES / d / "episodes.jsonl"
    if not f.exists():
        return []
    out = []
    for line in f.open():
        r = json.loads(line)
        if not r.get("summary") or r.get("error"):
            continue
        if sub == "gpt-5.4" and "pro" in r["model"]:
            continue
        if sub is None or sub in r["model"]:
            out.append(r)
    return out


def comp_traj(eps, key):
    """mean cumulative value of comps[key] at each round across episodes."""
    acc = np.zeros(ROUNDS)
    cnt = np.zeros(ROUNDS)
    for r in eps:
        for rd in r.get("rounds", []):
            ri = rd.get("r")
            if ri is None or ri >= ROUNDS:
                continue
            v = (rd.get("comps") or {}).get(key)
            if v is not None:
                acc[ri] += v
                cnt[ri] += 1
    cnt[cnt == 0] = 1
    return acc / cnt


# load each policy's episodes once
loaded = [(lab, episodes(d, sub), base) for (lab, d, sub, base) in POLICIES]
loaded = [(lab, eps, base) for (lab, eps, base) in loaded if eps]

cmap = plt.get_cmap("tab20")
colmap = {lab: cmap(i % 20) for i, (lab, _e, _b) in enumerate(loaded)}

ncol = 3
nrow = (len(COMPS) + ncol - 1) // ncol
fig, axes = plt.subplots(nrow, ncol, figsize=(5.6 * ncol, 3.4 * nrow),
                         sharex=True, squeeze=False)
x = np.arange(ROUNDS)
for k, (key, title) in enumerate(COMPS):
    ax = axes[k // ncol][k % ncol]
    for lab, eps, base in loaded:
        ys = comp_traj(eps, key)
        ax.plot(x, ys, lw=1.6, ls="--" if base else "-", color=colmap[lab],
                alpha=0.85, label=f"{lab} (n={len(eps)})")
    ax.set_title(title, fontsize=10)
    ax.grid(alpha=0.25); ax.axhline(0, color="k", lw=0.5)
    if k % ncol == 0:
        ax.set_ylabel("cumulative (mean)", fontsize=8)
    if k // ncol == nrow - 1:
        ax.set_xlabel("round", fontsize=8)
for k in range(len(COMPS), nrow * ncol):
    axes[k // ncol][k % ncol].axis("off")
handles, labels = axes[0][0].get_legend_handles_labels()
fig.legend(handles, labels, fontsize=8.5, loc="lower center", ncol=5,
           bbox_to_anchor=(0.5, -0.01))
fig.suptitle("Reward sub-component trajectories — one line per policy/model, per component "
             "(cumulative over the episode)\nbaselines dashed; LLMs solid", fontsize=13)
fig.tight_layout(rect=[0, 0.06, 1, 0.95])
fig.savefig(OUT / "reward_components.png", dpi=140)
plt.close(fig)

# CSV: final-round (end of episode) mean per component per policy
with (OUT / "reward_components.csv").open("w", newline="") as fh:
    w = csv.writer(fh)
    w.writerow(["policy", "n"] + [c[0] for c in COMPS])
    for lab, eps, base in loaded:
        finals = [comp_traj(eps, key)[-1] for key, _t in COMPS]
        w.writerow([lab, len(eps)] + [f"{v:.3f}" for v in finals])

print(f"wrote reward_components.png + .csv ({len(loaded)} policies, {len(COMPS)} components)")
for lab, eps, base in loaded:
    f = {key: comp_traj(eps, key)[-1] for key, _t in COMPS}
    print(f"  {lab:22} n={len(eps):2}  satL={f['sat_lodging']:.2f} caseSat={f['casework_processing_sat']:.2f} "
          f"costLodg={f['cost_lodging']:.2f} score={f['score']:.2f}")
