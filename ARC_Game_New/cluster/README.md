# Running the ARC benchmark on a GPU cluster (vLLM, text-only)

Goal: escape the ~29 tok/s Mac-GPU ceiling. A GPU node + **vLLM** (continuous batching)
serves the open-weights models at 100s–1000s tok/s, and because requests are batched you
can run **many Unity sims in parallel** (`--workers` high) instead of the local `workers=1`.

The Unity side needs **no code changes and no display**: the Linux *Dedicated Server* build
has graphics stripped. You build it once on the Mac and copy the binary over — the cluster
never needs Unity installed.

---

## One-time: build the Linux binary (on the Mac)

1. Unity Hub → **Installs** → `2022.3.62f3` → ⚙ → **Add Modules** → check
   **Linux Build Support (Mono)** (+ IL2CPP if you want it). ~1 GB.
2. Close the Unity editor (one instance per project), then from the repo root:
   ```bash
   /Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
     -quit -batchmode -nographics -projectPath "$PWD" \
     -executeMethod HeadlessBuildScript.BuildLinux -logFile /tmp/arc_linux_build.log
   ```
   Output: `Build/Headless/Linux/ARC_Headless.x86_64` (+ `ARC_Headless_Data/`).
   *(This must run unsandboxed — the batchmode build writes a local Unity DB.)*

## Ship to the cluster

```bash
# copy the repo (or git clone) AND the Linux build dir
rsync -av Build/Headless/Linux/  <cluster>:~/ARC_Game_New/Build/Headless/Linux/
# benchmark_models.py + arc_game_gym_env_tcp.py + llm_smoke_test.py + cluster/  must be present
```
The build is cross-compiled, so it runs on the cluster's glibc with no Unity. If you hit a
glibc-too-old error, build with the IL2CPP backend or run on a newer node.

## Set up the Python env (on the cluster)

```bash
cd ~/ARC_Game_New
bash cluster/setup_env.sh          # venv + gym deps + vLLM (pin the vLLM version!)
```

## Run

Edit the `TODO` lines at the top of `cluster/run_benchmark.slurm` (partition, account,
modules, `HF_TOKEN` for gated repos like gemma-3), then:

```bash
sbatch cluster/run_benchmark.slurm                          # defaults: EFFORT=none N=3 ROUNDS=32 WORKERS=8
EFFORT=none N=5 ROUNDS=32 WORKERS=8 sbatch cluster/run_benchmark.slurm
```

It loops over models: serve with vLLM → wait for `/health` → run `benchmark_models.py`
against `http://localhost:8000/v1` → kill vLLM → next. Results land in
`bench_cluster_<effort>/<tag>/episodes.jsonl`, identical schema to the local runs, so the
existing plotting/aggregation scripts work unchanged. Re-running skips already-complete models.

---

## Key wiring (already done in this repo)

- **Exe path** — `benchmark_models.py` auto-selects the Linux binary on Linux, and honors
  `ARC_HEADLESS_EXE` (the SLURM script sets it explicitly). No edit needed.
- **Endpoint** — `--base-url http://localhost:8000/v1 --api-key vllm`. The served-model-name
  must equal the `--models` tag; the SLURM `MODELS` array keeps them in sync.
- **Concurrency** — `--workers N` spawns N Unity Server sims on `--base-port`+offset, all
  hitting the one batched vLLM server. Start at 8; raise until GPU/CPU saturates.

## Reasoning / thinking control on vLLM (differs from Ollama!)

The `reasoning_effort` knob was built for Ollama. On vLLM it's model-specific:
- **gpt-oss** honors OpenAI-style `reasoning_effort` directly.
- **Qwen3 / Qwen3.5** use a chat-template switch (`enable_thinking`), not `reasoning_effort`;
  serve with `VLLM_EXTRA="--reasoning-parser qwen3"` and pass
  `extra_body={"chat_template_kwargs":{"enable_thinking":false}}` if you want thinking off.
- **qwen2.5 / gemma-3** are non-thinking — effort is a no-op.

`benchmark_models.py` already **retries without `reasoning_effort`** if the server rejects it,
so an unsupported model degrades gracefully (just won't limit thinking). If you specifically
need thinking *off* for Qwen3 on vLLM, tell me and I'll wire the `enable_thinking` path into
`chat()` — it's a small addition.

## Scaling the big models

- `qwen3-30b-a3b` (MoE, 3B active) and `gemma3-27b` (dense) fit on one 80 GB GPU at bf16.
  On smaller cards set `TP` (and `--gres=gpu:N`) for tensor parallelism.
- Tune `VLLM_GPU_UTIL` (default 0.90) and `VLLM_MAX_LEN` (default 8192) per card.
