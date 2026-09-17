# CORA — Failure-mode report (live human-AI games, 2026-07-29)

_Author: analysis by Claude (container session). For the local agent to rsync and act on._

**Build under test:** WebGL rebuild deployed 2026-07-29 ~16:06 · router on `continuous_all_officers_ddmlab`
· model **claude-sonnet-5** (all 5 officers) · global prompt **v3.0** (assistant-first) · officer
opening = named brief, conversational after.

**Sessions analyzed** (`logs/sessions/adminpass/`):
- `session_20260729T142643Z_3a583387.jsonl` — player `b5606f68`, 2 rounds, 13 director msgs (primary evidence)
- `session_20260729T134246Z_f498e759.jsonl` — player `d5ac64af`, 1 round
- `session_20260729T152733Z_025506eb.jsonl` — player `8425d44b`, 1 round (briefs only)

**Working well (keep):** assistant-first behavior mostly holds — frequent "Awaiting director's decision" /
"no action taken pending director," honest capability statements ("I don't have a facility called Shelter
Alpha in my records"), clean remit boundaries. Named-brief-then-conversational works. No blatant rogue
budget grab like the original incident.

The user wants **all** of the fixes below.

---

## Priority order

1. **#1 Task-answer id mismatch** (engine) — biggest gameplay impact; also triggers #7.
2. **#2 Silent Confirm failure / answered tasks don't clear** (engine + client).
3. **#3 Map ↔ officer-state grounding divergence** (client/config).
4. **#5 + #6 Trajectory logging: zero deltas & propose-vs-execute** (logging — blocks data goal / Workstream S).
5. **#4 AI executes on advisory/diagnostic requests** (prompt).
6. **#7 Token blow-ups** (perf/cost — caching + retry cap; #1's fix removes the worst trigger).

---

## #1 — Free daily budget is un-answerable: "Task 4 not found among active tasks"  ·  ENGINE · HIGH

**Evidence** (`3a583387`, round 2): director "Can I request more funds?" → External Officer tried to accept
`BUDGET_DAILY` (the free $2,000 Daily Budget Allocation) and got, on **4 separate attempts**:
```
task_choice success=False err="Task 4 not found among active tasks"
```
The task shows as *active* in the officer's observation but every answer is rejected. Recurred in earlier
games (`a8e32b44`) too — reproducible. Compounds the budget starvation (see below): the one relief valve is
broken, so budget craters to $500 and stays there.

**Root cause (hypothesis):** a **taskId mismatch between enumeration and answering**. The error string is
**not** in the Python — Unity returns it. The router:
- enumerates choice-tasks in `_enumerate_task_choices` (`agent_router.py:101`), carrying `taskId` from
  `game_state` (`t.get("taskId")`), and
- answers via `_execute_choice_via_unity` (`agent_router.py:718`) sending
  `{type: select_task_choice, taskId, choiceId}` to Unity (same frame the gym env uses).

So the `taskId` the router reads from the observed state for `BUDGET_DAILY` does not match the id Unity's
task handler recognizes at answer time (stale id, re-indexed tasks, or budget-domain tasks keyed
differently). Because the gym env uses the **same** `select_task_choice` path, this likely affects RL too —
worth checking as part of Workstream S (action-ISA sync).

**Fix direction:**
- Confirm on the Unity side (task-answer handler / `GymServerManager`) how active-task ids are keyed vs. what
  `game_state` exposes; reconcile so an id enumerated in the observation is answerable.
- Add the failing id + the set of ids Unity considers active to the rejection log so this is diagnosable.
- Router-side guard: if Unity reports "not found," do **not** blindly retry (see #7).

---

## #2 — "I click Confirm and nothing happens" (silent failure + answered tasks don't clear)  ·  ENGINE/CLIENT · HIGH

**Evidence** (`3a583387`, round 2, repeated director complaints):
- *"When I click the 3rd option and press Confirm, nothing happens."*
- *"I see one option I can take. But when I click it and hit Confirm, nothing happens. Why?"*

Two distinct causes:
- **(a) Budget-gated actions fail silently.** EVAC tasks cost **$5,000**, budget was **$500**, so Confirm
  does nothing — with **no error surfaced to the player**. The officer had to infer the budget block.
- **(b) Answered tasks don't clear.** The Food Officer executed `FOOD_C01` itself; the engine logged
  `Resolved: task FOOD_C01/0 → answered task 7 with choice 0 ✅` and our decision log confirms
  `attempts=1 ok=1`, yet "the task still shows open on refresh." So a successful resolution is **not
  reflected back** into the observation/UI — a state-sync bug.

**Fix direction:**
- **Player feedback on blocked actions** (client): when Confirm is rejected (insufficient budget, etc.),
  show *why* instead of no-op.
- **Post-answer state sync** (engine/client): ensure a resolved choice-task is removed from the active set
  and the refreshed state propagates to both the UI and the officer observation.

---

## #3 — Human and AI see different worlds (map vs. officer state)  ·  CLIENT/CONFIG · HIGH

**Evidence** (`3a583387`, round 2):
- Director: *"The map shows Shelter Alpha as being active. Is that correct?"* → Officer: *"I don't have a
  facility called 'Shelter Alpha' in my records at all. The only shelter I show is Shelter_12, and it is NOT
  active (status NeedWorker)."*
- Director references a *"3rd option"* on a task; officer sees only a single option.

The human's rendered map/UI shows a **different scenario** (Shelter Alpha, extra options) than the officers'
observed state (`Shelter_12`, single option). This is the dead `mapConfigUrl` surfacing as a **collaboration
failure** — the rendered map is the default scene, while officers reason from the CSV/state scenario. For a
human-AI teamwork game this is serious: the two players are not looking at the same board.

**Fix direction:**
- Make the client render **the same authoritative scenario** the officers observe (fix/replace the dead
  `mapConfigUrl` source; the officers read from `game_state`, the map must too).
- This is the client-side twin of the Workstream-S "one observation" principle: human view and officer
  observation must derive from one source of truth.

---

## #4 — AI executes consequential actions on advisory/diagnostic requests  ·  PROMPT/BEHAVIOR · MEDIUM

The assistant-first contract still leaks under "helpfulness":
- `f498e759`: Director *"recommend me what to do, I don't know how this game works"* → Disaster Officer
  **built Shelter + Kitchen + CaseworkSite for $3,000** ("recommend" ≠ "build it").
- `3a583387`: Director *"When I click Confirm nothing happens, why?"* → Food Officer **answered the task
  itself** "to test," committing it.

Both are advisory/diagnostic prompts treated as authorization to commit costed actions. Much milder than the
original rogue behavior, but the "let me just do it to help / to test" instinct crosses the line.

**Fix direction (prompt — `config/global_prompt_config.json` v3.0):** add an explicit clause that
*diagnosing a UI problem, answering "why," or being asked to "recommend"* is **not** authorization to
execute — describe/propose and wait for an imperative. (Note the tension: confused new players say "just help
me"; the desired behavior is to lay out the option and ask "want me to do it?", not to act.)

---

## #5 — Trajectory logging: state deltas recorded as zero  ·  LOGGING · HIGH (blocks data goal)

**Evidence** (`3a583387`): every decision event logs `budget X→X, sat Y→Y` even though state clearly changed
(budget **8000 → 500** between rounds; satisfaction **0 → 10**). The `budget_before/after`,
`satisfaction_before/after`, `*_delta`, and `reward` fields are populated from the officer's **frozen
turn-snapshot**, not the post-resolution state, so they don't capture transitions.

Code: `budget_before = _get_budget(game_state)` (`agent_router.py:555, 831, 875`); the after/reward are logged
without re-reading post-execution state. This is the **reward/trajectory divergence called out in Workstream
S** — the live `reward` field is not a trustworthy training signal today.

**Fix direction:** capture `*_after` and `reward` from the **refreshed** state returned after execution (the
result already carries a fresh `game_state`), and route reward through the shared `reward_components.py` used
by the gym so live games and RL rollouts score identically.

---

## #6 — Logging artifact: `propose_choices` counted as a failed action  ·  LOGGING · MEDIUM

**Evidence** (`f498e759`): the Disaster Officer successfully built 3 facilities, but the execution result was
logged as `propose_choices ok=None err=None` and counted as `failed_actions=1`. So the "4/5 actions failed"
headline is partly false — the action-success accounting **conflates proposals with executions**.

**Fix direction:** distinguish `propose_choices` (a proposal handed to the human) from an actual
execute-and-resolve in the success/failure tallies and in the trajectory schema. Part of the Workstream-S
trajectory-format cleanup.

---

## #7 — Token blow-ups (amplified by #1)  ·  PERF/COST · MEDIUM

**Evidence** (`3a583387`, round 2): single-turn `tokens_used` of **125,774** and **67,640** for the External
and Food officers (normal brief ≈ 6–8k). Cause: the `max_steps=8` tool loop (`agent_router.py:1112,1161`)
retrying the un-answerable "Task 4" repeatedly, with **no prompt caching** — the full system prompt + tool
schemas are re-sent every step.

**Fix direction (two parts):**
- **Prompt caching** — add `cache_control` to the Anthropic system prompt + tool schemas in
  `continuous_agent._anthropic_tool_step` (Phase 0 in the platform plan). ~60–90% input-token cut.
- **Retry cap** — do not re-attempt an action that returned a hard "not found / invalid" error within the
  same turn; surface it to the director instead. Removes the runaway loop even before #1 is fixed.

---

## Fix checklist (all requested)

- [ ] **#1** Reconcile task-answer ids (router `_enumerate_task_choices` ↔ Unity `select_task_choice` handler); log the id set on rejection. Check gym parity.
- [ ] **#2a** Client: surface *why* a Confirm is rejected (budget/etc.) instead of no-op.
- [ ] **#2b** Engine/client: clear answered choice-tasks and propagate refreshed state to UI + officer observation.
- [ ] **#3** Client renders the same authoritative scenario officers observe (fix dead `mapConfigUrl`; one source of truth).
- [ ] **#4** Prompt: "diagnose / 'why' / 'recommend' ≠ authorization to execute" clause in `global_prompt_config.json`.
- [ ] **#5** Log `*_after`/`reward` from post-execution refreshed state; route reward through shared `reward_components.py`.
- [ ] **#6** Separate `propose_choices` from executed actions in success accounting + trajectory schema.
- [ ] **#7** Prompt caching in `_anthropic_tool_step` + per-turn retry cap on hard action errors.

_Cross-reference:_ #5 and #6 are the concrete first tasks of **Workstream S** (schema/format unification) in
`platform-scaling-plan-2026-07-29.md`; #7's caching is **Phase 0**.
