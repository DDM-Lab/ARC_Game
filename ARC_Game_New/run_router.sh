#!/usr/bin/env bash
#
# Launch the ARC Game multi-tenant agent router (for Janus or any host).
#
# Usage:
#   ./run_router.sh                 # uses defaults below
#   PORT=9876 ./run_router.sh       # override via env
#
# Auth: provide real keys in config/keys.json (gitignored). Copy
# config/keys.example.json to get started. If keys.json is absent and
# ARC_API_KEYS is unset, the router falls back to a single 'dev-local-key'
# (fine for local testing, NOT for a public endpoint).
#
# LLM provider keys (ANTHROPIC_API_KEY / OPENAI_API_KEY / the CMU gateway key)
# are read from .env in this directory by llm_query.py — keep that out of git.
#
set -euo pipefail

cd "$(dirname "$0")"

PORT="${PORT:-9876}"
CONFIG_DIR="${CONFIG_DIR:-config}"
LOG_DIR="${LOG_DIR:-logs/sessions}"
PYTHON="${PYTHON:-./.venv/bin/python}"

KEYS_ARG=()
if [[ -f "${CONFIG_DIR}/keys.json" ]]; then
  KEYS_ARG=(--keys-file "${CONFIG_DIR}/keys.json")
  echo "[run_router] Using keys file: ${CONFIG_DIR}/keys.json"
elif [[ -n "${ARC_API_KEYS:-}" ]]; then
  echo "[run_router] Using ARC_API_KEYS from environment"
else
  echo "[run_router] WARNING: no keys.json and no ARC_API_KEYS — falling back to 'dev-local-key'."
  echo "[run_router]          Do NOT expose this to untrusted clients."
fi

echo "[run_router] Port=${PORT} ConfigDir=${CONFIG_DIR} LogDir=${LOG_DIR}"
exec "${PYTHON}" agent_router.py \
  --port "${PORT}" \
  --config-dir "${CONFIG_DIR}" \
  --log-dir "${LOG_DIR}" \
  "${KEYS_ARG[@]+"${KEYS_ARG[@]}"}"
