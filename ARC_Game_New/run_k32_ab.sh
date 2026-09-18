#!/usr/bin/env bash
# K=32 ablation cell: the winning compact-cmd format, now HISTORY-CARRYING (whole episode).
# Identical code path to bench_format_ablation/compact_minimal_cmd_v3 (needStaff block + v3 prompts
# + compact obs) — the ONLY difference is --history 32: the policy sees an append-only window of
# all prior (state, action) turns instead of a single stateless step (K=1).
# Same 6-model ladder, manual transfers, image none, effort low, minimal prompt, 3 ep x 32 rounds.
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

MODELS="gpt-5-mini,us.anthropic.claude-haiku-4-5-20251001-v1:0,gemini-2.5-flash,gpt-5.4,us.anthropic.claude-sonnet-4-6,gemini-2.5-pro"
echo "### K=32 A/B  compact cmd + --history 32  -> bench_format_ablation/compact_minimal_cmd_k32"
python3 benchmark_models.py --policy llm --models "$MODELS" \
  --image_mode none --action_format cmd --transfers manual \
  --system_prompt minimal --obs_encoding compact --history 32 --reasoning_effort low \
  --episodes 3 --rounds 32 --workers 8 \
  --out bench_format_ablation/compact_minimal_cmd_k32 --base-port 9920 2>&1
echo "============================================================"
echo "K=32 A/B COMPLETE."
echo "  K=1  baseline : bench_format_ablation/compact_minimal_cmd_v3"
echo "  K=32 (new)    : bench_format_ablation/compact_minimal_cmd_k32"
