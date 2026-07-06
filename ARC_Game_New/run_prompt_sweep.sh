#!/usr/bin/env bash
# Exploration-knob SWEEP on the winning (system_prompt x action_format) cell, all 3 models.
# Usage:  ./run_prompt_sweep.sh SYS FMT      e.g.  ./run_prompt_sweep.sh minimal cmd
# The single split knob (per the design) reads per model from one invocation:
#   gpt-5*  -> reasoning_effort    gemini/claude -> temperature
# NOTE: Anthropic caps temperature at 1.0, so Haiku's level3 (temp=1.5) is clamped to 1.0 in
# run_episode (logged as temperature_sent); Haiku's two top sweep levels effectively coincide.
# Level 1 (baseline: effort=low, temp=0.2) is ALREADY in run_prompt_matrix.sh; this adds:
#   level2  effort=medium temp=1.0
#   level3  effort=high   temp=1.5
# Ports 9780-9799 avoid both the core grid (9700s) and the xfer matrix (9900s).
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

SYS="${1:?usage: run_prompt_sweep.sh SYS FMT}"
FMT="${2:?usage: run_prompt_sweep.sh SYS FMT}"
MODELS="gemini-2.5-flash,gpt-5-mini,gpt-5-nano,us.anthropic.claude-haiku-4-5-20251001-v1:0"
EP=3
ROUNDS=32
WORKERS=6
ROOT="bench_prompt_sweep"
mkdir -p "$ROOT"

run_level () {  # eff  temp  base_port  tag
  local EFF="$1" TEMP="$2" PORT="$3" TAG="$4"
  local OUT="$ROOT/${SYS}_${FMT}_${TAG}"
  echo "############################################################"
  echo "### SWEEP  $SYS x $FMT  effort=$EFF  temp=$TEMP  port=$PORT"
  echo "############################################################"
  python3 benchmark_models.py --policy llm --models "$MODELS" \
    --image_mode none --action_format "$FMT" --transfers manual \
    --system_prompt "$SYS" --reasoning_effort "$EFF" --temperature "$TEMP" \
    --episodes "$EP" --rounds "$ROUNDS" --workers "$WORKERS" \
    --out "$OUT" --base-port "$PORT" 2>&1
}

run_level medium 1.0 9780 explore-mid
run_level high   1.5 9790 explore-hi

echo "============================================================"
echo "SWEEP COMPLETE for $SYS x $FMT. Combine with the matching core-grid cell (baseline level)."
for d in "$ROOT"/*/; do echo "  $d summary.json"; done
