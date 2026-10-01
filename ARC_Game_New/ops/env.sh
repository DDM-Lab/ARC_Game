#!/usr/bin/env bash
# Shared config for the CORA collaborator scripts. Source it (the scripts do this for you).
# Override the router/key from your shell:  export CORA_URL=…  CORA_KEY=…
export CORA_URL="${CORA_URL:-http://localhost:9876}"
export CORA_KEY="${CORA_KEY:-dev-local-key}"

# repo root = parent of this script's dir
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export REPO
PYBIN="$REPO/.venv/bin/python"

# project python with proxy vars cleared (CMU gateway proxy must not touch localhost/LLM calls)
cora_py() { env -u ALL_PROXY -u HTTP_PROXY -u HTTPS_PROXY -u http_proxy -u https_proxy "$PYBIN" "$@"; }
# curl that bypasses any http(s)_proxy for localhost
cora_curl() { curl -s --noproxy '*' "$@"; }

abspath() { case "$1" in /*) printf '%s\n' "$1";; *) printf '%s\n' "$(pwd)/$1";; esac; }
