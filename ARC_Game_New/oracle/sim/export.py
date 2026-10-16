"""The surrogate's world as Unity's get_game_state payload (GameStatePayload).

game_state(w) is what oracle.sim.env.SimEnv hands to the shared code -- cora.observation,
cora.actions.enumerate_actions, cora.executor, the bench baselines, cora.scoring -- so a policy
written against Unity plays the surrogate unchanged. Every field those consumers read is
exported; fields only the GUI reads (pendingEffects, dailyReports, environmental flavour) are
present with neutral values.

Text (task titles, descriptions, choice texts) is resolved as TaskSystem.ResolvePlaceholders
resolves it, from the TaskData assets' templates; tasks built in code (casework requests,
departure alerts, morning reports) carry the texts their C# constructors write.
"""
from __future__ import annotations

import glob
import json
import os

from . import paths as P
import oracle.sim.sim as S

# The surrogate exported every choice as feasible until 2026-10-16; Unity marks the ones the
# player's panel greys out (_feasibility). False restores the old export (EXPORT_VERSION 1).
FEASIBILITY = os.environ.get("CORA_SIM_FEASIBILITY", "1") != "0"
EXPORT_VERSION = 2 if FEASIBILITY else 1
from .economy import C as _C, REQUIRED_WORKFORCE

# ── the TaskData assets: text templates and the choice fields the RPC export lacks ──────

_ASSETS = None


def _assets() -> dict:
    """{taskId: {"description", "choices": {choiceId: {"choiceText", "destinationType", ...}}}}"""
    global _ASSETS
    if _ASSETS is None:
        # Generated from the TaskData assets by oracle.sim.export_assets (corpus/task_text.json),
        # so the surrogate needs no Unity project at run time.
        raw = json.load(open(os.path.join(P.PKG, "corpus", "task_text.json"), encoding="utf-8"))
        _ASSETS = {tid: {"title": t["title"], "description": t["description"],
                         "choices": {int(cid): c for cid, c in t["choices"].items()}}
                   for tid, t in raw.items()}
    return _ASSETS


def _resolve(text, plain, food=None, population=None) -> str:
    """GameTask.ResolvePlaceholders(text, plainFacilityName: true, foodAmountOverride)."""
    if not text:
        return text or ""
    text = text.replace("[facility_name]", plain).replace("[facility_name_plain]", plain)
    if "[relocation_rounds]" in text:
        text = text.replace("[relocation_rounds]", str(int(_C.get("relocation_delay_rounds") or 0)))
    if "[food_amount]" in text:
        text = text.replace("[food_amount]", str(food if food is not None else 0))
    if "[population_amount]" in text:
        text = text.replace("[population_amount]", str(population if population is not None else 0))
    return text


# ── names ──────────────────────────────────────────────────────────────────────────────

def _object_name(w, display) -> str:
    """Unity's GameObject name for a facility: <Type>_<site> for a built building, the display
    name for a prebuilt (Community X, Motel)."""
    b = w.economy.facility(display)
    if b is not None and b.get("site_id") is not None and b["status"] != "Prebuilt":
        return f"{b['type']}_{b['site_id']}"
    built = getattr(w.economy, "built", {}).get(display)
    if built:
        return f"{built[0]}_{built[1]}"
    return display


def _trigger(w, affected):
    """TaskSystem.FindTriggeringFacility(task): the first constructed building (FindObjectsOfType
    order, newest first) that is OPERATIONAL and whose GameObject name CONTAINS the task's
    affectedFacility -- a substring match, so "Shelter_1" also finds "Shelter_10" -- else the first
    prebuilt whose name contains it. None when nothing matches (a demolished or idle building)."""
    if not affected:
        return None
    built, pre = _built(w)
    if S.EXACT_FACILITY_NAMES:
        # The fixed FindFacilityByName: an exact name decides on its own (operational or None);
        # the substring match only serves generic names no building carries ("Shelter").
        for b in built:
            if f"{b['type']}_{b['site_id']}" == affected:
                return b if b["status"] == "InUse" else None
        for b in pre:
            if b["name"] == affected:
                return b
    for b in built:
        if affected in f"{b['type']}_{b['site_id']}" and b["status"] == "InUse":
            return b
    for b in pre:
        if affected in b["name"]:
            return b
    return None


# ── facilities, sites, workforce, logistics ────────────────────────────────────────────

def _built(w):
    """Constructed buildings in FindObjectsOfType order (newest first), then the prebuilts."""
    e = w.economy
    built = [b for b in e.buildings if b["status"] != "Prebuilt" and not b.get("destroyed")]
    pre = [b for b in e.buildings if b["status"] == "Prebuilt"]
    return built[::-1], pre


def _unloading(w):
    """Deliveries that unloaded on the +35 paused frame: still Active when the state is exported
    (CompleteDelivery is +36), so they still hold their reservations there (Fleet.run_epilogue)."""
    return getattr(w.tasks.fleet, "export_unloading", None) or []


def _unloading_to(w, name) -> int:
    """Population still reserved incoming at `name` by an unloading delivery."""
    return sum(p[1] for p in _unloading(w) if str(p[2] or "") in (name, "Shelter:" + name))


def _unloading_food_to(w, name) -> int:
    return sum(p[1] for p in _unloading(w) if str(p[2] or "").startswith("__food__")
               and str(p[2])[8:].split("|")[0] == name)


def _incoming(w, name) -> int:
    return S._walking_to(w, name) + w.tasks.inbound_to(name) + _unloading_to(w, name)


def _facilities(w):
    pos = w._positions()
    built, pre = _built(w)
    out = []
    for b in built + pre:
        res = b.get("resources") or {}
        prebuilt = b["status"] == "Prebuilt"
        p = pos.get(b["name"]) or (0.0, 0.0)
        out.append({
            "facilityName": b["name"], "facilityType": "Prebuilt" if prebuilt else "Building",
            "buildingType": b["type"],
            "isOperational": prebuilt or b["status"] == "InUse",
            "resources": {"foodPacks": res.get("foodPacks") or 0,
                          "foodPacksCapacity": res.get("foodPacksCapacity") or 0,
                          "population": res.get("population") or 0,
                          "populationCapacity": res.get("populationCapacity") or 0,
                          "untrainedWorkers": 0, "trainedWorkers": 0},
            "currentPopulation": res.get("population") or 0,
            "populationCapacity": res.get("populationCapacity") or 0,
            "position": {"x": float(p[0]), "y": float(p[1]), "z": 0.0},
            "buildingStatus": "" if prebuilt else b["status"],
            "assignedWorkforce": 0 if prebuilt else (b.get("assigned") or 0),
            "requiredWorkforce": 0 if prebuilt else REQUIRED_WORKFORCE,
            "originalSiteId": 0 if prebuilt else b.get("site_id"),
            "incomingPopulation": _incoming(w, b["name"])})
    return out


def _sites(w):
    """Free AbandonedSites (descending id, as FindObjectsOfType returns them)."""
    raw = _C.get("site_positions") or {}
    used = w.economy.used_sites
    out = []
    for sid in sorted((int(k) for k in raw), reverse=True):
        if sid in used:
            continue
        x, y = raw[str(sid)] if str(sid) in raw else raw[sid]
        out.append({"siteId": sid, "siteName": f"AbandonedSite_{sid}", "isAvailable": True,
                    "position": {"x": float(x), "y": float(y), "z": 0.0}})
    return out


def _vehicles(w):
    f = w.tasks.fleet
    n = len(f.pos)
    out = []
    for i in reversed(range(n)):           # FindObjectsOfType: Vehicle 4 .. Vehicle 1
        load = f.carrying[i]
        snap = getattr(f, "export_status", None)
        if snap is not None and i < len(snap) and snap[i] != "Damaged" and not f.damaged[i]:
            status = snap[i]                   # as of the export frame (Fleet.run_epilogue)
        elif f.damaged[i]:
            status = "Damaged"
        elif load is not None or f.trip[i] is not None:
            status = "InTransit"
        else:
            status = "Idle"
        out.append({"vehicleName": f"Vehicle {i + 1}", "vehicleStatus": status,
                    "currentCapacity": (load[1] if load is not None else 0), "maxCapacity": S.VEHICLE_CAPACITY,
                    "currentCargo": "Population", "currentTask": ""})
    return out


def _workforce(e):
    arr_t = sum(1 for _d, k in e.arriving if k == "trained")
    arr_u = sum(1 for _d, k in e.arriving if k == "untrained")
    trained = e.free_trained + e.working_trained + arr_t
    untrained = e.free_untrained + e.working_untrained + arr_u + len(e.in_training)
    return {"freeTrainedWorkers": e.free_trained, "freeUntrainedWorkers": e.free_untrained,
            "workingTrainedWorkers": e.working_trained, "workingUntrainedWorkers": e.working_untrained,
            "trainedWorkersNotArrived": arr_t, "untrainedWorkersNotArrived": arr_u,
            "untrainedWorkersInTraining": len(e.in_training),
            "totalTrainedWorkers": trained, "totalUntrainedWorkers": untrained,
            "totalAvailableWorkforce": 2 * e.free_trained + e.free_untrained,
            "totalWorkforceCapacity": 2 * trained + untrained,
            "untrainedWorkerCost": _C["untrained_cost"], "trainedWorkerCost": _C["trained_cost"],
            "trainingCostPerWorker": _C["training_cost"],
            "trainingDurationDays": _C["training_days"], "newWorkersHiredToday": 0}


# ── tasks ──────────────────────────────────────────────────────────────────────────────

def _space(w, b) -> int:
    """GetDestinationsSorted's effectiveSpace for one destination (no path filter)."""
    res = b.get("resources") or {}
    return max(0, (res.get("populationCapacity") or 0) - (res.get("population") or 0)
               - w.tasks.inbound_to(b["name"]) - _unloading_to(w, b["name"]) - S._walking_to(w, b["name"]))


def _has_destination_space(w, src, to_shelter, to_motel) -> bool:
    """ClientRelocationHandler.HasDestinationSpace: the source (the task's triggering facility)
    still has people and some operational shelter / the motel has effective space."""
    if src is None or ((src.get("resources") or {}).get("population") or 0) <= 0:
        return False
    for b in w.economy.buildings:
        if b is src or b.get("destroyed"):
            continue
        if (to_shelter and b["type"] == "Shelter" and b["status"] == "InUse") or (to_motel and b["type"] == "Motel"):
            if _space(w, b) > 0:
                return True
    return False


def _feasibility(w, c, facility, qty, gone=False, dest=None) -> tuple:
    """TaskDetailUI.IsChoiceFeasibleFor, the check that greys a choice out in the player's task
    panel and that the gym exports per choice as (feasible, unavailableReason). Ported literally,
    reason strings included, because the models read them; it is NOT the execution gate
    (S._food_can_execute / S._relocation_can_execute decide what an answer does):
      - a choice that delivers nothing, an immediate food airlift and a send-to-casework choice
        are always feasible;
      - a kitchen food delivery (FoodDeliveryHandler.CanExecute) needs need left after the food
        already inbound, a reachable operational kitchen with unreserved stock, enough
        unreserved stock everywhere for a requireFullQuantity choice, and an undamaged vehicle;
      - a relocation (ClientRelocationHandler.CheckFeasibility) needs residents left and some
        space at the destinations; a road (non-immediate) one also an undamaged vehicle and a
        flood-free route to a destination with space -- though relocations self-walk since
        5d922203 and use no vehicle (a game bug, kept for parity).
    `facility` is the task's triggering facility, `qty` the choice's resolved deliveryQuantity."""
    if not (c.get("triggersDelivery") or c.get("immediateDelivery")):
        return True, ""
    cargo, immediate = c.get("deliveryCargoType"), bool(c.get("immediateDelivery"))
    undamaged = any(not d for d in w.tasks.fleet.damaged)
    if cargo == 1:
        if immediate:
            return True, ""
        # FoodDeliveryHandler.CanExecute's destination is FindTriggeringFacility: an operational
        # building (uct 7021 d24: Shelter Bravo's follow-up food request, its shelter not
        # operational, "Destination not found" where the port reached the need check).
        if gone or not facility or w.economy.facility(facility) is None:
            return False, "Destination not found"
        # ...and THAT building is the destination: FindTriggeringFacility's substring match can
        # return another one (uct-f7 1000 7009 d14: Shelter Alpha's request, "Shelter_1" ->
        # Shelter_11 = Shelter Bravo, "104 meals already inbound" -- Bravo's deliveries).
        facility = dest or facility
        inbound = S._food_inbound(w, facility) + _unloading_food_to(w, facility)
        need = max(0, int(qty) - inbound)
        if need <= 0:
            return False, (f"{inbound} meals already inbound \u2014 need is covered" if inbound > 0
                           else "No food is currently needed here")
        dst = w._facility_cell(facility)
        flooded = w.flooded_road_cells()
        outbound = w.tasks.outbound_by_kitchen()
        for p in _unloading(w):
            tag = str(p[2] or "")
            if tag.startswith("__food__") and "|" in tag:
                outbound[tag.split("|", 1)[1]] = outbound.get(tag.split("|", 1)[1], 0) + p[1]
        reachable, total_reachable, raw, effective = False, 0, 0, 0
        for k in w.economy.buildings:
            if k["type"] != "Kitchen" or k["status"] != "InUse":
                continue
            stock = (k.get("resources") or {}).get("foodPacks") or 0
            free = max(0, stock - outbound.get(k["name"], 0))
            raw += stock
            effective += free
            src = w._facility_cell(k["name"])
            if src is None or dst is None or S.roads.path_length(src, dst, flooded) is None:
                continue
            reachable = True
            total_reachable += free
        if not reachable:
            return False, "No kitchen is reachable: either none exist on the map, or every route to one is blocked."
        if total_reachable <= 0:
            return False, ("All available meals are already scheduled for other deliveries" if raw > 0
                           else "No meals available across any kitchen")
        if c.get("requireFullQuantity") and effective < need:
            return False, ("Not enough food across all kitchens for this request (some meals may already be "
                           f"scheduled for other deliveries). Available: {effective}, Required: {need}")
        if not undamaged:
            return False, "No undamaged vehicle available for food delivery"
        return True, ""
    cat = c.get("destinationCategory") or ""
    if cargo == 0 and cat == "CaseworkSite":
        return True, ""
    if cargo == 0:
        to_motel = cat == "Motel"
        to_shelter = not to_motel
        src = w.economy.facility(facility) if facility else None
        if src is None:
            return False, "Source facility not found"
        if ((src.get("resources") or {}).get("population") or 0) <= 0:
            return False, "No residents left to relocate"
        label = "shelter/motel" if to_shelter and to_motel else "shelter" if to_shelter else "motel"
        if sum(n for _, n in S._walk_destinations(w, facility, to_shelter, to_motel, filter_by_path=False)) <= 0:
            return False, f"No space available at any {label}"
        if immediate:
            return True, ""
        if not undamaged:
            return False, "No undamaged vehicle available"
        if not S._walk_destinations(w, facility, to_shelter, to_motel, filter_by_path=True):
            return False, f"All routes to the {label} are blocked by flooding"
    return True, ""


def _food_storage(b):
    """TaskSystem.PopulationFoodStorage: the facility's storage if it eats, else None."""
    if b is None:
        return None
    cfg = (_C.get("storage_by_type") or {}).get(b["type"]) or {}
    return b if cfg.get("consumptionEnabled") else None


def _priced(choice, qty):
    out = []
    cpu = float(choice.get("costPerUnit") or 0)
    for imp in choice.get("impacts") or []:
        v = float(imp.get("value") or 0)
        if imp.get("type") == "Budget" and cpu > 0 and qty is not None and v < 0 \
                and choice.get("deliveryCargoType") == 1:
            v = -(cpu * qty)
        if v != 0:
            out.append({"type": imp.get("type"), "value": int(round(v))})
    return out


def _task(w, tid, task):
    def_id, facility, spec = w.generated_specs[tid]
    facility = facility or ""
    asset = _assets().get(def_id) or {}
    affected = _object_name(w, facility) if facility else (spec.get("targetFacilityType") or "")
    plain = facility or affected
    title, desc, choices = spec.get("taskTitle") or "", asset.get("description") or "", []
    has_pop_food = any(c.get("deliveryCargoType") == 1 and c.get("quantityType") == "PopulationBased"
                       for c in spec.get("choices") or [])
    trig = _trigger(w, affected) if facility else None
    store = _food_storage(trig) if has_pop_food else None
    live_food = int(store.get("outstanding_need") or 0) if store is not None else None
    # [food_amount] is the LIVE need (GetTaskContextFromTask's LiveFoodNeed override) while
    # FindTriggeringFacility finds the facility; otherwise GameTask.foodAmount -- set at creation
    # and by RefreshTaskAgainstLiveState in the round-end/CompleteTask sweeps (sim._sweep_stale_tasks),
    # so it can be stale (uct 1000 7000 d26: Shelter Echo deconstructing, "25 meals" from the
    # last sweep, where the port fell back to the authored 100). The first live read stands in
    # A task generated from the database starts at 0 ("Refreshed live food need ... 0 -> 500"):
    # one whose facility stopped operating before any sweep read it shows "0 meals" (uct-noop
    # 7014 d18, Shelter Alpha deconstructing). Without a population-based choice (a community
    # request) the largest authored quantity stands in.
    gone = trig is None
    amounts = getattr(w, "food_amounts", None) or {}
    if has_pop_food:
        food_amount = live_food if live_food is not None else amounts.get(tid, 0)
    else:
        food_amount = max([int(c.get("deliveryQuantity") or 0) for c in spec.get("choices") or []
                           if c.get("deliveryCargoType") == 1] or [0])
    pop_amount = next((int(c.get("deliveryQuantity") or 0) for c in spec.get("choices") or []
                       if c.get("deliveryCargoType") == 0), 0)

    if def_id == S.CASEWORK_SPEC_ID:
        # GenerateCaseworkTask builds ONE choice (send to casework); the port's spec also holds a
        # non-delivering choice 2 that Unity never offers. populationAmount is the group's live
        # casework need (RefreshTaskAgainstLiveState).
        # With the facility unreachable Unity keeps the last refreshed value; the group's
        # current need stands in for it (it only moves when casework is processed).
        g = w.clients.group(spec.get("_gid"))
        c1 = next((c for c in spec.get("choices") or [] if c.get("choiceId") == 1), {})
        pop_amount = g.with_need if g is not None else int(c1.get("deliveryQuantity") or 0)
        title = "Casework Request"
        desc = (f"Clients have been in shelter for {facility} rounds and are requesting casework "
                f"assistance.|CLIENT_GROUP_ID:{spec.get('_gid')}")
    elif spec.get("taskTitle") and def_id not in _assets():
        desc = spec.get("taskDescription", desc)       # code-built: morning report, departures

    for c in spec.get("choices") or []:
        if def_id == S.CASEWORK_SPEC_ID and c.get("choiceId") != 1:
            continue
        delivers = bool(c.get("triggersDelivery") or c.get("immediateDelivery"))
        cat = c.get("destinationCategory") or ""
        if (delivers and c.get("deliveryCargoType") == 0 and cat != "CaseworkSite"
                and not _has_destination_space(w, trig, cat != "Motel", cat == "Motel")):
            continue                                     # B5/D5: no room anywhere for these people
        a_choice = (asset.get("choices") or {}).get(c.get("choiceId")) or {}
        text = c.get("choiceText") or a_choice.get("choiceText") or ""
        if def_id == S.CASEWORK_SPEC_ID:
            text = "Send [population_amount] clients to a casework site"
        qty = 0
        if delivers:
            qty = int(c.get("deliveryQuantity") or 0)
            if c.get("quantityType") == "PopulationBased" and not gone:
                need = int(trig.get("outstanding_need") or 0)
                pct = float(c.get("deliveryPercentage") or 0) or 100.0
                qty = int(round(need * pct / 100.0))
        feasible, why = (_feasibility(w, c, facility, qty, gone, trig["name"] if trig else None)
                         if FEASIBILITY else (True, ""))
        choices.append({
            "choiceId": c.get("choiceId"),
            "choiceText": _resolve(text, plain, food_amount, pop_amount),
            "impacts": _priced(c, qty if delivers else None),
            "destinationCategory": cat if delivers else "",
            "deliveryQuantity": qty,
            "immediateDelivery": bool(c.get("immediateDelivery")) if delivers else False,
            "triggersDelivery": bool(c.get("triggersDelivery")) if delivers else False,
            "feasible": feasible, "unavailableReason": why})
    # 1.0.2: a task ClientStayTracker builds in code carries "uid-" + its uid, which equals its
    # taskId in a fresh game process (both counters advance per task); other code-built tasks "".
    stable = (f"uid-{tid}" if def_id == S.CODE_BUILT_TASKS["Casework Request"]
              else def_id if def_id in S._TASK_SPEC and def_id not in S.CODE_BUILT_TASKS.values() else "")
    return {"taskId": tid, "stableTaskId": stable,
            "taskTitle": _resolve(title, plain, food_amount, pop_amount),
            "taskDescription": _resolve(desc, plain, food_amount, pop_amount),
            "taskType": spec.get("taskType") or task.task_type,
            "affectedFacility": affected, "roundsRemaining": task.rounds_remaining,
            "choices": choices}


def _tasks(w):
    out = []
    for tid, task in w.tasks.active.items():
        if task.resolved or tid not in w.generated_specs:
            continue
        out.append(_task(w, tid, task))
    return out


# ── the payload ────────────────────────────────────────────────────────────────────────

def game_state(w) -> dict:
    e = w.economy
    final_day, per_day = S._FINAL_DAY, S.ROUNDS_PER_DAY
    over = w.day > final_day or (w.day == final_day and w.segment >= per_day)
    facs = _facilities(w)
    sites = _sites(w)
    vehicles = _vehicles(w)
    walks = [{"taskId": tid, "source": _object_name(w, src), "destination": _object_name(w, dst),
              "quantity": qty, "roundsRemaining": rounds} for rounds, src, dst, qty, tid in w.walks]
    idle = sum(1 for v in vehicles if v["vehicleStatus"] == "Idle")
    built, _pre = _built(w)
    return {
        "sessionInfo": {"currentDay": w.day, "currentRound": w.segment,
                        "currentGameTime": f"Day {w.day}, Round {w.segment}",
                        "simulationSpeed": 0.0, "isPaused": True, "finalDay": final_day,
                        "roundsPerDay": per_day, "isGameOver": over},
        "satisfactionAndBudget": {"efficiency": e.efficiency, "satisfaction": int(e.satisfaction),
                                  "budget": e.budget},
        "allActiveTasks": _tasks(w),
        "mapState": {"facilities": facs, "vehicles": vehicles,
                     "totalPopulation": sum(f["currentPopulation"] for f in facs),
                     "floodState": {"isActive": True, "affectedRoads": 0, "blockedRoutes": [],
                                    "waterLevel": 0.0},
                     "abandonedSites": sites},
        "logistics": {"pendingRelocations": walks, "availableVehicles": idle,
                      "vehiclesInTransit": sum(1 for v in vehicles if v["vehicleStatus"] == "InTransit"),
                      "damagedVehicles": sum(1 for v in vehicles if v["vehicleStatus"] == "Damaged"),
                      "activeDeliveries": []},
        "workforceState": _workforce(e),
        "constructionState": {
            "availableSites": sites,
            "buildingsUnderConstruction": [f"{b['type']}_{b['site_id']}" for b in built
                                           if b["status"] == "UnderConstruction"],
            "buildingsNeedingWorkers": [f"{b['type']}_{b['site_id']}" for b in built
                                        if b["status"] == "NeedWorker"],
            "buildingConstructionCost": _C["build_cost"]["Shelter"],
            "constructionTimeDays": float(_C["construction_rounds"]),
            "deconstructionTimeDays": 3.0},
        "rewardMetrics": e.metrics(),
        "pendingEffects": [], "dailyReports": [],
        "motelCostPerPersonPerDay": float(_C["motel_per_person_per_day"]),
        "scenario": {"seed": -1, "mapStatus": "surrogate"},
    }

