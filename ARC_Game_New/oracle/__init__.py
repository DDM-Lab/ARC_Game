"""Search: traditional algorithms over game policies, on a fast surrogate of the game.

    sim/                   the EXACT surrogate (oracle/sim/README.md): matches the headless game
                           decision for decision; SimEnv plays it through cora.executor
    rollout.py             policies (bench baselines, + random-basket exploration) on SimEnv, in parallel
    search.py              search agents: MCTS / RHEA over macro-moves, UCT over sampled baskets,
                           greedy-N (the best basket N decisions ahead)

Every driver records per-decision tool calls; `python -m oracle.sim.capture <seed> --calls f.json`
replays a plan on Unity, which reproduces its surrogate score exactly.

Run as modules from the repo root: python -m oracle.search 7003 --algo mcts --budget 4000. Outputs
live under ARC_RESULTS_ROOT (default: benchmark_results).
"""
import os

RESULTS_ROOT = os.environ.get("ARC_RESULTS_ROOT", "benchmark_results")
