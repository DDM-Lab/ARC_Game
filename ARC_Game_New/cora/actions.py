"""The action menu: every game action available in a game state, in the shape Unity executes.

enumerate_actions(game_state) lists them in a fixed order (construction, worker, resource
transfer, worker assignment, deconstruction). The list is the round's action space: the gym's
step() takes indices into it, baselines pick from it, and cora.executor resolves typed tool calls
to indices into it. Each entry is

    {"action_id", "action_type", "description", "cost", "requirements", "effects",
     <action_type payload>}

where the payload key is construction | worker | transfer | assignment | deconstruction and is
the part Unity's ActionExecutor reads.

Spending into deficit is allowed: no action is withheld for lack of budget (the score's
cost-efficiency term penalizes it instead). Prices are the ones the game state reports this
round; the constants below are only fallbacks.
"""
from __future__ import annotations

BUILDING_TYPES = ("Kitchen", "Shelter", "CaseworkSite")
BUILDING_REQUIRED_WORKFORCE = 4

# Fallback prices; the live ones come from workforceState / constructionState each round.
UNTRAINED_WORKER_COST = 200
TRAINED_WORKER_COST = 1000
TRAINING_COST_PER_WORKER = 300
BUILDING_CONSTRUCTION_COST = 2000

MAX_HIRE_BUNDLE = 5            # hire offers per kind: 1..5 workers (a turn may repeat a bundle)
MAX_TRAIN_BUNDLE = 10
FOOD_TRANSFER_QUANTITIES = (10, 25, 50, 100)
PEOPLE_TRANSFER_QUANTITIES = (5, 10, 20)


def staffing_need(facility: dict) -> int:
    """Workforce units a built facility still needs (0 if it is not built or fully staffed)."""
    if facility.get("buildingStatus") not in ("NeedWorker", "InUse"):
        return 0
    required = facility.get("requiredWorkforce", BUILDING_REQUIRED_WORKFORCE) or 0
    return max(0, required - (facility.get("assignedWorkforce", 0) or 0))


def _action(action_id, action_type, description, cost, requirements, effects, payload_key, payload):
    return {"action_id": action_id, "action_type": action_type, "description": description,
            "cost": cost, "requirements": requirements, "effects": effects, payload_key: payload}


def assignment_action(building_name: str, quantity: int, worker_type: str | None = None,
                      need: int | None = None) -> dict:
    """A worker_assignment action. Without worker_type, Unity assigns trained workers first
    (how cora.executor staffs a building fully in one action)."""
    if worker_type:
        return _action(f"assign_{worker_type}_{building_name}_{quantity}", "worker_assignment",
                       f"Assign {quantity} {worker_type} worker(s) to {building_name}", 0,
                       {f"free_{worker_type}_workers": quantity, "building_needs_workers": need},
                       {f"free_{worker_type}_workers": -quantity, f"{building_name}_workforce": quantity},
                       "assignment", {"building_name": building_name, "worker_type": worker_type,
                                      "quantity": quantity})
    return {"action_id": f"assign_{building_name}_{quantity}", "action_type": "worker_assignment",
            "description": f"Assign {quantity} worker(s) ({need} workforce) to {building_name}",
            "cost": 0, "assignment": {"building_name": building_name, "quantity": quantity}}


def _construction(gs):
    cost = int((gs.get("constructionState") or {}).get("buildingConstructionCost") or BUILDING_CONSTRUCTION_COST)
    for site in (gs.get("constructionState") or {}).get("availableSites", []):
        if not site.get("isAvailable", False):
            continue
        site_id, site_name = site.get("siteId", -1), site.get("siteName", "Unknown Site")
        for btype in BUILDING_TYPES:
            yield _action(f"build_{btype}_{site_id}", "construction", f"Build {btype} at {site_name}", cost,
                          {"budget": cost, "available_site": True},
                          {"budget": -cost, "new_building": btype, "site_occupied": site_id},
                          "construction", {"building_type": btype, "site_id": site_id, "site_name": site_name})


def _worker(gs):
    ws = gs.get("workforceState") or {}
    offers = (("hire_untrained", "untrained_workers", int(ws.get("untrainedWorkerCost") or UNTRAINED_WORKER_COST),
               MAX_HIRE_BUNDLE),
              ("hire_trained", "trained_workers", int(ws.get("trainedWorkerCost") or TRAINED_WORKER_COST),
               MAX_HIRE_BUNDLE))
    for kind, effect, price, max_q in offers:
        label = kind.split("_")[1]
        for q in range(1, max_q + 1):
            yield _action(f"{kind}_{q}", "worker", f"Hire {q} {label} worker(s) (${price} each)", price * q,
                          {"budget": price * q}, {"budget": -price * q, effect: q},
                          "worker", {"worker_action_type": kind, "quantity": q})
    price = int(ws.get("trainingCostPerWorker") or TRAINING_COST_PER_WORKER)
    for q in range(1, min(ws.get("freeUntrainedWorkers", 0), MAX_TRAIN_BUNDLE) + 1):
        yield _action(f"train_workers_{q}", "worker",
                      f"Train {q} untrained worker(s) (${price} each, 3 days)", price * q,
                      {"budget": price * q, "untrained_workers": q},
                      {"budget": -price * q, "untrained_workers": -q, "workers_in_training": q},
                      "worker", {"worker_action_type": "train_untrained", "quantity": q})


def _transfers(gs):
    if (gs.get("logistics") or {}).get("availableVehicles", 0) == 0:
        return
    facilities = (gs.get("mapState") or {}).get("facilities", [])
    kinds = (("FoodPacks", "food", "foodPacks", "foodPacksCapacity", FOOD_TRANSFER_QUANTITIES, "food packs"),
             ("Population", "population", "population", "populationCapacity", PEOPLE_TRANSFER_QUANTITIES,
              "people"))
    for src in facilities:
        sname, sres = src.get("facilityName", ""), src.get("resources", {})
        for dst in facilities:
            dname, dres = dst.get("facilityName", ""), dst.get("resources", {})
            if sname == dname:
                continue
            for rtype, tag, key, cap_key, quantities, noun in kinds:
                have, room = sres.get(key, 0), dres.get(cap_key, 0) - dres.get(key, 0)
                if have <= 0:
                    continue
                for q in quantities:
                    if q <= have and q <= room:
                        yield _action(f"transfer_{tag}_{sname}_{dname}_{q}", "resource_transfer",
                                      f"Transfer {q} {noun} from {sname} to {dname}", 0,
                                      {f"source_{tag}": q, "destination_capacity": q, "available_vehicle": True},
                                      {f"{sname}_{tag}": -q, f"{dname}_{tag}": q, "vehicle_in_use": True},
                                      "transfer", {"resource_type": rtype, "quantity": q,
                                                   "source_facility": sname, "destination_facility": dname})


def _assignments(gs):
    ws = gs.get("workforceState") or {}
    free = {"trained": ws.get("freeTrainedWorkers", 0), "untrained": ws.get("freeUntrainedWorkers", 0)}
    for f in (gs.get("mapState") or {}).get("facilities", []):
        need = staffing_need(f)
        if need <= 0:
            continue
        for wtype in ("trained", "untrained"):
            for q in range(1, min(free[wtype], need) + 1):
                yield assignment_action(f.get("facilityName", ""), q, wtype, need)


def _deconstructions(gs):
    for f in (gs.get("mapState") or {}).get("facilities", []):
        if f.get("facilityType", "") != "Building":         # prebuilt facilities cannot be torn down
            continue
        name, site, workers = f.get("facilityName", ""), f.get("originalSiteId", -1), f.get("assignedWorkforce", 0)
        yield _action(f"deconstruct_{name}", "deconstruction", f"Deconstruct {name} (frees {workers} workers and site)",
                      0, {"is_player_building": True},
                      {"building_removed": name, "site_freed": site, "workers_freed": workers},
                      "deconstruction", {"building_name": name, "frees_site_id": site})


def enumerate_actions(game_state: dict) -> list[dict]:
    """Every action available in `game_state`, in the game's fixed menu order."""
    gs = game_state or {}
    return [*_construction(gs), *_worker(gs), *_transfers(gs), *_assignments(gs), *_deconstructions(gs)]


_TOOL_BUILDING = {"Kitchen": "kitchen", "Shelter": "shelter", "CaseworkSite": "casework"}
_TOOL_RESOURCE = {"FoodPacks": "food", "Population": "people"}


def as_tool_call(action: dict) -> tuple:
    """A menu action as the equivalent typed tool call (name, args) of cora.tools, so a policy that
    picks from the menu acts through cora.executor like a model does. A worker_assignment
    becomes staff(site): the executor staffs a building fully, the only staffing the game runs."""
    t = action["action_type"]
    if t == "construction":
        c = action["construction"]
        return "build", {"type": _TOOL_BUILDING[c["building_type"]], "site_id": int(c["site_id"])}
    if t == "worker":
        w = action["worker"]
        if w["worker_action_type"] == "train_untrained":
            return "train", {"count": w["quantity"]}
        return "hire", {"kind": w["worker_action_type"].split("_", 1)[1], "count": w["quantity"]}
    if t == "worker_assignment":
        return "staff", {"site": action["assignment"]["building_name"]}
    if t == "deconstruction":
        return "deconstruct", {"site": action["deconstruction"]["building_name"]}
    if t == "resource_transfer":
        tr = action["transfer"]
        return "transfer", {"resource": _TOOL_RESOURCE[tr["resource_type"]], "source": tr["source_facility"],
                            "dest": tr["destination_facility"], "qty": tr["quantity"]}
    raise ValueError(f"no tool for action type {t!r}")
