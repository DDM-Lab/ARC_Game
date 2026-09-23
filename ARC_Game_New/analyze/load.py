"""Turn CORA benchmark run directories into tidy CSVs.

Usage:
    python analyze/load.py --results 'benchmark_results/cluster*' \
                           --results 'benchmark_results/cluster_api*' --out analyze/out

Writes <out>/episodes.csv (one row per episode), <out>/rounds.csv
(one row per run x episode x round) and <out>/LOAD_STATS.json.
Deterministic: same input tree => byte-identical CSVs.
"""
from __future__ import annotations

import json

import pandas as pd

from _common import (ep_ok, expand_results, parse_common_args, run_identity,
                     check_schema, check_round)

# Ordering hints only; the union of keys actually seen in the data wins and
# unknown/new keys are carried through, never dropped.
CORE = ["run", "source_tree", "run_dir", "run_id", "model", "episode", "ok", "n_rounds"]
KNOWN_TOP = ["error", "action_format", "obs_encoding", "history", "transfers",
             "system_variant", "reasoning_effort", "max_tokens", "prompt_sha",
             "image_mode", "prompt_pack", "temperature", "temperature_sent",
             "parse_failures", "terminated", "show_impacts", "system_prompt"]
KNOWN_SUMMARY = ["totalReward", "finalSat", "finalBudget", "finalEff", "scoreFormula", "finalScore",
                 "foodFulfillRate", "lodgingFulfillRate", "foodResolved",
                  "lodgingResolved", "actionsRequested",
                 "actionsExecuted", "actionFailures", "invalidIndices",
                 "minBudget", "wentNegative", "everBuilt", "everHired",
                 "terminated", "roundsPlayed", "rewardWeights"]
KNOWN_ROUND = ["r", "reward", "sumR", "sat", "budget", "satScore", "costEff", "eff", "formula",
               "foodFul", "foodRes", "lodgFul", "lodgRes", "nSel", "nReq",
               "nFail", "parsed_ok", "reasoningTokens", "note"]
# Large per-round payloads: excluded from rounds.csv for size; the column set
# of the CSV is everything else in the round object, carried through.
CSV_HEAVY_ROUND = {"obs", "raw", "reasoning", "reasoningTrace"}


def _order_columns(rows: list, known: list, id_cols: list) -> list:
    keys = set()
    for row in rows:
        keys.update(row)
    keys -= set(id_cols)
    known = [k for k in known if k in keys]
    return id_cols + known + sorted(keys - set(known))


def _episode_row(ident: dict, obj: dict) -> dict:
    row = dict(ident)
    top = [k for k in obj if k not in ("rounds", "summary")]
    for k in [k for k in KNOWN_TOP if k in top] + sorted(set(top) - set(KNOWN_TOP)):
        row[k] = obj[k]
    for k, v in (obj.get("summary") or {}).items():
        row[k if k not in row else "summary_" + k] = v
    row["ok"] = ep_ok(obj)
    row["n_rounds"] = len(obj["rounds"])
    return row


def _read_episodes(path, ident: dict, rs: dict):
    rows = []
    n_bad = 0
    for line in path.read_text().splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            obj = json.loads(line)
        except json.JSONDecodeError:
            n_bad += 1
            rs["skipped_lines"] += 1
            continue
        check_schema(obj, str(path))
        row = _episode_row(ident, obj)
        rows.append(row)
        rs["episodes"] += 1
        rs["ok"] += int(row["ok"])
    return rows, n_bad


def load_runs(paths) -> pd.DataFrame:
    """Accept globs/dirs (any tree depth); return one tidy row per episode."""
    rows = []
    run_stats = {}
    n_bad = 0
    for tree, file in expand_results(paths):
        ident = run_identity(tree, file, file)
        rs = run_stats.setdefault(
            ident["run_id"],
            {"source_tree": tree.name, "run_dir": ident["run_dir"],
             "episodes": 0, "ok": 0, "skipped_lines": 0})
        part, bad = _read_episodes(file, ident, rs)
        rows.extend(part)
        n_bad += bad
    df = pd.DataFrame(rows)
    if not df.empty:
        cols = _order_columns(rows, KNOWN_TOP + [
            "summary_" + k if k == "terminated" else k for k in KNOWN_SUMMARY], CORE)
        df = df[[c for c in dict.fromkeys(cols) if c in df.columns]]
    df = df.sort_values(["source_tree", "run_dir", "episode"], kind="mergesort")
    df.attrs["n_skipped_lines"] = n_bad
    df.attrs["run_stats"] = run_stats
    return df


def load_rounds(paths) -> pd.DataFrame:
    """One row per (run, model, episode, round); unknown keys carried through.

    CSV export drops the heavy per-round payloads in CSV_HEAVY_ROUND.
    """
    files = expand_results(paths)
    rows = []
    for tree, file in files:
        ident = run_identity(tree, file, file)
        for line in file.read_text().splitlines():
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            check_schema(obj, str(file))
            for rd in obj["rounds"]:
                check_round(rd, str(file))
                row = dict(ident)
                row["model"] = obj.get("model")
                row["episode"] = obj.get("episode")
                row["ok"] = ep_ok(obj)
                for k, v in rd.items():
                    if k in CSV_HEAVY_ROUND:
                        continue
                    if k == "comps" and isinstance(v, dict):
                        row.update({f"comps_{kk}": vv for kk, vv in v.items()})
                    elif k == "actCats" and isinstance(v, dict):
                        row.update({f"act_{kk}": vv for kk, vv in v.items()})
                    elif k == "cmdErrors":
                        if isinstance(v, list):
                            row["n_cmd_errors"] = len(v)
                            row["cmd_errors"] = json.dumps(v, ensure_ascii=False, sort_keys=True)
                        else:
                            row["cmd_errors"] = v
                    else:
                        row[k] = v
                rows.append(row)
    df = pd.DataFrame(rows)
    if not df.empty:
        cols = _order_columns(rows, KNOWN_ROUND + ["n_cmd_errors", "cmd_errors"], CORE)
        df = df[[c for c in cols if c in df.columns]]
    df.attrs["n_rows"] = len(df)
    return df.sort_values(["source_tree", "run_dir", "episode", "r"], kind="mergesort")


def write_outputs(df_ep: pd.DataFrame, df_rd: pd.DataFrame, out) -> dict:
    from pathlib import Path
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    df_ep.to_csv(out / "episodes.csv", index=False)
    df_rd.to_csv(out / "rounds.csv", index=False)
    models = sorted(df_ep["model"].dropna().unique().tolist()) if "model" in df_ep else []
    stats = {
        "episodes_rows": len(df_ep),
        "rounds_rows": len(df_rd),
        "skipped_malformed_lines": int(df_ep.attrs.get("n_skipped_lines", 0)),
        "runs": df_ep.attrs.get("run_stats", {}),
        "models": models,
    }
    (out / "LOAD_STATS.json").write_text(json.dumps(stats, indent=1, sort_keys=True) + "\n")
    return stats


def main(argv=None):
    args = parse_common_args(argv)
    ep = load_runs(args.results)
    rd = load_rounds(args.results)
    stats = write_outputs(ep, rd, args.out)
    print(f"episodes: {stats['episodes_rows']} rows -> {args.out}/episodes.csv")
    print(f"rounds:   {stats['rounds_rows']} rows -> {args.out}/rounds.csv")
    print(f"runs: {len(stats['runs'])} | models: {len(stats['models'])} | "
          f"skipped malformed lines: {stats['skipped_malformed_lines']}")


if __name__ == "__main__":
    main()
