"""Search: traditional algorithms over game policies, on a fast surrogate of the game.

    arc_surrogate.py       the seeded surrogate (fast, flat Python; validated against Unity)
    plans.py               playing plans on it: one rollout loop, macro expansion, Pareto front
    pareto_sweep.py        the score-vs-budget frontier over cora.policy_family
    mcts_oracle.py         UCT search over per-round macros (an upper-bound estimate)
    policy_compare.py      engine agreement: the same policies on Unity and on the surrogate
    validate_surrogate.py  calibration gate against recorded Unity runs (non-zero exit on failure)
    legacy_score.py        the pre-export Python score the surrogate still uses (until ported)

Run as modules from the repo root: python -m oracle.pareto_sweep --seeds 60 --jobs 32. Outputs and
the Unity runs they compare against live under ARC_RESULTS_ROOT (default: benchmark_results).
"""
import os

RESULTS_ROOT = os.environ.get("ARC_RESULTS_ROOT", "benchmark_results")
