# CORA three-wing unification + off-policy instrumentation plan

**Date:** 2026-08-06
**Status:** DRAFT for review — no code until approved (standing constraint).
**Scope:** Two repos — ARC_Game (`ARC_Game_New/`) and the Verlog fork (`cpulling/Verlog`, branch `rl_cora_jul26`, tool-use commits `87d2c48` + `e0e936f`).

## Thesis

Three "wings" drive the same game through the same Unity env:

1. **GUI/live** — continuous LLM officers advising a human Director (`agent_router.py`).
2. **Benchmark** — scripted model eval (`benchmark_models.py`, `llm_smoke_test.py`).
3. **RL** — Qwen3 PPO in the Verlog fork (tool-call arm: `arc_tools.yaml` + `llm_agents_wrapper.py`).

They already share a spine at the bottom (`cmd_parser.parse_commands`, and the Verlog env is loaded live from ARC_Game via `ARC_GAME_PATH`), but diverge above it on **schema, prompt, observation-rendering, and logging** — each implemented 2–3 times. This plan makes ARC_Game the single source of truth for the *contract* (schema + translator + parser + observation encoding + reward), lets each wing keep its own *surface* (prompt/persona), and adds one **unified trajectory schema** so runs from any wing feed the others — the enabler for off-policy PPO on benchmark + live-human data.

## Current-state map (verified this session)

| Concern | GUI/live (officer) | Benchmark | RL (Verlog tool arm) | Shared? |
|---|---|---|---|---|
| **Action schema** | 1 tool `execute_commands(commands: tag-string)` (`continuous_agent.py:196`) | cmd-tag text / idx | **N typed tools** `build/hire/train/…` (`arc_tools.yaml`) | ❌ two "true tool-use" shapes disagree |
| **tool_call → action** | officer dispatch | n/a (tags direct) | `_synthesize_tags_from_tool_calls` + `_TOOL_TO_TAG` (`llm_agents_wrapper.py:665,675`) | ❌ two impls; RL one silently drops bad args (`:696-704`) |
| **Validation/execute core** | `parse_commands` | `parse_commands` | `parse_commands` (imported from ARC_Game) | ✅ already `cmd_parser` |
| **Prompt** | officer system prompt | `cmd_system_prompt` | preamble shared + `_TOOL_HOW_TO_ACT` spliced (`arc_game/__init__.py`) | ⚠️ preamble shared, framing diverges (correct — see below) |
| **Observation render** | `obs_encoder.render_state_text` (post-B3) | `llm_smoke_test.summarize_commands` | `llm_smoke_test.summarize_commands`+`render_state_compact` (`clean_lang_wrapper.py:58`) | ❌ obs_encoder vs llm_smoke_test helpers |
| **Reward scoring** | (live: none) | benchmark schema | `episode_logger` turn scoring + `format_bonus` | ❌ scorer embedded in logger; benchmark separate |
| **Logging/trajectory** | `_log_action`/`ctx.log`/`_fire_hooks` | benchmark JSON schema | Verlog metrics dicts | ❌ three schemas; `export_sft` reads only benchmark |

Key structural facts that make this tractable:
- Verlog **imports** ARC_Game live via `ARC_GAME_PATH` → `sys.path.insert` → `from llm_smoke_test import …` (`arc_game/__init__.py:11-13,56`). No vendored copy. **The control channel already exists.**
- `ArcEnvTool` is a **schema-only shell** (`verl/tools/arc_env_tool.py`, `execute()` is a no-op) — verl renders the schema from the YAML via the chat template; execution is redirected to the env wrapper. So adding CORA tools needs **no verl-loop changes**, only the env wrapper.
- `parse_commands` already lives in `cmd_parser.py`; `llm_smoke_test.py:31` just re-exports it. The parser is already correctly homed.

## Target architecture

**Two layers, explicitly separated:**

- **Contract (single source of truth in ARC_Game — identical across wings):**
  - `cmd_parser.py` — grammar/validation/execute (exists).
  - `cora_prompts.py` (**new**) — the shared **mechanics preamble** + composable fragments (moved out of `llm_smoke_test`).
  - `cora_tools.py` (**new**) — canonical **typed** tool schema (`build/hire/train/staff/deconstruct/task/transfer`), the `translate_tool_calls(tool_calls) → resolved actions` translator, and the derived tag-map. Officer `TOOL_SCHEMAS` and Verlog `arc_tools.yaml` are both **generated** from here.
  - `obs_encoder.py` — the single observation renderer (absorb/replace `llm_smoke_test.summarize_commands`/`render_state_compact`).
  - `reward.py` (**new**) — pure reward scorer (per-turn **and** per-action attribution), extracted from `episode_logger`.

- **Surface (per-wing, composable — prompts differ by design):** each wing assembles its own system prompt from the shared preamble + its own framing. Officer = human-facing roleplay + Director interaction; RL = terse reward-focused; benchmark = scoring-focused. **Prompt divergence is a feature, not drift** — only the *contract* is forced identical.

**Unified trajectory schema** (`cora_trajectory.py`, **new**) — one record emitted by every wing:
```
{ episode_id, turn_idx, wing: "live"|"benchmark"|"rl",
  messages[] (or transcript_ref + turn_offset),  # EXACT conditioning context (see below)
  raw_response,                              # verbatim model output, pre-parse/pre-synthesis
  obs_text, obs_state,                       # rendered state block (from obs_encoder)
  tool_calls[], resolved_actions[], per_action_validity[],
  behavior_logprobs?,                        # populated where available (see caveat)
  reward: { turn, per_action[] },            # from reward.py
  provenance: { llm_provider, llm_model, prompt_hash, config_name,
                cohort, player_id, is_human, plugin_ver },
  client_ts?, server_ts }
```
**Why `messages` + `raw_response` are mandatory, not `obs_text`+`prompt_hash`:** off-policy PPO — and the recompute-logprobs-under-current-policy path for frontier/human data — needs the *exact* context the behavior policy was conditioned on and its *verbatim* output. A `prompt_hash` can't reconstruct a prompt, and `obs_text` is **not** the officer's conditioning context: officers run a **persistent transcript**, so their turn-N action is conditioned on the whole conversation, not that turn's obs block. Without the full message list + raw response, live-officer records are unusable for recompute-under-policy — the exact data source we most want. Mandating `raw_response` also prevents the RL history-renderer loss the other agent flagged (storing the *synthesized* cmd-tag string instead of `_last_raw_output`).

## Off-policy research payoff (why the trajectory schema matters)

If all three wings emit the same record, one replay buffer holds:
- **RL rollouts** — on-policy, native logprobs.
- **Benchmark/frontier rollouts** — off-policy behavior data (frontier model as behavior policy).
- **Live human-Director + LLM-officer transcripts** — off-policy / preference / reward data (`is_human=true`).

**Honest methodological caveat (must shape the schema, not be discovered later):** proper off-policy PPO/IS needs *behavior-policy logprobs*. Availability differs by source:
- RL (vLLM/sglang): ✅ native.
- Local-model benchmark (Ollama/vLLM logprobs API): ✅ capturable.
- Frontier API (Anthropic/OpenAI) + human: ❌ generally no token logprobs.

So the schema carries `behavior_logprobs` as **nullable**, and the plan supports two consumption paths: (a) true off-policy PPO where behavior logprobs exist; (b) for null-logprob data, **recompute under the current policy** (offline-RL / reward-weighted regression / filtered-BC / v-trace-with-estimated-ρ). This is a real fork for the research; the schema is designed so the buffer never loses the information needed to choose per-batch.

## Phased implementation

### Phase A — Shared spines (foundation; mostly ARC_Game-internal, extends the `simplification-audit.md` Tier 1)
- **A0. Land Tier 0** (B1/B2/B3 already fixed + verified; commit after the pending B1 play-test). Fix the `os.killpg` teardown `PermissionError` (`arc_game_gym_env_tcp.py:109`).
- **A1. Name the homes.** Create `cora_prompts.py`; move `CMD_MINIMAL_SYSTEM_PROMPT`, `cmd_system_prompt`, `idx_system_prompt` out of `llm_smoke_test.py`. Triage render helpers (`summarize`, `summarize_commands`, `render_state_compact`, `render_state_delta`, `compact_action`) → reconcile into `obs_encoder.py`. Leave `llm_smoke_test.py` as a thin smoke driver (`main`/`ask`/`ask_commands`) importing from the new homes.
- **A2. Execute spine.** Three layers: **front-end** (tool_calls dominant / cmd-tags / idx) → **resolver** (`cmd_parser`: `_PRIO` commonsense reorder + `sim_wf` staffing — KEPT, shared) → **executor** `execute_resolved(items, *, game_state, scope_agent=None)`. Decisions (locked 2026-08-06): **execute-as-chosen** (Unity is sole judge; no local pre-validation/skip; failures recorded as engine truth, never remapped); **continue everywhere** (no `stop_on_failure`; the gym's break-on-first at `arc_game_gym_env_tcp:582` is dropped); **task choices are actions** (one ordered stream via `_execute_choice_via_unity`/`_execute_actions_via_unity`, state refreshed between items); **Reading B** — keep the resolver's reorder (ordering stays robust, no prompt change), so "as-chosen" means no skip/no remap, not literal textual order. Migrate officer dispatch + auto/choices/coach + gym onto `execute_resolved`; **delete** `_execute_validated_actions` local pre-validation (budget/worker/site skip, :3220-3242) and the dead `AssignWorker` gate (:3227). Gate the pre-validation removal on two live-Unity probes: (1) does the engine execute an over-budget spend into negative (as the prompt implies) rather than reject it; (2) does it cleanly reject a second construction on an occupied site.
- **A3. Read spine.** `obs_encoder` becomes the single renderer for all wings; **Verlog `clean_lang_wrapper` switches from `llm_smoke_test.summarize_commands` → `obs_encoder`** (coupled to A1). This makes the *rendered state block* identical across wings — necessary for transfer/off-policy validity. It does **not** make full contexts identical (officer = OPTIONS view + getters + persistent transcript; RL = per-turn state block); the parity guarantee is scoped to "same game state → same rendered state block," not "same prompt."
- **A4. Log spine.** `Session._emit(...)` as the sole `logger.log_event` caller; route `_log_action`/`log_turn`/`ctx.log`/`_fire_hooks` through it; normalize `actor` to one shape (fixes audit **B4**); keep `client_ts` vs server `timestamp` distinct.

### Phase B — Canonical typed tool schema across wings
- **B1.** Define canonical typed schema + `translate_tool_calls` + tag-map in `cora_tools.py`. Add generators: `TOOL_SCHEMAS` (officer) and `arc_tools.yaml` (Verlog build artifact — never hand-edited).
- **B2. Officer migration.** `execute_commands(tag-string)` → **parallel typed tool_calls** (`build/hire/…`); dispatch via `cora_tools.translate_tool_calls` → `execute_resolved`. (This *is* the execute_commands→typed change.) **Re-opens two things Steps 4–6 just settled, both must be re-handled here:** (a) the **ledger/block gate** currently lives in the *tags* path of `execute_commands` — port it into the typed dispatch or it silently stops enforcing double-commit protection; (b) **`propose_choices` packages bundles as command tags** (`continuous_agent.py:267-268`) — needs a typed-tool story or an explicit decision to keep tags there. This is the highest-care surface (it took the most iteration last time); its risk estimate assumes both are in scope.
- **B3. Verlog swap.** `llm_agents_wrapper.py`: delete local `_TOOL_TO_TAG` + `_synthesize_tags_from_tool_calls`; `from cora_tools import translate_tool_calls`; surface bad-arg errors as **reward-visible** (kills the silent `bad_json` dropout). **No loop/verl-core changes.**
- **B4. Benchmark tool-use mode** on the same schema.
- **B5. Per-wing prompt composition** via `cora_prompts` (shared preamble + divergent framing); guided-regex stays off in tool mode (already `""` in v3 sbatch); optionally neutralize the legacy `train_arc_game_qwen3_4B.sbatch:91` default.
- **Decision (open):** batch-per-turn execution + **per-action reward attribution** (Option C) vs one-call-per-step (Option B). Recommend C — typed tools make per-action attribution possible without breaking the planning-phase batch semantics.

### Phase C — Provenance + unified trajectory
- **C1.** `cora_trajectory.py` schema (above). `reward.py` extracted from `episode_logger` (per-turn + per-action).
- **C2.** `_provenance_for(agent)` stamped once inside `_emit`; stash `config_name` on `Session` at hello.
- **C3.** All wings emit the trajectory record through `_emit` (benchmark routed through `_emit` or an adapter — fixes the Tier 2 schema split where `export_sft` reads only the benchmark schema; RL wrapper emits it; officer emits it).

### Phase D — Instrumentation for off-policy research
- **D1. logprob capture.** RL native; local benchmark via logprobs API; frontier/human → null + documented recompute-under-policy path.
- **D2. Human/GUI capture.** Director GUI actions + ratings/surveys → trajectory records with `is_human=true` (extends existing `GuiInteractionRecorder`/`click_seq` logging).
- **D3. Unified exporter.** One reader over `cora_trajectory` → PPO replay buffer / SFT corpus; retire the benchmark-only `export_sft` split.
- **D4. Off-policy ingestion.** Replay buffer mixing on-policy RL + off-policy benchmark/human, honoring the behavior-logprob fork (D1).

## Sequencing, risk, verification
- **Order:** A → B → C → D. A is the refactor foundation (nothing new-behavior); B changes model-facing surface (officer + RL schema) — the highest-risk, needs live round-trips both wings; C/D are additive instrumentation once the spines are clean.
- **Bolting D before A/B** = new signals on divergent paths (the exact mistake the audit warns against). Do not.
- **Live cluster RL run is active NOW** (step-26 dumps). A3 (obs encoding) and B3 (schema/yaml regeneration) both **invalidate the SFT v2 corpus and make existing Qwen3 checkpoints non-comparable** — the corpus must be regenerated and runs restarted under the new contract. Plan the A/B→C cutover as a clean break, not a hot-swap.
- **Verify:** hermetic per-step where possible (`test_concurrent_officers.py` pattern); live Unity round-trip for B2/B3 (schema changes); a cross-wing **obs-parity test** (same game state → identical *rendered state block* from officer, benchmark, RL) gates A3; a **schema round-trip test** (each wing emits a valid `cora_trajectory` record with `messages`+`raw_response`) gates C3.
- Standing constraints: verify before commit; no config defaults flipped without user sign-off; no Verlog commit branding; commit locally, don't deploy.

## Decisions (locked 2026-08-06)
1. **Reward granularity: Option C** — batch execution + per-action attribution. (Easy for format/validity per-action credit; per-action *outcome* credit still needs shaping since Unity steps once per bundle.)
2. **Off-policy scope: keep current true-IS** — no new recompute-under-policy machinery for frontier/human now; schema still carries nullable `behavior_logprobs` + `messages`/`raw_response` so that path stays open later without a re-capture.
3. **Trajectory buffer: JSONL-at-the-edge → parquet-for-training.** Schema (`cora_trajectory.py`) owned by ARC_Game and imported by all wings; each wing appends JSONL shards where it runs; the export/aggregation step converts to parquet (matches SFT v2) and registers refs in the rollout catalog (#67). *(Confirm.)*
4. **Cutover timing:** Phase A is non-breaking → start anytime. Phase B is the breaking change (officer live tool surface + RL contract + prompts) → schedule for a clean window: benchmark #72 done, no live human study, RL restart acceptable (corpus regen). Not blocking A.
