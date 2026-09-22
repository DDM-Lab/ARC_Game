"""
End-state clustering across all collected episodes (current casework-fixed env).

Question: do episodes converge to a few common "trajectory end states" (attractors) in
(final satisfaction, final budget) space — shared across models — or is it highly variable
and model-specific?

Approach (numpy only; no sklearn):
  * Pool every episode from the comparable current-env runs. Each point = one episode's
    END state: (finalSatisfaction, finalBudget, totalReward, lodging%).
  * KMeans (k-means++ init, fixed seed) on standardized (sat, budget). Pick k by elbow.
  * Fig A: 3D scatter (sat, budget, reward) colored by cluster, centroids marked, 2 angles.
  * Fig B: 2D density (hexbin) of end states = the attractor map; cluster centroids overlaid.
  * Fig C: cluster composition per model (stacked) -> common attractors vs model-specific.
  * CSV: per-episode assignments + cluster summary.
"""
import json, csv
from pathlib import Path
from collections import defaultdict, Counter
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D  # noqa: F401

ROOT = Path(__file__).parent
RES = ROOT / "benchmark_results"
OUT = RES / "cheap_vs_baselines_report"
OUT.mkdir(exist_ok=True)

# (dir, group, condition, model_substr_or_None). Current-env, score_components-identical runs.
SOURCES = [
    ("v3_noop",         "baseline", "—",     None),
    ("v3_random",       "baseline", "—",     None),
    ("v3_greedy",       "baseline", "—",     None),
    ("v3_rules-based",  "baseline", "—",     None),
    ("ab_before_cheap", "cheap",    "before", None),
    ("ab_after_cheap",  "cheap",    "after",  None),
    ("ab_after_mid",    "mid",      "after",  None),
    ("ab_after_flagship","flagship","after",  None),
]


def short(m):
    m = m.split("/")[-1]
    return (m.replace("us.anthropic.", "").replace("-20251001-v1:0", "")
             .replace("claude-", "")[:16])


def load():
    pts = []
    for d, grp, cond, _ in SOURCES:
        f = RES / d / "episodes.jsonl"
        if not f.exists():
            continue
        for line in f.open():
            r = json.loads(line)
            s = r.get("summary")
            if not s or r.get("error"):
                continue
            fb, fs = s.get("finalBudget"), s.get("finalSat")
            tr = s.get("totalReward")
            if fb is None or fs is None or tr is None:
                continue
            # policy label: heuristic name for baselines, model name for LLMs
            if grp == "baseline":
                label = {"v3_noop": "noop", "v3_random": "random",
                         "v3_greedy": "greedy", "v3_rules-based": "rules-based"}[d]
            else:
                label = short(r["model"]) + (f"·{cond}" if grp == "cheap" else "")
            pts.append(dict(sat=float(fs), bud=float(fb), rew=float(tr),
                            lodg=(s.get("lodgingFulfillRate") or 0) * 100,
                            group=grp, cond=cond, label=label, model=short(r["model"])))
    return pts


def kmeans(X, k, iters=200, seed=7):
    rng = np.random.default_rng(seed)
    centers = [X[rng.integers(len(X))]]
    for _ in range(1, k):
        d2 = np.min(np.stack([((X - c) ** 2).sum(1) for c in centers]), axis=0)
        p = d2 / d2.sum() if d2.sum() > 0 else None
        centers.append(X[rng.choice(len(X), p=p)])
    C = np.array(centers)
    lab = np.zeros(len(X), int)
    for _ in range(iters):
        lab = np.argmin(((X[:, None, :] - C[None, :, :]) ** 2).sum(2), axis=1)
        newC = np.array([X[lab == j].mean(0) if (lab == j).any() else C[j]
                         for j in range(k)])
        if np.allclose(newC, C):
            break
        C = newC
    inertia = float(((X - C[lab]) ** 2).sum())
    return lab, C, inertia


pts = load()
n = len(pts)
print(f"pooled {n} episodes from {len({p['label'] for p in pts})} policies/models")

sat = np.array([p["sat"] for p in pts])
bud = np.array([p["bud"] for p in pts])
rew = np.array([p["rew"] for p in pts])
Xraw = np.column_stack([sat, bud])
mu, sd = Xraw.mean(0), Xraw.std(0)
Xz = (Xraw - mu) / sd

# elbow: inertia for k=2..6, pick k at largest relative drop-off (knee)
inertias = {k: kmeans(Xz, k)[2] for k in range(2, 7)}
drops = {k: (inertias[k - 1] - inertias[k]) / inertias[k - 1] for k in range(3, 7)}
K = 4  # default; report elbow for justification
print("elbow inertia:", {k: round(v, 1) for k, v in inertias.items()})
print("relative drop:", {k: round(v, 3) for k, v in drops.items()}, "-> using K =", K)

lab, Cz, _ = kmeans(Xz, K)
C = Cz * sd + mu  # centroids back in (sat, budget) units
# order clusters by centroid budget for stable coloring/labeling
order = np.argsort(C[:, 1])
remap = {old: new for new, old in enumerate(order)}
lab = np.array([remap[l] for l in lab])
C = C[order]
for p, l in zip(pts, lab):
    p["cluster"] = int(l)

COL = ["#d62728", "#ff7f0e", "#2ca02c", "#1f77b4", "#9467bd", "#8c564b"]
cluster_names = [f"C{j}" for j in range(K)]

# ── Fig A: 3D scatter (sat, budget, reward) colored by cluster, 2 view angles ──
figA = plt.figure(figsize=(17, 8.5))
handles = None
for vi, (az, el) in enumerate([(-60, 22), (40, 16)]):
    ax = figA.add_subplot(1, 2, vi + 1, projection="3d")
    for j in range(K):
        m = lab == j
        ax.scatter(sat[m], bud[m], rew[m], s=34, alpha=0.65, c=COL[j],
                   depthshade=True,
                   label=f"{cluster_names[j]}: sat≈{C[j,0]:.0f}, bud≈${C[j,1]:,.0f} (n={int(m.sum())})")
    ax.scatter(C[:, 0], C[:, 1], [rew[lab == j].mean() for j in range(K)],
               s=340, c="k", marker="X", depthshade=False)
    ax.set_xlabel("final satisfaction", labelpad=8)
    ax.set_ylabel("final budget ($)", labelpad=12)
    ax.set_zlabel("total reward", labelpad=4)
    ax.view_init(elev=el, azim=az)
    ax.set_title(f"view {vi+1}", fontsize=9)
    if vi == 0:
        handles, _ = ax.get_legend_handles_labels()
figA.legend(handles=handles, fontsize=10, loc="lower center", ncol=2,
            bbox_to_anchor=(0.5, -0.02))
figA.suptitle(f"End-state clusters across all collected episodes (n={n})\n"
              f"axes = (final satisfaction, final budget, total reward); ✕ = cluster centroid",
              fontsize=13)
figA.tight_layout(rect=[0, 0.08, 1, 0.95])
figA.savefig(OUT / "endstate_clusters_3d.png", dpi=140)
plt.close(figA)

# ── Fig B: 2D attractor density (hexbin) + centroids ──
figB, (axd, axg) = plt.subplots(1, 2, figsize=(15, 6))
hb = axd.hexbin(sat, bud, gridsize=24, cmap="magma", mincnt=1)
figB.colorbar(hb, ax=axd, label="episodes in bin")
axd.scatter(C[:, 0], C[:, 1], s=240, c="cyan", marker="X", edgecolor="k", zorder=5)
for j in range(K):
    axd.annotate(cluster_names[j], (C[j, 0], C[j, 1]), color="cyan",
                 fontsize=11, fontweight="bold", xytext=(6, 6), textcoords="offset points")
axd.set_xlabel("final satisfaction"); axd.set_ylabel("final budget ($)")
axd.set_title("Attractor density of end states (peaks = common end states)")
axd.axhline(0, color="w", lw=0.5, alpha=0.5)
# colored by group, to see if attractors are shared across model tiers
GRP_COL = {"baseline": "#7f8c8d", "cheap": "#e67e22", "mid": "#2980b9", "flagship": "#8e44ad"}
for g, c in GRP_COL.items():
    m = np.array([p["group"] == g for p in pts])
    if m.any():
        axg.scatter(sat[m], bud[m], s=20, alpha=0.55, c=c, label=f"{g} (n={int(m.sum())})")
axg.scatter(C[:, 0], C[:, 1], s=240, c="k", marker="X", zorder=5)
axg.set_xlabel("final satisfaction"); axg.set_ylabel("final budget ($)")
axg.set_title("Same end states colored by tier (shared attractors?)")
axg.axhline(0, color="k", lw=0.5); axg.legend(fontsize=8); axg.grid(alpha=0.3)
figB.tight_layout()
figB.savefig(OUT / "endstate_density.png", dpi=140)
plt.close(figB)

# ── Fig C: cluster composition per policy/model (stacked %) ──
bym = defaultdict(Counter)
for p in pts:
    bym[p["label"]][p["cluster"]] += 1
labels = sorted(bym, key=lambda L: (-sum(bym[L].values()), L))
figC, axc = plt.subplots(figsize=(13, 6))
bottom = np.zeros(len(labels))
for j in range(K):
    vals = np.array([bym[L][j] / sum(bym[L].values()) * 100 for L in labels])
    axc.bar(range(len(labels)), vals, bottom=bottom, color=COL[j],
            label=f"{cluster_names[j]}: sat≈{C[j,0]:.0f}, bud≈${C[j,1]:,.0f}")
    bottom += vals
axc.set_xticks(range(len(labels)))
axc.set_xticklabels([f"{L}\n(n={sum(bym[L].values())})" for L in labels],
                    rotation=40, ha="right", fontsize=8)
axc.set_ylabel("% of policy's episodes in cluster"); axc.set_ylim(0, 100)
axc.set_title("Which end-state cluster each policy/model lands in "
              "(uniform colors across bars = shared attractors; distinct = model-specific)")
axc.legend(fontsize=8, ncol=2, loc="lower center", bbox_to_anchor=(0.5, 1.04))
figC.tight_layout()
figC.savefig(OUT / "endstate_cluster_by_model.png", dpi=140)
plt.close(figC)

# ── CSV: per-cluster summary + per-episode assignment ──
with (OUT / "endstate_clusters_summary.csv").open("w", newline="") as fh:
    w = csv.writer(fh)
    w.writerow(["cluster", "n", "centroid_sat", "centroid_budget",
                "mean_reward", "mean_lodging%", "dominant_groups"])
    for j in range(K):
        idx = [i for i in range(n) if lab[i] == j]
        gc = Counter(pts[i]["group"] for i in idx)
        w.writerow([cluster_names[j], len(idx), f"{C[j,0]:.1f}", f"{C[j,1]:.0f}",
                    f"{np.mean([pts[i]['rew'] for i in idx]):.2f}",
                    f"{np.mean([pts[i]['lodg'] for i in idx]):.1f}",
                    "; ".join(f"{g}:{c}" for g, c in gc.most_common())])
with (OUT / "endstate_clusters_points.csv").open("w", newline="") as fh:
    w = csv.writer(fh)
    w.writerow(["label", "group", "cond", "cluster", "finalSat", "finalBudget", "reward", "lodging%"])
    for p in pts:
        w.writerow([p["label"], p["group"], p["cond"], f"C{p['cluster']}",
                    f"{p['sat']:.0f}", f"{p['bud']:.0f}", f"{p['rew']:.2f}", f"{p['lodg']:.0f}"])

print("\ncluster summary:")
for j in range(K):
    idx = [i for i in range(n) if lab[i] == j]
    gc = Counter(pts[i]["group"] for i in idx)
    print(f"  {cluster_names[j]}: n={len(idx):3} centroid(sat={C[j,0]:5.1f}, bud=${C[j,1]:8,.0f}) "
          f"rew={np.mean([pts[i]['rew'] for i in idx]):5.2f} lodg={np.mean([pts[i]['lodg'] for i in idx]):4.0f}% "
          f"groups[{', '.join(f'{g}:{c}' for g,c in gc.most_common())}]")
print("\nwrote:", *[p.name for p in sorted(OUT.glob('endstate_*'))])
