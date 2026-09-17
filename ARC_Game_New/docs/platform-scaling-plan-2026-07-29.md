# ARC Game — Data-Collection Platform & Scaling Plan

_Drafted 2026-07-29. Owner: Conner. Status: draft for review._

Goal: evolve from a single-router demo into a **multi-version, multi-tenant platform for
collecting LLM-only and LLM-human game data at scale**, with self-service colleague
deployments, A/B testing, a management dashboard, and a clean path to finetuning a local
model on the collected trajectories.

---

## Where we are today (grounded in the code)

- **Frontend**: Unity WebGL served by rootless Apache. Reads a single `wsUrl` from
  `StreamingAssets/config.json`; connects by WebSocket and sends
  `{type: hello, api_key, config, player_id}`.
- **Router** (`agent_router.py`, single uvicorn process):
  - Loads agent **configs** from a folder (`config/*.json`) and API **keys** from
    `config/keys.json` (key → label + allowed configs).
  - `hello` handshake validates key + config scope, then builds an **isolated `Session`**
    per connection (own state, locks, logger). Game state lives **client-side** in Unity.
  - Already exposes **`GET /configs`** (reads the folder live, returns
    `{name, title, agents}` filtered by the key's scope) and **`GET /health`**.
  - Per-session JSONL logs in `logs/sessions/<label>/`, plus a per-key session index.
    Decision events already carry the full training signal: observation, `proposed_packages`,
    `execution_results`, `llm_raw_response`, `satisfaction/budget deltas`, `reward`,
    `tokens_used`.

**Why this matters:** sessions are **shared-nothing** and state is client-side, so
horizontal scaling, blue/green, A/B, and federation are all *clean* — there is no shared
game state to coordinate. The `/configs`-reads-the-folder behavior is already half of the
"config-driven frontend" we want.

## Design principles

1. **The config is the unit of deployment** — a named, versioned, self-describing manifest.
2. **Routers are pluggable backends** — a config declares *which router* serves it; any router
   honoring the hello/session protocol interoperates. Custom agent types live behind that contract.
3. **Shared-nothing forever** — never add cross-session mutable state; keep scaling free.
4. **Everything is training data** — one standardized, tagged trajectory schema.
5. **Backwards compatible** — new fields optional; old configs keep working (the codebase
   already follows this).
6. **One format across all four surfaces** — the *live game*, the *transcripts*, the *tool/prompt
   schema*, and the *RL environment* must share a single definition of observation, action, reward,
   and trajectory. A checkpoint trained in the RL env should drop into the live router with zero
   translation; every live game should be usable as RL/eval data unchanged. This is the precondition
   for the eventual self-play ⇄ real-data ⇄ new-checkpoint loop. (See Workstream S.)

---

## Phase 0 — Prompt caching  *(do now; easy, big win)*

Add `cache_control` to the Anthropic **system prompt + tool schemas** in
`continuous_agent._anthropic_tool_step` (and the OpenAI-compatible path where supported).
The static prefix (v3.0 global prompt ~6k + tool schemas) is re-sent on every one of the
up-to-8 internal steps, every turn, every player — identical bytes. Caching drops cache-hit
input to 10%.

- **Payoff:** ~60–90% input-token cut → major cost + latency + rate-limit relief at every scale.
- **Effort:** low. **Risk:** low. **Verify:** measure token delta on one live turn.

---

## Workstream S — Schema & format unification  *(foundational; do early, mostly cheap)*

Keep the **live serving path** and the **headless RL/eval path** using one definition of every
interface surface, so CORA-the-game and CORA-the-training-env never drift. Grounded in the current
code, here is what is already shared vs. what still diverges:

**Already unified (the good news):**
- **Game rules / dynamics** — the RL env (`arc_game_gym_env_tcp.py`, TCP to *headless Unity*) and the
  live game (WebSocket → *browser Unity*) run the **same Unity codebase**. Rules can't drift because
  it's literally one engine, headless vs. rendered.
- **Action grammar (the ISA)** — `cmd_parser.parse_commands` is used by **both** the router and the
  gym env. That command grammar is the single action ISA; the LLM tool layer (`build_tools`) and the
  RL index layer (`ActionEnumerator`) are just two *encoders* over it. Keep it that way: one grammar,
  two skins.

**Still diverges (close these to hit principle #6):**
- **Reward / score** — a rich scorer lives in `reward_components.py` (used by the gym, `benchmark_models`,
  rescorers), while the live router logs its own `reward` field per turn. _Action:_ make **one** reward
  module the single source, called by both, so "score" means the same thing in a played game and a
  rollout.
- **Trajectory / transcript schema** — the router writes `logs/sessions/<label>/session_*.jsonl`;
  `rollout_runner` writes `episode_*.jsonl` + a `trajectory` summary. _Action:_ converge on **one**
  trajectory schema (fields, event types, IDs) so a human-AI game and a self-play episode are
  interchangeable training rows.
- **Observation encoding** — `obs_encoder` is shared by the router path; confirm the RL/eval path
  produces the **same** encoding (same fields, same rendering) rather than a parallel one.
- **Prompt + tool scaffolding** — `global_prompt_config.json` + per-config rosters + `build_tools` is
  the single prompt/tool contract. When a fine-tuned checkpoint is served, it **must** be prompted and
  tool-fed identically to how its data was logged. Pin a `prompt_hash` / `tool_schema_version` into
  every trajectory for provenance.

**Payoff:** this is the concrete precondition for the full loop — serve a new checkpoint into the live
router with no format shim, and feed every live game straight back into training/eval. **Effort:**
mostly consolidation (extract shared reward + trajectory-writer modules), low new surface area.

---

## Phase 1 — Config-as-manifest + config-driven frontend

Make the frontend render a **picker** from `/configs` instead of a single baked config, and let
each config carry metadata and its **own router URL**.

- **Extend the config descriptor** (all optional — no breaking change; `list_configs` already
  reads raw JSON for `title`):
  `title`, `description`, `version`, `owner`, `ws_url`/`router_url`, `agent_framework`,
  `status` (`draft|staging|live`), `visibility`, `tags`.
- **Frontend**: fetch `/configs`, show a game/version dropdown; connect using the **per-config
  `ws_url`** (falls back to the global one). This single change is what enables pointing different
  configs at different routers.
- **Registry = the config folder** to start. Drop a file → it appears (already true server-side).
- Unlocks versioning, A/B, and federation below.

---

## Phase 2 — Versioning + blue/green  *(work on v-next while v-current plays)*

Because sessions are shared-nothing, run **multiple router instances at once** (blue = current,
green = next) on different ports; configs point at the right one.

- Version configs (`name` + `version` field, or `name@vN`). Keep the live config stable; add a
  `..._vnext` config whose `ws_url` targets the green router.
- **Workflow:** bring up green router with new code/prompt → register a staging config scoped to
  your own key → test in-browser → promote (flip `status: live` / add to A/B). Players on blue are
  never touched.
- Deliverable: a small script to spin up / tear down a versioned router instance on a port.

---

## Phase 3 — A/B testing / experiments

- **Experiment layer**: deterministic bucketing by `hash(player_id)` → variant config; **sticky**
  per player. Log the assignment (`experiment_id`, `variant`) in the `session_start` event.
- **Manifest**: `{experiment_id, variants: [{config, weight}], allocation, holdout}`.
- **Readout is just a query** over session logs — outcome (satisfaction/budget/reward) and variant
  are both logged. Add a kill-switch to force everyone to a safe variant.

---

## Phase 4 — Federation: colleague self-service routers + custom frameworks

Formalize the **Router Protocol** (hello/hello_ack, session messages, `/configs`, `/health`) as a
**documented, versioned contract**. Any framework that speaks it interoperates. Two integration
levels:

- **(a) Config-only** *(easy)*: colleague adds a config to the shared folder and runs on the shared
  router. Good for variations on the built-in `continuous` framework (roster, prompts, models,
  scopes). No code from them.
- **(b) Own-router** *(full freedom)*: colleague hosts **their own router** with their custom agent
  types/framework; their config's `ws_url` points at it. The shared frontend federates it. This is
  the "new config → server points at a different router that has their agent type" model.

Supporting pieces: a config **manifest + light review** (PR to the registry, or a registry API with
owner auth); per-config keys; CORS allowlist per router; isolation (each colleague's router = their
process/credentials/cost, so failures and spend are contained).

---

## Phase 5 — Management dashboard + data platform

A **separate small web service** (FastAPI + simple SPA) reading the same logs — keeps the router
lean and independently deployable.

- **Games view**: player_id, config + version, mode (LLM-only / LLM-human), start/end, **outcome
  score** (final satisfaction/budget, reward), status.
- **Drill-in**: transcript viewer + **one-click JSONL download**.
- **Filters/search**: by player, config, version, experiment, date, mode.
- **Aggregates**: scores by config/version (**A/B readout**), completion rate, rogue-action rate,
  tokens/cost per game.
- **API key management UI**: CRUD **scoped keys** (label, allowed configs, owner, created,
  rotate/revoke). Backs onto `keys.json` now; graduate to a store.
- **Finetuning export**: bulk export filtered trajectories in a training-ready schema; LLM-only vs
  LLM-human tags.
- **Storage evolution**: JSONL stays the source of truth; add a **SQLite index** (→ Postgres later)
  populated from session logs for fast queries; object storage (S3) for transcripts at volume.

---

## Phase 6 — Data collection for finetuning  *(parallel, ongoing)*

- **Tag every trajectory**: mode (`llm_only|llm_human`), config, version, experiment,
  outcome/reward, model + prompt-hash (provenance/reproducibility).
- **LLM-only self-play at volume** via the existing gym env (`arc_game_gym_env_tcp.py`,
  `rollout_runner.py`, `headless_multiagent_harness.py`) — cheap bulk trajectories.
- **Curation pipeline**: session JSONL → filter good games → SFT-format dataset → storage. Later,
  RL against the gym's own reward (satisfaction/budget/task completion) on top of the SFT checkpoint.
- Distill-from-Claude first (production games are labeled data), then wean onto the local model via
  the Phase-1 `ws_url` swap.

---

## Cross-cutting

- **Auth & keys**: scoped, rotatable, per-colleague / per-experiment. **Rotate `adminpass` now.**
- **Observability**: per-router health + metrics (active sessions, LLM latency, tokens, error/429
  rate) + alerting.
- **Cost accounting**: per-config / per-key token & $ rollups (data already in `tokens_used`).
- **Protocol versioning**: version the hello handshake; keep the optional-field discipline.
- **Security**: config review, CORS allowlist per router, secrets never in configs (env / keys file).

---

## Suggested sequencing

1. **Now:** Phase 0 (caching).
2. **Next 1–2 weeks:** Phase 1 (config manifest + frontend picker) + Phase 2 (blue/green). These are
   the backbone — versioning, A/B, and federation all depend on config-as-manifest.
3. **Then:** a minimal read-only Phase 5 dashboard (pays off immediately for data QA) alongside
   Phase 3 A/B.
4. **Parallel/ongoing:** Phase 6 data collection; Phase 4 federation as colleagues come online.

## Key decisions to lock

1. **Federation model** — you leaned toward per-config `router_url` (colleague-hosted routers).
   _Recommend:_ support both; lead with config-only, enable own-router via `ws_url`.
2. **Dashboard** — extend the router vs a separate service. _Recommend:_ separate service reading the
   same logs (keeps the router lean and scalable).
3. **Metadata store** — stay on JSONL+index vs add SQLite now. _Recommend:_ add a SQLite index early
   (cheap, huge dashboard win); JSONL stays source of truth.
4. **Multi-host config registry** — shared folder vs a registry service. _Recommend:_ start with the
   folder; design the registry API for when routers span hosts.

---

## Future-proofing anchor bets  *(cheap discipline now; not scheduled builds)*

The plan above is all shared-nothing and doesn't foreclose the eventual **real-time collaborative
live-app** direction — but a few cheap disciplines now avoid painful retrofits later. None of these are
"start building"; they're "don't paint into a corner."

1. **Event-source the authoritative state.** Treat game state as a *fold over an action-event stream*
   (which the logs already approximate). If state can always be rebuilt and *broadcast* from the event
   log, then live shared-state, reconnection, spectating, and grounding a voice agent all fall out of
   one mechanism. **Highest-leverage single bet.** Keep every state mutation captured as a replayable
   event.
2. **Decouple agent cognition from its I/O channel.** The officer "brain" (observe state → produce
   language + tool calls) should be cleanly separable from the *channel* (chat box today; screen actions
   or voice later). Then a spoken teammate is an I/O adapter, not an agent rewrite. Watch for the router
   entangling decision logic with chat-UI formatting.
3. **Model a Room of participants over channels.** Both a networked shared-state match and a co-located
   voice table are "one room, N participants (human/AI), M channels." Build the room concept once when
   the time comes; keep today's `player_id` growing toward durable identity so repeated play (and
   relationships) can be linked across sessions.
4. **Never let the Unity client rules and the headless engine drift.** They're the same codebase today
   (Workstream S) — keep it that way, and lean authoritative game logic server-side over time. This is
   the cheapest hedge that makes an eventual move to server-authoritative shared state affordable.

## North star — far-future, NOT on the roadmap

Captured so the thinking survives; explicitly **not scheduled** and out of scope for current scaling.

- **Real-time collaborative live-app CORA** — multiple humans (scoped as officers) + AIs sharing one
  server-authoritative world; actions apply live and broadcast to everyone's screen. This is the
  "collaborative live app" model (authoritative server + live event broadcast, not twitch netcode —
  discrete chunky actions, latency-tolerant). Biggest cost: the Unity client moving from
  *authoritative* to *view-over-server-state*. Deep convergence insight: the live authoritative server
  and the headless RL server want to become **the same server** — one headless CORA engine that
  browsers render, LLM officers act in, and RL trains against.
- **Timed rounds** — everyone gets a few minutes to act, then the world advances. Discrete resolution
  is preserved (dodges real-time netcode); new cost is a **server-authoritative match clock** and
  **inference latency becoming a gameplay SLA** (a slow/rate-limited AI *misses the round*) — which is
  exactly why Phase 0 caching + async client + eventual local models matter beyond cost.
- **Voice at the table** — single mic + STT + TTS + LLM so the AI is a spoken teammate. Mostly
  *orthogonal* to the netcode and reuses the agent brain (ears + mouth on an existing cognition); the
  hard parts are conversational (diarization, turn-taking, sub-second latency → local-inference fit).
  Depends on the shared-state work to ground on live game state.
- **Research payoff (owner-led):** in a live-mutable world, communication and action share the round
  clock, so good teamwork (asking before double-building, knowing when to speak up) becomes an
  *emergent optimum under cost*, not a prompt — the environment scores it for you. Open hard problem
  to keep in mind when structuring logged data: **cooperative credit assignment** (whose behavior
  caused the team outcome). _Training/RL approach is owner-handled; the platform's job is to produce
  the unified, well-attributed event stream it needs._
