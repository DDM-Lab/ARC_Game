#!/usr/bin/env bash
# Action-format ablation: idx vs cmd, across a capability ladder (cheap + mid tier).
# This is the ONLY axis that varies. Everything else is held fixed so the idx-vs-cmd
# delta is clean and the "do smarter models do better with cmd?" hypothesis is testable.
#
#   FIXED: system_prompt=original, transfers=manual, image=none,
#          reasoning_effort=low (thinking models), temperature=vendor-default, 3 ep x 32 rounds.
#   VARY:  action_format {idx, cmd}
#   MODELS (3 vendors x 2 tiers):
#     cheap: gpt-5-mini,  claude-haiku-4-5,  gemini-2.5-flash
#     mid:   gpt-5.4,     claude-sonnet-4-6, gemini-2.5-pro
#
# Per-round cmd parser rejections are logged (rounds[].cmdErrors) so cmd failures can be
# characterized by category afterward; idx logs invalidIndices as its analogue.
# Ports 9700-9727. Clean stop: ./stop_bench.sh (written after this run) or tree-kill.
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

MODELS="gpt-5-mini,us.anthropic.claude-haiku-4-5-20251001-v1:0,gemini-2.5-flash,gpt-5.4,us.anthropic.claude-sonnet-4-6,gemini-2.5-pro"
EP=3
ROUNDS=32
WORKERS=8
ROOT="bench_format_ablation"
mkdir -p "$ROOT"

run_cell () {  # fmt  base_port
  local FMT="$1" PORT="$2"
  echo "############################################################"
  echo "### CELL  action_format=$FMT  port=$PORT  (6 models x $EP ep)"
  echo "############################################################"
  python3 benchmark_models.py --policy llm --models "$MODELS" \
    --image_mode none --action_format "$FMT" --transfers manual \
    --system_prompt original --reasoning_effort low \
    --episodes "$EP" --rounds "$ROUNDS" --workers "$WORKERS" \
    --out "$ROOT/$FMT" --base-port "$PORT" 2>&1
}

run_cell idx 9700
run_cell cmd 9720

echo "============================================================"
echo "FORMAT ABLATION COMPLETE."
for d in "$ROOT"/*/; do echo "  $d summary.json"; done
