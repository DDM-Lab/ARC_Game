"""The round loop: chains the ported mechanics into a stepping world model.

Per-mechanic tests replay each mechanic from its own captured entry state, so they cannot
catch an error in the ORDER or the SCHEDULE -- a port whose mechanics are individually
perfect still diverges if it runs weather after flood, or runs a task-generation pass on a
segment Unity skips. This module is where that ordering lives, and test_sim chains it for
whole episodes against Unity instead of replaying single rounds.

THE SCHEDULE, READ OFF THE INSTRUMENTED TRACE (not inferred from the code):

    ... endSim:enter, endSim:afterMetrics
    OnRoundChanged(seg)                       generation pass IF seg != 3
      draw:TaskTrigger.probability  x N
    endSim:afterAdvanceSegment, endSim:afterOnRoundEnd
    flood:enter                               the flood update
    ...
    day:enterProceedToNextDay                 at the end of segment 3
      day:beforeOnDayChanged
      draw:Weather.select                     weather for the WHOLE next day
      day:afterOnDayChanged
    OnRoundChanged(0)                         generation pass for segment 0
      draw:TaskTrigger.probability  x N
    OnRoundChanged(1)                         and immediately another for segment 1
      draw:TaskTrigger.probability  x N

Two consequences that a plausible implementation gets wrong:

  - Generation runs BEFORE flood in a round, so a task generated this round sees last
    round's flood. Running it after would still produce the right draw COUNT.
  - A day rollover fires TWO generation passes back to back (segment 0, then segment 1)
    around one weather draw. That is the 3/0/0/6 draw pattern per day, and it is why a
    naive "one pass per round" loop drifts by three draws every day.

Segment 3 is skipped deliberately in TaskSystem.OnRoundChanged; it is not an off-by-one.
"""
from __future__ import annotations

from .clients import ClientTracker
from .economy import BUDGET_MAX, BUDGET_MIN, C as _ECON_C, Economy, step_round as economy_step
from .flood import FloodState, update_flood
from .floodmap import FloodMap
from . import roads
from .tasks import Task, TaskBoard, demand_of
from .generation import TriggerContext, generation_pass, suitable_facilities
from .triggers import INVENTORY as _INVENTORY

# Task definitions by id, so a generated task carries its own choices.
_TASK_SPEC = {t["taskId"]: t for t in _INVENTORY}
# ClientStayTracker.GenerateCaseworkTask builds this task in code, not from a TaskData
# asset, so it is not in the exported inventory. Transcribed: Advisory, tag BackToHome,
# rounds 3; choice 1 ships the group's needy clients to casework sites (+10), choice 2
# tells them to wait (-10). deliveryQuantity is per instance (the group's needy count) and
# is filled in on the copy stored in generated_specs.
CASEWORK_SPEC_ID = "Casework_Request"
_TASK_SPEC[CASEWORK_SPEC_ID] = {
    "taskId": CASEWORK_SPEC_ID, "taskTitle": "Casework Request", "taskType": "Advisory",
    "taskTag": "BackToHome", "roundsRemaining": 3, "isGlobalTask": False,
    "choices": [
        {"choiceId": 1, "triggersDelivery": True, "immediateDelivery": False,
         "deliveryQuantity": 0, "budgetDelayRounds": 0, "destinationCategory": "CaseworkSite",
         "enableMultipleDeliveries": True, "impacts": [{"type": "Satisfaction", "value": 10}]},
        {"choiceId": 2, "triggersDelivery": False, "immediateDelivery": False,
         "deliveryQuantity": 0, "budgetDelayRounds": 0, "destinationCategory": "",
         "enableMultipleDeliveries": False, "impacts": [{"type": "Satisfaction", "value": -10}]},
    ]}
_CASEWORK_DESTS = 3          # FindMultipleDestinations(choice, facility, 3)
# Tasks the game builds in code (no TaskData asset, stableTaskId ""), by title, for the
# validator and the lockstep diagnostics to map Unity's instances onto the port's specs.
CODE_BUILT_TASKS = {"Casework Request": "Casework_Request",
                    "Road Blockage Emergency": "Road_Blockage",
                    "Vehicle Repair Required": "Repair"}

# FloodTaskGenerator.CreateRoadBlockageTask: built in code when a vehicle is stopped by the
# flood. Emergency, rounds 2, impacts Satisfaction -20 (applied on Incomplete expiry), and a
# choice list that depends on the cargo and on whether it was aboard. The base entry lists
# the union so the search gene can name any of them; the per-instance copy in
# generated_specs holds the case's actual list. Verified on 5503 (population, loaded);
# the food and not-yet-loaded cases are transcribed from the C# and unverified.
ROAD_BLOCKAGE_SPEC_ID = "Road_Blockage"
_BLOCKAGE_FAILURE_PENALTY = 10.0      # GameTask.deliveryFailureSatisfactionPenalty default
_BLOCKAGE_ABANDON_PENALTY = 30.0      # FloodTaskGenerator.OnAnyTaskCompleted, loaded clients
_TASK_SPEC[ROAD_BLOCKAGE_SPEC_ID] = {
    "taskId": ROAD_BLOCKAGE_SPEC_ID, "taskTitle": "Road Blockage Emergency", "taskType": "Emergency",
    "taskTag": "None", "roundsRemaining": 2, "isGlobalTask": False,
    "choices": [
        {"choiceId": 1, "triggersDelivery": True, "immediateDelivery": False, "deliveryQuantity": 0,
         "budgetDelayRounds": 0, "destinationCategory": "", "enableMultipleDeliveries": False,
         "impacts": [{"type": "Satisfaction", "value": 5}]},
        {"choiceId": 2, "triggersDelivery": True, "immediateDelivery": False, "deliveryQuantity": 0,
         "budgetDelayRounds": 0, "destinationCategory": "", "enableMultipleDeliveries": False,
         "impacts": [{"type": "Budget", "value": -200}, {"type": "Satisfaction", "value": 8}]},
        {"choiceId": 3, "triggersDelivery": False, "immediateDelivery": False, "deliveryQuantity": 0,
         "budgetDelayRounds": 0, "destinationCategory": "", "enableMultipleDeliveries": False,
         "impacts": [{"type": "Budget", "value": -1000}, {"type": "Satisfaction", "value": 15}]},
        {"choiceId": 4, "triggersDelivery": False, "immediateDelivery": False, "deliveryQuantity": 0,
         "budgetDelayRounds": 0, "destinationCategory": "", "enableMultipleDeliveries": False,
         "impacts": [{"type": "Satisfaction", "value": -30}]},
    ]}
_INCOMPLETE_PENALTY = {k: v for k, v in (_ECON_C.get("incompletePenalty") or {}).items() if not k.startswith("_")}
from .triggers import roll_pass
from .weather import RAIN_INTENSITY, generate_weather

ROUNDS_PER_DAY = 4

# Segments are tagged 1..4 in the instrumented trace. A task-generation pass runs on the
# advance INTO segment 2, and twice at the day rollover (Unity fires OnRoundChanged for
# segment 0 and then segment 1 back to back around the weather draw). Segments 3 and 4 run
# none: TaskSystem skips newSegment == 3 explicitly, and the 3 -> 4 advance fires no
# OnRoundChanged at all. Three passes per day, which is what the trace shows.
#
# This is read off the trace rather than derived from the segment numbering, because the
# numbering does not line up: the mark tag counts 1..4 while OnRoundChanged reports 1,2,3
# then 0. Encoding the observable schedule is honest; encoding a guessed numbering is not.
_GENERATION_SEGMENTS = (2,)
# GameDataManager.InitialEmergencyTaskFrequency -- the one economy constant not exported.
# 4 is the .cs default and reproduces the observed spacing (two Emergency tasks in 24
# rounds). Export it if the scene is ever retuned.
_NUM_EMERGENCY_TASKS = 4
_FINAL_DAY = 8
_ROLLOVER_PASSES = 2
# TaskSystem.CreateTask's per-type defaults: Emergency 1, Demand 2, Advisory 3, Alert 2.
_ALERT_ROUNDS = 2


class World:
    """Everything a round transition reads or writes, and nothing else.

    `facilities_for` is injected rather than derived: it returns the ordered facility list
    a task rolls against, and Unity's order is FindObjectsOfType order, which is neither
    sorted nor creation order (see PLAN.md). Guessing it here would be inventing physics."""

    __slots__ = ("rng", "flood", "fmap", "weather", "day", "segment",
                 "facilities_for", "generated", "clients", "economy", "tasks",
                 "round_index", "_trigger_memory", "use_generation",
                 "pending_arrivals", "pending_removals", "_casework_live", "generated_specs", "_alerts_shown",
                 "_emergency_count", "_last_emergency_round", "_sourced_now",
                 "_food_reserved")

    def __init__(self, rng, weather, day=1, segment=0, flood=None, fmap=None,
                 facilities_for=None):
        self.rng = rng
        self.fmap = fmap if fmap is not None else FloodMap.load()
        self.flood = flood if flood is not None else FloodState()
        self.weather = weather
        self.day = day
        # Segment starts at 0, not 1. step_round advances BEFORE acting, so starting at 1
        # gave day 1 only three rounds and rolled every subsequent day over one round early
        # -- which put every generated task, every delivery and every counter a round ahead
        # of Unity. A single off-by-one at construction, visible only once the exact replay
        # compared round by round.
        self.segment = segment
        # Default to the port's OWN facilities. Previously this defaulted to an empty
        # stub, so a shelter the surrogate built could never become a suitable facility,
        # never satisfy a resource trigger and never change the generation draw count --
        # the building lifecycle existed but nothing consumed it.
        self.facilities_for = facilities_for or self._own_facilities
        self.generated = []            # last round's trigger rolls, for tasks.py to consume
        # roll_pass only DRAWS; generation_pass draws AND decides which tasks fire. The
        # closed-loop equivalence tests pin the stream with roll_pass, so generation stays
        # opt-in until it has ground truth of its own.
        self.use_generation = False
        self.clients = ClientTracker()
        self.economy = Economy()
        # Facilities resolve to road cells geometrically, from the position the game
        # already reports, rather than through a name table. RoadConnection picks the
        # nearest road tile the same way, and the two agree on all five prebuilts -- and
        # unlike a name map this keeps working for facilities the player builds mid-episode.
        self.tasks = TaskBoard(cell_for=self._facility_cell)
        # A food delivery that reaches an empty kitchen does not fail -- LoadCargo aborts
        # and the trip runs again once the kitchen restocks at the day reset.
        self.tasks.retry_if_unsourced = self._can_source
        self.tasks.on_blocked = self._blocked_delivery
        self._sourced_now = {}      # task -> packs already pulled this round
        self._food_reserved = 0     # packs promised to orders not yet loaded
        self.round_index = 0
        self._trigger_memory = {}       # stateful triggers (FloodExpanded, BudgetDropped)
        self.pending_arrivals = []      # deliveries that landed LAST round, drawn this one
        self.pending_removals = []      # (facility, n): casework removals queued by a landing
        self._casework_live = {}        # casework task id -> client group id, until it ends
        self.generated_specs = {}       # live task id -> (definition id, facility, spec)
        self._alerts_shown = set()      # Alert tasks fire once per GAME
        self._emergency_count = 0
        # TaskSystem initialises lastEmergencyTaskRound to 0, NOT to "long ago". The gate
        # is `currentRound < lastEmergencyTaskRound + dynamicInterval`, so with an interval
        # of totalRounds/numEmergencyTasks = 32/4 = 8 the FIRST emergency cannot fire
        # before round 8. Seeding this at -99 let the port fire one immediately, and
        # because Community Emergency Evacuation is Lodging-tagged and sits ahead of
        # Population Relocation in the inventory, it took the one-lodging-task-per-facility
        # slot -- which is why the port had an Evacuation at round 5 where Unity had a
        # second Relocation.
        self._last_emergency_round = 0

    def _can_source(self, task, quantity):
        """Packs the kitchens could hand a vehicle right now, for LoadCargo's abort test.

        Only food is sourced from a building; a population relocation loads people from the
        community that asked, and that is checked when the choice is made.
        """
        if getattr(task, "tag", "") == "BackToHome":
            # LoadCargo: RemoveResource(Population, quantity) from the requesting facility;
            # an empty source aborts the trip.
            src = self.economy.facility(getattr(task, "source", "") or "")
            have = ((src.get("resources") or {}).get("population") or 0) if src else 0
            return min(quantity, have)
        if str(task.destination or "").startswith("__food__") is False:
            return quantity
        chosen = getattr(task, "chosen_id", None)
        spec = (self.generated_specs.get(task.task_id) or (None, None, {}))[2]
        choice = next((c for c in (spec.get("choices") or [])
                       if c.get("choiceId") == chosen), None)
        if (choice or {}).get("immediateDelivery"):
            return quantity          # external supply, no kitchen involved
        got = _source_food(self, quantity)
        self._food_reserved = max(0, self._food_reserved - quantity)
        if got:
            self._sourced_now[task.task_id] = got
        return got

    def _blocked_delivery(self, payload, loaded, task, was_open):
        """StopVehicleDueToFlood's task side. `task` is the parent (already off the board),
        `loaded` whether the cargo was aboard, `was_open` whether HandleDeliveryFailure found
        it InProgress (then it charges the failure penalty)."""
        if was_open:
            self.economy.satisfaction = max(0.0, min(100.0, self.economy.satisfaction
                                                     - _BLOCKAGE_FAILURE_PENALTY))
        _create_blockage_task(self, payload, loaded, task)

    def reserve_food(self, quantity):
        """FoodDeliveryHandler's effectiveStock rule, applied when the ORDER IS PLACED.

        GetKitchensSorted ranks kitchens by (actual stock - already outbound) and the
        handler sends min(remaining, effectiveStock) from each; with no kitchen left
        holding anything it creates NO delivery at all and returns false. So the cap is
        applied at order-creation time against food already promised to other orders, not
        when a vehicle happens to load.

        That is the round-5 mechanism. Three 100-pack orders answered together against a
        200-pack kitchen: the first two reserve 100 each, the third finds effectiveStock 0
        and never becomes a delivery. Reserving at ARRIVAL instead let all three through
        and then rationed them, which resolves the wrong ones at the wrong times.
        """
        free = sum((b.get("resources") or {}).get("foodPacks") or 0
                   for b in self.economy.buildings
                   if b["type"] == "Kitchen" and b["status"] == "InUse") - self._food_reserved
        take = max(0, min(quantity, free))
        self._food_reserved += take
        return take

    def _facility_cell(self, name):
        """Facility name -> its road-network cell, resolved geometrically.

        RoadConnection picks a building's nearest road tile from its position; doing the
        same here agrees with the dumped connection cell on all five prebuilts, and unlike a
        name table it keeps working for facilities the player builds mid-episode.
        """
        if not name:
            return None
        cell = roads.FACILITY_CELL.get(str(name))
        if cell is not None:
            return cell
        f = self.economy.facility(str(name))
        if not f:
            return None
        # A player-built facility sits on the site it was built on, and SITE_CELL knows
        # where every site is. Before this, every built facility was locationless and its
        # deliveries fell back to the fitted constant -- 176 of 247 travel computations.
        sid = f.get("site_id")
        if sid is not None:
            cell = roads.SITE_CELL.get(sid)
            if cell is not None:
                return cell
        # The port's own economy records carry no position -- only Unity observations do --
        # so this branch serves callers driven by live observations (play.py) and any
        # facility not in the prebuilt table.
        pos = f.get("position") or {}
        x, y = pos.get("x"), pos.get("y")
        if x is None or y is None:
            return None
        return roads.nearest_road(roads.world_to_cell(x, y))

    def flooded_road_cells(self):
        """Flooded cells that A* actually cares about.

        Only the 106 road cells can block a route, so this checks those rather than walking
        the whole flood set -- the pathfinder is called once per delivery leg and this keeps
        it cheap enough not to cost the surrogate its speed.
        """
        tiles = self.flood.tiles
        if not tiles:
            return frozenset()
        # One packed road table per process; the intersection runs in C instead of
        # packing all 106 road cells on every call (three calls a step).
        roads_p = _PACKED_ROADS
        return frozenset(roads_p[p] for p in tiles.intersection(roads_p))

    @staticmethod
    def _live_ids(w):
        return set(w.tasks.active)

    def _own_facilities(self, task_def):
        # FindObjectsOfType order: prebuilts in the scene's fixed order (calibrated:
        # Community01, Community03, Community02), constructed buildings NEWEST FIRST --
        # "Found 2 suitable facilities for Food Request From Shelter: Shelter_9, Shelter_5"
        # (5901 validation). With two operational shelters the first probability roll
        # belongs to the newer one; construction order handed it to the older and the
        # port missed a shelter food request Unity created (round 24, day 7 rollover).
        facs = self.economy.facilities()
        pre = [f for f in facs if f.get("prebuilt")]
        built = [f for f in facs if not f.get("prebuilt")]
        return suitable_facilities(task_def, pre + built[::-1])

    def trigger_context(self) -> TriggerContext:
        """Everything the trigger conditions read, from the port's own state."""
        free = self.economy.free_trained + self.economy.free_untrained
        total = max(1, self.economy.total_workers())
        return TriggerContext(
            day=self.day, segment=self.segment, weather=self.weather,
            flood_tiles=len(self.flood.tiles), budget=self.economy.budget,
            satisfaction=self.economy.satisfaction, free_workforce=free,
            idle_ratio=100.0 * free / total, facilities=self.economy.facilities(),
            trained=self.economy.free_trained + self.economy.working_trained,
            untrained=self.economy.free_untrained + self.economy.working_untrained,
            idle_trained=self.economy.free_trained,
            idle_untrained=self.economy.free_untrained,
            prev=self._trigger_memory,
            flooded=self.flood.tiles, positions=_facility_positions())

    def clone(self) -> "World":
        w = World.__new__(World)
        w.rng = self.rng.clone()
        w.fmap = self.fmap             # immutable terrain, shared on purpose
        w.flood = self.flood.clone()
        w.weather = self.weather
        w.day = self.day
        w.segment = self.segment
        w.facilities_for = self.facilities_for
        w.generated = []
        w.clients = self.clients.clone()
        w.economy = self.economy.clone()
        w.tasks = self.tasks.clone()
        w.round_index = self.round_index
        w._trigger_memory = dict(self._trigger_memory)
        w.pending_arrivals = list(self.pending_arrivals)
        w.pending_removals = list(self.pending_removals)
        w._casework_live = dict(self._casework_live)
        w.generated_specs = dict(self.generated_specs)
        w._alerts_shown = set(self._alerts_shown)
        w._sourced_now = dict(self._sourced_now)
        w._food_reserved = self._food_reserved
        w._emergency_count = self._emergency_count
        w._last_emergency_round = self._last_emergency_round
        w.use_generation = self.use_generation
        # REBIND CALLBACKS TO THE CLONE. TaskBoard holds bound methods of the World that
        # created it (retry_if_unsourced -> _can_source, cell_for, has_supplier) and the
        # World holds facilities_for; copied by reference they keep pointing at the
        # ORIGINAL, so a clone's fleet asked the original whether a kitchen had stock and
        # its generation pass evaluated the original's facilities -- reading the wrong
        # world without mutating it, which is why an isolation test cannot catch it. A
        # search rollout on a clone scored 1.39 where a fresh world scored 2.50.
        for owner, attr in ((w.tasks, "retry_if_unsourced"), (w.tasks, "cell_for"),
                            (w.tasks, "has_supplier"), (w, "facilities_for"),
                            (w.tasks, "on_blocked")):
            fn = getattr(owner, attr, None)
            if getattr(fn, "__self__", None) is self:
                setattr(owner, attr, getattr(w, fn.__name__))
        return w


def _occupies_slot(w, live_id):
    """Is this task still holding its facility's slot, as Unity's activeTasks would be?

    Unity keeps an ANSWERED task in activeTasks with status InProgress until its deliveries
    finish, so the per-facility and global duplicate checks still see it and no replacement
    is generated. The port moves answered tasks to `awaiting`, and the gates only looked at
    `active` -- so the slot freed the instant a task was answered and the same trigger fired
    again in the same round's generation pass. That is where the port's extra round-6 food
    tasks come from: three at round 5, answered before step_round runs, and three more
    generated during it.

    Silently-dropped tasks are already popped out of awaiting by the delivery-failure path,
    so they correctly stop occupying anything -- which matches HandleDeliveryFailure taking
    the task out of activeTasks.
    """
    if live_id in w.tasks.active:
        return True
    t = w.tasks.awaiting.get(live_id)
    return t is not None and not t.resolved


def _admits(w: World, spec, facility) -> bool:
    """TaskSystem's duplicate suppression, which is the difference between a plausible
    task stream and 2.7x too much demand.

    Without these gates the port fires a task every pass its triggers permit, and since
    triggers stay satisfied for many rounds it re-fires the same request endlessly:
    measured at lodgingResolved 2451 against Unity's 901 on the same policy.

      ALERT      once per GAME (shownAlertIds), not once per round.
      GLOBAL     one live instance per title.
      LODGING    at most ONE per facility at a time -- verified against a capture, where
                 the maximum concurrent lodging tasks on any facility was exactly 1.

      EMERGENCY  capped at `numEmergencyTasks` per game AND spaced by
                 max(2, totalRounds / numEmergencyTasks) rounds.

    The Emergency gate turned out to be load-bearing rather than a detail. Without it
    Community_Flood_Damge (Emergency, tagged Lodging) fired six times and SQUATTED the
    one-per-facility lodging slot, which blocked Community_TransportRequest entirely --
    the port generated no relocation demand at all while Unity generated 901. Unity's own
    capture shows exactly two Emergency tasks in 24 rounds, consistent with the cap.

    `numEmergencyTasks` comes from GameDataManager and is the ONE constant here not
    exported; 4 is the .cs default and matches the observed spacing. Flagged rather than
    silently assumed."""
    kind = spec.get("taskType")
    if kind == "Emergency":
        if w._emergency_count >= _NUM_EMERGENCY_TASKS:
            return False
        interval = max(2, (_FINAL_DAY * ROUNDS_PER_DAY) // max(1, _NUM_EMERGENCY_TASKS))
        if w.round_index < w._last_emergency_round + interval:
            return False
        w._emergency_count += 1
        w._last_emergency_round = w.round_index
        # Evict only once this emergency is actually being created. Doing it before the cap
        # and spacing gates threw away a lodging task for an emergency that was then
        # rejected, which is a strictly worse error than not evicting at all.
        if spec.get("taskTag") == "Lodging" and facility:
            for live_id, (def_id, fac, sp) in list(w.generated_specs.items()):
                if (live_id in w.tasks.active and fac == facility
                        and sp.get("taskTag") == "Lodging"
                        and sp.get("taskType") != "Emergency"):
                    # Removed from activeTasks with no RecordTaskResolution: never resolved,
                    # never counted, silently gone.
                    w.tasks.active.pop(live_id, None)
                    w.tasks.awaiting.pop(live_id, None)
                    w.tasks.pending = [x for x in w.tasks.pending if x[1][0] != live_id]
        return True
    if kind == "Alert":
        if spec["taskId"] in w._alerts_shown:
            return False
        w._alerts_shown.add(spec["taskId"])
        return True
    if spec.get("isGlobalTask"):
        # `s[0] in w._live_ids(w)` compared a DEFINITION id (a string, "Budget_Allocation")
        # against a set of live TASK ids (ints), so it was always False and the global gate
        # never blocked anything. It went unnoticed because nothing global fired twice in a
        # round until the rollover began evaluating at segment 0 -- then Daily Budget
        # Allocation appeared TWICE on the round-5 board, granting +10000 where Unity
        # grants +5000. Compare the live id against the live set, and the definition id
        # against the definition.
        return not any(def_id == spec["taskId"]
                       for live_id, (def_id, _fac, _sp) in w.generated_specs.items()
                       if _occupies_slot(w, live_id))
    # GENERAL per-facility duplicate check, which applies to EVERY task type:
    #     activeTasks.Any(t => t.taskTitle == taskData.taskTitle
    #                       && t.affectedFacility == facilityName)
    # The port only had the Lodging-specific rule, so a food request for a community could
    # be re-created every pass while one was already live -- 45 food tasks resolved against
    # Unity's 15.
    for live_id, (def_id, fac, sp) in w.generated_specs.items():
        if _occupies_slot(w, live_id) and fac == facility and def_id == spec["taskId"]:
            return False
    # THEN the Lodging-specific rule, which is stricter: at most one lodging task per
    # facility even across DIFFERENT lodging titles.
    if spec.get("taskTag") == "Lodging":
        for live_id, (def_id, fac, sp) in w.generated_specs.items():
            if _occupies_slot(w, live_id) and fac == facility and sp.get("taskTag") == "Lodging":
                return False
    return True


def _pass(w: World, marks):
    """One task-generation pass. Same draws either way; only the outcome differs."""
    if w.use_generation:
        return generation_pass(w.rng, w.trigger_context(), w.facilities_for, marks=marks)
    return roll_pass(w.rng, w.facilities_for, marks=marks)



def _tracker(w, marks):
    """ClientStayTracker.OnRoundChanged: unfiltered, so once per segment advance.

    TaskSystem skips segment 3; the tracker does not, and it also fires on the rollover's
    Invoke(0) -- four invokes a day against the port's previous one. Placed after the advance
    and before that advance's generation pass, which is the order the mark diff shows on a
    normal round: caseworkGen then TaskTrigger.probability.
    """
    # THE TRACKER DOES NOT FIRE ON THE LAST SEGMENT OF A DAY. Measured over a full 32-round
    # capture: Unity's caseworkGen draws land on r0, r1, r2 and r3 of every day and NEVER on
    # r4 -- steps 8, 12, 16, 20, 24 and 28 are all r4 and all empty. That is Fable's "four
    # invokes per day: segments 1, 2, 3, 0" read from the other side: segment 4 ends the day
    # and the next OnRoundChanged the tracker sees is the rollover's Invoke(0). The port fired
    # every step, which is exactly the surplus caseworkGen the mark diff kept reporting.
    # Re-arm BEFORE this pass draws, and on EVERY pass: a casework task that expired in
    # the rollover's first pass re-enables its group in that pass's CheckExpiredTasks, and
    # the group rolls again in the second pass (5901 validation, day-4 rollover: group 2's
    # task went Incomplete in d4r0 and its new request was generated in d4r1).
    _sweep_casework(w)
    if w.segment >= ROUNDS_PER_DAY:
        return
    generated = []
    for count, facility in w.clients.update(w.rng, _unity_round(w), w.economy.counters, marks,
                                            generated=generated):
        # DEPARTURES DO NOT FREE THE FACILITY. TriggerNonCaseworkDeparture mutates tracker
        # state only; OnCaseworklessClientsDeparted has no subscribers, and Motel Population
        # storage only ever drops via a vehicle LoadCargo. Unity's lodgingSpend therefore steps
        # by a CONSTANT every day (5901: +120,000 per rollover for the whole episode), while the
        # port's dwindled to nothing as its residents 'went home'. That gap was 46.7M of the
        # 46.9M total state error and invisible to every first-divergence report.
        pass
        w.economy.motel_pop = w.economy.motel_population
    for gid, facility, with_need in generated:
        _create_casework_task(w, gid, facility, with_need)


def _create_casework_task(w, gid, facility, with_need):
    """GenerateCaseworkTask -> TaskSystem.CreateTask, bypassing every TaskData gate.

    AGED AT BIRTH. The tracker's OnTimeSegmentChanged handler runs BEFORE TaskSystem's on
    the same advance (it subscribed first), so the task is created with roundsRemaining 3
    and decremented to 2 in the same frame -- 5503 validation: `task:created ... rounds 3`
    and `rounds remaining: 2` under one segment tag, and task 10 (born d2r3) went
    Incomplete on the day-3 rollover's second pass. The port ages before it runs the
    tracker, so the decrement is applied here by hand."""
    spec = dict(_TASK_SPEC[CASEWORK_SPEC_ID])
    spec["choices"] = [dict(c) for c in spec["choices"]]
    spec["choices"][0]["deliveryQuantity"] = with_need
    spec["_gid"] = gid
    t = Task(w.tasks.next_id, "BackToHome", 0, spec["roundsRemaining"])
    t.rounds_remaining -= 1
    t.fresh = False
    t.source = str(facility)
    t.destination = ""
    w.tasks.next_id += 1
    w.tasks.add(t)
    w.generated_specs[t.task_id] = (CASEWORK_SPEC_ID, str(facility), spec)
    w._casework_live[t.task_id] = gid


def _create_blockage_task(w, payload, loaded, parent):
    """CreateRoadBlockageTask for one stopped delivery. Cargo and endpoints come from the
    dropped payload: (task_id, quantity, destination_tag); the source is the parent's."""
    tid, quantity, dest = payload[0], payload[1], str(payload[2] or "")
    source = (parent.source if parent is not None else "") or ""
    food = dest.startswith("__food__")
    base = _TASK_SPEC[ROAD_BLOCKAGE_SPEC_ID]
    spec = dict(base)
    spec["_loaded"] = bool(loaded)
    spec["_cargo"] = "food" if food else "people"
    spec["_dest"] = dest
    if food:
        choices = [dict(c) for c in base["choices"]]
        for c in choices[:2]:
            c["deliveryQuantity"] = quantity
    elif loaded:
        # CreatePopulationLoadedChoices: clients returned to the source (the port never
        # debited it -- people leave at the landing), one $1500 immediate transport.
        choices = [{"choiceId": 1, "triggersDelivery": False, "immediateDelivery": True,
                    "deliveryQuantity": quantity, "budgetDelayRounds": 0,
                    "destinationCategory": "Shelter", "enableMultipleDeliveries": False,
                    "impacts": [{"type": "Budget", "value": -1500}, {"type": "Satisfaction", "value": 10}]}]
    else:
        # CreatePopulationUnloadedChoices: a new vehicle, same endpoints.
        choices = [{"choiceId": 1, "triggersDelivery": True, "immediateDelivery": False,
                    "deliveryQuantity": quantity, "budgetDelayRounds": 0,
                    "destinationCategory": dest if dest in ("Motel", "Shelter") else "Shelter",
                    "enableMultipleDeliveries": False,
                    "impacts": [{"type": "Satisfaction", "value": 5}]}]
    spec["choices"] = choices
    t = Task(w.tasks.next_id, "None", 0, spec["roundsRemaining"])
    t.source = source
    w.tasks.next_id += 1
    w.tasks.add(t)
    w.generated_specs[t.task_id] = (ROAD_BLOCKAGE_SPEC_ID, source, spec)


def _sweep_casework(w):
    """OnCaseworkTaskFinished for every casework task that has left the board since the
    last sweep: completed by its deliveries, completed at answer (choice 2), expired, or
    dropped by a vehicle failure. Runs before the tracker so the re-armed group draws on
    this advance, which is when Unity's flag (cleared at the event) is next read."""
    for tid, gid in list(w._casework_live.items()):
        if tid in w.tasks.active:
            continue
        task = w.tasks.awaiting.get(tid)
        if task is not None and not task.resolved:
            continue
        w.clients.rearm(gid)
        del w._casework_live[tid]
        w.tasks._sources.pop(tid, None)


def _unity_round(w):
    """Unity's ClientStayTracker.currentRound: segment + (day-1)*4.

    NOT the port's round_index. They drift: a rollover moves the port's index by one while
    Unity re-uses the previous index for its segment-0 invoke (day 1 segment 4 and day 2
    segment 0 are both 4) and only then moves to 5. Y = currentRound - arrivalRound drives
    the caseworkGen threshold 10 * 1.5^(Y-1), so the drift changes OUTCOMES on identical
    randoms -- which is exactly the step-8 residue the mark diff isolated.
    """
    # STALE ON THE LAST SEGMENT. currentRound is assigned inside OnRoundChanged, and the
    # tracker never fires on segment 4 (no caseworkGen draw ever lands on r4), so a group
    # registered while the clock reads d4r4 is stamped with r3's value: "Registered 63
    # clients at Shelter_0 (Group: Relocate_47..., Round: 15)" under a d4r4 tag (5503). Stamping
    # 16 put that group's departure a round late, so its casework roll credited 63 instead
    # of the 15 left after the non-casework members had gone home.
    return min(w.segment, ROUNDS_PER_DAY - 1) + (w.day - 1) * ROUNDS_PER_DAY


def _create_tasks(w, rolls, day_changed):
    """Build task objects from rolls that have already drawn.

    Called PER ROLLOVER PASS so a task born in pass 0 is on the board for pass 1's
    segment advance, which is what ages it. Unity creates at s5d2r0 and its own second
    rollover advance takes rounds 2 -> 1; the port used to create after BOTH passes, so
    the task arrived an advance young and expired -- and credited -- a round late.
    """
    for task_id, facility, born_in in rolls:
        spec = _TASK_SPEC.get(task_id)
        if spec is None or not _admits(w, spec, facility):
            continue
        state = {"choices": spec.get("choices") or []}
        tag = spec.get("taskTag") or "None"
        t = Task(w.tasks.next_id, tag, demand_of(state, tag),
                 spec.get("roundsRemaining") or 1)
        t.destination = ""
        # A task born in the ROLLOVER is not new to the round that follows it. The
        # rollover IS a segment advance (Unity's d2r0), so OnTimeSegmentAdvanced ticks
        # such a task at d2r1 and again at d2r2 -- two decrements by the time round 5's
        # advance runs. The port's fresh-skip ate one of them and put every
        # rollover-born task a full round late, which is why an answered relocation
        # that should have gone Incomplete at round 5 was still waiting.
        # ...but only one born in the FIRST rollover pass. The rollover runs two passes,
        # evaluated as segment 0 and segment 1; a task created in the segment-1 pass is
        # new to that segment and must not be aged by it. Marking both passes not-fresh
        # aged half the board a round early.
        # The pass this roll actually fired in, not w.segment -- which has already been
        # reset to 1 by the time these tasks are built. That reset is why the earlier
        # version of this rule was a silent no-op: relocations fire in the FIRST
        # rollover pass (they carry no round trigger at all), and Unity's marks show
        # them created at d2r0 and expiring at d2r2, two ticks later.
        if day_changed and born_in == 0:
            t.fresh = False
        w.tasks.next_id += 1
        w.tasks.add(t)
        w.generated_specs[t.task_id] = (task_id, facility, spec)


_POSITIONS = {}


from .floodmap import pack as _pack_cell
_PACKED_ROADS = {_pack_cell(c[0], c[1]): c for c in roads.ROAD_CELLS}


def _facility_positions(spec=None):
    """facility name -> transform position, for the per-facility flood trigger.

    The map dump keys transforms by BUILDING name (Community01) and trigger facilities by
    display name (Community Charleston); the two share a road cell, which is the join. A
    facility without a dumped transform (one built mid-episode) falls back to the centre of
    its cell, which is at most ~1.6 units off and only matters at the edge of the square.
    """
    from .roads import DEFAULT_MAP, cell_to_world
    m = spec or DEFAULT_MAP
    key = id(m)
    if key in _POSITIONS:
        return _POSITIONS[key]
    by_cell = {tuple(c): n for n, c in m.building_cell.items()}
    out = {}
    for name, cell in m.facility_cell.items():
        bname = by_cell.get(tuple(cell))
        pos = m.building_pos.get(bname) if bname else None
        out[name] = tuple(pos) if pos else cell_to_world(tuple(cell), m)
    for bname, pos in m.building_pos.items():
        out.setdefault(bname, tuple(pos))
    _POSITIONS[key] = out
    return out


def _land(w, _task_id, quantity, destination):
    """One landed delivery: population moves, counters, client arrivals queued."""
    dest = str(destination or "")
    if dest.startswith("__food__"):
        task = w.tasks.awaiting.get(_task_id) or w.tasks.active.get(_task_id)
        spec = (w.generated_specs.get(_task_id) or (None, None, {}))[2]
        choice = next((c for c in (spec.get("choices") or [])
                       if c.get("choiceId") == (task.chosen_id if task else None)), None)
        # Already pulled by the LoadCargo check; pulling again would double-charge the
        # kitchen and let a 200-pack kitchen fill four orders.
        sourced = (quantity if (choice or {}).get("immediateDelivery")
                   else w._sourced_now.pop(_task_id, 0))
        if sourced:
            w.economy.add_food(dest[len("__food__"):], sourced)
        if task is not None:
            task.delivered = sourced
        return
    if dest.startswith("__cw__"):
        # One trip of a casework order landing at a specific site. LoadCargo took the
        # people out of the requesting facility (charged here, at the port's landing);
        # AddResource deposits them at the site up to its capacity; then the tracker
        # removes them from the source TWICE -- UnloadCargo's HandlePopulationDelivery
        # with the actual amount (gated > 0), and the completion handler one tick later
        # with the nominal amount. The second is queued so a split landing can defer it
        # past this round's tracker, exactly as the nominal client group is.
        source = w.tasks._sources.get(_task_id, "")     # every trip, so no pop
        site = dest[len("__cw__"):]
        loaded = -w.economy.move_population(source, -quantity) if source else quantity
        actual = w.economy.move_population(site, loaded)
        if actual > 0 and source:
            w.clients.process_home(source, actual, w.economy.counters)
        if source:
            w.pending_removals.append((source, quantity))
        if source == "Motel":
            w.economy.motel_pop = w.economy.motel_population
        return
    # Every trip debits its own load, so the source is looked up, not popped: a multi-site
    # order lands two trips and the second used to find no source.
    source = getattr(w.tasks, "_sources", {}).get(_task_id, "")
    if source:
        w.economy.move_population(source, -quantity)
    if dest.startswith("Shelter:"):
        dest, _named = "Shelter", dest[len("Shelter:"):]
    else:
        _named = None
    if dest in ("Motel", "Shelter"):
        # People land in an actual facility, so its population -- and therefore the
        # triggers that read it and the bill that charges it -- move together.
        # No fallback. GetDestinationsSorted is called with includeShelters /
        # includeMotels taken from the CHOICE, so a Shelter-destination relocation
        # never spills into the motel -- it simply has nowhere to go. The port used to
        # fall back and that quietly moved people Unity would have left in place.
        target = "Motel" if dest == "Motel" else (_named or next(
            (b["name"] for b in w.economy.buildings
             if b["type"] == "Shelter" and b["status"] == "InUse"), None))
        moved = w.economy.move_population(target, quantity) if target else 0
        # TWO ClientGroups PER POPULATION DELIVERY. Unity registers the arrival from two
        # unrelated call sites and neither knows about the other:
        #   Vehicle.UnloadCargo -> HandlePopulationDelivery   count = ACTUAL, gated > 0
        #   DeliverySystem.OnVehicleDeliveryCompleted         count = NOMINAL, ungated
        # The centralized hook's own comment lists the scattered branches it replaced and
        # omits DeliverySystem, whose legacy branch was never deleted. Only the TRACKER is
        # duplicated -- the population storage is deposited once by AddResource -- which is
        # why the port's lodging spend was already exact while its client draws were half
        # of Unity's. The counts differ once the motel clamps: actual is post-clamp, nominal
        # is not, so a near-full facility draws different numbers from the two groups.
        # The group is tagged with the SPECIFIC facility (Shelter_5, Motel), not the
        # category: RemoveClientsByQuantity filters by currentFacility == source, so a
        # casework delivery from Shelter_5 must find Shelter_5's groups and nobody else's.
        if target:
            if moved:
                w.pending_arrivals.append((moved, target))
            w.pending_arrivals.append((quantity, target))
        if dest == "Motel":
            w.economy.motel_pop = w.economy.motel_population
    elif dest == "Kitchen":
        pass



def _incomplete_penalties(w: World, expired) -> None:
    """ApplyTaskPenalties: an overdue Emergency/Demand task (ExpireTask -> Incomplete, or an
    InProgress one via SetTaskIncomplete) applies the task's own impact list. The values
    are MEASURED from the headless logs (corpus "incompletePenalty"): every incomplete
    community food request is "Recorded budget change: 1" and satisfaction -1, 108 of 108
    across the captures; a relocation costs satisfaction only. The TaskData assets in the
    working tree list other impacts than the build applies, so the log is the source."""
    table = _INCOMPLETE_PENALTY
    for tid in expired:
        entry = w.generated_specs.get(tid)
        if not entry or entry[2].get("taskType") not in ("Emergency", "Demand"):
            continue
        if entry[0] == ROAD_BLOCKAGE_SPEC_ID:
            # ApplyTaskPenalties on the task's own impact (-20), then FloodTaskGenerator's
            # OnAnyTaskCompleted abandonment penalty (-30) when the clients were aboard.
            drop = 20.0 + (_BLOCKAGE_ABANDON_PENALTY if entry[2].get("_loaded") else 0.0)
            w.economy.satisfaction = max(0.0, min(100.0, w.economy.satisfaction - drop))
            continue
        pen = table.get(entry[0])
        if not pen:
            continue
        if pen.get("budget"):
            w.economy.budget = max(BUDGET_MIN, min(BUDGET_MAX, w.economy.budget + int(pen["budget"])))
        if pen.get("satisfaction"):
            w.economy.satisfaction = max(0.0, min(100.0, w.economy.satisfaction + float(pen["satisfaction"])))


def step_round(w: World, marks=None, on_flood_enter=None, arrivals=()) -> None:
    """Advance one round: segment bookkeeping, then generation, then flood.

    Generation runs BEFORE flood, so a task generated this round sees last round's flood.
    Running it after would still give the right draw count and the wrong game.

    `on_flood_enter(w)` fires at exactly the instant Unity emits its flood:enter mark --
    after the segment advance, weather and generation, before the flood update. The
    equivalence test compares there rather than at the end of the round, because that is
    the only point where the captured state and the port's state describe the same moment.
    Comparing at the round end instead is an off-by-one that silently passes on days when
    the flood set is empty.

    THE FULL DRAW ORDER WITHIN A ROUND, read off the instrumented trace rather than
    inferred:

        arrivals      caseworkNeed x N PEOPLE, then one stayDuration, per delivered group
        casework gen  caseworkGen, once per group that has not yet requested casework
        generation    TaskTrigger.probability, on the segments that run a pass
        flood         the ten flood sites

    `arrivals` may be passed in explicitly (a test injecting a scenario), but the default is
    that the surrogate DERIVES them: a delivery that landed at the end of last round becomes
    a client arrival at the start of this one. Deliveries land after flood and arrivals draw
    before it, so the one-round offset is the mechanic, not a convenience.

    The per-person granularity is load-bearing: a 300-person relocation advances the stream
    301 places, so getting it wrong makes every later draw in the round read someone else's
    randoms."""
    # DELIVERIES SIMULATE AT THE HEAD OF THE STEP, BEFORE THE SEGMENT ADVANCE.
    # The draw-for-draw mark diff settles this. Unity's step 6 on seed 5901 reads
    #   caseworkNeed x100, stayDuration, caseworkNeed x100, stayDuration, caseworkGen x2,
    #   TaskTrigger x3, Flood...
    # and the port's read was that same tail with the whole client block missing, because
    # the tick sat at the END of the step and its arrivals were deferred to the next one.
    # Unity's vehicles finish during the simulation phase (f307-f315), which precedes the
    # segment advance that runs the tracker update and generation (f319). Ticking here and
    # consuming pending_arrivals immediately below puts the client draws where Unity has
    # them. The flooded set read here is deliberately the PREVIOUS round's post-spread set:
    # the vehicles drove before this round's flood update, so that is the map they saw.
    w.tasks.flooded = w.flooded_road_cells()      # post-spread, for this round's driving
    _late = []
    _late_nominal = []                # OnVehicleDeliveryCompleted groups that fire at +35
    _late_removals = []               # ... and its casework removals, same tick
    for _e in w.tasks.tick(w.economy.counters):
        if len(_e) > 3 and _e[3] == "late":
            _late.append(_e)          # epilogue unload: after this round's invoke
            continue
        _n = len(w.pending_arrivals)
        _m = len(w.pending_removals)
        _land(w, _e[0], _e[1], _e[2])
        if len(_e) > 3 and _e[3] == "split":
            # Unloaded on the last movement frame: the actual group registers now, the
            # nominal group (queued last by _land) at completion, after the flood.
            if len(w.pending_arrivals) > _n:
                _late_nominal.append(w.pending_arrivals.pop())
            if len(w.pending_removals) > _m:
                _late_removals.append(w.pending_removals.pop())
        else:
            # Completion is the tick after unload, BEFORE the next vehicle's unload, so a
            # casework landing's two removals hit the tracker back to back (5503: 23, 23,
            # 19, 19 -- not 23, 19, 23, 19). Batching them after the loop hits different
            # groups and deducts the needy count from the wrong one.
            _apply_removals(w)
    _apply_removals(w)
    arrivals = list(arrivals) + w.pending_arrivals
    w.pending_arrivals = []
    # ARRIVAL STAMP vs UPDATE ROUND. Unity stamps arrivalRound = currentRound at the DELIVERY
    # instant, which is still the pre-advance segment; CheckClientStayDurations then runs after
    # OnRoundChanged has bumped currentRound. So the first caseworkGen draw sees Y = 1, and each
    # later round adds one. Stamping and evaluating at the same index made every later Y one
    # short, and Y drives the threshold 10 * 1.5^(Y-1) -- identical randoms, different outcomes.
    for count, facility in arrivals:
        # Unity stamps arrivalRound at the DELIVERY instant, which is still pre-advance.
        w.clients.register_arrival(w.rng, count, _unity_round(w), facility, marks)

    rolls = []
    day_changed = w.segment >= ROUNDS_PER_DAY
    if day_changed:
        w.day += 1
        # Unity's marks run d1r0..d1r4, d2r0, d2r1 -- there IS a segment 0 on every day,
        # and tasks gated on `round: targetRound 0` fire there. Daily Budget Allocation is
        # one, and it grants +5000 budget a day: Unity has it on the board at round 5 and
        # the port did not, because the port jumped segment 4 -> 1 and never visited 0
        # again after day 1.
        #
        # But segment 0 is NOT a fifth player round. Its frame span is 5-7 against 34 for a
        # real round, so it is the rollover instant, which the port already models as
        # _ROLLOVER_PASSES. Making it a separate segment added a round per day and broke
        # the draw census at d2r2 -- and that census, chaining the RNG state from round to
        # round, is the strongest equivalence signal available. So the rollover EVALUATES
        # as segment 0 for trigger purposes and then settles on 1, which fires the round-0
        # tasks without inventing a round.
        w.segment = 0
        w.weather = generate_weather(w.rng, marks=marks)
        # The two rollover passes are not the same instant. Unity's day change fires the
        # round-0 tasks (Daily Budget Allocation: `targetRound 0, exactMatch`) and then the
        # first round's tasks (the advisories: `targetRound 1, exactMatch False`, i.e.
        # round >= 1). Running BOTH passes at segment 0 meant the second class could never
        # fire at all -- Training Recommendation Alert and Workforce Optimization Alert
        # never appeared, which is two of the three tasks missing from the port's round-5
        # board. So pass 1 evaluates as segment 0 and pass 2 as segment 1.
        # Worker arrivals complete AT the day change, and the rollover's task generation
        # reads the post-arrival counts: Unity's totalWorkers is 10 through turn 3 and 35
        # at turn 4, and Training Recommendation Alert needs trained/untrained < 1, which
        # only holds once the hires land (5/30 = 0.17, against 5/5 = 1.0 before). The port
        # generated first and settled the economy afterwards, so it evaluated that trigger
        # against a ratio of exactly 1.0 and the task never fired.
        #
        # on_day_end is hoisted here rather than reordered inside economy.step_round, whose
        # phase order is pinned against captures: day-end must precede the round
        # accumulators (or arrivals lose two idle-worker units) and must precede this
        # round's transfers landing (or the motel is over-billed by a day). Generation runs
        # before both, so calling it here keeps both invariants and economy_step is told the
        # day is already handled.
        w.economy.on_day_end(w.day)
        # WeatherReportSystem.OnTimeSegmentChanged fires GenerateDailyReport when the new
        # round is 0, creating "Day N Start of Day Report" straight through
        # TaskSystem.CreateTask -- not through the trigger inventory, which is why no amount
        # of trigger work could produce it. TaskType.Alert, so roundsRemaining is 2, and it
        # carries no choices, so it cannot move a counter. It DOES consume a task id, and
        # task identity is what the exact-replay suite cannot otherwise align. It draws no
        # randoms, so the census is untouched.
        w.tasks.add(Task(w.tasks.next_id, "None", 0, _ALERT_ROUNDS))
        w.generated_specs[w.tasks.next_id] = (
            "Daily_Report", None, {"taskId": "Daily_Report",
                                   "taskTitle": f"Day {w.day} Start of Day Report",
                                   "taskType": "Alert", "taskTag": "None", "choices": []})
        w.tasks.next_id += 1
        for i in range(_ROLLOVER_PASSES):
            w.segment = i
            # OnTimeSegmentAdvanced fires per ADVANCE, and the rollover advances twice, so a
            # rounds=2 task created before it ages 2 -> 0 and expires inside this step.
            w.tasks.age()
            _tracker(w, marks)
            _r = [r + (i,) for r in _pass(w, marks)]
            rolls += _r
            if w.use_generation:
                _create_tasks(w, _r, day_changed)
            # BuildingResourceStorage.OnRoundChanged is subscribed after TaskSystem's, so on
            # the same invoke consumption runs AFTER the generation pass: at the rollover's
            # segment 0 the pass sees pre-consumption stock, and segment 1's sees the drained
            # communities. That split is why Unity requests food for one community at pass 0
            # and the other two at pass 1 -- and the queue order that follows from it decides
            # which vehicle is left for a stranded assignment three rounds later.
            w.economy.production_tick()
            w.economy.consumption_tick()
            # CheckExpiredTasks runs on the Update AFTER the advance: the dying task held its
            # slot through the generation pass above.
            _incomplete_penalties(w, w.tasks.expire(w.economy.counters))
        w.segment = 1
    else:
        w.segment += 1
        # SEGMENT 4 HAS NO INVOKE. GlobalClock.AdvanceTimeSegment returns early once the
        # segment reaches roundsPerDay, before OnTimeSegmentChanged fires, so a day's invokes
        # are 0, 1, 2, 3: nothing subscribed to the clock -- ageing, the tracker, generation,
        # consumption -- runs on the last round of a day. The port aged and expired tasks
        # there, one decrement per day too many.
        if w.segment < ROUNDS_PER_DAY:
            w.tasks.age()
        _tracker(w, marks)
        if w.segment in _GENERATION_SEGMENTS:
            _r = [r + (w.segment,) for r in _pass(w, marks)]
            rolls += _r
            if w.use_generation:
                _create_tasks(w, _r, day_changed)
        if w.segment < ROUNDS_PER_DAY:
            w.economy.production_tick()
            w.economy.consumption_tick()
            _incomplete_penalties(w, w.tasks.expire(w.economy.counters))
    w.generated = rolls
    # THE JOIN THAT MAKES THE SURROGATE SELF-DRIVING. generation_pass decides WHICH tasks
    # fire; without this the port produced a list of ids and created nothing, so it could
    # generate a task and never answer one -- which is why every equivalence test so far
    # has had to feed it Unity's own task lifecycle.

    if on_flood_enter is not None:
        on_flood_enter(w)
    # THE FLEET ROUTES AGAINST THE POST-SPREAD FLOOD, and the snapshot is taken after the
    # update for that reason. Unity's phases are: answers happen in the planning phase
    # against the flood as it stands, THEN the round simulates -- the flood ticks at the
    # start of it -- and only then do vehicles drive. Its own marks show the split: the
    # Community03 order passed its route estimate at f291 and was blocked at f295, four
    # frames later, by water that had arrived in between.
    #
    # The port had this backwards: it snapshotted before generation and before the update,
    # so dispatch routed against water that was already stale by the time vehicles moved.
    update_flood(w.flood, w.fmap, w.rng, w.weather,
                 RAIN_INTENSITY[w.weather], marks)

    # Deterministic bookkeeping runs after the stochastic phases: deliveries land, tasks
    # age and expire, and the economy accumulates. None of this draws, so its position
    # relative to flood cannot desynchronise the stream -- only the counters.
    # Deliveries that land now produce client arrivals NEXT round, and only into lodging
    # buildings -- a delivery to a casework site sends people home instead, which is the
    # caseworkProcessed path rather than a new tracked group.
    # Food lands at the DESTINATION named by the choice -- a Shelter -- not back at the
    # community that asked. With no operational shelter the food goes nowhere, the
    # community's `FoodPacks Empty` condition stays true, and it keeps requesting. That is
    # why Unity fires 35 food requests in 24 rounds and why foodFulfilled sits far below
    # foodResolved. Stocking the requester instead silenced it after one delivery (6
    # passes against 35).
    # EPILOGUE LANDINGS. A delivery that unloads on the paused frame after the round
    # lands after the invoke and the flood update, so its people move and its clients spawn
    # HERE -- their caseworkNeed/stayDuration draws follow the flood draws, and the group
    # joins the tracker behind this step's caseworkGen rolls, which is the order Unity's
    # marks show (5601 step 30, 6101 step 11). Registered now rather than at the next head so
    # the group precedes the next round's sim-phase arrivals.
    for count, facility in _late_nominal:
        w.clients.register_arrival(w.rng, count, _unity_round(w), facility, marks)
    w.pending_removals.extend(_late_removals)
    _apply_removals(w)
    for _e in _late + w.tasks.settle_late(w.economy.counters):
        _n = len(w.pending_arrivals)
        _land(w, _e[0], _e[1], _e[2])
        for count, facility in w.pending_arrivals[_n:]:
            w.clients.register_arrival(w.rng, count, _unity_round(w), facility, marks)
        del w.pending_arrivals[_n:]
        _apply_removals(w)
    economy_step(w.economy, False, w.day)   # day-end already run above
    w.round_index += 1


def _apply_removals(w):
    """The completion-handler removals queued by casework landings (draw-free)."""
    for facility, n in w.pending_removals:
        w.clients.process_home(facility, n, w.economy.counters)
    w.pending_removals = []


def _park_blocked(w: World, task_id, task, choice_id) -> bool:
    """A MULTI-DELIVERY choice whose delivery could not be created.

    Two exits, by the choice's enableMultipleDeliveries flag (TaskData assets):
      * multi (kitchen food 0/1, shelter relocation 0/2, flood-damage choices):
        ExecuteGeneratorDelivery routes to ExecuteMultipleDeliveries and returns -1
        UNCONDITIONALLY, so a blocked route still logs "No delivery subtasks", applies
        the impacts and sets the task InProgress with nothing linked: it leaves the choice
        list, keeps its slot, and expires Incomplete on its own deadline (5901 validation,
        task 25: blocked at round 9, resolved Incomplete three rounds later).
      * single (motel relocation 1/3): the handler returns false -> "Delivery Blocked" ->
        no impacts, task stays listed and can be re-answered next round (5701 task 9:
        blocked at step 5, re-answered and queued at step 6). That is the caller's
        `return False` path, not this function.
    """
    task.chosen_id = choice_id
    w.tasks.active.pop(task_id, None)
    w.tasks.awaiting[task_id] = task
    return True


def answer(w: World, task_id, choice_id) -> bool:
    """Answer a generated task by choice id, using the exported choice definition.

    This is the surrogate's equivalent of env.choose(): it applies the choice's budget and
    satisfaction impacts, queues its delivery with the right latency, and takes the task
    off the board. Without it a self-driven episode can only ever let tasks expire."""
    # A vehicle-repair task has no generated spec -- it is spawned by the flood damaging a
    # vehicle, not by a trigger -- so it is dispatched before the spec lookup below, which
    # would otherwise reject it and leave the fleet permanently short.
    if task_id in w.tasks.repair_for:
        repaired = w.tasks.answer_repair(task_id, choice_id, w.economy.counters)
        if repaired:
            w.economy.spend(w.tasks.REPAIR_COST, "other")
        else:
            w.economy.satisfaction = max(
                0, w.economy.satisfaction + w.tasks.REPAIR_DELAY_SATISFACTION)
        return True

    entry = w.generated_specs.get(task_id)
    task = w.tasks.active.get(task_id)
    if entry is None or task is None:
        return False
    _def_id, _facility, spec = entry
    choice = next((c for c in (spec.get("choices") or [])
                   if c.get("choiceId") == choice_id), None)
    if choice is None:
        return False
    # DEMANDED vs SENDABLE. ClientRelocationHandler:
    #     int available = GetPopulation(source);
    #     int toSend    = requestedQuantity > 0 ? Mathf.Min(requestedQuantity, available)
    #                                           : available;
    #     ...
    #     int sendAmount = Mathf.Min(remaining, effectiveSpace);
    #
    # The task's DEMAND is what the choice promised and is what lodgingResolved counts; the
    # people who actually move are capped by the source's remaining population and by space
    # at the destination, and that is what lodgingFulfilled counts. Unity's own totals are
    # 901 resolved against 600 fulfilled -- a third of demanded relocations never land.
    # Delivering the promised number instead both inflates fulfilment AND empties the
    # communities, which then silences every population-threshold trigger: the port drained
    # all three to zero by round 8 while Unity ended near 200 each.
    demanded = choice.get("deliveryQuantity", 0) or 0
    qty = demanded
    dest_cat = choice.get("destinationCategory") or ""
    def _land_now(kind, amount, where):
        """Apply an IMMEDIATE delivery's side effect.

        tasks.answer() credits an immediate delivery and resolves the task in the same
        call, so it never passes through tick()'s landing hook -- which is where the world
        actually changes. Without this, an immediate food delivery scored as fulfilled
        while the community's foodPacks stayed at 0, so its `Empty` condition never stopped
        holding and it requested food forever: 30 food tasks resolved against Unity's 15."""
        if amount <= 0:
            return
        if kind == "food":
            w.economy.add_food(where, amount)
        elif kind == "people":
            w.economy.move_population(where, amount)

    if task.tag == "Food" and demanded > 0:
        # Food lands at the requester, so its `FoodPacks Empty` condition stops holding --
        # which is how Unity's food requests for that community stop.
        task.source = ""                      # food does not move people out of anywhere
        immediate = bool(choice.get("immediateDelivery"))
        # An infeasible choice is still ANSWERABLE; it just delivers nothing, and the task
        # resolves unfulfilled when the delivery comes due.
        # Sourcing happens when the delivery LANDS, not when the order is placed. Measured:
        # Unity's kitchen is stocked by the day reset at the END of the round a food order
        # is answered, and that order still fulfils the next round -- so the food is pulled
        # at arrival. Sourcing at answer time made the order fail against an empty kitchen
        # and left the port a round behind (unity resolved 1 at round 5, port 0).
        task.chosen_id = choice_id
        # No latency is computed here any more. The order is placed; the FLEET decides when
        # it arrives, during the round, exactly as ProcessPendingTasks does. Costing the
        # trip at answer time is what made the port land five orders in a round where Unity
        # landed two.
        _kitchen = next((b["name"] for b in w.economy.buildings
                         if b["type"] == "Kitchen" and b["status"] == "InUse"), None)
        # NOT gated on effectiveStock here. FoodDeliveryHandler has TWO exits when it
        # cannot send, and they resolve the task differently: the destination-inbound branch
        # calls CompleteTask (resolved AND fulfilled), while the no-kitchen-stock branch
        # returns false and completes nothing. Gating both the same way made four traces
        # read foodResolved 0 against Unity's 1. Which branch each request takes has to be
        # measured before this is re-attempted -- see diag_orders.
        _lat = None
        # `_lat in (None, False)` was WRONG twice over: the flag was inverted, and
        # `0 in (None, False)` is True because 0 == False in Python -- so a delivery
        # measured as landing THIS round was thrown away and replaced by the fitted 4.
        # Identity tests only.
        _cut = _lat is False
        _measured = _lat is not None and _lat is not False
        # NO reservation at order time. FoodDeliveryHandler's effectiveStock rule reads as
        # though a 200-pack kitchen can only spawn two 100-pack orders, and I implemented
        # that -- then the delivery:queue marks refuted it: Unity creates THREE food orders
        # in a single round, to three different communities, against that same kitchen.
        #
        #   d2r1: created 3 [Community01, Community03, Community02]   completed 1
        #   d3r1: created 3 [Community02, Community01, Community03]   completed 1
        #
        # Creation is not the limiter. Completion is: three vehicles are shared with the
        # population relocations answered in the same round, and each trip is two legs.
        # A DELIVERING CHOICE THAT CANNOT QUEUE ITS DELIVERY IS REJECTED OUTRIGHT.
        # CompleteTaskAction returns false when ExecuteGeneratorDelivery queues nothing:
        # no impacts applied, no SetTaskInProgress, and SelectTaskChoiceHeadless reports the
        # action as failed. The task simply stays on the board, unanswered.
        #
        # That is why Unity turns three answered food requests into ONE delivery. Two of
        # them cannot route from the kitchen -- the flood cuts (-5, 0) -- so those two
        # answers FAIL and their tasks remain active. The port accepted all three.
        if not immediate and _kitchen:
            _src = w._facility_cell(_kitchen)
            _dst = w._facility_cell(str(_facility))
            if (_src is None or _dst is None
                    or roads.path_length(_src, _dst, w.flooded_road_cells()) is None):
                if not choice.get("enableMultipleDeliveries"):
                    return False
                w.economy.apply_choice(task.tag, choice.get("impacts"),
                                       choice.get("budgetDelayRounds", 0) or 0, "", 0)
                return _park_blocked(w, task_id, task, choice_id)
        task.source = _kitchen or ""
        w.tasks.answer(task_id, 0 if _cut else demanded, immediate=immediate,
                       latency=_lat if _measured else None,
                       destination="__food__" + str(_facility),
                       counters=w.economy.counters,
                       latency_measured=_measured,
                       destination_facility=str(_facility))
        if immediate:
            _land_now("food", demanded, str(_facility))
        w.economy.apply_choice(task.tag, choice.get("impacts"),
                               choice.get("budgetDelayRounds", 0) or 0, "", 0)
        return True
    if _def_id == ROAD_BLOCKAGE_SPEC_ID and (choice.get("triggersDelivery")
                                            and not choice.get("immediateDelivery")):
        # INERT IN THE GAME. The food choices (1, 2) and the not-yet-loaded population
        # choice (1) are ManualAssignment deliveries, but ExecuteGeneratorDelivery routes
        # them to FoodDeliveryHandler.Execute / ClientRelocationHandler.Execute, which take
        # the SOURCE from FindTriggeringFacility(task) -- and only the loaded-population
        # case sets affectedFacility. The lookup fails, nothing is queued, CompleteTaskAction
        # returns false: no impacts, the task stays until it expires Incomplete. 5901
        # validation has four such tasks and every one expired.
        return False
    if dest_cat == "CaseworkSite" and (choice.get("triggersDelivery")
                                       or choice.get("immediateDelivery")):
        return _answer_casework(w, task_id, task, choice, choice_id, str(_facility), demanded)
    if (demanded > 0 and dest_cat == "Shelter" and choice.get("enableMultipleDeliveries")
            and choice.get("triggersDelivery") and not choice.get("immediateDelivery")):
        return _answer_multi_shelter(w, task_id, task, choice, choice_id, str(_facility), demanded)
    if demanded > 0 and dest_cat in ("Motel", "Shelter"):
        src = w.economy.facility(str(_facility))
        available = ((src.get("resources") or {}).get("population") or 0) if src else 0
        qty = min(demanded, available)
        target = "Motel" if dest_cat == "Motel" else next(
            (b["name"] for b in w.economy.buildings
             if b["type"] == "Shelter" and b["status"] == "InUse"), "Motel")
        dst = w.economy.facility(target)
        if dst is not None:
            res = dst.get("resources") or {}
            cap = res.get("populationCapacity")
            if cap is not None:
                qty = min(qty, max(0, cap - (res.get("population") or 0)))
    w.economy.apply_choice(task.tag, choice.get("impacts"),
                           choice.get("budgetDelayRounds", 0) or 0,
                           dest_cat, qty)
    # RELOCATION MOVES PEOPLE OUT OF THE SOURCE. Without this the community stays at 400
    # forever, its population-threshold trigger never stops firing, and the port generates
    # relocation demand indefinitely -- the second half of the 2.7x over-generation.
    # Population and food move when the delivery LANDS, not when the choice is made. Doing
    # it at answer time drains the source community several rounds early, which pushes it
    # under the MoreThan-200 threshold and silences the relocation trigger long before
    # Unity's does: the port fell to 4 trigger passes against Unity's 17.
    task.source = str(_facility)
    immediate = bool(choice.get("immediateDelivery"))
    if not (choice.get("triggersDelivery") or immediate):
        # A choice with NO delivery completes the task at answer time: CompleteTaskAction
        # -> CompleteTask, logged "Completed task: Storm Funding Advisory" in the same
        # frame as the choice. Parking it for a latency round (the delivery model) kept the
        # global one-per-title slot taken through the next pass, so the port never
        # generated the second advisory Unity did -- and lost its +50000 (5901 validation,
        # round 6). Counters: tag-None tasks resolve silently either way.
        _t = w.tasks.active.pop(task_id, None)
        if _t is not None:
            w.tasks.resolve(_t, fulfilled=False, counters=w.economy.counters)
        return True
    _target = ("Motel" if dest_cat == "Motel" else next(
        (b["name"] for b in w.economy.buildings
         if b["type"] == "Shelter" and b["status"] == "InUse"), "Motel"))
    _lat = None                      # the fleet decides; see the food path above
    # Same rejection for relocations: a delivering choice whose route is cut queues
    # nothing, so CompleteTaskAction returns false and the task stays on the board.
    if not immediate and qty > 0:
        _src = w._facility_cell(str(_facility))
        _dst = w._facility_cell(_target)
        if (_src is None or _dst is None
                or roads.path_length(_src, _dst, w.flooded_road_cells()) is None):
            # impacts were applied above, as CompleteTaskAction does before InProgress
            return (_park_blocked(w, task_id, task, choice_id)
                    if choice.get("enableMultipleDeliveries") else False)
    _cut = _lat is False
    _measured = _lat is not None and _lat is not False
    # ONE FLOOD SET FOR BOTH ROUTE CHECKS. The check above used the CURRENT post-update tiles,
    # which is what Unity's choice-time route check reads; TaskBoard.answer re-checks against
    # self.flooded, which was captured at the previous head-of-step tick, BEFORE that round's
    # flood update. When the flood receded in between (5701 round 6: 4 cells -> 2) the outer
    # check passed and the inner one failed, so the task was parked in awaiting with no order
    # and the answer still returned True -- a relocation Unity delivered late and the port
    # never dispatched. The fleet reads this same set at the next tick, so refreshing it here
    # changes nothing for driving.
    w.tasks.flooded = w.flooded_road_cells()
    _multi = bool(choice.get("enableMultipleDeliveries"))
    if immediate and qty <= 0 and not _multi:
        # ExecuteImmediate moved nobody -> CompleteTaskAction's "moved != 0" fails ->
        # return false: no impacts, task stays listed. A multi-delivery immediate completes.
        return False
    w.tasks.answer(task_id, 0 if _cut else qty, immediate=immediate,
                   latency=_lat if _measured else None,
                   destination=dest_cat, counters=w.economy.counters,
                   latency_measured=_measured,
                   destination_facility=_target,
                   credit_delivered=immediate and not _multi)
    if immediate and qty > 0 and dest_cat in ("Motel", "Shelter"):
        target = "Motel" if dest_cat == "Motel" else next(
            (b["name"] for b in w.economy.buildings
             if b["type"] == "Shelter" and b["status"] == "InUse"), None)
        if target:
            w.economy.move_population(str(_facility), -qty)
            moved = w.economy.move_population(target, qty)
            # ClientRelocationHandler does the same double-registration, calling
            # RegisterClientArrival and HandlePopulationDelivery back to back on one transfer.
            if moved:
                w.pending_arrivals.append((moved, target))
            w.pending_arrivals.append((qty, target))
            w.economy.motel_pop = w.economy.motel_population
    return True


def _answer_casework(w: World, task_id, task, choice, choice_id, facility, demanded) -> bool:
    """ExecuteSingleSourceMultiDest for a casework request.

    Destinations: operational CaseworkSites with population space, FindObjectsOfType
    order (constructed newest first), at most three. None -> "No suitable destinations":
    the +10 still applies and the task parks InProgress with nothing linked (5503, task
    10). Otherwise `quantityPerDest = max(1, total / n)` and one delivery per site;
    CreateDeliveryTask refuses a site whose route is cut, and if every site is cut the
    task parks the same way. The quantity is the choice's Fixed deliveryQuantity -- the
    group's needy count -- not capped by the source here; LoadCargo caps it."""
    sites = _casework_sites(w)
    w.economy.apply_choice(task.tag, choice.get("impacts"),
                           choice.get("budgetDelayRounds", 0) or 0, "CaseworkSite", 0)
    if not sites:
        return _park_blocked(w, task_id, task, choice_id)
    per = max(1, demanded // len(sites))
    src = w._facility_cell(facility)
    flooded = w.flooded_road_cells()
    legs = []
    for name in sites:
        dst = w._facility_cell(name)
        if src is None or dst is None or roads.path_length(src, dst, flooded) is None:
            continue
        legs.append((per, dst, "__cw__" + name))
    if not legs:
        return _park_blocked(w, task_id, task, choice_id)
    task.source = facility
    task.chosen_id = choice_id
    w.tasks.flooded = flooded
    w.tasks.answer_multi(task_id, src, legs)
    return True


def _answer_blockage_food(w: World, task_id, task, choice, choice_id, spec) -> bool:
    """Road blockage, food cargo: 1 = same kitchen again (ManualAssignment endpoints),
    2 = the first operational kitchen holding food. Both are single vehicle orders; the
    cargo lands through the __food__ path with no counter credit (tag None). Unverified
    against a capture."""
    facility = spec.get("_dest", "")[len("__food__"):].split("|")[0]
    if choice_id == 1:
        kitchen = task.source
    else:
        kitchen = next((k["name"] for k in w.economy.operational("Kitchen")
                        if ((k.get("resources") or {}).get("foodPacks") or 0) > 0), None)
    src = w._facility_cell(kitchen) if kitchen else None
    dst = w._facility_cell(facility)
    flooded = w.flooded_road_cells()
    if src is None or dst is None or roads.path_length(src, dst, flooded) is None:
        return False
    task.source = kitchen
    task.chosen_id = choice_id
    w.tasks.flooded = flooded
    w.tasks.answer_multi(task_id, src, [(choice.get("deliveryQuantity") or 0, dst, "__food__" + facility)])
    w.economy.apply_choice(task.tag, choice.get("impacts"), 0, "", 0)
    return True


def _answer_multi_shelter(w: World, task_id, task, choice, choice_id, facility, demanded) -> bool:
    """A multi-delivery "Send to Shelters" (Community_TransportRequest 0, the flood-damage
    choices): ExecuteSingleSourceMultiDest, not ClientRelocationHandler.Execute.

    Destinations are the operational shelters with ANY space (CanBuildingHandleCargo,
    no inbound subtraction), FindObjectsOfType order (newest first), Take(3); the
    quantity is the choice's Fixed deliveryQuantity split max(1, total / n) per site --
    NOT capped by the shelter's space or the source's population. 5503 validation, step
    13: Shelter_0 at 77/100 and Unity queues 100; the vehicle loads 100, the shelter takes
    23 on unload, and the parent is credited the nominal 100. The port used to cap the
    order at the space (23), which under-credited the landing and every downstream event
    (the stranded-cargo credit, the emergency transport's quantity)."""
    w.economy.apply_choice(task.tag, choice.get("impacts"),
                           choice.get("budgetDelayRounds", 0) or 0, "Shelter", demanded)
    sites = []
    for b in [x for x in w.economy.buildings if x["type"] == "Shelter"][::-1]:
        if b["status"] != "InUse":
            continue
        res = b.get("resources") or {}
        cap = res.get("populationCapacity")
        if cap is not None and cap - (res.get("population") or 0) <= 0:
            continue
        sites.append(b["name"])
        if len(sites) >= _CASEWORK_DESTS:
            break
    if not sites:
        return _park_blocked(w, task_id, task, choice_id)
    per = max(1, demanded // len(sites))
    src = w._facility_cell(facility)
    flooded = w.flooded_road_cells()
    legs = []
    for name in sites:
        dst = w._facility_cell(name)
        if src is None or dst is None or roads.path_length(src, dst, flooded) is None:
            continue
        legs.append((per, dst, "Shelter:" + name))
    if not legs:
        return _park_blocked(w, task_id, task, choice_id)
    task.source = facility
    task.chosen_id = choice_id
    w.tasks.flooded = flooded
    w.tasks.answer_multi(task_id, src, legs)
    return True


def _casework_sites(w: World):
    """FindMultipleDestinations(SpecificBuilding=CaseworkSite): operational, has space,
    newest first, Take(3)."""
    out = []
    built = [b for b in w.economy.buildings if b["type"] == "CaseworkSite"]
    for b in built[::-1]:
        if b["status"] != "InUse":
            continue
        res = b.get("resources") or {}
        cap = res.get("populationCapacity")
        if cap is not None and (res.get("population") or 0) >= cap:
            continue
        out.append(b["name"])
        if len(out) >= _CASEWORK_DESTS:
            break
    return out


def _has_destination_space(w: World, choice) -> bool:
    """TaskSystem.BuildTaskContext's population-relocation gate, transcribed:

        bool toShelter = c.destinationType != DeliveryDestinationType.SpecificPrebuilt
                      || c.destinationPrebuilt != PrebuiltBuildingType.Motel;
        bool toMotel   = c.destinationType == DeliveryDestinationType.SpecificPrebuilt
                      && c.destinationPrebuilt == PrebuiltBuildingType.Motel;
        if (!toShelter && !toMotel) { toShelter = true; toMotel = true; }
        if (!HasDestinationSpace(task, toShelter, toMotel)) continue;

    Verified against a capture: the transport task offered choiceIds (1, 3) -- the two
    Motel options -- and never (0, 2), because no shelter existed to receive them. So an
    agent that answers "the first choice" is answering a MOTEL relocation, not a shelter
    one. That single fact accounts for Unity housing 600 people and generating 898 casework
    requests where the port, offering the shelter option, delivered nobody."""
    # Space is RAW SPACE MINUS INBOUND, as GetDestinationsSorted computes it. Ignoring
    # what is already on its way let two relocations both target a motel that only has room
    # for one, so the port delivered both where Unity creates one order and resolves the
    # other unfulfilled.
    to_motel = (choice.get("destinationCategory") or "") == "Motel"
    if to_motel:
        m = w.economy.facility("Motel")
        res = (m or {}).get("resources") or {}
        cap = res.get("populationCapacity")
        if m is None:
            return False
        if cap is None:
            return True
        free = cap - (res.get("population") or 0) - w.tasks.inbound_to("Motel")
        return free > 0
    for b in w.economy.buildings:                    # shelter-bound
        if b["type"] != "Shelter" or b["status"] != "InUse":
            continue
        res = b.get("resources") or {}
        cap = res.get("populationCapacity")
        if cap is None:
            return True
        if cap - (res.get("population") or 0) - w.tasks.inbound_to(b["name"]) > 0:
            return True
    return False


def _offered(w: World, choice, tag) -> bool:
    """Is this choice present in the payload at all?

    Only POPULATION relocation is gated, and casework is explicitly exempt -- the C#
    comment is emphatic that "send to casework site" is return-home processing, not a
    shelter relocation, "so it must NOT be gated on shelter space". FOOD choices are never
    gated, which is why a capture offered all three food options (0, 1, 2) including
    kitchen orders that then failed for lack of stock: 9 of 15 fulfilled.

    So there are two distinct mechanisms and conflating them was the error in both
    directions -- gating food hid its failure mode, and not gating relocation offered
    choices Unity withholds."""
    dest = choice.get("destinationCategory") or ""
    if not dest or not (choice.get("triggersDelivery") or choice.get("immediateDelivery")):
        return True
    if dest == "CaseworkSite":
        return True
    if tag == "Food":
        return True
    return _has_destination_space(w, choice)


def _source_food(w: World, quantity) -> int:
    """Pull food packs out of the operational kitchens, up to `quantity`.

    DeliverySystem moves the food OUT of the kitchen, so a 200-pack kitchen fills two
    100-pack orders per day and no more -- and it is pulled when the delivery executes,
    which is why an order placed on the round the kitchen is first stocked still fulfils."""
    remaining, sourced = quantity, 0
    for k in w.economy.operational("Kitchen"):
        if remaining <= 0:
            break
        res = k.get("resources") or {}
        take = min(res.get("foodPacks", 0) or 0, remaining)
        if take > 0:
            res["foodPacks"] -= take
            sourced += take
            remaining -= take
    return sourced


def _deliverable(w: World, choice, tag, quantity=0) -> int:
    """Can an OFFERED choice actually deliver -- and if so, CONSUME the source.

    DeliverySystem.CreateDeliveryTask(kitchen, destination, FoodPacks, sendAmount) moves
    food OUT of the kitchen, so a kitchen holding 200 can serve two 100-pack orders per day
    and no more. Checking stock without spending it let one kitchen satisfy unlimited
    orders: the port fulfilled 11-15 food tasks against Unity's 6-9, and every community
    got fed from a single restock.

    Returns the amount actually sourced, so an order that finds a partially stocked kitchen
    delivers what is there rather than all-or-nothing."""
    if tag != "Food":
        return quantity
    if choice.get("immediateDelivery"):
        return quantity                               # external source, unlimited
    remaining = quantity
    sourced = 0
    for k in w.economy.operational("Kitchen"):
        if remaining <= 0:
            break
        res = k.get("resources") or {}
        have = res.get("foodPacks", 0) or 0
        take = min(have, remaining)
        if take > 0:
            res["foodPacks"] = have - take
            sourced += take
            remaining -= take
    return sourced


def open_choices(w: World):
    """Every (task_id, choice_id) the planner may answer this round.

    NOT feasibility-filtered. CheckFeasibility greys choices out in the GUI, but the gym
    payload that BuildTaskContext produces carries every choice: measured on a capture,
    all 15 food tasks offered choiceIds (0, 1, 2) with no filtering, and the policy picked
    the kitchen order every time. An agent can therefore choose something that cannot be
    carried out -- and that is not a no-op, it is a task that resolves UNFULFILLED, which
    is where Unity's 9-of-15 food fulfilment comes from. Filtering here hid that failure
    mode and made every answered task succeed."""
    out = []
    for task_id in w.tasks.active:
        # A vehicle-repair task is spawned by flood damage rather than by a trigger, so it
        # has no generated spec and the loop below would skip it. It still appears on the
        # board and still takes a choice: 1 repairs for $1200, 2 delays at -5 satisfaction.
        # Omitting it left 14 repair tasks created and 0 ever answered, so every damaged
        # vehicle stayed damaged and the fleet drained to nothing.
        if task_id in w.tasks.repair_for:
            out.append((task_id, 1))
            out.append((task_id, 2))
            continue
        entry = w.generated_specs.get(task_id)
        if not entry:
            continue
        task = w.tasks.active.get(task_id)
        for c in (entry[2].get("choices") or []):
            if _offered(w, c, task.tag if task else ""):
                out.append((task_id, c.get("choiceId")))
    return out
