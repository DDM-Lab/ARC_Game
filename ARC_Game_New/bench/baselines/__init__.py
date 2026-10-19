"""Non-learning baseline policies (no model, no API). Each is policy(env, rnd, rounds_total) -> a
decision on the env's current state.

    greedy-N         the basket that maximises the score N decisions ahead, on the surrogate
                     (greedy-1 never builds: the under-investment floor)
    rules            rules for study build 1.0.2 through the model's own tool calls (rules.PARAMS)
"""
from bench.baselines.greedy import greedy_n
from bench.baselines.rules import rules

POLICIES = {
    "greedy-1": greedy_n(1),
    "greedy-2": greedy_n(2),
    "greedy-3": greedy_n(3),
    "rules": rules,
}
