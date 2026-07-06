#!/usr/bin/env bash
# LARGE ABLATION (n=20): 2x2 factorial over K x transfers.
# Guidance held at MINIMAL (no strategic guidance); effort held LOW (RL-realistic); format held at
# the validated corner: cmd + compact + image none, 32 rounds.
#   K          in {1, 32}            -- history heavy-lifting (stateless vs whole-episode)
#   transfers  in {manual, auto}     -- logistics confound (task_only == auto)
# (reasoning_effort dropped as an axis: the CMU gateway rejects the knob for Claude -> no-op for
#  opus/sonnet/haiku, so it could not probe opus. Guidance dropped per design = minimal only.)
# 6-model set = tier x archetype (haiku/gpt-5-mini/gemini-pro/sonnet) + 2 flagships (opus, gpt-5.5).
# 4 cells x 6 models x n=20 = 480 episodes. Cells run SEQUENTIALLY (8 Unity workers each).
set -uo pipefail
cd "$(dirname "$0")"
source .venv/bin/activate 2>/dev/null

MODELS="gpt-5-mini,us.anthropic.claude-haiku-4-5-20251001-v1:0,gemini-2.5-pro,us.anthropic.claude-sonnet-4-6,us.anthropic.claude-opus-4-8,gpt-5.5"
OUT=bench_large_ablation
N=20
ROUNDS=32
WORKERS=8
mkdir -p "$OUT"

port=9900
cell=0
for K in 1 32; do
  for T in manual task_only; do
    tname=$([ "$T" = "task_only" ] && echo auto || echo manual)
    cell=$((cell+1))
    name="k${K}_${tname}"
    echo "============================================================"
    echo "### CELL ${cell}/4  $name   K=$K transfers=$T  -> $OUT/$name  (port $port)"
    python3 benchmark_models.py --policy llm --models "$MODELS" \
      --image_mode none --action_format cmd --obs_encoding compact \
      --system_prompt minimal --transfers "$T" --history "$K" --reasoning_effort low \
      --episodes "$N" --rounds "$ROUNDS" --workers "$WORKERS" \
      --out "$OUT/$name" --base-port "$port" 2>&1
    echo "### CELL ${cell}/4 COMPLETE: $name"
    port=$((port+30))
  done
done
echo "============================================================"
echo "LARGE ABLATION COMPLETE: 2x2 (K x transfers), 6 models, n=$N (480 episodes)."
