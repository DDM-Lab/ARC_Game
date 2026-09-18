"""RHEA (rolling-horizon evolutionary algorithm) over the surrogate.

RHEA evolves a PLAN -- a fixed-length sequence of actions -- rather than a policy. Each
generation mutates the population, evaluates every candidate by rolling the world forward
under that plan, keeps the elites, and at the end commits only the plan's FIRST action;
the rest is shifted forward to seed the next decision. That shift is why it suits this
game: consecutive rounds share most of their structure, so last round's plan is a strong
starting point for this round's.

WHAT IS AND IS NOT VALIDATED HERE.

The search itself is validated on a deterministic toy environment with a known optimum
(test_search), which is what exercises mutation, selection and the shift buffer.

Applied to CORA, it currently plans over an action model that DOES NOT YET EXIST: budget,
construction, workforce and deliveries are not ported, so no action changes the world. The
integration below therefore demonstrates that RHEA drives the surrogate correctly and how
fast it runs -- it does NOT demonstrate that it plays the game well, and any score it
reports is a placeholder. Wire a real ActionModel in when tasks.py and the economy land.

COMMON RANDOM NUMBERS. Candidates are compared on the SAME futures. Because the world's
randomness is the Unity stream and the stream is part of the state, cloning the world
clones the stream, so two candidates evaluated from one clone source see byte-identical
weather and flood -- CRN comes free and exact, with none of the usual seed bookkeeping.
Without it, plan A can beat plan B purely by drawing a calmer week.
"""
from __future__ import annotations


class ActionModel:
    """What RHEA is allowed to do, and what it is worth.

    Deliberately tiny: the search must not know anything about CORA, so the economy port
    can drop in without touching this file."""

    def legal(self, world):
        """Actions available in this state, as a sequence."""
        raise NotImplementedError

    def apply(self, world, action):
        """Mutate `world` by taking `action`, BEFORE the round advances."""
        raise NotImplementedError

    def value(self, world):
        """Score of a terminal rollout. Higher is better."""
        raise NotImplementedError


class NoOpActions(ActionModel):
    """Placeholder for CORA until the economy is ported.

    Every action is a no-op, so every plan scores the same; this exists to run the search
    against real ported dynamics and measure throughput, not to plan anything. It is a
    stub on purpose and says so, rather than pretending to be a policy."""

    def __init__(self, n_actions=4):
        self._actions = tuple(range(n_actions))

    def legal(self, world):
        return self._actions

    def apply(self, world, action):
        return

    def value(self, world):
        # Fewer flooded tiles is better. A real objective is the score components; this is
        # the only quantity the ported subset actually computes.
        return -len(world.flood.tiles)


class RHEA:
    """Rolling-horizon evolutionary search.

    `rng` is a plain random.Random for the SEARCH's own choices. It is deliberately not the
    Unity stream: drawing search randomness from the simulated stream would advance the
    game's RNG and change the very future being evaluated."""

    def __init__(self, step, model, horizon=6, population=12, generations=8,
                 elites=2, mutation_rate=0.3, scenarios=1, rng=None, crossover=0.0):
        import random
        self.crossover = crossover
        self.step = step
        self.model = model
        self.horizon = horizon
        self.population = population
        self.generations = generations
        self.elites = elites
        self.mutation_rate = mutation_rate
        self.scenarios = scenarios
        self.rng = rng or random.Random(0)
        self.rollouts = 0                 # nodes evaluated, for throughput reporting

    # ── plan representation ─────────────────────────────────────────────────────────
    def _random_plan(self, actions):
        # A model may supply whole-turn genes (actions.CoraActions); otherwise draw from
        # the flat list, as the toy and live models do.
        ra = getattr(self.model, "random_action", None)
        if ra is not None:
            return [ra(self._plan_world, self.rng) for _ in range(self.horizon)]
        return [self.rng.choice(actions) for _ in range(self.horizon)]

    def _mutate(self, plan, actions):
        child = list(plan)
        ma = getattr(self.model, "mutate_action", None)
        for i in range(len(child)):
            if self.rng.random() < self.mutation_rate:
                child[i] = (ma(self._plan_world, child[i], self.rng) if ma is not None
                            else self.rng.choice(actions))
        return child

    # ── evaluation ──────────────────────────────────────────────────────────────────
    def _rollout(self, world, plan):
        """One plan, one future. The world is cloned by the caller."""
        for action in plan:
            self.model.apply(world, action)
            self.step(world)
        self.rollouts += 1
        return self.model.value(world)

    def _fitness(self, world, plan, futures):
        """Mean value over the shared futures -- the CRN comparison."""
        total = 0.0
        for base in futures:
            total += self._rollout(base.clone(), plan)
        return total / len(futures)

    def _futures(self, world):
        """The scenario set every candidate this generation is judged on.

        Scenario i is the world with its stream advanced i*horizon draws. These are real
        xorshift states from the same generator, which is what makes them comparable; they
        are NOT a claim that Unity would be at those states at those moments. With
        scenarios=1 the search plans against the single true future, which is the right
        default while the action model cannot influence anything."""
        out = [world]
        for i in range(1, self.scenarios):
            alt = world.clone()
            for _ in range(i * self.horizon):
                alt.rng.next_uint()
            out.append(alt)
        return out

    # ── the loop ────────────────────────────────────────────────────────────────────
    def plan(self, world, seed_plan=None):
        """Return (best_plan, best_fitness) without mutating `world`."""
        actions = list(self.model.legal(world))
        if not actions:
            return [], 0.0
        self._plan_world = world           # the state the genes are sampled against
        futures = self._futures(world)

        pop = [self._shift(seed_plan, actions)] if seed_plan else []
        pop += [self._random_plan(actions) for _ in range(self.population - len(pop))]

        scored = [(self._fitness(world, p, futures), p) for p in pop]
        for _ in range(self.generations):
            scored.sort(key=lambda t: t[0], reverse=True)
            survivors = scored[:self.elites]
            children = []
            while len(children) < self.population - self.elites:
                parent = survivors[self.rng.randrange(len(survivors))][1]
                if self.crossover and len(survivors) > 1 and self.rng.random() < self.crossover:
                    other = survivors[self.rng.randrange(len(survivors))][1]
                    # uniform crossover: genes that pay off only in combination (a build
                    # here, a hire there) can meet in one child
                    parent = [a if self.rng.random() < 0.5 else b for a, b in zip(parent, other)]
                children.append(self._mutate(parent, actions))
            scored = survivors + [(self._fitness(world, c, futures), c) for c in children]

        scored.sort(key=lambda t: t[0], reverse=True)
        return scored[0][1], scored[0][0]

    def _shift(self, plan, actions):
        """The rolling part: drop the action just taken, append a fresh one."""
        ra = getattr(self.model, "random_action", None)
        return list(plan[1:]) + [ra(self._plan_world, self.rng) if ra is not None
                                 else self.rng.choice(actions)]
