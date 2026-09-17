# CORA experiment platform — roadmap

_Saved 2026-08-06. Reflects the four research areas + the capture-first prioritization. Platform
work through Phase 0–4 is committed on `feature/contributor-platform` (c01be87e), local only._

## The four areas we're building for
- **A1 — AI-assisted teaching/education** in disaster relief (adaptive curriculum, learning/confidence detection).
- **A2 — Human-AI teaming** (trust, complementarity, AI↔AI comms, prompt/train for better collaboration). *near-term*
- **A3 — Novel RL/ML** (train small LLMs from multi-agent human-AI teamwork data; self-improvement loops). *near-term; the maintainer's focus*
- **A4 — Product / planning-search** (critic-as-evaluator → next-best moves; policy exploration; backtrack to best initial conditions; reduce human cognitive load).

Guiding principle: **participant data is expensive and unrepeatable — capture everything now**, so a
session collected for A2 also feeds A3 training, and nothing is wasted. Explicitly OUT of scope for
now (L2, needs Unity): real-time timed turns, multi-human + voice, new mechanics. Maps + parameters
are enough. External ML infra (trainers, vLLM/LoRA serving, nightly loops, search) is L3 — the
router's job there is only export + provenance + serve-via-provider-enum.

## Layers
- **L1 — platform** (plugin/config/keys/data): self-serve, cheap, mostly built.
- **L2 — game engine** (Unity/C#): deferred.
- **L3 — external ML infra**: off the router.

## Near-term build order (capture-first)
**Step 1 — Provenance stamping** — every logged turn tagged `{model, prompt_ver, plugin_ver, actor,
is_human, config, cohort}`. Foundational for all training/teaming/debugging. *(S, L1)*

**Step 2 — Capture completeness** (so no session is wasted):
- Logprob / confidence logging (A3 signal + over/under-confidence) *(M, L1 + L3 for local activations)*
- GUI/client-event hooks — expose the human click/UI stream *(S, L1)*
- Rating/survey capture — structured trust/satisfaction events *(S, L1)*
- Training-ready export — canonical `(obs→actor→action, reward, provenance)` *(M, L1)*

**Step 3 — Self-serve enablers** (collaborators run their own studies):
- `ctx.config_name` + `plugins/<team>/` isolation *(S)* · `POST /plugins` upload→canary→reload *(M)* · quota enforcement *(S)*

**Then (A2/A3 specifics):** complementarity metrics *(M)* · teacher/student paired capture *(S)* ·
inter-agent text channel *(M)* · dynamic prompt/style injection *(S)*.

**Later tracks:**
- Observability: dry-run/replay endpoint, live session tail, per-experiment metrics view.
- A4 planning/search: gym state checkpoint/restore + branching, critic/value interface, exploration+backtrack harness (L3), scenario/initial-condition search.
- A1 teaching: per-player performance model (persist), scenario-selection API, LLM config generator.

## Feature → area map (remaining)
See the table in the conversation / prior `docs/*` for the full cluster breakdown. Foundation
(provenance, isolation, POST /plugins, quota) + capture (hooks, rating, logprobs, export) is the
shared core that unlocks A1/A2/A3 data; A4 is the distinct gym+critic track.

## Invariant to protect (why the simplification audit)
All arms — officers, plugin `ctx`, hooks, the benchmark, and the RL gym — must READ state, EXECUTE
actions, and LOG/EMIT events through **one canonical pathway each** (obs_encoder read; cmd_parser +
one execute path; one logger/event spine). New plugin/ctx/hook code must *delegate* to those, never
reimplement — to avoid the past bug classes (worker-hire execution divergence; getter/setter
state-divergence). An audit is underway to dedup these before adding the capture features.
