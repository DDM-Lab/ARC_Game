#!/usr/bin/env bash
# Launch the ARC agent router with the CMU gateway key loaded from .env.
# The router reads OPENAI_API_KEY from its environment (no load_dotenv),
# so we must export it here before exec.
set -euo pipefail
cd /home/conner/arc-game/ARC_Game_New

set -a
. ./.env
set +a

# Sanity: refuse to start keyless (that silently produces 0-token stub turns).
if [ -z "${OPENAI_API_KEY:-}" ]; then
  echo "[launch_router] ERROR: OPENAI_API_KEY empty after sourcing .env" >&2
  exit 1
fi
echo "[launch_router] OPENAI_API_KEY loaded (len=${#OPENAI_API_KEY})"

# The gateway is reached directly; proxies would break it.
exec env -u ALL_PROXY -u all_proxy -u HTTPS_PROXY -u https_proxy \
         -u HTTP_PROXY -u http_proxy \
  .venv/bin/python agent_router.py \
    --port 9876 \
    --config-dir config \
    --log-dir logs/sessions \
    --keys-file config/keys.json \
    --cors-origins '*'
