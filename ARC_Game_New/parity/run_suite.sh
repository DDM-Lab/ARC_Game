#!/bin/zsh
# Run the same seeded, PLAYED episode on two builds across many seeds, then summarise.
#
#   parity/run_suite.sh <baseline.app> <candidate.app> [seeds] [rounds] [outdir]
#
# Why many seeds. A single seed is one draw from the space of episodes: parity on it is a data
# point, not a property. Divergences in this game are also seed-selective — ledger D19 only
# surfaced on day 3, and only because one task type happened to become eligible. A suite says
# how OFTEN and how FAR two builds disagree, which is the question worth answering.
#
# Why played episodes. A clock-only episode never builds, deconstructs or confirms, so the paths
# where the branches actually differ are unreachable (D1 needs a deconstruction, D5 a confirm).
# -parity-play lets the ported greedy baseline act each round.
set -e
BASE_APP="$1"; CAND_APP="$2"; SEEDS="${3:-32}"; ROUNDS="${4:-24}"
OUT="${5:-$TMPDIR/parity_suite}"
DATA="$HOME/Library/Application Support/com.DefaultCompany.ARC-DisasterSimulation"
mkdir -p "$OUT"

run() {   # run <app> <tag> <seed>
  local app="$1" tag="$2" seed="$3"
  local bin="$app/Contents/MacOS/$(ls "$app/Contents/MacOS" | head -1)"
  local cfg="$app/Contents/Resources/Data/StreamingAssets/config.json"
  [[ -f "$cfg" ]] && python3 - "$cfg" <<'PY'
import json,sys
p=sys.argv[1]; c=json.load(open(p))
c["mapConfigUrl"]="http://127.0.0.1:9/config"; c["logServerUrl"]=""; c["dataCollectionEnabled"]=False
json.dump(c,open(p,"w"),indent=2)
PY
  "$bin" -batchmode -nographics -logFile "$OUT/log_${tag}_${seed}.txt" \
     -no-llm -parity-hermetic -parity-play \
     -seed "$seed" -parity-probe -parity-drive "$ROUNDS" >/dev/null 2>&1 || true
  cp "$(ls -t "$DATA"/parity_trace_*.jsonl | head -1)" "$OUT/${tag}_${seed}.jsonl"
}

for seed in $(seq 1 $SEEDS); do
  print -n "seed $seed "
  run "$BASE_APP" baseline "$seed"
  run "$CAND_APP" candidate "$seed"
  print "done"
done

python3 "$(dirname "$0")/summarize_suite.py" "$OUT" "$SEEDS"
