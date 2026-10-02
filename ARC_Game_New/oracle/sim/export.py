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
import os
import re

from . import corpus_paths, paths as P
import oracle.sim.sim as S
from .economy import C as _C, REQUIRED_WORKFORCE

# ── the TaskData assets: text templates and the choice fields the RPC export lacks ──────

_ASSETS = None


def _assets() -> dict:
    """{taskId: {"description", "choices": {choiceId: {"choiceText", "destinationType", ...}}}}"""
    global _ASSETS
    if _ASSETS is None:
        import yaml
        out = {}
        for path in glob.glob(os.path.join(P.ROOT, "Assets/Scripts/Tasks/TaskData/*.asset")):
            txt = open(path, encoding="utf-8").read()
            d = yaml.safe_load(txt[txt.index("MonoBehaviour:"):])["MonoBehaviour"]
            out[d.get("taskId")] = {
                "title": d.get("taskTitle") or "", "description": d.get("description") or "",
                "choices": {c.get("choiceId"): c for c in d.get("agentChoices") or []}}
        _ASSETS = out
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


# ── facilities, sites, workforce, logistics ────────────────────────────────────────────

def _built(w):
    """Constructed buildings in FindObjectsOfType order (newest first), then the prebuilts."""
    e = w.economy
    built = [b for b in e.buildings if b["status"] != "Prebuilt" and not b.get("destroyed")]
    pre = [b for b in e.buildings if b["status"] == "Prebuilt"]
    return built[::-1], pre


def _incoming(w, name) -> int:
    return S._walking_to(w, name) + w.tasks.inbound_to(name)


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
        if f.damaged[i]:
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
               - w.tasks.inbound_to(b["name"]) - S._walking_to(w, b["name"]))


def _has_destination_space(w, source, to_shelter, to_motel) -> bool:
    """ClientRelocationHandler.HasDestinationSpace: the source still has people and some
    operational shelter / the motel has effective space."""
    src = w.economy.facility(source)
    if src is None or ((src.get("resources") or {}).get("population") or 0) <= 0:
        return False
    for b in w.economy.buildings:
        if b["name"] == source or b.get("destroyed"):
            continue
        if (to_shelter and b["type"] == "Shelter" and b["status"] == "InUse") or (to_motel and b["type"] == "Motel"):
            if _space(w, b) > 0:
                return True
    return False


def _food_storage(w, facility):
    """TaskSystem.PopulationFoodStorage: the facility's storage if it eats, else None."""
    b = w.economy.facility(facility)
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
    store = _food_storage(w, facility) if (has_pop_food and facility) else None
    live_food = int(store.get("outstanding_need") or 0) if store is not None else None
    food_amount = live_food if live_food is not None else next(
        (int(c.get("deliveryQuantity") or 0) for c in spec.get("choices") or [] if c.get("deliveryCargoType") == 1), 0)
    pop_amount = next((int(c.get("deliveryQuantity") or 0) for c in spec.get("choices") or []
                       if c.get("deliveryCargoType") == 0), 0)

    if def_id == S.CASEWORK_SPEC_ID:
        # GenerateCaseworkTask builds ONE choice (send to casework); the port's spec also holds a
        # non-delivering choice 2 that Unity never offers. populationAmount is the group's live
        # casework need (RefreshTaskAgainstLiveState).
        g = w.clients.group(spec.get("_gid"))
        pop_amount = g.with_need if g is not None else pop_amount
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
                and not _has_destination_space(w, facility, cat != "Motel", cat == "Motel")):
            continue                                     # B5/D5: no room anywhere for these people
        a_choice = (asset.get("choices") or {}).get(c.get("choiceId")) or {}
        text = c.get("choiceText") or a_choice.get("choiceText") or ""
        if def_id == S.CASEWORK_SPEC_ID:
            text = "Send [population_amount] clients to a casework site"
        qty = 0
        if delivers:
            qty = int(c.get("deliveryQuantity") or 0)
            if c.get("quantityType") == "PopulationBased":
                b = w.economy.facility(facility)
                need = int((b or {}).get("outstanding_need") or 0)
                pct = float(c.get("deliveryPercentage") or 0) or 100.0
                qty = int(round(need * pct / 100.0))
        choices.append({
            "choiceId": c.get("choiceId"),
            "choiceText": _resolve(text, plain, food_amount, pop_amount),
            "impacts": _priced(c, qty if delivers else None),
            "destinationCategory": cat if delivers else "",
            "deliveryQuantity": qty,
            "immediateDelivery": bool(c.get("immediateDelivery")) if delivers else False,
            "triggersDelivery": bool(c.get("triggersDelivery")) if delivers else False,
            "feasible": True, "unavailableReason": ""})
    return {"taskId": tid, "stableTaskId": def_id if def_id in S._TASK_SPEC and def_id not in S.CODE_BUILT_TASKS.values() else "",
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
        "scenario": {"seed": getattr(w, "seed", -1), "mapStatus": "surrogate"},
    }


_ = (re, corpus_paths)
