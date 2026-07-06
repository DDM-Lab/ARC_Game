#!/usr/bin/env bash
# MINIMAL-prompt half of the {cmd,idx} x {original,minimal} 2x2 action-format ablation.
# The ORIGINAL half (system_prompt=original) is produced by run_format_ablation.sh, which
# writes bench_format_ablation/{idx,cmd}. This script adds the minimal-prompt cells,
# bench_format_ablation/minimal_{idx,cmd}, completing the 2x2:
#
#                 idx                         cmd
#   original   bench_format_ablation/idx   bench_format_ablation/cmd          (run_format_ablation.sh)
#   minimal    .../minimal_idx             .../minimal_cmd                    (this script)
#
# Everything else identical to the original half: 6-model ladder, transfers=manual,
# image=none, effort=low, vendor-default temperature, 3 ep x 32 rounds.
# Optional arg = PID of the original-half driver to wait for (keeps <=8 Unity workers at once).
# Ports 9740/9760 (distinct from the original half's 9700/9720, harmless even if overlap).
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

WAIT_PID="${1:-}"
if [ -n "$WAIT_PID" ]; then
  echo "[minimal half] waiting for original-half driver PID $WAIT_PID to exit..."
  while kill -0 "$WAIT_PID" 2>/dev/null; do sleep 30; done
  echo "[minimal half] original half finished; starting minimal cells."
fi

MODELS="gpt-5-mini,us.anthropic.claude-haiku-4-5-20251001-v1:0,gemini-2.5-flash,gpt-5.4,us.anthropic.claude-sonnet-4-6,gemini-2.5-pro"
EP=3
ROUNDS=32
WORKERS=8
ROOT="bench_format_ablation"
mkdir -p "$ROOT"

run_cell () {  # fmt  base_port
  local FMT="$1" PORT="$2"
  echo "############################################################"
  echo "### CELL  system_prompt=minimal  action_format=$FMT  port=$PORT  (6 models x $EP ep)"
  echo "############################################################"
  python3 benchmark_models.py --policy llm --models "$MODELS" \
    --image_mode none --action_format "$FMT" --transfers manual \
    --system_prompt minimal --reasoning_effort low \
    --episodes "$EP" --rounds "$ROUNDS" --workers "$WORKERS" \
    --out "$ROOT/minimal_$FMT" --base-port "$PORT" 2>&1
}

run_cell idx 9740
run_cell cmd 9760

echo "============================================================"
echo "MINIMAL HALF COMPLETE. Full 2x2 now in $ROOT/:"
echo "  original:  $ROOT/idx  $ROOT/cmd"
echo "  minimal:   $ROOT/minimal_idx  $ROOT/minimal_cmd"
