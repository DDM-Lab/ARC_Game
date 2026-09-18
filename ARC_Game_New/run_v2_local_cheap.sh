#!/usr/bin/env bash
# minimal_v2 on the LOCAL cheap tier, then the mid-tier, 32 rounds, effort low, max_tokens 3000.
# Runs AFTER the in-flight local run (Track A) frees the GPU; coexists with the parallel
# gateway-cheap run (that one is API-bound on port 9990). base-port 9970. Separate --out per
# model group (benchmark_models.py truncates episodes.jsonl per --out on start).
#   1) cheap local  : qwen2.5:3b, qwen2.5:7b, qwen3.5:9b  -> bench_promptfix_v2_local_cheap
#   2) mid tier      : gpt-oss:20b, qwen3:30b-a3b          -> bench_promptfix_v2_low32
# No global `pkill ARC_Headless` (would kill the parallel gateway run's Unity on 9990).
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate

echo "$(date +%H:%M:%S) local-cheap: waiting for in-flight local (ollama) benchmark to finish..."
while pgrep -f "benchmark_models.py.*11434" >/dev/null 2>&1; do sleep 30; done
sleep 5
echo "$(date +%H:%M:%S) local-cheap: GPU free — starting"

echo "############ [1/2] cheap local: qwen2.5:3b, qwen2.5:7b, qwen3.5:9b ############"
python3 -u benchmark_models.py --policy llm \
  --models "qwen2.5:3b,qwen2.5:7b,qwen3.5:9b" \
  --base-url http://localhost:11434/v1 --api-key ollama \
  --image_mode none --action_format cmd --obs_encoding compact \
  --system_prompt minimal_v2 --transfers task_only --history 1 \
  --reasoning_effort low --max_tokens 3000 \
  --episodes 3 --rounds 32 --workers 1 \
  --out bench_promptfix_v2_local_cheap --base-port 9970 2>&1

echo "############ [2/2] mid tier: gpt-oss:20b, qwen3:30b-a3b ############"
python3 -u benchmark_models.py --policy llm \
  --models "gpt-oss:20b,qwen3:30b-a3b" \
  --base-url http://localhost:11434/v1 --api-key ollama \
  --image_mode none --action_format cmd --obs_encoding compact \
  --system_prompt minimal_v2 --transfers task_only --history 1 \
  --reasoning_effort low --max_tokens 3000 \
  --episodes 2 --rounds 32 --workers 1 \
  --out bench_promptfix_v2_low32 --base-port 9970 2>&1

echo "############ local ALL DONE ############"
wc -l bench_promptfix_v2_local_cheap/episodes.jsonl bench_promptfix_v2_low32/episodes.jsonl 2>/dev/null
