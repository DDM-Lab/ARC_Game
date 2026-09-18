#!/usr/bin/env bash
# Re-run the two cmd cells of the {cmd,idx}x{original,minimal} ablation with the improved cmd path
# (available affordance block + commonsense ordering + same-turn hire->staff synthesis).
# Writes to *_v2 dirs so the original cmd results (bench_format_ablation/{cmd,minimal_cmd}) stay
# intact for the before/after reward delta. idx cells are unchanged and not re-run.
# Identical config to the original ablation: 6-model ladder, manual transfers, image none,
# effort low, vendor-default temperature, 3 ep x 32 rounds. Fresh ports (9820/9840).
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

MODELS="gpt-5-mini,us.anthropic.claude-haiku-4-5-20251001-v1:0,gemini-2.5-flash,gpt-5.4,us.anthropic.claude-sonnet-4-6,gemini-2.5-pro"
EP=3
ROUNDS=32
WORKERS=8
ROOT="bench_format_ablation"
mkdir -p "$ROOT"

run_cell () {  # variant  out_subdir  base_port
  local VARIANT="$1" OUT="$2" PORT="$3"
  echo "############################################################"
  echo "### CMD RE-RUN  system_prompt=$VARIANT  port=$PORT  -> $ROOT/$OUT  (6 models x $EP ep)"
  echo "############################################################"
  python3 benchmark_models.py --policy llm --models "$MODELS" \
    --image_mode none --action_format cmd --transfers manual \
    --system_prompt "$VARIANT" --reasoning_effort low \
    --episodes "$EP" --rounds "$ROUNDS" --workers "$WORKERS" \
    --out "$ROOT/$OUT" --base-port "$PORT" 2>&1
}

run_cell original cmd_v3         9860
run_cell minimal  minimal_cmd_v3 9880

echo "============================================================"
echo "CMD RE-RUN COMPLETE."
echo "  baseline: $ROOT/cmd        $ROOT/minimal_cmd"
echo "  v2 (block+staff synth):    $ROOT/cmd_v2  $ROOT/minimal_cmd_v2"
echo "  v3 (+needStaff +prompt):   $ROOT/cmd_v3  $ROOT/minimal_cmd_v3"
