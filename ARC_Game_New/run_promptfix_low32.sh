#!/usr/bin/env bash
# Full-game (32-round) run of the new-prompt fix on the local reasoning models,
# reasoning ON at low effort, total budget capped at 3000 tokens (--max_tokens).
# Purpose: capture reasoning traces to see what uncertainties (beyond the Motel
# status fix) still keep these models from acting.
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate

OUT="bench_promptfix_low32"
N=2
ROUNDS=32

pkill -9 -f "ARC_Headless" 2>/dev/null; sleep 2

echo "############ LOCAL reasoning models — effort low, max_tokens 3000, 32 rounds ############"
python3 -u benchmark_models.py --policy llm \
  --models "gpt-oss:20b,qwen3:30b-a3b" \
  --base-url http://localhost:11434/v1 --api-key ollama \
  --image_mode none --action_format cmd --obs_encoding compact \
  --system_prompt minimal --transfers task_only --history 1 \
  --reasoning_effort low --max_tokens 3000 \
  --episodes "$N" --rounds "$ROUNDS" --workers 1 \
  --out "$OUT" --base-port 9970 2>&1
pkill -9 -f "ARC_Headless" 2>/dev/null

echo "############ DONE ############"
wc -l "$OUT/episodes.jsonl" 2>/dev/null
