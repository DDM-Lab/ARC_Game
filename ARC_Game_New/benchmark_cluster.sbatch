#!/bin/bash
# CORA benchmark on the cluster — ONE harness for local weights and API frontier models.
#
# WHY NOT veRL FOR THIS
# veRL's rollout is vLLM serving local HF weights; it has no API-model path, so it cannot
# benchmark Claude/GPT/Gemini at all. benchmark_models.py speaks OpenAI-compatible HTTP, so
# pointing it at a local `vllm serve` covers open weights AND the CMU gateway covers frontier
# models — same prompt (cora_prompts), same obs encoder, same tool schema, same episode loop.
# veRL stays for RL TRAINING, where its loop earns its keep.
#
# MODES
#   MODE=api    no GPU needed. Models come from the CMU gateway. WORKERS scales with cores.
#   MODE=local  starts `vllm serve $MODEL` on this node, waits for /health, then benchmarks it.
#
# USAGE
#   MODE=api   MODELS="us.anthropic.claude-opus-4-8,gpt-5.5" WORKERS=16 sbatch benchmark_cluster.sbatch
#   MODE=local MODEL=Qwen/Qwen3-4B WORKERS=8 sbatch benchmark_cluster.sbatch
#
#SBATCH --job-name=cora_bench
#SBATCH --partition=general
#SBATCH --cpus-per-task=16
#SBATCH --mem=96G
#SBATCH --time=08:00:00
#SBATCH --output=/zfsauton/scratch/%u/CORA/logs/bench/%x_%j.out
#SBATCH --error=/zfsauton/scratch/%u/CORA/logs/bench/%x_%j.err

set -uo pipefail

MODE=${MODE:-api}
ARC=${ARC_GAME_PATH:-/zfsauton/scratch/$USER/CORA/ARC_Game/ARC_Game_New}
export ARC_GAME_PATH="$ARC"
export ARC_HEADLESS_EXE="${ARC_HEADLESS_EXE:-$ARC/Build/Headless/Linux/ARC_Headless.x86_64}"
export PYTHONUNBUFFERED=1
mkdir -p "/zfsauton/scratch/$USER/CORA/logs/bench"
cd "$ARC"

PY=${PY:-./.venv/bin/python}
[ -x "$PY" ] || PY=python3

EPISODES=${EPISODES:-5}
ROUNDS=${ROUNDS:-16}
# One Unity headless process per worker, each on its own TCP port (see run_episode's port_pool).
# Unity is the CPU bottleneck for API models, so WORKERS ~ cpus-per-task is the right scale.
WORKERS=${WORKERS:-8}
# Job-unique base port so concurrent jobs on one node never collide.
BASE_PORT=${BASE_PORT:-$(( 20000 + (${SLURM_JOB_ID:-0} * 64) % 40000 ))}
TAG=${TAG:-$(date +%m%d_%H%M)}
OUT=${OUT:-benchmark_results/cluster_${MODE}_${TAG}}
mkdir -p "$OUT"

# Format knobs held to what the local sweep used, so cluster and laptop numbers are comparable.
FMT=(--action_format tools --obs_encoding compact --system_prompt minimal
     --image_mode none --transfers task_only --history 1)

echo "=== CORA benchmark  mode=$MODE  job=${SLURM_JOB_ID:-local}  node=$(hostname)  $(date)"
echo "    ARC_GAME_PATH=$ARC"
echo "    episodes=$EPISODES rounds=$ROUNDS workers=$WORKERS base_port=$BASE_PORT"
[ -x "$ARC_HEADLESS_EXE" ] || { echo "FATAL: headless binary not executable at $ARC_HEADLESS_EXE"; exit 1; }

VLLM_PID=""
cleanup() {
  echo "[cleanup] $(date +%H:%M:%S)"
  [ -n "$VLLM_PID" ] && kill "$VLLM_PID" 2>/dev/null
  pkill -f "$ARC_HEADLESS_EXE" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

EXTRA=()
if [ "$MODE" = "local" ]; then
  MODEL=${MODEL:?MODE=local requires MODEL=<hf-id>}
  PORT=${VLLM_PORT:-$(( BASE_PORT + 900 ))}
  # 12288 mirrors the local ctx12k variants: prompt ~2k + generation 6k, with headroom. A larger
  # window only inflates the KV reservation, which is what drove this machine into swap locally.
  vllm serve "$MODEL" --port "$PORT" --max-model-len ${MAX_MODEL_LEN:-12288} \
       --gpu-memory-utilization ${GPU_UTIL:-0.85} > "$OUT/vllm.log" 2>&1 &
  VLLM_PID=$!
  echo "    vllm serve $MODEL -> :$PORT (pid $VLLM_PID), waiting for /health"
  for i in $(seq 1 120); do
    curl -sf "http://127.0.0.1:$PORT/health" >/dev/null 2>&1 && { echo "    vLLM ready after ${i}0s"; break; }
    kill -0 "$VLLM_PID" 2>/dev/null || { echo "FATAL: vLLM died during startup"; tail -30 "$OUT/vllm.log"; exit 1; }
    sleep 10
  done
  curl -sf "http://127.0.0.1:$PORT/health" >/dev/null 2>&1 || { echo "FATAL: vLLM not healthy"; exit 1; }
  MODELS="$MODEL"
  EXTRA+=(--base-url "http://127.0.0.1:$PORT/v1" --api-key local --max_tokens ${MAX_TOKENS:-6000})
  # Qwen3/3.5 emit chain-of-thought into the CONTENT channel unless the template flag is set.
  # It does NOT reduce generation (~3-5k tokens either way) but keeps content clean. Must not be
  # combined with --reasoning_effort: sending both defeats it (measured, median 16.6k chars back).
  case "$MODEL" in *[Qq]wen3*) EXTRA+=(--no-thinking) ;; esac
else
  MODELS=${MODELS:?MODE=api requires MODELS="a,b,c"}
  # Gateway key comes from the environment / .env; never echoed.
fi

# ── stall watchdog ───────────────────────────────────────────────────────────────────────────
# The gym can deadlock with Unity alive, socket ESTABLISHED and both sides idle; the env's 120s
# socket timeout does NOT fire (observed: a single episode hung 10h locally). With WORKERS>1 the
# exposure multiplies, so kill a run that stops writing episodes and let the job end honestly
# rather than burn its whole wall-clock.
STALL_MIN=${STALL_MIN:-30}
( while sleep 120; do
    pid=$(pgrep -f "benchmark_models.py" | head -1); [ -z "$pid" ] && exit 0
    newest=$(ls -t "$OUT"/*/episodes.jsonl "$OUT"/episodes.jsonl 2>/dev/null | head -1)
    [ -z "$newest" ] && continue
    age=$(( ( $(date +%s) - $(stat -c %Y "$newest" 2>/dev/null || echo 0) ) / 60 ))
    if [ "$age" -ge "$STALL_MIN" ]; then
      echo "[watchdog] no episode written for ${age}m (>= ${STALL_MIN}m) — killing $pid"
      kill "$pid" 2>/dev/null; sleep 5; kill -9 "$pid" 2>/dev/null
      pkill -f "$ARC_HEADLESS_EXE" 2>/dev/null
    fi
  done ) &
WATCHDOG=$!

echo "=== models: $MODELS"
$PY -u benchmark_models.py --models "$MODELS" \
    --episodes "$EPISODES" --rounds "$ROUNDS" --workers "$WORKERS" \
    "${FMT[@]}" "${EXTRA[@]}" \
    --out "$OUT" --base-port "$BASE_PORT"
RC=$?
kill "$WATCHDOG" 2>/dev/null
echo "=== exit=$RC  $(date)"
echo "=== results: $OUT/summary.json"
[ -f "$OUT/summary.json" ] && $PY -c "
import json,sys
d=json.load(open('$OUT/summary.json'))
for k,v in d.items():
    print(f\"  {k:<34} reward={v.get('meanTotalReward')} sat={v.get('meanFinalSat')} \"
          f\"completed={v.get('completed')}/{v.get('episodes')}\")"
exit $RC
