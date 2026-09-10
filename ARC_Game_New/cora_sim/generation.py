"""Task generation: evaluating every trigger category to decide which tasks fire.

This is the last mechanic. Until now the port CONSUMED tasks -- the equivalence tests fed
it Unity's own task lifecycle and checked only the arithmetic. This module creates them.

WHAT MAKES IT SUBTLE, AND IT IS NOT THE CONDITIONS THEMSELVES:

1. NO SHORT-CIRCUIT. AreTriggersActivated evaluates EVERY trigger of EVERY task into a list
   and reduces with AND/OR afterwards. A task whose day trigger already failed still rolls
   its ProbabilityTrigger, so the draw count is fixed by the inventory rather than by which
   tasks fire. Short-circuiting here would be a correct-looking optimisation that silently
   shortens the RNG stream.

2. NON-GLOBAL TASKS EVALUATE ONCE PER SUITABLE FACILITY, in Unity's FindObjectsOfType order
   -- which is neither sorted nor creation order (observed: Community01, Community03,
   Community02). Each facility rolls its own probability, so the order decides WHICH
   community gets a task, not just how many draws happen.

3. SEVERAL TRIGGERS ARE STATEFUL. FloodExpanded / BudgetDecreased / SatisfactionDropped
   compare against a `previous*` field that the check itself UPDATES. Evaluating one twice
   gives two different answers, so a port that re-evaluates for convenience corrupts the
   next round's comparison. They are stepped exactly once per check here.

Conditions are transcribed from TaskTrigger.cs; parameters come from the sim_constants
export, never from the source file.
"""
from __future__ import annotations

from .triggers import INVENTORY, roll_pass


def _compare(kind: str, value, target) -> bool:
    """ComparisonType, as used by the flood/budget/satisfaction/workforce triggers."""
    if kind == "ExactMatch":
        return value == target
    if kind == "LessThan":
        return value < target
    if kind == "MoreThan" or kind == "GreaterThan":
        return value > target
    if kind == "AtLeast":
        return value >= target
    if kind == "AtMost":
        return value <= target
    return False


class TriggerContext:
    """Everything the trigger conditions read, gathered once per check.

    Passing a snapshot rather than the live World keeps the conditions pure and makes them
    unit-testable without standing up a whole simulation."""

    __slots__ = ("day", "segment", "weather", "flood_tiles", "budget", "satisfaction",
                 "free_workforce", "idle_ratio", "facilities", "prev",
                 "trained", "untrained", "idle_trained", "idle_untrained",
                 "flooded", "positions")

    def __init__(self, day=1, segment=1, weather="Sunny", flood_tiles=0, budget=0,
                 satisfaction=50.0, free_workforce=0, idle_ratio=0.0, facilities=None,
                 prev=None, trained=0, untrained=0, idle_trained=0, idle_untrained=0,
                 flooded=frozenset(), positions=None):
        # THE FLOOD TILE SET AND FACILITY TRANSFORMS, for FloodedFacilityTrigger. That
        # trigger is per-facility: it counts flood tiles in a (2r+1)^2 square around
        # facility.transform.position and compares the count to a threshold. Without these
        # two the port could not evaluate it at all, and a requireAllTriggers task whose one
        # unevaluable condition is silently skipped fires on the rest -- which is how the
        # port raised "Community Emergency Evacuation" on five traces where Unity, with no
        # flooded community anywhere, raised none.
        self.flooded = flooded
        self.positions = positions or {}
        self.trained = trained            # GetTrainedWorkersCount
        self.untrained = untrained        # GetUntrainedWorkersCount
        self.idle_trained = idle_trained      # GetAvailableTrainedWorkers
        self.idle_untrained = idle_untrained  # GetAvailableUntrainedWorkers
        self.day = day
        self.segment = segment
        self.weather = weather
        self.flood_tiles = flood_tiles
        self.budget = budget
        self.satisfaction = satisfaction
        self.free_workforce = free_workforce
        self.idle_ratio = idle_ratio
        self.facilities = facilities or []      # [{"type","operational","resources":{...}}]
        self.prev = prev if prev is not None else {}   # stateful trigger memory


def _round_ok(t, ctx):
    return (ctx.segment == t["targetRound"] if t.get("exactMatch")
            else ctx.segment >= t["targetRound"])


def _day_ok(t, ctx):
    kind = t["conditionType"]
    if kind == "SpecificDay":
        return ctx.day == t["targetDay"]
    if kind == "DayInterval":
        return t["intervalDays"] > 0 and ctx.day % t["intervalDays"] == 0
    if kind == "DayRange":
        return t["startDay"] <= ctx.day <= t["endDay"]
    if kind == "StartsFrom":
        return ctx.day >= t["startDay"]
    return False


def _weather_ok(t, ctx):
    sev = t["severity"]
    if sev == "Light":
        return ctx.weather == "Sunny"
    if sev == "Moderate":
        return ctx.weather in ("MediumRain", "SmallRain")
    if sev == "Severe":
        return ctx.weather in ("HeavyRain", "Storm")
    return False


# ResourceType is a C# ENUM ("Population", "FoodPacks") while the facility resource dict
# uses the export's camelCase keys ("population", "foodPacks"). Looking the enum name up
# directly returns 0 for everything, so EVERY resource trigger silently evaluates False --
# and since these tasks require ALL triggers, the entire Community/Shelter task family
# never fires. Measured: a self-driven episode produced lodgingResolved 3 where Unity had
# 901, and this mapping was the whole difference.
_RESOURCE_KEY = {"Population": "population", "FoodPacks": "foodPacks"}


def _resource_ok(t, ctx, facility=None):
    """Resource condition, evaluated against ONE facility when we have one.

    THIS IS TWO DIFFERENT FUNCTIONS IN THE C# AND THEY DISAGREE:

      AreTriggersActivated            -> ResourceTrigger.CheckCondition(), which scans ALL
                                         operational facilities and returns true if ANY
                                         satisfies. Used for GLOBAL tasks.
      AreTriggersActivatedForFacility -> CheckResourceTriggerForFacility(trigger, facility),
                                         which checks THAT facility and nothing else. Used
                                         for every facility-scoped task.

    Collapsing them into the "any" form makes a facility-scoped trigger all-or-nothing
    across a whole pass: either every community fires or none does. Unity's own log shows
    1-3 communities firing per pass as they individually gain food or drain population,
    totalling 35 food requests in 24 rounds where the any-form produced 15.

    `IsOperational()` still gates it: an unstaffed building is invisible here, so building
    without staffing does not silence the requests it was meant to silence."""
    candidates = ctx.facilities
    if facility is not None:
        candidates = [f for f in ctx.facilities if f.get("name") == facility]
    for f in candidates:
        if f.get("type") != t["facilityType"] or not f.get("operational"):
            continue
        res = f.get("resources") or {}
        key = _RESOURCE_KEY.get(t["resourceType"], t["resourceType"])
        amount = res.get(key, 0)
        cap = res.get(key + "Capacity", 0)
        cond = t["condition"]
        if cond == "Empty" and amount == 0:
            return True
        if cond == "Full" and cap and amount >= cap:
            return True
        if cond == "LessThan" and amount < t["threshold"]:
            return True
        if cond == "MoreThan" and amount > t["threshold"]:
            return True
        # NeedsFood (main-bugfixes d5e5f683) is BuildingResourceStorage.GetFoodNeed() > 0:
        # this facility's own population times its own rate, minus what it holds. It is what
        # gates the Shelter/Motel follow-up food request, so it needs the per-type storage
        # settings rather than the resource dict alone.
        if cond == "NeedsFood" and _food_need(f) > 0:
            return True
    return False


def _food_need(f) -> int:
    """BuildingResourceStorage.GetFoodNeed for one facility dict."""
    from .economy import C as _C, _consumes
    cfg = (_C.get("storage_by_type") or {}).get(f.get("type"), {})
    glob = _C.get("consumption") or {}
    if not _consumes(cfg, glob):
        return 0
    res = f.get("resources") or {}
    people = res.get("population") or 0
    if cfg.get("workersConsumeFoodToo", glob.get("workersConsumeFoodToo", True)):
        people += (f.get("trained") or 0) + (f.get("untrained") or 0)
    per = int(cfg.get("foodPerPersonPerNRounds") or glob.get("foodPerPersonPerNRounds", 1) or 1)
    return max(0, people * per - (res.get("foodPacks") or 0))


def _stateful(kind, current, target, comparison, key, ctx):
    """FloodExpanded / BudgetDecreased and friends: compare against remembered value, then
    UPDATE it. The update is the part that makes re-evaluation unsafe."""
    previous = ctx.prev.get(key, 0)
    ctx.prev[key] = current
    if kind.endswith("Expanded") or kind.endswith("Increased"):
        delta = current - previous
    elif kind.endswith("Shrank") or kind.endswith("Decreased") or kind.endswith("Dropped"):
        delta = previous - current
    else:
        delta = abs(current - previous)
    return delta > 0 and _compare(comparison, delta, target)


def _flood_ok(t, ctx):
    if t["conditionType"] == "CurrentFloodTiles":
        return _compare(t["comparison"], ctx.flood_tiles, t["targetValue"])
    return _stateful(t["conditionType"], ctx.flood_tiles, t["targetValue"],
                     t["comparison"], "flood", ctx)


def _budget_ok(t, ctx):
    if t["conditionType"] == "CurrentAmount":
        return _compare(t["comparison"], ctx.budget, t["targetValue"])
    return _stateful(t["conditionType"], ctx.budget, t["targetValue"],
                     t["comparison"], "budget", ctx)


def _satisfaction_ok(t, ctx):
    if t["conditionType"] == "CurrentLevel":
        return _compare(t["comparison"], ctx.satisfaction, t["targetValue"])
    return _stateful(t["conditionType"], ctx.satisfaction, t["targetValue"],
                     t["comparison"], "satisfaction", ctx)


def _workforce_ok(t, ctx):
    """WorkforceTrigger, one branch per WorkerSystem getter.

    The port used to funnel every condition whose name contained "Idle" or "Ratio" into a
    single idle_ratio, which conflates quantities on different SCALES:
    TrainedUntrainedRatio is trained/untrained, about 1, and is compared against a target of
    1; IdleUntrainedWorkerPercentage is a percentage compared against 20. One value cannot
    serve both, and the collapse is why Training Recommendation Alert and Workforce
    Optimization Alert never fired -- two of the three tasks missing from the port's
    round-5 board.

        CurrentAvailableWorkforce        GetTotalAvailableWorkforce()
        TrainedUntrainedRatio            untrained > 0 ? trained / untrained : 0
        IdleWorkerPercentage             total    > 0 ? idle / total * 100 : 0
        IdleUntrainedWorkerPercentage    untrained> 0 ? idleUntrained / untrained * 100 : 0
    """
    kind = t["conditionType"]
    if kind == "TrainedUntrainedRatio":
        value = (ctx.trained / ctx.untrained) if ctx.untrained > 0 else 0.0
    elif kind == "IdleWorkerPercentage":
        total = ctx.trained + ctx.untrained
        value = ((ctx.idle_trained + ctx.idle_untrained) / total * 100.0) if total > 0 else 0.0
    elif kind == "IdleUntrainedWorkerPercentage":
        value = (ctx.idle_untrained / ctx.untrained * 100.0) if ctx.untrained > 0 else 0.0
    else:                                   # CurrentAvailableWorkforce
        value = ctx.free_workforce
    return _compare(t["comparison"], value, t["targetValue"])


def _facility_status_ok(t, ctx):
    n = sum(1 for f in ctx.facilities
            if f.get("type") == t["facilityType"] and f.get("status") == t["requiredStatus"])
    return n >= t["minimumCount"]


_FF_KIND = {0: "AnyFacility", 1: "AnyBuilding", 2: "AnyPrebuilt",
            3: "SpecificBuildingType", 4: "SpecificPrebuiltType"}
_FF_CMP = {0: "ExactMatch", 1: "AtLeast", 2: "MoreThan", 3: "LessThan", 4: "AtMost"}
_PREBUILT = {0: "Community", 1: "Motel"}
_BUILDING = {0: "Kitchen", 1: "Shelter", 2: "CaseworkSite", 3: "Community", 4: "Motel"}


def _flooded_facility_ok(t, ctx, facility=None):
    """FloodedFacilityTrigger for ONE facility (CheckFloodedFacilityTriggerForFacility).

    Mirrors TaskDatabases.cs:329-: the facility must match the trigger's type filter, then
    the flood tiles in the square of `detectionRadius` around its TRANSFORM are counted --
    Tilemap.WorldToCell floors each offset position, so the square is the integer cells
    around floor(transform) -- and compared with the trigger's OWN ComparisonType, whose
    order (ExactMatch, AtLeast, MoreThan, LessThan, AtMost) differs from the generic
    TaskTrigger enum. Enum values arrive as ints from the .asset dump and as names from the
    live exporter; both are accepted. The .asset for Community Emergency Evacuation reads
    facilityType 4 / prebuilt 0 / comparison 1 / threshold 1 / radius 2: any Community with
    at least one flood tile within two cells.
    """
    from math import floor
    if facility is None:
        return False          # global CheckCondition path is not used for these tasks
    if not isinstance(facility, dict):
        # generation_pass passes the facility NAME; resolve it the way the resource
        # condition does, against the snapshot in ctx.facilities.
        facility = next((f for f in ctx.facilities if f.get("name") == facility), None)
        if facility is None:
            return False
    ftype = facility.get("type")
    kind = _FF_KIND.get(t.get("facilityType"), t.get("facilityType"))
    prebuilt = ftype in ("Community", "Motel")
    if kind == "AnyBuilding" and prebuilt:
        return False
    if kind == "AnyPrebuilt" and not prebuilt:
        return False
    if kind == "SpecificBuildingType":
        want = _BUILDING.get(t.get("specificBuildingType"), t.get("specificBuildingType"))
        if prebuilt or ftype != want:
            return False
    if kind == "SpecificPrebuiltType":
        want = _PREBUILT.get(t.get("specificPrebuiltType"), t.get("specificPrebuiltType"))
        if not prebuilt or ftype != want:
            return False
    pos = ctx.positions.get(facility.get("name"))
    if pos is None:
        return False
    r = int(t.get("detectionRadius", 2))
    cx, cy = floor(pos[0]), floor(pos[1])
    n = sum(1 for dx in range(-r, r + 1) for dy in range(-r, r + 1)
            if (cx + dx, cy + dy) in ctx.flooded)
    thr = int(t.get("floodTileThreshold", 1))
    cmp = _FF_CMP.get(t.get("comparison"), t.get("comparison"))
    return {"ExactMatch": n == thr, "AtLeast": n >= thr, "MoreThan": n > thr,
            "LessThan": n < thr, "AtMost": n <= thr}[cmp]


_EVALUATORS = (("round", _round_ok), ("day", _day_ok), ("resource", _resource_ok),
               ("floodTile", _flood_ok), ("floodedFacility", _flooded_facility_ok),
               ("budget", _budget_ok),
               ("satisfaction", _satisfaction_ok), ("workforce", _workforce_ok),
               ("facilityStatus", _facility_status_ok), ("weather", _weather_ok))


def evaluate_task(task_def: dict, ctx: TriggerContext, rng, marks=None,
                  facility=None) -> bool:
    """One task's triggers for ONE facility context. Draws for every ProbabilityTrigger.

    Deliberately mirrors the C# structure: collect every result, THEN reduce. The
    probability rolls happen in the trigger-category order the C# uses, which is what keeps
    the draw positions right."""
    from .triggers import threshold as _prob_threshold
    results = []
    triggers = task_def.get("triggers") or {}
    for key, fn in _EVALUATORS:
        for t in triggers.get(key) or []:
            # Only the resource condition is facility-scoped; round, day, weather, budget,
            # satisfaction and workforce are global in both C# paths.
            results.append(bool(fn(t, ctx, facility)
                                if key in ("resource", "floodedFacility") else fn(t, ctx)))
    for p in task_def.get("probabilities") or []:
        if marks is not None:
            marks.append("draw:TaskTrigger.probability")
        results.append(rng.range01_lt(_prob_threshold(p)))
    if not results:
        return False                       # "No triggers = never activate"
    return all(results) if task_def.get("requireAllTriggers") else any(results)


def suitable_facilities(task_def: dict, facilities) -> list:
    """FindAllSuitableFacilities: which facilities a non-global task evaluates against.

    THE OPERATIONAL GATE IS THE POINT. A constructed Building counts only if
    IsOperational() -- i.e. it is staffed -- while PrebuiltBuildings are always suitable.
    Measured: an episode that built shelters but never staffed them logged "Found 0
    suitable facilities for Food Request From Shelter" for its whole length, and Unity drew
    exactly 3 probability rolls per pass throughout. Dropping the gate makes the port draw
    4 on those rounds and desynchronise the stream from the moment a shelter is built.

    So building without staffing is doubly useless: it does not serve anyone AND it does
    not silence the requests it was meant to answer."""
    if task_def.get("isGlobalTask"):
        return []
    want = task_def.get("targetFacilityType")
    out = []
    for f in facilities:
        if f.get("type") != want:
            continue
        if f.get("prebuilt") or f.get("operational"):
            out.append(f.get("name"))
    return out


def generation_pass(rng, ctx: TriggerContext, facilities_for, inventory=None, marks=None):
    """CheckTriggeredTasksPerFacility. Returns [(taskId, facility)] that fired.

    Global tasks evaluate once with no facility; the rest evaluate once per suitable
    facility, in the caller's order -- Unity's, not one this module invents."""
    fired = []
    for task_def in (INVENTORY if inventory is None else inventory):
        if task_def.get("isGlobalTask"):
            if evaluate_task(task_def, ctx, rng, marks):
                fired.append((task_def["taskId"], None))
            continue
        for facility in facilities_for(task_def):
            if evaluate_task(task_def, ctx, rng, marks, facility=facility):
                fired.append((task_def["taskId"], facility))
    return fired
