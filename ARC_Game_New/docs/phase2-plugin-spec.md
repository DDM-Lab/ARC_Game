# Phase 2 — Tool/loop extensibility + plugin dev workflow (spec)

_Status: design, for review. No code yet. Builds on docs/contributor-platform-design.md and
docs/CORA_API_v1.md. Acceptance test: the `bayesian_choices` plugin (§8)._

## 0. Goals & non-goals

**Goal:** let trusted colleagues extend CORA with their own **tools** (LLM-callable) and **hooks**
(event-driven) — e.g. a Bayesian preference elicitor — that can pull live game data, act on the
game, import libraries, do math, keep persistent state, and write custom logs. Give them a **fast
local test loop** and a **staging container** to run plugins against the real game, with promotion
to production by git.

**Not a goal (yet):** sandboxing against *malicious* code. Threat model is *trusted colleagues who
write bugs*, not attackers. Execution is in-process, gated by a per-key capability; the validation
pipeline catches bugs, not malice. A real execution sandbox (gVisor/Firecracker) is deferred to a
future "untrusted uploads" phase.

## 1. Two extension kinds

- **Tool** — the officer LLM can call it. Handler signature `(ctx, args) -> ToolResult`.
- **Hook** — fires on a game event, even when no tool was called. `(ctx, event) -> None`.
  Event set (v1): `on_round_start`, `on_choice_resolved`, `on_action_executed`, `on_session_end`.

Both are registered against a stable module, `cora_ext`, and reach the game **only** through `ctx`.

## 2. `ToolContext` (`ctx`) — the host-owned handle

`ctx` is NOT a library; it is an instance the host (`Session`) constructs per call and injects. It
is a curated facade over the running `Session` (which owns the Unity socket, `_unity_commit_lock`,
`_director_attention_lock`, `_latest_game_state`, the logger). Surface:

**Read (instant; off the cached authoritative snapshot):**
- `ctx.state` — raw latest `game_state` dict
- `ctx.get_facilities()` / `get_workforce()` / `get_tasks()` / `get_logistics()`
- `ctx.budget`, `ctx.satisfaction`
- `ctx.enumerate_actions()` / `ctx.enumerate_choice_packages()` — current valid affordances

**Fresh pull (async; wraps `{"type":"get_game_state"}` under the commit lock):**
- `await ctx.refresh_state()` → `Session._fetch_fresh_state()`

**Act (async; canonical actions only):**
- `await ctx.emit_commands(tags)` → `cmd_parser.parse_commands` → `Session._execute_actions_via_unity`
- `await ctx.propose_choices(packages)` → the existing propose/repropose path

**Persistent state (three explicit scopes — see §4 for concurrency):**
- `ctx.agent_store` — dict scoped to (session, this officer)
- `ctx.session_store` — dict scoped to the whole game (all officers, one human)
- `ctx.persist` — durable KV, keyed by participant, across games (SQLite-backed)
- `async with ctx.session_lock:` — serialize shared-session writes

**Misc:**
- `ctx.log(event_type, payload)` → `episode_logger.log_event` (correct attribution/timestamp)
- `await ctx.run_blocking(fn, *args)` → `loop.run_in_executor` (offload MCMC etc.)
- `ctx.agent` (name/role, read-only), `ctx.participant_id`, `ctx.session_id`, `ctx.round`

## 3. `cora_ext` — the stable plugin API (the only thing plugins import)

```python
from cora_ext import register_tool, register_hook, ToolResult, ToolContext

@register_tool("name", schema={...}, acting=False, override_of=None)
async def handler(ctx, args) -> ToolResult: ...

@register_hook("on_choice_resolved")
def obs(ctx, event): ...
```
- `register_tool(name, schema, *, acting, override_of)` — `override_of` replaces a built-in
  (e.g. `propose_choices`). Collision without `override_of` → error. Namespaced names (`lab/tool`).
- `register_hook(event)` — one of the §1 events.
- `ToolResult(text, executed=0, finish=False)`.
- `load_plugins(dirs)` — import every module under the plugin dirs + `entry_points("cora.plugins")`;
  registration happens as a side effect of import. Called at router startup. **cmd_parser stays
  harness-owned** — plugins act only via `ctx.emit_commands`, preserving the single action
  representation (RL/SFT parity).

Additive-only versioning (grow `ctx`/events by adding fields; never remove/reorder). `API_VERSION`
in `cora_ext`.

## 4. Concurrency & multi-agent

One session = one game, one human (director), N concurrent officers. Game reads/acts from a tool
inherit the existing session locks (`_unity_commit_lock`, `_director_attention_lock`) — a tool is
as safe as an officer. For **shared persistent state**: use `ctx.session_store`/`ctx.persist` (the
human is one person regardless of which officer surfaced a choice), not `agent_store`. Choice
resolutions are already serialized by the attention lock, so an `on_choice_resolved`-driven model
is race-free; other shared writes use `ctx.session_lock`. Never use module globals for per-
participant state (concurrent sessions/officers would collide).

## 5. Plugin delivery & lifecycle

- Plugins live in **`plugins/<label>/<module>.py`** (per-uploader namespace).
- **Capability-gated upload:** a key may carry `caps: ["upload_code"]`. Only such keys can POST a
  plugin. Participant/cohort keys never get it.
- **Config bundles reference tools by name** (bundle `tools` slot); the handler must already be
  registered, else validation rejects it as an unknown tool.

## 6. The three-tier test workflow (the point of this phase)

1. **Local (offline, instant)** — `cora-plugin check plugins/foo.py`:
   - imports the module in a **subprocess** (syntax/import/dep crashes caught safely);
   - asserts it registers well-formed tools/hooks (valid schema, namespaced, no collision);
   - runs each tool/hook against a **mock `ctx`** (`MockToolContext` + recorded `get_game_state`
     fixtures + recording `emit_commands`); asserts valid `ToolResult`, valid `cmd_parser` tags,
     no exception, within a **time budget**.
2. **Staging container** — a separate router instance (own port/subdomain, own non-prod keys,
   headless test Unity). `cora-plugin push-plugin foo.py --url <staging> --key <cap-key>` →
   server **canary-validates in a subprocess**, then loads into `plugins/<label>/`. Colleagues play
   or benchmark against staging to catch integration bugs the mock can't. Bugs here never touch a
   live study.
3. **Production** — plugin graduates via **git/PR** (reviewed, versioned); production loads only
   promoted plugins at startup and does **not** accept raw code uploads.

**Runtime guards (all tiers):** per-tool wall-clock timeout + exception isolation (a throwing/
overrunning tool yields an error result to the officer, never a router crash); heavy compute via
`ctx.run_blocking`. Per-label plugin namespacing so colleagues don't clobber each other on the
shared staging box.

## 7. Scaling to tens/hundreds of users (design-for-now, build-later)

- `ctx.persist` backed by **SQLite/DB**, not in-memory — survives restarts, shared across sessions.
- **Unity backend pool:** one game = one Unity process; scaling users = a pool/allocator of headless
  Unity instances (the real capacity bottleneck).
- **Sessions are in-memory per worker** → if we ever run multiple router workers, use sticky routing
  (or a shared session store). Plugin registries are per-worker (fine — code, not per-user data).
- **Keys/quotas/metering:** Phase 4 (hashed keys + cohorts + `usage_events` + rate limits) is what
  makes hundreds of participant keys safe and attributable.
- Staging vs production isolation is itself a scaling best-practice (blast-radius containment).

## 8. Acceptance test — `bayesian_choices`

A single plugin exercising every capability (see docs/contributor-platform-design.md discussion):
`on_choice_resolved` hook updates a participant posterior in `ctx.session_store` seeded from a
population prior in `ctx.persist`, logs it via `ctx.log`; the `bayesian_choices` tool pulls fresh
state, ranks choice packages with `ctx.run_blocking(model.rank, …)`, and emits via
`ctx.propose_choices`. Must pass `cora-plugin check` (mock ctx) then run on staging end-to-end.

## 9. Staging page (dashboard) — later sub-phase

A web page on the staging URL: upload a config/plugin, run `check`, view its logs/transcripts,
launch a test game against it. Reuses the same validation core + endpoints (CLI-first, page-later —
the page is a front-end over the same APIs). This is the no-code surface for colleagues.

## 10. Build order within Phase 2

1. `cora_ext.py`: registries (`register_tool`/`register_hook`/`ToolResult`) + `ToolContext` facade
   over `Session` + `MockToolContext`.
2. Mechanical refactor: `_dispatch_continuous_tool` → registry lookup; seed built-in tools; convert
   `_run_subagent` loops. Behavior-preserving (verify with existing tests).
3. Hooks: emit `on_round_start`/`on_choice_resolved`/`on_action_executed`/`on_session_end` from the
   Session at the right points.
4. Stores: `agent_store`/`session_store`/`persist`(SQLite)/`session_lock`.
5. `cora-plugin check` + fixtures (recorded `get_game_state` snapshots).
6. Staging upload: `POST /plugins` (capability-gated) + subprocess canary + per-label dirs +
   runtime timeout/exception isolation.
7. `bayesian_choices` reference plugin + end-to-end staging test.
8. Staging page (dashboard).

## 11. Limitations & trust boundaries (tell contributors up front)

- Tools act only via `cmd_parser` tags — can compose existing actions, **cannot invent a new game
  mechanic** (needs Unity C# + rebuild).
- Tools read only what's in the `get_game_state` snapshot — **new game facts need a Unity change**.
- In-process, trusted execution — gated by `upload_code` capability; validation catches bugs, not
  malice; real sandbox deferred.
- A tool that changes officer behavior changes rollouts — for RL/fine-tune training data it must be
  deterministic and present at train time, else it's inference-only.
- `session_store` is in-memory (lost on restart); durability = `ctx.persist`.
