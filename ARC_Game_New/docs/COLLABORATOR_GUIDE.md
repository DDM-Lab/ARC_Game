# CORA collaborator guide

How to bring your own officers to CORA: prepare a config (and optional tools), upload it, run it
with participants, and get your data back. Legend: **✅ works today**, **🔜 planned**.

Set once:
```bash
export CORA_URL=https://cora_game_llm.dev.ddmlab.com     # or http://localhost:9876 for local
export CORA_KEY=<your-api-key>
```

---

## 1. What you prepare

### A. A config bundle — prompts / personas / rosters  ✅  (no code)
A single JSON file. Scaffold one:
```bash
python cora_bundle.py new yourlab/terse-food --author "You"
# edits go in bundles/yourlab/terse-food.json
```
It looks like:
```jsonc
{
  "manifest": {"name": "yourlab/terse-food", "author": "You", "version": "0.1.0",
               "cora_api_version": "1.0", "description": "terser food officer"},
  "config": {
    "agent_order_rule": "sequential",
    "agents": [
      {"subagent_name": "Player", "role": "director", "actor_type": "manual"},
      {"subagent_name": "Food Officer", "role": "subagent", "actor_type": "continuous",
       "provider": "anthropic",                       // enum only — NEVER an endpoint or key
       "llm_model": "claude-sonnet-4-6",
       "subaction_space": [{"category": "task_choice", "group": "food"}],
       "subobservation_space": ["sessionInfo", "satisfactionAndBudget", "tasks:food"],
       "system_prompt": "You are the Food Officer. Answer the number first, one sentence.",
       "tools": ["read_state","get_facilities","execute_commands","talk_to_director","finish"]
      }
    ]
  }
}
```
Rules the validator enforces: `provider` is a fixed enum (`anthropic`, `anthropic-ddmlab`,
`cmu-gateway`, `openai`, `ollama-local`) — you can never set a raw endpoint or secret; unknown
keys are rejected; exactly one director. A **delta** bundle (override just a few fields of a base
config) is also supported — see `docs/CORA_API_v1.md`.

### B. (Optional) A plugin — new tools / hooks  ✅ code, trusted
A Python file under `plugins/` (or delivered by git-PR). It reaches the game ONLY through the
injected `ctx`; it never imports the router. Minimal example (`plugins/yourlab_tools.py`):
```python
from cora_ext import register_tool, register_hook, ToolResult

@register_tool("unmet_needs", {"type":"function","function":{
    "name":"unmet_needs","description":"List facilities with the biggest gaps.",
    "parameters":{"type":"object","properties":{"top":{"type":"integer"}}}}})
def unmet_needs(ctx, args):
    facs = (ctx.state.get("mapState") or {}).get("facilities", [])
    ranked = sorted(facs, key=lambda f: f.get("needed",0)-f.get("have",0), reverse=True)
    return ToolResult("\n".join(f"- {f['name']}" for f in ranked[:args.get("top",3)]))

@register_hook("on_choice_resolved")             # fires on every director choice
def observe(ctx, ev):
    m = ctx.session_store.setdefault("pref", {"n":0}); m["n"] += 1     # per-game state
    key = f"yourlab/posterior/{ctx.participant_id}"                    # cross-game state
    ctx.persist.set(key, {"n": m["n"]})                               # survives restarts
    ctx.log("preference_update", {"n": m["n"]})                        # into the session log
```
`ctx` gives you: reads (`ctx.state`, `get_facilities/...`, `await ctx.refresh_state()`), acting
(`await ctx.emit_commands("<hire>untrained,4</hire>")`, `await ctx.propose_choices([...])`),
three store scopes (`agent_store`, `session_store`, durable `persist`), `session_lock`, `log`,
and `run_blocking` (offload heavy math). See `examples/plugins/example_tools.py` and
`docs/phase2-plugin-spec.md`.

---

## 2. The workflow

```
        LOCAL                         STAGING                        PRODUCTION
  ┌──────────────┐   push cfg   ┌──────────────────┐   git-PR    ┌──────────────┐
  │ edit + check │ ───────────► │ upload + test on │ ──────────► │ maintainer   │
  │ (offline)    │              │ real game        │  promote    │ merges       │
  └──────────────┘              └──────────────────┘             └──────────────┘
```

### Configs  ✅
```bash
python cora_bundle.py validate bundles/yourlab/terse-food.json         # offline check
python cora_bundle.py push     bundles/yourlab/terse-food.json \
       --url "$CORA_URL" --key "$CORA_KEY"                              # -> stored privately to you
curl -s "$CORA_URL/configs" -H "Authorization: Bearer $CORA_KEY"       # see it in your catalog
```
Your uploaded config is **private to your key** and selectable in the game's config picker.

### Plugins (tools/hooks)
```bash
python cora_plugin.py check plugins/yourlab_tools.py                    # ✅ offline: import, schema,
                                                                       #    run vs MockToolContext
# TODAY:   drop the file in plugins/ (git-PR for production).          # ✅
# 🔜 STAGING: python cora_plugin.py push-plugin plugins/yourlab_tools.py \
#             --url $STAGING_URL --key $CORA_KEY     (capability-gated, subprocess canary)
```
A config can then reference your tool by listing it in an officer's `tools` (e.g.
`"tools": [ ..., "unmet_needs"]`).

---

## 3. Participant keys (running a cohort)

You hand each participant (or population) a key scoped to your configs, so their games are tagged
to your cohort and their data lands in your namespace.

- **🔜 Planned (Phase 4) — self-serve minting** with your admin key:
  ```bash
  curl -X POST "$CORA_URL/admin/keys" -H "Authorization: Bearer $CORA_ADMIN_KEY" \
       -d '{"cohort":"study-A","configs":["yourlab__terse-food"],"count":50,"expires_days":30}'
  # -> returns 50 scoped participant keys (shown once; stored hashed)
  ```
  Keys are hashed at rest, scoped to your configs, quota/expiry-bounded, and revocable instantly.
- **✅ Today (interim)** — the maintainer adds keys to `config/keys.json` (gitignored):
  ```jsonc
  {
    "pk_studyA_p001": {"label": "study-A", "configs": ["yourlab__terse-food"]},
    "pk_studyA_p002": {"label": "study-A", "configs": ["yourlab__terse-food"]}
  }
  ```
  The `label` is your **cohort**: it namespaces every session's logs and is what the data
  endpoints below filter on. Participant keys must NOT carry the `upload_code` capability.

---

## 4. View and download your data  ✅

Every session a participant plays is logged as an NDJSON event stream (officer messages, actions,
choices, and your plugin's `ctx.log` events like `preference_update`), grouped under your cohort
`label`. You only ever see your own cohort's data (the namespace comes from your token).

```bash
# list your cohort's sessions
curl -s "$CORA_URL/my/sessions" -H "Authorization: Bearer $CORA_KEY"
# -> {"label":"study-A","count":37,"sessions":[{"session_id":"...","config":"...","log_file":"...","started_at":"..."}]}

# download one session's full event log
curl -s "$CORA_URL/my/sessions/<session_id>" -H "Authorization: Bearer $CORA_KEY" -o session.jsonl
```
The durable `ctx.persist` state (e.g. per-participant posteriors) accumulates across sessions and
survives restarts; the logs are the immutable record you analyze (or pull into W&B).

🔜 Planned: a `GET /my/data.zip` bulk export and a staging **dashboard** page that does all of the
above no-code (upload, check, launch a test game, browse/download logs).

---

## What's built vs planned (summary)

| Step | Status |
|---|---|
| Config/prompt bundle upload (`/bundles`, `cora-bundle`) | ✅ |
| Plugin dev + offline check (`cora-plugin check`, `plugins/`) | ✅ |
| `ctx` acting/reads/hooks/stores + durable SQLite `persist` | ✅ |
| View/download your own session data (`/my/sessions`) | ✅ |
| Staging `POST /plugins` (capability-gated) + canary | 🔜 |
| Self-serve participant-key minting (`/admin/keys`, hashed) | 🔜 (interim: `keys.json`) |
| Bulk data export + no-code dashboard | 🔜 |
