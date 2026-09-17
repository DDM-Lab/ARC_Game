# Task: CORA benchmark analysis + figures

You are writing a small, reusable analysis toolkit for an LLM benchmark called CORA. The
benchmark has an LLM play a turn-based disaster-response resource game; each run produces
per-episode JSONL. Your job: turn ~68 run directories into a tidy dataset and a set of
publication-quality figures a researcher can show colleagues.

**The scripts must be re-runnable on future data with zero edits.** The current results were
produced with an old game binary and WILL be regenerated. Nothing may be hard-coded to
today's model names, run names, or directory list.

---

## 1. Data location and shape

Two trees, same format:

```
benchmark_results/cluster/<run_name>/     53 dirs — local open-weights models via vLLM
benchmark_results/cluster_api/<run_name>/ 15 dirs — API models + non-learning baselines
```

Each run directory contains:
- `episodes.jsonl` — one JSON object per episode (the primary artifact)
- `summary.json`   — `{model_name: {aggregate metrics}}`
- `vllm.log`       — server log (optional; may be absent for API runs)

~1,700 episodes total.

### Episode object (top level)

| field | type | notes |
|---|---|---|
| `model` | str | e.g. `Qwen/Qwen3.5-27B`, `claude-haiku-4-5` |
| `episode` | int | index within the run |
| `error` | str | **present only on failure**; such episodes have empty/short `rounds` |
| `action_format` | str | `tools` |
| `obs_encoding` | str | `compact` |
| `history` | int | **1, 4 or 32** — turns of context |
| `transfers` | str | `manual` or `task_only` |
| `system_variant` | str | `minimal` |
| `reasoning_effort` | str | `low`, `medium`, `high` |
| `max_tokens` | int | **512, 2048, 4096, 6000, 16384** |
| `prompt_sha` | str | **3 distinct values across runs** — different prompts! |
| `image_mode` | str | `none` |
| `system_prompt` | str | full prompt text |
| `summary` | dict | see below |
| `rounds` | list | up to 32 round objects |

### `summary`
`totalReward`, `finalSat`, `finalBudget`, `finalScore`, `foodFulfillRate`,
`lodgingFulfillRate`, `foodResolved`, `lodgingResolved`, `actionsRequested`,
`actionsExecuted`, `actionFailures`, `invalidIndices`, `minBudget`, `wentNegative`,
`everBuilt`, `everHired`, `terminated`, `roundsPlayed`, `rewardWeights`

**Many of these are `None` on failed episodes. Never assume a number.**

### `rounds[i]`
`r`, `reward`, `sumR`, `sat`, `budget`, `satScore`, `costEff`,
`comps` (dict: `sat_food`, `sat_lodging`, `sat_worker_use`, …),
`foodFul`, `foodRes`, `lodgFul`, `lodgRes`, `nSel`, `nReq`, `nFail`,
`actCats` (dict: action_type → count), `cmdErrors` (list of strings),
`note`, `reasoning`, `reasoningTokens`, `obs` (full game-state dict),
`raw` (model's visible text), `parsed_ok`

---

## 2. The single most important requirement

**Configuration varies more than model does.** The same model appears under several configs
(`history`, `max_tokens`, `reasoning_effort`, `transfers`, `prompt_sha`), and reward spans a
range of ~1.0 for one model across configs — larger than most between-model gaps.

Therefore:
- The unit of analysis is **(model, config)**, never model alone.
- Any figure comparing models MUST either hold config fixed or show config explicitly.
- Treat `prompt_sha` as a first-class grouping key. Runs with different `prompt_sha` were
  given different prompts and are **not** directly comparable.
- If you produce a single "model leaderboard", it must state which config it is conditioned
  on, and the code must make that an explicit parameter.

Silently averaging across configs would produce a misleading result. Do not do it.

---

## 3. Deliverables

### `analyze/load.py`
- `load_runs(paths: list[str]) -> pandas.DataFrame` — accepts globs/dirs, walks any depth,
  returns ONE tidy row per episode with: `run`, `source_tree`, `model`, `episode`, all
  config fields, all `summary` fields, plus derived `ok` (bool: no `error` and
  `roundsPlayed > 0`).
- `load_rounds(paths) -> DataFrame` — one row per (run, model, episode, round).
- Robust to: missing files, `None` values, empty `rounds`, malformed lines (skip + count),
  and unknown/new config fields (carry them through, don't drop).
- Writes `analyze/out/episodes.csv` and `analyze/out/rounds.csv` so plotting never re-parses.

### `analyze/figures.py`
Generates PNG **and** PDF at ≥200 dpi into `analyze/out/figures/`. Every figure must have
axis labels with units, a title stating the config being shown, and n= annotated.

Required figures:
1. **Leaderboard** — mean `totalReward` per model at a *stated fixed config*, with 95% CI
   error bars over episodes, sorted. Annotate n per bar.
2. **Config sensitivity** — for each config axis (`history`, `max_tokens`,
   `reasoning_effort`, `transfers`): reward vs that axis, one line per model. This is the
   figure that shows config matters.
3. **Learning-within-episode** — mean `sumR` vs round index, one line per model, shaded CI.
4. **Score decomposition** — stacked/grouped bars of the `comps` components
   (`sat_food`, `sat_lodging`, `sat_worker_use`, …) plus `costEff`, per model.
5. **Behaviour profile** — mean actions per round by `actCats` category, per model
   (shows what each model actually *does*, not just its score).
6. **Reliability** — per model: episode completion rate, `parsed_ok` rate, mean
   `cmdErrors`/round, `actionFailures`/`actionsRequested`.
7. **Budget trajectory** — median `budget` vs round with IQR band, per model; mark the
   zero line (going negative is allowed and expected, but recovery is what separates
   good policies).

### `analyze/report.py`
Emits `analyze/out/REPORT.md`: dataset inventory (runs, episodes, models, configs seen),
the leaderboard table, per-model reliability table, and an explicit **caveats** section
listing any run with <100% episode completion, any config axis with only one level, and any
model appearing under only one `prompt_sha`.

### `analyze/README.md`
How to run on a fresh results tree, in three commands or fewer.

---

## 4. Constraints

- Python 3.9+, `pandas` + `matplotlib` only. **No seaborn, no plotly.**
- Deterministic output — no random colours; a stable model→colour mapping derived from
  sorted model names so figures are comparable across regenerations.
- Colour-blind-safe palette; do not encode meaning by colour alone (use markers/hatching too).
- No network access. No writes outside `analyze/`.
- Every script takes `--results` (repeatable glob, default `benchmark_results/cluster*`)
  and `--out` (default `analyze/out`).
- Fail loudly on a malformed *schema* (e.g. `rounds` not a list); skip and count malformed
  *lines*.

## 5. Known data hazards — handle these explicitly

- **74 episodes carry an `error` field** and have no usable rounds. Exclude from performance
  stats, but REPORT the count per run — a model that crashes often is not a model that scores 0.
- Several runs have partial completion (e.g. 24/32, 11/32) and at least two have **0/32**.
  A run with 0 completed episodes must not silently vanish; it belongs in the caveats table.
- `finalSat` / `finalBudget` / `minBudget` are `None` on failed episodes.
- Baseline policies (greedy, build-potential, choice-lookahead, combined, random, noop) live
  in `cluster_api/baselines_*/`. They are the non-learning reference — plot them as a
  horizontal reference line or a distinct marker on the leaderboard, not as ordinary models.

## 6. Acceptance criteria

Before you report done:
1. `python analyze/load.py --results 'benchmark_results/cluster*'` produces both CSVs and
   prints a row count.
2. `python analyze/figures.py` produces all 7 figures with no exceptions.
3. Re-running on a **subset** (one run dir) still works and produces figures.
4. Deleting `analyze/out/` and re-running reproduces byte-identical CSVs.
5. `REPORT.md` caveats section is non-empty and correctly names the 0/32 runs.

Show me the REPORT.md and the leaderboard figure first.
