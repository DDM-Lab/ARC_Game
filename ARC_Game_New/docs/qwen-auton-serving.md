# Serving Qwen3.8-27B on Auton for CORA — settings to investigate

> **SUPERSEDED for anything measured.** `proj_dashboard/serve/QWEN27B_SERVING.md`
> contains numbers taken from a RUNNING engine on Auton `debug` (2× RTX A6000, sm_86,
> vLLM 0.27.1, FP8 checkpoint, 2026-09-11). Prefer it. This file was written from
> published specs BEFORE that existed, and it got things wrong. Corrections:
>
> * **Parallelism: use TP=2, not data parallel.** This file argued DP-over-TP. That
>   holds only if the weights are small; the FP8 checkpoint is 29 GB on a 48 GB card,
>   so duplicating it halves the KV pool (~28 GB DP vs ~57 GB TP=2), and on a
>   concurrency-bound service the KV pool IS the agent count. DP becomes right again
>   only if the model is requantised to INT4 (~15 GB).
> * **Prefix caching is chunked at 784 tokens and this is architectural.** Below 784
>   nothing caches at all; growth inside a chunk buys nothing. That INVERTS this
>   file's advice to shrink the static prefix — larger static heads cache
>   proportionally better, and should be padded toward a multiple of 784.
> * **`--kv-cache-dtype fp8` is refused on sm_86** (needs SM89+). Not a tuning choice.
> * **Measured concurrency is 41 sequences at 16k**, from the engine's own startup
>   line — an arithmetic estimate was 40% optimistic.
> * **The biggest lever is client-side:** `chat_template_kwargs {"enable_thinking":
>   false}` measured 10.6x on a real officer turn (72.4s -> 6.8s, same tool sequence).
>
> What remains useful here: the workload description, the tool-parser trap, and the
> prompt-size measurements.

Handoff list for whoever sets this up. Nothing here has been run on Auton; the
local numbers are measured on an M5 Pro (mlx-dspark, `127.0.0.1:8090`), the A40
numbers are arithmetic from published specs and must be re-measured.

## What the workload actually looks like

Per game round: **6 officers × up to 4 tool steps = ~24 requests**, issued
concurrently per round. Each request is:

* **input ~2k–8k tokens** — a game-state observation plus a shared global prompt
  and tool definitions,
* **output ~300 tokens** (measured: 287–361), and those are mostly REASONING
  tokens,
* **must return real `tool_calls`** — the officers are tool-driven
  (`read_state`, `get_facilities`, `talk_to_director`, `finish`). A stack that
  cannot emit tool calls is useless here regardless of speed.

Measured locally, one officer-shaped call: 7.79 s cold, 6.65 s warm. Prefix
caching bought 1.1 s; **the other ~7 s is decode of ~300 tokens**.

## The headline, before anyone tunes anything

Decode is bandwidth-bound. A40 is **696 GB/s**; at 4-bit (~15 GB of weights)
that is ~21 ms/token ≈ **40 tok/s realistic**, against **32 tok/s** already
measured on the laptop. So:

* **2×A40 buys CONCURRENCY, not per-request latency.** Expect roughly the same
  seconds-per-officer as the Mac, but ~80 of them at once instead of one.
* **The latency lever is output tokens, not hardware.** Measured locally on an
  identical prompt: default 29 tokens / 1.05 s → `reasoning_effort=none`
  2 tokens / **0.33 s**. Cutting officer reasoning is worth more than the GPUs.

## Prerequisites (check these FIRST — they can sink the whole plan)

1. **A CUDA-servable checkpoint.** `mlx-community/Qwen3.8-27B-4bit` is MLX, i.e.
   Apple-only. Auton needs base HF weights quantised to **AWQ or GPTQ INT4**
   (Marlin kernels), or an existing AWQ build. Budget time for this.
2. **Architecture support in the serving stack.** `config.json` reports
   `model_type: qwen3_5`. Confirm the installed vLLM/SGLang version actually
   supports it — including any hybrid/linear-attention layers. If the stack does
   not know the architecture, nothing else matters.
3. **Ampere has no FP8.** Do not plan around FP8 weights or FP8 compute. INT8 and
   INT4 (Marlin) are the options.
4. **Dense or MoE?** Not confirmed. If it is MoE with a small active-parameter
   count, prefill FLOPs drop roughly an order of magnitude and every estimate
   below improves substantially. Worth settling early.

## Settings to investigate

### Parallelism — prefer data parallel over tensor parallel
* `--tensor-parallel-size 1`, and run **two independent replicas, one per GPU**,
  behind a round-robin. At INT4 (~15 GB) the model fits one A40 with room to
  spare, and DP roughly doubles throughput.
* TP=2 only if serving BF16 (~54 GB does not fit on one card) or if single-request
  latency matters more than aggregate throughput — TP adds a per-layer all-reduce.

### Memory / concurrency
* `--max-model-len 16384`. **Not** the native 262144. The context cap is what
  frees KV to admit many sequences.
* `--gpu-memory-utilization 0.90`
* `--max-num-seqs 64` (start here, raise while watching KV utilisation)
* KV budget: measured **0.086 GB per 1k tokens** locally (architecture-determined,
  so it should transfer). Per GPU: 48 − ~15 weights − ~4 overhead ≈ **28 GB KV**
  ≈ 325k tokens ≈ ~40 sequences at 8k, ~20 at 16k.
* `--kv-cache-dtype` — a RAM lever, not a speed lever (confirmed locally: quantised
  KV measured slightly SLOWER). Only reach for it if KV-bound. Verify what SM86
  actually supports.

### Latency under load
* **`--enable-chunked-prefill`** — important. Without it a single 8k prefill
  stalls every other stream's decode; with 24 concurrent requests per round that
  is the difference between fair and terrible p95.
* `--max-num-batched-tokens 8192` (tune with the above)
* Keep **CUDA graphs on** (do NOT pass `--enforce-eager`) — they matter for decode.

### Prefix caching — the second-biggest win
* vLLM: `--enable-prefix-caching`. SGLang: RadixAttention, on by default and
  better suited to this branching shape (6 officers sharing one prefix).
* Prompt layout must put the shared bytes FIRST: global prompt → tools → shared
  game state → officer-specific → step history. Any per-officer preamble ahead of
  the shared block destroys reuse.
* Verify it is actually hitting — locally we confirmed 6338/6816 cached. vLLM
  exposes `gpu_prefix_cache_hit_rate`; treat a low value as a bug, not a tuning
  detail.

### Speculative decoding — the biggest latency win after token count
* There is a drafter in use locally: `incoai/Qwen3.8-27B-DFlash2`. Needs a
  CUDA-compatible equivalent.
* `--speculative-model ... --num-speculative-tokens 3..5`. Typically 2–3× on
  decode, which is the entire response time once caching is on.
* Caveat: speculative decoding and very large batches interact badly (rejection
  overhead). Best at low/medium concurrency; measure both ways.

### Tool calling — non-negotiable, and the known way to get silent zeros
* vLLM: `--enable-auto-tool-choice --tool-call-parser qwen3_xml`.
  **NOT `hermes`.** This is not a preference: three benchmark runs
  (`Qwen3.5-27B`, `Qwen3.6-27B`, `Qwen3.5-35B-A3B`) were served with `hermes`,
  parsed **0/1024 rounds**, took no actions, raised NO error, and scored exactly
  0.000 — indistinguishable from a no-op agent in the summary table. They had to
  be found by hand and re-run. See `arc_bench/assets/MANIFEST.md`, caveat 2.
* GATING CHECK before trusting any number: confirm the endpoint returns
  `finish_reason: "tool_calls"` with parsed arguments, and that a real run parses
  >5% of rounds. The figure pipeline now drops runs below that automatically,
  which tells you how easy this failure is to miss.

### Reasoning control — verify it is exposed
* The server must honour per-request `reasoning_effort` (or
  `chat_template_kwargs: {"enable_thinking": false}`). This is the single largest
  latency lever measured, so confirm the CUDA stack exposes the same switch the
  MLX one does.

## Wiring it into CORA

* Add one entry to `provider_registry.py` — a `Provider` enum member plus a
  `ProviderSpec("openai", "<auton-url>/v1", "<KEY_ENV>")`. That file is a
  deliberate security boundary: configs name a provider, never a raw URL, so the
  endpoint is added here and nowhere else.
* If it is reachable beyond localhost it needs a key; name the env var in the
  spec, never the secret.
* Then clone `config/continuous_all_officers_qwen.json` with the new provider.

## Prompt size — measured, and smaller than it looks

The **system prompt** is ~1.1k–1.4k tokens, not the ~4–5k the `model_view_*.txt`
dumps suggest (those files are a whole turn: prompt + tool schema + observation +
response + game reply).

| variant | prompt | Qwen3.8-27B score |
|---------|--------|-------------------|
| `minimal_v2` | 4,489 chars (~1,122 tok) | **0.646** (best) |
| `minimal_v3` | 5,031 chars (~1,258 tok) | 0.643 |
| `minimal_v4` | 5,625 chars (~1,406 tok) | never run |

For THIS model the prompt rewrite is worth nothing (−0.003, CIs overlapping),
while every other model gained: Qwen3.5-27B +0.122, Qwen3.5-4B +0.076, Qwen3-4B
+0.562. So the v3 prompt is load-bearing for smaller models and optional for
Qwen3.8-27B — do not generalise one model's result to the fleet.

**The prompt is not where the input tokens are.** A real call is roughly
1.2k prompt + ~0.5k tool schema + **~2.7k observation**. If the goal is fewer
input tokens, trim the per-officer `subobservation_space` (the Workforce officer
does not need `constructionState`), not the prompt.

Also measured: `history` is the ONLY sweep axis with a negative mean
(-0.030 over 13 pairs). Shrinking the history window saves tokens AND scores
marginally better — a free saving, unlike cutting the prompt.

## Numbers to report back

* p50 / p95 per-request latency at 1, 8, 24, 64 concurrent
* aggregate tokens/s
* prefix cache hit rate
* KV utilisation at steady state
* tokens/s with and without speculative decoding, and with reasoning off

The target worth aiming at: **~24 requests per round finishing in one wave**, with
per-officer latency dominated by output tokens rather than queueing.
