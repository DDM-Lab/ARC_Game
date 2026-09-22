#!/usr/bin/env bash
# Hot-reload plugins/ on a running router (no restart). Needs a key with the 'upload_code' cap.
#   scripts/reload_plugins.sh
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"; . "$SCRIPT_DIR/env.sh"; cd "$REPO"
cora_curl -X POST "$CORA_URL/admin/plugins/reload" -H "Authorization: Bearer $CORA_KEY" | cora_py -m json.tool
