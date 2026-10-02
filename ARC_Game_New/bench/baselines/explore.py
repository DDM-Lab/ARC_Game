"""explore: a base policy that, on a random EPSILON of decisions, plays a random basket instead.

The surrogate's parity test and its search both need trajectories off the baselines' beaten
path: a policy that always plays `combined` only ever exercises the states `combined` reaches.
A random basket is what the game's own menu allows -- a random answer to each open task (or
none) plus a handful of random menu actions -- so every mechanic a player can trigger gets
exercised, and the base policy keeps the trajectory realistic in between.

    policy = explore(POLICIES["combined"], epsilon=0.05, seed=7)
    dec = policy(env, decision, rounds)          # same shape as every baseline's decision
"""
from __future__ import annotations

import random

_MENU_TYPES = ("construction", "worker", "worker_assignment", "deconstruction", "resource_transfer")


def random_basket(env, rng: random.Random, max_actions: int = 4) -> dict:
    """A random legal decision: each open task answered with a random choice half the time,
    plus 1..max_actions distinct random menu actions."""
    choices = []
    for t in (env.game_state or {}).get("allActiveTasks") or []:
        opts = [c.get("choiceId") for c in t.get("choices") or []]
        if opts and rng.random() < 0.5:
            choices.append({"taskId": t["taskId"], "choiceId": rng.choice(opts)})
    menu = [i for i, a in enumerate(env.valid_actions or []) if a.get("action_type") in _MENU_TYPES]
    k = min(len(menu), rng.randint(1, max_actions))
    return {"choices": choices, "actions": sorted(rng.sample(menu, k)) if k else []}


def explore(base, epsilon: float = 0.05, seed: int = 0):
    """`base` with probability 1 - epsilon, a random basket otherwise (one seeded stream per
    policy instance, so a trajectory is reproducible from its seed)."""
    rng = random.Random(seed)

    def policy(env, rnd, rounds):
        if rng.random() < epsilon:
            return random_basket(env, rng)
        return base(env, rnd, rounds)
    return policy
