# CORA — Talos deploy runbook (collaboration platform)

Operational guide for deploying CORA so collaborators can upload configs/plugins, mint their own
participant keys, and pull transcripts for training. Every command below was verified against a
local router before being written down.

Companion docs: `TALOS_AGENT_QUICKSTART.md` (orientation), `contributor-platform-design.md`
(design rationale), `CORA_API_v1.md` (action/observation contract).

---

## 1. ⚠️ Apache proxy gap — read this first

The vhost currently proxies only `/ws`, `/configs`, `/health`, `/bundles` (+ `Alias /sheet.csv`).
Before deploying, decide what is publicly reachable.

**The admin routes now live on a SEPARATE app and port** (`--admin-port`, default **9877**,
bound to `127.0.0.1`). They are no longer served by the data-plane app at all, so they cannot be
exposed by a proxy misconfiguration — verified: `GET :9876/admin/keys` → **404**, while
`GET :9877/admin/keys` → 401/200. This changes the SSH tunnel port below; tunnelling 9876 no
longer reaches `/admin/*`.

| Endpoint | Purpose | Expose publicly? |
|---|---|---|
| `GET /health` | liveness | ✅ already |
| `GET /configs` | config catalog (key-scoped) | ✅ already |
| `POST /bundles` | config upload | ✅ already |
| `WS /ws` | gameplay | ✅ already |
| `GET /whoami` | key label + capabilities (`cora.py doctor`) | ✅ **add** — self-diagnosis |
| `GET /my/sessions` | list own sessions | ✅ **add** — collaborators need it |
| `GET /my/sessions/export` | bulk corpus download | ✅ **add** — the SFT path |
| `GET /my/sessions/{id}` | single transcript | ✅ **add** |
| `POST /plugins` | stage plugin **code** | ⚠️ **your call** (see §5) |
| `GET /plugins` | list staged/active | ⚠️ with the above |
| `POST /admin/keys` | mint keys | 🔒 separate port 9877, loopback |
| `GET /admin/keys` | list keys/usage | 🔒 separate port 9877, loopback |
| `POST /admin/keys/revoke` | revoke | 🔒 separate port 9877, loopback |
| `POST /admin/plugins/reload` | **activates uploaded code** | 🔒 separate port 9877, loopback |
| `GET /admin/plugins/errors` | plugin diagnostics | 🔒 separate port 9877, loopback |

**Recommended posture:** proxy the participant/collaborator surface; reach `/admin/*` over an SSH
tunnel so key minting and code activation are never internet-facing.

```apache
# add to the CORA vhost (alongside the existing /ws, /configs, /health, /bundles)
ProxyPass        /my/      http://127.0.0.1:9876/my/
ProxyPassReverse /my/      http://127.0.0.1:9876/my/
ProxyPass        /whoami   http://127.0.0.1:9876/whoami
ProxyPassReverse /whoami   http://127.0.0.1:9876/whoami

# Only if collaborators upload plugin code directly (see §5); otherwise omit.
ProxyPass        /plugins  http://127.0.0.1:9876/plugins
ProxyPassReverse /plugins  http://127.0.0.1:9876/plugins

# /admin/* is not on this port at all — it is a separate app on 9877 (loopback).
# Reach it with:  ssh -L 9877:127.0.0.1:9877 talos
```

Verify after reload, from your laptop:
```bash
export CORA_URL=https://<host> CORA_KEY=<key>
python cora.py doctor          # ✓ reachable, ✓ key valid, capabilities listed
curl -s -o /dev/null -w '%{http_code}\n' "$CORA_URL/admin/keys"   # expect 404
```
`doctor` exercises `/health`, `/whoami` and `/configs` in one shot, so a missing `ProxyPass`
shows up immediately instead of during someone's first upload.

---

## 2. Key model

Three tiers, all hashed at rest; raw keys are shown **once** at mint time.

- **Admin** (root, from `config/keys.json`) — caps `mint`, `upload_code`.
- **Collaboration key** — a cohort key you mint *with* `mint` (+ optionally `upload_code`) so a
  collaborator can run their own study without you in the loop.
- **Participant key** — minted by a collaborator, no caps, scoped to specific configs + a quota.

Capability boundaries are enforced, not advisory — a participant attempting `POST /plugins`
gets **403** (verified).

---

## 3. Verified collaboration workflow

```bash
B=https://cora_game_llm.dev.ddmlab.com      # locally: http://localhost:9876
ADMIN=<root admin key>

# (1) YOU mint a collaboration key (via the SSH tunnel — /admin is not public)
COLLAB=$(curl -s -X POST -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d '{"role":"cohort","cohort":"ddmlab-collab","count":1,
       "caps":["mint","upload_code"],"quota":200}' \
  $B/admin/keys | python -c "import sys,json;print(json.load(sys.stdin)['keys'][0])")
# hand COLLAB to the collaborator — it is not retrievable later

# (2) COLLABORATOR uploads a config bundle with their own key
python cora_bundle.py push bundles/mylab/terse.json --url $B --key $COLLAB
#   -> stored as 'ddmlab-collab__terse'  (namespace derives from the TOKEN, never the body)

# (3) COLLABORATOR stages a plugin (optional; requires upload_code)
curl -X POST -H "Authorization: Bearer $COLLAB" --data-binary @my_tools.py \
     "$B/plugins?name=my_tools"
#   -> {"status":"staged","active":false,...}   NOT running yet — see §5

# (4) COLLABORATOR mints participant keys SCOPED TO THEIR CONFIG
curl -X POST -H "Authorization: Bearer $COLLAB" -H 'Content-Type: application/json' \
  -d '{"role":"cohort","cohort":"study-A","count":30,"quota":20,
       "configs":["ddmlab-collab__terse"]}' $B/admin/keys
```

**The `configs` scope in step 4 is required.** Participants do NOT inherit the minter's uploads;
without it they see an empty config list. With it, the config is both visible and playable.

```bash
# (5) COLLABORATOR pulls their corpus and builds an SFT set
curl -H "Authorization: Bearer $COLLAB" "$B/my/sessions/export?format=tar" -o corpus.tar.gz
python export_sft.py --from-sessions corpus.tar.gz --out sft.jsonl
#   filters: --agent "<Officer>", --min-reward X
#   export filters: ?config=<name>&limit=N&format=ndjson|tar
```

Every `/my/*` route is scoped to the **token's label**, so a key can only ever read its own
cohort's data — there is no parameter that widens it.

---

## 4. Maps are per-deployment (and NOT served by the router)

Maps come from `mapConfigUrl` in the client's `StreamingAssets/config.json`, fetched by
`GameConfigLoader` — deliberately **outside** the router, which stays an LLM/session concern.
This is what lets a partner expose a map derived from proprietary data without CORA ever seeing
the source data.

Consequences, and what protects you:

- **One map per deployment.** A map matrix = one deployment per condition (or swap + restart).
- **`strictMap: true`** in `config.json` — if a configured map can't be applied, the client
  **refuses to start** instead of silently falling back to the default scene layout (which would
  quietly run the wrong condition). Leave it off for casual play; turn it **on for studies**.
- **Provenance is recorded.** The client reports `map_url` / `map_hash` / `map_status` in the
  hello frame and the router stamps them into `session_start`, e.g.
  `"map": {"url": "...", "hash": "a1b2c3d4e5f6", "status": "loaded"}`.
  A merged corpus therefore stays separable by map, and `status != "loaded"` marks any session
  that silently ran the default layout. The router only *records* this; it never serves maps.

```json
{ "wsUrl": "wss://<host>/ws", "apiKey": "<participant key>",
  "mapConfigUrl": "https://<map-host>/flood-a.json", "strictMap": true }
```

---

## 5. Plugin code: staged, manual activation

Uploaded Python **executes in the router process** once activated, so activation is deliberately
a separate admin step.

1. `POST /plugins` (cap `upload_code`) → validates it parses, writes
   `plugins_staged/<label>__<slug>.py`, returns `active: false` plus advisory `review_notes`
   (risky imports/calls, "no register_tool found"). **Nothing is imported.**
2. **Review the staged file.** The AST scan informs review; it is *not* a sandbox.
3. `POST /admin/plugins/reload` (admin, via tunnel) imports `plugins/` + `plugins_staged/` and
   activates. A plugin that fails to import is reported in `load_errors`, never fatal.

`GET /plugins` shows staged-inactive vs active tools at any time.

**Decide before exposing `/plugins` publicly:** granting `upload_code` to a collaboration key is
granting eventual code execution on the server, gated only by your review. For lower trust, omit
`upload_code` from collaboration keys and have collaborators send plugins out-of-band.

---

## 6. Running the router (unbuffered!)

```bash
env -u ALL_PROXY -u HTTP_PROXY -u HTTPS_PROXY -u http_proxy -u https_proxy \
  ./.venv/bin/python -u agent_router.py \
      --config-dir config --keys-file config/keys.json \
      --port 9876 --admin-port 9877 --log-dir logs/sessions
```

**Use `python -u`.** Under `nohup`/systemd, Python block-buffers stdout, so router logs lag
behind reality — you can be staring at a log that is thousands of lines behind the live
process while debugging an incident. `-u` (or `PYTHONUNBUFFERED=1`) makes them stream.

`--admin-port` binds loopback-only (§1). `CORA_DEV_DOCS=1` re-enables `/docs` + `/openapi.json`
on the public app — **local dev only**; leaving it off in production keeps the route/schema
surface unlisted.

## 7. Loop-shaping hooks (customize the agentic loop without replacing it)

A plugin can shape *how an officer thinks* — not just its prompt and tools — via two hooks.
The harness still owns tool-protocol pairing, action execution, the reply guarantee and turn
logging, so every variant produces identical action semantics and a comparable corpus.

```python
from cora_ext import register_hook

@register_hook("on_turn_start")      # -> str | [{"role":"user"|"system","content":str}] | None
def scratchpad(ctx, ev):
    if ev["brief_only"]:
        return None                   # skip the officer's opening brief
    return "Think it through in one line: what changed, biggest need, one action."

@register_hook("on_step_end")        # -> "stop" | True | None
def one_action_per_turn(ctx, ev):
    return "stop" if ev["executed_total"] >= 1 else None
```

`ev` (on_turn_start): `agent, round, max_steps, brief_only, triggered_by_director,
actions_available`. `ev` (on_step_end): `agent, step, max_steps, content, tool_names,
executed_total, spoke`.

Covers ReAct-style scratchpads, self-critique, retrieval injection, step budgets, confidence
gates, and control conditions. `assistant`/`tool` roles are refused on injection — they would
break tool_call_id pairing. Working example: `examples/plugins/loop_shaping.py`. Tests:
`test_loop_hooks.py`. A full custom loop (`register_loop`) is deliberately NOT implemented —
these hooks cover the common cases; revisit if a collaborator genuinely needs to replace the
loop itself.

## 8. Pre-deploy checklist

- [ ] Apache: add `/my/` and `/whoami` (+ `/plugins` if used); confirm `/admin/*` is **not**
      reachable publicly (it is on port 9877 now, so it should 404 on the public host).
- [ ] Router launched with **both** `--port 9876 --admin-port 9877`. Without `--admin-port` the
      admin app still binds its default 9877 on loopback — but confirm the tunnel matches
      (`ssh -L 9877:127.0.0.1:9877`), because the old runbook said 9876.
- [ ] `config/keys.json` and `.env` present on the box (never committed, never printed).
- [ ] Router started **without** `--keys-file` only in dev; production must pass it (otherwise it
      falls back to a single `dev-local-key`).
- [ ] Client `config.json`: correct `wsUrl`, `mapConfigUrl`, and `strictMap` for the condition.
      **A WebGL rebuild resets this file to the Talos default — re-check it after every build.**
- [ ] Serve the WebGL client with cache headers that don't mix builds (a stale
      `framework.js` against a new `.wasm` throws `ASM_CONSTS[code].apply`).
- [ ] Smoke test from a laptop (not the box), with `CORA_URL`/`CORA_KEY` set:
      ```bash
      python cora.py doctor                      # reachability, key, capabilities
      python cora.py new  smoke/probe            # scaffold
      python cora.py check bundles/smoke/probe.json
      python cora.py push  bundles/smoke/probe.json
      # play one round in the browser picking the uploaded config, then:
      python cora.py data --export               # the round comes back
      ```
      This is the same path a collaborator walks; if it works, onboarding works.
- [ ] Hermetic tests green before shipping:
      `test_tag_translation.py`, `test_reactive_officers.py`, `test_loop_hooks.py`,
      `test_concurrent_officers.py`, `test_bundle_platform.py`, `test_gym_tags.py`,
      `test_router_fixes.py`, `test_step7_polish.py`.
      (`test_propose_tags.py` has a known pre-existing dedup failure — not a regression;
      `test_alerts_dont_stall.py` needs `ARC_GAME_BUILD` and is not hermetic.)
- [ ] Do **not** push to Talos without the maintainer's approval.
