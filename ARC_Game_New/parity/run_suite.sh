#!/bin/zsh
# Run the same seeded, PLAYED episode on two builds across many seeds, then summarise.
#
#   parity/run_suite.sh <baseline.app> <candidate.app> [seeds] [rounds] [outdir] [workers]
#
# Why many seeds. A single seed is one draw from the space of episodes: parity on it is a data
# point, not a property. Divergences here are also seed-selective — ledger D19 only surfaced on
# day 3, and only because one task type happened to become eligible.
#
# Why played episodes. A clock-only episode never builds, deconstructs or confirms, so the paths
# where the branches actually differ are unreachable (D1 needs a deconstruction, D5 a confirm).
#
# PARALLELISM. Each run is given its own trace path via ARC_PARITY_TRACE, so runs no longer
# race over "newest file in persistentDataPath" — that shared-directory pick was the only thing
# forcing them to be sequential. Default is half the cores: these are headless Unity players and
# oversubscribing makes each round slower, which matters because ParityDriver aborts a run whose
# round takes more than 120s. If runs start failing with "stuck waiting to advance", lower it.
#
# Determinism is unaffected by parallelism: the driver advances on STATE (idle + button
# interactable), never on a timer, and the policy draws only from the episode seed.
set -e
BASE_APP="$1"; CAND_APP="$2"; SEEDS="${3:-32}"; ROUNDS="${4:-24}"
OUT="${5:-$TMPDIR/parity_suite}"
WORKERS="${6:-$(( $(sysctl -n hw.ncpu 2>/dev/null || echo 8) / 2 ))}"
[[ $WORKERS -lt 1 ]] && WORKERS=1
mkdir -p "$OUT"

# Pin the experiment once, up front, rather than per-run: blanking mapConfigUrl in the built
# StreamingAssets/config.json stops either build downloading a map (ledger D9). Doing it here
# also keeps parallel workers from writing the same file at the same time.
for app in "$BASE_APP" "$CAND_APP"; do
  cfg="$app/Contents/Resources/Data/StreamingAssets/config.json"
  [[ -f "$cfg" ]] && python3 - "$cfg" <<'PY'
import json,sys
p=sys.argv[1]; c=json.load(open(p))
c["mapConfigUrl"]="http://127.0.0.1:9/config"; c["logServerUrl"]=""; c["dataCollectionEnabled"]=False
json.dump(c,open(p,"w"),indent=2)
PY
done

run_one() {          # run_one <app> <tag> <seed>
  local app="$1" tag="$2" seed="$3"
  local bin="$app/Contents/MacOS/$(ls "$app/Contents/MacOS" | head -1)"
  ARC_PARITY_TRACE="$OUT/${tag}_${seed}.jsonl" \
  "$bin" -batchmode -nographics -logFile "$OUT/log_${tag}_${seed}.txt" \
     -no-llm -parity-hermetic -parity-play \
     -seed "$seed" -parity-probe -parity-drive "$ROUNDS" >/dev/null 2>&1 || true
}

export OUT ROUNDS BASE_APP CAND_APP
print "running $SEEDS seeds x 2 builds across $WORKERS worker(s)"

# One job per (build, seed). xargs -P bounds concurrency; each job is independent because each
# writes to its own trace and log path.
for seed in $(seq 1 $SEEDS); do print "baseline $seed"; print "candidate $seed"; done \
| xargs -P "$WORKERS" -n 2 zsh -c '
    tag=$0; seed=$1
    app=$([[ $tag == baseline ]] && print -r -- "$BASE_APP" || print -r -- "$CAND_APP")
    bin="$app/Contents/MacOS/$(ls "$app/Contents/MacOS" | head -1)"
    ARC_PARITY_TRACE="$OUT/${tag}_${seed}.jsonl" \
    "$bin" -batchmode -nographics -logFile "$OUT/log_${tag}_${seed}.txt" \
       -no-llm -parity-hermetic -parity-play \
       -seed "$seed" -parity-probe -parity-drive "$ROUNDS" >/dev/null 2>&1 || true
    print "  done $tag $seed"
  '

python3 "$(dirname "$0")/summarize_suite.py" "$OUT" "$SEEDS"
