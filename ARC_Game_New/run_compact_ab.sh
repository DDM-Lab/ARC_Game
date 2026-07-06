#!/usr/bin/env bash
# A/B for the compact obs renderer: ONE new cell (minimal cmd + --obs_encoding compact),
# directly comparable to the existing JSON baseline bench_format_ablation/minimal_cmd_v3
# (identical code path — needStaff block + v3 prompts — the ONLY difference is obs serialization).
# Same config as the v3 cells: 6-model ladder, manual transfers, image none, effort low, 3 ep x 32 rounds.
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

MODELS="gpt-5-mini,us.anthropic.claude-haiku-4-5-20251001-v1:0,gemini-2.5-flash,gpt-5.4,us.anthropic.claude-sonnet-4-6,gemini-2.5-pro"
echo "### COMPACT A/B  minimal cmd + obs_encoding=compact  -> bench_format_ablation/compact_minimal_cmd_v3"
python3 benchmark_models.py --policy llm --models "$MODELS" \
  --image_mode none --action_format cmd --transfers manual \
  --system_prompt minimal --obs_encoding compact --reasoning_effort low \
  --episodes 3 --rounds 32 --workers 8 \
  --out bench_format_ablation/compact_minimal_cmd_v3 --base-port 9900 2>&1
echo "============================================================"
echo "COMPACT A/B COMPLETE."
echo "  json baseline : bench_format_ablation/minimal_cmd_v3"
echo "  compact (new) : bench_format_ablation/compact_minimal_cmd_v3"
