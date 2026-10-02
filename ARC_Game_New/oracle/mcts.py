"""UCT search over per-decision choices, on the exact surrogate.

At each decision the candidate moves are what a portfolio of the shared policies would do there
(combined, greedy, build-potential, the pareto family default, nothing) plus a few random legal
baskets (bench.baselines.explore.random_basket), each a set of tool calls acted through
cora.executor. A rollout finishes the game with combined. The surrogate is deterministic given
the seed and the calls, so the tree is exact: a node IS a game state.

The result is a plan -- per-decision tool calls, the capture.py --calls format -- with the score
it gets, which replaying it on Unity reproduces:

    python -m oracle.mcts 5503 --iterations 2000 --calls best_5503.json
    python -m oracle.sim.capture 5503 --calls best_5503.json          # on Unity, unsandboxed
"""
from __future__ import annotations

import argparse
import json
import math
import random
import sys
import time

from oracle.rollout import DECISIONS, make_policy

PORTFOLIO = ("combined", "greedy", "build-potential", "pareto", "noop")


def _execute(env, calls):
    from cora import executor
    _, (_, _r, term, trunc, _i) = executor.execute_turn(env, calls)
    return term or trunc


def candidates(env, decision, rng, n_random=3):
    """Distinct candidate moves at this decision: [(label, calls)]."""
    from bench.baselines.common import tool_calls
    from bench.baselines.explore import random_basket
    out, seen = [], set()
    for name in PORTFOLIO:
        calls = tool_calls(env, make_policy({"name": name}, 0)(env, decision, DECISIONS))
        key = json.dumps(calls, sort_keys=True)
        if key not in seen:
            seen.add(key)
            out.append((name, calls))
    for k in range(n_random):
        calls = tool_calls(env, random_basket(env, rng))
        key = json.dumps(calls, sort_keys=True)
        if key not in seen:
            seen.add(key)
            out.append((f"random{k}", calls))
    return out


def _score(env):
    from cora.scoring import score_components
    return score_components(env.game_state.get("rewardMetrics"))["score"]


def rollout(env, decision):
    """Finish the game from `env` (at `decision`) with combined: (score, the calls it made)."""
    from bench.baselines.common import tool_calls
    policy, calls = make_policy({"name": "combined"}, 0), []
    for i in range(decision, DECISIONS):
        turn = tool_calls(env, policy(env, i, DECISIONS))
        calls.append(turn)
        if _execute(env, turn):
            break
    return _score(env), calls


class Node:
    __slots__ = ("env", "decision", "terminal", "children", "untried", "visits", "total", "rng",
                 "calls_taken")

    def __init__(self, env, decision, terminal, rng, calls_taken=None):
        self.env, self.decision, self.terminal, self.rng = env, decision, terminal, rng
        self.children, self.untried, self.visits, self.total = {}, None, 0, 0.0
        self.calls_taken = calls_taken           # the move that led here


def search(seed, iterations=1000, c=0.05, n_random=3, search_seed=0, budget_s=None):
    """(best score, best plan's calls, stats). The plan is the best FULL trajectory any rollout
    reached, so its score is exact, not an estimate."""
    from oracle.sim.env import SimEnv
    env = SimEnv(seed=seed, max_episode_steps=DECISIONS + 4, manual_transfers=False)
    env.reset()
    root = Node(env, 0, False, random.Random(f"{search_seed}:{seed}:root"))
    best = (-math.inf, None)
    t0 = time.time()
    for it in range(iterations):
        if budget_s and time.time() - t0 > budget_s:
            break
        node, path, plan = root, [root], []
        # selection
        while not node.terminal:
            if node.untried is None:
                node.untried = candidates(node.env, node.decision, node.rng, n_random)
            if node.untried:
                break
            label, child = max(node.children.items(), key=lambda kv: kv[1].total / kv[1].visits
                               + c * math.sqrt(math.log(node.visits) / kv[1].visits))
            plan.append(child.calls_taken)
            node = child
            path.append(node)
        # expansion
        if not node.terminal and node.untried:
            label, calls = node.untried.pop(0)
            env2 = node.env.clone()
            done = _execute(env2, calls)
            child = Node(env2, node.decision + 1, done or node.decision + 1 >= DECISIONS,
                         random.Random(f"{search_seed}:{seed}:{node.decision + 1}:{label}:{it}"), calls)
            node.children[label] = child
            plan.append(calls)
            node = child
            path.append(node)
        # simulation
        if node.terminal:
            value, tail = _score(node.env), []
        else:
            value, tail = rollout(node.env.clone(), node.decision)
        if value > best[0]:
            best = (value, plan + tail)
        for n in path:
            n.visits += 1
            n.total += value
    return best[0], best[1], {"iterations": it + 1, "seconds": round(time.time() - t0, 1),
                              "root_children": len(root.children)}



def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("seed", type=int)
    ap.add_argument("--iterations", type=int, default=1000)
    ap.add_argument("--seconds", type=float, default=None, help="wall-clock cap")
    ap.add_argument("--random", type=int, default=3, help="random baskets per decision")
    ap.add_argument("--calls", help="write the best plan (capture.py --calls)")
    a = ap.parse_args(argv)
    base = rollout(_start(a.seed), 0)[0]
    score, plan, stats = search(a.seed, a.iterations, n_random=a.random, budget_s=a.seconds)
    print(json.dumps({"seed": a.seed, "combined": round(base, 4), "best": round(score, 4), **stats}))
    if a.calls:
        json.dump([[list(c) for c in turn] for turn in plan], open(a.calls, "w"))
    return 0


def _start(seed):
    from oracle.sim.env import SimEnv
    env = SimEnv(seed=seed, max_episode_steps=DECISIONS + 4, manual_transfers=False)
    env.reset()
    return env


if __name__ == "__main__":
    sys.exit(main())
