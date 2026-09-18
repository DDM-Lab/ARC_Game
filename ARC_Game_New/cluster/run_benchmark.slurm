#!/usr/bin/env bash
#SBATCH --job-name=arc-bench
#SBATCH --output=arc-bench-%j.out
#SBATCH --gres=gpu:1                 # TODO: bump for tensor-parallel big models (set TP to match)
#SBATCH --cpus-per-task=8            # Unity Server sims are light; a few cores per worker is plenty
#SBATCH --mem=64G
#SBATCH --time=08:00:00
### TODO: uncomment / set for your cluster ----------------------------------
###SBATCH --partition=gpu
###SBATCH --account=YOUR_ACCOUNT
# ---------------------------------------------------------------------------
#
# Serve each model with vLLM, run the ARC benchmark against it, tear down, repeat.
# Mirrors run_oss_smoke.sh but on the cluster GPU with a batched vLLM endpoint, so
# --workers can be high (many Unity Server sims hammering one batched server).
#
#   sbatch cluster/run_benchmark.slurm                 # defaults below
#   EFFORT=none N=5 ROUNDS=32 WORKERS=8 sbatch cluster/run_benchmark.slurm
set -uo pipefail

# ── cluster env (EDIT) ──────────────────────────────────────────────────────
REPO="${REPO:-$HOME/ARC_Game_New}"
cd "$REPO"
# module load cuda/12.4            # TODO if your cluster uses modules
source .venv/bin/activate          # TODO: or `conda activate arc`
export HF_HOME="${HF_HOME:-$REPO/.hf_cache}"     # where weights are cached/staged
# export HF_TOKEN=hf_xxx           # TODO: required for gated repos (e.g. gemma-3)

# Linux Dedicated Server build (no display needed). Shipped from the Mac build.
export ARC_HEADLESS_EXE="$REPO/Build/Headless/Linux/ARC_Headless.x86_64"
chmod +x "$ARC_HEADLESS_EXE" 2>/dev/null || true
[ -x "$ARC_HEADLESS_EXE" ] || { echo "FATAL: $ARC_HEADLESS_EXE missing/not executable"; exit 1; }

# ── experiment config ───────────────────────────────────────────────────────
EFFORT="${EFFORT:-none}"           # none|low|medium|high (see README: vLLM reasoning control)
N="${N:-3}"
ROUNDS="${ROUNDS:-32}"
WORKERS="${WORKERS:-8}"            # parallel Unity sims; vLLM batches their requests
TP="${TP:-1}"                      # tensor-parallel size; must match --gres=gpu:N above
PORT="${PORT:-8000}"
OUT="${OUT:-bench_cluster_${EFFORT}}"
mkdir -p "$OUT"

# tag (passed to --models AND --served-model-name)  |  HF repo id
MODELS=(
  "qwen2.5-7b|Qwen/Qwen2.5-7B-Instruct"
  "gpt-oss-20b|openai/gpt-oss-20b"
  "qwen3-30b-a3b|Qwen/Qwen3-30B-A3B"
  "gemma3-27b|google/gemma-3-27b-it"
)

wait_health() {  # $1=port ; up to 10 min for first-time weight download + load
  for i in $(seq 1 120); do
    curl -sf "http://localhost:$1/health" >/dev/null 2>&1 && return 0
    sleep 5
  done
  return 1
}

for entry in "${MODELS[@]}"; do
  TAG="${entry%%|*}"; REPO_ID="${entry##*|}"
  dir="$OUT/$TAG"; mkdir -p "$dir"
  if [ "$(wc -l < "$dir/episodes.jsonl" 2>/dev/null || echo 0)" -ge "$N" ]; then
    echo "=== $TAG already complete, skip ==="; continue
  fi
  echo "============================================================"
  echo "=== serve $REPO_ID as '$TAG' on :$PORT (tp=$TP) ==="
  bash cluster/serve_vllm.sh "$REPO_ID" "$TAG" "$PORT" "$TP" > "$dir/vllm.log" 2>&1 &
  VPID=$!
  if ! wait_health "$PORT"; then
    echo "!!! vLLM never became healthy for $TAG — last log:"; tail -30 "$dir/vllm.log"
    kill -9 "$VPID" 2>/dev/null; sleep 5; continue
  fi
  echo "=== vLLM healthy; running benchmark for $TAG ==="
  PYTHONUNBUFFERED=1 python -u benchmark_models.py --policy llm --models "$TAG" \
    --base-url "http://localhost:$PORT/v1" --api-key vllm \
    --image_mode none --action_format cmd --obs_encoding compact \
    --system_prompt minimal --transfers task_only --history 1 --reasoning_effort "$EFFORT" \
    --episodes "$N" --rounds "$ROUNDS" --workers "$WORKERS" \
    --out "$dir" --base-port 9970 >> "$dir/run.log" 2>&1
  echo "=== $TAG done: $(wc -l < "$dir/episodes.jsonl" 2>/dev/null || echo 0)/$N eps ==="
  kill -9 "$VPID" 2>/dev/null              # free the GPU before the next model loads
  pkill -9 -f "ARC_Headless" 2>/dev/null
  sleep 8
done
echo "============================================================"
echo "ALL DONE -> $OUT/"
for entry in "${MODELS[@]}"; do
  TAG="${entry%%|*}"
  echo "  $TAG: $(wc -l < "$OUT/$TAG/episodes.jsonl" 2>/dev/null || echo 0)/$N eps"
done
