#!/usr/bin/env bash
# Image x action-format benchmark matrix over the cheap-tier vision models.
#   image_mode {none, synthetic, real} x action_format {idx, cmd} = 6 cells
#   3 models x 3 episodes x 32 rounds per cell.
# Each cell -> its own out dir with episodes.jsonl + summary.json.
# real cells use the graphics render build (workers=4 to keep GPU contention sane);
# none/synthetic use the fast headless Server build (workers=8 on M5 Pro / 48GB).
# reasoning models run at reasoning_effort=low; reasoning_tokens are logged per round.
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

MODELS="gemini-2.5-flash,gpt-5-mini,gpt-5-nano"
EP=3
ROUNDS=32
ROOT="bench_matrix"
mkdir -p "$ROOT"

run_cell () {  # img_mode  fmt  workers  base_port
  local IMG="$1" FMT="$2" W="$3" PORT="$4"
  local OUT="$ROOT/${IMG}_${FMT}"
  echo "############################################################"
  echo "### CELL  image_mode=$IMG  action_format=$FMT  workers=$W  port=$PORT"
  echo "############################################################"
  python3 benchmark_models.py --policy llm --models "$MODELS" \
    --image_mode "$IMG" --action_format "$FMT" \
    --episodes "$EP" --rounds "$ROUNDS" --workers "$W" \
    --out "$OUT" --base-port "$PORT" 2>&1
}

# text-only and synthetic: fast Server build, 8 workers (M5 Pro / 48GB).
# 9 jobs/cell, so 8 workers ~ all but one episode run concurrently.
run_cell none      idx 8 9900
run_cell none      cmd 8 9920
run_cell synthetic idx 8 9940
run_cell synthetic cmd 8 9960
# real: graphics render build, 4 workers (each opens a render context; keep GPU contention sane)
run_cell real      idx 4 9980
run_cell real      cmd 4 10000

echo "============================================================"
echo "MATRIX COMPLETE. Per-cell summaries:"
for d in "$ROOT"/*/; do
  echo "  $d summary.json"
done
