#!/usr/bin/env bash
#
# Verify a CORA deployment from OUTSIDE the box — run this from your laptop.
#
#   ./verify_talos.sh                                   # public host, no key
#   CORA_KEY=<key> ./verify_talos.sh                    # include authed checks
#   ./verify_talos.sh http://localhost:9876             # verify a local router
#
# Exit 0 = every required check passed. Written to be run BEFORE and AFTER a deploy so the
# delta is obvious: the pre-deploy run should fail exactly the checks the deploy is meant to fix.
set -uo pipefail

HOST="${1:-https://cora_game_llm.dev.ddmlab.com}"
KEY="${CORA_KEY:-}"
PASS=0; FAIL=0; WARN=0

ok()   { printf '  \033[32mPASS\033[0m  %s\n' "$1"; PASS=$((PASS+1)); }
bad()  { printf '  \033[31mFAIL\033[0m  %s\n' "$1"; FAIL=$((FAIL+1)); }
warn() { printf '  \033[33mWARN\033[0m  %s\n' "$1"; WARN=$((WARN+1)); }

code() { curl -s -m 15 -o /dev/null -w '%{http_code}' "$@" 2>/dev/null; }
auth=( -H "Authorization: Bearer ${KEY}" )

echo "════ CORA deployment check ════"
echo "  host: $HOST"
[ -z "$KEY" ] && echo "  key : (none — set CORA_KEY for authed checks)" || echo "  key : ${KEY:0:8}…"
echo

echo "── liveness ──"
c=$(code "$HOST/health")
[ "$c" = "200" ] && ok "/health -> 200" || bad "/health -> ${c:-unreachable}"
if [ "$c" = "200" ]; then
  curl -s -m 15 "$HOST/health" | sed 's/^/        /'
fi
echo

echo "── routes that MUST be proxied ──"
# Unauthenticated these should be 401 (route exists, key required) — NOT 404 (not proxied).
for p in /configs /whoami /my/sessions "/my/sessions/export?format=tar"; do
  c=$(code "$HOST$p")
  case "$c" in
    401|403) ok "$p -> $c (route present, auth enforced)" ;;
    200)     ok "$p -> 200" ;;
    404)     bad "$p -> 404 — NOT PROXIED (add ProxyPass to the vhost)" ;;
    *)       bad "$p -> ${c:-unreachable}" ;;
  esac
done
echo

echo "── admin plane must NOT be public ──"
for p in /admin/keys /admin/plugins/reload; do
  c=$(code "$HOST$p")
  [ "$c" = "404" ] && ok "$p -> 404 (correctly unreachable)" \
                   || bad "$p -> $c — ADMIN IS EXPOSED, fix the vhost immediately"
done
echo

if [ -n "$KEY" ]; then
  echo "── authenticated ──"
  c=$(code "${auth[@]}" "$HOST/configs")
  [ "$c" = "200" ] && ok "/configs with key -> 200" || bad "/configs with key -> $c"

  c=$(code "${auth[@]}" "$HOST/whoami")
  if [ "$c" = "200" ]; then
    ok "/whoami with key -> 200"
    curl -s -m 15 "${auth[@]}" "$HOST/whoami" | sed 's/^/        /'
  else
    bad "/whoami with key -> $c"
  fi

  c=$(code "${auth[@]}" "$HOST/my/sessions")
  [ "$c" = "200" ] && ok "/my/sessions with key -> 200" || bad "/my/sessions with key -> $c"
  echo
fi

echo "── new-code markers (are today's changes actually running?) ──"
# /whoami only exists in the new build, so its presence dates the deployment.
c=$(code "$HOST/whoami")
[ "$c" = "404" ] && warn "/whoami absent — router predates the contributor-platform work" \
                 || ok "/whoami present — router carries the new code"
echo

echo "════ $PASS passed, $FAIL failed, $WARN warning(s) ════"
[ "$FAIL" -eq 0 ] || exit 1
