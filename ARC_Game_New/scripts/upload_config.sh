#!/usr/bin/env bash
# Upload a config bundle. If a companion plugin accompanies it, validate that too and warn about
# any tools the config references that no loaded plugin provides.
#
#   scripts/upload_config.sh <bundle.json> [companion_plugin.py]
#
# Auto-detects a companion plugin at "<bundle-basename>.py" if you don't pass one.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"; . "$SCRIPT_DIR/env.sh"

BUNDLE="${1:-}"; PLUGIN="${2:-}"
if [ -z "$BUNDLE" ]; then
  echo "usage: $0 <bundle.json> [companion_plugin.py]" >&2; exit 2
fi
BUNDLE="$(abspath "$BUNDLE")"
if [ -z "$PLUGIN" ] && [ -f "${BUNDLE%.json}.py" ]; then PLUGIN="${BUNDLE%.json}.py"; fi
[ -n "$PLUGIN" ] && PLUGIN="$(abspath "$PLUGIN")"
cd "$REPO"

echo "==> [1/4] validate bundle"
cora_py cora_bundle.py validate "$BUNDLE"

if [ -n "$PLUGIN" ]; then
  echo "==> [2/4] companion plugin: $PLUGIN"
  cora_py cora_plugin.py check "$PLUGIN"
  if [ ! -f "$REPO/plugins/$(basename "$PLUGIN")" ]; then
    echo "    NOTE: to activate this plugin, copy it into plugins/ and restart the router:"
    echo "          cp '$PLUGIN' '$REPO/plugins/' && <restart router>"
  fi
else
  echo "==> [2/4] no companion plugin (config uses built-in tools only, or a pre-installed plugin)"
fi

echo "==> [3/4] check tool references have a provider"
cora_py - "$BUNDLE" <<'PYEOF'
import json, sys, cora_ext
from continuous_agent import DEFAULT_TOOLS
cora_ext.load_plugins(["plugins"])
builtins, plugins = set(DEFAULT_TOOLS), set(cora_ext.all_tools())
cfg = (json.load(open(sys.argv[1])).get("config") or {})
missing = {t for a in cfg.get("agents", []) for t in (a.get("tools") or [])
           if t not in builtins and t not in plugins}
if missing:
    print(f"    WARNING: no provider for tools {sorted(missing)} — install the plugin in plugins/")
    print(f"    (loaded plugin tools: {sorted(plugins) or 'none'})")
else:
    print("    OK: every referenced tool is built-in or provided by a loaded plugin")
PYEOF

echo "==> [4/4] push"
cora_py cora_bundle.py push "$BUNDLE" --url "$CORA_URL" --key "$CORA_KEY"
