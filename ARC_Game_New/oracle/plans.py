"""Playing plans on the surrogate: the one rollout loop, macro expansion and the Pareto front.

A plan is a policy(rnd, sim) -> macro or None, where a macro is
(building or None, workers to hire, food rule, relocation rule, answer casework); None does
nothing that round. cora.policy_family.macro(cfg) is such a policy, so a family member plays on the
surrogate exactly as the benchmark's pareto baseline plays it in Unity.
"""
from __future__ import annotations

import statistics as st

from oracle.arc_surrogate import ArcSurrogate


def apply(sim, macro):
    """Expand a macro into the concrete per-round action the surrogate consumes."""
    b, h, food_rule, reloc_rule, cw = macro
    answer = [(i, food_rule if t.kind == "food" else reloc_rule)
              for i, t in enumerate(sim.tasks) if not (t.answered or t.resolved)]
    return {"build": b, "hire": h, "answer": answer, "casework": cw}


def play(policy, seed) -> ArcSurrogate:
    """One episode of `policy` on the surrogate seeded with `seed`; returns the finished sim."""
    sim, rnd = ArcSurrogate(seed), 0
    while not sim.done():
        macro = policy(rnd, sim)
        sim.step(apply(sim, macro) if macro else {})
        rnd += 1
    return sim


def evaluate(policy, seeds) -> tuple:
    """(mean score, mean final budget) of `policy` over `seeds` (common random numbers: compare
    plans on the same seed list)."""
    sims = [play(policy, sd) for sd in seeds]
    return st.mean(s.score() for s in sims), st.mean(s.budget for s in sims)


def pareto_front(points):
    """Non-dominated (score, budget, ...) tuples, maximising both: nothing else is at least as good
    on both and strictly better on one. Best score first."""
    front, best_budget = [], float("-inf")
    for p in sorted(points, key=lambda t: (-t[0], -t[1])):
        if p[1] > best_budget:
            front.append(p)
            best_budget = p[1]
    return front
