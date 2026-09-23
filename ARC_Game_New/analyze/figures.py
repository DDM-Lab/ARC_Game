"""Publication-quality figures for the CORA benchmark.

Reads the CSVs produced by load.py (auto-runs it if they are missing) and
writes PNG + PDF (>=200 dpi) into <out>/figures/. Nothing is hard-coded to
today's model/run names; baselines are detected from the data (no LLM token
budget) and can be overridden with --baseline / --no-baseline.

Every figure states the fixed configuration in its title, labels axes with
units, and annotates n. Colour is a stable, colour-blind-safe function of the
sorted model name; line/marker style encodes baseline-ness, so no meaning
rests on colour alone.
"""
from __future__ import annotations

import numpy as np
import pandas as pd

from _common import (axis_levels, detect_baseline_models, fixed_config, fmt_level,
                     mean_ci, norm_series, parse_common_args, read_frames, save_fig,
                     short_name, set_style, series_style, CONFIG_AXES, PROMPT_AXIS)

set_style()
import matplotlib.pyplot as plt  # noqa: E402

CAT_COLORS = ["#0072B2", "#E69F00", "#009E73", "#CC79A7", "#56B4E9",
              "#D55E00", "#F0E442", "#4D4D4D", "#999999", "#82B4D8",
              "#EBBD5E", "#7ECBA8", "#DCA9C4", "#A8D5F2"]
HATCHES = ["", "//", "\\\\", "..", "++", "xx", "OO", "oo", "--", "/"]


def pick_col(df, *names):
    for n in names:
        if n in df.columns:
            return n
    return None


def add_extra(p):
    p.add_argument("--history", type=int, default=None)
    p.add_argument("--max-tokens", type=int, default=None)
    p.add_argument("--effort", default=None,
                   help="reasoning_effort value to condition on")
    p.add_argument("--transfers", default=None)
    p.add_argument("--prompt-sha", default=None)
    p.add_argument("--baseline", action="append", default=[],
                   help="repeatable: treat model as non-learning baseline")
    p.add_argument("--no-baseline", action="store_true",
                   help="disable automatic baseline detection")
    p.add_argument("--dpi", type=int, default=200)


def build_ctx(ep: pd.DataFrame, args) -> dict:
    baselines = set() if args.no_baseline else detect_baseline_models(ep)
    baselines.update(args.baseline)
    baselines &= set(ep["model"].dropna())
    styles = series_style(sorted(set(ep["model"].dropna())))
    for m in baselines:
        c, mk, _ = styles[m]
        styles[m] = (c, mk, ":")
    return {"baselines": baselines, "styles": styles,
            "learning": sorted(set(ep["model"].dropna()) - baselines)}


def reward_col(ep: pd.DataFrame):
    c = pick_col(ep, "totalReward", "summary_totalReward")
    if c is None:
        raise SystemExit("error: no totalReward column in episodes.csv")
    return c


def model_reward_stats(ep: pd.DataFrame, ok: pd.Series, models):
    c = reward_col(ep)
    out = {}
    vals = pd.to_numeric(ep.loc[ok, c], errors="coerce")
    for m in models:
        v = vals[ep.loc[ok, "model"] == m].dropna()
        out[m] = mean_ci(list(v))
    return out


# ---------------------------------------------------------------------------
# 1. Leaderboard
# ---------------------------------------------------------------------------
def fig_leaderboard(ep, ctx, args, out) -> None:
    ok = ep["ok"] == True  # noqa: E712
    fixes = {"history": args.history, "max_tokens": args.max_tokens,
             "reasoning_effort": args.effort, "transfers": args.transfers,
             PROMPT_AXIS: args.prompt_sha}
    sub, label = fixed_config(ep, ok, fixes)
    models = [m for m in ctx["learning"] if (sub["model"] == m).any()]
    stats = {m: model_reward_stats(sub, sub["ok"], [m])[m] for m in models}
    stats = {m: s for m, s in stats.items() if s[2] > 0 and not np.isnan(s[0])}
    stats = dict(sorted(stats.items(), key=lambda kv: kv[1][0]))

    fig, ax = plt.subplots(figsize=(9, max(4, 0.42 * (len(stats) + 2))))
    for i, (m, (mu, hw, n)) in enumerate(stats.items()):
        c, mk, ls = ctx["styles"][m]
        ax.barh(i, mu, xerr=hw or 0, color=c, edgecolor="k", alpha=0.85,
                height=0.6)
        ax.text(mu + (hw or 0) + max(0.02 * mu, 0.01), i, f"n={n}",
                va="center", fontsize=7)
    ax.set_yticks(range(len(stats)))
    ax.set_yticklabels([short_name(m) for m in stats], fontsize=8)
    ax.set_xlabel("mean totalReward per episode (unitless)")
    ax.set_title(f"Leaderboard — mean totalReward per model\n"
                 f"fixed config: {label}\n"
                 f"completed episodes only; n = episodes per model", fontsize=9)
    for m in sorted(ctx["baselines"]):
        s = model_reward_stats(ep, ep["ok"] == True, [m])[m]  # noqa: E712
        if s[2] == 0 or np.isnan(s[0]):
            continue
        c, _, _ = ctx["styles"][m]
        ax.axhline(s[0], color=c, ls=":", lw=1.2)
        ax.text(ax.get_xlim()[1] * 0.98, s[0], f" baseline {short_name(m)} "
                f"(mean over all configs, n={s[2]})", ha="right", va="bottom",
                fontsize=7, color="k")
    save_fig(fig, out, "leaderboard")


# ---------------------------------------------------------------------------
# 2. Config sensitivity
# ---------------------------------------------------------------------------
def fig_config_sensitivity(ep, ctx, args, out) -> None:
    ok = ep["ok"] == True  # noqa: E712
    fixes = {"history": args.history, "max_tokens": args.max_tokens,
             "reasoning_effort": args.effort, "transfers": args.transfers,
             PROMPT_AXIS: args.prompt_sha}
    # prompt_sha is a first-class grouping key: pin it; vary one axis at a time
    # and hold the other axes at their most common level (stated in each panel).
    base = ep[ok]
    nser = {a: norm_series(base[a]) for a in CONFIG_AXES}
    nser[PROMPT_AXIS] = norm_series(base[PROMPT_AXIS])
    pf = fixes[PROMPT_AXIS]
    if pf is None:
        vc = nser[PROMPT_AXIS].dropna().value_counts()
        pf = vc.index[0] if len(vc) else None
    if pf is not None:
        base = base[nser[PROMPT_AXIS] == pf]
        for a in list(nser):
            nser[a] = nser[a].loc[base.index]
    mods = {}
    for a in CONFIG_AXES:
        vc = nser[a].dropna().value_counts()
        mods[a] = vc.index[0] if len(vc) else None
    c = reward_col(ep)

    fig, axes = plt.subplots(2, 2, figsize=(11, 9))
    models = ctx["learning"]
    for ax, target in zip(axes.ravel(), CONFIG_AXES):
        levels = axis_levels(nser[target])
        for m in models:
            c_, mk, _ = ctx["styles"][m]
            xs, ys, hs, ns = [], [], [], []
            for lv in levels:
                mask = (base["model"] == m) & (nser[target] == lv)
                for other, mv in mods.items():
                    if other == target or mv is None:
                        continue
                    mask &= nser[other] == mv
                v = pd.to_numeric(base.loc[mask, c], errors="coerce").dropna()
                if v.empty:
                    continue
                mu, hw, n = mean_ci(list(v))
                xs.append(lv); ys.append(mu); hs.append(hw); ns.append(n)
            if not xs:
                continue
            ax.errorbar(range(len(xs)), ys, yerr=hs, marker=mk, ms=4,
                        color=c_, ls="-", lw=0.8, alpha=0.85, capsize=2)
            for x, n in zip(range(len(xs)), ns):
                ax.annotate(f"n={n}", (x, ys[x]), textcoords="offset points",
                            xytext=(0, -11), ha="center", fontsize=5.5, alpha=0.8)
        held = [f"{a}={mods[a]}" for a in CONFIG_AXES if a != target
                and mods.get(a) is not None and a != PROMPT_AXIS]
        ax.set_xticks(range(len(levels)))
        ax.set_xticklabels(levels, rotation=45, ha="right", fontsize=7)
        ax.set_ylabel("mean totalReward (unitless)")
        ax.set_title(f"vs {target}   [held: {', '.join(held)}]", fontsize=8)
    if ctx["baselines"]:
        fig.suptitle(
            f"Config sensitivity — reward by model, prompt_sha={pf} fixed, other axes at most common level\n"
            f"(baselines {', '.join(sorted(ctx['baselines']))} excluded: non-LLM policies have no such config)",
            fontsize=9)
    else:
        fig.suptitle(f"Config sensitivity — prompt_sha={pf} fixed, other axes at most common level", fontsize=9)
    save_fig(fig, out, "config_sensitivity")


# ---------------------------------------------------------------------------
# 3. Learning within an episode
# ---------------------------------------------------------------------------
def fig_learning(rd, ep, ctx, args, out) -> None:
    rd = rd[rd["ok"] == True]  # noqa: E712
    models = sorted(set(rd["model"].dropna()))
    fig, ax = plt.subplots(figsize=(9, 5.5))
    for m in models:
        c, mk, ls = ctx["styles"][m]
        sel = rd[rd["model"] == m]
        if sel.empty:
            continue
        grp = sel.groupby("r")["sumR"]
        means = grp.mean().sort_index()
        sds = grp.std(ddof=1).reindex(means.index).fillna(0)
        ns = grp.count().reindex(means.index)
        rs = means.index
        ax.plot(rs, means, color=c, marker=mk, ms=3, ls=ls if ls == ":" else "-",
                lw=1 if ls == ":" else 0.8, alpha=0.9,
                label=short_name(m) + (" (baseline)" if m in ctx["baselines"] else ""))
        ax.fill_between(rs, means - 1.96 * sds / np.sqrt(ns.replace(0, 1)),
                        means + 1.96 * sds / np.sqrt(ns.replace(0, 1)),
                        color=c, alpha=0.12)
        nn = sel["episode"].nunique()
        ax.annotate(f"n={nn}ep", (rs[-1], means.iloc[-1]),
                    textcoords="offset points", xytext=(4, 0), fontsize=6, color=c)
    ax.set_xlabel("round index (turns)")
    ax.set_ylabel("cumulative reward sumR (unitless)")
    ax.set_title("Learning within an episode — mean cumulative reward per round\n"
                 "(completed episodes only; shaded band = 95% CI across episodes)")
    ax.legend(fontsize=6.5, ncols=2, loc="upper left", framealpha=0.8)
    save_fig(fig, out, "learning_within_episode")


# ---------------------------------------------------------------------------
# 4. Score decomposition
# ---------------------------------------------------------------------------
def fig_components(rd, ep, ctx, args, out) -> None:
    rd = rd[rd["ok"] == True]  # noqa: E712
    models = sorted(set(rd["model"].dropna()))
    comps = sorted(c for c in rd.columns if c.startswith("comps_"))
    if "costEff" in rd.columns:
        comps = [c for c in comps if c != "comps_score"] + ["comps_score", "costEff"]
    ne = len(comps)
    ncols = 3
    nrows = int(np.ceil(ne / ncols))
    fig, axes = plt.subplots(nrows, ncols, figsize=(4.2 * ncols, 3.0 * nrows))
    axes = np.atleast_1d(axes).ravel()
    n_eps = rd.groupby("model")["episode"].nunique() if len(rd) else pd.Series(dtype=int)
    for ax, col in zip(axes, comps):
        vals = pd.to_numeric(rd[col], errors="coerce")
        for i, m in enumerate(models):
            v = vals[rd["model"] == m]
            mu = v.mean()
            n = int(n_eps.get(m, 0))
            c, _, _ = ctx["styles"][m]
            if not v.empty and not np.isnan(mu):
                ax.bar(i, mu, color=c, alpha=0.85, width=0.7)
                spread = max(abs(v.min()), abs(v.max()), 1e-9) * 0.06 + 1e-9
                ax.text(i, mu + (spread if mu >= 0 else -spread), f"n={n}",
                        ha="center", va="bottom" if mu >= 0 else "top",
                        fontsize=5, alpha=0.75)
            else:
                ax.text(i, 0, f"n/a", ha="center", va="bottom", fontsize=5, alpha=0.5)
        ax.set_xticks(range(len(models)))
        ax.set_xticklabels([short_name(m) for m in models], rotation=90, fontsize=5.5)
        if col == "comps_score":
            name = "total per-round score"
        elif col == "costEff":
            name = "costEff (per-round)"
        else:
            name = col.replace("comps_", "")
        ax.set_title(name, fontsize=7)
        ax.tick_params(axis="y", labelsize=6)
    for ax in axes[ne:]:
        ax.axis("off")
    fig.suptitle("Score decomposition — mean per-round reward component, by model\n"
                 "(completed episodes; number above each bar = n episodes)", fontsize=9)
    save_fig(fig, out, "score_decomposition")


# ---------------------------------------------------------------------------
# 5. Behaviour profile
# ---------------------------------------------------------------------------
def fig_actions(rd, ep, ctx, args, out) -> None:
    rd = rd[rd["ok"] == True]  # noqa: E712
    models = sorted(set(rd["model"].dropna()))
    cats = sorted(c for c in rd.columns if c.startswith("act_"))
    if not cats:
        print("skip actions figure: no act_* columns")
        return
    n_eps = rd.groupby("model")["episode"].nunique()
    means = rd.groupby("model")[cats].mean()
    means = means.reindex(models).fillna(0)
    fig, ax = plt.subplots(figsize=(9, 5.5))
    bottoms = np.zeros(len(models))
    for j, cat in enumerate(cats):
        col = means[cat].values
        ax.bar(range(len(models)), col, bottom=bottoms,
               color=CAT_COLORS[min(j, len(CAT_COLORS) - 1)],
               hatch=HATCHES[min(j, len(HATCHES) - 1)], edgecolor="k", lw=0.4,
               label=cat.replace("act_", ""))
        bottoms += col
    ax.set_xticks(range(len(models)))
    ax.set_xticklabels([short_name(m) for m in models], rotation=60, ha="right", fontsize=6.5)
    ax.set_ylabel("mean actions per round (actions / round)")
    for i, m in enumerate(models):
        ax.text(i, bottoms[i] * 1.01, f"n={int(n_eps.get(m, 0))}ep",
                ha="center", fontsize=6)
    ax.set_title("Behaviour profile — mean selected actions per round by category\n"
                 "(completed episodes; stacked by action category)")
    ax.legend(fontsize=6, ncols=2, loc="upper right")
    save_fig(fig, out, "behaviour_profile")


# ---------------------------------------------------------------------------
# 6. Reliability
# ---------------------------------------------------------------------------
def fig_reliability(ep, rd, ctx, args, out) -> None:
    models = sorted(set(ep["model"].dropna()))
    n_ep = ep.groupby("model")["episode"].count()
    n_ok = ep[ep["ok"] == True].groupby("model")["episode"].count()  # noqa: E712
    completion = (n_ok.reindex(models).fillna(0) / n_ep)
    if "parsed_ok" in rd.columns:
        rdv = rd[rd["parsed_ok"].notna()]
        parsed = rdv.groupby("model")["parsed_ok"].mean().reindex(models)
    else:
        parsed = pd.Series(np.nan, index=models)
    if "n_cmd_errors" in rd.columns:
        cerr = rd[rd["ok"] == True].assign(n_cmd_errors=lambda d: d["n_cmd_errors"].fillna(0))  # noqa: E712
        cerr = cerr.groupby("model")["n_cmd_errors"].mean().reindex(models).fillna(0)
    else:
        cerr = pd.Series(0.0, index=models)
    af = pick_col(ep, "actionFailures", "summary_actionFailures")
    ar = pick_col(ep, "actionsRequested", "summary_actionsRequested")
    fails = ep[ep["ok"] == True].groupby("model")[af].sum() if af else pd.Series(0.0, index=models)  # noqa: E712
    reqs = ep[ep["ok"] == True].groupby("model")[ar].sum() if ar else pd.Series(0.0, index=models)  # noqa: E712
    failrate = (fails.reindex(models).fillna(0) / reqs.reindex(models).replace(0, np.nan)).fillna(0)

    panels = [
        ("episode completion rate", completion, "fraction of episodes completed (0–1)"),
        ("parsed_ok rate (rounds with a parse decision)", parsed,
         "fraction of rounds parsed successfully (0–1)"),
        ("cmd errors per round", cerr, "mean command-parser errors per round"),
        ("action failures per requested", failrate,
         "sum(actionFailures) / sum(actionsRequested) (0–1)"),
    ]
    fig, axes = plt.subplots(2, 2, figsize=(12, 8))
    for ax, (title, ser, ylabel) in zip(axes.ravel(), panels):
        vals = ser.reindex(models)
        ax.bar(range(len(models)), vals.fillna(0), color="#0072B2", alpha=0.8, width=0.6)
        ax.set_xticks(range(len(models)))
        ax.set_xticklabels([short_name(m) for m in models], rotation=60, ha="right", fontsize=6)
        for i, m in enumerate(models):
            v = vals.get(m, np.nan)
            if v is None or (isinstance(v, float) and np.isnan(v)):
                ax.text(i, 0.01, "no LLM\nn={}".format(int(n_ep.get(m, 0))),
                        ha="center", fontsize=5.5)
            else:
                ax.text(i, v, f"{v:.2f}\nn={int(n_ep.get(m, 0))}",
                        ha="center", va="bottom", fontsize=5.5)
        ax.set_title(title, fontsize=8)
        ax.set_ylabel(ylabel)
    fig.suptitle("Reliability — per model (all episodes; n annotated per bar)", fontsize=9)
    save_fig(fig, out, "reliability")


# ---------------------------------------------------------------------------
# 7. Budget trajectory
# ---------------------------------------------------------------------------
def fig_budget(rd, ep, ctx, args, out) -> None:
    rd = rd[rd["ok"] == True]  # noqa: E712
    models = sorted(set(rd["model"].dropna()))
    gr = rd[rd["budget"].notna()].groupby(["model", "r"])["budget"]
    fig, ax = plt.subplots(figsize=(9, 5.5))
    for m in models:
        c, mk, ls = ctx["styles"][m]
        sel = rd[(rd["model"] == m) & rd["budget"].notna()]
        if sel.empty:
            continue
        grp = sel.groupby("r")["budget"]
        med, q1, q3 = grp.median(), grp.quantile(0.25), grp.quantile(0.75)
        rs = med.index
        dashed = ":" if (ls == ":" or m in ctx["baselines"]) else "-"
        ax.plot(rs, med, color=c, marker=mk, ms=3, lw=1 if dashed == ":" else 0.8,
                ls=dashed, alpha=0.9,
                label=short_name(m) + (" (baseline)" if m in ctx["baselines"] else ""))
        ax.fill_between(rs, q1, q3, color=c, alpha=0.12)
        nn = sel["episode"].nunique()
        ax.annotate(f"n={nn}ep", (rs[-1], med.iloc[-1]),
                    textcoords="offset points", xytext=(4, 0), fontsize=6, color=c)
    ax.axhline(0, color="red", ls="--", lw=1)
    ax.text(ax.get_xlim()[0] + 0.3, 0, " 0 (negative budget allowed; recovery distinguishes policies)",
            fontsize=6.5, color="red", va="bottom")
    ax.set_xlabel("round index (turns)")
    ax.set_ylabel("budget (game units)")
    ax.set_title("Budget trajectory — median per round (IQR band)\n"
                 "(completed episodes only)")
    ax.legend(fontsize=6.5, ncols=2, loc="best", framealpha=0.8)
    save_fig(fig, out, "budget_trajectory")


def main(argv=None):
    import load as _load
    args = parse_common_args(argv, extra=add_extra)
    try:
        ep, rd = read_frames(args.out)
    except SystemExit:
        _load.main(["--results", *args.results, "--out", str(args.out)])
        ep, rd = read_frames(args.out)
    ep["ok"] = ep["ok"].astype(bool)
    rd["ok"] = rd["ok"].astype(bool)
    ctx = build_ctx(ep, args)
    import matplotlib
    matplotlib.rcParams["savefig.dpi"] = args.dpi
    print(f"models: {len(ctx['learning'])} learning, {len(ctx['baselines'])} baseline "
          f"({', '.join(sorted(ctx['baselines'])) or 'none'})")
    fig_leaderboard(ep, ctx, args, args.out)
    fig_config_sensitivity(ep, ctx, args, args.out)
    fig_learning(rd, ep, ctx, args, args.out)
    fig_components(rd, ep, ctx, args, args.out)
    fig_actions(rd, ep, ctx, args, args.out)
    fig_reliability(ep, rd, ctx, args, args.out)
    fig_budget(rd, ep, ctx, args, args.out)
    print(f"wrote 7 figures -> {args.out}/figures/")


if __name__ == "__main__":
    main()
