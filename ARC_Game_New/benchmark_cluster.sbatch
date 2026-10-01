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

# Conda env carrying vllm + the benchmark deps (the one the Sep 2026 v6 sweeps ran in).
# CONDA_ENV="" skips it and falls back to ./.venv or python3.
CONDA_ENV=${CONDA_ENV-/zfsauton/scratch/$USER/conda_envs/vllm027}
if [ -n "$CONDA_ENV" ] && [ -x "$CONDA_ENV/bin/python" ]; then
  export PATH="$CONDA_ENV/bin:$PATH"
  PY=${PY:-$CONDA_ENV/bin/python}
fi
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
# HISTORY: turns of prior conversation replayed. Each turn adds ~950 prompt tokens, so
#   h=1 ~3.6k, h=4 ~6.4k, h=16 ~18k, h=32 ~33k -> raise MAX_MODEL_LEN past h=4.
# TRANSFERS: task_only suppresses standalone transfers (human-faithful); manual enumerates them.
# SYSTEM_PROMPT: minimal_v6_1 (default: v6 with live numbers + unavailable choices marked) | minimal_v6 | minimal_v2..v5 |
#   minimal | original. SEED: base seed; episode i uses SEED+i, so arms see identical scenarios.
# MAP_CONFIG: map JSON path for every episode, or "none" for the built-in layout (default: the
#   build's bundled map_config.json). Each episode record carries the map fingerprint it ran.
FMT=(--obs_encoding compact --system_prompt "${SYSTEM_PROMPT:-minimal_v6_1}"
     --image_mode none --transfers "${TRANSFERS:-task_only}" --history "${HISTORY:-1}")

echo "=== CORA benchmark  mode=$MODE  job=${SLURM_JOB_ID:-local}  node=$(hostname)  $(date)"
echo "    ARC_GAME_PATH=$ARC"
echo "    episodes=$EPISODES rounds=$ROUNDS workers=$WORKERS base_port=$BASE_PORT"
[ -x "$ARC_HEADLESS_EXE" ] || { echo "FATAL: headless binary not executable at $ARC_HEADLESS_EXE"; exit 1; }

# Kill ONLY this job's Unity processes. `pkill -f "$ARC_HEADLESS_EXE"` matches the exe path,
# which is identical for every bench job, so on a shared node one job's cleanup/watchdog killed
# a concurrently running job's Unity (observed: 33214's exit trap took down 33215, whose five
# episodes all reported "Unity closed connection"). Each Unity is launched with `-gym-port N`,
# and this job owns BASE_PORT..BASE_PORT+WORKERS, so match on those instead.
kill_our_unity() {
  local p
  for p in $(seq "$BASE_PORT" $((BASE_PORT + WORKERS))); do
    pkill -f -- "-gym-port $p" 2>/dev/null
  done
  return 0
}

VLLM_PID=""
cleanup() {
  echo "[cleanup] $(date +%H:%M:%S)"
  [ -n "$VLLM_PID" ] && kill "$VLLM_PID" 2>/dev/null
  kill_our_unity
}
trap cleanup EXIT INT TERM

EXTRA=()
if [ "$MODE" = "local" ]; then
  MODEL=${MODEL:?MODE=local requires MODEL=<hf-id>}
  PORT=${VLLM_PORT:-$(( BASE_PORT + 900 ))}
  # 12288 mirrors the local ctx12k variants: prompt ~2k + generation 6k, with headroom. A larger
  # window only inflates the KV reservation, which is what drove this machine into swap locally.
  # --action_format tools sends tool_choice=auto, which vLLM rejects with a 400 unless the
  # server is started with a tool parser ("auto" tool choice requires --enable-auto-tool-choice
  # and --tool-call-parser to be set). Ollama parses tool calls natively, so the local sweep
  # never needed this. hermes is the Qwen2.5/Qwen3 parser in vLLM (qwen3coder is Coder-only).
  # VLLM_EXTRA passes serve-time tuning through unquoted (word-split on purpose), e.g.
  #   VLLM_EXTRA='--max-num-batched-tokens 16384'
  #   VLLM_EXTRA='--speculative-config {"method":"ngram","num_speculative_tokens":5,"prompt_lookup_max":4}'
  # Auton GPU nodes have no CUDA toolkit. FlashInfer JIT-compiles its sampler (and is the default
  # attention backend), and the pip cu13 nvcc does not match its CUDA-12 headers: startup died with
  # "CUDA compiler and CUDA toolkit headers are incompatible" after the model loaded (job 69407).
  # vLLM's native sampler + Triton attention need no nvcc -- the settings serve/qwen27b_debug.sbatch
  # has used all along.
  export VLLM_USE_FLASHINFER_SAMPLER=0 VLLM_WORKER_MULTIPROC_METHOD=spawn
  # Tool-call parser by family: Qwen2.5/Qwen3 emit JSON-in-<tool_call> (hermes); Qwen3.5 and later
  # emit the XML form (qwen3_xml), and hermes silently parses nothing. Those also need the qwen3
  # reasoning parser, or </think> leaks into content.
  case "$MODEL" in
    *Qwen3.[5-9]*|*Qwen3.[1-9][0-9]*) : "${TOOL_PARSER:=qwen3_xml}"; REASONING=(--reasoning-parser qwen3) ;;
    *) : "${TOOL_PARSER:=hermes}"; REASONING=() ;;
  esac
  vllm serve "$MODEL" --port "$PORT" --max-model-len ${MAX_MODEL_LEN:-12288} \
       --gpu-memory-utilization ${GPU_UTIL:-0.85} \
       --tensor-parallel-size ${TP:-1} --attention-backend TRITON_ATTN \
       --enable-auto-tool-choice --tool-call-parser "$TOOL_PARSER" "${REASONING[@]}" \
       ${VLLM_EXTRA:-} \
       > "$OUT/vllm.log" 2>&1 &
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
  # NOTHINK=0 turns the thinking-off default OFF, so a run can be done WITH thinking and a
  # chosen effort (REASONING_EFFORT=low|medium|high). The two are mutually exclusive: sending
  # reasoning_effort alongside enable_thinking=false defeats the flag (measured), so never both.
  if [ "${NOTHINK:-1}" = "1" ]; then
    case "$MODEL" in *[Qq]wen3*) EXTRA+=(--no-thinking) ;; esac
  else
    EXTRA+=(--reasoning_effort "${REASONING_EFFORT:-low}")
  fi
else
  MODELS=${MODELS:?MODE=api requires MODELS="a,b,c"}
  # Gateway key comes from the environment / .env; never echoed.
  # API_BASE points the harness at a non-gateway OpenAI-compatible endpoint, e.g.
  #   API_BASE=https://api.anthropic.com/v1/  with the key in ARC_API_KEY (never on argv).
  [ -n "${API_BASE:-}" ] && EXTRA+=(--base-url "$API_BASE")
  EXTRA+=(--max_tokens ${MAX_TOKENS:-4096})
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
      kill_our_unity
    fi
  done ) &
WATCHDOG=$!

echo "=== models: $MODELS"
$PY -u benchmark_models.py --models "$MODELS" \
    --episodes "$EPISODES" --rounds "$ROUNDS" --workers "$WORKERS" \
    "${FMT[@]}" "${EXTRA[@]}" \
    --out "$OUT" --base-port "$BASE_PORT" ${SEED:+--seed "$SEED"} ${MAP_CONFIG:+--map-config "$MAP_CONFIG"}
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
