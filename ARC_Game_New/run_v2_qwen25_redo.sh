#!/usr/bin/env bash
# Re-run qwen2.5:3b / qwen2.5:7b under minimal_v2 after the chat() fix: these are NON-thinking
# Ollama models, and --reasoning_effort made the harness send a thinking request they reject with
#   400 '"qwen2.5:Xb" does not support thinking'
# The retry-without-reasoning_effort fallback in benchmark_models.py:chat() now also matches "think"
# (not just "reasoning"), so the call self-heals. SEPARATE --out (bench_promptfix_v2_local_qwen25)
# so we don't truncate the good qwen3.5:9b data in bench_promptfix_v2_local_cheap.
#
# Gated on the in-flight local launcher (run_v2_local_cheap.sh) so we don't contend on the single
# Mac GPU / Ollama. Self-verifying: a 1-ep/2-round smoke must produce rounds>0 before the full run.
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate

echo "$(date +%H:%M:%S) qwen25-redo: waiting for run_v2_local_cheap.sh to finish..."
while pgrep -f "run_v2_local_cheap.sh" >/dev/null 2>&1; do sleep 30; done
# also wait out any other ollama benchmark, then let its env close
while pgrep -f "benchmark_models.py.*11434" >/dev/null 2>&1; do sleep 30; done
sleep 5
echo "$(date +%H:%M:%S) qwen25-redo: GPU free — smoke-testing the fix on qwen2.5:3b"

SMOKE=bench_promptfix_v2_qwen25_smoke
python3 -u benchmark_models.py --policy llm \
  --models "qwen2.5:3b" \
  --base-url http://localhost:11434/v1 --api-key ollama \
  --image_mode none --action_format cmd --obs_encoding compact \
  --system_prompt minimal_v2 --transfers task_only --history 1 \
  --reasoning_effort low --max_tokens 3000 \
  --episodes 1 --rounds 2 --workers 1 \
  --out "$SMOKE" --base-port 9970 2>&1

ROUNDS=$(python3 - "$SMOKE/episodes.jsonl" <<'PY'
import json,sys
try:
    rows=[json.loads(l) for l in open(sys.argv[1])]
    print(sum(len(r["rounds"]) for r in rows))
except Exception: print(0)
PY
)
echo "$(date +%H:%M:%S) qwen25-redo: smoke produced $ROUNDS rounds"
if [ "${ROUNDS:-0}" -lt 1 ]; then
  echo "############ SMOKE FAILED — still 0 rounds, NOT launching full run. Inspect the error. ############"
  exit 1
fi

echo "############ smoke OK — full run qwen2.5:3b, qwen2.5:7b (minimal_v2, 32r, n=3) ############"
python3 -u benchmark_models.py --policy llm \
  --models "qwen2.5:3b,qwen2.5:7b" \
  --base-url http://localhost:11434/v1 --api-key ollama \
  --image_mode none --action_format cmd --obs_encoding compact \
  --system_prompt minimal_v2 --transfers task_only --history 1 \
  --reasoning_effort low --max_tokens 3000 \
  --episodes 3 --rounds 32 --workers 1 \
  --out bench_promptfix_v2_local_qwen25 --base-port 9970 2>&1

echo "############ qwen25-redo DONE ############"
wc -l bench_promptfix_v2_local_qwen25/episodes.jsonl 2>/dev/null
