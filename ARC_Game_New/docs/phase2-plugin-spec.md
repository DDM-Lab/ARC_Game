# Phase 2 — tool/hook plugins and the plugin dev workflow

_Status: implemented in `router/plugin_api.py` (registries, `ToolContext`, `MockToolContext`),
`router/plugin_context.py` (the live `ToolContext`), `router/plugin_store.py` (durable store),
`router/plugin_cli.py` (offline check) and the `/plugins` endpoints in `router/service.py`. Builds
on docs/contributor-platform-design.md and docs/CORA_API_v1.md. Sections marked "not built" are
still design._

## 0. Goals and non-goals

**Goal:** let trusted colleagues extend the GUI officers with their own **tools** (LLM-callable)
and **hooks** (event-driven), e.g. a Bayesian preference model, that read live game data, act on
the game, import libraries, keep persistent state and write custom log events, with a fast offline
check and a staged path onto a server.

**Not a goal (yet):** sandboxing against malicious code. The threat model is trusted colleagues
who write bugs. Plugins run in-process; activation is an explicit admin action; validation catches
bugs, not malice.

## 1. Two extension kinds

- **Tool** — the officer can call it. Handler `(ctx, args) -> ToolResult` (sync or async).
- **Hook** — fires on an event, even when no tool was called. Handler `(ctx, event)`.

`plugin_api.HOOK_EVENTS`:

| Event | Return value |
|---|---|
| `on_round_start`, `on_choice_resolved`, `on_action_executed`, `on_session_end` | ignored |
| `on_turn_start` | `str` or `[{role, content}]` to add context before the officer's first step (user/system roles only); `None` for nothing |
| `on_step_end` | `"stop"` or `True` ends the officer's turn early |

The last two shape the officer loop (scratchpads, self-critique, custom stopping) without handing
the loop itself to a plugin. The harness keeps tool-call pairing, action execution, the reply
guarantee and turn logging. Example: `examples/plugins/loop_shaping.py` (its import still names
the old module `cora_ext`; change it to `router.plugin_api`).

## 2. `ToolContext` (`ctx`)

`ctx` is constructed by the host per call and injected; plugins never touch the router's
internals. The live implementation is `router/plugin_context.py` `_SessionToolContext`; the
offline one is `plugin_api.MockToolContext`.

**Reads (instant, from the latest snapshot):**
- `ctx.state` — the raw latest `game_state` dict
- `ctx.get_facilities()`, `get_workforce()`, `get_tasks()`, `get_logistics()` — the officer's
  filtered observation sections as text (the same text as the officer's getter tools)
- `ctx.enumerate_actions()` — the officer's scoped action list;
  `ctx.enumerate_choice_packages()` — its `task_choice` entries

**Fresh pull (async):** `await ctx.refresh_state()` → `Session._fetch_fresh_state()`.

**Act (async; requires an officer context, not a session-level hook):**
- `await ctx.execute(calls)` — `calls` is `[(tool, args), ...]` or `[{"tool": ..., "args": ...}]`
  of the action tools in `cora/tools.py`, e.g. `[("hire", {"kind": "untrained", "count": 4})]`.
  Runs through `Session._execute_calls`, the same path as the officer's own action calls
  (`cora.executor`, scope, ledger, logging). Returns a `ToolResult` with the per-call outcomes as
  text and the executed count.
- `await ctx.propose_choices(packages)` — the same path as the `propose_choices` tool; packages are
  `{label, calls: [{tool, args}], description?}`.

**State, three scopes:**
- `ctx.agent_store` — dict for (session, this officer)
- `ctx.session_store` — dict for the whole game (in memory; lost on restart)
- `ctx.persist` — durable KV (`get` / `set` / `setdefault`, JSON values), SQLite at
  `data/plugin_store.db`, shared across games and restarts; prefix your keys
- `async with ctx.session_lock:` — serialize shared-session writes

**Misc:** `ctx.log(event_type, payload)` (written to the session log with the payload nested under
`payload`), `await ctx.run_blocking(fn, *args)` (run heavy work off the event loop), `ctx.agent`,
`ctx.participant_id`, `ctx.session_id`, `ctx.round`.

## 3. The plugin API (`router.plugin_api`)

```python
from router.plugin_api import register_tool, register_hook, ToolResult

@register_tool("mylab_tool", schema={...}, acting=False, override_of=None)
async def handler(ctx, args) -> ToolResult: ...

@register_hook("on_choice_resolved")
def observe(ctx, event): ...
```

- `register_tool(name, schema, *, acting=False, override_of=None)` — `schema` is an OpenAI
  function-format dict. `acting=True` marks a tool that changes the game; it is hidden and refused
  on an unprompted turn of a `reactive` officer. Registering an existing name is an error unless
  `override_of` is given.
- `register_hook(event)` — one of `HOOK_EVENTS`.
- `ToolResult(text, executed=0, finish=False)` — `text` goes to the officer; `finish=True` ends
  its turn.
- `load_plugins(dirs)` imports every `*.py` under the given dirs (files starting with `_` are
  skipped) plus `entry_points(group="cora.plugins")`; registration happens on import. The router
  calls it on `plugins/` at startup.
- Plugins act only through `ctx.execute` / `ctx.propose_choices`, so every action goes through the
  one tool-call executor and stays comparable with the benchmark and RL.

`plugin_api.API_VERSION` is `"0.1"`. Growth is additive: new `ctx` members and events, nothing
removed or reordered.

## 4. Concurrency and multi-agent

One session is one game, one human Director and N concurrent officers. Reads and acts from a tool
go through the same session locks as the officers' own calls (the Unity commit lock, the Director
attention lock for proposals). For state shared across officers use `ctx.session_store` or
`ctx.persist`, not `agent_store`; choice resolutions are already serialized by the attention lock,
other shared writes use `ctx.session_lock`. Never keep per-participant state in module globals.

## 5. Delivery and lifecycle

- `plugins/*.py` in the repo are loaded at router startup (currently `example_tools.py` and
  `preference_model.py`).
- `POST /plugins?name=<slug>` (key with the `upload_code` capability; body = the Python source)
  parses the file, runs an advisory AST scan and writes it to `plugins_staged/<label>__<slug>.py`.
  It is **not** imported.
- `POST /admin/plugins/reload` on the loopback admin app (`--admin-port`, default 9877) clears the
  registry and re-imports `plugins/` and `plugins_staged/`. This is the activation step.
- `GET /plugins` lists staged and active tools/hooks and load errors;
  `GET /admin/plugins/errors` returns load failures and recent runtime tracebacks.
- An officer's `tools` allowlist names plugin tools like built-ins. The bundle-level `tools` field
  is reserved and must be empty.

## 6. Test workflow

1. **Offline** — `python -m router.plugin_cli check plugins/foo.py` (or
   `python -m router.cli plugin plugins/foo.py [--upload]`): imports the module, checks it
   registers well-formed tools/hooks, and smoke-runs each tool and the hook events from the
   module's optional `check_fixtures()` against `MockToolContext`, with a per-call time budget
   (`--timeout`, default 5 s). Note: as of 2026-10 the check crashes with `AttributeError: 'MockToolContext' object has no attribute 'emitted'` (a bug in `router/plugin_cli.py`) once it smoke-runs a tool, after the import and registration checks have passed.
2. **Server** — upload to a non-production router, have an admin activate it, and play a game
   against it.
3. **Production** — plugins graduate via git review into `plugins/`.

Runtime guards on every tier: a per-call wall-clock timeout (10 s in the router) and exception
isolation, so a failing tool returns an error result to the officer and a failing hook is logged;
neither stops the game.

## 7. Scaling (design, not built)

- One game is one Unity process; serving many users needs a pool of game backends.
- Sessions are in memory per router process; multiple workers would need sticky routing.
- Keys are hashed in SQLite (`router/key_store.py`, `data/keys.db`) with cohort, config allowlist,
  capabilities, quota and expiry, plus a `usage_events` table; rate limiting is not built.

## 8. Reference plugin

`plugins/preference_model.py`: an `on_choice_resolved` hook updates a Dirichlet-categorical model
of the human Director's picks in `ctx.persist` and logs it with `ctx.log`; the acting tool
`preferred_choices` ranks the choice slots by that model and sends them through
`ctx.propose_choices`. Its packages carry labels only, no `calls`, so they predate the current
package shape.

## 9. Not built

- A staging dashboard page (upload, check, view logs, launch a test game).
- A custom loop registry (`register_loop`); the loop-shaping hooks in §1 cover the intended uses.
- Bundle-declared tools (the bundle `tools` field).

## 10. Limitations (tell contributors up front)

- Plugins act only through the action tools: they can compose existing actions but cannot invent
  a game mechanic (that needs a Unity change and rebuild).
- Plugins read only what is in the `get_game_state` snapshot; new game facts need a Unity change.
- In-process, trusted execution; activation is manual; there is no sandbox.
- A tool that changes officer behavior changes rollouts; for training data it must be
  deterministic and present at train time.
- `session_store` is lost on restart; use `ctx.persist` for durability.
