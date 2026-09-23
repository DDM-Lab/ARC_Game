#!/usr/bin/env python3
"""Which macro actions predict a good score?

MACRO ACTION SPACE: an action tagged with WHEN it happened. `build_Shelter|p0_r0-3` is a
different feature from `build_Shelter|p3_r16-23`, so timing is part of the strategy, not
averaged away. Built by analysis/extract_macro (run on the cluster) -- see
analysis/macro_actions_per_episode.csv.

THE ANALYSIS IS WITHIN-RUN, AND THAT IS THE WHOLE POINT.
Pooling all 853 episodes and correlating actions with score mostly rediscovers "Qwen3.8-27B
scores well, and here is what Qwen3.8-27B happens to do" -- model identity drives both sides.
Every feature and the outcome are therefore demeaned BY RUN (model x config) first, so each
correlation answers: holding the model and settings fixed, do episodes that did more of this
score better? The pooled correlation is reported alongside purely as the contrast; where the
two disagree, the difference IS the confound.

PROACTIVE vs REACTIVE. Builds, hires and staffing are model-initiated. Task choices are NOT:
the game issues an Emergency Budget Crisis BECAUSE you are already broke, and food Demands
BECAUSE shelters are empty. A negative coefficient on choice:Emergency is a symptom of
failure, not a cause of it. Only proactive features support a strategy reading; reactive ones
are labelled and should be read as diagnostics.

COMPLETED EPISODES ONLY (853 of 908). Terminated episodes have structurally zero late-round
counts and score lower (0.386 vs 0.469), so including them would manufacture spurious negative
associations for every late-phase feature.

Significance: permutation test that shuffles the outcome WITHIN each run (preserving the
run structure the demeaning assumes), then Benjamini-Hochberg FDR across features.

USAGE
  python analysis/macro_action_analysis.py [--perms 10000] [--q 0.05]
"""
import argparse, csv, os, sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle

from plot_components import house_axes, INK, MUTE, GRID, V3, RUST, TEAL_RAMP, RUST_RAMP

PROACTIVE = ("build_", "worker")


def is_proactive(feat):
    return feat.startswith(PROACTIVE)


def demean_by_group(x, groups):
    """Subtract each group's mean. Removes any effect constant within a run."""
    out = x.astype(float).copy()
    for g in np.unique(groups):
        m = groups == g
        out[m] -= out[m].mean()
    return out


def pearson(a, b):
    a = a - a.mean(); b = b - b.mean()
    d = np.sqrt((a * a).sum() * (b * b).sum())
    return float((a * b).sum() / d) if d else np.nan


def bh_fdr(pvals, q):
    """Benjamini-Hochberg: returns a boolean mask of discoveries at level q."""
    p = np.asarray(pvals, float)
    n = len(p)
    order = np.argsort(p)
    thresh = q * (np.arange(1, n + 1) / n)
    passed = p[order] <= thresh
    k = np.max(np.where(passed)[0]) + 1 if passed.any() else 0
    keep = np.zeros(n, bool)
    keep[order[:k]] = True
    return keep


SAT = ["sat_food", "sat_lodging", "sat_worker_use"]
COST = ["cost_food", "cost_lodging", "cost_worker"]


def outcome(row, kind):
    """Score variants. Building a CaseworkSite mechanically enables the casework
    satisfaction term, so an effect on the full score is partly definitional. Removing
    that term tests whether early casework helps for any OTHER reason.

    Removing only the satisfaction half is the literal reading but is biased against
    casework: casework_efficiency still subtracts the cost of running the site while its
    credit is gone. no_cw_pair removes both halves and is the fairer comparison.
    """
    g = lambda k: float(row[k]) if row.get(k) not in (None, "") else 0.0
    if kind == "score":
        return float(row["score_norm"])
    if kind == "sat_only":
        return sum(g(k) for k in SAT) / 4.0
    v = sum(g(k) for k in SAT) - sum(g(k) for k in COST)
    if kind == "no_cw_sat":
        v -= g("casework_efficiency")
    return v / 4.0


def load(path):
    rows = list(csv.DictReader(open(path)))
    meta = ("run", "episode", "model", "score_norm", "rounds_played",
            "completed", "terminated", "went_negative", "final_budget",
            "sat_food", "sat_lodging", "sat_worker_use", "casework_processing_sat",
            "cost_food", "cost_lodging", "cost_worker", "casework_efficiency")
    feats = [k for k in rows[0] if k not in meta]
    return rows, feats


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--episodes", default="analysis/macro_actions_per_episode.csv")
    ap.add_argument("--matrix", default="analysis/macro_round_matrix.csv")
    ap.add_argument("--out", default="analysis/figs_macro")
    ap.add_argument("--perms", type=int, default=10000)
    ap.add_argument("--q", type=float, default=0.05)
    ap.add_argument("--outcome", default="score",
                    choices=["score", "no_cw_sat", "no_cw_pair", "sat_only"],
                    help="score = as reported. no_cw_sat = drop ONLY the casework "
                         "satisfaction term (keeps its cost, so it penalises casework "
                         "spend with no credit). no_cw_pair = drop casework satisfaction "
                         "AND casework cost, the fairer removal. sat_only = satisfaction "
                         "terms with no cost subtracted.")
    ap.add_argument("--min-nonzero", type=int, default=40,
                    help="skip features present in fewer than this many episodes")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    rng = np.random.default_rng(0)

    rows, feats = load(a.episodes)
    rows = [r for r in rows if r["completed"] == "1"]
    runs = np.array([r["run"] for r in rows])
    y = np.array([outcome(r, a.outcome) for r in rows])
    yd = demean_by_group(y, runs)
    print(f"{len(rows)} completed episodes across {len(set(runs))} runs, "
          f"{len(feats)} features   outcome={a.outcome}")

    # Precompute the within-run permutation index blocks once.
    blocks = [np.where(runs == g)[0] for g in np.unique(runs)]

    res = []
    for fname in feats:
        x = np.array([float(r[fname]) for r in rows])
        nz = int((x > 0).sum())
        if nz < a.min_nonzero:
            continue
        xd = demean_by_group(x, runs)
        if xd.std() == 0:
            continue
        r_within = pearson(xd, yd)
        r_pooled = pearson(x, y)
        # Null: shuffle the outcome inside each run, so run-level structure is preserved.
        null = np.empty(a.perms)
        for i in range(a.perms):
            yp = yd.copy()
            for b in blocks:
                yp[b] = rng.permutation(yp[b])
            null[i] = pearson(xd, yp)
        p = float((np.abs(null) >= abs(r_within)).mean())
        res.append({"feature": fname, "n_nonzero": nz, "r_within": r_within,
                    "r_pooled": r_pooled, "p": max(p, 1.0 / a.perms),
                    "proactive": is_proactive(fname)})

    keep = bh_fdr([x["p"] for x in res], a.q)
    for x, k in zip(res, keep):
        x["sig"] = bool(k)
    res.sort(key=lambda x: -abs(x["r_within"]))

    tag0 = "" if a.outcome == "score" else f"_{a.outcome}"
    with open(os.path.join(a.out, f"macro_effects{tag0}.csv"), "w", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=list(res[0].keys())); w.writeheader(); w.writerows(res)

    print(f"\n{sum(1 for x in res if x['sig'])} of {len(res)} features significant "
          f"at FDR q={a.q} ({a.perms} within-run permutations)\n")
    print(f"{'r_within':>9} {'r_pooled':>9} {'p':>8} {'n':>5}  kind        feature")
    for x in res:
        if x["sig"]:
            print(f"{x['r_within']:>+9.3f} {x['r_pooled']:>+9.3f} {x['p']:>8.4f} "
                  f"{x['n_nonzero']:>5}  {'proactive' if x['proactive'] else 'reactive ':<11} {x['feature']}")

    tag = "" if a.outcome == "score" else f"_{a.outcome}"
    effects_chart(res, os.path.join(a.out, f"macro_effects{tag}.png"), a.q, a.outcome)
    heatmap(a.matrix, rows, runs, yd, os.path.join(a.out, f"macro_heatmap{tag}.png"),
            a.min_nonzero)


def effects_chart(res, out, q, outcome_kind="score"):
    data = [x for x in res if x["sig"]] or res[:20]
    data.sort(key=lambda x: x["r_within"])
    n = len(data)
    fig, ax = plt.subplots(figsize=(12.6, max(3.4, 0.34 * n + 2.8)), dpi=150)
    ys = np.arange(n)
    cols = [TEAL_RAMP[0] if d["proactive"] else RUST_RAMP[1] for d in data]
    ax.barh(ys - 0.18, [d["r_within"] for d in data], height=0.34, color=cols,
            label="within-run (confound removed)")
    ax.barh(ys + 0.18, [d["r_pooled"] for d in data], height=0.34,
            color=[GRID] * n, label="pooled (model identity NOT controlled)")
    ax.set_yticks(ys)
    ax.set_yticklabels([d["feature"].replace("|", "  ·  ") for d in data], fontsize=8.4)
    ax.axvline(0, color=INK, lw=1.0)
    for y, d in zip(ys, data):
        x = d["r_within"]
        ax.text(x + (0.008 if x >= 0 else -0.008), y - 0.18, f"{x:+.3f}",
                va="center", ha="left" if x >= 0 else "right",
                fontsize=7.8, family="monospace", color=MUTE)
    ax.set_xlabel("correlation with final score  (episode level, completed episodes only)",
                  fontsize=11)
    NAMES = {"score": "the reported score",
             "no_cw_sat": "score WITHOUT the casework satisfaction term",
             "no_cw_pair": "score WITHOUT either casework term",
             "sat_only": "satisfaction only (no cost subtracted)"}
    ax.set_title(f"Which macro actions predict {NAMES[outcome_kind]}?   "
                 f"significant at FDR q={q}", fontsize=14, fontweight="bold",
                 loc="left", pad=14)
    house_axes(ax)
    h = [Rectangle((0, 0), 1, 1, color=TEAL_RAMP[0], label="proactive (model-initiated)"),
         Rectangle((0, 0), 1, 1, color=RUST_RAMP[1], label="reactive (game-issued task) — symptom, not cause"),
         Rectangle((0, 0), 1, 1, color=GRID, label="pooled, model identity not controlled")]
    ax.legend(handles=h, loc="lower right", frameon=False, fontsize=8.8)
    fig.tight_layout()
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"\n  wrote {out}")


def heatmap(matrix_path, rows, runs, yd, out, min_nonzero):
    """Round x category within-run correlation with score. Exploratory: no FDR here."""
    idx = {(r["run"], int(r["episode"])): i for i, r in enumerate(rows)}
    cats, maxr = set(), 0
    cells = defaultdict(lambda: np.zeros(len(rows)))
    for rec in csv.DictReader(open(matrix_path)):
        key = (rec["run"], int(rec["episode"]))
        i = idx.get(key)
        if i is None:
            continue
        c, rd = rec["category"], int(rec["round"])
        if c == "construction":          # superseded by the typed build_* features
            continue
        cats.add(c); maxr = max(maxr, rd)
        cells[(c, rd)][i] += float(rec["count"])

    cats = sorted(cats)
    M = np.full((len(cats), maxr + 1), np.nan)
    for (c, rd), x in cells.items():
        if (x > 0).sum() < min_nonzero:
            continue
        xd = demean_by_group(x, runs)
        if xd.std() == 0:
            continue
        M[cats.index(c), rd] = pearson(xd, yd)

    fig, ax = plt.subplots(figsize=(15.0, 4.6), dpi=150)
    v = np.nanmax(np.abs(M)) or 0.1
    im = ax.imshow(M, cmap="RdBu_r", vmin=-v, vmax=v, aspect="auto")
    ax.set_xticks(range(0, maxr + 1, 2)); ax.set_xticklabels(range(0, maxr + 1, 2), fontsize=9)
    ax.set_yticks(range(len(cats)))
    ax.set_yticklabels([f"{c}{'' if c.startswith(PROACTIVE) else '   (reactive)'}"
                        for c in cats], fontsize=9.5)
    ax.set_xlabel("round", fontsize=11)
    ax.set_title("Within-run correlation of each action with final score, by round\n"
                 "grey = too few episodes to estimate   ·   reactive rows are game-issued, "
                 "read as symptoms not strategy",
                 fontsize=13, fontweight="bold", loc="left", pad=12)
    cb = fig.colorbar(im, ax=ax, fraction=0.022, pad=0.012)
    cb.set_label("correlation with score", fontsize=9.5)
    ax.set_facecolor("#EEF1F3")
    fig.tight_layout()
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"  wrote {out}")


if __name__ == "__main__":
    main()
