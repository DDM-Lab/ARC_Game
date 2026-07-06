#!/usr/bin/env bash
# minimal_v2 on the CHEAP GATEWAY tier (gpt-5-mini, claude-haiku-4.5), 32 rounds, effort low.
# API-bound -> no local GPU contention. Compares to the n=20 minimal baselines in
# bench_large_ablation/k1_auto (small code-rev confound, accepted per user).
# Gated on the in-flight LOCAL (ollama) run so its terminal `pkill ARC_Headless` can't kill our
# Unity sims; we run on base-port 9990 (Track A / local-cheap use 9970), so once clear we coexist
# with the local-cheap GPU run with no port or device collision. No global pkill here (would nuke
# the parallel local run's Unity).
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate

OUT="bench_promptfix_v2_gateway"

echo "$(date +%H:%M:%S) gateway-cheap: waiting for in-flight local (ollama) benchmark to finish..."
while pgrep -f "benchmark_models.py.*11434" >/dev/null 2>&1; do sleep 30; done
sleep 5   # let the local run's terminal pkill fire before we spawn Unity on 9990
echo "$(date +%H:%M:%S) gateway-cheap: starting (gateway default endpoint, port 9990)"

python3 -u benchmark_models.py --policy llm \
  --models "gpt-5-mini,us.anthropic.claude-haiku-4-5-20251001-v1:0" \
  --image_mode none --action_format cmd --obs_encoding compact \
  --system_prompt minimal_v2 --transfers task_only --history 1 \
  --reasoning_effort low \
  --episodes 3 --rounds 32 --workers 1 \
  --out "$OUT" --base-port 9990 2>&1

echo "############ gateway-cheap DONE ############"
wc -l "$OUT/episodes.jsonl" 2>/dev/null
