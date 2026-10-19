---
name: cora-benchmark
description: Benchmark a policy on the CORA game — an LLM, a decision model (Clef/JEV), a non-learning baseline or a search agent — on the surrogate or the headless Unity build, locally or on the Auton cluster, and read the results. Use when asked to benchmark, compare or score policies on CORA.
---

# Benchmarking policies on CORA

Every arm plays the same game through the same seven tools (`cora/tools.py`), the same
observation (`cora/observation.py`) and the same executor (`cora/executor.py`), so scores are
comparable across LLMs, decision models, baselines and search. A full game is 29 decisions
(the Day-1 setup, then 4 a day for days 2-8); the score is Unity's `rewardMetrics`
(`cora/scoring.py`), satisfaction + efficiency, roughly 0-2.

## Pick the environment

| `--env` | What it runs | Use it for |
|---|---|---|
| `sim` | `oracle.sim.SimEnv`, the Python surrogate: ~1 ms a turn, no Unity, default map only | everything iterative; it gives Unity 1.0.2's final score on every replayed game checked (4,217 search plans, 32 LLM games) |
| `unity` (default) | the headless Linux/macOS build over TCP, one process per episode | spot-checking a result before publishing it |

Pass `--seed S` so episode i plays seed S+i: two runs with one `--seed` play the same
scenarios, so differences are paired. The standard sweep is `--seed 7000 --episodes 32`.

## Local runs

```bash
python -m bench --policy rules --env sim --episodes 32 --seed 7000            # reference baseline (~1.50)
python -m bench --policy greedy-2 --env sim --episodes 8 --seed 7000          # best basket 2 decisions ahead (sim only)
python -m bench --models Qwen/Qwen3-4B --base-url http://127.0.0.1:8000/v1 \
  --env sim --episodes 32 --seed 7000 --prompt minimal_v7                     # an LLM on a local vLLM
python -m oracle.search 7003 --algo uct-f7 --budget 16000 --crn 32 --reuse    # one search game
```

Results go to `--out` (default `benchmark_results/...`): `episodes.jsonl` (every turn: the
observation, the calls and each call's status/reason) and `summary.json` (`meanTotalReward`,
food/lodging fulfilment, `meanInvalidCalls`, final budget).

## Cluster runs (Auton)

Export the settings and submit with a plain `--export=ALL`. **`--export=ALL,VAR=x` makes Slurm
rebuild the user environment on the node, which fails and leaves the job held** ("user env
retrieval failed"). Point `ARC_GAME_PATH` at the checkout you want measured.

```bash
# An open-weights LLM: starts vLLM on the node, waits for /health, plays the games.
export ARC_GAME_PATH=$PWD MODE=local MODEL=Qwen/Qwen3.8-27B TP=2 ENV=sim EPISODES=32 SEED=7000 \
       WORKERS=64 PROMPT=minimal_v7 OUT=/zfsauton/scratch/$USER/CORA/bench/qwen3.8-27b
sbatch -J bench_27b --gres=gpu:a6000:2 --export=ALL ops/benchmark_cluster.sbatch

# A decision model (BACKEND: clef | jev | jev-hf | llm); SCHEMA v1 is the reference setting.
export ARC_GAME_PATH=$PWD ENV=sim SCHEMA=v1 MODEL_NAME=clef-flash BACKEND=clef \
       CKPT=/zfsauton/scratch/$USER/models/decision/Cloudflare__clef-flash OUT=...
sbatch -J dec_clef_flash --export=ALL ops/benchmark_decision.sbatch

# A search agent over a block of seeds (REPLAY=0: surrogate score only).
export ALGO=uct-f7 INFO=fair BUDGET=16000 SEARCH_ARGS="--crn 32 --reuse" REPLAY=0 OUT=...
sbatch -J search_uct --export=ALL ops/search_cluster.sbatch
```

## Settings that matter (each one measured)

- **gpt-oss calls tools one at a time.** With one reply per decision it made exactly one call
  per turn (898/898 turns). `ops/benchmark_cluster.sbatch` sets `TOOL_ROUNDS=8` for any
  `*gpt-oss*` model (`--tool-rounds 8`); every call is acknowledged as queued and all of them
  run at the end of the turn. 0.64 → 0.97.
- **vLLM's harmony parser** can leave the channel header in a tool name
  (`task<|channel|>commentary`); `bench/llm.py` strips it.
- **Qwen3.5 hybrids on vLLM 0.27.1** need `--enable-prefix-caching --mamba-cache-mode align`
  together, or prefix caching silently stays off (2.1 → 4.2 turns/s at 256 games).
- **fp8 KV cache is refused on the A6000 (SM86).** Use Triton attention at high concurrency.
- **Decision models answer each question separately.** JEV and an LLM answering typed
  questions see one question per prompt; Clef sees them all but still returns one distribution
  per question. Schema `v2` (one build question per free site) makes them build on every site
  at once: JEV-9B scored 0.31 under v2 and 0.72 under v1. Report v1.

## Reading a run

Before trusting a score, check the calls. In `episodes.jsonl`, `rounds[i].calls[j].status` is
`executed`, `refused` (a game rule said no; the reason is Unity's own) or `invalid` (malformed,
unknown tool, or impossible: e.g. staffing beyond the free workforce). A model with one call per
turn, a tool name nobody defined, or most turns empty is a harness problem, not a model result.

Reference numbers (surrogate, seeds 7000-7031): rules 1.498; UCT-f7 16k with CRN+reuse 1.633;
Qwen3.5-35B-A3B 1.22, Qwen3.8-27B 1.19, gpt-oss-20b 0.97, Qwen3-4B 0.84; Clef 0.72 (v1).
