# Continuous officers

A continuous officer (`"actor_type": "continuous"`) is an LLM that runs entirely in the router
(`python -m router`) and plays alongside the human Director in the GUI. It holds its whole tool
palette every step and decides how to engage: act directly, propose options to the Director,
message the Director or a colleague, look something up, or end its turn. The router does not pick
a style for it; which tools it reaches for is an observed behavior. `continuous` and `manual` (the
human Director) are the only actor types. Config fields are in
[AGENT_CONFIG_GUIDE.md](AGENT_CONFIG_GUIDE.md).

Everything here is router-side Python, so changing it needs a router restart, not a Unity rebuild.

## Tool palette

Built-in schemas are `router/officer_llm.py` `TOOL_SCHEMAS`; dispatch is
`router/officer_tools.py` `OfficerToolsMixin._dispatch_continuous_tool`. A config's `tools`
allowlist narrows the palette; `null` means all of it.

| Tool | What it does |
|---|---|
| `build`, `hire`, `train`, `staff`, `deconstruct`, `task`, `transfer` | the typed game actions from `cora/tools.py`, the same schema the benchmark and RL use. Run through `cora.executor` (`_execute_calls`); see below |
| `propose_choices(reasoning?, packages)` | offer the Director 2-4 packages, each `{label, calls: [{tool, args}], description?}`; blocks until the Director picks, then returns the choice and its result |
| `send_message(to, message)` | message the Director or a peer. `to` is an enum of `Director` plus the officer's `can_address` names that are in the roster |
| `read_state` | the officer's filtered observation as text |
| `get_facilities`, `get_workforce`, `get_tasks`, `get_logistics` | one section of that observation |
| `list_actions` | the action options in the officer's scope |
| `responsibility_lookup(task?, category?, building_type?)` | which officer owns an action or answers a task, and the roster |
| `add_to_autonomy_list(tool, context, args?, reason?)` | ask the Director for a standing order (`router/standing_orders.py`) |
| `remove_autonomy_rule(rule_id)` | cancel one of its standing orders |
| `finish(note?)` | end the turn |

Officers always get `transfer` (`openai_tools(manual_transfers=True)`). Registered plugin tools
(`router/plugin_api.py`) are added to the palette; a plugin tool registered under a built-in's
name (other than the action tools) replaces it;
see [docs/phase2-plugin-spec.md](docs/phase2-plugin-spec.md).

### Action calls

`_execute_calls` runs an officer's typed calls the same way the benchmark and RL do:

1. `cora.executor.plan_turn` resolves each call against the officer's scoped action menu
   (`router/scope.py` `filter_actions` over its `subaction_space`). An unknown tool, bad arguments
   or an out-of-scope target makes the call `invalid`.
2. Resolved game actions are committed to Unity in the executor's order; task answers are sent one
   by one, after a scope check against the task's group.
3. With `ledger_mode: "block"`, a non-repeatable action already committed this phase is not
   re-sent.
4. Each outcome (executed, refused with Unity's reason, invalid) is logged and returned to the
   officer as the tool result. Each committed action also posts an `Action: ...` receipt to the
   Director.

`propose_choices` packages use the same call shape. Their calls are resolved with
`executor.plan_turn` into indices of the action list the client renders, and the client executes
the package the Director picks (`router/proposals.py`). Task answers cannot go in a package;
officers answer tasks with the `task` tool.

## The loop

`router/officer_loop.py` (`OfficerLoopMixin`):

- **Activation.** All officers run concurrently on each `begin_round`; an officer also runs when
  the Director messages it (`director_message`) or a peer messages it. Turns of one officer are
  serialized by a per-officer lock, and commits to Unity by a shared commit lock. An officer with
  no in-scope actions is skipped on a round-start turn.
- **Messages.** Each officer keeps one transcript for the whole game: the system message (global
  prompt, `AGENT ROLE`, tool policy), unseen Director and peer messages, and one user message per
  activation carrying the filtered state and the action options. Old activations are compacted
  away, sized to the endpoint's context window when it is known.
- **Steps.** Up to `max_steps` times: one model call through `officer_llm.run_tool_step`, then
  every returned tool call is dispatched and answered with one tool result. The turn ends when the
  model makes no tool call, calls `finish`, a proposal is superseded, or an `on_step_end` plugin
  hook returns `"stop"`. An identical call that already failed this turn is not re-dispatched.
- **Reactive mode.** With `opening_mode: "reactive"`, an unprompted turn gets a palette without
  the acting tools (action tools, `propose_choices`, `add_to_autonomy_list`), except tools covered
  by an approved standing order, and ends after one `send_message`.
- **Delivery.** A Director-triggered turn that sent the Director nothing ends with a fallback
  reply, so the Director never waits on a silent officer.
- **Logging.** Each turn is written to the session log under `--log-dir` (`router/episode_log.py`).

`run_tool_step` puts three adapters behind one OpenAI-shaped message list: native OpenAI-compatible
tool calls, native Anthropic `tool_use` blocks, and a text (JSON) fallback.
`tool_mode: "auto"` uses the text fallback only for the `ollama` backend.

The tool policy appended to every system message is `_CONTINUOUS_TOOL_POLICY` in
`officer_loop.py`, unless a config sets `tool_policy`. It states, as guidance for the model, when
each tool fits, that every cited number must come from the state, that the officer must not claim
an action it did not see succeed, and that actions stay within its remit.

## Running one

```bash
ops/run_router.sh            # then connect the game and pick e.g. continuous_agent_local
```

`config/continuous_agent_local.json` is a one-officer config; `continuous_all_officers_*.json`
fill all five talking-head slots. To run officers against a headless Unity build with a stub
Director (each proposal answered with its first package), see `router/harness.py`.

## File map

| Path | Role |
|---|---|
| `router/officer_llm.py` | `TOOL_SCHEMAS`, `build_tools`, `run_tool_step` and the provider adapters, context-window fitting |
| `router/officer_loop.py` | activation, transcript, system and turn messages, the step loop, hooks |
| `router/officer_tools.py` | tool dispatch, `_execute_calls`, action options text, commit ledger, `responsibility_lookup` |
| `router/proposals.py` | `propose_choices` cards and the Director's pick |
| `router/standing_orders.py` | `add_to_autonomy_list` / `remove_autonomy_rule` |
| `router/messaging.py` | Director and officer messages |
| `router/scope.py` | `subaction_space` / `subobservation_space` filters |
| `cora/tools.py`, `cora/executor.py` | the action tools and their resolution, shared with the benchmark and RL |
