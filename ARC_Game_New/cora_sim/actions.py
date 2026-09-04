"""The surrogate's action model for search: what a turn IS, what it may contain, and what
it is worth. Sits behind search.ActionModel so RHEA (and later MCTS) need not know CORA.

A TURN IS A GENE, NOT AN INDEX. play.UnityActions decomposes a turn into a sequence of
single actions closed by END_TURN, because the live game is stepped by hand and a plan can
afford to spend its horizon on that. The surrogate runs ~7,000 steps a second, so here a
plan entry is one whole round:

    ("turn", {"choices": {task_spec_id: choice_id, ...}, "menu": (action_id, ...)})

`choices` is keyed by task TYPE (the exported taskId such as Community_TransportRequest),
not by the numeric id of a task that may not exist yet when the plan is made. At apply time
every open task looks up its type; an unmapped type takes its first offered choice, exactly
what the replay harness does. `menu` is a small set of menu action ids from the live grammar:

    build_<Kitchen|Shelter|CaseworkSite>_<site>   advertised $1000, charged $2000
    hire_untrained_<1..5>  $100 each              hire_trained_<1..5>  $300 each
    train_workers_<1..5>   $500 each              staff_<building>     $0
    transfer_population_<community>_Motel_<5|10|20>  $0  -- EXCLUDED, see below

Prices are the ones the live menu ADVERTISES (dumped from the headless build), because
that is what the economy deducts for hires and training; construction deducts the scene's
$2000 regardless (Unity bug #5, reproduced).

PRUNING is only ever provable: pruning.prune drops dead and dominated actions (built-on
sites, unaffordable, untrainable, unstaffable), and _span keeps the endpoints and middle of
each quantity family, since the search cannot tell hire_3 from hire_4. Nothing is dropped
for looking silly.

TRANSFERS ARE OFF BY DEFAULT. The surrogate's menu-transfer path only bumps a side counter
at round end: no population leaves the community, no clients spawn, and motel billing reads
the building, not that counter. No capture has exercised it, so a search allowed to use it
would be optimising a hole. Enable `allow_transfers` only once a capture with transfers has
been replayed exactly.
"""
from __future__ import annotations

from .economy import (REQUIRED_WORKFORCE, STATUS_NEED_WORKER, action_from_id, apply_action,
                      basket_order)
from .pruning import prune
from .search import ActionModel

ADVERTISED = {"untrained": 100, "trained": 300, "train": 500, "build": 1000}   # live menu
BUILD_TYPES = ("Kitchen", "Shelter", "CaseworkSite")
QUANTITIES = (1, 2, 3, 4, 5)
TRANSFER_QUANTITIES = (5, 10, 20)
REPAIR_CHOICE = 1                       # "Vehicle Repair Required": 1 repairs, 2 delays
STAFF_ALL = "staff_all"                 # symbolic: staff whatever needs workers this turn


def _score(world):
    import reward_scoring
    return reward_scoring.compute_score(world.economy.metrics())[2]


def _components(world):
    import reward_scoring
    return reward_scoring.compute_score_components(world.economy.metrics())


class CoraActions(ActionModel):
    def __init__(self, rng, max_menu=16, max_per_turn=2, allow_transfers=False,
                 shaping=0.0, idle_turn_rate=0.62, auto_staff=True):
        self.auto_staff = auto_staff
        # 62% of real benchmark turns take no menu action at all (5,482 turns measured in
        # play.py); random genes follow that so a fresh population is not all spenders.
        self.idle_turn_rate = idle_turn_rate
        self.rng = rng
        self.max_menu = max_menu
        self.max_per_turn = max_per_turn
        self.allow_transfers = allow_transfers
        self.shaping = shaping
        self._specs = None
        self._static = None

    # ── the alphabet ────────────────────────────────────────────────────────────────
    def task_specs(self):
        """(spec_id -> [choice ids]) for every task type that has choices."""
        if self._specs is None:
            from .sim import _TASK_SPEC
            self._specs = {sid: [c.get("choiceId") for c in (spec.get("choices") or [])]
                           for sid, spec in _TASK_SPEC.items() if spec.get("choices")}
        return self._specs

    def basket(self, world, span=True):
        """Pruned menu action dicts legal in THIS state, in Unity's basket order; spanned
        to max_menu for sampling, unspanned (span=False) for checking legality."""
        econ = world.economy
        used = econ.used_sites
        if self._static is None:
            # The build/hire/train dicts never depend on state; only which are legal does.
            sites = sorted(world.fmap_sites() if hasattr(world, "fmap_sites") else _site_ids(world))
            self._static = ([(s, [action_from_id(f"build_{bt}_{s}", ADVERTISED["build"]) for bt in BUILD_TYPES])
                             for s in sites],
                            [action_from_id(f"{kind}_{q}", ADVERTISED[key] * q) for q in QUANTITIES
                             for kind, key in (("hire_untrained", "untrained"), ("hire_trained", "trained"),
                                               ("train_workers", "train"))])
        raw = []
        for s, builds in self._static[0]:
            if s not in used:
                raw.extend(builds)
        raw.extend(self._static[1])
        for i, b in enumerate(econ.buildings):
            if econ.can_staff(i) and b.get("assigned", 0) < REQUIRED_WORKFORCE \
                    and b.get("status") == STATUS_NEED_WORKER:
                raw.append({"action_type": "worker_assignment", "cost": 0,
                            "action_id": f"staff_{b['name']}",
                            "assignment": {"building_name": b["name"],
                                           "quantity": REQUIRED_WORKFORCE}})
        if self.allow_transfers:
            for b in econ.buildings:
                if b.get("type") == "Community" and (b.get("resources") or {}).get("population", 0) > 0:
                    for q in TRANSFER_QUANTITIES:
                        raw.append(action_from_id(f"transfer_population_{b['name']}_Motel_{q}", 0))
        kept, _dropped = prune(raw, econ=econ, budget=econ.budget)
        return self._span_families(kept) if span else kept

    def _span_families(self, actions):
        """At most max_menu actions, spread across families, endpoints + middle within one."""
        fams = {}
        for a in actions:
            fams.setdefault(_family(a), []).append(a)
        per = max(1, self.max_menu // max(1, len(fams)))
        out = []
        for fam in sorted(fams):
            group = sorted(fams[fam], key=_true_cost)
            if len(group) > per:
                # endpoints first, then the middle: hire_1 and hire_5 before hire_3
                idx = []
                for i in (0, len(group) - 1, len(group) // 2):
                    if i not in idx:
                        idx.append(i)
                group = [group[i] for i in sorted(idx[:per])]
            out.extend(group)
        out.sort(key=basket_order)
        return out

    # ── ActionModel ─────────────────────────────────────────────────────────────────
    def legal(self, world):
        """The alphabet, as a list of sample turns -- RHEA's generic path needs a list.
        With the hooks below RHEA never indexes it beyond an emptiness check."""
        return [self.random_action(world, self.rng) for _ in range(4)]

    def random_action(self, world, rng):
        specs = self.task_specs()
        choices = {sid: rng.choice(cids) for sid, cids in specs.items() if cids}
        menu = [a["action_id"] for a in self.basket(world)] + [STAFF_ALL]
        k = 0 if rng.random() < self.idle_turn_rate else rng.randint(1, max(1, min(self.max_per_turn, len(menu))))
        picked = tuple(sorted(rng.sample(menu, min(k, len(menu))))) if k and menu else ()
        return ("turn", {"choices": choices, "menu": picked})

    @staticmethod
    def baseline_plan(horizon):
        """The replay harness's policy as a plan: first offered choice, no menu actions.
        Seed every search with it so evolution can never return something worse."""
        return [("turn", {"choices": {}, "menu": ()}) for _ in range(horizon)]

    def mutate_action(self, world, action, rng):
        kind, g = action
        choices, menu = dict(g["choices"]), list(g["menu"])
        specs = self.task_specs()
        r = rng.random()
        if r < 0.5 and specs:
            sid = rng.choice(list(specs))
            if specs[sid]:
                choices[sid] = rng.choice(specs[sid])
        else:
            basket = [a["action_id"] for a in self.basket(world)] + [STAFF_ALL]
            if menu and (r < 0.75 or not basket):
                menu.remove(rng.choice(menu))
            elif basket:
                pick = rng.choice(basket)
                if pick not in menu:
                    menu.append(pick)
                    if len(menu) > self.max_per_turn:
                        menu.remove(rng.choice(menu))
        return ("turn", {"choices": choices, "menu": tuple(sorted(menu))})

    def apply(self, world, action):
        from . import sim as S
        kind, g = action
        if kind != "turn":
            return
        choices = g["choices"]
        offered = {}
        for tid, cid in S.open_choices(world):
            offered.setdefault(tid, []).append(cid)
        for tid, cids in offered.items():
            if tid in world.tasks.repair_for:
                S.answer(world, tid, REPAIR_CHOICE)
                continue
            entry = world.generated_specs.get(tid)
            want = choices.get(entry[0]) if entry else None
            S.answer(world, tid, want if want in cids else cids[0])
        if self.auto_staff:
            # An unstaffed building is pure cost, so "build and never staff" is dominated by
            # "build and staff when ready" in every respect; taking the staffing off the
            # genome removes a valley the search would otherwise have to cross blind (build
            # this turn, staff four rounds later, hire in between). Done directly rather
            # than through the basket: this runs every turn of every rollout.
            econ = world.economy
            for i, b in enumerate(econ.buildings):
                if b.get("status") == STATUS_NEED_WORKER and econ.can_staff(i) \
                        and b.get("assigned", 0) < REQUIRED_WORKFORCE:
                    econ.staff(i, count=REQUIRED_WORKFORCE)
        done = []
        if not g["menu"]:
            return done
        legal_now = {a["action_id"]: a for a in self.basket(world, span=False)}
        for aid in g["menu"]:
            if aid == STAFF_ALL:
                # Staff every building that is waiting for workers, in list order. A gene
                # sampled at plan time cannot name a building that does not exist yet, and
                # staffing is what every winning strategy does the round a build completes.
                for a in legal_now.values():
                    if a.get("action_type") == "worker_assignment":
                        apply_action(world.economy, a); done.append(a["action_id"])
                continue
            a = legal_now.get(aid)
            if a is None:
                continue                      # not legal in this state: the game ignores it
            if _true_cost(a) > world.economy.budget:
                continue
            apply_action(world.economy, a); done.append(aid)
        return done

    def value(self, world):
        v = _score(world)
        if self.shaping:
            m = world.economy.metrics()
            v += self.shaping * (m.get("cumWorkingWorkers", 0) / max(1, m.get("roundsCompleted", 1)))
        return v

    def components(self, world):
        return _components(world)


def _family(a):
    kind = a.get("action_type")
    if kind == "construction":
        return f"build:{(a.get('construction') or {}).get('building_type', '?')}"
    if kind == "worker":
        return f"worker:{(a.get('worker') or {}).get('worker_action_type', '?')}"
    return str(kind)


def _true_cost(a):
    cost = a.get("cost") or 0
    return cost * 2 if a.get("action_type") == "construction" else cost


def _site_ids(world):
    from .roads import DEFAULT_MAP
    return list(DEFAULT_MAP.site_cell.keys())
