# CORA analysis toolkit

Turns CORA benchmark results trees (`benchmark_results/cluster*`) into tidy
CSVs, publication-quality figures, and a report. Re-runnable on any future
results tree with zero edits: nothing is hard-coded to model or run names.
Baseline (non-LLM) policies are detected from the data (episodes with no
`max_tokens`) and can be overridden with `--baseline NAME` / `--no-baseline`.

Dependencies: Python 3.9+, `pandas`, `matplotlib` (nothing else; no network
needed at run time; only `analyze/out/` is written).

Run on a fresh results tree:

```bash
python3 analyze/load.py --results 'benchmark_results/cluster*' \
                        --results 'benchmark_results/cluster_api*' --out analyze/out
python3 analyze/figures.py --out analyze/out
python3 analyze/report.py --out analyze/out
```

Outputs in `analyze/out/`: `episodes.csv`, `rounds.csv`, `LOAD_STATS.json`,
`REPORT.md`, `figures/*.png` and `*.pdf` (200 dpi).

- `episodes.csv` — one row per episode: run identity, all config fields, all
  `summary` fields (a `summary_` prefix is added only where a top-level and a
  summary field collide), plus derived `ok` (no `error`, `roundsPlayed > 0`)
  and `n_rounds`.
- `rounds.csv` — one row per run×episode×round. The heavy per-round payloads
  (`obs`, `raw`, `reasoning`, `reasoningTrace`) are dropped for size; all other
  fields (including new/unknown ones, `comps_*`, `act_*`, `cmd_errors`) are
  carried through.
- Figures condition on a *fixed configuration* (most common level of each
  config axis by default; override with `--history/--max-tokens/--effort/
  --transfers/--prompt-sha` to `figures.py`). The fixed config is stated in
  every figure title. `prompt_sha` is treated as a first-class grouping key:
  runs with different prompts are never merged.
- Malformed JSON lines are skipped and counted (`LOAD_STATS.json`, reported in
  `REPORT.md`); a malformed *schema* (e.g. `rounds` not a list) aborts loudly.
- CSVs are byte-identical across re-runs on the same input.
