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
from .economy import Economy, step_round as economy_step
from .flood import FloodState, update_flood
from .floodmap import FloodMap
from . import roads
from .tasks import Task, TaskBoard, demand_of
from .generation import TriggerContext, generation_pass, suitable_facilities
from .triggers import INVENTORY as _INVENTORY

# Task definitions by id, so a generated task carries its own choices.
_TASK_SPEC = {t["taskId"]: t for t in _INVENTORY}
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


class World:
    """Everything a round transition reads or writes, and nothing else.

    `facilities_for` is injected rather than derived: it returns the ordered facility list
    a task rolls against, and Unity's order is FindObjectsOfType order, which is neither
    sorted nor creation order (see PLAN.md). Guessing it here would be inventing physics."""

    __slots__ = ("rng", "flood", "fmap", "weather", "day", "segment",
                 "facilities_for", "generated", "clients", "economy", "tasks",
                 "round_index", "_trigger_memory", "use_generation",
                 "pending_arrivals", "generated_specs", "_alerts_shown",
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
        self._sourced_now = {}      # task -> packs already pulled this round
        self._food_reserved = 0     # packs promised to orders not yet loaded
        self.round_index = 0
        self._trigger_memory = {}       # stateful triggers (FloodExpanded, BudgetDropped)
        self.pending_arrivals = []      # deliveries that landed LAST round, drawn this one
        self.generated_specs = {}       # live task id -> (definition id, facility, spec)
        self._alerts_shown = set()      # Alert tasks fire once per GAME
        self._emergency_count = 0
        self._last_emergency_round = -99

    def _can_source(self, task, quantity):
        """Packs the kitchens could hand a vehicle right now, for LoadCargo's abort test.

        Only food is sourced from a building; a population relocation loads people from the
        community that asked, and that is checked when the choice is made.
        """
        if getattr(task, "tag", "") != "Food":
            return quantity
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
        from .floodmap import pack
        tiles = self.flood.tiles
        if not tiles:
            return frozenset()
        return frozenset(c for c in roads.ROAD_CELLS if pack(c[0], c[1]) in tiles)

    @staticmethod
    def _live_ids(w):
        return set(w.tasks.active)

    def _own_facilities(self, task_def):
        return suitable_facilities(task_def, self.economy.facilities())

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
            prev=self._trigger_memory)

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
        w.generated_specs = dict(self.generated_specs)
        w._alerts_shown = set(self._alerts_shown)
        w._sourced_now = dict(self._sourced_now)
        w._food_reserved = self._food_reserved
        w._emergency_count = self._emergency_count
        w._last_emergency_round = self._last_emergency_round
        w.use_generation = self.use_generation
        return w


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
                       if live_id in w.tasks.active)
    # GENERAL per-facility duplicate check, which applies to EVERY task type:
    #     activeTasks.Any(t => t.taskTitle == taskData.taskTitle
    #                       && t.affectedFacility == facilityName)
    # The port only had the Lodging-specific rule, so a food request for a community could
    # be re-created every pass while one was already live -- 45 food tasks resolved against
    # Unity's 15.
    for live_id, (def_id, fac, sp) in w.generated_specs.items():
        if live_id in w.tasks.active and fac == facility and def_id == spec["taskId"]:
            return False
    # THEN the Lodging-specific rule, which is stricter: at most one lodging task per
    # facility even across DIFFERENT lodging titles.
    if spec.get("taskTag") == "Lodging":
        for live_id, (def_id, fac, sp) in w.generated_specs.items():
            if live_id in w.tasks.active and fac == facility and sp.get("taskTag") == "Lodging":
                return False
    return True


def _pass(w: World, marks):
    """One task-generation pass. Same draws either way; only the outcome differs."""
    if w.use_generation:
        return generation_pass(w.rng, w.trigger_context(), w.facilities_for, marks=marks)
    return roll_pass(w.rng, w.facilities_for, marks=marks)


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
    arrivals = list(arrivals) + w.pending_arrivals
    w.pending_arrivals = []
    for count, facility in arrivals:
        w.clients.register_arrival(w.rng, count, w.round_index, facility, marks)
    for count, facility in w.clients.update(w.rng, w.round_index, w.economy.counters, marks):
        # Departures release occupancy at the facility they were staying in.
        name = "Motel" if "motel" in str(facility).lower() else str(facility)
        w.economy.move_population(name, -count)
        w.economy.motel_pop = w.economy.motel_population

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
        for i in range(_ROLLOVER_PASSES):
            w.segment = i
            rolls += _pass(w, marks)
        w.segment = 1
    else:
        w.segment += 1
        if w.segment in _GENERATION_SEGMENTS:
            rolls += _pass(w, marks)
    w.generated = rolls
    # THE JOIN THAT MAKES THE SURROGATE SELF-DRIVING. generation_pass decides WHICH tasks
    # fire; without this the port produced a list of ids and created nothing, so it could
    # generate a task and never answer one -- which is why every equivalence test so far
    # has had to feed it Unity's own task lifecycle.
    if w.use_generation:
        for task_id, facility in rolls:
            spec = _TASK_SPEC.get(task_id)
            if spec is None or not _admits(w, spec, facility):
                continue
            state = {"choices": spec.get("choices") or []}
            tag = spec.get("taskTag") or "None"
            t = Task(w.tasks.next_id, tag, demand_of(state, tag),
                     spec.get("roundsRemaining") or 1)
            t.destination = ""
            w.tasks.next_id += 1
            w.tasks.add(t)
            w.generated_specs[t.task_id] = (task_id, facility, spec)

    if on_flood_enter is not None:
        on_flood_enter(w)
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
    for _task_id, quantity, destination in w.tasks.tick(w.economy.counters):
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
            continue
        source = getattr(w.tasks, "_sources", {}).pop(_task_id, "")
        if source:
            w.economy.move_population(source, -quantity)
        if dest in ("Motel", "Shelter"):
            # People land in an actual facility, so its population -- and therefore the
            # triggers that read it and the bill that charges it -- move together.
            # No fallback. GetDestinationsSorted is called with includeShelters /
            # includeMotels taken from the CHOICE, so a Shelter-destination relocation
            # never spills into the motel -- it simply has nowhere to go. The port used to
            # fall back and that quietly moved people Unity would have left in place.
            target = "Motel" if dest == "Motel" else next(
                (b["name"] for b in w.economy.buildings
                 if b["type"] == "Shelter" and b["status"] == "InUse"), None)
            moved = w.economy.move_population(target, quantity) if target else 0
            if moved:
                w.pending_arrivals.append((moved, dest))
            if dest == "Motel":
                w.economy.motel_pop = w.economy.motel_population
        elif dest == "Kitchen":
            pass
        elif dest == "CaseworkSite":
            w.clients.process_home(quantity, w.economy.counters)
            w.economy.move_population("Motel", -quantity)
            w.economy.motel_pop = w.economy.motel_population
    economy_step(w.economy, day_changed, w.day)
    w.round_index += 1


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
        # How long the food takes is how long the drive takes. DEFERRED_LATENCY's 4 was
        # fitted, and behaved like a fitted constant: every value that matched one metric
        # broke another. travel_rounds returns False when the flood has cut the route, which
        # is not a slow delivery but one that never arrives.
        _kitchen = next((b["name"] for b in w.economy.buildings
                         if b["type"] == "Kitchen" and b["status"] == "InUse"), None)
        _lat = w.tasks.travel_rounds(_kitchen, str(_facility), w.flooded_road_cells(),
                                     demanded, task_id, w.segment) if _kitchen else None
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
        w.tasks.answer(task_id, 0 if _cut else demanded, immediate=immediate,
                       latency=_lat if _measured else None,
                       destination="__food__" + str(_facility),
                       counters=w.economy.counters,
                       latency_measured=_measured)
        if immediate:
            _land_now("food", demanded, str(_facility))
        w.economy.apply_choice(task.tag, choice.get("impacts"),
                               choice.get("budgetDelayRounds", 0) or 0, "", 0)
        return True
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
    _target = ("Motel" if dest_cat == "Motel" else next(
        (b["name"] for b in w.economy.buildings
         if b["type"] == "Shelter" and b["status"] == "InUse"), "Motel"))
    _lat = w.tasks.travel_rounds(str(_facility), _target, w.flooded_road_cells(),
                                 qty, task_id, w.segment)
    _cut = _lat is False
    _measured = _lat is not None and _lat is not False
    w.tasks.answer(task_id, 0 if _cut else qty, immediate=immediate,
                   latency=_lat if _measured else None,
                   destination=dest_cat, counters=w.economy.counters,
                   latency_measured=_measured)
    if immediate and qty > 0 and dest_cat in ("Motel", "Shelter"):
        target = "Motel" if dest_cat == "Motel" else next(
            (b["name"] for b in w.economy.buildings
             if b["type"] == "Shelter" and b["status"] == "InUse"), None)
        if target:
            w.economy.move_population(str(_facility), -qty)
            moved = w.economy.move_population(target, qty)
            if moved:
                w.pending_arrivals.append((moved, dest_cat))
            w.economy.motel_pop = w.economy.motel_population
    return True


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
    to_motel = (choice.get("destinationCategory") or "") == "Motel"
    if to_motel:
        m = w.economy.facility("Motel")
        res = (m or {}).get("resources") or {}
        cap = res.get("populationCapacity")
        return m is not None and (cap is None or (res.get("population") or 0) < cap)
    for b in w.economy.buildings:                    # shelter-bound
        if b["type"] != "Shelter" or b["status"] != "InUse":
            continue
        res = b.get("resources") or {}
        cap = res.get("populationCapacity")
        if cap is None or (res.get("population") or 0) < cap:
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
