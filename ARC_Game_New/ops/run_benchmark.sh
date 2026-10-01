#!/usr/bin/env bash
# One-command CORA benchmark for prompt engineering — for collaborators who want to A/B a
# prompt without touching Python. Edit a JSON prompt pack in prompts/, then:
#
#   ops/run_benchmark.sh <prompt-pack> <model> [episodes] [-- extra python -m bench args]
#
# Examples
#   ops/run_benchmark.sh minimal_v6_1 gpt-5-mini 5
#   ops/run_benchmark.sh my_prompt    gpt-5-mini 5 -- --base-url http://localhost:8080/v1 --api-key x
#   ops/run_benchmark.sh minimal_v6   gpt-5.5 10                  # reproduce the Sep 2026 prompt
#
# Outputs (under bench_packs/<pack>__<model>/):
#   episodes.jsonl   full per-decision transcripts (obs, model text, reasoning, each tool call's outcome)
#   summary.json     aggregate scores + mistake profile
#
# The harness spawns its own headless CORA game per episode, so you only need the local build
# (Build/Headless/<platform>/...) and an OpenAI-compatible model endpoint (--base-url for a
# local/self-hosted model; default is the CMU gateway).
set -euo pipefail
cd "$(dirname "$0")/.."

PACK="${1:?usage: ops/run_benchmark.sh <prompt-pack> <model> [episodes] [-- extra args]}"
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
echo "  episodes: $EPISODES (full games: 36 decisions each)"
echo "  out:      $OUT"
echo "  extra:    ${EXTRA[*]:-(none)}"
echo

# The benchmark defaults are the cluster protocol: task-only transfers, compact observation,
# no history, a full game per episode.
"$PY" -m bench \
  --prompt "$PACK" \
  --models "$MODEL" \
  --episodes "$EPISODES" \
  --out "$OUT" \
  "${EXTRA[@]}"

echo
echo "Transcripts: $OUT/episodes.jsonl"
echo "Scores:      $OUT/summary.json"
echo "Inspect the prompt exactly as sent:  $PY -m cora.prompts $PACK"
