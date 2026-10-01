"""pareto: the strategy the surrogate's Pareto frontier converges on, played in the game."""
from __future__ import annotations

from bench.baselines.common import fill_shelters, impacts_dict
from bench.baselines.greedy import greedy
from cora import policy_family


# ── pareto policy ───────────────────────────────────────────────────────────────────────────
# The strategy the calibrated surrogate's Pareto frontier converges on. Every one of the nine
# non-dominated policies shares these invariants (the rest -- kitchen count, casework count, food
# rule -- only trade score against banked budget):
#     6 shelters · build from round 0 · one building per round · relocations to SHELTERS
#     · answer every casework request
# Build ORDER is casework -> shelters -> kitchens, because casework has the longest chain to payoff
# (build ~4 rounds, then staff, and requests only mature once residents have been housed a while),
# while paid food covers the gap cheaply until the kitchens come online.
# Sizing rationale, all derived rather than assumed:
#   shelters  ~ relocated population / beds-per-shelter. Swept 4..12 on the surrogate: score peaks
#               at 5-6 (3.876/3.877) and falls away after -- extra shelters eat the sites and cash
#               that casework and kitchens need. 6 is a real optimum, not the sweep's cap.
#   kitchens  ~ fleet haul capacity: a kitchen refills ~1 vehicle-load per round, so more kitchens
#               than the fleet can drain just stall on a full store.
#   casework  ~ sized to the request backlog; its cost term is ~0.001, so it is nearly free score.

def pareto(env, rnd=0, rounds_total=32, cfg=None):
    """A member of the shared policy family (cora.policy_family; default: the frontier plan)."""
    cfg = cfg or policy_family.from_env()
    food_rule, reloc_rule = policy_family.rules(cfg, rnd)
    gs = env.game_state or {}
    va = env.valid_actions or []
    facs = gs.get("mapState", {}).get("facilities", []) or []
    have = {k: sum(1 for f in facs if f.get("buildingType") == k)
            for k in ("Shelter", "Kitchen", "CaseworkSite")}
    want = [("CaseworkSite", cfg["n_casework"]), ("Shelter", cfg["n_shelter"]),
            ("Kitchen", cfg["n_kitchen"])]

    base = greedy(env)
    choices, actions = base["choices"], list(base["actions"])
    tasks_by_id = {t["taskId"]: t for t in (gs.get("allActiveTasks") or [])}

    def _txt(c):  return (c.get("choiceText") or "").lower()
    def _cost(c): return abs(float(impacts_dict(c).get("Budget", 0) or 0))
    free_veh = int((gs.get("logistics") or {}).get("availableVehicles") or 0)
    shelter_beds_free = sum(max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0))
                            for f in facs if f.get("buildingType") == "Shelter"
                            and f.get("buildingStatus") == "InUse")
    kitchen_stock = sum(((f.get("resources") or {}).get("foodPacks") or 0)
                        for f in facs if f.get("buildingType") == "Kitchen"
                        and f.get("buildingStatus") == "InUse")

    for ch in choices:
        t = tasks_by_id.get(ch["taskId"])
        if not t:
            continue
        cs, title = (t.get("choices") or []), (t.get("taskTitle") or "")
        if "Relocation" in title or "Population" in title:
            # Route to a shelter only when operational shelters can take the whole group: Unity
            # refuses "Send to Shelters" without beds (the first rounds of this front-loaded build
            # have none; unguarded, sat_lodging fell to 0.803 against build-potential's 0.998).
            shelter = next((c for c in cs if "shelter" in _txt(c) and _cost(c) == 0), None)
            group = int((shelter or {}).get("deliveryQuantity") or 0)
            opt = (shelter if shelter is not None
                   and policy_family.route_to_shelter(cfg, rnd, shelter_beds_free, group) else None)
            if opt is None:
                opt = next((c for c in cs if "motel" in _txt(c) and _cost(c) == 0), None)
            if opt is not None:
                ch["choiceId"] = opt["choiceId"]
                if opt is shelter:
                    shelter_beds_free -= group
        elif "Food Request" in title:
            # Haul from a kitchen only with a free vehicle and the meals in stock; otherwise buy
            # the immediate option (ordering from kitchens still under construction cost
            # sat_food 0.226).
            hauled = sorted([c for c in cs if _cost(c) == 0],
                            key=lambda c: int(c.get("deliveryQuantity") or 1))
            instant = next((c for c in cs if _cost(c) > 0), None)
            load = int(hauled[0].get("deliveryQuantity") or 0) if hauled else 0
            can_haul = bool(hauled) and policy_family.haul_from_kitchen(cfg, rnd, free_veh, kitchen_stock, load)
            pick = hauled[0] if can_haul else (instant or (hauled[0] if hauled else None))
            if pick is not None:
                ch["choiceId"] = pick["choiceId"]
                if can_haul and pick is hauled[0]:
                    free_veh -= 1
                    kitchen_stock -= load
        elif "Casework" in title and cfg["answer_cw"]:
            # casework_processing_sat is the largest untapped term and casework_efficiency ~0.001,
            # so always take an offered casework action.
            opt = next((c for c in cs if _cost(c) == 0), None) or (cs[0] if cs else None)
            if opt is not None:
                ch["choiceId"] = opt["choiceId"]

    # ONE building per round, in priority order, until each target is met -- but only on the
    # rounds the schedule names, so `start` and `spacing` mean the same thing in both engines.
    sched = policy_family.build_schedule(cfg)
    for btype, target in (want if rnd in sched else []):
        if have.get(btype, 0) >= target:
            continue
        cand = [(i, a) for i, a in enumerate(va)
                if a.get("action_type") == "construction"
                and (a.get("construction") or {}).get("building_type") == btype]
        if cand:
            actions.append(min(cand, key=lambda x: x[1].get("cost") or 0)[0])
        break                                   # at most one build per round

    # hire enough untrained bodies to staff what is standing or rising
    wf = gs.get("workforceState", {}) or {}
    free_w = int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
    need_w = sum(max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
                 for f in facs if f.get("buildingStatus") in ("NeedWorker", "UnderConstruction"))
    hires = 0
    while need_w > free_w + hires and hires < 8:
        idx = next((i for i, a in enumerate(va)
                    if a.get("action_type") == "worker"
                    and (a.get("worker") or {}).get("worker_action_type") == "hire_untrained"
                    and i not in actions), None)
        if idx is None:
            break
        actions.append(idx); hires += 1
    fill_shelters(env, actions)
    return {"choices": choices, "actions": actions, "note": "pareto",
            "reasoning": f"r{rnd} have={have} want={[(b, t) for b, t in want]} "
                         f"food={food_rule} reloc={reloc_rule} veh={free_veh}"}
