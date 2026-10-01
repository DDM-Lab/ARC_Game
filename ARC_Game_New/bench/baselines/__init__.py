"""Non-learning baseline policies (no model, no API). Each is policy(env, rnd, rounds_total) -> a
decision on the env's current state.

    greedy           myopic task answers + staffing; never builds (the under-investment floor)
    build-potential  greedy + building toward anticipated demand
    combined         long-term-value task answers + build-potential's building
    pareto           a member of the surrogate's policy family (cora.policy_family)
"""
from bench.baselines.build_potential import build_potential
from bench.baselines.combined import combined
from bench.baselines.greedy import greedy
from bench.baselines.pareto import pareto

POLICIES = {
    "greedy": greedy,
    "build-potential": build_potential,
    "combined": combined,
    "pareto": pareto,
}
