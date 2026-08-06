#!/usr/bin/env bash
# View or download your own session data (scoped to your key's cohort/label).
#
#   scripts/get_data.sh                 # list your sessions
#   scripts/get_data.sh <session_id>    # download that session's log to <session_id>.jsonl
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"; . "$SCRIPT_DIR/env.sh"; cd "$REPO"

if [ -z "${1:-}" ]; then
  cora_curl "$CORA_URL/my/sessions" -H "Authorization: Bearer $CORA_KEY" | cora_py -m json.tool
else
  OUT="$1.jsonl"
  cora_curl "$CORA_URL/my/sessions/$1" -H "Authorization: Bearer $CORA_KEY" -o "$OUT"
  echo "wrote $OUT"
fi
