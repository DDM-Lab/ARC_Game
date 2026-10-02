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
from .economy import C as _ECON_C, Economy
from .flood import FloodState, update_flood
from .floodmap import FloodMap
from . import roads
from .tasks import Task, TaskBoard, demand_of, VEHICLE_CAPACITY
from .generation import TriggerContext, generation_pass, suitable_facilities, _food_need
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
         # main-bugfixes 2a435b4e removed the satisfaction impacts from every code-built
         # task choice: answering casework no longer pays +10, waiting no longer costs -10.
         # These are built in ClientStayTracker, not from a TaskData asset, so they are not
         # in the export and have to track the C# by hand.
         "enableMultipleDeliveries": True, "impacts": []},
        {"choiceId": 2, "triggersDelivery": False, "immediateDelivery": False,
         "deliveryQuantity": 0, "budgetDelayRounds": 0, "destinationCategory": "",
         "enableMultipleDeliveries": False, "impacts": []},
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
# CommunityFoodDepletionManager spawns this one directly (its own triggers are empty), so it
# is never produced by a generation pass.
COMMUNITY_FOOD_SPEC_ID = "Community_FoodRequest"
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
from .triggers import roll_pass
from .weather import RAIN_INTENSITY, generate_weather

# The clock (bench-v6 GlobalClock): a gym step is a DECISION. Day 1 is one decision
# (Day1SkipCoroutine: four rounds with time frozen, only OnRoundEnd, then OnSimulationEnded);
# each day rollover is a decision (OnDayChanged, then OnTimeSegmentChanged(0)); each round is a
# decision whose EndSimulation runs OnRoundEnd BEFORE AdvanceTimeSegment, and reaching segment 4
# fires no OnTimeSegmentChanged. A game is 1 + 7 x (1 + 4) = 36 decisions.

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
# Since the A1 clock fix (v1_fixes d17eb70f) the gate is explicit in the source:
# TaskSystem.OnRoundChanged generates when `newSegment < roundsPerDay - 1`, i.e. segments 0,
# 1 and 2 -- day start plus the ticks opening rounds 2 and 3. The rollover step covers 0 and
# 1; a normal step covers 2. Day 1 is the case that needs segment 1 here: it starts at
# segment 0 with no rollover, so its round-2 pass is a normal step (capture merge_v2 s1,
# d1r1: TaskTrigger.probability x3 before any flood draw).
_GENERATION_SEGMENTS = (1, 2)
# TaskSystem.numEmergencyTasks = GameDataManager.InitialEmergencyTaskFrequency, which since
# 3d8c8b00 ("parameter sheet drives every build") is the SHEET's value -- 2, not 0.
#
# It really was 0 before: MainScene's GameDataManager had no configLoader wired, LoadAllData
# took the SetDefaults() branch, and SetDefaults never assigned InitialEmergencyTaskFrequency at
# all, so every database Emergency was "[Limit] Skipping ...: Max emergencies reached" (38 skips,
# 0 creations on 5901). With the loader wired the cap is live, and merge_v4 shows the
# consequence: Unity expires an Emergency Budget Crisis at s17 for satisfaction +1 / budget -1,
# a task the port could not create. Reading the export keeps this correct when the sheet changes.
_NUM_EMERGENCY_TASKS = int((_ECON_C.get("initial_state") or {}).get("emergencyTotal", 0) or 0)

_FINAL_DAY = 8
# TaskSystem.CreateTask's per-type defaults: Emergency 1, Demand 2, Advisory 3, Alert 2.
_ALERT_ROUNDS = 2
# GameTask.deliveryFailureSatisfactionPenalty, the field default. Not exported per task, so a
# TaskData asset that overrides it is not modelled -- flagged rather than guessed.


class World:
    """Everything a round transition reads or writes, and nothing else.

    `facilities_for` is injected rather than derived: it returns the ordered facility list
    a task rolls against, and Unity's order is FindObjectsOfType order, which is neither
    sorted nor creation order (see PLAN.md). Guessing it here would be inventing physics."""

    __slots__ = ("walks", "rng", "flood", "fmap", "weather", "day", "segment",
                 "facilities_for", "generated", "clients", "economy", "tasks",
                 "round_index", "_trigger_memory", "use_generation",
                 "pending_arrivals", "pending_removals", "_casework_live", "generated_specs", "_alerts_shown", "_pos_cache",
                 "_emergency_count", "_last_emergency_round", "_sourced_now", "_pop_loaded",
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
        self.tasks.source_transform = self._built_transform
        self.tasks.on_blocked = self._blocked_delivery
        self._sourced_now = {}      # task -> packs already pulled this round
        self._pop_loaded = {}       # (task, qty, dest) -> [people aboard, per trip, load order]
        self._food_reserved = 0     # packs promised to orders not yet loaded
        self.round_index = 0
        self._trigger_memory = {}       # stateful triggers (FloodExpanded, BudgetDropped)
        self.pending_arrivals = []      # deliveries that landed LAST round, drawn this one
        # ClientRelocationHandler.pendingRelocations: population WALKS now, no vehicle.
        # [rounds_remaining, source, destination, quantity, task_id]
        self.walks = []
        self.pending_removals = []      # (facility, n): casework removals queued by a landing
        self._casework_live = {}        # casework task id -> client group id, until it ends
        self.generated_specs = {}       # live task id -> (definition id, facility, spec)
        self._alerts_shown = set()      # Alert tasks fire once per GAME
        self._emergency_count = 0
        self._pos_cache = None
        # TaskSystem initialises lastEmergencyTaskRound to 0, NOT to "long ago". The gate
        # is `currentRound < lastEmergencyTaskRound + dynamicInterval`, so with an interval
        # of totalRounds/numEmergencyTasks = 32/4 = 8 the FIRST emergency cannot fire
        # before round 8. Seeding this at -99 let the port fire one immediately, and
        # because Community Emergency Evacuation is Lodging-tagged and sits ahead of
        # Population Relocation in the inventory, it took the one-lodging-task-per-facility
        # slot -- which is why the port had an Evacuation at round 5 where Unity had a
        # second Relocation.
        self._last_emergency_round = 0

    def _can_source(self, task, quantity, payload=None):
        """Packs the kitchens could hand a vehicle right now, for LoadCargo's abort test.

        Only food is sourced from a building; a population relocation loads people from the
        community that asked, and that is checked when the choice is made.
        """
        if str(task.destination or "").startswith("__food__") is False:
            # PEOPLE LEAVE THE SOURCE AT THE LOAD, NOT THE LANDING. Vehicle.LoadCargo:
            # actualLoaded = sourceStorage.RemoveResource(Population, quantity); <= 0 aborts
            # (vehicle Idle, task kept); the vehicle carries actualLoaded and UnloadCargo
            # deposits that. Debiting at the landing instead left the people in the Motel
            # across a day change: 5501 step 16, 22 casework clients loaded at f733, the
            # day-5 bill at f750 charged Unity 109,800 and the port 114,200 (22 x $200).
            # The dispatch-time pre-check (no payload) only asks; the load (payload) takes.
            source = (self.tasks._sources.get(task.task_id, "")
                      or getattr(task, "source", "") or "")
            src = self.economy.facility(source) if source else None
            if src is None:
                return quantity          # no modelled source (immediate/external supply)
            have = (src.get("resources") or {}).get("population") or 0
            loaded = min(quantity, have)
            if payload is not None and loaded > 0:
                self.economy.move_population(source, -loaded)
                self._pop_loaded.setdefault(tuple(payload[:3]), []).append(loaded)
                if source == "Motel":
                    self.economy.motel_pop = self.economy.motel_population
            return loaded
        chosen = getattr(task, "chosen_id", None)
        spec = (self.generated_specs.get(task.task_id) or (None, None, {}))[2]
        choice = next((c for c in (spec.get("choices") or [])
                       if c.get("choiceId") == chosen), None)
        if (choice or {}).get("immediateDelivery"):
            return quantity          # external supply, no kitchen involved
        # The trip's own kitchen (LoadCargo removes from sourceBuilding), carried in the
        # payload tag after the '|'; older single-kitchen tags fall back to any kitchen.
        tag = str(payload[2] or "") if payload else str(task.destination or "")
        kitchen = tag.split("|", 1)[1] if "|" in tag else None
        got = _source_food(self, quantity, kitchen)
        self._food_reserved = max(0, self._food_reserved - quantity)
        if got:
            # Accumulate: two trips of one order can both load before either lands.
            self._sourced_now[task.task_id] = self._sourced_now.get(task.task_id, 0) + got
        return got

    def _blocked_delivery(self, payload, loaded, task, was_open):
        """StopVehicleDueToFlood's task side. `task` is the parent (already off the board),
        `loaded` the cargo aboard (0 if not loaded), `was_open` whether HandleDeliveryFailure
        found it InProgress). HandleDeliveryFailure applies no satisfaction penalty on this build."""
        if str(payload[2] or "").startswith("__food__"):
            # FloodTaskGenerator.CreateRoadBlockageTask, food: DiscardVehicleCargo (the meals
            # aboard are wasted: RecordFoodWasted) and ShowFoodBlockageAlert -- an Alert filed
            # under the destination, no choices, no task to answer.
            if loaded:
                self.economy.report.food_wasted += loaded
            _food_blockage_alert(self, payload)
            return
        if loaded:
            # StopVehicleDueToFlood -> ReturnCargoToSource: the people go back where they
            # were loaded from (they left the source at LoadCargo, see _can_source).
            source = (task.source if task is not None else "") or self.tasks._sources.get(payload[0], "")
            back = _take_loaded(self, tuple(payload[:3]), 0)
            if back and source:
                self.economy.move_population(source, back)
                if source == "Motel":
                    self.economy.motel_pop = self.economy.motel_population
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

    def _positions(self):
        """Facility name -> transform, INCLUDING buildings constructed this episode.

        A built building sits on its site's transform, and the per-facility flood triggers
        count tiles in a square around floor(that). The static table only knows the map
        dump's pre-placed buildings, so without this every flood trigger on a built shelter
        evaluated false and Shelter_Flood_Damage could never fire (merge_v6 seed 5503: Unity
        resolves a 100-person Flood Damage Relocation the port never created)."""
        global _SITE_POS
        if _SITE_POS is None:
            _SITE_POS = _site_positions()
        # Rebuilt only when the building list changes -- this runs on every trigger context
        # (several times a round) and the static table is ~20 entries that never move.
        n = len(self.economy.buildings)
        cached = self._pos_cache
        if cached is not None and cached[0] == n:
            return cached[1]
        out = dict(_facility_positions())
        if _SITE_POS:
            for b in self.economy.buildings:
                sid = b.get("site_id")
                if sid is not None and sid in _SITE_POS:
                    out[b["name"]] = _SITE_POS[sid]
        self._pos_cache = (n, out)
        return out

    def _built_transform(self, name):
        """A building constructed this episode -> its site's transform; None for anything the
        map dump already places (Fleet scores those against building_pos). DeliverySystem
        scores a vehicle by its distance to the source TRANSFORM, which for a built building
        sits a cell off its road connection (site 3: (1.5, 2.5) vs road (1.5, 3.5)); scored
        against the road cell, 5504's MCTS plan sent Vehicle 4 where Unity sent Vehicle 1."""
        global _SITE_POS
        if _SITE_POS is None:
            _SITE_POS = _site_positions()
        b = self.economy.facility(name)
        sid = b.get("site_id") if b else None
        return _SITE_POS.get(sid) if sid is not None else None

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
        # GetTotalAvailableWorkforce: workforce UNITS (a trained worker is 2), not heads.
        workforce = 2 * self.economy.free_trained + self.economy.free_untrained
        return TriggerContext(
            day=self.day, segment=self.segment, weather=self.weather,
            flood_tiles=len(self.flood.tiles), budget=self.economy.budget,
            satisfaction=self.economy.satisfaction, free_workforce=workforce,
            idle_ratio=100.0 * free / total, facilities=self.economy.facilities(),
            # WorkerSystem.GetTrainedWorkersCount/GetUntrainedWorkersCount count EVERY
            # Worker object of the type, and StartWorkerRequest creates the hire at once as
            # NotArrived -- so the day's hires are in the ratio before they land. merge_v4
            # s6: hire_untrained_4 at d2r1 makes it 5/9 < 1 and Training Recommendation
            # Alert fires that round; counting arrived workers only kept it at 5/5.
            trained=(self.economy.free_trained + self.economy.working_trained
                     + sum(1 for _d, k in self.economy.arriving if k == "trained")),
            untrained=(self.economy.free_untrained + self.economy.working_untrained
                       + sum(1 for _d, k in self.economy.arriving if k == "untrained")
                       + len(self.economy.in_training)),
            idle_trained=self.economy.free_trained,
            idle_untrained=self.economy.free_untrained,
            prev=self._trigger_memory,
            flooded=self.flood.tiles, positions=self._positions())

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
        w.walks = [list(x) for x in self.walks]
        w.pending_removals = list(self.pending_removals)
        w._casework_live = dict(self._casework_live)
        w.generated_specs = dict(self.generated_specs)
        w._alerts_shown = set(self._alerts_shown)
        w._sourced_now = dict(self._sourced_now)
        w._pop_loaded = {k: list(v) for k, v in self._pop_loaded.items()}
        w._food_reserved = self._food_reserved
        w._emergency_count = self._emergency_count
        w._pos_cache = self._pos_cache
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
                            (w.tasks, "source_transform"),
                            (w.tasks, "has_supplier"), (w, "facilities_for"),
                            (w.tasks, "on_blocked")):
            fn = getattr(owner, attr, None)
            if getattr(fn, "__self__", None) is self:
                setattr(owner, attr, getattr(w, fn.__name__))
        return w


def new_world(state, fmap=None, weather=None) -> World:
    """A fresh game at its first decision, from the RNG state Unity starts it with
    (rng.game_start(seed), or a capture's logged state). Day 1's weather comes from the
    parameter sheet (initialState.weather)."""
    from .rng import UnityRandom
    if weather is None:
        weather = (_ECON_C.get("initial_state") or {}).get("weather") or "Sunny"
    w = World(rng=UnityRandom(state=state), weather=weather, fmap=fmap)
    w.use_generation = True
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
    # EXTERNAL-RELATION CONTACTS ARE CAPPED TOO (TaskSystem:1068): an ExternalRelationship
    # task other than Budget_Allocation counts against InitialExternalRelationFrequency (5).
    # Unity logs "[Limit] Skipping Storm Funding Advisory: Max external-relation contacts
    # reached (5)" three times on seed 5901; the port had no cap, generated the extras and
    # paid itself their +5 satisfaction.
    # (No external-relation cap: TaskSystem removed it, ledger D6.)
    kind = spec.get("taskType")
    if kind == "Emergency":
        if w._emergency_count >= _NUM_EMERGENCY_TASKS:
            return False
        interval = max(2, (_FINAL_DAY * ROUNDS_PER_DAY) // max(1, _NUM_EMERGENCY_TASKS))
        # The spacing is measured on the clock: (day - 1) x roundsPerDay + segment.
        now = (w.day - 1) * ROUNDS_PER_DAY + w.segment
        if now < w._last_emergency_round + interval:
            return False
        w._emergency_count += 1
        w._last_emergency_round = now
        # Evict only once this emergency is actually being created. Doing it before the cap
        # and spacing gates threw away a lodging task for an emergency that was then
        # rejected, which is a strictly worse error than not evicting at all.
        if spec.get("taskTag") == "Lodging" and facility:
            for live_id, (def_id, fac, sp) in list(w.generated_specs.items()):
                if (live_id in w.tasks.active and fac == facility
                        and sp.get("taskTag") == "Lodging"
                        and sp.get("taskType") != "Emergency"):
                    # SupersedeTask: removed with no RecordTaskResolution, and its recorded
                    # lodging demand is reversed (ReverseLodgingRequested).
                    w.economy.report.lodging(w.economy, requested=-_requested_clients(sp))
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



def community_depletion(w, marks=None):
    """CommunityFoodDepletionManager.OnRoundChanged (main-bugfixes d5e5f683).

    Communities no longer eat. Instead every community rolls ONE Random.value per round
    (days >= firstEligibleDay, rounds 1..lastEligibleRound); on a hit it loses
    `amount` food packs and a Community_FoodRequest for exactly that amount is created --
    the request IS the community's demand, there is no population x rate term any more.
    A community with a request already open is skipped, and so is one already at 0 food,
    but NEITHER skip happens before the draw: the draw is unconditional, which is what
    matters for the RNG stream.

    POSITION IN THE STREAM IS UNVERIFIED. The manager is a scene object subscribing in
    Start(), so where its draws sit relative to the tracker and the generation pass is a
    subscription-order question that only a capture of the new build can settle; this
    places it before the tracker. Every draw is marked draw:CommunityFoodDepletion, the
    same string Unity's SnapshotDebug emits, so a capture diff will point straight at it.
    """
    cfg = _ECON_C.get("community_depletion") or {}
    chance = float(cfg.get("chancePerRound", 0) or 0)
    if chance <= 0:
        return []
    if w.day < int(cfg.get("firstEligibleDay", 2) or 2):
        return []
    if (w.segment + 1) > int(cfg.get("lastEligibleRound", 3) or 3):
        return []
    amount = int(cfg.get("amount", 100) or 100)
    return _community_depletion_v6(w, chance, amount)


def _community_food_pending(w, name) -> bool:
    """A live (Active or InProgress) Community_FoodRequest for this community."""
    return any(spec_id == COMMUNITY_FOOD_SPEC_ID and fac == name and not task.resolved
               for live, (spec_id, fac, _sp) in w.generated_specs.items()
               for task in (w.tasks.active.get(live) or w.tasks.awaiting.get(live),)
               if task is not None)


def _food_inbound(w, name) -> int:
    """DeliverySystem.GetReservedIncomingQuantity(facility, FoodPacks): food on every live
    delivery to it -- queued, assigned to a vehicle still driving to load, or aboard."""
    def ours(tag):
        tag = str(tag or "")
        return tag == name or (tag.startswith("__food__") and tag[8:].split("|")[0] == name)
    total = sum(qty for _seq, payload, _s, _d, qty in w.tasks.pending if ours(payload[2]))
    f = w.tasks.fleet
    for i, load in enumerate(f.carrying):
        trip = f.trip[i]
        if trip is not None and "payload" in trip and trip.get("phase") != "complete":
            if ours(trip["payload"][2]):          # assigned: driving to load, or loaded
                total += trip["payload"][1]
        elif load is not None and ours(load[2]):
            total += load[1]
    return total


def _community_food_follow_up(w, name) -> None:
    """CommunityFoodDepletionManager.HandleCommunityFoodRequestEnded: a community food request
    that ends Incomplete is re-raised at once for the current shortfall, inside the same
    day/round window the depletion rolls use."""
    cfg = _ECON_C.get("community_depletion") or {}
    if w.day < int(cfg.get("firstEligibleDay", 2) or 2):
        return
    if (w.segment + 1) > int(cfg.get("lastEligibleRound", 3) or 3):
        return
    if _community_food_pending(w, name):
        return
    b = w.economy.facility(name)
    res = (b or {}).get("resources") or {}
    space = (res.get("foodPacksCapacity") or 0) - (res.get("foodPacks") or 0)
    if space > 0:
        _create_community_food_task(w, name, space)


def _community_depletion_v6(w, chance, amount):
    """CommunityFoodDepletionManager.OnRoundChanged on the bench-v6 build, per community in
    FindObjectsOfType order: TryDeplete, then TopUpShortfall.

    TryDeplete draws unconditionally; on a hit the community loses min(amount, stock) EVEN
    with a request pending, and only a community with nothing pending gets a new request,
    sized to refill it (its free space). TopUpShortfall then asks for the current shortfall if
    nothing pending or inbound covers it."""
    hits = []
    for b in w.economy.buildings:
        if b.get("type") != "Community":
            continue
        res = b.setdefault("resources", {})
        cap = res.get("foodPacksCapacity") or 0
        if w.rng.value() < chance:
            w.economy.report.food(w.economy, needed=amount)     # RecordCommunityFoodDemand
            available = res.get("foodPacks") or 0
            if available > 0:
                lost = min(amount, available)
                res["foodPacks"] = available - lost
                hits.append((b["name"], lost))
                if not _community_food_pending(w, b["name"]):
                    space = cap - res["foodPacks"] if cap > 0 else lost
                    _create_community_food_task(w, b["name"], space)
        if _community_food_pending(w, b["name"]):
            continue
        space = cap - (res.get("foodPacks") or 0)
        if space > 0 and _food_inbound(w, b["name"]) <= 0:
            _create_community_food_task(w, b["name"], space)
    return hits


def cancel_overnight_food(w) -> None:
    """TaskSystem.CancelIncompleteFoodDeliveries (main-bugfixes d5e5f683).

    "Food cannot be delivered overnight": when round 4 ends, every food delivery still
    queued or in transit is cancelled and its parent task fails -- Incomplete, its demand
    counted as resolved-unfulfilled, and the delivery-failure satisfaction penalty applied.
    A trip whose cargo already landed is left alone (B37, fixed on v1_merge_test)."""
    board = w.tasks
    victims = set()
    keep = []
    for entry in board.pending:
        payload = entry[1]
        if str(payload[2] or "").startswith("__food__"):
            victims.add(payload[0])
        else:
            keep.append(entry)
    board.pending = keep
    fleet = board.fleet
    for i, load in enumerate(fleet.carrying):
        if load is not None and str(load[2] or "").startswith("__food__"):
            victims.add(load[0])
            _return_cargo(w, fleet, i)
    # A TRIP STILL DRIVING TO THE KITCHEN COUNTS TOO. Unity cancels the DELIVERY TASK
    # ("Cancelled active delivery task: Task 3: 100 FoodPacks from Kitchen_0 to Motel"),
    # which is live from CreateDeliveryTask onward -- whether or not LoadCargo has run yet.
    # Matching only `carrying` let a vehicle that was still on its source leg at the end of
    # round 4 survive the night and deliver on day N+1, which Unity never does.
    for i, trip in enumerate(fleet.trip):
        if trip is None or "race_ready" in trip:
            continue
        payload = trip.get("payload")
        if payload is not None and str(payload[2] or "").startswith("__food__"):
            victims.add(payload[0])
            _return_cargo(w, fleet, i)
    for task_id in victims:
        task = board.awaiting.pop(task_id, None) or board.active.pop(task_id, None)
        if task is None or task.delivered > 0:
            continue
        board.resolve(task, fulfilled=False, counters=w.economy.counters)
        # (HandleDeliveryFailure applies no satisfaction penalty on this build.)


def _spec_id(w, task_id):
    """The definition id behind a live task, for the impact overrides."""
    entry = w.generated_specs.get(task_id)
    return entry[0] if entry else None


_BUDGET_ALLOCATION_SPEC_ID = "Budget_Allocation"


def _configured_impacts(def_id, impacts, choice=None, food_qty=None):
    """The Budget/Satisfaction impacts a choice applies. A priced FoodPacks choice charges
    costPerUnit x the resolved quantity (AgentChoice.ChargedBudget) in place of its authored
    cost; everything else is applied as authored (ApplyConfiguredAllocation is disabled on this
    build, so the Daily Budget Allocation keeps the asset's grant)."""
    cpu = float((choice or {}).get("costPerUnit") or 0)
    if cpu > 0 and food_qty is not None:
        return [dict(i, value=-(cpu * food_qty)) if i.get("type") == "Budget" and float(i.get("value") or 0) < 0
                else i for i in (impacts or [])]
    return impacts


def _resolve_quantity(w, choice, facility) -> int:
    """FoodDeliveryHandler.ResolveQuantity (main-bugfixes d5e5f683).

    A PopulationBased choice carries deliveryQuantity 0 and sizes itself at EXECUTION time
    from the destination's live food need x deliveryPercentage -- 100% for "fulfil this
    round", 200% for "deliver double to cover the follow-up request". Reading the authored
    quantity instead delivers nothing at all for the two ordinary shelter/motel choices."""
    if str(choice.get("quantityType") or "Fixed") != "PopulationBased":
        return choice.get("deliveryQuantity", 0) or 0
    fac = w.economy.facility(facility)
    if fac is None:
        return 0
    need = _food_need(dict(fac, name=facility))
    pct = float(choice.get("deliveryPercentage") or 0) or 100.0
    return int(round(need * pct / 100.0))


def _create_community_food_task(w, facility, amount):
    """CommunityFoodDepletionManager.SpawnRequestTask -> TaskSystem.CreateTaskFromDatabase.

    Every food-delivering choice on the instance is overridden to the amount actually lost;
    the asset's authored deliveryQuantity is only a default."""
    spec = dict(_TASK_SPEC[COMMUNITY_FOOD_SPEC_ID])
    spec["choices"] = [dict(c) for c in spec["choices"]]
    for c in spec["choices"]:
        if c.get("deliveryQuantity"):
            c["deliveryQuantity"] = amount
    task = Task(w.tasks.next_id, spec.get("taskTag") or "Food", 0, spec["roundsRemaining"],
                task_type=spec.get("taskType") or "Demand")
    task.source = ""
    task.destination = ""
    w.tasks.next_id += 1
    w.tasks.add(task)
    w.generated_specs[task.task_id] = (COMMUNITY_FOOD_SPEC_ID, str(facility), spec)


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
    # Segment 4 IS an invoke since the A1 clock fix: Unity's s8 on merge_v4 (d2r4) rolls
    # caseworkGen for the group that walked in at d2r3, then lands the next walk. The old
    # early return here was the pre-fix clock (no segment-4 invoke at all).
    # CheckClientStayDurations handles one group at a time -- its departure, then its casework
    # roll -- so departure alerts and casework tasks take task ids in group order.
    events = []
    w.clients.update(w.rng, _unity_round(w), w.economy.counters, marks, generated=events)
    for ev in events:
        if ev[0] == "casework":
            _kind, gid, facility, with_need = ev
            _create_casework_task(w, gid, facility, with_need)
            w.economy.report.casework_requested(w.economy, gid, with_need)   # OnCaseworkRequested
            continue
        _kind, count, facility = ev
        # DEPARTURES DO NOT FREE THE FACILITY. TriggerNonCaseworkDeparture mutates tracker
        # state only; OnCaseworklessClientsDeparted has no subscribers, and Motel Population
        # storage only ever drops via a vehicle LoadCargo. Unity's lodgingSpend therefore steps
        # by a CONSTANT every day (5901: +120,000 per rollover for the whole episode), while the
        # port's dwindled to nothing as its residents 'went home'. That gap was 46.7M of the
        # 46.9M total state error and invisible to every first-divergence report.
        # DEPARTURES LEAVE STORAGE (v1_fixes d5441454, never re-derived here):
        # TriggerNonCaseworkDeparture removes the leavers from the facility's Population
        # resource, not just from the tracker. merge_v4 s12: "84 clients without casework
        # departed" and Shelter_0 reads 0 the same round, where the port still held 84 --
        # and then raised a shelter food request Unity never did.
        w.economy.move_population(facility, -count)
        w.economy.motel_pop = w.economy.motel_population
        if count > 0:
            _departure_alert(w, facility)


def _walk_destinations(w, source, include_shelters, include_motels):
    """ClientRelocationHandler.GetDestinationsSorted(filterByPath: true).

    Operational shelters (FindObjectsOfType order: newest first) and/or the motel, each with the
    space it has LEFT after what is already walking towards it, kept only if a road path reaches
    it (a walk is flood-aware though it uses no vehicle), then ordered by that space, largest
    first (a stable sort, so ties keep the find order)."""
    src_cell = w._facility_cell(str(source))
    flooded = w.flooded_road_cells()
    found = []
    if include_shelters:
        found += [b for b in w.economy.buildings[::-1] if b["type"] == "Shelter" and b["status"] == "InUse"]
    if include_motels:
        found += [b for b in w.economy.buildings if b["type"] == "Motel"]
    out = []
    for b in found:
        if b["name"] == str(source):
            continue
        res = b.get("resources") or {}
        cap = res.get("populationCapacity")
        space = (10 ** 9 if cap is None
                 else cap - (res.get("population") or 0) - _walking_to(w, b["name"]))
        if space <= 0:
            continue
        dst_cell = w._facility_cell(b["name"])
        if src_cell is None or dst_cell is None or roads.path_length(src_cell, dst_cell, flooded) is None:
            continue
        out.append((b["name"], space))
    out.sort(key=lambda r: -r[1])
    return out


def _walking_to(w, destination) -> int:
    """People already on foot towards this destination (effectiveSpace's inbound term)."""
    return sum(x[3] for x in w.walks if x[2] == destination)


def apply_menu_action(w: World, action: dict) -> bool:
    """One game action from Unity's menu, against the whole world: a population transfer is a
    self-walk (queue_menu_transfer); a deconstruction also drops the clients housed there from
    the tracker (Building.ReleaseClientGroups); everything else is economy-only."""
    from .economy import apply_action
    kind = action.get("action_type")
    if kind == "resource_transfer":
        tr = action.get("transfer") or {}
        if tr.get("resource_type", "Population") == "Population":
            return queue_menu_transfer(w, tr.get("source_facility"), tr.get("destination_facility"),
                                       tr.get("quantity", 0))
    ok = apply_action(w.economy, action)
    if ok and kind in ("deconstruct", "deconstruction"):
        name = (action.get("deconstruction") or {}).get("building_name")
        w.clients.groups = [g for g in w.clients.groups if g.facility != name]
        _cancel_deliveries_involving(w, name)
    return ok


def _cancel_deliveries_involving(w, name) -> None:
    """DeliverySystem.CancelAllDeliveriesInvolving, which Building.StartDeconstruction calls the
    moment the building starts coming down (not when it is destroyed): every delivery to or from
    it, queued or under way, is dropped. A queued one simply leaves the queue -- no event, so
    its parent task stays answered and its reserved stock is free again at the source (5504
    MCTS plan, s17: Kitchen_5 keeps the 200 packs promised to the shelter)."""
    def involves(payload):
        tag = str(payload[2] or "")
        if tag.startswith("__food__"):
            return name in tag[len("__food__"):].split("|", 1)
        return tag in (name, "Shelter:" + name)
    board = w.tasks
    board.pending = [e for e in board.pending if not involves(e[1])]
    fleet = board.fleet
    for i, trip in enumerate(fleet.trip):
        payload = (trip or {}).get("payload") or fleet.carrying[i]
        if payload is not None and involves(payload):
            _return_cargo(w, fleet, i)


def _return_cargo(w, fleet, i) -> None:
    """Vehicle.CancelCurrentTask -> AbortDelivery -> ReturnAllCargoToSource: a cancelled
    delivery is not credited, and whatever the vehicle has aboard goes back into its source
    (AddResource, so only what fits). 5509 MCTS plan, s15: the end-of-day cancel caught 81
    meals on their way from Kitchen_3; Unity put them back and wasted them overnight. Frees
    the vehicle."""
    trip = fleet.trip[i] or {}
    payload = trip.get("payload") or fleet.carrying[i]
    aboard = trip.get("aboard", 0) if trip.get("phase") in ("boarding", "to_dst") else 0
    # StopAllCoroutines leaves the vehicle where it stood: mid-route, not back at the leg's
    # start (5513 MCTS plan: Vehicle 3 cancelled at path index 13 of 18 overnight, so the next
    # day's suitability scoring sees it there).
    path = trip.get("path")
    if path and trip.get("phase") in ("to_src", "to_dst"):
        idx = len(path) - trip.get("left", len(path))
        if 0 <= idx < len(path):
            fleet.pos[i] = path[idx]
    fleet.carrying[i] = None
    fleet.trip[i] = None
    if payload is None or aboard <= 0:
        return
    tag = str(payload[2] or "")
    if tag.startswith("__food__"):
        if "|" in tag:
            w.economy.add_food(tag.split("|", 1)[1], aboard)
        return
    source = w.tasks._sources.get(payload[0], "")
    task = w.tasks.active.get(payload[0]) or w.tasks.awaiting.get(payload[0])
    source = source or (getattr(task, "source", "") if task is not None else "")
    back = _take_loaded(w, tuple(payload[:3]), 0)
    if back and source:
        w.economy.move_population(source, back)
        if source == "Motel":
            w.economy.motel_pop = w.economy.motel_population


def queue_menu_transfer(w, source, destination, quantity) -> bool:
    """The menu action `transfer_population_<src>_<dst>_<qty>`.

    ActionExecutor.ExecuteTransfer routes a Population transfer through
    ClientRelocationHandler.ExecuteToSpecificDestination -- the SAME self-walk path a task
    choice takes: route-checked, capped by the destination's effective space, people leave
    the source now and arrive `relocationDelayRounds` later, and a walk to a casework site
    is the processing-home event. The port used to append to `pending_transfers`, which
    only bumped the motel counter and never moved anybody; that is why transfers were
    excluded from the search basket ("optimising a hole").

    Returns whether anything was queued -- a refused transfer is a no-op in the game."""
    src = w.economy.facility(str(source))
    dst = w.economy.facility(str(destination))
    if src is None or dst is None or quantity <= 0:
        return False
    src_cell, dst_cell = w._facility_cell(str(source)), w._facility_cell(str(destination))
    if src_cell is None or dst_cell is None:
        return False
    if roads.path_length(src_cell, dst_cell, w.flooded_road_cells()) is None:
        return False
    res = dst.get("resources") or {}
    cap = res.get("populationCapacity")
    space = (10 ** 9 if cap is None
             else cap - (res.get("population") or 0) - _walking_to(w, dst["name"]))
    available = (src.get("resources") or {}).get("population") or 0
    send = min(int(quantity), available, max(0, space))
    if send <= 0:
        return False
    removed = -w.economy.move_population(str(source), -send)
    if removed <= 0:
        return False
    if dst.get("type") == "CaseworkSite":
        w.clients.process_home(str(source), removed, w.economy.counters)
    w.economy.motel_pop = w.economy.motel_population
    # THE TRANSFER ADOPTS AN OPEN LODGING TASK. ExecuteTransfer looks for the first active,
    # not-Completed Lodging task whose affectedFacility is the SOURCE and hands it to
    # ExecuteToSpecificDestination as the parent -- so a menu transfer can take a relocation
    # request off the board, and the walk's arrival credits its deliveredQuantity and
    # completes it. Passing no parent left that task on the board in the port and cost a
    # whole lodgingResolved credit (probe_tx r06, the first transfer aimed at a shelter).
    # `status != Completed` includes an InProgress one, which the port keeps in `awaiting`,
    # and activeTasks is in creation order -- so scan both, lowest id first.
    parent = None
    for tid in sorted(list(w.tasks.active) + list(w.tasks.awaiting)):
        task = w.tasks.active.get(tid) or w.tasks.awaiting.get(tid)
        if task is None or task.tag != "Lodging" or task.resolved:
            continue
        entry = w.generated_specs.get(tid)
        if entry and entry[1] == str(source):
            parent = tid
            break
    rounds = max(1, int(_ECON_C.get("relocation_delay_rounds", 2) or 2))
    w.walks.append([rounds, str(source), dst["name"], removed, parent])
    if parent is not None and parent in w.tasks.active:
        w.tasks.awaiting[parent] = w.tasks.active.pop(parent)   # SetTaskInProgress
    return True


def queue_walks(w, task_id, task, facility, demanded,
                include_shelters=True, include_motels=False) -> bool:
    """ClientRelocationHandler.Execute (main-bugfixes 5d922203).

    People leave the source the moment the choice is made and arrive `relocationDelayRounds`
    rounds later, split across destinations by the space each has left. No vehicle, no
    route latency; the only thing that can stop a leg is having nowhere reachable to put
    people. Returns False when nothing could be queued, which is Unity's `anyCreated ==
    false` -> CompleteTaskAction false -> the task stays on the board."""
    src = w.economy.facility(str(facility))
    available = ((src.get("resources") or {}).get("population") or 0) if src else 0
    to_send = min(demanded, available) if demanded > 0 else available
    if to_send <= 0:
        return False
    rounds = max(1, int(_ECON_C.get("relocation_delay_rounds", 2) or 2))
    remaining, any_created = to_send, False
    for name, space in _walk_destinations(w, facility, include_shelters, include_motels):
        if remaining <= 0:
            break
        send = min(remaining, space)
        if send <= 0:
            continue
        removed = -w.economy.move_population(str(facility), -send)
        if removed <= 0:
            continue
        w.walks.append([rounds, str(facility), name, removed, task_id])
        remaining -= removed
        any_created = True
    if any_created:
        # Execute completes the parent at once (CompleteTask: resolved, nothing delivered
        # yet); each walk that lands credits its people late (FinalizeRelocation ->
        # AddLateDelivery). Kept in `awaiting`, resolved, so the landing can find it.
        w.tasks.active.pop(task_id, None)
        w.tasks.resolve(task, fulfilled=True, counters=w.economy.counters)
        w.tasks.awaiting[task_id] = task
    return any_created


def tick_walks(w) -> None:
    """ClientRelocationHandler.HandleRoundEnd -> FinalizeRelocation, at OnRoundEnd.

    Capture merge_v2: queued at s5 (d2r1), landed at s7 (d2r3) -- two rounds -- and the
    arriving group registers with the tracker in the SAME step, immediately before the
    `relocation:arrive` mark, not on the next one as a vehicle unload does. Overflow goes
    back to the source. The parent was resolved when the walk was queued; landing people
    credit it late (AddLateDelivery)."""
    if not w.walks:
        return
    for entry in w.walks:
        entry[0] -= 1
    arrived = [e for e in w.walks if e[0] <= 0]
    w.walks = [e for e in w.walks if e[0] > 0]
    for _r, source, dest, qty, task_id in arrived:
        delivered = w.economy.move_population(dest, qty)
        if delivered < qty:
            w.economy.move_population(source, qty - delivered)
        task = w.tasks.awaiting.get(task_id) or w.tasks.active.get(task_id)
        if task is not None and delivered > 0:
            task.delivered += delivered
            w.tasks.late_delivery(task, delivered, w.economy.counters)     # AddLateDelivery
        # HandleSelfWalkArrival registers a group only at a LODGING building; people who
        # walk to a casework site are already off the tracker (departure processed them).
        d = w.economy.facility(dest)
        if delivered > 0 and d is not None and d.get("type") != "CaseworkSite":
            w.economy.report.lodging(w.economy, satisfied=delivered)   # RecordLodgingSatisfiedToday
        if delivered > 0 and d is not None and d.get("type") in ("Shelter", "Motel"):
            w.pending_arrivals.append((delivered, dest))
        if task is not None and not any(e[4] == task_id for e in w.walks):
            w.tasks.awaiting.pop(task_id, None)       # its last walk is in


def _sweep_stale_tasks(w: World) -> None:
    """TaskSystem.SweepStalePopulationTasks (OnRoundEnd) -> RefreshTaskAgainstLiveState, for
    every task still awaiting an answer. A population choice re-reads the live headcount (the
    group's casework need for a casework task, else the facility's population): nobody left
    auto-resolves the task (no metrics, no penalty), otherwise its Clients impact becomes that
    headcount -- which is what DailyReportData's nights-needed reads. A population-based food
    choice at a facility that eats auto-resolves the task once its outstanding need is met."""
    by_type = _ECON_C.get("storage_by_type") or {}
    for tid in list(w.tasks.active):
        task = w.tasks.active[tid]
        entry = w.generated_specs.get(tid)
        if task.resolved or not entry or not entry[1]:
            continue
        def_id, fac, spec = entry
        b = w.economy.facility(fac)
        if b is None:
            continue
        for c in spec.get("choices") or []:
            if not (c.get("triggersDelivery") or c.get("immediateDelivery")):
                continue
            if c.get("deliveryCargoType") == 0:
                if spec.get("_gid") is not None:
                    g = w.clients.group(spec["_gid"])
                    people = g.with_need if g is not None else 0
                else:
                    people = (b.get("resources") or {}).get("population") or 0
                if people <= 0:
                    _auto_resolve(w, tid, task)
                    break
                impacts = spec.get("taskImpacts") or []
                if any(i.get("type") == "Clients" and i.get("value") != people for i in impacts):
                    spec = dict(spec, taskImpacts=[dict(i, value=people) if i.get("type") == "Clients" else i
                                                   for i in impacts])
                    w.generated_specs[tid] = (def_id, fac, spec)
            elif (c.get("deliveryCargoType") == 1 and c.get("quantityType") == "PopulationBased"
                  and (by_type.get(b["type"]) or {}).get("consumptionEnabled")
                  and not (b.get("outstanding_need") or 0)):
                _auto_resolve(w, tid, task)
                break


def _auto_resolve(w: World, tid, task) -> None:
    """ResolveTaskClientsAlreadyRelocated: off the board, Completed, no RecordTaskResolution."""
    w.tasks.active.pop(tid, None)
    task.resolved = True


def _departure_alert(w, facility) -> None:
    """ClientStayTracker.ShowDepartureAlert: a "Clients Departed" Alert straight through
    TaskSystem.CreateTask. The tracker runs before TaskSystem's ageing on the same invoke, so it
    is aged at birth (as the casework task is)."""
    w.tasks.add(Task(w.tasks.next_id, "None", 0, _ALERT_ROUNDS - 1, task_type="Alert"))
    w.generated_specs[w.tasks.next_id] = (
        "Clients Departed", facility, {"taskId": "", "taskTitle": "Clients Departed",
                                       "taskType": "Alert", "taskTag": "None", "choices": []})
    w.tasks.next_id += 1


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
    t = Task(w.tasks.next_id, "BackToHome", 0, spec["roundsRemaining"],
             task_type=spec.get("taskType") or "Advisory")
    t.rounds_remaining -= 1
    t.fresh = False
    t.source = str(facility)
    t.destination = ""
    w.tasks.next_id += 1
    w.tasks.add(t)
    w.generated_specs[t.task_id] = (CASEWORK_SPEC_ID, str(facility), spec)
    w._casework_live[t.task_id] = gid


def _restore_vehicles(w, before) -> None:
    """FloodTaskGenerator.OnFloodTileRemoved -> RestoreVehiclesClearOfFlood: the paid repair
    task is gone on this build (CreateVehicleRepairTask does nothing), so a flood-damaged
    vehicle comes back when a flood tile is removed and the cell it stands on is dry.
    `before` is the tile set ahead of the update (None when no vehicle was damaged). Unity
    checks at each removal, mid-update; this checks once against the updated set."""
    if before is None or not (before - w.flood.tiles):
        return
    fleet = w.tasks.fleet
    flooded = w.flooded_road_cells()
    for v, dmg in enumerate(fleet.damaged):
        if dmg and fleet.pos[v] not in flooded:
            fleet.repair(v)


def _food_blockage_alert(w, payload):
    """FloodTaskGenerator.ShowFoodBlockageAlert: TaskSystem.CreateTask("Delivery Blocked by
    Flood", Alert, <destination>) -- it draws nothing but takes a task id."""
    dest = str(payload[2] or "")[len("__food__"):].split("|")[0]
    title = "Delivery Blocked by Flood"
    w.tasks.add(Task(w.tasks.next_id, "None", 0, _ALERT_ROUNDS, task_type="Alert"))
    w.generated_specs[w.tasks.next_id] = (
        title, dest, {"taskId": "", "taskTitle": title, "taskType": "Alert", "taskTag": "None",
                      "choices": []})
    w.tasks.next_id += 1


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
        # CreateFoodBlockageChoices: the spoiled cargo is discarded and the ONLY way forward
        # is one immediate emergency delivery of a fresh batch, priced per meal
        # (quantity * 10), not the generic four-choice list -- which is the population
        # blockage's. 5802 step 28: Unity charged -$370 for 37 meals and completed the task;
        # the port offered choices the game never had, could not answer any of them, and let
        # the blockage expire instead.
        choices = [{"choiceId": 1, "triggersDelivery": False, "immediateDelivery": True,
                    "deliveryQuantity": quantity, "budgetDelayRounds": 0,
                    "destinationCategory": "", "enableMultipleDeliveries": False,
                    "impacts": [{"type": "Budget", "value": -10 * int(quantity)}]}]
    elif loaded:
        # CreatePopulationLoadedChoices: clients returned to the source by
        # ReturnCargoToSource (_blocked_delivery), one $1500 immediate transport.
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
    t = Task(w.tasks.next_id, "None", 0, spec["roundsRemaining"],
             task_type=spec.get("taskType") or "Emergency")
    t.source = source
    w.tasks.next_id += 1
    w.tasks.add(t)
    # WHICH END THE BLOCKAGE IS FILED UNDER (FloodTaskGenerator.CreateRoadBlockageTask):
    # a FoodPacks blockage takes affectedFacility from the DESTINATION -- "the food choices
    # deliver TO the original destination (FoodDeliveryHandler resolves the task's facility
    # as the destination)" -- while a Population blockage takes it from the source, loaded
    # or not. The port filed both under the source, so a stopped kitchen run showed up on
    # the kitchen instead of the motel it was feeding (5504 s9, 5601 s9).
    affected = (dest[len("__food__"):].split("|")[0] or source) if food else source
    w.generated_specs[t.task_id] = (ROAD_BLOCKAGE_SPEC_ID, affected, spec)


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
    # (That staleness was the pre-A1 clock. The tracker now fires on segment 4 and stamps
    # currentRound = 4 + (day-1)*4 there -- the same number the next day's segment 0 would
    # carry, which is harmless because no tracker invoke happens on segment 0 any more.)
    return w.segment + (w.day - 1) * ROUNDS_PER_DAY


def _sized_for(w, spec, facility):
    """TaskSystem.CreateTask sizes every delivering choice AT CREATION.

    `newChoice.deliveryQuantity = CalculateDeliveryQuantity(choice, source)`: Fixed keeps the
    authored number, All takes the triggering facility's whole stock of the cargo, Percentage
    takes a share of it. The instance is what the game then uses -- for the choice AND for
    `demandQuantity` (the largest delivering quantity, which is what lodgingResolved counts) --
    and the TaskData asset is never written, so the export carries the authored 0.

    Shelter_Flood_Damage's two choices are `All` over a 100-person shelter: Unity resolves them
    to 100 and books 100 lodgingResolved, the port read 0 and booked the zero-demand 1.
    PopulationBased is deliberately NOT resolved here -- CalculateDeliveryQuantity has no case
    for it, so it keeps the authored value and FoodDeliveryHandler.ResolveQuantity sizes it at
    execution instead (see _resolve_quantity)."""
    choices = spec.get("choices") or []
    if not any(c.get("quantityType") in ("All", "Percentage") for c in choices):
        return spec
    fac = w.economy.facility(str(facility)) if facility else None
    res = (fac.get("resources") or {}) if fac else {}
    # Lodging tasks move people; a food task's All would take the facility's packs.
    key = "foodPacks" if (spec.get("taskTag") == "Food") else "population"
    available = res.get(key) or 0
    out = []
    for c in choices:
        if not (c.get("triggersDelivery") or c.get("immediateDelivery")):
            out.append(c)
            continue
        qt = c.get("quantityType")
        if qt == "All":
            out.append(dict(c, deliveryQuantity=available))
        elif qt == "Percentage":
            pct = float(c.get("deliveryPercentage") or 0)
            out.append(dict(c, deliveryQuantity=int(round(available * pct / 100.0))))
        else:
            out.append(c)
    return dict(spec, choices=out)


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
        spec = _sized_for(w, spec, facility)
        state = {"choices": spec.get("choices") or []}
        tag = spec.get("taskTag") or "None"
        t = Task(w.tasks.next_id, tag, demand_of(state, tag),
                 spec.get("roundsRemaining") or 1, task_type=spec.get("taskType") or "Demand")
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
        if tag == "Lodging":                                     # RecordLodgingRequestedToday
            w.economy.report.lodging(w.economy, requested=_requested_clients(spec))


def _requested_clients(spec) -> int:
    """The live requested count of a lodging task: its first Population-cargo choice's
    resolved quantity (TaskSystem.CreateTaskFromData)."""
    c = next((c for c in spec.get("choices") or [] if c.get("deliveryCargoType") == 0), None)
    return int((c or {}).get("deliveryQuantity") or 0)


_POSITIONS = {}


from .floodmap import pack as _pack_cell
_PACKED_ROADS = {_pack_cell(c[0], c[1]): c for c in roads.ROAD_CELLS}


def _site_positions():
    """site id -> transform, from the sim_constants export (AbandonedSite transforms)."""
    raw = (_ECON_C.get("site_positions") or {})
    return {int(k): (float(v[0]), float(v[1])) for k, v in raw.items()}


_SITE_POS = None


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


def _take_loaded(w, key, quantity):
    """What the vehicle for this trip actually carried (LoadCargo's actualLoaded), or the
    nominal quantity when the trip never went through the load hook (tests, settle paths)."""
    pool = w._pop_loaded.get(tuple(key))
    if not pool:
        return quantity
    got = pool.pop(0)
    if not pool:
        w._pop_loaded.pop(tuple(key), None)
    return got


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
        if (choice or {}).get("immediateDelivery"):
            sourced = quantity
        else:
            pool = w._sourced_now.get(_task_id, 0)
            sourced = min(quantity, pool)
            if pool - sourced > 0:
                w._sourced_now[_task_id] = pool - sourced
            else:
                w._sourced_now.pop(_task_id, None)
        if sourced:
            # The landing belongs to the round the vehicles drove in -- the step's
            # PRE-advance round -- so it shares that round's once-per-round consumption
            # key: a load arriving after the tick already fed the building is not eaten
            # until the next tick (merge_v4 s14: two 100-pack loads reach a 225-person
            # Motel, the first is eaten, the second sits at 100).
            w.economy.add_food(dest[len("__food__"):].split("|")[0], sourced,
                               round_key=w.day * 100 + w.segment)
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
        loaded = _take_loaded(w, (_task_id, quantity, destination), quantity)
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
    loaded = _take_loaded(w, (_task_id, quantity, destination), quantity)
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
        moved = w.economy.move_population(target, loaded) if target else 0
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
    """What an overdue task still costs. ExpireTask and SetTaskIncomplete no longer call
    ApplyTaskPenalties (ledger D22), so only two things happen: a stranded population blockage
    pays FloodTaskGenerator's direct -30 abandonment, and a community food request that ended
    Incomplete is re-raised (CommunityFoodDepletionManager.HandleCommunityFoodRequestEnded)."""
    for tid, _answered in expired:
        entry = w.generated_specs.get(tid)
        if not entry:
            continue
        if entry[0] == ROAD_BLOCKAGE_SPEC_ID and entry[2].get("_loaded") \
                and entry[2].get("_cargo") != "food":
            w.economy.add_satisfaction(-_BLOCKAGE_ABANDON_PENALTY)
        elif entry[0] == COMMUNITY_FOOD_SPEC_ID:
            _community_food_follow_up(w, entry[1])


def _round_end(w: World, marks) -> None:
    """GlobalClock.OnRoundEnd, in subscriber order: funding lands (BudgetAllocationManager),
    walks land and register with the tracker (ClientRelocationHandler), DailyReportData
    accumulates the round, then buildings advance construction/deconstruction."""
    w.economy.arrive_funding()
    _n = len(w.pending_arrivals)
    tick_walks(w)
    for count, facility in w.pending_arrivals[_n:]:
        w.clients.register_arrival(w.rng, count, _unity_round(w), facility, marks)
    del w.pending_arrivals[_n:]
    # DailyReportData.AccumulateRoundMetrics, the next OnRoundEnd subscriber.
    e = w.economy
    awaiting = sum(g.with_need for g in w.clients.groups
                   if g.with_need > 0 and not g.departed and g.gid in e.report.casework_groups)
    e.report.round_end(e, e.free_trained + e.free_untrained, e.working_trained + e.working_untrained,
                       len(e.in_training), e.total_workers(), awaiting)
    _sweep_stale_tasks(w)
    e.tick_construction()


def _segment_invoke(w: World, marks, rolls, day_changed) -> None:
    """One OnTimeSegmentChanged invoke at the current segment: ageing, the client tracker,
    the generation pass (segments that run one), community depletion, then storage."""
    w.tasks.age()
    _tracker(w, marks)
    if w.segment in _GENERATION_SEGMENTS or day_changed:
        _r = [r + (w.segment,) for r in _pass(w, marks)]
        rolls += _r
        if w.use_generation:
            _create_tasks(w, _r, day_changed)
    if w.segment == 0:
        _daily_report(w)              # WeatherReportSystem.OnTimeSegmentChanged, after generation
    community_depletion(w, marks)
    w.economy.storage_tick(w.day, w.segment)


def step(w: World, marks=None, on_flood_enter=None) -> None:
    """Advance to the next decision point: the Day-1 setup, a day rollover, or a round."""
    if w.day == 1 and w.segment == 0:
        return _day1_skip(w, marks, on_flood_enter)
    if w.segment >= ROUNDS_PER_DAY:
        return _day_rollover(w, marks)
    return step_round(w, marks=marks, on_flood_enter=on_flood_enter)


def _day1_skip(w: World, marks, on_flood_enter) -> None:
    """Day1SkipCoroutine: the day's four rounds with time frozen -- nothing drives, no segment
    invoke, no per-round metrics; only OnRoundEnd fires each round -- then the clock parks at
    segment 4 and OnSimulationEnded runs once (the flood update among it)."""
    for rnd in range(ROUNDS_PER_DAY):
        w.segment = rnd
        _round_end(w, marks)
    w.segment = ROUNDS_PER_DAY
    cancel_overnight_food(w)
    if on_flood_enter is not None:
        on_flood_enter(w)
    _before = set(w.flood.tiles) if any(w.tasks.fleet.damaged) else None
    update_flood(w.flood, w.fmap, w.rng, w.weather, RAIN_INTENSITY[w.weather], marks)
    _restore_vehicles(w, _before)
    _end_of_day_waste(w)
    _destroy_pending(w)
    w.generated = []
    w.round_index += 1


def _day_rollover(w: World, marks) -> None:
    """GlobalClock.ProceedToNextDay at "End Today": OnDayChanged, then one OnTimeSegmentChanged
    at segment 0. Nothing simulates: no driving, no flood, no round-end."""
    w.day += 1
    w.segment = 0
    w.weather = generate_weather(w.rng, marks=marks)
    w.economy.on_day_end(w.day)
    _lodging_nights(w)
    rolls = []
    _segment_invoke(w, marks, rolls, True)
    w.generated = rolls
    _incomplete_penalties(w, w.tasks.expire(w.economy.counters))


def _lodging_nights(w: World) -> None:
    """DailyReportData.OnDayChangedForLodgingNights: everyone the tracker holds slept somewhere
    tonight; what open lodging tasks still ask for (their authored Clients impact) did not."""
    # The tracker drops a group once it is empty, or departed with no casework need left.
    housed = sum(g.count for g in w.clients.groups
                 if g.count > 0 and not (g.departed and g.with_need == 0))
    waiting = 0
    for live_id, (_def, _fac, sp) in w.generated_specs.items():
        task = w.tasks.active.get(live_id) or w.tasks.awaiting.get(live_id)
        if task is not None and not task.resolved and sp.get("taskTag") == "Lodging":
            waiting += sum(int(i.get("value") or 0) for i in sp.get("taskImpacts") or []
                           if i.get("type") == "Clients")
    w.economy.report.nights(w.economy, housed, waiting)


def _destroy_pending(w: World) -> None:
    """End of the frame: completed deconstructions are destroyed, and Building.OnDestroy ->
    ClientStayTracker.HandleFacilityDestroyed drops every client group still housed there."""
    gone = set(w.economy.destroy_pending())
    if gone:
        w.clients.groups = [g for g in w.clients.groups if g.facility not in gone]


def _end_of_day_waste(w: World) -> None:
    """BuildingResourceStorage.OnSimulationEndedCheckEndOfDayWaste: when the day's last round
    ends, every non-community storage that wastes records what it still holds (the day reset
    removes it). Recorded only -- it feeds S_Waste, which the live score does not apply."""
    by_type = _ECON_C.get("storage_by_type") or {}
    for b in w.economy.buildings:
        if b["type"] == "Community" or not (by_type.get(b["type"]) or {}).get("enableFoodWaste"):
            continue
        left = (b.get("resources") or {}).get("foodPacks") or 0
        if left > 0:
            w.economy.report.food_wasted += left


def _daily_report(w: World) -> None:
    """WeatherReportSystem's "Day N Morning Report": an Alert created straight through
    TaskSystem.CreateTask, filed under the "Weather Report" facility. It draws nothing but
    consumes a task id."""
    title = f"Day {w.day} Morning Report"
    w.tasks.add(Task(w.tasks.next_id, "None", 0, _ALERT_ROUNDS, task_type="Alert"))
    w.generated_specs[w.tasks.next_id] = (
        title, "Weather Report", {"taskId": "", "taskTitle": title,
                                  "taskType": "Alert", "taskTag": "None", "choices": []})
    w.tasks.next_id += 1


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
    # PLANNING-PHASE ARRIVALS DRAW BEFORE THE ROLLOVER'S FIRST PASS. An immediate
    # relocation answered in the planning phase registers its two client groups at the
    # choice (5503 s16 f751: choice:at, caseworkNeed x63, stayDuration, x63, stayDuration),
    # and the day change -- Weather.select, the tracker's caseworkGen, the generation pass
    # -- follows at round:advance (f753). They are stamped with the PRE-advance round
    # ("Round: 15" there), so they register here, before anything advances.
    for count, facility in list(arrivals) + w.pending_arrivals:
        w.clients.register_arrival(w.rng, count, _unity_round(w), facility, marks)
    w.pending_arrivals = []
    rolls = []
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
    arrivals = w.pending_arrivals          # this step's landings (the planning-phase ones went above)
    w.pending_arrivals = []
    # ARRIVAL STAMP vs UPDATE ROUND. Unity stamps arrivalRound = currentRound at the DELIVERY
    # instant, which is still the pre-advance segment; CheckClientStayDurations then runs after
    # OnRoundChanged has bumped currentRound. So the first caseworkGen draw sees Y = 1, and each
    # later round adds one. Stamping and evaluating at the same index made every later Y one
    # short, and Y drives the threshold 10 * 1.5^(Y-1) -- identical randoms, different outcomes.
    for count, facility in arrivals:
        # Unity stamps arrivalRound at the DELIVERY instant, which is still pre-advance.
        w.clients.register_arrival(w.rng, count, _unity_round(w), facility, marks)

    w.economy.accumulate()            # EndSimulation: metrics, then OnRoundEnd
    _round_end(w, marks)              # OnRoundEnd precedes AdvanceTimeSegment
    w.segment += 1
    # Reaching segment 4 fires no OnTimeSegmentChanged: AdvanceTimeSegment returns first.
    if w.segment < ROUNDS_PER_DAY:
        _segment_invoke(w, marks, rolls, False)
    w.generated = rolls
    # THE JOIN THAT MAKES THE SURROGATE SELF-DRIVING. generation_pass decides WHICH tasks
    # fire; without this the port produced a list of ids and created nothing, so it could
    # generate a task and never answer one -- which is why every equivalence test so far
    # has had to feed it Unity's own task lifecycle.

    # GlobalClock's OnRoundEnd sits between the segment advance and the flood update
    # (capture merge_v3 s7/s11: ... endSim:afterAdvanceSegment -> the arriving group's
    # Client.caseworkNeed/stayDuration draws -> relocation:arrive -> endSim:afterOnRoundEnd
    # -> flood:enter). Walks therefore land, and their groups register with the tracker,
    # before this round's water moves.
    if w.segment >= ROUNDS_PER_DAY:
        cancel_overnight_food(w)          # food cannot be delivered overnight

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
    _before = set(w.flood.tiles) if any(w.tasks.fleet.damaged) else None
    update_flood(w.flood, w.fmap, w.rng, w.weather,
                 RAIN_INTENSITY[w.weather], marks)
    _restore_vehicles(w, _before)
    if w.segment >= ROUNDS_PER_DAY:
        _end_of_day_waste(w)          # OnSimulationEnded, after the flood update
    _destroy_pending(w)

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
    # THE PAUSED FRAMES RUN HERE, against the flood as just updated (Fleet.run_epilogue).
    w.tasks.flooded = w.flooded_road_cells()
    _late += w.tasks.tick_epilogue(w.economy.counters)
    for _e in _late + w.tasks.settle_late(w.economy.counters):
        _n = len(w.pending_arrivals)
        _land(w, _e[0], _e[1], _e[2])
        for count, facility in w.pending_arrivals[_n:]:
            w.clients.register_arrival(w.rng, count, _unity_round(w), facility, marks)
        del w.pending_arrivals[_n:]
        _apply_removals(w)
    # GlobalClock.OnRoundEnd -> ClientRelocationHandler.HandleRoundEnd. Walks land at the
    # very end of the step (capture merge_v2: the arriving group's tracker draws sit
    # immediately before `relocation:arrive`, which itself sits just before
    # `endSim:afterOnRoundEnd`), so the registration happens HERE, in this step, not on the
    # next one the way a vehicle unload's does.
    # THE LAST DAY IS BILLED. MotelCostManager subscribes to OnSimulationEnded as well as
    # OnDayChanged and charges again when `day == lastDay && segment >= roundsPerDay` -- the
    # final round of the episode, which no day rollover ever follows (B28, fixed on this
    # build). Without it the port under-bills one full day of occupancy: seeds 6001 and 7002
    # ended 20,000 and 28,200 light, exactly the motel's last-round population x $200.
    if w.day >= _FINAL_DAY and w.segment >= ROUNDS_PER_DAY:
        residents = w.economy.motel_population        # never the legacy `motel_pop` shadow
        if residents > 0:
            w.economy.bill_motel(residents)
    # CheckExpiredTasks runs on the UPDATE AFTER the round, which is after OnRoundEnd and
    # after the flood: merge_v4 s7 reads ... endSim:afterOnRoundEnd -> flood:enter -> the
    # flood draws -> task:resolved. Expiring before the walks land killed a relocation the
    # round its own people arrived, so Unity credited lodgingFulfilled 100 and the port 0.
    _incomplete_penalties(w, w.tasks.expire(w.economy.counters))
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
            w.economy.add_satisfaction(w.tasks.REPAIR_DELAY_SATISFACTION)
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
    # A delivering choice starts from TaskSystem.FindTriggeringFacility: an OPERATIONAL building
    # whose GameObject name contains the task's affectedFacility (or a prebuilt). A demolished,
    # deconstructing or idle building is "Destination not found" and the answer is refused.
    if _facility and (choice.get("triggersDelivery") or choice.get("immediateDelivery")):
        from .export import _object_name, _trigger
        if _trigger(w, _object_name(w, _facility)) is None:
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
    demanded = _resolve_quantity(w, choice, str(_facility))
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

    if (choice.get("deliveryCargoType") == 1
            and (choice.get("triggersDelivery") or choice.get("immediateDelivery"))
            and not _food_can_execute(w, choice, str(_facility), demanded)):
        return False
    if task.tag == "Food" and demanded > 0:
        # Food lands at the requester, so its `FoodPacks Empty` condition stops holding --
        # which is how Unity's food requests for that community stop.
        task.source = ""                      # food does not move people out of anywhere
        immediate = bool(choice.get("immediateDelivery"))
        task.chosen_id = choice_id
        facility = str(_facility)
        if immediate:
            # FoodDeliveryHandler.ExecuteImmediate adds only what fits; when nothing fits it
            # returns 0 and ExecuteFoodDelivery fails the answer -- no impacts, the task stays
            # on the board (5508 MCTS plan, s5: Community Amherst at 3000/3000, "added 0/100").
            res = (w.economy.facility(facility) or {}).get("resources") or {}
            cap = res.get("foodPacksCapacity")
            if cap is not None and cap - (res.get("foodPacks") or 0) <= 0:
                return False
            w.tasks.answer(task_id, demanded, immediate=True, destination="__food__" + facility,
                           counters=w.economy.counters, destination_facility=facility)
            _land_now("food", demanded, facility)
            w.economy.apply_choice(task.tag, _configured_impacts(_def_id, choice.get("impacts"), choice, demanded),
                                   choice.get("budgetDelayRounds", 0) or 0, "", 0)
            return True
        # THE KITCHEN ORDER IS A MULTI-DELIVERY (choices 0/1: enableMultipleDeliveries), so
        # ExecuteGeneratorDelivery routes it to ExecuteMultipleDeliveries ->
        # ExecuteMultiSourceSingleDest, NOT to FoodDeliveryHandler.Execute. FindMultipleSources
        # takes every operational kitchen holding food, FindObjectsOfType order (newest
        # first), Take(3); each gets CreateDeliveryTask for the choice's FULL Fixed quantity
        # (route-checked, capacity-chunked). Two reachable kitchens therefore ship 2x the
        # request (6001, s22: Kitchen_5 AND Kitchen_9 -> Community03), and a kitchen whose
        # route is flood-cut is simply skipped -- the port used to route every order from
        # the first in-use kitchen and reject the answer when THAT route was cut (6001,
        # step 21: two of three orders lost). No creation -> the multi rule: impacts applied,
        # task parked InProgress until it expires.
        # FoodDeliveryHandler.Execute + GetKitchensSorted (main-bugfixes d5e5f683): every
        # operational kitchen with UNRESERVED stock, NEAREST to the destination first, each
        # shipping min(what is still needed, its effective stock) until the request is
        # covered -- then CreateDeliveryTask chunks that amount by vehicle capacity. The old
        # multi-source rule (up to three kitchens, each shipping the FULL quantity) was the
        # pre-overhaul game and delivered 2-3x what was asked.
        dst = w._facility_cell(facility)
        flooded = w.flooded_road_cells()
        outbound = w.tasks.outbound_by_kitchen()
        # FoodDeliveryHandler.GetKitchensSorted: operational kitchens with effective stock, in
        # FindObjectsOfType order (newest first), then by transform distance to the destination
        # when the choice prioritizes the nearest source, else by effective stock, largest first.
        pos = w._positions()
        here = pos.get(facility)
        kitchens = []
        for k in w.economy.buildings[::-1]:
            if k["type"] != "Kitchen" or k["status"] != "InUse":
                continue
            stock = ((k.get("resources") or {}).get("foodPacks") or 0) - outbound.get(k["name"], 0)
            if stock <= 0:
                continue
            src = w._facility_cell(k["name"])
            if src is None or dst is None:
                continue
            there = pos.get(k["name"])
            dist = (((there[0] - here[0]) ** 2 + (there[1] - here[1]) ** 2) ** 0.5
                    if there is not None and here is not None else 0.0)
            kitchens.append((dist, k["name"], src, stock))
        if choice.get("prioritizeNearestSource"):
            kitchens.sort(key=lambda r: r[0])
        else:
            kitchens.sort(key=lambda r: -r[3])
        legs = []
        remaining = demanded
        for _d, kname, src, stock in kitchens:
            if remaining <= 0:
                break
            if roads.path_length(src, dst, flooded) is None:
                continue
            send = min(remaining, stock)
            remaining -= send
            while send > 0:
                q = min(send, VEHICLE_CAPACITY)
                legs.append((q, src, dst, "__food__" + facility + "|" + kname))
                send -= q
        if not legs:
            if not choice.get("enableMultipleDeliveries"):
                return False
            w.economy.apply_choice(task.tag, _configured_impacts(_def_id, choice.get("impacts"), choice, demanded),
                                   choice.get("budgetDelayRounds", 0) or 0, "", 0)
            return _park_blocked(w, task_id, task, choice_id)
        task.source = legs[0][3].split("|")[1]
        w.tasks.flooded = flooded
        w.tasks.answer_legs(task_id, legs)
        w.economy.apply_choice(task.tag, _configured_impacts(_def_id, choice.get("impacts"), choice, demanded),
                               choice.get("budgetDelayRounds", 0) or 0, "", 0)
        return True
    if _def_id == ROAD_BLOCKAGE_SPEC_ID and spec.get("_cargo") == "food":
        # The emergency food run. Without this dispatch the branch below swallowed the
        # choice and `_answer_blockage_food` was dead code -- defined and never called.
        #
        # IT ONLY LANDS IF THE VEHICLE HAD LOADED -- MEASURED ON TWO CAPTURES, MECHANISM
        # UNVERIFIED. Both send choice 1 and the outcomes split on the blockage's phase:
        # 5802 s28 "en route to drop-off" applies "Emergency fast food delivery ($370)" and
        # COMPLETES the task, while 5504 s10 "en route to pick-up" logs
        # `choice:at {"task":15,"choice":1}` and then nothing -- no impacts, no completion --
        # and the task expires Incomplete for +20. CreateFoodBlockageChoices itself does NOT
        # branch on hasLoadedCargo, so the gate is somewhere in CompleteTaskAction /
        # ExecuteGeneratorDelivery that this has not located; the immediate FoodPacks choice
        # carries no ManualAssignment endpoints and its source falls back to the task's
        # facility, which for a food blockage is the DESTINATION. Two samples, both agree.
        if not spec.get("_loaded"):
            return False
        return _answer_blockage_food(w, task_id, task, choice, choice_id, spec)
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
    # SELF-WALK (main-bugfixes 5d922203). Every non-immediate population relocation is now a
    # walk: no vehicle, no load/unload, no multi-source machinery. The immediate ("emergency
    # transport") choices below still teleport, which is unchanged.
    if (demanded > 0 and dest_cat in ("Shelter", "Motel")
            and choice.get("triggersDelivery") and not choice.get("immediateDelivery")):
        w.economy.apply_choice(task.tag, _configured_impacts(_def_id, choice.get("impacts")),
                               choice.get("budgetDelayRounds", 0) or 0, dest_cat, demanded)
        task.chosen_id = choice_id
        task.source = str(_facility)
        return queue_walks(w, task_id, task, str(_facility), demanded,
                           include_shelters=(dest_cat == "Shelter"),
                           include_motels=(dest_cat == "Motel"))
    if (demanded > 0 and dest_cat == "Shelter" and choice.get("enableMultipleDeliveries")
            and choice.get("triggersDelivery") and not choice.get("immediateDelivery")):
        return _answer_multi_shelter(w, task_id, task, choice, choice_id, str(_facility), demanded)
    if (demanded > 0 and dest_cat == "Shelter" and choice.get("enableMultipleDeliveries")
            and choice.get("immediateDelivery")):
        return _answer_multi_shelter_immediate(w, task_id, task, choice, choice_id, str(_facility), demanded)
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
    w.economy.apply_choice(task.tag, _configured_impacts(_def_id, choice.get("impacts")),
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
            if moved:
                w.economy.report.lodging(w.economy, satisfied=moved)
            # ClientRelocationHandler does the same double-registration, calling
            # RegisterClientArrival and HandlePopulationDelivery back to back on one transfer.
            if moved:
                w.pending_arrivals.append((moved, target))
            w.pending_arrivals.append((qty, target))
            w.economy.motel_pop = w.economy.motel_population
    return True


def _food_can_execute(w: World, choice, facility, requested) -> bool:
    """FoodDeliveryHandler.CanExecute, which gates an agent's food answer (immediate or not):
    refused when food already inbound covers the request; a kitchen delivery also needs a
    reachable operational kitchen, unreserved stock, and -- for requireFullQuantity -- enough
    unreserved stock across all kitchens for the whole remaining need."""
    need = max(0, requested - _food_inbound(w, facility))
    if need <= 0:
        return False
    if choice.get("immediateDelivery"):
        return True
    dst = w._facility_cell(facility)
    flooded = w.flooded_road_cells()
    outbound = w.tasks.outbound_by_kitchen()
    reachable, total_reachable, total = False, 0, 0
    for k in w.economy.buildings:
        if k["type"] != "Kitchen" or k["status"] != "InUse":
            continue
        free = max(0, ((k.get("resources") or {}).get("foodPacks") or 0) - outbound.get(k["name"], 0))
        total += free
        src = w._facility_cell(k["name"])
        if src is None or dst is None or roads.path_length(src, dst, flooded) is None:
            continue
        reachable = True
        total_reachable += free
    if not reachable or total_reachable <= 0:
        return False
    return not (choice.get("requireFullQuantity") and total < need)


def _answer_casework(w: World, task_id, task, choice, choice_id, facility, demanded) -> bool:
    """The casework choice is a SELF-WALK since main-bugfixes 5d922203.

    TaskDetailUI routes a SpecificBuilding=CaseworkSite destination through
    ExecuteFallbackDelivery -> DetermineChoiceDeliveryDestination (operational casework
    sites other than the source, with room once inbound is counted, NEAREST first) ->
    ClientRelocationHandler.ExecuteToSpecificDestination: route must exist, send =
    min(requested, source population, effective space), the people leave the source NOW
    and land `relocationDelayRounds` later. Departure is the processing-home event:
    HandleSelfWalkDeparture -> RemoveClientsByQuantity(source, count, groupId) takes the
    task's own group's needy members first and credits caseworkProcessed at that moment --
    merge_v4 s10: Shelter_0 100 -> 84 and caseworkProcessed 16 the round the walk is
    queued, the 16 landing at Casework Alpha two rounds later with no tracker draws.
    No destination, no route or no room -> false -> the task stays on the board (its
    impacts are applied first, as CompleteTaskAction does)."""
    src_cell = w._facility_cell(facility)
    flooded = w.flooded_road_cells()
    # IsValidDeliveryDestination: room (effective space) for the whole requested quantity,
    # min(requested, the source's population); then nearest by TRANSFORM distance
    # (prioritizeNearestDestination defaults on), ties to FindObjectsOfType order (newest first).
    src_b = w.economy.facility(facility)
    src_pop = ((src_b or {}).get("resources") or {}).get("population") or 0
    required = max(1, min(demanded, src_pop) if demanded > 0 else src_pop)
    pos = w._positions()
    here = pos.get(facility)
    best = None
    for b in w.economy.buildings[::-1]:
        if b["type"] != "CaseworkSite" or b["status"] != "InUse" or b["name"] == facility:
            continue
        res = b.get("resources") or {}
        cap = res.get("populationCapacity")
        space = 10 ** 9 if cap is None else cap - (res.get("population") or 0) - _walking_to(w, b["name"])
        if space < required:
            continue
        dst_cell = w._facility_cell(b["name"])
        if src_cell is None or dst_cell is None:
            continue
        there = pos.get(b["name"])
        dist = (((there[0] - here[0]) ** 2 + (there[1] - here[1]) ** 2) ** 0.5
                if there is not None and here is not None else 0.0)
        if best is None or dist < best[0]:
            best = (dist, b["name"], space, dst_cell)
    if best is None:
        return False
    _dist, dest, space, dst_cell = best
    if roads.path_length(src_cell, dst_cell, flooded) is None:
        return False
    src = w.economy.facility(facility)
    available = ((src.get("resources") or {}).get("population") or 0) if src else 0
    to_send = min(demanded, available) if demanded > 0 else available
    to_send = min(to_send, space)
    if to_send <= 0:
        return False
    removed = -w.economy.move_population(facility, -to_send)
    if removed <= 0:
        return False
    spec = (w.generated_specs.get(task_id) or (None, None, {}))[2]
    w.clients.process_home(facility, removed, w.economy.counters, gid=spec.get("_gid", -1))
    w.economy.motel_pop = w.economy.motel_population
    rounds = max(1, int(_ECON_C.get("relocation_delay_rounds", 2) or 2))
    w.walks.append([rounds, facility, dest, removed, task_id])
    # IMPACTS ONLY ON SUCCESS. A refused choice (no operational casework site, no room, no
    # route) is not executed and applies nothing -- the single validation gate of v1_fixes
    # 16d1a106. The port used to apply them first and then refuse, so on seed 6001 it paid
    # itself the casework +10 about a hundred times against Unity's twelve and sat pinned at
    # 100 satisfaction while Unity drifted to 87.
    w.economy.apply_choice(task.tag, _configured_impacts(_spec_id(w, task_id), choice.get("impacts")),
                           choice.get("budgetDelayRounds", 0) or 0, "CaseworkSite", 0)
    task.source = facility
    task.chosen_id = choice_id
    w.tasks.active.pop(task_id, None)
    w.tasks.awaiting[task_id] = task
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
    w.economy.apply_choice(task.tag, _configured_impacts(_spec_id(w, task_id), choice.get("impacts")), 0, "", 0)
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
    w.economy.apply_choice(task.tag, _configured_impacts(_spec_id(w, task_id), choice.get("impacts")),
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


def _answer_multi_shelter_immediate(w: World, task_id, task, choice, choice_id, facility, demanded) -> bool:
    """The immediate multi-delivery "Send to Shelters" (Community_TransportRequest 2):
    ExecuteSingleSourceMultiDest -> ExecuteImmediateDeliveryBetween per destination.

    Destinations as for the vehicle variant (every operational shelter with space, newest
    first, Take(3)), quantityPerDest = max(1, total / n). Each transfer removes what the
    source has, deposits what the shelter takes, returns the overflow, and registers the
    ACTUAL arrivals twice -- once unconditionally ("Delivery_Vehicle_<id>", so an empty
    transfer still registers a 0-person group and draws its stay duration), once more for
    a Community -> Shelter move when anyone arrived ("Multi_<id>_..."). The task completes
    either way with nothing credited as delivered. The port used to send everyone to the
    FIRST shelter: full on 6001 at step 21, so 100 people Unity housed in Shelter Bravo
    stayed in Community Amherst."""
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
    per = max(1, demanded // len(sites)) if sites else 0
    for name in sites:
        removed = -w.economy.move_population(facility, -per)
        delivered = w.economy.move_population(name, removed)
        if delivered < removed:
            w.economy.move_population(facility, removed - delivered)
        w.pending_arrivals.append((delivered, name))
        if delivered > 0:
            w.pending_arrivals.append((delivered, name))
    task.source = facility
    task.chosen_id = choice_id
    w.economy.apply_choice(task.tag, _configured_impacts(_spec_id(w, task_id), choice.get("impacts")),
                           choice.get("budgetDelayRounds", 0) or 0, "Shelter", 0)
    w.tasks.answer(task_id, 0, immediate=True, destination="Shelter",
                   counters=w.economy.counters, destination_facility=sites[0] if sites else "",
                   credit_delivered=False)
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


def _source_food(w: World, quantity, kitchen=None) -> int:
    """Pull food packs out of the operational kitchens, up to `quantity`.

    DeliverySystem moves the food OUT of the kitchen, so a 200-pack kitchen fills two
    100-pack orders per day and no more -- and it is pulled when the delivery executes,
    which is why an order placed on the round the kitchen is first stocked still fulfils."""
    remaining, sourced = quantity, 0
    for k in w.economy.operational("Kitchen"):
        if remaining <= 0:
            break
        if kitchen is not None and k["name"] != kitchen:
            continue
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
