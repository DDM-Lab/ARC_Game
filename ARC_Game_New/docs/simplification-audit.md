# CORA pathway-unification audit (2026-08-06)

Three read-only audits (state-read, action-execution, event/logging-provenance) of whether the new
plugin/ctx/hook layer plus existing arms (officers, benchmark, RL gym) share ONE canonical pathway
per concern. Findings below with `file:line`. **Two are live correctness bugs** (the worker-hire and
getter/setter classes). Verify-before-fix on the two marked ⚠️VERIFY.

## STATUS (2026-08-06)
- **B1 FIXED** (dedup dropped in `_tags_to_indices`) — verified parse repeats `[0,0]` for `<hire>…,10</hire>`. ⚠️ manual-proposal path needs an in-game play-test (does the Unity client execute duplicate `action_indices`?).
- **B2 FIXED** (integer-index-CSV fallback added to `arc_game_gym_env_tcp.step`) — fallback logic verified. ⚠️ needs a fresh benchmark run to confirm `actionsExecuted` reflects build/hire.
- **B3 FIXED + verified** (CLARIFY snapshot → `render_state_text`; tasks render again). Hermetic.
- **B4 → deferred to Tier 1** (log-spine): actor dict-vs-string + timestamp clobber are logging-consistency, not data loss; the example plugins branch on `is_human`. Fixed properly by the `Session._emit` chokepoint.
- Regressions (bundle + plugin-check suites) pass. NOT committed pending B1/B2 live confirmation.

## Tier 0 — Live correctness bugs (fix first)

- **B1 ⚠️VERIFY — worker-hire quantity divergence (exec D2).** `propose_choices` path
  (`_tags_to_indices`, agent_router.py:1887-1889) **dedups** repeated bundle indices, while
  `execute_commands` (agent_router.py:2150) and the gym (arc_game_gym_env_tcp.py:550-551) **keep
  duplicates**. So `<hire>untrained,10</hire>` executes **10** via execute/gym but **5** via a
  proposal. Same tag, different quantity by surface. Fix: drop the `seen` dedup so all paths agree.
- **B2 ⚠️VERIFY — benchmark/gym action no-op (exec D1).** `env.step()` is now tags-only
  (arc_game_gym_env_tcp.py:548-551), but `benchmark_models.py:1125` and `llm_smoke_test.py:490`
  still pass an **integer CSV** → `parse_commands("5,12,3")` returns `[]` → build/hire/staff/etc.
  **silently no-op**; only task choices execute (applied separately at benchmark_models.py:1102).
  Static evidence strong (gym mtime Jul 30 post-dates the last nonzero-action runs Jun 26-30).
  Fix: pass the raw command-tag text to `step()` (or add a documented CSV branch); update the stale
  module docstring (arc_game_gym_env_tcp.py:15). Confirm on a fresh run (`actionsExecuted` should be 0).
- **B3 — CLARIFY snapshot bypasses encoder + drops tasks (read D2).** `_build_observation_snapshot`
  (agent_router.py:2791-2838) emits `json.dumps` of raw subtrees instead of `render_state_text`, and
  its `relevant_keys` name `workers`/`tasks` which don't exist post-filter (real keys
  `workforceState`/`allActiveTasks`) → **tasks omitted from grounding**. Fix: route through obs_encoder.
- **B4 — actor type + timestamp inconsistency (log D2/D3).** `actor` is a dict in `_log_action`, a
  string/`"system"` in `ctx.log` (agent_router.py:4457-4460), and **string for officer vs dict for
  human** in the hook payloads I added (2219/2293 vs 2572/2578) → a hook can't rely on
  `event["actor"]`. Plus `log_event` (episode_logger.py:124) **clobbers** caller timestamps. Fix:
  normalize actor to the dict block everywhere; keep `client_ts` separate from server `timestamp`.

## Tier 1 — One canonical pathway per concern (the dedup)

**Read** (state): everything already reaches `obs_encoder` EXCEPT `ctx.state` (raw, unfiltered) and
the CLARIFY snapshot. Make `ctx.state` filtered-by-default (matching `ctx.get_*`) + add explicit
`ctx.raw_state`; collapse the three action-menu renderers (`_render_action_list`,
`_render_options_compact`, llm_query inline) onto the obs_encoder affordance surface; recompute
`filtered_actions` from the same snapshot state is rendered from (freshness, read D3).

**Execute** (actions): one `execute_resolved(actions, choices, *, scope_agent, ledger,
stop_on_failure)` behind `_execute_actions_via_unity` + `_execute_choice_via_unity`; migrate
auto/choices/coach (`_validate_action_indices`, `_execute_validated_actions`) onto `cmd_parser` /
one validation dialect (kills the dead `'AssignWorker'` gate at :3212 and the site-conflict
divergence); unify failure semantics (gym breaks on first failure :582 vs router continues) and
action-vs-choice ordering. NOTE: `ctx.emit_commands`/`propose_choices` already delegate (the
"wired in follow-up slice" docstring at :4389 is stale — update it).

**Log/event:** one `Session._emit(event_type, payload, *, agent, actor, client_ts, category)` as the
sole caller of `logger.log_event`; route `_log_action`, `log_turn`, `ctx.log`, and **`_fire_hooks`
(mirror each event to the log)** through it; nest caller payload under `"payload"` (stop the
`ctx.log` splat at :4467); move reward scoring out of `EpisodeLogger` (make it a dumb appender);
remove dead `log_conversation_message`/`log_episode_end`.

## Tier 2 — Provenance (prereq for the capture roadmap)

- `_provenance_for(agent)` stamped ONCE inside `_emit`: `{llm_provider, llm_model, prompt_hash,
  plugin_ver+loaded plugins, config_name, key_label, player_id, cohort}`. Prereq: **stash
  `config_name` on `Session` at hello** (currently only passed to `record_session`).
- **Schema split (critical for A3):** `export_sft.py` reads the **benchmark** schema, NOT the
  episode log; `benchmark_models.py` never uses `EpisodeLogger`. So provenance stamped in the router
  reaches **zero training examples**. Reconcile: route the benchmark through the same `_emit` spine,
  or add a router-JSONL reader to the exporter. Also record which reward formula `log_turn` used
  (episode_logger.py:64-69) so export filters are well-defined.

## Sequencing
Tier 0 (verify + fix live bugs) → Tier 1 (dedup the three spines) → Tier 2 (provenance + schema
reconcile) → THEN the capture features (logprobs, GUI/rating hooks, training export) land once on a
clean spine. Doing capture before this bolts new signals onto divergent paths.
