#!/usr/bin/env bash
# Transfer-mode x action-format benchmark matrix over the cheap-tier models.
#   transfers {manual, task_only} x action_format {idx, cmd} = 4 cells
#   3 models x 3 episodes x 32 rounds per cell.  image_mode=none (fast Server build).
# manual    = standalone food/people resource_transfer actions exposed to the LLM.
# task_only = human-faithful; transfers happen only as a side effect of task choices.
# Each cell -> its own out dir with episodes.jsonl + summary.json.
# reasoning models run at reasoning_effort=low; reasoning_tokens are logged per round.
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

MODELS="gemini-2.5-flash,gpt-5-mini,gpt-5-nano"
EP=3
ROUNDS=32
WORKERS=8          # fast headless Server build; 9 jobs/cell
ROOT="bench_xfer_matrix"
mkdir -p "$ROOT"

run_cell () {  # transfers  fmt  base_port
  local XFER="$1" FMT="$2" PORT="$3"
  local OUT="$ROOT/${XFER}_${FMT}"
  echo "############################################################"
  echo "### CELL  transfers=$XFER  action_format=$FMT  port=$PORT"
  echo "############################################################"
  python3 benchmark_models.py --policy llm --models "$MODELS" \
    --image_mode none --action_format "$FMT" --transfers "$XFER" \
    --episodes "$EP" --rounds "$ROUNDS" --workers "$WORKERS" \
    --out "$OUT" --base-port "$PORT" 2>&1
}

run_cell manual    idx 9900
run_cell manual    cmd 9920
run_cell task_only idx 9940
run_cell task_only cmd 9960

echo "============================================================"
echo "MATRIX COMPLETE. Per-cell summaries:"
for d in "$ROOT"/*/; do
  echo "  $d summary.json"
done
