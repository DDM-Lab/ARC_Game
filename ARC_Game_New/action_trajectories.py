"""
Per-policy/model AVERAGE ACTION TRAJECTORY: for each policy, the mean number of each
action category attempted at each round (averaged across that policy's episodes), shown
as a stacked area over the 18-round episode. Small-multiples grid, shared colors + y-axis.

Action categories come from each round's `actCats` (the strategy mix the policy requested):
  game actions : construction, worker(hire/train), worker_assignment, resource_transfer, deconstruction
  task choices : choice:Demand, choice:Emergency, choice:Advisory

Output: benchmark_results/cheap_vs_baselines_report/action_trajectories.png  (+ .csv)
Re-runnable: picks up mid/flagship dirs as they fill in; policies with no data are skipped.
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

# (display label, dir, model_substr_or_None) — same policy set as the reward trajectories.
POLICIES = [
    ("noop",            "v3_noop",          None),
    ("random",          "v3_random",        None),
    ("greedy",          "v3_greedy",        None),
    ("rules-based",     "v3_rules-based",   None),
    ("haiku-4.5·after", "ab_after_cheap",   "claude-ha"),
    ("gpt-5-mini·after","ab_after_cheap",   "gpt-5-mini"),
    ("gemini-flash·after","ab_after_cheap", "gemini-2.5-flash"),
    ("sonnet-4.6 (mid)","ab_after_mid",     "sonnet-4-6"),
    ("gpt-5.4 (mid)",   "ab_after_mid",     "gpt-5.4"),
    ("gemini-2.5pro (mid)","ab_after_mid",  "gemini-2.5-pro"),
    ("opus-4.8 (flag)", "ab_after_flagship","opus-4-8"),
    ("gpt-5.5 (flag)",  "ab_after_flagship","gpt-5.5"),
    ("gpt-5.4-pro (flag)","ab_after_flagship","gpt-5.4-pro"),
    ("gemini-3.1pro (flag)","ab_after_flagship","gemini-3.1"),
]
# fixed category order + colors (consistent across all subplots)
CATS = ["construction", "worker", "worker_assignment", "resource_transfer",
        "deconstruction", "choice:Demand", "choice:Emergency", "choice:Advisory"]
CCOL = {c: plt.get_cmap("tab10")(i) for i, c in enumerate(CATS)}


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


def avg_mix(eps):
    """mean[cat] over episodes at each round -> array [ROUNDS, len(CATS)]."""
    acc = np.zeros((ROUNDS, len(CATS)))
    cnt = np.zeros(ROUNDS)
    for r in eps:
        for rd in r.get("rounds", []):
            ri = rd.get("r")
            if ri is None or ri >= ROUNDS:
                continue
            cnt[ri] += 1
            ac = rd.get("actCats") or {}
            for j, c in enumerate(CATS):
                acc[ri, j] += ac.get(c, 0)
    cnt[cnt == 0] = 1
    return acc / cnt[:, None]


# build data for policies that have episodes
panels = []
for lab, d, sub in POLICIES:
    eps = episodes(d, sub)
    if eps:
        panels.append((lab, len(eps), avg_mix(eps)))

ncol = 4
nrow = (len(panels) + ncol - 1) // ncol

# independent y-axes (sharey=False): each policy's action MIX/shape stays readable even
# though magnitudes differ ~50x across policies. The per-episode total is in each title.
fig, axes = plt.subplots(nrow, ncol, figsize=(4.4 * ncol, 3.1 * nrow),
                         sharex=True, sharey=False, squeeze=False)
x = np.arange(ROUNDS)
for k, (lab, n, mix) in enumerate(panels):
    ax = axes[k // ncol][k % ncol]
    ax.stackplot(x, *[mix[:, j] for j in range(len(CATS))],
                 colors=[CCOL[c] for c in CATS], labels=CATS)
    ax.set_title(f"{lab}  (n={n}, {mix.sum():.0f} acts/ep)", fontsize=9)
    ax.set_ylim(0, max(mix.sum(1).max() * 1.08, 0.5)); ax.grid(alpha=0.25)
    if k % ncol == 0:
        ax.set_ylabel("mean actions / round", fontsize=8)
    if k // ncol == nrow - 1:
        ax.set_xlabel("round", fontsize=8)
# blank any unused axes
for k in range(len(panels), nrow * ncol):
    axes[k // ncol][k % ncol].axis("off")
handles = [plt.Rectangle((0, 0), 1, 1, color=CCOL[c]) for c in CATS]
fig.legend(handles, CATS, fontsize=9, loc="lower center", ncol=8,
           bbox_to_anchor=(0.5, -0.01))
fig.suptitle("Average action trajectory per policy/model — mean count of each action category "
             "attempted per round\n(stacked; total height = mean actions/round. NOTE: y-axes "
             "differ per panel — see 'acts/ep' in titles for magnitude)", fontsize=12)
fig.tight_layout(rect=[0, 0.05, 1, 0.95])
fig.savefig(OUT / "action_trajectories.png", dpi=140)
plt.close(fig)

# CSV: policy, round, mean per category
with (OUT / "action_trajectories.csv").open("w", newline="") as fh:
    w = csv.writer(fh)
    w.writerow(["policy", "n", "round"] + CATS)
    for lab, n, mix in panels:
        for ri in range(ROUNDS):
            w.writerow([lab, n, ri] + [f"{mix[ri, j]:.3f}" for j in range(len(CATS))])

print(f"wrote action_trajectories.png + .csv ({len(panels)} policies)")
for lab, n, mix in panels:
    tot = mix.sum()
    dom = CATS[int(mix.sum(0).argmax())]
    print(f"  {lab:22} n={n:2}  mean total actions/episode={tot:5.1f}  dominant={dom}")
