"""Budget, workforce, construction and motel billing -- the deterministic half of CORA.

WHY THIS IS EASIER THAN FLOOD, AND WHY THAT IS A MEASURED CLAIM RATHER THAN A HOPE.
The draw census chains each round's exit RNG state to the next round's entry state and
accounts for every random number with flood, Weather.select and TaskTrigger.probability
alone. Nothing here draws. So this module needs ARITHMETIC fidelity, not stream fidelity:
there is no draw ordering to discover and no risk that a mis-ported cost silently shifts
every mechanic downstream, which is the failure mode that made the RNG value() bug so
expensive. (Caveat, stated because it was got wrong once: that census currently covers
idle episodes. Action-bearing re-measurement is in flight.)

THE SPEC IS FOUR FILES, NOT THE WHOLE GAME. Every one of the 16 rewardMetrics counters
funnels through RewardMetricsTracker; the four spend counters come from
SatisfactionAndBudget.RemoveBudget(amount, SpendCategory). So the increment sites plus
their guards ARE the specification, and nothing outside them can move the score.

COSTS COME FROM THE PATH THAT EXECUTES, NOT FROM THE MENU. Measured against the live
build: hire and train deduct exactly the cost their action advertises, but a build
advertises 1000 and deducts 2000, because BuildingSystem ignores action.cost and uses its
own per-type scene value. This module deducts what the GAME deducts, and
`advertised_cost_error()` exists so a caller can see the gap rather than inherit it
silently.
"""
from __future__ import annotations

import json as _json
import os as _os

_CONST_PATH = _os.path.join(_os.path.dirname(_os.path.abspath(__file__)),
                            "corpus", "sim_constants.json")

# BuildingSystem's spend category per building type. Kitchens are food-service spend,
# shelters lodging, casework sites casework -- so the same $2000 lands in a different
# score term depending on what you build, which is a real strategic distinction and not
# bookkeeping.
SPEND_CATEGORY = {"Kitchen": "food", "Shelter": "lodging", "CaseworkSite": "casework"}

# A task choice's COST is attributed by the TASK'S TAG, not by what the choice does
# (TaskDetailUI): Food-tagged tasks charge foodSpend, Lodging-tagged charge lodgingSpend,
# everything else is uncategorised and moves the budget without moving any spend counter.
# Getting this wrong nets out in the budget and silently corrupts cost-efficiency, which is
# the half of the score that is hardest to notice being wrong.
TAG_CATEGORY = {"Food": "food", "Lodging": "lodging"}

# BUILDING STATUS LIFECYCLE (Building.cs). A building is NOT usable the moment it is paid
# for, and this is the trap the surrogate previously fell into by tracking only a countdown:
#
#   UnderConstruction  -> for `constructionRounds` rounds. Cannot be staffed at all;
#                         UpdateWorkerStatus only acts on NeedWorker/InUse.
#   NeedWorker         -> construction finished, still not operational.
#   InUse              -> assigned workforce >= requiredWorkforce (4). IsOperational() is
#                         EXACTLY this state, and it is what resource triggers, task
#                         suitability and deliveries all check.
#
# So a building bought on round 1 cannot be staffed until round 5 and cannot serve anyone
# until it is staffed. Prebuilt buildings (communities, the motel) skip the whole lifecycle.
# Workforce is counted in UNITS, not heads: a trained worker is worth 2, an untrained 1.
# Prebuilt buildings never enter the lifecycle: they have no status in the export, are
# always suitable for tasks, and cannot be staffed or deconstructed. Constructed ones walk
# the whole thing.
STATUS_PREBUILT = "Prebuilt"
STATUS_DECONSTRUCTING = "Deconstructing"
STATUS_DISABLED = "Disabled"
STATUS_UNDER_CONSTRUCTION = "UnderConstruction"
STATUS_NEED_WORKER = "NeedWorker"
STATUS_IN_USE = "InUse"
REQUIRED_WORKFORCE = 4
WORKFORCE_VALUE = {"trained": 2, "untrained": 1}

COUNTERS = ("foodResolved", "foodFulfilled", "lodgingResolved", "lodgingFulfilled",
            "caseworkRequested", "caseworkProcessed", "cumWorkingWorkers",
            "cumTrainingWorkers", "cumIdleWorkers", "roundsCompleted", "daysCompleted",
            "totalWorkers", "foodSpend", "lodgingSpend", "workerSpend", "caseworkSpend")


def load_economy_constants(path=None):
    d = _json.load(open(path or _CONST_PATH))
    missing = [k for k in ("construction", "motel", "workers") if k not in d]
    if missing:
        raise KeyError(f"sim_constants.json lacks {missing} -- re-export it from a build "
                       f"that includes the economy block; do not hand-write these")
    c, m, w = d["construction"], d["motel"], d["workers"]
    return {
        "construction_rounds": int(c["rounds"]),
        "build_cost": {"Shelter": int(c["shelterCost"]), "Kitchen": int(c["kitchenCost"]),
                       "CaseworkSite": int(c["caseworkSiteCost"])},
        "motel_per_person_per_day": float(m["costPerPersonPerDay"]),
        "untrained_cost": int(w["untrainedCost"]),
        "trained_cost": int(w["trainedCost"]),
        "untrained_arrival_days": int(w["untrainedArrivalDays"]),
        "trained_arrival_days": int(w["trainedArrivalDays"]),
        "training_cost": int(w["trainingCostPerWorker"]),
        "training_days": int(w["trainingDurationDays"]),
        "deconstruction_rounds": int(c.get("deconstructionRounds", 3)),
    }


C = load_economy_constants()


class Economy:
    """Budget, spend counters, workforce and construction. Cloned at search branch points."""

    __slots__ = ("budget", "satisfaction", "counters",
                 "free_trained", "free_untrained", "working_trained", "working_untrained",
                 "in_training", "arriving", "under_construction", "buildings", "motel_pop",
                 "pending_transfers", "pending_budget", "used_sites")

    @staticmethod
    def default_prebuilts():
        """The four buildings every episode starts with, from the sim_constants export.

        They carry the population and food storage the ResourceTrigger reads, which is why
        the port needs per-facility resources at all rather than a single global pool: a
        food request fires because ONE community is empty, not because the map is."""
        return [
            {"name": "Community Charleston", "type": "Community", "status": STATUS_PREBUILT,
             "assigned": 0, "trained": 0, "untrained": 0,
             "resources": {"foodPacks": 0, "foodPacksCapacity": 400,
                           "population": 400, "populationCapacity": 400}},
            {"name": "Community Trinity", "type": "Community", "status": STATUS_PREBUILT,
             "assigned": 0, "trained": 0, "untrained": 0,
             "resources": {"foodPacks": 0, "foodPacksCapacity": 400,
                           "population": 400, "populationCapacity": 400}},
            {"name": "Community Amherst", "type": "Community", "status": STATUS_PREBUILT,
             "assigned": 0, "trained": 0, "untrained": 0,
             "resources": {"foodPacks": 0, "foodPacksCapacity": 400,
                           "population": 400, "populationCapacity": 400}},
            {"name": "Motel", "type": "Motel", "status": STATUS_PREBUILT,
             "assigned": 0, "trained": 0, "untrained": 0,
             "resources": {"foodPacks": 0, "foodPacksCapacity": 0,
                           "population": 0, "populationCapacity": 3000}},
        ]

    def __init__(self, budget=5000, satisfaction=50.0, free_trained=5, free_untrained=5,
                 prebuilts=True):
        self.budget = int(budget)
        self.satisfaction = float(satisfaction)
        self.counters = dict.fromkeys(COUNTERS, 0)
        self.counters["daysCompleted"] = 1
        self.free_trained = free_trained
        self.free_untrained = free_untrained
        self.working_trained = 0
        self.working_untrained = 0
        self.in_training = []        # [days_remaining] per worker being trained
        self.arriving = []           # [(days_remaining, "trained"|"untrained")]
        self.under_construction = [] # [(rounds_remaining, building_type)]
        self.buildings = Economy.default_prebuilts() if prebuilts else []
        self.motel_pop = 0
        self.pending_transfers = []  # population moves that land at the END of the round
        self.pending_budget = []     # [rounds_remaining, amount] approved-but-not-arrived funding
        self.used_sites = set()      # site ids already built on -- a rebuild there is a no-op
        self.counters["totalWorkers"] = self.total_workers()

    def apply_choice(self, task_tag: str, impacts, budget_delay_rounds: int = 0,
                     destination: str = "", delivery_quantity: int = 0) -> None:
        """Resolve one task choice's impacts.

        THE SIGN DECIDES THE MECHANIC, which is easy to miss reading the JSON:
          * a NEGATIVE Budget impact is an immediate cost, charged to the task's tag
            category ("Costs are always immediate", TaskDetailUI);
          * a POSITIVE Budget impact is FUNDING, and it arrives after
            `choice.budgetDelayRounds` -- crediting it immediately hands the planner money
            it cannot actually spend yet, which is exactly the kind of error that makes a
            surrogate-optimised plan fail on the real game;
          * Satisfaction impacts apply immediately, clamped to the scene's bounds.
        """
        for impact in impacts or []:
            kind, value = impact.get("type"), impact.get("value") or 0
            if kind == "Budget":
                if value < 0:
                    self.spend(-int(value), TAG_CATEGORY.get(task_tag, "other"))
                elif value > 0:
                    self.pending_budget.append([int(budget_delay_rounds), int(value)])
            elif kind == "Satisfaction":
                self.satisfaction = max(0.0, min(100.0, self.satisfaction + float(value)))

        # A choice that routes people to the Motel is the single largest cost driver in the
        # game, and it does NOT show up in the choice's own impacts: the money arrives later
        # as a recurring $200/person/DAY bill. Measured on one capture, motel occupancy went
        # 20 -> 325 in a single round of relocation choices, and the resulting lodging spend
        # dwarfed every explicit cost by an order of magnitude (113,000 vs 13,000 of direct
        # charges). Tracking only the explicit impacts models the cheap half of the game.
        if destination and "motel" in destination.lower() and delivery_quantity > 0:
            self.pending_transfers.append(("", destination, int(delivery_quantity)))

    def clone(self) -> "Economy":
        e = Economy.__new__(Economy)
        e.budget, e.satisfaction = self.budget, self.satisfaction
        e.counters = dict(self.counters)
        e.free_trained, e.free_untrained = self.free_trained, self.free_untrained
        e.working_trained, e.working_untrained = self.working_trained, self.working_untrained
        e.in_training = list(self.in_training)
        e.arriving = list(self.arriving)
        e.under_construction = list(self.under_construction)
        e.buildings = [dict(b) for b in self.buildings]
        e.motel_pop = self.motel_pop
        e.pending_transfers = list(self.pending_transfers)
        e.pending_budget = [list(x) for x in self.pending_budget]
        e.used_sites = set(self.used_sites)
        return e

    # ── budget ──────────────────────────────────────────────────────────────────────
    def spend(self, amount: int, category: str) -> None:
        """SatisfactionAndBudget.RemoveBudget(amount, SpendCategory).

        The budget is allowed to go NEGATIVE -- minBudget is -999999, not 0 -- so this
        does not refuse. A policy that overspends is making a bad decision, not an illegal
        one, and clamping here would hide the cost-efficiency penalty that is the whole
        point of the score term."""
        self.budget -= int(amount)
        key = {"food": "foodSpend", "lodging": "lodgingSpend",
               "worker": "workerSpend", "casework": "caseworkSpend"}.get(category)
        if key:                      # uncategorised RemoveBudget (task penalties) moves
            self.counters[key] += int(amount)   # the budget without moving a counter

    # ── actions ─────────────────────────────────────────────────────────────────────
    def build(self, building_type: str, site_id=None) -> bool:
        """Start construction. Charges BuildingSystem's per-type cost, NOT action.cost.

        TWO WAYS A BUILD SILENTLY DOES NOTHING, both measured on captures rather than
        inferred, and both of which a naive port charges for anyway:

        1. THE SITE IS ALREADY BUILT ON. The menu keeps offering a build at a consumed
           site, and Unity accepts the request and does nothing -- one trace issued
           build_Kitchen_4 at round 15 on a site consumed at round 3, and the budget did
           not move. Charging it put the port 2000 ahead of Unity for the rest of the
           episode.
        BuildingSystem also has a no-debt gate (WouldAllowSpend) that rejects a build the
        budget cannot cover -- but it "honors allowNegativeBudget (RL) which permits
        overspend", and the gym runs with that on. Enforcing it here refused builds Unity
        happily charged for, on traces whose budget went to -74,596 and kept building. So
        the gate is deliberately NOT modelled, and this comment exists so the next person
        who reads BuildingSystem.cs and notices the omission finds the reason.
        """
        cost = C["build_cost"].get(building_type)
        if cost is None:
            return False
        if site_id is not None and site_id in self.used_sites:
            return False
        if site_id is not None:
            self.used_sites.add(site_id)
        self.spend(cost, SPEND_CATEGORY.get(building_type, "other"))
        self.under_construction.append([C["construction_rounds"], building_type])
        return True

    def hire(self, kind: str, quantity: int, advertised_cost: int) -> bool:
        """ActionExecutor deducts `action.cost` verbatim -- measured, both worker paths."""
        if kind not in ("trained", "untrained") or quantity <= 0:
            return False
        self.spend(advertised_cost, "worker")
        days = C["trained_arrival_days"] if kind == "trained" else C["untrained_arrival_days"]
        self.arriving += [[days, kind]] * quantity
        return True

    def train(self, quantity: int, advertised_cost: int) -> bool:
        quantity = min(quantity, self.free_untrained)
        if quantity <= 0:
            return False
        self.spend(advertised_cost, "worker")
        self.free_untrained -= quantity
        self.in_training += [C["training_days"]] * quantity
        return True

    # ── per-round and per-day bookkeeping ───────────────────────────────────────────
    def on_round_end(self) -> None:
        """RewardMetricsTracker.OnRoundEnded.

        The accumulators are the one DENSE part of the score: they move every round
        regardless of whether any task resolves, which is why worker utilisation is the
        only signal a short-horizon planner can see before round ~14."""
        self.counters["roundsCompleted"] += 1
        self.counters["cumWorkingWorkers"] += self.working_trained + self.working_untrained
        self.counters["cumTrainingWorkers"] += len(self.in_training)
        self.counters["cumIdleWorkers"] += self.free_trained + self.free_untrained
        self.counters["totalWorkers"] = self.total_workers()
        for entry in self.under_construction:
            entry[0] -= 1
        finished = [e for e in self.under_construction if e[0] <= 0]
        self.under_construction = [e for e in self.under_construction if e[0] > 0]
        for _rounds, btype in finished:
            # Construction completing puts a building in NeedWorker, NOT in service.
            self.buildings.append({"name": f"{btype}_{len(self.buildings)}", "type": btype,
                                   "status": STATUS_NEED_WORKER, "assigned": 0,
                                   "trained": 0, "untrained": 0,
                                   "resources": {"foodPacks": 0, "foodPacksCapacity": 400,
                                                 "population": 0,
                                                 "populationCapacity": 400}})
        # Deconstruction runs on the same round clock as construction.
        for b in self.buildings:
            if b.get("deconstruct_rounds"):
                b["deconstruct_rounds"] -= 1
                if b["deconstruct_rounds"] <= 0:
                    b["status"] = STATUS_DISABLED
                    self.free_trained += b["trained"]
                    self.free_untrained += b["untrained"]
                    self.working_trained -= b["trained"]
                    self.working_untrained -= b["untrained"]
                    b["trained"] = b["untrained"] = b["assigned"] = 0

    def on_day_end(self, day: int) -> None:
        """Day rollover: motel billing, worker arrivals, training completion.

        Motel billing is where 89-97% of spend goes in practice, at $200 per resident per
        day, charged on the day change for the day that just ended. It is also the reason
        an unused shelter is actively expensive: the residents keep billing."""
        if self.motel_pop > 0:
            self.spend(int(self.motel_pop * C["motel_per_person_per_day"]), "lodging")
        for entry in self.arriving:
            entry[0] -= 1
        for days, kind in [e for e in self.arriving if e[0] <= 0]:
            if kind == "trained":
                self.free_trained += 1
            else:
                self.free_untrained += 1
        self.arriving = [e for e in self.arriving if e[0] > 0]
        self.in_training = [d - 1 for d in self.in_training]
        self.free_trained += sum(1 for d in self.in_training if d <= 0)
        self.in_training = [d for d in self.in_training if d > 0]
        self.counters["daysCompleted"] = day
        self.counters["totalWorkers"] = self.total_workers()

    def deconstruct(self, index: int) -> bool:
        """Begin tearing a building down. Takes `deconstructionTimeDays` and frees its
        workers only when it COMPLETES -- until then the workers stay committed and the
        building is neither operational nor available."""
        if not (0 <= index < len(self.buildings)):
            return False
        b = self.buildings[index]
        if b["status"] in (STATUS_PREBUILT, STATUS_DECONSTRUCTING, STATUS_DISABLED):
            return False
        b["status"] = STATUS_DECONSTRUCTING
        b["deconstruct_rounds"] = C.get("deconstruction_rounds", 3)
        return True

    def facilities(self):
        """Everything the trigger conditions and task suitability read.

        Prebuilts count as operational; constructed buildings only when InUse. This is the
        connection that was missing: the port had a `buildings` list and a trigger
        evaluator, and nothing joined them, so a shelter the port built could never become
        a suitable facility or satisfy a resource trigger."""
        out = []
        for b in self.buildings:
            prebuilt = b["status"] == STATUS_PREBUILT
            out.append({"name": b.get("name"), "type": b["type"],
                        "prebuilt": prebuilt, "status": b["status"],
                        "operational": prebuilt or b["status"] == STATUS_IN_USE,
                        "resources": b.get("resources") or {}})
        return out

    def can_staff(self, index: int) -> bool:
        """Is this building in a state where workers can be assigned?

        UnderConstruction cannot be staffed -- Building.UpdateWorkerStatus only runs on
        NeedWorker and InUse. Trying anyway is a silent no-op in Unity, which is exactly
        the kind of action a planner should never waste a turn slot on."""
        if not (0 <= index < len(self.buildings)):
            return False
        return self.buildings[index]["status"] in (STATUS_NEED_WORKER, STATUS_IN_USE)

    def index_of(self, name):
        for i, b in enumerate(self.buildings):
            if b.get("name") == name:
                return i
        return -1

    def staff(self, index: int, count: int = 0, trained: int = None,
              untrained: int = None) -> bool:
        """Assign `count` WORKERS -- a head count, not workforce points.

        MEASURED, and it is the opposite of what the cmd_parser comment says. Unity's
        ExecuteAssignment calls TryReassignWorkerCountToBuilding(id, quantity) and the C#
        comment is explicit: "assign EXACTLY p.quantity workers (count, not workforce
        points)". A capture that requested 4 workers for a kitchen needing 4 workforce
        ended with assignedWorkforce = 8 and free trained 5 -> 1, so the system took FOUR
        TRAINED workers and delivered double the requested capacity.

        Two consequences a planner cares about: asking for `requiredWorkforce` workers
        OVER-STAFFS by 2x while trained workers last, and it burns the trained pool first
        -- the same workers that are worth 2 each everywhere else.

        Explicit trained/untrained are still accepted for tests that want to pin a mix."""
        if not self.can_staff(index):
            return False
        if trained is None and untrained is None:
            # Greedy, trained first -- what TryReassignWorkerCountToBuilding does.
            trained = min(count, self.free_trained)
            untrained = min(count - trained, self.free_untrained)
        else:
            trained = min(trained or 0, self.free_trained)
            untrained = min(untrained or 0, self.free_untrained)
        if trained + untrained <= 0:
            return False
        b = self.buildings[index]
        self.free_trained -= trained
        self.free_untrained -= untrained
        self.working_trained += trained
        self.working_untrained += untrained
        b["trained"] += trained
        b["untrained"] += untrained
        b["assigned"] = (b["trained"] * WORKFORCE_VALUE["trained"]
                         + b["untrained"] * WORKFORCE_VALUE["untrained"])
        if b["assigned"] >= REQUIRED_WORKFORCE:
            b["status"] = STATUS_IN_USE
        return True

    def operational(self, building_type=None):
        """IsOperational() == InUse. Anything else is invisible to triggers and deliveries."""
        return [b for b in self.buildings
                if b["status"] == STATUS_IN_USE
                and (building_type is None or b["type"] == building_type)]

    # ── derived ─────────────────────────────────────────────────────────────────────
    def total_workers(self) -> int:
        """BuildPayload's `presentWorkers`: everyone PRESENT, which excludes workers still
        travelling. A hire does not raise totalWorkers until it arrives -- and totalWorkers
        is the denominator of the worker-use score term, so counting them early would
        quietly deflate the score."""
        return (self.working_trained + self.working_untrained + len(self.in_training)
                + self.free_trained + self.free_untrained)

    def metrics(self) -> dict:
        m = dict(self.counters)
        m["totalWorkers"] = self.total_workers()
        return m


def advertised_cost_error(action: dict) -> int:
    """How much MORE the game charges than the action claims. Zero except for builds.

    Exposed rather than folded away so a caller can report the discrepancy instead of
    inheriting it: every LLM in this repo is shown the advertised number."""
    if action.get("action_type") != "construction":
        return 0
    btype = (action.get("construction") or {}).get("building_type")
    return C["build_cost"].get(btype, 0) - (action.get("cost") or 0)


# CANONICAL EXECUTION ORDER within a turn (cmd_parser._PRIO). A turn is a BASKET of
# actions, and Unity executes them in this order regardless of the order they were chosen,
# so "hire, then staff the workers you just hired" works in one turn. A port that applies a
# basket in selection order gets a different game: the staff action would find no free
# workers, silently do nothing, and leave the building unstaffed -- which then makes it
# invisible to the triggers and deliveries that depend on IsOperational().
EXECUTION_ORDER = {"deconstruct": 0, "construction": 1, "worker": 2, "resource_transfer": 5}


def basket_order(action: dict) -> int:
    kind = action.get("action_type")
    if kind == "worker":
        wat = (action.get("worker") or {}).get("worker_action_type") or ""
        if wat.startswith("hire"):
            return 2
        if wat.startswith("train"):
            return 3
        return 4                            # staff/assign
    return EXECUTION_ORDER.get(kind, 6)


def apply_basket(econ: Economy, actions) -> int:
    """Apply a whole turn's worth of actions, in Unity's execution order.

    Returns how many were accepted. Sorting is stable within a priority, so the relative
    order of two builds is preserved while the categories are reordered."""
    accepted = 0
    for action in sorted(actions, key=basket_order):
        accepted += bool(apply_action(econ, action))
    return accepted


def apply_action(econ: Economy, action: dict) -> bool:
    """Dispatch one menu action against the economy.

    Takes the ACTION DICT, never an index: Unity re-enumerates the menu every round, so an
    index is only meaningful in the round it came from. (Sending `{"actionIndex": i}` to
    execute_action is also silently ignored by the real game, which is how an entire
    capture run looked like it was playing while doing nothing.)"""
    kind = action.get("action_type")
    if kind == "construction":
        c = action.get("construction") or {}
        site = c.get("site_id")
        if site is None:
            site = _trailing_int(action.get("action_id"), default=None)
        return econ.build(c.get("building_type"), site)
    if kind == "worker":
        w = action.get("worker") or {}
        wat = w.get("worker_action_type") or ""
        qty = int(w.get("quantity") or 0) or _trailing_int(action.get("action_id"))
        cost = int(action.get("cost") or 0)
        if wat.startswith("hire"):
            return econ.hire("trained" if "trained" in wat and "untrained" not in wat
                             else "untrained", qty, cost)
        if wat.startswith("train"):
            return econ.train(qty, cost)
        return False
    if kind == "worker_assignment":
        a = action.get("assignment") or {}
        idx = econ.index_of(a.get("building_name"))
        return econ.staff(idx, count=int(a.get("quantity") or 0))
    if kind == "worker" and (action.get("worker") or {}).get("worker_action_type", "").startswith(
            ("staff", "assign")):
        w = action.get("worker") or {}
        return econ.staff(int(w.get("building_index", -1)),
                          count=int(w.get("quantity") or w.get("count") or 0))
    if kind == "resource_transfer":
        tr = action.get("transfer") or {}
        if tr.get("resource_type") == "FoodPacks":
            return False                      # food routing, not population
        qty = int(tr.get("quantity") or 0)
        src, dst = str(tr.get("source_facility") or ""), str(tr.get("destination_facility") or "")
        # Motel occupancy is the recurring cost, so it is the only population figure the
        # economy has to track to get lodging spend right.
        # A population transfer is a DELIVERY, not an instant move: it lands at the end of
        # the round, AFTER the day rollover has already billed. Measured -- a 20-person
        # transfer issued on a day-change round shows motelPop 20 in the end-of-round state
        # while that day's motel bill is still 0. Applying it instantly over-charges the
        # largest spend line in the game by a full day.
        econ.pending_transfers.append((src, dst, qty))
        return True
    return False


def action_from_id(action_id: str, cost: int = 0) -> dict:
    """Reconstruct a structured action from Unity's `action_id`.

    The id is the stable handle across the whole repo -- the command grammar, the logs and
    the journals all key on it, while the structured sub-objects only exist in a live
    enumeration. Parsing it here means a replay does not need the menu that produced it.

    Formats, all from the live build:
        build_<BuildingType>_<siteIndex>
        hire_untrained_<n> / hire_trained_<n>
        train_workers_<n>
        transfer_population_<source>_<destination>_<quantity>
    Facility names contain spaces, not underscores ("Community Charleston"), so splitting
    on "_" is safe for the transfer form."""
    aid = str(action_id or "")
    parts = aid.split("_")
    if aid.startswith("build_") and len(parts) >= 3:
        return {"action_type": "construction", "action_id": aid, "cost": cost,
                "construction": {"building_type": parts[1],
                                 "site_id": _trailing_int(aid, default=None)}}
    if aid.startswith("hire_") and len(parts) >= 3:
        return {"action_type": "worker", "action_id": aid, "cost": cost,
                "worker": {"worker_action_type": f"hire_{parts[1]}",
                           "quantity": _trailing_int(aid)}}
    if aid.startswith("train_"):
        return {"action_type": "worker", "action_id": aid, "cost": cost,
                "worker": {"worker_action_type": "train", "quantity": _trailing_int(aid)}}
    if aid.startswith("transfer_population_") and len(parts) >= 5:
        return {"action_type": "resource_transfer", "action_id": aid, "cost": cost,
                "transfer": {"resource_type": "Population", "quantity": _trailing_int(aid),
                             "source_facility": parts[2], "destination_facility": parts[3]}}
    return {"action_type": aid.split("_")[0] if aid else None, "action_id": aid, "cost": cost}


def step_round(econ: Economy, day_changed: bool = False, new_day: int = 0) -> None:
    """One round of economy bookkeeping, in Unity's phase order.

    THE ORDER IS THE MECHANIC, and each step of it was pinned against a capture:

      1. day rollover first, if this round crosses one -- motel billing reads the occupancy
         from BEFORE this round's transfers land, and worker arrivals complete here;
      2. then the round accumulators, which is why newly-arrived workers are counted as
         idle in the very round they arrive (cumIdleWorkers 52 vs 50 over five rounds);
      3. then pending transfers land, which is why they are billed from the NEXT day.

    Getting 1 and 2 the other way round costs two idle-worker units per arrival; getting 1
    and 3 the other way round over-bills the motel by a full day on every transfer."""
    if day_changed:
        econ.on_day_end(new_day)
    econ.on_round_end()
    for entry in econ.pending_budget:
        entry[0] -= 1
    arrived = [e for e in econ.pending_budget if e[0] <= 0]
    econ.pending_budget = [e for e in econ.pending_budget if e[0] > 0]
    for _rounds, amount in arrived:
        econ.budget += amount        # AddBudget: funding, never a spend counter

    for _src, dst, qty in econ.pending_transfers:
        if "motel" in dst.lower():
            econ.motel_pop += qty
    econ.pending_transfers = []


def _trailing_int(action_id, default=1):
    """hire_untrained_3 -> 3. The quantity axis Unity pre-discretises into menu entries,
    and for builds the same suffix is the SITE id."""
    if not action_id:
        return default
    tail = str(action_id).rsplit("_", 1)[-1]
    return int(tail) if tail.isdigit() else default


def from_game_state(state: dict) -> Economy:
    """Seed an Economy from a captured Unity game_state, for replay validation."""
    sb = state.get("satisfactionAndBudget") or {}
    wf = state.get("workforceState") or {}
    e = Economy(budget=sb.get("budget", 5000), satisfaction=sb.get("satisfaction", 50.0),
                free_trained=wf.get("freeTrainedWorkers", 0),
                free_untrained=wf.get("freeUntrainedWorkers", 0))
    e.working_trained = wf.get("workingTrainedWorkers", 0) or 0
    e.working_untrained = wf.get("workingUntrainedWorkers", 0) or 0
    e.in_training = [1] * (wf.get("untrainedWorkersInTraining", 0) or 0)
    for f in ((state.get("mapState") or {}).get("facilities") or []):
        if f.get("buildingType") == "Motel":
            e.motel_pop = f.get("currentPopulation", 0) or 0
    rm = state.get("rewardMetrics") or {}
    for k in COUNTERS:
        if k in rm:
            e.counters[k] = rm[k]
    return e
