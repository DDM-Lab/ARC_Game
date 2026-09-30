"""
Focused action-trajectory comparison: rules-based (baseline) vs rules-based-v2.

Reads the fresh n=20 benchmark episodes for both policies and renders one figure:
  (A,B) stacked-area action mix per round, shared y-axis (fair magnitude comparison)
  (C)   cumulative CONSTRUCTION over the episode  -> shows v2's front-loaded capital
  (D)   cumulative WORKER (hire/train) actions     -> shows v2 batch-hiring early vs rb dribbling
  (E)   choices answered per round (Demand+Emergency+Advisory) -> v2 is leaner/more selective

Pass the benchmark dir (containing rb/ and v2/ episodes.jsonl) as argv[1].
Output: benchmark_results/rb_vs_v2_trajectories.png  (+ .csv)
"""
import json, sys, csv
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

D = Path(sys.argv[1])
OUT = Path(__file__).parent / "benchmark_results"
OUT.mkdir(exist_ok=True)
ROUNDS = 18
CATS = ["construction", "worker", "worker_assignment", "resource_transfer",
        "deconstruction", "choice:Demand", "choice:Emergency", "choice:Advisory"]
CCOL = {c: plt.get_cmap("tab10")(i) for i, c in enumerate(CATS)}


def load(p):
    return [json.loads(l) for l in (D / p / "episodes.jsonl").open()]


def avg_mix(eps):
    acc = np.zeros((ROUNDS, len(CATS))); cnt = np.zeros(ROUNDS)
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


rb_eps, v2_eps = load("rb"), load("v2")
rb, v2 = avg_mix(rb_eps), avg_mix(v2_eps)
x = np.arange(ROUNDS)
ymax = max(rb.sum(1).max(), v2.sum(1).max()) * 1.08


def col(m, c):
    return m[:, CATS.index(c)]


fig = plt.figure(figsize=(15, 9))
gs = fig.add_gridspec(2, 3, height_ratios=[1.25, 1])

# (A,B) stacked-area mix, shared y
for k, (lab, m, n) in enumerate([("rules-based", rb, len(rb_eps)),
                                 ("rules-based-v2", v2, len(v2_eps))]):
    ax = fig.add_subplot(gs[0, k])
    ax.stackplot(x, *[m[:, j] for j in range(len(CATS))],
                 colors=[CCOL[c] for c in CATS], labels=CATS)
    ax.set_title(f"{lab}  (n={n}, {m.sum():.0f} acts/ep)", fontsize=11)
    ax.set_ylim(0, ymax); ax.set_xlim(0, ROUNDS - 1); ax.grid(alpha=0.25)
    ax.set_xlabel("round"); ax.set_ylabel("mean actions / round")
handles = [plt.Rectangle((0, 0), 1, 1, color=CCOL[c]) for c in CATS]
fig.add_subplot(gs[0, 2]).axis("off")
fig.legend(handles, CATS, fontsize=9, loc="upper right", bbox_to_anchor=(0.99, 0.93),
           title="action category")

# (C) cumulative construction
axc = fig.add_subplot(gs[1, 0])
axc.plot(x, np.cumsum(col(rb, "construction")), "o-", color="#888", label="rules-based")
axc.plot(x, np.cumsum(col(v2, "construction")), "s-", color="#1b7837", label="rules-based-v2")
axc.set_title("cumulative construction (capital build-out)", fontsize=10)
axc.set_xlabel("round"); axc.set_ylabel("builds / episode"); axc.grid(alpha=0.3); axc.legend(fontsize=8)

# (D) cumulative worker (hire/train) actions
axd = fig.add_subplot(gs[1, 1])
axd.plot(x, np.cumsum(col(rb, "worker")), "o-", color="#888", label="rules-based")
axd.plot(x, np.cumsum(col(v2, "worker")), "s-", color="#1b7837", label="rules-based-v2")
axd.set_title("cumulative worker (hire/train) actions", fontsize=10)
axd.set_xlabel("round"); axd.set_ylabel("worker actions / episode"); axd.grid(alpha=0.3); axd.legend(fontsize=8)

# (E) choices answered per round
axe = fig.add_subplot(gs[1, 2])
rbc = col(rb, "choice:Demand") + col(rb, "choice:Emergency") + col(rb, "choice:Advisory")
v2c = col(v2, "choice:Demand") + col(v2, "choice:Emergency") + col(v2, "choice:Advisory")
axe.plot(x, rbc, "o-", color="#888", label=f"rules-based ({rbc.sum():.0f}/ep)")
axe.plot(x, v2c, "s-", color="#1b7837", label=f"rules-based-v2 ({v2c.sum():.0f}/ep)")
axe.set_title("task choices answered per round", fontsize=10)
axe.set_xlabel("round"); axe.set_ylabel("choices / round"); axe.grid(alpha=0.3); axe.legend(fontsize=8)

fig.suptitle("Action trajectories: rules-based vs rules-based-v2 (n=20 each, rebuilt env)", fontsize=13)
fig.tight_layout(rect=[0, 0, 1, 0.96])
fig.savefig(OUT / "rb_vs_v2_trajectories.png", dpi=140)
plt.close(fig)

# CSV
with (OUT / "rb_vs_v2_trajectories.csv").open("w", newline="") as fh:
    w = csv.writer(fh)
    w.writerow(["policy", "round"] + CATS)
    for lab, m in [("rules-based", rb), ("rules-based-v2", v2)]:
        for ri in range(ROUNDS):
            w.writerow([lab, ri] + [f"{m[ri, j]:.3f}" for j in range(len(CATS))])

print("wrote benchmark_results/rb_vs_v2_trajectories.png + .csv")
for lab, m, eps in [("rules-based", rb, rb_eps), ("rules-based-v2", v2, v2_eps)]:
    constr = col(m, "construction")
    # fraction of construction done by end of day 2 (round 7)
    early = constr[:8].sum() / max(constr.sum(), 1e-9)
    print(f"  {lab:16} acts/ep={m.sum():5.1f}  builds/ep={constr.sum():4.1f} "
          f"({early*100:3.0f}% by round 7)  worker_acts/ep={col(m,'worker').sum():4.1f}")
