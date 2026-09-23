"""Emit analyze/out/REPORT.md: dataset inventory, leaderboard, per-model
reliability and an explicit caveats section. Deterministic.
"""
from __future__ import annotations

import json

import numpy as np
import pandas as pd

from _common import (config_axes_seen, fixed_config, load_stats, mean_ci,
                     parse_common_args, pick_col, read_frames)


def md_table(headers: list, rows: list) -> str:
    lines = ["| " + " | ".join(headers) + " |",
             "|" + "|".join(["---"] * len(headers)) + "|"]
    for r in rows:
        lines.append("| " + " | ".join(str(x) for x in r) + " |")
    return "\n".join(lines)


def fmt_f(x, spec=".3f"):
    try:
        return format(float(x), spec)
    except (TypeError, ValueError):
        return "NA"


def main(argv=None):
    from figures import add_extra as _fig_extra
    args = parse_common_args(argv, extra=_fig_extra)
    import figures as _fig
    ep, rd = read_frames(args.out)
    stats = load_stats(args.out)
    ep["ok"] = ep["ok"].astype(bool)
    rd["ok"] = rd["ok"].astype(bool)
    ctx = _fig.build_ctx(ep, args)

    L = []
    L.append("# CORA benchmark report\n")

    # ---------------- dataset inventory ----------------
    L.append("## Dataset inventory\n")
    n_ep, n_rd = len(ep), len(rd)
    n_runs = ep.groupby(["source_tree", "run_dir"]).ngroups
    n_models = ep["model"].nunique()
    runs = stats.get("runs", {})
    L.append(f"- episodes: **{n_ep}** (completed: {int(ep['ok'].sum())}, "
             f"failed/errored: {int((~ep['ok']).sum())})")
    L.append(f"- round rows: **{n_rd}**")
    L.append(f"- runs (dirs with episodes.jsonl): **{len(runs) if runs else n_runs}** | "
             f"models: **{n_models}**")
    if stats:
        L.append(f"- episodes files scanned: {len(runs)} | "
                 f"skipped malformed lines: {stats.get('skipped_malformed_lines', 0)}")
    if runs:
        L.append("")
        L.append("Run inventory (every scanned run dir; `ok` = completed episodes):")
        rows = [[rid, v["source_tree"], v["run_dir"], int(v["episodes"]), int(v["ok"]),
                 v.get("skipped_lines", 0)] for rid, v in sorted(runs.items())]
        L.append(md_table(["run", "tree", "run dir / sub-runs", "episodes", "ok",
                           "skipped lines"], rows))
    L.append("")
    L.append("Config value inventory (across all episodes; a value that appears "
             "for only one model-config is flagged in caveats below):\n")
    rows = []
    for a in config_axes_seen(ep):
        vc = ep[a].dropna().astype(str).value_counts().sort_index()
        rows.append([f"`{a}`", "; ".join(f"{k} (×{int(v)})" for k, v in vc.items()) or "—"])
    L.append(md_table(["axis", "levels (×episodes)"], rows))
    L.append("")

    # ---------------- leaderboard ----------------
    ok = ep["ok"]
    fixes = {"history": args.history, "max_tokens": args.max_tokens,
             "reasoning_effort": args.effort, "transfers": args.transfers,
             "prompt_sha": args.prompt_sha}
    sub, label = fixed_config(ep, ok, fixes)
    rc = _fig.reward_col(ep)
    L.append("## Leaderboard (mean totalReward, completed episodes)\n")
    L.append(f"**Fixed config:** {label} — models appearing under other configs "
             f"are *not* merged in. Baselines (non-LLM) are listed separately, "
             f"averaged over all their episodes (no config axes apply to them).\n")
    rows = []
    for m in ctx["learning"]:
        v = pd.to_numeric(sub.loc[sub["model"] == m, rc], errors="coerce").dropna()
        if v.empty:
            rows.append([m, 0, "NA", "NA", "no episodes at this config"])
            continue
        mu, hw, n = mean_ci(list(v))
        rows.append([m, n, fmt_f(mu), fmt_f(hw), ""])
    rows = sorted(rows, key=lambda r: float(r[2]) if r[2] != "NA" else -9e9)
    L.append(md_table(["model", "n episodes", "mean", "±95% CI", "note"], rows))
    L.append("")
    for m in sorted(ctx["baselines"]):
        sel = ep.loc[(ep["model"] == m) & ok, rc]
        v = pd.to_numeric(sel, errors="coerce").dropna()
        mu, hw, n = mean_ci(list(v))
        L.append(f"- baseline **{m}**: mean {fmt_f(mu)} (±{fmt_f(hw)}), n={n} episodes")
    L.append("")

    # ---------------- reliability ----------------
    L.append("## Per-model reliability\n")
    models = sorted(set(ep["model"].dropna()))
    n_ep_m = ep.groupby("model")["episode"].count()
    n_ok_m = ep[ok].groupby("model")["episode"].count()
    af = pick_col(ep, "actionFailures", "summary_actionFailures")
    ar = pick_col(ep, "actionsRequested", "summary_actionsRequested")
    rows = []
    for m in models:
        n = int(n_ep_m.get(m, 0))
        nok = int(n_ok_m.get(m, 0))
        rdd = rd[rd["model"] == m]
        po = rdd["parsed_ok"].dropna() if "parsed_ok" in rdd else pd.Series([], dtype=float)
        if len(po):
            par = f"{float((po.astype(bool).mean())):.2%}"
        else:
            par = "no LLM"
        if "n_cmd_errors" in rdd:
            ce = f"{float(pd.to_numeric(rdd['n_cmd_errors'], errors='coerce').fillna(0).mean()):.3f}"
        else:
            ce = "NA"
        if af and ar:
            f_ = pd.to_numeric(ep.loc[(ep["model"] == m) & ok, af], errors="coerce").fillna(0).sum()
            rq = pd.to_numeric(ep.loc[(ep["model"] == m) & ok, ar], errors="coerce").sum()
            fr = f"{float(f_) / float(rq):.3f}" if rq else "NA"
        else:
            fr = "NA"
        tag = " (baseline)" if m in ctx["baselines"] else ""
        rows.append([m + tag, f"{nok}/{n} ({nok / n:.0%})" if n else "0/0",
                     par, ce, fr])
    L.append(md_table(["model", "episodes completed", "parsed_ok rate",
                       "cmd errors/round", "failures/requested"], rows))
    L.append("")

    # ---------------- caveats ----------------
    L.append("## Caveats\n")
    caveats = []

    # runs (including 0-completed and 0-episode ones) with <100% completion
    if runs:
        partial = [(rid, v) for rid, v in sorted(runs.items())
                   if v["episodes"] and v["ok"] < v["episodes"]
                   or (not v["episodes"] and v.get("skipped_lines"))]
    else:
        g = ep.groupby("run_id").agg(n=("episode", "count"), ok=("ok", "sum"))
        partial = [(rid, {"episodes": int(r_["n"]), "ok": int(r_["ok"]),
                          "skipped_lines": 0})
                   for rid, r_ in g[g["ok"] < g["n"]].sort_values("ok").iterrows()]
    if partial:
        caveats.append("Runs with **<100% episode completion** (failures are excluded "
                       "from performance stats, not scored as 0):")
        for rid, v in partial:
            note = " — **zero completed episodes**" if v["ok"] == 0 else ""
            caveats.append(f"  - `{rid}`: {v['ok']}/{v['episodes']}{note}")

    # single-level config axes
    single = [a for a in config_axes_seen(ep)
              if ep[a].dropna().nunique() <= 1]
    if single:
        def _one(a):
            v = ep[a].dropna().astype(str)
            return f"`{a}` = {v.iloc[0]}" if len(v) else f"`{a}` = <none>"
        caveats.append("Config axes with only one level observed (no sensitivity "
                       "can be measured on them): " + ", ".join(_one(a) for a in single))

    # models under a single prompt_sha
    if "prompt_sha" in ep.columns:
        one = []
        for m, gg in ep.groupby("model"):
            shas = set(gg["prompt_sha"].dropna().astype(str))
            if len(shas) <= 1:
                one.append(f"{m} ({', '.join(sorted(shas))})")
        if one:
            caveats.append("Models appearing under a single prompt_sha (prompt "
                           "cannot be separated from model for these): " +
                           "; ".join(sorted(one)))

    # baselines excluded from model-vs-model comparisons
    if ctx["baselines"]:
        caveats.append("Baselines (non-LLM: no max_tokens) — "
                       + ", ".join(sorted(ctx["baselines"]))
                       + " — are plotted as reference, never merged with model bars.")

    # skipped lines
    skipped = stats.get("skipped_malformed_lines", 0) if stats else 0
    if skipped:
        caveats.append(f"{skipped} malformed JSON line(s) skipped during load.")

    if not caveats:
        caveats.append("None detected for this dataset.")
    L.extend(caveats)
    L.append("")

    out = args.out / "REPORT.md"
    args.out.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(L))
    print(f"wrote {out}")


if __name__ == "__main__":
    main()
