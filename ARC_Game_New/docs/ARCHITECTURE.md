# CORA research platform — architecture

CORA is a disaster-response game (Unity) plus a Python platform that lets four kinds of
player use the **same game through the same interface**:

| Front end | Who plays | How it reaches the game |
|---|---|---|
| **Benchmark** (`bench/`) | outside LLMs (API or self-hosted), baseline policies | headless Unity over TCP (the "gym") |
| **RL** (`rl/` + a thin trainer adapter) | policies being trained | headless Unity over TCP |
| **GUI with LLM officers** (`router/`) | a human Director plus LLM officers | the WebGL/desktop game over a websocket |
| **Search** (`oracle/`) | traditional search over policies (Pareto sweep, MCTS) | a fast Python surrogate of the game, validated against seeded Unity runs |

The point of the platform is that results from all of them are comparable: same game rules,
same observation, same tools, same execution semantics, same logs. Anything game-facing is
written **once**, in the shared core, and the front ends only add what is truly theirs (an
episode loop, a trainer adapter, the officer conversation).

> Status: this document describes the target layout of the 2026-10 cleanup. The migration
> table at the end says what has moved so far.

## Layout

```
Assets/                  Unity game (C#). The gym server and websocket client live here.
cora/                    SHARED CORE — the only place game-facing Python logic lives
  observation.py         game state -> observation dict -> compact text (one ObsConfig, no globals)
  tools.py               the typed action-tool schema (OpenAI shape; verl arc_tools.yaml)
  actions.py             the action menu: every game action available in a state, Unity's shape
  executor.py            tool calls -> resolved game actions -> committed; per-call outcomes
  prompts.py             system prompts, as JSON packs in prompts/ (minimal_v6_1, minimal_v6)
  prompt_ablation.py     rule removal / paraphrase arms for prompt studies
  scoring.py             the score (Unity's rewardMetrics) and its components
  llm/                   one LLM client factory (providers, gateway, keys, reasoning capture)
  env/                   GameEnv (Gymnasium over the Unity gym server) + Unity process lifecycle
  policy_family.py       the pareto policy family, shared by the surrogate search and the game
  records.py             reading episode records (episodes.jsonl)
bench/                   benchmark (python -m bench): CLI, episode loop, LLM policy, baselines,
                         results, plots, SFT export, per-turn probe
oracle/                  search: the surrogate, Pareto sweep, MCTS, surrogate validation
router/                  GUI officers: service, session, officer loop, officer tools, proposals,
                         standing orders, developer panel API
rl/                      trainer-facing glue that is not framework specific
ops/                     cluster launchers, Talos deploy, build scripts
tests/                   all tests (pytest; markers: unit, needs_unity, needs_router)
analysis/                analysis and diagnostic scripts that are still maintained
docs/                    this file, guides, runbooks
```

## The turn contract (benchmark and RL)

One turn is one model call:

1. **Input:** the system prompt, the tool descriptions, and the current observation. Nothing
   else (no earlier turns unless the history option is on).
2. **Output:** optional reasoning (text or the model's reasoning channel), then any number of
   tool calls. Zero calls means "do nothing this round".
3. **Tools** are the game actions only — `build`, `hire`, `train`, `staff`, `deconstruct`,
   `task` — and they **return nothing to the model**. The next observation is the only feedback.
4. The executor runs the calls, the game advances one round, and the loop repeats.

Getters, code execution or per-call feedback are later extensions of the same executor, not a
second path.

## Executing tool calls (`cora/executor.py`)

Every front end executes actions the same way:

```
tool calls ──resolve──▶ CallResults (resolved | invalid) ──commit──▶ executed | refused
```

- **Resolve** is pure: it reads the turn's game state and the enumerated actions, and turns
  each typed call into game actions or a task answer, tracking what earlier calls this turn use
  up (workers hired, trained, assigned). It never talks to Unity.
- **Commit** sends the resolved actions to the game. There is one committer per transport: the
  TCP gym (benchmark, RL) and the websocket (router). Both apply the same order (task answers,
  deconstruct, build, hire, train, staff) and report the same outcomes.
- Each call ends as `executed`, `refused` (the game said no, with its reason) or `invalid`
  (could not be resolved: unknown tool, bad arguments, nothing to apply it to).
- What a front end does with the outcomes is its own business: the benchmark logs them, RL may
  turn them into reward, the router tells the officer.

Baseline policies pick from the action menu; `cora.actions.as_tool_call` turns each pick into
the equivalent tool call, so baselines act through the executor and are logged, scored and
exported (as behavior-cloning data) exactly like models.

## Scenario and parity

A game is fully determined by its **scenario**: the map, the parameter sheet and the seed. Each
exported game state carries a `scenario` block (map hash, map source, parameter source, seed)
and every benchmark/RL record stores it.

- **Parameters** come from one CSV (`StreamingAssets/game_param_config.csv`, or
  `ARC_PARAM_CONFIG`). The map never overrides them.
- **The map** comes from `StreamingAssets/map_config.json` (or `ARC_MAP_CONFIG`; `none` = the
  scene's built-in layout). It supplies layout only.
- **The seed** seeds Unity's global RNG before the scene loads.
- **Decision points:** a step ends wherever a human could next act. Day 1 is one decision, then
  the GUI's frozen four-round setup step, then the "End Today" decision. Every later day has a
  decision at the start of each of its four rounds (Round 1 shows the new day's tasks before it
  simulates) and one at "End Today"; the day rollover is its own step with no simulation. A full
  game is 36 decisions and 29 simulated rounds, in the GUI and in the gym alike.

Parity is checked with `analysis/diag_action_coverage.py`: a scripted, seeded game that uses
every tool; `--compare a.json b.json` checks two runs round by round.

## Rules for contributors (people and coding agents)

- **One implementation.** If two front ends need it, it belongs in `cora/`. Do not copy it.
- **No module-global switches.** Options are explicit arguments (e.g. `ObsConfig`), so two runs
  in one process cannot interfere and a function's behaviour is visible in its signature.
- **No retired paths.** Command-tag text, the index menu for models and old prompt variants are
  gone. Do not reintroduce a second action format.
- **Numbers come from the game.** Prompts and observations read live values (costs, capacities,
  workforce); never hard-code a game number in Python.
- **Every record is attributable.** Log the scenario, the prompt hash and the model settings.
- **Keep it deletable.** One-off experiment scripts do not live in the repo; git history keeps
  finished work.

## Migration status (2026-10 cleanup)

| Area | Status |
|---|---|
| Tool-call executor | done in the benchmark (`cora/executor.py`); router and RL pending |
| Headless parameters, pinned map, scenario block | done |
| Gym steps = human decision points (Day 1 setup, rollover step) | done |
| `cora/` package | done |
| One turn record for every front end (now: router `episode_logger.py`, benchmark round record) | pending, with the router migration |
| `bench/` split | done |
| Baselines act through tool calls (`execute_indices` retired) | done |
| Search wing: `oracle/` as a package on `cora/` (policy family, records) | done |
| Surrogate on the new rules (`cora/params`, Unity's score), parity restored | pending |
| `router/` split, legacy actors retired | pending |
| `CoraEnv` for RL; thin Verlog adapter | pending |
| Repo hygiene (scripts, docs, tests) | pending |
| C# `GameApi` facade, task logic out of the UI | separate branch |
