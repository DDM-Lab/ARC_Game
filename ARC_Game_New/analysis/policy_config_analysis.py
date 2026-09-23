#!/usr/bin/env python3
"""Across-config analysis of the scripted policies: which strategy parameters score well?

UNIT = CONFIG, NOT EPISODE. A scripted policy is a deterministic function of state, so within
one config the only variation is the environment's -- an episode-level "which action predicts
score" would collapse into "which situation predicts score". Across configs the parameters
differ by design, which is what makes the comparison meaningful.

Pareto configs (102 of them) are EXCLUDED: that sweep is not currently trusted. 48 configs
remain across 8 policy families.

TWO ESTIMATES, AND THE GAP BETWEEN THEM IS THE POINT.
  pooled       all 48 configs. Dominated by between-family differences, so it mostly says
               "the demand-forecast family scores around 0.67 and does X" -- family identity,
               not strategy.
  within-family features and outcome demeaned by family (only families with >=3 configs
               contribute). Asks: among configs of the SAME policy, does more X help?

n is small (48 pooled, ~40 in the within-family estimate), so this is exploratory. p-values
come from permuting the outcome within family; FDR is Benjamini-Hochberg.

USAGE
  python analysis/policy_config_analysis.py [--perms 20000]
"""
import argparse, csv, os, sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle

from macro_action_analysis import demean_by_group, pearson, bh_fdr, is_proactive
from plot_components import house_axes, INK, MUTE, GRID, TEAL_RAMP, RUST_RAMP


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--configs", default="analysis/policy_configs.csv")
    ap.add_argument("--out", default="analysis/figs_policy")
    ap.add_argument("--perms", type=int, default=20000)
    ap.add_argument("--q", type=float, default=0.10,
                    help="FDR level; 0.10 not 0.05 because n=48 configs is small")
    ap.add_argument("--min-family", type=int, default=3,
                    help="families with fewer configs cannot support within-family demeaning")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    rng = np.random.default_rng(0)

    rows = list(csv.DictReader(open(a.configs)))
    meta = {"config", "family", "episodes", "score", "score_sem", "spend_k"}
    feats = [k for k in rows[0] if k not in meta]

    fam_counts = Counter(r["family"] for r in rows)
    keep_fam = {f for f, n in fam_counts.items() if n >= a.min_family}
    wrows = [r for r in rows if r["family"] in keep_fam]
    print(f"pooled: {len(rows)} configs across {len(fam_counts)} families")
    print(f"within-family: {len(wrows)} configs across {len(keep_fam)} families "
          f"({', '.join(sorted(keep_fam))})\n")

    fam = np.array([r["family"] for r in wrows])
    y_all = np.array([float(r["score"]) for r in rows])
    y_w = demean_by_group(np.array([float(r["score"]) for r in wrows]), fam)
    blocks = [np.where(fam == g)[0] for g in np.unique(fam)]

    res = []
    for f_ in feats:
        x_all = np.array([float(r[f_]) for r in rows])
        x_w = demean_by_group(np.array([float(r[f_]) for r in wrows]), fam)
        if x_w.std() == 0 or x_all.std() == 0:
            continue
        r_w = pearson(x_w, y_w)
        null = np.empty(a.perms)
        for i in range(a.perms):
            yp = y_w.copy()
            for b in blocks:
                yp[b] = rng.permutation(yp[b])
            null[i] = pearson(x_w, yp)
        res.append({"feature": f_, "r_within_family": r_w,
                    "r_pooled": pearson(x_all, y_all),
                    "p": max(float((np.abs(null) >= abs(r_w)).mean()), 1.0 / a.perms),
                    "proactive": is_proactive(f_)})

    keep = bh_fdr([x["p"] for x in res], a.q)
    for x, k in zip(res, keep):
        x["sig"] = bool(k)
    res.sort(key=lambda x: -abs(x["r_within_family"]))
    with open(os.path.join(a.out, "policy_config_effects.csv"), "w", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=list(res[0].keys())); w.writeheader(); w.writerows(res)

    print(f"{sum(1 for x in res if x['sig'])} of {len(res)} features significant "
          f"at FDR q={a.q}\n")
    print(f"{'within-fam':>11} {'pooled':>8} {'p':>8}  kind        feature")
    for x in res[:14]:
        mark = "*" if x["sig"] else " "
        print(f"{x['r_within_family']:>+11.3f} {x['r_pooled']:>+8.3f} {x['p']:>8.4f} {mark} "
              f"{'proactive' if x['proactive'] else 'reactive ':<11} {x['feature']}")

    chart(res, os.path.join(a.out, "policy_config_effects.png"), a.q, len(wrows))


def chart(res, out, q, n_cfg):
    data = [x for x in res if x["sig"]] or res[:14]
    data.sort(key=lambda x: x["r_within_family"])
    n = len(data)
    fig, ax = plt.subplots(figsize=(12.6, max(3.4, 0.36 * n + 2.8)), dpi=150)
    ys = np.arange(n)
    ax.barh(ys - 0.18, [d["r_within_family"] for d in data], height=0.34,
            color=[TEAL_RAMP[0] if d["proactive"] else RUST_RAMP[1] for d in data])
    ax.barh(ys + 0.18, [d["r_pooled"] for d in data], height=0.34, color=GRID)
    ax.set_yticks(ys)
    ax.set_yticklabels([d["feature"].replace("|", "  ·  ") for d in data], fontsize=8.4)
    ax.axvline(0, color=INK, lw=1.0)
    for y, d in zip(ys, data):
        v = d["r_within_family"]
        ax.text(v + (0.012 if v >= 0 else -0.012), y - 0.18, f"{v:+.3f}", va="center",
                ha="left" if v >= 0 else "right", fontsize=7.8,
                family="monospace", color=MUTE)
    ax.set_xlabel("correlation with mean score  (unit = policy config)", fontsize=11)
    ax.set_title(f"Scripted policies: which strategy parameters score well?\n"
                 f"{n_cfg} non-pareto configs   ·   significant at FDR q={q}",
                 fontsize=13.5, fontweight="bold", loc="left", pad=14)
    house_axes(ax)
    h = [Rectangle((0, 0), 1, 1, color=TEAL_RAMP[0], label="proactive (policy-initiated)"),
         Rectangle((0, 0), 1, 1, color=RUST_RAMP[1], label="reactive (game-issued task)"),
         Rectangle((0, 0), 1, 1, color=GRID, label="pooled — family identity not controlled")]
    ax.legend(handles=h, loc="lower right", frameon=False, fontsize=8.8)
    fig.tight_layout()
    fig.savefig(out, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    print(f"\n  wrote {out}")


if __name__ == "__main__":
    main()
