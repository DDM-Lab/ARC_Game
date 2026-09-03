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
                 "_emergency_count", "_last_emergency_round")

    def __init__(self, rng, weather, day=1, segment=1, flood=None, fmap=None,
                 facilities_for=None):
        self.rng = rng
        self.fmap = fmap if fmap is not None else FloodMap.load()
        self.flood = flood if flood is not None else FloodState()
        self.weather = weather
        self.day = day
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
        self.tasks = TaskBoard()
        self.round_index = 0
        self._trigger_memory = {}       # stateful triggers (FloodExpanded, BudgetDropped)
        self.pending_arrivals = []      # deliveries that landed LAST round, drawn this one
        self.generated_specs = {}       # live task id -> (definition id, facility, spec)
        self._alerts_shown = set()      # Alert tasks fire once per GAME
        self._emergency_count = 0
        self._last_emergency_round = -99

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
        return not any(s[0] == spec["taskId"] for s in w.generated_specs.values()
                       if s[0] in w._live_ids(w))
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
        w.segment = 1
        w.weather = generate_weather(w.rng, marks=marks)
        for _ in range(_ROLLOVER_PASSES):
            rolls += _pass(w, marks)
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
        source = getattr(w.tasks, "_sources", {}).pop(_task_id, "")
        if source:
            w.economy.move_population(source, -quantity)
        if dest in ("Motel", "Shelter"):
            # People land in an actual facility, so its population -- and therefore the
            # triggers that read it and the bill that charges it -- move together.
            # A relocation aimed at a Shelter with no OPERATIONAL shelter to receive it
            # still houses people -- it falls back to the motel, which is why an episode
            # that never builds a shelter still accumulates motel occupancy and casework
            # demand. Dropping the delivery instead sent caseworkRequested to 0 against
            # Unity's 898 while lodging still looked fulfilled.
            target = "Motel" if dest == "Motel" else next(
                (b["name"] for b in w.economy.buildings
                 if b["type"] == "Shelter" and b["status"] == "InUse"), "Motel")
            moved = w.economy.move_population(target, quantity)
            if target == "Motel":
                dest = "Motel"
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
    entry = w.generated_specs.get(task_id)
    task = w.tasks.active.get(task_id)
    if entry is None or task is None:
        return False
    _def_id, _facility, spec = entry
    choice = next((c for c in (spec.get("choices") or [])
                   if c.get("choiceId") == choice_id), None)
    if choice is None:
        return False
    qty = choice.get("deliveryQuantity", 0) or 0
    w.economy.apply_choice(task.tag, choice.get("impacts"),
                           choice.get("budgetDelayRounds", 0) or 0,
                           choice.get("destinationCategory") or "",
                           qty)
    # RELOCATION MOVES PEOPLE OUT OF THE SOURCE. Without this the community stays at 400
    # forever, its population-threshold trigger never stops firing, and the port generates
    # relocation demand indefinitely -- the second half of the 2.7x over-generation.
    # Population and food move when the delivery LANDS, not when the choice is made. Doing
    # it at answer time drains the source community several rounds early, which pushes it
    # under the MoreThan-200 threshold and silences the relocation trigger long before
    # Unity's does: the port fell to 4 trigger passes against Unity's 17.
    task.source = str(_facility)
    w.tasks.answer(task_id, choice.get("deliveryQuantity") or 0,
                   immediate=bool(choice.get("immediateDelivery")),
                   destination=choice.get("destinationCategory") or "",
                   counters=w.economy.counters)
    return True


def open_choices(w: World):
    """Every (task_id, choice_id) the planner may answer this round."""
    out = []
    for task_id in w.tasks.active:
        entry = w.generated_specs.get(task_id)
        if not entry:
            continue
        for c in (entry[2].get("choices") or []):
            out.append((task_id, c.get("choiceId")))
    return out
