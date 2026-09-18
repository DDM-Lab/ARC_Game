#!/usr/bin/env bash
# minimal_v2 arm of the 32-round full-game run, SAME models/config as bench_promptfix_low32
# (gpt-oss:20b + qwen3:30b-a3b, effort low, max_tokens 3000) so the two are directly comparable.
# vs bench_promptfix_low32 this isolates Theme 1 (sharpened build-then-staff) + Theme 4 (choices
# un-truncated 90->200) + the v2 prompt/encoding fix layer. Separate --out (no overwrite).
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate

OUT="bench_promptfix_v2_low32"
N=2
ROUNDS=32

# Don't contend with the in-flight bench_promptfix_low32 run on the single Mac GPU / Ollama:
# wait for any other benchmark_models.py to exit first (this script's own python starts later).
echo "$(date +%H:%M:%S) waiting for any in-flight benchmark to finish..."
while pgrep -f "benchmark_models.py" >/dev/null 2>&1; do sleep 30; done
echo "$(date +%H:%M:%S) clear — starting minimal_v2 run"

pkill -9 -f "ARC_Headless" 2>/dev/null; sleep 2

echo "############ minimal_v2 — effort low, max_tokens 3000, 32 rounds ############"
python3 -u benchmark_models.py --policy llm \
  --models "gpt-oss:20b,qwen3:30b-a3b" \
  --base-url http://localhost:11434/v1 --api-key ollama \
  --image_mode none --action_format cmd --obs_encoding compact \
  --system_prompt minimal_v2 --transfers task_only --history 1 \
  --reasoning_effort low --max_tokens 3000 \
  --episodes "$N" --rounds "$ROUNDS" --workers 1 \
  --out "$OUT" --base-port 9970 2>&1
pkill -9 -f "ARC_Headless" 2>/dev/null

echo "############ DONE ############"
wc -l "$OUT/episodes.jsonl" 2>/dev/null
