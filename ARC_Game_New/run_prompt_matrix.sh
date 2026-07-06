#!/usr/bin/env bash
# Prompt x action-format CORE grid (fractional design; exploration knob held at baseline).
#   system_prompt {original, minimal} x action_format {idx, cmd} = 4 cells
#   3 models x 3 episodes x 32 rounds.  transfers=manual, image=none (fast Server build).
# Exploration knob fixed at BASELINE here: reasoning_effort=low (gpt-5*) + temperature=0.2 (gemini).
# The high-exploration SWEEP (3 levels) is run separately on the winning cell across all 3 models
# via run_prompt_sweep.sh.  Each model reads its own knob from one invocation:
#   level1 (baseline)  effort=low    temp=0.2
#   level2             effort=medium temp=1.0
#   level3             effort=high   temp=1.5
# Ports 9700-9767 avoid the still-running transfers matrix (9900s).
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

MODELS="gemini-2.5-flash,gpt-5-mini,gpt-5-nano,us.anthropic.claude-haiku-4-5-20251001-v1:0"
EP=3
ROUNDS=32
WORKERS=8          # xfer matrix finished; 4 models x 3 ep = 12 jobs/cell
BASE_TEMP=0.2      # gemini baseline (low/exploit); gpt-5* ignore temperature
ROOT="bench_prompt_matrix"
mkdir -p "$ROOT"

run_cell () {  # system_prompt  fmt  base_port
  local SYS="$1" FMT="$2" PORT="$3"
  local OUT="$ROOT/${SYS}_${FMT}"
  echo "############################################################"
  echo "### CELL  system_prompt=$SYS  action_format=$FMT  port=$PORT"
  echo "############################################################"
  python3 benchmark_models.py --policy llm --models "$MODELS" \
    --image_mode none --action_format "$FMT" --transfers manual \
    --system_prompt "$SYS" --reasoning_effort low --temperature "$BASE_TEMP" \
    --episodes "$EP" --rounds "$ROUNDS" --workers "$WORKERS" \
    --out "$OUT" --base-port "$PORT" 2>&1
}

run_cell original idx 9700
run_cell original cmd 9720
run_cell minimal  idx 9740
run_cell minimal  cmd 9760

echo "============================================================"
echo "CORE GRID COMPLETE. Per-cell summaries:"
for d in "$ROOT"/*/; do echo "  $d summary.json"; done
echo "Next: pick the winning (system_prompt x format) cell and run ./run_prompt_sweep.sh SYS FMT"
