"""Search: traditional algorithms over game policies, on a fast surrogate of the game.

    sim/                   the EXACT surrogate (oracle/sim/README.md): matches the headless game
                           decision for decision; SimEnv plays it through cora.executor
    rollout.py             policies (bench baselines, + random-basket exploration) on SimEnv, in parallel
    pareto_sweep.py        the score-vs-budget frontier over cora.policy_family
    mcts.py                UCT over per-decision candidate moves (portfolio + random baskets)

Every driver records per-decision tool calls; `python -m oracle.sim.capture <seed> --calls f.json`
replays a plan on Unity, which reproduces its surrogate score exactly.

Run as modules from the repo root: python -m oracle.pareto_sweep --seeds 60 --jobs 32. Outputs
live under ARC_RESULTS_ROOT (default: benchmark_results).
"""
import os

RESULTS_ROOT = os.environ.get("ARC_RESULTS_ROOT", "benchmark_results")
