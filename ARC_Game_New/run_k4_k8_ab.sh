#!/usr/bin/env bash
# K=4 and K=8 ablation cells: map the LOW end of the history curve.
# Same code path as compact_minimal_cmd_v3 (K=1) / _k16 / _k32 — ONLY --history differs.
# Goal: find the cheapest K that retains K=16-level reward (reward-per-GPU-byte / training throughput).
# Run SEQUENTIALLY (K=4 then K=8): each uses 8 Unity workers; 16 at once would contend.
# Same 6-model ladder, manual transfers, image none, effort low, minimal prompt, 3 ep x 32 rounds.
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

MODELS="gpt-5-mini,us.anthropic.claude-haiku-4-5-20251001-v1:0,gemini-2.5-flash,gpt-5.4,us.anthropic.claude-sonnet-4-6,gemini-2.5-pro"

echo "### K=4 A/B  compact cmd + --history 4  -> bench_format_ablation/compact_minimal_cmd_k4"
python3 benchmark_models.py --policy llm --models "$MODELS" \
  --image_mode none --action_format cmd --transfers manual \
  --system_prompt minimal --obs_encoding compact --history 4 --reasoning_effort low \
  --episodes 3 --rounds 32 --workers 8 \
  --out bench_format_ablation/compact_minimal_cmd_k4 --base-port 9980 2>&1
echo "============================================================"
echo "K=4 A/B COMPLETE."

echo "### K=8 A/B  compact cmd + --history 8  -> bench_format_ablation/compact_minimal_cmd_k8"
python3 benchmark_models.py --policy llm --models "$MODELS" \
  --image_mode none --action_format cmd --transfers manual \
  --system_prompt minimal --obs_encoding compact --history 8 --reasoning_effort low \
  --episodes 3 --rounds 32 --workers 8 \
  --out bench_format_ablation/compact_minimal_cmd_k8 --base-port 9860 2>&1
echo "============================================================"
echo "K=8 A/B COMPLETE."
echo "K curve now spans: K=1 (compact_minimal_cmd_v3) / K=4 / K=8 / K=16 / K=32"
