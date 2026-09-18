#!/usr/bin/env bash
# Admin key management (needs a key with the 'mint' capability, e.g. the dev key locally).
#
#   scripts/keys.sh mint <cohort> <config1,config2> [count] [quota] [expires_days]
#   scripts/keys.sh list [cohort]
#   scripts/keys.sh revoke <prefix>
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"; . "$SCRIPT_DIR/env.sh"; cd "$REPO"

cmd="${1:-}"; shift || true
case "$cmd" in
  mint)
    COHORT="${1:?cohort required}"; CONFIGS="${2:?configs csv required}"
    COUNT="${3:-5}"; QUOTA="${4:-null}"; EXP="${5:-30}"
    CFG=$(cora_py -c "import json,sys;print(json.dumps(sys.argv[1].split(',')))" "$CONFIGS")
    BODY="{\"cohort\":\"$COHORT\",\"configs\":$CFG,\"count\":$COUNT,\"quota\":$QUOTA,\"expires_days\":$EXP}"
    cora_curl -X POST "$CORA_URL/admin/keys" -H "Authorization: Bearer $CORA_KEY" \
      -H "Content-Type: application/json" -d "$BODY" | cora_py -m json.tool
    ;;
  list)
    Q=""; [ -n "${1:-}" ] && Q="?cohort=$1"
    cora_curl "$CORA_URL/admin/keys$Q" -H "Authorization: Bearer $CORA_KEY" | cora_py -m json.tool
    ;;
  revoke)
    PREFIX="${1:?prefix required}"
    cora_curl -X POST "$CORA_URL/admin/keys/revoke" -H "Authorization: Bearer $CORA_KEY" \
      -d "{\"prefix\":\"$PREFIX\"}" | cora_py -m json.tool
    ;;
  *)
    echo "usage: $0 {mint <cohort> <configs> [count] [quota] [expires_days] | list [cohort] | revoke <prefix>}" >&2
    exit 2;;
esac
