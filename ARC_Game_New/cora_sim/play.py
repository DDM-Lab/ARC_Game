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
END_TURN = ("end_turn", None)


class UnityWorld:
    """A branch point in the real game.

    clone() restores the snapshot rather than copying anything: there is one Unity process,
    and RHEA evaluates rollouts strictly one at a time, so restoring on clone puts the game
    at the branch point exactly when the next rollout is about to run."""

    __slots__ = ("env", "snapshot", "rounds_left", "basket", "turn_ended")

    def __init__(self, env, snapshot=None, rounds_left=0):
        self.env = env
        self.snapshot = snapshot if snapshot is not None else self._capture()
        self.rounds_left = rounds_left
        self.basket = []                 # actions chosen this turn, not yet executed
        self.turn_ended = False

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

    def budget_committed(self):
        """What the basket has already spent, so affordability accounts for the whole turn
        rather than pricing each action as though it were the only one."""
        from cora_sim.economy import advertised_cost_error
        return sum((a.get("cost") or 0) + advertised_cost_error(a) for a in self.basket)

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
        """The basket, plus END_TURN.

        A TURN IS A SET OF ACTIONS, NOT ONE ACTION. Measured across 5,482 real benchmark
        turns: agents take 0 actions on 62% of turns and up to 33 on others, with a median
        of 3-5 when they act at all. So the per-turn decision is a SUBSET of a ~69-element
        basket -- 2^69 possibilities -- which is why fixed-arity search behaves oddly here.

        The fix is to decompose the turn SEQUENTIALLY: the planner picks one action at a
        time and closes the turn with an explicit END_TURN. That turns a combinatorial
        choice into a sequence both RHEA and MCTS handle natively, at the cost of making
        the horizon count ACTIONS rather than rounds. It is the standard treatment for
        combinatorial action spaces, and it is also how a person actually plays: click
        several things, then hit next round.

        Consequence worth stating: with END_TURN in the set, a plan of length L no longer
        spans L rounds. A horizon that must reach the reward has to be measured in turns
        and multiplied by the expected basket size."""
        actions = [NOOP, END_TURN]
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
        budget -= world.budget_committed() if hasattr(world, "budget_committed") else 0
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
        """Accumulate into the current turn; END_TURN is what advances the round.

        Actions are held rather than executed immediately so the basket can be sorted into
        Unity's canonical order (deconstruct, build, hire, train, staff, transfer) when the
        turn closes -- the order that makes "hire then staff" work within one turn."""
        kind, payload = action
        if kind == "end_turn":
            world.turn_ended = True
            return
        if kind == "menu":
            # getattr rather than attribute access: a caller that does not model baskets
            # (a test stub, or a policy that executes immediately) still works, and gets
            # the old one-action-at-a-time behaviour rather than an AttributeError.
            basket = getattr(world, "basket", None)
            if basket is None:
                self._apply_now(world, action)
            else:
                basket.append(payload)
            return
        self._apply_now(world, action)

    def flush(self, world):
        """Execute the accumulated basket in canonical order, then clear it."""
        from cora_sim.economy import basket_order
        for menu_action in sorted(world.basket, key=basket_order):
            self._apply_now(world, ("menu", menu_action))
        world.basket = []

    def _apply_now(self, world, action):
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
    """Advance the round ONLY when the turn has been closed.

    RHEA's rollout calls step after every action; with a basket-shaped turn most of those
    actions are still filling the same turn, so stepping unconditionally would advance a
    round per action and make a 6-action plan span 6 rounds instead of one or two."""
    if not getattr(world, "turn_ended", False):
        return
    world.turn_ended = False
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
        # Commit the whole basket the plan opens with -- every action up to and including
        # the first END_TURN. Committing only plan[0] would throw away the rest of a turn
        # the search evaluated as a unit, which is a different (and worse) policy than the
        # one that was scored.
        committed = []
        for action in plan:
            if action[0] == "end_turn":
                break
            model.apply(here, action)
            committed.append(action)
        model.flush(here)
        env.advance_round()
        score = score_of(env._game_state_dict().get("rewardMetrics"))
        trace.append({"round": r, "basket": len(committed), "score": score})
        if verbose:
            print(f"    round {r:>2}  basket of {len(committed):>2}  score={score:+.4f}")
    return trace
