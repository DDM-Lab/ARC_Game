# Continuous Agent Guide

The **continuous agent** is a tool-using LLM that lives entirely in the Python
router. Unlike the `choices`, `auto`, or `coach` agents — each of which is locked
into one interaction style — a continuous agent holds the **full tool palette
every step** and decides for itself how to engage: act directly, propose options
to the director, ask, or explain.

> **Core principle — style is emergent, not imposed.** The router never gates
> which tool the agent may use for a given state, and never inserts a
> confirmation/veto floor. Interaction *style* (instruct/act, recommend, ask,
> coach) is an **observed output** of which tools the agent reached for under
> what conditions — not an input the config dictates. This is deliberate: it
> lets us *study* interaction style as behavior rather than prescribe it.

Like the choices agent, everything lives router-side: change the router, restart
it, and it flows to any connected client. Phase 1 needs **no Unity rebuild**.

---

## 1. The tool palette (Phase 1)

Defined in `continuous_agent.py` (`TOOL_SCHEMAS`); bound to game backends in
`agent_router.py::_dispatch_continuous_tool`.

| Tool | Style it expresses | Backend | Blocks? |
|------|--------------------|---------|---------|
| `execute_game_action(index)` | instruct / act — commit on own judgment | `_execute_action` (raw engine result, no pre-filter) | no |
| `propose_choices(packages)` | recommend — hand the decision to the human | `_send_choices_proposal` + `choice_made` future | yes, until the director picks |
| `talk_to_director(message)` | ask / coach / chat | `_send_agent_response` | no |
| `read_state()` | sense | filtered observation | no |
| `list_actions()` | sense | filtered, indexed action list | no |
| `finish(note?)` | end turn | — | ends the loop |

`execute_game_action` returns the engine's **honest** success/failure (including
reasons like insufficient budget) plus a refreshed action list — the agent tries,
sees the real result, and adapts. There is **no safety floor** in Phase 1;
verification/approval gates may be added later as *additional tools or optional
config*, never as hidden router restrictions.

The system-prompt policy (`_CONTINUOUS_TOOL_POLICY`) encodes 2026 human–AI
interaction findings (review time is the scarce resource; grounded explanations
build calibrated trust; ask only when the answer changes the action) as
**judgment the model weighs**, not as code that constrains it.

---

## 2. The loop

`Session._run_continuous` (in `agent_router.py`):

1. Build the tool schemas (`build_tools(agent.tools)`) and the opening messages
   (system policy + prior conversation + current state + indexed action list).
2. Up to `max_steps` iterations: call `run_tool_step` (provider-agnostic), append
   the assistant turn, dispatch **every** returned tool call, and append one tool
   result per call (protocol requirement), until the agent emits no tool call or
   calls `finish`.

`run_tool_step` normalizes three adapters behind one OpenAI-shaped message list:
- **native** OpenAI / OpenAI-compatible gateways (CMU gateway — verified),
- **native** Anthropic (`tool_use` blocks),
- **text** ReAct fallback (single JSON object) for providers without reliable
  native tool calling (e.g. Ollama). Selected by `tool_mode` (`auto` picks
  native for openai/anthropic, text for ollama).

---

## 3. Configure one

Add an agent with `"actor_type": "continuous"` (see
`config/continuous_agent_local.json`). Continuous-specific knobs
(`agent_config.py`):

| Field | Default | Meaning |
|-------|---------|---------|
| `tools` | `null` (= full palette) | optional allowlist narrowing the palette. `null` is the no-gating default; narrowing is an explicit, documented choice, never per-turn router logic |
| `max_steps` | `8` | max tool-call steps per turn (loop guard) |
| `tool_mode` | `"auto"` | `auto` \| `native` \| `text` |

The config still needs exactly one `role: "director"` agent. As with all router
configs, the client authorizes the config name via `config/keys.json` (yours to
edit).

---

## 4. Roadmap

- **Phase 1 (done):** router-only tool loop — `read_state`, `list_actions`,
  `execute_game_action`, `propose_choices`, `talk_to_director`, `finish`. No
  client changes.
- **Phase 2:** `open_interaction` ⇄ `interaction_result` — hand a native dialog
  panel (Group A: worker management, construction picker, task cards) to the
  human and await completion. Needs a client `open_panel` message + rebuild.
- **Future — multi-agent seam:** `talk_to_director` generalizes to
  `send_message(to: director | <agent_name>)` — additive, point-to-point by
  default; a group chat is emergent from many point-to-point messages, not a new
  primitive. The wire (`agent_message` / `director_message`) already exists.

---

## 5. File map

| Path | Role |
|------|------|
| `continuous_agent.py` | tool schemas + provider-agnostic `run_tool_step` (native openai/anthropic + text fallback) |
| `agent_router.py::_run_continuous` | the tool loop |
| `agent_router.py::_dispatch_continuous_tool` | binds each tool to a game backend |
| `agent_config.py` | `continuous` actor type + `tools`/`max_steps`/`tool_mode` |
| `config/continuous_agent_local.json` | one-officer demo scenario |
