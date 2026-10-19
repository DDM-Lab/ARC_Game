"""greedy-N: each turn, the basket of actions that maximises the score N decisions ahead.

The basket is searched on the surrogate (oracle.search.greedy_move: forward selection, then local
search over drops and task-answer switches), with nothing done after this turn. greedy-1 sees an
investment (build, hire, training) only as its cost, so it never builds: it is the myopic floor
that `rules` and search are measured against; greedy-2 and greedy-3 see one and two more rounds
of what a turn's actions earn.

It needs a simulator to look ahead, so it runs on the surrogate (--env sim). For a Unity score,
play it with `python -m oracle.search <seed> --algo greedy1 --plan plan.json` and replay the plan
(oracle.sim.capture --calls), as for the other search agents.
"""
from __future__ import annotations

import random

from cora.env.game import DECISIONS


def greedy(env, rnd=0, rounds_total=DECISIONS, horizon=1):
    from oracle.search import Sim, greedy_move
    if not hasattr(env, "clone"):
        raise ValueError("greedy looks ahead on the surrogate: run it with --env sim, or play it with "
                         "oracle.search --algo greedy1 and replay the plan on Unity")
    seed = max(0, getattr(env, "active_seed", 0) or 0)
    calls = greedy_move(Sim(env, rnd, "fair", random.Random(seed * 1_000_003 + rnd)), horizon=horizon)
    return {"tool_calls": calls, "note": f"greedy-{horizon}",
            "reasoning": f"best basket by the score {horizon} decision(s) ahead"}


def greedy_n(horizon):
    return lambda env, rnd=0, rounds_total=DECISIONS: greedy(env, rnd, rounds_total, horizon)
