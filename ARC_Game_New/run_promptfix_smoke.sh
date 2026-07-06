#!/usr/bin/env bash
# Quick smoke of the new prompting fixes (Passive status, transfers gated to
# manual mode, affects-placeholder drop). Two flagships via the CMU gateway +
# two local Ollama models. Same canonical flags as the OSS smoke, minimal prompt.
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate

OUT="bench_promptfix_smoke"
N=2
ROUNDS=16
COMMON="--policy llm --image_mode none --action_format cmd --obs_encoding compact \
        --system_prompt minimal --transfers task_only --history 1 \
        --episodes $N --rounds $ROUNDS --workers 1"

pkill -9 -f "ARC_Headless" 2>/dev/null; sleep 2

echo "############ GATEWAY (gpt-5-mini, haiku) — effort low ############"
python3 -u benchmark_models.py $COMMON \
  --models "gpt-5-mini,us.anthropic.claude-haiku-4-5-20251001-v1:0" \
  --reasoning_effort low \
  --out "$OUT" --base-port 9970 2>&1
pkill -9 -f "ARC_Headless" 2>/dev/null; sleep 3

echo "############ LOCAL (qwen2.5:3b, gpt-oss:20b) — effort none ############"
python3 -u benchmark_models.py $COMMON \
  --models "qwen2.5:3b,gpt-oss:20b" \
  --base-url http://localhost:11434/v1 --api-key ollama \
  --reasoning_effort none \
  --out "$OUT" --base-port 9970 2>&1
pkill -9 -f "ARC_Headless" 2>/dev/null

echo "############ DONE ############"
for d in "$OUT"/*/; do
  echo "  $(basename "$d"): $(wc -l < "$d/episodes.jsonl" 2>/dev/null || echo 0)/$N eps"
done
