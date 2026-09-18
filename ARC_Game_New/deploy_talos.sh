#!/usr/bin/env bash
#
# Deploy CORA on the Talos box. RUN THIS ON THE SERVER, not from a laptop.
#
#   ./deploy_talos.sh              # pull, restart router, verify
#   ./deploy_talos.sh --no-pull    # restart only (code already in place)
#   ./deploy_talos.sh --dry-run    # print what it would do and exit
#
# What it does NOT do: touch Apache. The vhost needs root and a human eye, so the required
# snippet is printed at the end and you apply it yourself. Nothing here is destructive except
# restarting the router, and it refuses to do that while a game is in progress.
set -uo pipefail
cd "$(dirname "$0")"

BRANCH="${BRANCH:-feature/contributor-platform}"
PORT="${PORT:-9876}"
ADMIN_PORT="${ADMIN_PORT:-9877}"
PY="${PY:-./.venv/bin/python}"
LOG_DIR="${LOG_DIR:-logs/sessions}"
DRY=0; PULL=1
for a in "$@"; do
  case "$a" in
    --dry-run) DRY=1 ;;
    --no-pull) PULL=0 ;;
    *) echo "unknown flag: $a"; exit 2 ;;
  esac
done

say() { printf '\n\033[1m== %s\033[0m\n' "$1"; }
run() { if [ "$DRY" = 1 ]; then echo "  [dry-run] $*"; else eval "$@"; fi; }

say "1. pre-flight"
[ -x "$PY" ] || { echo "  !! no venv at $PY — create it first"; exit 1; }
echo "  python : $($PY -V 2>&1)"
echo "  branch : $(git rev-parse --abbrev-ref HEAD 2>/dev/null)"

# Refuse to restart mid-game: a live session dies with the process and the participant
# loses their run. Better to wait than to silently destroy someone's data.
live=$(curl -s -m 5 "http://127.0.0.1:$PORT/health" 2>/dev/null \
       | sed -n 's/.*"live_sessions":\([0-9]*\).*/\1/p')
if [ -n "${live:-}" ] && [ "$live" != "0" ]; then
  echo "  !! $live LIVE SESSION(S) in progress — refusing to restart."
  echo "     Wait for them to finish, or set FORCE=1 to override."
  [ "${FORCE:-0}" = "1" ] || exit 1
  echo "     FORCE=1 set — proceeding anyway."
else
  echo "  live sessions: ${live:-unknown} (safe to restart)"
fi

say "2. back up state that must survive"
STAMP=$(date +%Y%m%d-%H%M%S)
BK="backups/deploy-$STAMP"
run "mkdir -p '$BK'"
for f in config/keys.json data/keys.db data/plugin_store.db; do
  [ -e "$f" ] && run "cp -a '$f' '$BK/' && echo '  saved $f'"
done
run "echo '  backup -> $BK'"

if [ "$PULL" = 1 ]; then
  say "3. update code"
  # Stash rather than discard: uncommitted changes on a shared box are usually someone's
  # in-flight debugging, and silently blowing them away is unrecoverable.
  if [ -n "$(git status --porcelain 2>/dev/null)" ]; then
    run "git stash push -u -m 'deploy-$STAMP' && echo '  stashed local changes (git stash list)'"
  fi
  run "git fetch origin '$BRANCH'"
  run "git checkout '$BRANCH'"
  run "git pull --ff-only origin '$BRANCH'"
  run "$PY -m pip install -q -r requirements.txt 2>/dev/null || true"
else
  say "3. update code (skipped: --no-pull)"
fi

say "4. sanity-check the new code BEFORE swapping the process"
run "$PY -c 'import agent_router, cora_tools, bundle, agent_config; print(\"  imports OK\")'"
for t in test_tag_translation.py test_cost_attribution.py; do
  [ -f "$t" ] && run "$PY '$t' >/dev/null && echo '  $t OK'"
done

say "5. restart the router"
run "pkill -f 'agent_router.py --port $PORT' || true"
run "sleep 2"
run "ARC_LOG_PROMPTS=\${ARC_LOG_PROMPTS:-0} nohup $PY -u agent_router.py \
     --config-dir config --keys-file config/keys.json \
     --port $PORT --admin-port $ADMIN_PORT --log-dir '$LOG_DIR' \
     > logs/router-$STAMP.log 2>&1 &"
run "sleep 6"

say "6. verify"
if [ "$DRY" = 0 ]; then
  for i in $(seq 1 15); do
    curl -s -m 3 "http://127.0.0.1:$PORT/health" >/dev/null 2>&1 && break
    sleep 1
  done
  echo "  /health      : $(curl -s -m 5 -o /dev/null -w '%{http_code}' http://127.0.0.1:$PORT/health)"
  echo "  /whoami      : $(curl -s -m 5 -o /dev/null -w '%{http_code}' http://127.0.0.1:$PORT/whoami)  (401 = new code present)"
  echo "  admin :$ADMIN_PORT : $(curl -s -m 5 -o /dev/null -w '%{http_code}' http://127.0.0.1:$ADMIN_PORT/admin/keys)  (401 = up, loopback-only)"
  echo "  /admin on :$PORT : $(curl -s -m 5 -o /dev/null -w '%{http_code}' http://127.0.0.1:$PORT/admin/keys)  (404 = correctly split)"
fi

cat <<'APACHE'

== 7. APACHE — apply by hand (needs root) ==

  The vhost currently proxies only /ws /configs /health /bundles.
  /whoami and /my/ return 404 publicly, so `cora.py doctor` and ALL data download are broken.

  Add to the CORA vhost:

    ProxyPass        /whoami   http://127.0.0.1:9876/whoami
    ProxyPassReverse /whoami   http://127.0.0.1:9876/whoami
    ProxyPass        /my/      http://127.0.0.1:9876/my/
    ProxyPassReverse /my/      http://127.0.0.1:9876/my/

    # Only if collaborators upload plugin code directly:
    # ProxyPass        /plugins  http://127.0.0.1:9876/plugins
    # ProxyPassReverse /plugins  http://127.0.0.1:9876/plugins

    # Do NOT proxy /admin/* — it is a separate app on 9877 bound to loopback.
    # Reach it with:  ssh -L 9877:127.0.0.1:9877 <box>

  Then:  sudo apachectl configtest && sudo systemctl reload apache2

  Finally, from your LAPTOP:
    CORA_KEY=<key> ./verify_talos.sh
APACHE
