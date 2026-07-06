#!/usr/bin/env bash
# OPEN-SOURCE LOCAL SMOKE (n=3): 5 self-hosted models via Ollama, validated corner.
# Format held at minimal_cmd_v3: --action_format cmd --obs_encoding compact --system_prompt minimal
#   --image_mode none, 32 rounds.  Logistics: --transfers task_only (=auto), --history 1.
# Thinking control: $1 = reasoning_effort (none|low|medium|high, default none). "none" disables
#   thinking on the local reasoning models (qwen3/qwen3.5/gpt-oss) so the smoke runs in minutes and
#   measures the raw policy; "low" lets them think (much slower). Output dir is per-effort.
# Models run SEQUENTIALLY, one benchmark_models.py call each, so Ollama keeps a single model resident
# (interleaving models in one run would force a reload per request -> thrashing). Local concurrency
# kept low (2 Unity workers) since one model serves all requests on the Mac GPU.
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

EFFORT="${1:-none}"
BASE_URL="http://localhost:11434/v1"
OUT="bench_oss_smoke_${EFFORT}"
N=3
ROUNDS=16   # smoke horizon (was 32). Probe showed time is generation-bound (~29 tok/s) + GPU
            # contention w/ headless Unity, NOT prompt size or token cap — so fewer rounds is the
            # only clean ~2x. Not directly comparable to the 32-round gateway runs; this is a smoke.
WORKERS=1   # local: one headless Unity at a time. Two concurrent sims sharing the GPU with the
            # resident LLM was wedging mid-episode (Unity hang -> gym recv blocks). One is robust.
PORT=9970
mkdir -p "$OUT"
echo "### OSS smoke: reasoning_effort=$EFFORT  ->  $OUT/"

# ollama tag  ->  output subdir name
MODELS=(
  "qwen2.5:7b"
  # qwen3.5:9b skipped — generates valid output but plays a degenerate passive policy
  # (submits no action ~24/25 rounds) AND is dense-9B-slow (~29 tok/s) on this Mac GPU.
  "gpt-oss:20b"
  "qwen3:30b-a3b"
  "gemma3:27b"
)

# --- watchdog config: a TRUE Unity/gym hang (recv blocks forever) is rare but real (observed a
# 13-min freeze). Far more common is the local LLM just being SLOW: qwen3.5 ~10 tok/s (~30s/round),
# and the 30B/27B models slower still, so a single round (esp. one that rambles toward max_tokens)
# can legitimately go quiet for minutes. So the threshold is generous and python runs UNBUFFERED
# (python3 -u) — without that, block-buffered stdout never updates the log mtime and the watchdog
# kills healthy slow runs. benchmark_models.py opens episodes.jsonl "w", so a kill+rerun redoes the
# model cleanly (no dup episodes). Only a stall LONGER than any real generation trips a retry.
STALL_TIMEOUT=600
MAX_TRIES=3

eps_count() { [ -f "$1/episodes.jsonl" ] && wc -l < "$1/episodes.jsonl" | tr -d ' ' || echo 0; }

run_model() {   # $1=M $2=safe $3=port ; echoes status, returns 0 on success
  local M="$1" safe="$2" port="$3" dir="$OUT/$safe" mlog
  mkdir -p "$dir"; mlog="$dir/run.log"
  local try=0
  while [ "$try" -lt "$MAX_TRIES" ]; do
    try=$((try+1))
    [ "$(eps_count "$dir")" -ge "$N" ] && { echo "###   $M complete"; return 0; }
    echo "###   attempt $try/$MAX_TRIES: $M (port $port)"
    : > "$mlog"
    PYTHONUNBUFFERED=1 python3 -u benchmark_models.py --policy llm --models "$M" \
      --base-url "$BASE_URL" --api-key ollama \
      --image_mode none --action_format cmd --obs_encoding compact \
      --system_prompt minimal --transfers task_only --history 1 --reasoning_effort "$EFFORT" \
      --episodes "$N" --rounds "$ROUNDS" --workers "$WORKERS" \
      --out "$dir" --base-port "$port" >> "$mlog" 2>&1 &
    local pypid=$!
    while kill -0 "$pypid" 2>/dev/null; do
      sleep 20
      local now mt idle; now=$(date +%s); mt=$(stat -f %m "$mlog" 2>/dev/null || echo "$now")
      idle=$((now - mt))
      if [ "$idle" -ge "$STALL_TIMEOUT" ]; then
        echo "###   STALL: ${idle}s no log activity -> kill+retry $M"
        kill -9 "$pypid" 2>/dev/null
        pkill -9 -f "ARC_DisasterSimulation" 2>/dev/null
        sleep 3; break
      fi
    done
    wait "$pypid" 2>/dev/null
    [ "$(eps_count "$dir")" -ge "$N" ] && { echo "###   $M done ($(eps_count "$dir") eps)"; return 0; }
    pkill -9 -f "ARC_DisasterSimulation" 2>/dev/null; sleep 2   # clean strays before retry
  done
  echo "###   GAVE UP on $M after $MAX_TRIES tries (have $(eps_count "$dir")/$N)"
  return 1
}

i=0
for M in "${MODELS[@]}"; do
  i=$((i+1))
  safe=$(echo "$M" | tr ':/' '__')
  echo "============================================================"
  echo "### OSS ${i}/${#MODELS[@]}  $M  -> $OUT/$safe"
  run_model "$M" "$safe" "$PORT"
  PORT=$((PORT+20))
done
echo "============================================================"
echo "OSS SMOKE COMPLETE: ${#MODELS[@]} models, n=$N (minimal_cmd_v3, auto, K=1, reasoning_effort=$EFFORT)."
for M in "${MODELS[@]}"; do
  safe=$(echo "$M" | tr ':/' '__')
  echo "  $M: $(eps_count "$OUT/$safe")/$N eps"
done
