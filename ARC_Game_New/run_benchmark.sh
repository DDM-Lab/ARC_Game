#!/usr/bin/env bash
# One-command CORA benchmark for prompt engineering — for collaborators who want to A/B a
# prompt without touching Python. Edit a JSON prompt pack in prompts/, then:
#
#   ./run_benchmark.sh <prompt-pack> <model> [episodes] [-- extra benchmark_models.py args]
#
# Examples
#   ./run_benchmark.sh cmd_minimal gpt-5-mini 5
#   ./run_benchmark.sh my_prompt   gpt-5-mini 5 -- --base-url http://localhost:8080/v1 --api-key x
#   ./run_benchmark.sh cmd_minimal_flagship_v0 gpt-5.5 10        # reproduce the flagship prompt
#
# Outputs (under bench_packs/<pack>__<model>/):
#   episodes.jsonl   full per-round transcripts (obs, raw model text, reasoning, parsed actions)
#   summary.json     aggregate scores + mistake profile
#   components.*     normalized per-mechanic graphs (if analyze script present)
#
# The harness spawns its own headless CORA game per episode, so you only need the local build
# (Build/Headless/<platform>/...) and an OpenAI-compatible model endpoint (--base-url for a
# local/self-hosted model; default is the CMU gateway).
set -euo pipefail
cd "$(dirname "$0")"

PACK="${1:?usage: ./run_benchmark.sh <prompt-pack> <model> [episodes] [-- extra args]}"
MODEL="${2:?pass a model id as arg2 (e.g. gpt-5-mini, or a full local model path)}"
EPISODES="${3:-5}"
shift $(( $# < 3 ? $# : 3 ))
# allow a leading `--` before passthrough args
[ "${1:-}" = "--" ] && shift || true
EXTRA=("$@")

PY="${ARC_PY:-.venv/bin/python}"
[ -x "$PY" ] || PY="python3"

SAFE_MODEL="$(printf '%s' "$MODEL" | tr '/:' '__')"
OUT="bench_packs/${PACK}__${SAFE_MODEL}"

echo "=== CORA prompt-pack benchmark ==="
echo "  pack:     $PACK"
echo "  model:    $MODEL"
echo "  episodes: $EPISODES (32 rounds each)"
echo "  out:      $OUT"
echo "  extra:    ${EXTRA[*]:-(none)}"
echo

# task_only + K=1 matches the flagship k1_auto protocol; the pack drives format/variant.
"$PY" benchmark_models.py \
  --prompt-pack "$PACK" \
  --models "$MODEL" \
  --episodes "$EPISODES" \
  --rounds 32 \
  --transfers task_only \
  --history 1 \
  --reasoning_effort low \
  --out "$OUT" \
  "${EXTRA[@]}"

echo
echo "Transcripts: $OUT/episodes.jsonl"
echo "Scores:      $OUT/summary.json"
echo "Inspect one prompt exactly as sent:  $PY prompt_packs.py $PACK"
