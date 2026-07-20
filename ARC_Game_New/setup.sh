#!/usr/bin/env bash
# ARC Game — LLM Router setup (macOS / Linux)
# Idempotent: safe to re-run. Never overwrites an existing real key in .env.
#
#   ./setup.sh            # interactive: prompts for provider keys (hidden input)
#   ./setup.sh --no-keys  # skip key prompts, just env + deps (writes placeholders)
#
# Keys can also be supplied non-interactively by exporting them first, e.g.:
#   OPENAI_API_KEY=sk-... ANTHROPIC_API_KEY=sk-... ./setup.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

PROMPT_KEYS=1
[ "${1:-}" = "--no-keys" ] && PROMPT_KEYS=0

echo "==> ARC Game router setup ($(uname -s))"

# ---------------------------------------------------------------------------
# 1. Find a Python >= 3.9
# ---------------------------------------------------------------------------
PY=""
for c in python3.12 python3.11 python3.10 python3.9 python3; do
  if command -v "$c" >/dev/null 2>&1; then
    ver="$("$c" -c 'import sys;print("%d.%d"%sys.version_info[:2])' 2>/dev/null || echo 0.0)"
    maj="${ver%%.*}"; min="${ver##*.}"
    if [ "${maj:-0}" -eq 3 ] && [ "${min:-0}" -ge 9 ]; then PY="$c"; break; fi
  fi
done
if [ -z "$PY" ]; then
  echo "ERROR: need Python 3.9+ on PATH (tried python3.12/3.11/3.10/3.9/python3)." >&2
  exit 1
fi
echo "==> Using $PY ($($PY --version 2>&1))"

# ---------------------------------------------------------------------------
# 2. Virtualenv
# ---------------------------------------------------------------------------
if [ ! -d .venv ]; then
  echo "==> Creating virtualenv .venv"
  "$PY" -m venv .venv
else
  echo "==> .venv already exists — reusing"
fi
VENV_PY=".venv/bin/python"

# ---------------------------------------------------------------------------
# 3. Dependencies
# ---------------------------------------------------------------------------
echo "==> Installing dependencies (requirements.txt)"
"$VENV_PY" -m pip install --upgrade pip >/dev/null
"$VENV_PY" -m pip install -r requirements.txt

# ---------------------------------------------------------------------------
# 4. Provider keys -> .env  (loaded by the router via python-dotenv)
# ---------------------------------------------------------------------------
ENV_FILE=".env"
touch "$ENV_FILE"

# True if KEY is present in .env with a real (non-placeholder) value.
env_has_real_value() {
  local key="$1" line v
  line="$(grep -E "^[[:space:]]*${key}=" "$ENV_FILE" 2>/dev/null | tail -1 || true)"
  [ -n "$line" ] || return 1
  v="${line#*=}"
  case "$v" in ""|PASTE_*|"<"*) return 1;; esac
  return 0
}

# Replace/insert a KEY=VALUE line, preserving everything else.
upsert_env() {
  local key="$1" val="$2" tmp
  tmp="$(mktemp)"
  grep -v -E "^[[:space:]]*${key}=" "$ENV_FILE" > "$tmp" 2>/dev/null || true
  mv "$tmp" "$ENV_FILE"
  printf '%s=%s\n' "$key" "$val" >> "$ENV_FILE"
}

# prompt_key KEY required|optional "human hint"
prompt_key() {
  local key="$1" need="$2" hint="$3" envval val
  # (a) already configured in .env -> keep
  if env_has_real_value "$key"; then
    echo "==> $key already set in .env — keeping it"
    return
  fi
  # (b) exported in the current shell -> use it
  envval="$(eval "printf '%s' \"\${$key:-}\"")"
  if [ -n "$envval" ]; then
    upsert_env "$key" "$envval"
    echo "==> $key taken from environment and saved to .env"
    return
  fi
  # (c) interactive prompt (hidden), only if we have a TTY and keys weren't disabled
  if [ "$PROMPT_KEYS" -eq 1 ] && [ -t 0 ]; then
    echo ""
    echo "   $key — $hint"
    if [ "$need" = "required" ]; then
      printf "   enter value (required): "
    else
      printf "   enter value (optional, press Enter to skip): "
    fi
    read -r -s val; echo
    if [ -n "$val" ]; then
      upsert_env "$key" "$val"
      echo "   ✓ $key saved to .env"
      return
    fi
  fi
  # (d) fallback: placeholder for required, skip for optional
  if [ "$need" = "required" ]; then
    env_has_real_value "$key" || upsert_env "$key" "PASTE_YOUR_${key}_HERE"
    echo "==> $key not provided — wrote placeholder to .env (edit before running)"
  else
    echo "==> $key skipped (optional)"
  fi
}

echo "==> Configuring provider keys in .env"
prompt_key OPENAI_API_KEY    required "CMU AI-gateway key (used by all 'openai' provider configs -> https://ai-gateway.andrew.cmu.edu/v1)"
prompt_key ANTHROPIC_API_KEY optional "only needed if you run an 'anthropic' provider config"
echo "   (ollama-provider configs need no key — they use a local Ollama server)"

# ---------------------------------------------------------------------------
# 5. config/keys.json  (router <-> client auth) — copy from example if missing
# ---------------------------------------------------------------------------
if [ ! -f config/keys.json ]; then
  if [ -f config/keys.example.json ]; then
    echo "==> Creating config/keys.json from config/keys.example.json"
    cp config/keys.example.json config/keys.json
  else
    echo "WARNING: config/keys.example.json missing — create config/keys.json manually." >&2
  fi
else
  echo "==> config/keys.json already exists — leaving it untouched"
fi

# ---------------------------------------------------------------------------
# 6. Git LFS assets (needed only to build the Unity client)
# ---------------------------------------------------------------------------
if command -v git >/dev/null 2>&1 && git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  if command -v git-lfs >/dev/null 2>&1; then
    echo "==> git lfs pull"
    git lfs pull || echo "   (git lfs pull failed — fine if you only run the router)"
  else
    echo "==> git-lfs not installed (skipping; needed only to build the Unity client)"
  fi
fi

cat <<'EOF'

✅ Setup complete.

Start the router:

   env -u ALL_PROXY -u all_proxy -u HTTPS_PROXY -u https_proxy -u HTTP_PROXY -u http_proxy \
     ./.venv/bin/python agent_router.py --port 9876 --config-dir config \
     --log-dir logs/sessions --keys-file config/keys.json

(If .env still shows PASTE_YOUR_..., edit it and put your real key in first.)
EOF
