#!/bin/zsh
# usage: run_parity.sh <app-bundle> <tag> <seed> <rounds>
#
# Pins the EXPERIMENT, not just the build. Two things are forced before the player starts:
#   * mapConfigUrl blanked in the built StreamingAssets/config.json, so neither build downloads
#     a map (ledger D9 — upstream otherwise pulls the live production map over the network and
#     the two players run different geography).
#   * -no-llm, so the AI teammates are off on both sides (ledger D8).
set -e
APP="$1"; TAG="$2"; SEED="$3"; N="$4"
DIR="$HOME/Library/Application Support/com.DefaultCompany.ARC-DisasterSimulation"
BIN="$APP/Contents/MacOS/$(ls "$APP/Contents/MacOS" | head -1)"
CFG="$APP/Contents/Resources/Data/StreamingAssets/config.json"
if [[ -f "$CFG" ]]; then
  python3 - "$CFG" <<'PY'
import json,sys
p=sys.argv[1]; c=json.load(open(p))
# A DEAD URL, not an empty one. Upstream only overrides its scene value when config.json's
# entry is non-empty, so blanking it left the serialized "http://localhost:8765/config" in
# force -- and with a map server running on this machine, upstream silently downloaded a
# different map anyway. An unreachable address is the only value that reliably puts BOTH
# builds on the "could not reach map config server - using default scene layout" path.
c["mapConfigUrl"]="http://127.0.0.1:9/config"; c["logServerUrl"]=""; c["dataCollectionEnabled"]=False
json.dump(c,open(p,"w"),indent=2)
PY
fi
LOG="/tmp/claude-501/log_${TAG}.txt"
"$BIN" -batchmode -nographics -logFile "$LOG" -no-llm -parity-hermetic \
  -seed "$SEED" -parity-probe -parity-drive "$N" >/dev/null 2>&1 || true
NEW=$(ls -t "$DIR"/parity_trace_*.jsonl | head -1)
cp "$NEW" "/tmp/claude-501/trace_${TAG}.jsonl"
echo "$TAG: lines=$(wc -l < "/tmp/claude-501/trace_${TAG}.jsonl")  log=$LOG"
grep -E "ParityDriver\]|AI teammates|map config|Hardcoded Defaults|parameters in effect|gym server|Registered .* abandoned" "$LOG" | head -20
