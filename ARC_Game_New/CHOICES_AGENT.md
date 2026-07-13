# Choices Agent Guide

How to run, understand, and modify the **choices agent** — the LLM agent that
proposes strategic packages of actions to the human director as selectable cards.

This is the subsystem most likely to be extended by other groups. The key idea:
**the agent lives entirely in the Python router. The Unity client is a thin
renderer.** Change the router (config, prompt, or code), restart it, and the
change flows through to any already-built or deployed client on its next
connection — no client rebuild required.

For the full configuration-field reference (all agent types, action/observation
spaces, LLM providers), see **[AGENT_CONFIG_GUIDE.md](AGENT_CONFIG_GUIDE.md)**.
For swapping *director* prompts to get benchmark numbers, see
**[prompts/README.md](prompts/README.md)**.

---

## 1. What the choices agent does

Each round, a `choices` agent proposes `num_choices` packages (each a small set
of concrete game actions). The client renders them as cards. The director can:

- **Select** a package (the checkbox / card) → `choice_made`.
- **Talk to the agent** via the free-text card or the bottom chat bar → this
  sends a `director_message`, which the router classifies as **REPROPOSE**,
  **CLARIFY**, or **CHAT** (see §5).

Everything the agent knows, proposes, and says is computed server-side from the
game state the client streams to the router.

---

## 2. Architecture / data flow

```
  Unity client  ──WebSocket (ws://host:9876/ws)──►  agent_router.py  ──►  LLM
  (renders cards,        hello / begin_round /            │            (OpenAI /
   sends director        director_message / …             │             Anthropic /
   messages)      ◄──  choices_proposal / agent_message ──┘             CMU gateway)
```

- The client connects, performs a `hello` handshake (API key + config **name**),
  and thereafter only streams game state and director intent.
- The router owns the agent config, prompt, observation encoding, LLM calls, and
  the reliability layer. **This is the surface you change.**
- Because the client picks a config **by name** at connect time, one running
  router can serve many clients/configs (multi-tenant).

> **"Change the router → it flows to the game."** Edit `config/*.json`, a prompt,
> or the router Python; restart `./run_router.sh`; reconnect the client. No
> Unity rebuild needed unless you change the client UI itself.

---

## 3. Run it locally

**One-time setup** (Python 3.8+):

```bash
cd ARC_Game_New
python -m venv .venv
./.venv/bin/pip install anthropic openai ollama python-dotenv fastapi uvicorn websockets
```

**Secrets** (both git-ignored):

- `.env` — LLM provider keys, read by `llm_query.py`
  (`OPENAI_API_KEY` / `ANTHROPIC_API_KEY` / the CMU gateway key).
- `config/keys.json` — client API keys for the router's own auth. Copy
  `config/keys.example.json` to start. If absent (and `ARC_API_KEYS` unset) the
  router falls back to a single `dev-local-key` — fine locally, **never** expose
  it publicly.

**Launch the router:**

```bash
./run_router.sh                 # port 9876, loads every config/*.json
PORT=9876 ./run_router.sh       # override via env
```

**Point a client at it:** launch the game (Editor or a built client) and connect
with a `hello` naming a choices config, e.g. `openai_choices_only_local`.

**REST endpoints** (handy for integration / health checks):

| Endpoint | Auth | Returns |
|----------|------|---------|
| `GET /health` | none | liveness |
| `GET /configs` | `Authorization: Bearer <key>` | configs this key may use |

---

## 4. Configure a choices agent

A choices agent is any entry in a config with `"actor_type": "choices"`. Minimal
example (from `config/openai_choices_only_local.json`):

```jsonc
{
  "subagent_name": "Resource Manager",
  "role": "subagent",
  "actor_type": "choices",
  "num_choices": 3,                 // packages proposed per round
  "max_actions_per_package": 3,     // actions bundled into each package
  "talkinghead_endpoint": "FoodMassCare",
  "llm_provider": "openai",
  "llm_model": "claude-haiku-4-5-20251001-v1:0",
  "llm_endpoint": "https://ai-gateway.andrew.cmu.edu/v1",
  "api_key_env": "OPENAI_API_KEY",
  "system_prompt": "You are a resource allocation strategist. ...",
  "use_global_prompt": true
}
```

Every config also needs exactly one `"role": "director"` agent (usually
`actor_type: "manual"` for a human). See AGENT_CONFIG_GUIDE.md for the full set.

> **Addressing gotcha:** clients may address the agent by its
> `talkinghead_endpoint` (`"FoodMassCare"`) rather than its `subagent_name`
> (`"Resource Manager"`). The router resolves both to the same conversation
> thread — keep that in mind if you add addressing logic.

### Reliability knobs (choices-specific, in `agent_config.py`)

These shape how proposals are cleaned up and explained before the client sees
them. All default sensibly; override per-agent in the config JSON.

| Field | Default | Effect |
|-------|---------|--------|
| `choices_max_retries` | `1` | extra LLM re-queries if a parse under-delivers packages |
| `choices_min_packages` | `1` | floor below which the router retries / falls back |
| `choices_fallback` | `true` | synthesize deterministic packages to fill the set |
| `explain_grounded` | `true` | prepend engine-computed `$cost` to each package description |
| `explain_summary` | `true` | prepend grounded context to the pre-choices summary |
| `choices_repropose_hint` | `true` | append a "you can ask me to repropose" nudge to the summary |

---

## 5. Repropose / clarify / chat

When the director sends a `director_message` **and** the agent has an active
proposal, the router forces a single-path decision instead of doing several
things at once (the old bug: asking a clarifying question *and* silently
reproposing).

`AgentRouter._classify_director_intent()` returns one of:

- **REPROPOSE** → acknowledge, then `_repropose_choices()` regenerates a fresh
  proposal **and** its summary.
- **CLARIFY** → ask a single clarifying question; no new proposal.
- **CHAT** → answer conversationally.

Both the free-text "type anything" card and the bottom chat bar route through
this same `director_message` path (via `WebSocketManager.SendDirectorMessage` on
the client). To tune the behavior, edit the classifier prompt in
`_classify_director_intent` and the regeneration in `_repropose_choices`.

**Proposal memory:** right after proposing, the router records the exact
packages as one of the agent's own turns (`_format_proposal_for_memory`) so that
"what did you just propose?" is answered from record rather than the model
disowning options it can't see.

---

## 6. The reliability layer (`choices_reliability.py`)

Pure functions applied to raw LLM output before it reaches the client. Tune
these to change how packages are filtered, explained, and summarized:

| Function | Purpose |
|----------|---------|
| `enforce_diversity` | drop near-duplicate packages (Jaccard on action-type sets) |
| `dedupe_packages` | remove exact duplicates |
| `apply_grounded_explanations` | prepend engine-computed `$cost` + a grounded "why" |
| `build_fallback_packages` | synthesize deterministic packages when the LLM under-delivers |
| `compose_summary` | build the pre-choices summary blob |
| `append_repropose_hint` | append the discoverability nudge |

---

## 7. WebSocket message protocol

Frames the client and router exchange (all JSON, `{"type": ...}`).

**Client → router**

| `type` | Meaning |
|--------|---------|
| `hello` | handshake: `{api_key, config}` (config = a `config/*.json` base name) |
| `game_start` | new game (clears the queue) |
| `begin_round` | `{day, segment, game_state}` — triggers a proposal |
| `choice_made` | director selected a package |
| `director_message` | `{to_agent, content}` — free-text to the agent (see §5) |
| `request_reproposal` | explicit repropose request |
| `round_end` / `client_event` / `gui_event` | lifecycle + telemetry |

**Router → client**

| `type` | Meaning |
|--------|---------|
| `hello_ack` / `hello_error` | handshake result (`hello_ack` carries the agent list) |
| `choices_proposal` | `{agent_name, packages, reasoning}` — the cards to render |
| `agent_message` | `{message_type, content}` — a conversational reply; `message_type` is `agent_response`, `feedback`, `action_summary`, … |

---

## 8. Build & deploy the client

Router changes need no client rebuild. When you *do* change the Unity UI:

```bash
./build_client.sh            # macOS (default)
./build_client.sh webgl      # mac | windows | linux | webgl | all
```

Output lands under `Build/Client/<platform>/`. Close the Unity Editor first
(one instance per project), and install the target's Build Support module in
Unity Hub for non-mac targets.

- **macOS:** the `.app` under `Build/Client/macOS/`. Unsigned builds hit
  Gatekeeper — right-click → Open, or notarize for wider distribution.
- **WebGL:** static files under `Build/Client/WebGL/` — host anywhere that
  serves static content to get a zero-install URL.

Build artifacts are **not** committed to git; the game itself is deployed
separately (e.g. published on Talos).

---

## 9. File map

| Path | Role |
|------|------|
| `agent_router.py` | router, sessions, intent classifier, repropose/proposal logic |
| `choices_reliability.py` | diversity / dedupe / grounding / fallback / summary helpers |
| `agent_config.py` | config schema + validation (the reliability knobs live here) |
| `obs_encoder.py` | game-state → LLM observation encoding (shared with the benchmark) |
| `llm_query.py` | provider-agnostic LLM calls (reads `.env`) |
| `run_router.sh` | launch the router (multi-tenant) |
| `build_client.sh` | build the Unity client per platform |
| `config/*.json` | agent configs (choices, auto, coach, manual director) |
| `prompts/*.json` | director prompt packs for benchmarking |
| `Assets/Scripts/UI/AgentConversationUI.cs`, `Assets/Scripts/Tasks/TaskDetailUI.cs` | client-side card + free-text rendering |
| `Assets/Scripts/WebSocketManager.cs` | client transport (`SendDirectorMessage`, etc.) |
