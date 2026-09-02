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

    def __init__(self, env, max_menu=8, rng=None, shaping=0.0):
        import random
        self.env = env
        self.max_menu = max_menu
        self.rng = rng or random.Random(0)
        self.shaping = shaping

    def legal(self, world):
        actions = [NOOP]
        state = world.env._game_state_dict()
        for task in (state.get("allActiveTasks") or []):
            for choice in (task.get("choices") or []):
                actions.append(("choice", (task.get("taskId"), choice.get("choiceId"))))
        menu = world.env.get_valid_actions() or []
        budget = (state.get("satisfactionAndBudget") or {}).get("budget", 0)

        # Carry the whole action DICT, never an index into this round's menu. Unity
        # re-enumerates every round, so an index captured now means something different
        # two rounds into a rollout -- the planner would evaluate one action and commit a
        # different one. execute_action wants the dict anyway.
        #
        # The advertised `cost` under-reports construction: a build advertises 1000 and
        # deducts 2000 (measured). Affordability is therefore checked against the DEDUCTED
        # cost, or the planner will queue builds it cannot pay for.
        affordable = [a for a in menu if self.true_cost(a) <= budget]
        by_family = {}
        for a in affordable:
            by_family.setdefault(self.family(a), []).append(a)

        # Stratify, don't uniformly sample. The menu is ~69 entries of which ~45 are the
        # same three building types on 15 interchangeable sites, so a uniform draw spends
        # the budget distinguishing near-duplicates and regularly drops transfers entirely
        # -- and transfers are the highest-value actions, since draining the motel saves
        # $200/person/day for the rest of the game.
        per_family = max(1, self.max_menu // max(1, len(by_family)))
        for family, group in sorted(by_family.items()):
            actions += [("menu", a) for a in self._span(family, group, per_family)]
        return actions

    @staticmethod
    def family(action):
        """Semantic category: type plus, for builds, the building kind. Sites collapse."""
        kind = action.get("action_type")
        if kind == "construction":
            return f"build:{(action.get('construction') or {}).get('building_type', '?')}"
        if kind == "worker":
            return f"worker:{(action.get('worker') or {}).get('worker_action_type', '?')}"
        return str(kind)

    @staticmethod
    def true_cost(action):
        """What the budget is ACTUALLY charged.

        Measured against the live build: hire and train deduct exactly their advertised
        cost, but construction deducts BuildingSystem's per-type scene value (2000) rather
        than the 1000 the action advertises. The advertised field is what every LLM and
        every scripted policy in this repo sees, so this correction lives here rather than
        being silently assumed."""
        cost = action.get("cost") or 0
        if action.get("action_type") == "construction":
            return cost * 2
        return cost

    def _span(self, family, group, k):
        """Pick k options that actually differ.

        Within a family the only axis is an integer quantity (hire 1..5, transfer 5/10/20)
        or an interchangeable site. Endpoints plus a middle value cover the trade-off; the
        search cannot meaningfully tell hire_3 from hire_4."""
        if len(group) <= k:
            return group
        group = sorted(group, key=self.true_cost)
        if k == 1:
            return [group[0]]
        idx = sorted({round(i * (len(group) - 1) / (k - 1)) for i in range(k)})
        return [group[i] for i in idx]

    def apply(self, world, action):
        kind, payload = action
        try:
            if kind == "menu":
                # The FULL action dict is the payload execute_action expects. Sending
                # {"actionIndex": i} is accepted by the socket and silently does nothing --
                # which is how a whole capture run produced all-zero counters while looking
                # like it was playing.
                world.env.execute(json.dumps(payload))
            elif kind == "choice":
                world.env.choose(payload[0], payload[1])
        except Exception:
            # An action that Unity rejects is a legal thing for a planner to try; treat it
            # as a no-op rather than aborting the rollout. Rejections are informative --
            # the plan simply scores as if nothing happened.
            pass

    def value(self, world):
        """The game's own score, optionally plus a dense shaping term.

        WHY SHAPING IS NEEDED HERE. Measured on this build: the composite score is exactly
        0.0000 for the first fourteen rounds of a 16-round episode under both idle and
        random play, then steps to its final value. Nothing a planner does in rounds 0-13
        changes the number it is being scored on, so a search with a horizon shorter than
        the resolution latency optimises a CONSTANT -- every candidate ties, selection is
        arbitrary, and the plan it commits is noise. That is exactly what the first run
        did: it picked menu action 0 twice in a row.

        The sparsity is structural, not a bug. sat_worker_use accumulates every round and
        is the one dense term, but it is unreachable until buildings exist to staff, and
        construction takes four days. Food and lodging only move when tasks resolve.

        `shaping` adds leading indicators of the counters -- people housed, workers
        actually working, food positioned -- scaled small so it breaks ties without
        outranking real score. It is OFF by default: the reported score must stay the
        game's own, and a planner tuned on a proxy should never be presented as one that
        beat the real objective."""
        score = score_of(world.metrics())
        if not self.shaping:
            return score
        return score + self.shaping * self._potential(world)

    def _potential(self, world):
        """Leading indicators, in the units the counters will eventually credit.

        Deliberately built only from quantities that FEED rewardMetrics: people housed
        become lodgingFulfilled, working workers become cumWorkingWorkers, food packs on
        site become foodFulfilled. Nothing here rewards activity for its own sake."""
        state = world.env._game_state_dict()
        wf = state.get("workforceState") or {}
        working = (wf.get("workingTrainedWorkers", 0) or 0) + (wf.get("workingUntrainedWorkers", 0) or 0)
        housed = food = 0
        for f in ((state.get("mapState") or {}).get("facilities") or []):
            res = f.get("resources") or {}
            if f.get("buildingType") in ("Motel", "Shelter"):
                housed += res.get("population", 0) or 0
            food += res.get("foodPacks", 0) or 0
        return 0.001 * housed + 0.01 * working + 0.001 * food


def step_unity(world):
    world.env.advance_round()


def play_episode(env, rounds=12, horizon=3, population=6, generations=2, seed=0,
                 max_menu=8, shaping=0.0, verbose=True):
    """Play `rounds` rounds with RHEA, returning the score trace.

    Deliberately small search settings: every node is a real Unity round, so the budget
    buys tens of rollouts per decision, not thousands. The point here is that the loop is
    real and the score is the game's own -- the surrogate is what makes the budget large."""
    import random
    from cora_sim.search import RHEA

    model = UnityActions(env, max_menu=max_menu, rng=random.Random(seed), shaping=shaping)
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
