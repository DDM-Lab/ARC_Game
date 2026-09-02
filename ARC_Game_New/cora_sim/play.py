"""RHEA playing CORA for real score, through the native Unity snapshot.

WHY AGAINST UNITY AND NOT THE SURROGATE. The surrogate reproduces the whole stochastic
surface of a round exactly (flood, weather, probability triggers) but not yet the economy,
so it cannot score a plan. Unity can. Branching Unity is ~1000x slower per node than the
surrogate will be, but it is REAL: the score this reports is the game's own score, from the
same rewardMetrics the benchmark uses, run through the same reward_scoring.py the RL gym
and the live router use. So this measures planning quality today, and the surrogate becomes
a drop-in speed optimisation behind the same ActionModel interface rather than a
prerequisite for it.

THE SNAPSHOT IS THE ENABLER. Search needs to branch: evaluate a plan, rewind, evaluate
another from the same point. SearchableEnv.save_state replays the whole action journal,
which costs more every round and would make deep search quadratic. The native save_state /
load_state RPCs restore Unity's own state object in O(1), which is what they were built
for -- and their trajectory-equivalence harness (9/9) is what makes rewinding trustworthy
rather than merely fast.

WHAT AN ACTION IS. One of: a menu action from Unity's own enumeration (build, hire, train,
transfer), answering an active task's choice, or explicit no-op. Task choices matter most:
food, lodging and casework fulfilment all flow through them, which is why a policy that
only builds and hires scores nothing -- measured, and the reason the first scripted capture
in this session produced all-zero fulfilment counters.
"""
from __future__ import annotations

import json

from reward_scoring import compute_score_components   # the objective the benchmark uses


def score_of(metrics):
    """The composite score, from the SAME function the benchmark and the RL gym use.

    compute_score() returns a 3-tuple; taking [2] by index is how that becomes a scalar
    objective. Reusing this rather than reimplementing is deliberate -- a surrogate that
    optimises a slightly different score is worse than useless, because it would look like
    it was planning well."""
    return compute_score_components(metrics or {})["score"]

NOOP = ("noop", None)


class UnityWorld:
    """A branch point in the real game.

    clone() restores the snapshot rather than copying anything: there is one Unity process,
    and RHEA evaluates rollouts strictly one at a time, so restoring on clone puts the game
    at the branch point exactly when the next rollout is about to run."""

    __slots__ = ("env", "snapshot", "rounds_left")

    def __init__(self, env, snapshot=None, rounds_left=0):
        self.env = env
        self.snapshot = snapshot if snapshot is not None else self._capture()
        self.rounds_left = rounds_left

    def _capture(self):
        resp = self.env._send_request({"type": "save_state"})
        state = resp.get("state")
        if state is None:
            raise RuntimeError(f"save_state returned no state: {resp}")
        return state

    def restore(self):
        resp = self.env._send_request({"type": "load_state", "state": self.snapshot})
        if resp.get("type", "").startswith("error"):
            raise RuntimeError(f"load_state failed: {resp}")

    def clone(self):
        self.restore()
        return UnityWorld(self.env, self.snapshot, self.rounds_left)

    def metrics(self):
        return self.env._game_state_dict().get("rewardMetrics") or {}


class UnityActions:
    """CORA's real action space, as Unity enumerates it.

    `max_menu` caps how many menu actions RHEA considers per decision. The full list is ~69
    and mostly near-duplicates (the same build on 15 sites), so an uncapped branching factor
    spends the search budget distinguishing options that barely differ. Task choices are
    never capped -- they are where the score comes from."""

    def __init__(self, env, max_menu=8, rng=None):
        import random
        self.env = env
        self.max_menu = max_menu
        self.rng = rng or random.Random(0)

    def legal(self, world):
        actions = [NOOP]
        state = world.env._game_state_dict()
        for task in (state.get("allActiveTasks") or []):
            for choice in (task.get("choices") or []):
                actions.append(("choice", (task.get("taskId"), choice.get("choiceId"))))
        menu = world.env.get_valid_actions() or []
        budget = (state.get("satisfactionAndBudget") or {}).get("budget", 0)
        affordable = [i for i, a in enumerate(menu) if (a.get("cost") or 0) <= budget]
        if len(affordable) > self.max_menu:
            affordable = self.rng.sample(affordable, self.max_menu)
        actions += [("menu", i) for i in affordable]
        return actions

    def apply(self, world, action):
        kind, payload = action
        try:
            if kind == "menu":
                world.env.execute(json.dumps({"actionIndex": payload}))
            elif kind == "choice":
                world.env.choose(payload[0], payload[1])
        except Exception:
            # An action that Unity rejects is a legal thing for a planner to try; treat it
            # as a no-op rather than aborting the rollout. Rejections are informative --
            # the plan simply scores as if nothing happened.
            pass

    def value(self, world):
        return score_of(world.metrics())


def step_unity(world):
    world.env.advance_round()


def play_episode(env, rounds=12, horizon=3, population=6, generations=2, seed=0,
                 max_menu=8, verbose=True):
    """Play `rounds` rounds with RHEA, returning the score trace.

    Deliberately small search settings: every node is a real Unity round, so the budget
    buys tens of rollouts per decision, not thousands. The point here is that the loop is
    real and the score is the game's own -- the surrogate is what makes the budget large."""
    import random
    from cora_sim.search import RHEA

    model = UnityActions(env, max_menu=max_menu, rng=random.Random(seed))
    search = RHEA(step_unity, model, horizon=horizon, population=population,
                  generations=generations, elites=2, mutation_rate=0.3,
                  rng=random.Random(seed))
    trace, plan = [], None
    for r in range(rounds):
        here = UnityWorld(env)
        plan, _ = search.plan(here, seed_plan=plan)
        here.restore()                       # undo the search's exploration
        model.apply(here, plan[0])           # commit only the first action
        env.advance_round()
        score = score_of(env._game_state_dict().get("rewardMetrics"))
        trace.append({"round": r, "action": str(plan[0]), "score": score})
        if verbose:
            print(f"    round {r:>2}  {str(plan[0]):<28} score={score:+.4f}")
    return trace
