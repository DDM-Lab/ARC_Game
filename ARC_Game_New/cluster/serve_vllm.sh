#!/usr/bin/env bash
# Launch a vLLM OpenAI-compatible server for ONE model on the cluster GPU(s).
# vLLM serves a single model per process, so the benchmark driver (run_benchmark.slurm)
# starts one of these per model, waits for /health, runs the benchmark, then kills it
# to free the GPU before the next model.
#
#   Usage: ./serve_vllm.sh <hf-model-id> [served-name] [port] [tensor-parallel]
#   Ex:    ./serve_vllm.sh Qwen/Qwen2.5-7B-Instruct qwen2.5-7b 8000 1
#
# Env knobs: VLLM_MAX_LEN (ctx window), VLLM_GPU_UTIL (0-1), VLLM_EXTRA (extra args,
#            e.g. "--reasoning-parser qwen3" for thinking models — see README).
set -euo pipefail
MODEL="${1:?hf model id required}"
SERVED="${2:-$(basename "$MODEL")}"
PORT="${3:-8000}"
TP="${4:-1}"                       # tensor-parallel size = number of GPUs for this model
MAXLEN="${VLLM_MAX_LEN:-8192}"     # ARC prompts are ~1-2k tok; 8k is plenty of headroom
UTIL="${VLLM_GPU_UTIL:-0.90}"

echo "[serve_vllm] $MODEL  ->  served-name=$SERVED  port=$PORT  tp=$TP  max-len=$MAXLEN"
exec python -m vllm.entrypoints.openai.api_server \
  --model "$MODEL" \
  --served-model-name "$SERVED" \
  --port "$PORT" \
  --tensor-parallel-size "$TP" \
  --max-model-len "$MAXLEN" \
  --gpu-memory-utilization "$UTIL" \
  --disable-log-requests \
  ${VLLM_EXTRA:-}
