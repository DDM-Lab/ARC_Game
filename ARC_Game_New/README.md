# CORA — Collaborative Operations And Resource Management with Agentic AI

CORA is a disaster-response management game (Unity) plus a Python research platform around it.
The player (the Director) builds kitchens, shelters and casework sites, hires, trains and staffs
workers, and answers incoming tasks over a multi-day game, scored on Unity's satisfaction and
efficiency measures (`cora/scoring.py`). The
platform lets four kinds of player use the same game through the same interface: LLM officers
alongside a human in the GUI, outside models in a benchmark, policies under RL training, and
traditional search over a fast surrogate. The GUI, benchmark and RL wings share one Python core
(`cora/`) for the observation, the typed action tools, tool-call execution and scoring, so their
results are comparable; the search wing shares the policy family and episode records. The design is described in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## The four wings

| Wing | Entry point | Plays against |
|---|---|---|
| GUI officers | `python -m router` (`router/`) | the WebGL/desktop game over a websocket, with a human Director |
| Benchmark | `python -m bench` (`bench/`) | a headless Unity build over TCP, one process per episode |
| RL | `rl.cora_env.CoraEnv` (`rl/`) | the same headless build; the Verlog fork (separate repo) wraps it in a thin adapter |
| Search | `python -m oracle.rollout`, `oracle.pareto_sweep`, `oracle.mcts` | `oracle/sim`, an exact Python port of the headless build |

The GUI, benchmark and RL wings act through the same seven tools (`build`, `hire`, `train`,
`staff`, `deconstruct`, `task`, and `transfer` when manual transfers are on), defined in
`cora/tools.py` and executed by `cora/executor.py`; `cora.tools.arc_tools_yaml()` emits the same
schema as Verlog's tool config. The search wing runs the same policies and tools on its
surrogate (`oracle.sim.env.SimEnv`), so any plan it finds replays on Unity to the same score.

## Repository map

| Path | Contents |
|---|---|
| `cora/` | shared core: `observation`, `prompts`, `prompt_ablation`, `tools`, `actions` (the action menu), `executor`, `scoring`, `params`, `policy_family`, `records`, `llm/` (providers, `client_for`), `env/` (`GameEnv` TCP client, `unity_process`) |
| `bench/` | the benchmark CLI, episode loop, LLM policy, results, plots, SFT export; `bench/baselines/` holds the non-LLM policies `greedy`, `build-potential`, `combined`, `pareto` |
| `rl/` | `CoraEnv`, the turn contract as a framework-neutral tool-call environment |
| `oracle/` | search: `sim/` (the exact surrogate and its Unity parity tooling, [oracle/sim/README.md](oracle/sim/README.md)), `rollout` (policies on the surrogate, with random-basket exploration), `pareto_sweep` (the policy-family frontier), `mcts` (per-decision UCT) |
| `router/` | the LLM-officer service: FastAPI app, sessions, officer loop and tools, proposals, standing orders, configs, bundles, plugins, keys, dev panel, headless harness |
| `config/` | officer configs (`continuous_*.json`), `global_prompt_config.json`, `keys.example.json` |
| `prompts/` | system-prompt packs for the benchmark and RL ([prompts/README.md](prompts/README.md)) |
| `bundles/`, `templates/` | contributor config bundles and starting templates |
| `plugins/`, `examples/plugins/` | officer tool/hook plugins loaded by the router, and examples |
| `devpanel/` | the router-served developer panel page |
| `ops/` | launch, cluster, deploy and build scripts ([ops/README.md](ops/README.md)) |
| `analysis/` | maintained analysis and diagnostic scripts ([analysis/README.md](analysis/README.md)) |
| `tests/` | pytest suite |
| `Assets/` | the Unity project (C#), including the gym server and websocket client |
| `save_game_logs.py` | game log receiver |
| `setup.sh`, `setup.ps1` | onboarding: venv, dependencies, provider keys in `.env` |

## Requirements

- Python 3.9+ (`setup.sh` checks for it).
- Unity 2022.3.62f3, only to build the game.
- For the benchmark and RL: a headless build. `cora.env.unity_process.default_exe()` looks for
  `Build/Headless/macOS/ARC_Headless.app/...`, `Build/Headless/Linux/ARC_Headless.x86_64` or
  `Build/Headless/Windows/ARC_Headless.exe` under the repo root; set `ARC_HEADLESS_EXE` to use
  another path (`ARC_RENDER_EXE` for the graphics-capable render build).

## Quick start

Setup (creates `.venv`, installs `requirements.txt`, writes provider keys to `.env`):

```bash
./setup.sh               # or ./setup.sh --no-keys
```

### GUI officers (router)

```bash
ops/run_router.sh        # = .venv/bin/python -m router --port 9876 --config-dir config --log-dir logs/sessions
```

`python -m router` flags: `--config-dir`, `--keys-file`, `--log-dir`, `--port` (9876),
`--admin-port` (9877, loopback), `--admin-host`, `--cors-origins`. The client picks a config from
the catalog in its hello frame. Without `config/keys.json` or `ARC_API_KEYS` the router accepts a
single `dev-local-key`; do not expose that publicly. Officer configs are documented in
[AGENT_CONFIG_GUIDE.md](AGENT_CONFIG_GUIDE.md) and the officer loop in
[CONTINUOUS_AGENT.md](CONTINUOUS_AGENT.md).

### Benchmark

```bash
python -m bench --validate                                   # one short no-op game: build and ports
python -m bench --policy combined --episodes 8 --seed 7000   # a baseline, no API
python -m bench --models gpt-5-mini --episodes 4 --seed 7000 # an LLM (default endpoint: CMU gateway)
python -m bench --models qwen3:4b --base-url http://127.0.0.1:11434/v1 --episodes 4
ops/run_benchmark.sh minimal_v6_1 gpt-5-mini 5               # prompt pack + model + episodes
```

Results go to `--out` (default `benchmark_results/`): `episodes.jsonl` and `summary.json`. See
`python -m bench --help` for the observation, prompt and model options, and
`ops/benchmark_cluster.sbatch` for cluster runs.

### RL

```python
from rl.cora_env import CoraEnv, CoraEnvConfig

env = CoraEnv(CoraEnvConfig(seed=7000))
user, info = env.reset()
while True:
    calls = policy(env.system_prompt, env.tools, user)   # [(name, args)] or tool_call objects
    user, reward, terminated, truncated, info = env.step(calls)
    if terminated or truncated:
        break
env.close()
```

The reward is the change in score, so an episode's rewards sum to its final score; the benchmark
plays its episodes through this same class.

### Tests

```bash
pytest                     # unit tests; needs_unity and needs_router tests are deselected by default
pytest -m needs_unity      # needs a headless build (ARC_HEADLESS_EXE)
```

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — layout, turn contract, executor, scenario and parity, contributor rules
- [AGENT_CONFIG_GUIDE.md](AGENT_CONFIG_GUIDE.md) — officer config field reference
- [CONTINUOUS_AGENT.md](CONTINUOUS_AGENT.md) — the officer tool loop
- [docs/CORA_API_v1.md](docs/CORA_API_v1.md) — the contributor contract (tools, observation keys, providers, bundles)
- [docs/COLLABORATOR_GUIDE.md](docs/COLLABORATOR_GUIDE.md) — bundles, plugins, keys and data for collaborators
- [docs/phase2-plugin-spec.md](docs/phase2-plugin-spec.md), [docs/contributor-platform-design.md](docs/contributor-platform-design.md) — plugin API and platform design
- [docs/TALOS_AGENT_QUICKSTART.md](docs/TALOS_AGENT_QUICKSTART.md), [docs/TALOS_DEPLOY_RUNBOOK.md](docs/TALOS_DEPLOY_RUNBOOK.md) — the hosted deployment
- [docs/GAME_LOGIC_FINDINGS.md](docs/GAME_LOGIC_FINDINGS.md), [docs/BUG_REPORTS_v1_testing.md](docs/BUG_REPORTS_v1_testing.md) — game-logic notes and bug reports
- [docs/qwen-auton-serving.md](docs/qwen-auton-serving.md) — serving local models on the Auton cluster
