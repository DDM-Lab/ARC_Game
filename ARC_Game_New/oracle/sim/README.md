# oracle.sim — the exact surrogate

A pure-Python port of the headless game, exact against it: on every captured game it holds
Unity's RNG state at every decision and reproduces the observation, the score and
DailyReportData's whole scoring ledger, decision for decision. A 36-decision game takes
about 9 ms (replay) to 18 ms (a policy playing natively); a world clones in ~12 µs. It exists
so search can explore the game thousands of times faster than Unity can play it.

## Using it

```python
from oracle.sim.env import SimEnv            # GameEnv's interface, so cora.executor,
env = SimEnv(seed=5503)                      # rl.CoraEnv and the bench baselines run on it
env.reset()
```

`SimEnv.game_state` is Unity's `get_game_state` payload (`oracle/sim/export.py`); the policy,
the action menu and the observation text are the same code paths as on Unity. Lower level:
`sim.new_world(rng.game_start(seed).get_state())`, `sim.answer`, `sim.apply_menu_action`,
`sim.step` (one decision), `World.clone`.

## The game it models

The bench-v6 build: 36 decisions (the Day-1 setup decision, then per day a rollover decision
and four round decisions), the pinned map config, the parameter sheet in effect, Unity's
score (`cora.scoring` reads `Economy.metrics()`, the same fields RewardMetricsTracker exports).

| Module | Ports |
|---|---|
| `sim.py` | the decision loop (GlobalClock), task generation and answering, deliveries, walks, the client tracker hooks |
| `economy.py` | budget, workforce, construction, storage (food-need ledger), motel |
| `report.py` | DailyReportData: the cumulative ledger satisfaction and efficiency are applied from |
| `tasks.py`, `roads.py` | the task board and the vehicle fleet (A*, flood-aware) |
| `generation.py`, `triggers.py` | TaskDatabases triggers and the generation pass |
| `flood.py`, `floodmap.py`, `weather.py`, `clients.py` | flood spread, weather, ClientStayTracker |
| `rng.py` | UnityEngine.Random (xorshift128, InitState, the 48 scene-load draws) |
| `export.py`, `env.py` | Unity's state format and the GameEnv-shaped env |

The data it runs on is exported from the build, never hand-written (`corpus/`, `maps/`):
`sim_constants.json` and `map_grid.json` (gym RPCs), `task_assets.json` and
`prefab_fields.json` (`python -m oracle.sim.export_assets`: TaskData / prefab fields the RPC
lacks), `maps/default.json` (`python -m oracle.sim.dump_map`, roads and vehicles from the
build's map config). A build, scene, parameter or TaskData change means re-exporting them.

## Keeping it exact

```bash
python -m oracle.sim.capture 8101 --policy combined --epsilon 0.05 --explore-seed 1   # Unity, unsandboxed
python -m oracle.sim.obs_diff oracle/sim/runs/bench_v6 8101 [--all]                  # field-by-field, per decision
python -m oracle.sim.parity oracle/sim/runs/bench_v6                                  # -> tests/fixtures/sim_parity
python tests/test_sim_env.py build oracle/sim/runs/bench_v6                          # -> tests/fixtures/sim_env
pytest tests/test_surrogate_parity.py tests/test_sim_env.py tests/test_sim_rng.py
```

A capture plays one seeded game through `rl.CoraEnv` with Unity's draw instrumentation on and
records what Unity accepted each decision; `lockstep.py` replays that into the port. The
fixtures are the ratchet: every committed game must stay exact. When a new capture diverges,
`obs_diff` names the first decision and field; the cause has always been a rule read off the
C#, never a fitted constant. Captures live under `runs/` (untracked).

## Search

`search.py` (RHEA), `evolve.py` (population search logging every rollout), `pareto.py`
(frontier and strategy clusters) and `pruning.py` plan over the surrogate's action model
(`actions.py`).
