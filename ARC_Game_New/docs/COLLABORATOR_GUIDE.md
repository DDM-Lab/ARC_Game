# CORA collaborator guide

How to bring your own officers to CORA: prepare a config (and optional tools), upload it, run it
with participants, and get your data back. Legend: **✅ works today**, **🔜 planned**.

Set once:
```bash
export CORA_URL=https://cora_game_llm.dev.ddmlab.com     # or http://localhost:9876 for local
export CORA_KEY=<your-api-key>
```

## Quickstart — one script

`cora.py` is the only command you need. Run it bare for a guided menu, or drive it directly:

```bash
python cora.py                          # interactive menu
python cora.py doctor                   # ← ALWAYS START HERE
python cora.py new    yourlab/terse     # scaffold an experiment
python cora.py check  bundles/yourlab/terse.json
python cora.py push   bundles/yourlab/terse.json
python cora.py data   --export          # download your cohort
python cora.py sft    corpus.tar.gz     # -> SFT training pairs
```

`doctor` tells you in one shot whether the server is reachable, whether your key works, and
exactly which capabilities it carries — run it first, and whenever something behaves oddly:

```
$ python cora.py doctor
  ✓ server reachable  (0 live session(s), 14 configs)
  ✓ key valid  label=yourlab  role=collaborator
    upload configs  : yes
    upload plugins  : no (needs upload_code)
    mint keys       : yes
```

The rest of this guide explains what you put *in* the files. Every command below is also
available through `cora.py`; the underlying CLIs (`cora_bundle.py`, `cora_plugin.py`) still work
if you prefer them.

---

## 1. What you prepare

### A. A config bundle — prompts / personas / rosters  ✅  (no code)
A single JSON file. Scaffold one:
```bash
python cora.py new yourlab/terse-food --author "You"
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
       "provider": "anthropic-ddmlab",                // enum only — NEVER an endpoint or key
       "llm_model": "claude-sonnet-5",
       "talkinghead_endpoint": "FoodMassCare",        // REQUIRED to appear in the UI
       "subaction_space": [{"category": "task_choice", "group": "food"},
                           {"category": "construction", "building_types": ["kitchen"]}],
       "subobservation_space": ["sessionInfo", "satisfactionAndBudget", "tasks:food"],
       "system_prompt": "You are the Food Officer. Answer the number first, one sentence.",
       "opening_mode": "emergent",                    // speaks from round 1
       "tools": ["read_state","get_facilities","build","task","talk_to_director","finish"]
      }
    ]
  }
}
```
Rules the validator enforces: `provider` is a fixed enum (`anthropic`, `anthropic-ddmlab`,
`cmu-gateway`, `openai`, `ollama-local`) — you can never set a raw endpoint or secret; unknown
keys are rejected; exactly one director. A **delta** bundle (override just a few fields of a base
config) is also supported — see `docs/CORA_API_v1.md`.

> **Three fields decide whether your officer looks alive.** `cora.py check` warns about all
> three, but they cause most first-run confusion:
> - **`talkinghead_endpoint`** — the GUI has exactly **5 fixed slots**
>   (`DisasterOfficer`, `FoodMassCare`, `LodgingMassCare`, `WorkforceService`,
>   `ExternalRelationship`). Without one, your officer runs but its messages can never render.
>   Two officers on the same slot collapse into one tab.
> - **`subaction_space`** — a `task_choice`-only officer is **skipped** in any round with no
>   matching task, including round 0 when no task exists yet. Add an always-available category
>   (`construction`, `worker_assignment`, `resource_transfer`) to be active from the start.
> - **`opening_mode`** — `reactive` means silent until the director addresses it. Use
>   `emergent` while you are still checking that things work.

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
python cora.py check bundles/yourlab/terse-food.json    # offline: schema + authoring warnings
python cora.py push  bundles/yourlab/terse-food.json    # -> stored privately to you
python cora.py doctor                                   # lists your catalog
```
Your uploaded config is **private to your key** and selectable in the game's config picker.

It is stored as `<your-key-label>__<slug>` — so `yourlab/terse-food` pushed with a key labelled
`yourlab` becomes **`yourlab__terse-food`**. The label comes from your *key*, not the manifest,
so you cannot upload into another lab's namespace. The push output prints the final name; that
is the name to use for `--config` filters and participant-key scoping.

Re-pushing the same name **overwrites** it. Bump `manifest.version` so your logs record which
revision produced which session.

### Plugins (tools/hooks)
```bash
python cora_plugin.py check plugins/yourlab_tools.py       # ✅ offline: import, schema,
                                                           #    run vs MockToolContext

# UPLOAD (needs the 'upload_code' capability on your key):  ✅
curl -X POST -H "Authorization: Bearer $CORA_KEY" \
     --data-binary @plugins/yourlab_tools.py \
     "$CORA_URL/plugins?name=yourlab_tools"
# -> {"status":"staged","active":false,"review_notes":[...]}
```
Upload **stages** your plugin — it is validated and stored but **NOT running**. An admin
activates it (`POST /admin/plugins/reload`) after review, because activation imports and
executes your code on the server. `GET /plugins` shows staged-vs-active at any time.

A config references your tool by listing it in an officer's `tools`
(e.g. `"tools": [ ..., "unmet_needs"]`).

### Controlling which tools an officer gets  ✅

Two independent knobs, both per-officer, no code required:

```jsonc
// 1. `tools` — an ALLOWLIST. Omit it for the full palette; set it to narrow.
"tools": ["read_state", "get_facilities", "task", "talk_to_director", "finish"],

// 2. `subaction_space` — what those tools may TOUCH, regardless of the palette.
"subaction_space": [{"category": "construction", "building_types": ["kitchen"]}]
```

The built-in palette is: `read_state`, `get_facilities`, `get_workforce`, `get_tasks`,
`get_logistics`, `list_actions`, `responsibility_lookup`, `propose_choices`,
`talk_to_director`, `finish`, and the typed action tools `build`, `hire`, `train`, `staff`,
`deconstruct`, `task`, `transfer`.

The two compose: an officer given `build` but scoped to `{"category": "task_choice"}` is offered
the tool and finds nothing to build. Scope is the load-bearing control — it is enforced at
resolution, so it holds even if a model tries to act outside it.

### Shaping the agentic loop  ✅

Beyond tools, two hooks let you change **how an officer thinks** — without replacing the loop.
The harness keeps action execution, the reply guarantee and turn logging identical, so your
variant stays comparable to every other run.

```python
from cora_ext import register_hook

@register_hook("on_turn_start")     # -> str | [{"role":"user"|"system","content":str}] | None
def scratchpad(ctx, ev):
    if ev["brief_only"]:
        return None                  # skip the officer's opening brief
    return "Think it through in one line: what changed, biggest need, one action."

@register_hook("on_step_end")       # -> "stop" | True | None
def one_action_per_turn(ctx, ev):
    return "stop" if ev["executed_total"] >= 1 else None
```

Use them for ReAct-style scratchpads, self-critique passes, retrieval injection, step budgets,
confidence gates, or a "one action per turn" control condition. `assistant`/`tool` roles are
refused on injection (they would break tool-call pairing). Full example:
`examples/plugins/loop_shaping.py`.

### Custom prompts — every layer is yours  ✅

An officer's prompt is assembled from five layers. **All five are overridable per bundle**, so
two collaborators can run completely different prompt conditions on one server. Omit any layer
to inherit the server default.

| Layer | Field | What it is |
|---|---|---|
| 1. Behavior | `global_prompt.behavior` | Shared persona / communication rules |
| 2. Game manual | `global_prompt.manual` | Mechanics, costs, strategy |
| 3. Officer persona | `agents[].system_prompt` | Per-officer role |
| 4. Tool policy | `tool_policy` | How to call tools; anti-hallucination; stay-in-lane |
| 5. Per-turn framing | `turn_instructions` | The authored text around each turn's state |

```jsonc
{
  "manifest": {...}, "config": {...},

  // A BARE STRING replaces the BEHAVIOR half only — the game manual is kept.
  // This is almost always what you want when tuning personality.
  "global_prompt": "Answer with numbers first. One sentence.",

  // ...or override each half independently:
  // "global_prompt": {"behavior": "...", "manual": "..."},

  "tool_policy": "Call one tool at a time. Never claim an action unless the tool succeeded.",

  "turn_instructions": {
    "preamble":       "SITUATION",
    "actions_header": "WHAT YOU MAY DO",
    "default":        "Pick at most one action, then call finish.",
    "first_brief":    "Introduce yourself in one line as {title}.",
    "addressed":      "The director asked you directly — answer them first."
  },

  // Reword a BUILT-IN tool's description (this text rides the API `tools` argument,
  // not the prompt, so nothing above can reach it).
  "tool_descriptions": {
    "build": "Break ground on a facility. Kitchens first — hunger compounds fastest.",
    "talk_to_director": "Message the Director. Be terse: one sentence, no preamble."
  }
}
```

**Why a bare string means "behavior only":** the shared prompt is really two things glued
together — behavior rules and the game manual. A single whole-blob replace meant that anyone
tweaking personality silently deleted the mechanics, leaving officers who don't know what a
kitchen costs. Use `{"behavior": ..., "manual": ...}` when you genuinely want to replace the
manual too. The legacy `{"global_system_prompt": ...}` whole-blob shape is still accepted.

**Sharp edges, deliberately exposed.** `tool_policy` is the *mechanical* contract. Rewriting it
is allowed — you are assumed to know what you are doing — but dropping its key clauses reliably
produces officers that narrate actions they never took or act outside their remit, and makes
your runs non-comparable with other arms. `cora.py check` names the specific clause you dropped.

**What you cannot change:** a tool's **parameters and enums**, and the generated observation
text. Parameters feed the command grammar directly, so a renamed field or a widened enum emits
actions the engine cannot resolve — silently broken, in every wing. Descriptions are inert to
that machinery, which is why they *are* exposed. To change parameters or add a tool outright,
ship a plugin (`register_tool(..., override_of="build")`).

---

## 3. Participant keys (running a cohort)

You hand each participant (or population) a key scoped to your configs, so their games are tagged
to your cohort and their data lands in your namespace.

- **✅ Minting** — ask the maintainer to run this; it needs the `mint` capability and the admin
  API is bound to **localhost on the server** (it is not reachable over the internet by design):
  ```bash
  # run ON the server, not from your laptop
  curl -X POST "http://127.0.0.1:9877/admin/keys" -H "Authorization: Bearer $CORA_ADMIN_KEY" \
       -d '{"cohort":"study-A","configs":["yourlab__terse-food"],"count":50,"expires_days":30}'
  # -> returns 50 scoped participant keys (shown once; stored hashed)
  ```
  Keys are hashed at rest, scoped to your configs, quota/expiry-bounded, and revocable instantly.
  `python cora.py doctor` shows whether your own key carries `mint`.
- **✅ Also supported** — the maintainer adds static keys to `config/keys.json` (gitignored):
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

# BULK: your whole cohort in one request — the corpus step for fine-tuning  ✅
curl -s "$CORA_URL/my/sessions/export?format=tar"  -H "Authorization: Bearer $CORA_KEY" -o corpus.tar.gz
curl -s "$CORA_URL/my/sessions/export?format=ndjson&config=yourlab__terse&limit=50" \
     -H "Authorization: Bearer $CORA_KEY" -o corpus.jsonl
```

### Turning your corpus into SFT data  ✅

```bash
python export_sft.py --from-sessions corpus.tar.gz --out sft.jsonl
#   --agent "Food Mass Care Officer"   only that officer's turns
#   --min-reward 0.5                   only better-scoring turns
```
Reads the export directly (tar, directory, or a single `.jsonl`) and emits chat-format
`{messages, meta}` pairs from each officer turn's observation + response.

**Comparability:** every wing (live games, RL, benchmark) scores through the same
`reward_scoring.compute_score_components`, and each session stamps the `reward_weights` that
produced its scores — so live and RL runs are directly comparable, and a corpus can be
re-scored later under different weights (raw `rewardMetrics` are preserved per turn).

The durable `ctx.persist` state (e.g. per-participant posteriors) accumulates across sessions and
survives restarts; the logs are the immutable record you analyze (or pull into W&B).

🔜 Planned: a staging **dashboard** page that does the above no-code (upload, check, launch a
test game, browse/download logs).

---

## What's built vs planned (summary)

| Step | Status |
|---|---|
| One-script workflow + self-diagnosis (`cora.py`, `doctor`) | ✅ |
| Config/prompt bundle upload (`/bundles`, `cora-bundle`) | ✅ |
| Full prompt override (behavior/manual, tool policy, turn text, tool descriptions) | ✅ |
| Per-officer tool allowlist + action scoping | ✅ |
| Plugin dev + offline check (`cora-plugin check`, `plugins/`) | ✅ |
| `ctx` acting/reads/hooks/stores + durable SQLite `persist` | ✅ |
| Loop-shaping hooks (`on_turn_start`, `on_step_end`) | ✅ |
| Staging `POST /plugins` (capability-gated), manual activation | ✅ |
| View/download your own session data (`/my/sessions`) | ✅ |
| Bulk cohort export + SFT conversion (`/my/sessions/export`, `export_sft.py`) | ✅ |
| Self-serve participant-key minting (`/admin/keys`, hashed) | ✅ (admin port, maintainer-run) |
| No-code dashboard | 🔜 |
| Custom observation encoder as a plugin | 🔜 |
