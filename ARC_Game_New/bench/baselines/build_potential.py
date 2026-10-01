"""build-potential: greedy's task answers plus building toward anticipated demand (shelters,
kitchens, one casework site), hiring to staff them, and filling built shelters."""
from __future__ import annotations

import os

from bench.baselines.common import fill_shelters, impacts_dict
from bench.baselines.greedy import greedy


# ── Potential-shaping baseline (greedy selection + a hand-crafted state potential) ──
# Builds shelter/kitchen capacity toward anticipated demand (anchored to community
# population, capped by the empirical arrival rate, horizon-discounted), staffs them
# to claim worker_use, and fulfills via the cheapest *effective* option.
_POT_MODE = os.environ.get("POT_MODE", "baseline")     # "baseline" | "demandsupply"
_POT_DS_COVERAGE = float(os.environ.get("POT_DS_COVERAGE", "1.0"))  # shelter-cap target as fraction of P
_POT_MIN_HORIZON = int(os.environ.get("POT_MIN_HORIZON", "4"))   # don't build with fewer rounds left
_POT_KITCHEN_TARGET = int(os.environ.get("POT_KITCHEN_TARGET", "2"))  # operational kitchens to aim for
_POT_CASEWORK_BUILD_ROUND = 0  # build the casework site EARLY. The workforce is capped (~3-4 operational
                               # buildings), and a building only gets staffed if free workers exist when it's
                               # built — deferring the casework build to ~round 8 left it permanently
                               # NeedWorker (workers already committed to shelters) → 0 processed. Building it
                               # first claims its 4 workers up front, which is the only way it stays operational.
                               # The cost (~one shelter's staffing → lower lodging) is inherent to the worker cap.
_POT_BUDGET_RESERVE = float(os.environ.get("POT_BUDGET_RESERVE", "1500"))  # reserve before discretionary building
_POT_SHELTER_COVERAGE = 1e9  # θ: route lodging to free shelter only when space >= θ×need.
                             # Set huge = OFF: deferred shelter relocations are unreliable
                             # (travel/expiry) and cost lodging fulfillment vs the reliable
                             # immediate option, so free-shelter routing is disabled by
                             # default. Lower (e.g. 1.0) to re-enable the cost-vs-fulfillment trade.


def build_potential(env, rnd=0, rounds_total=32):
    gs = env.game_state or {}
    va = env.valid_actions or []
    facs = gs.get("mapState", {}).get("facilities", []) or []
    budget = float((gs.get("satisfactionAndBudget") or {}).get("budget", 0) or 0)
    rounds_left = max(0, rounds_total - rnd)

    # Reuse greedy's RELIABLE choices + worker assignments (it prefers the acting/
    # immediate options that actually fulfill). Potential adds *building* on top — the
    # free/deferred options fail until infrastructure is stocked, so don't switch to
    # them; keep reliable fulfillment and let building pay off via worker_use + capacity.
    base = greedy(env)
    choices = base["choices"]
    actions = list(base["actions"])  # already includes worker assignments

    # demand anchor: community population (known from round 0); target free shelter
    # capacity ~ P so we can eventually relocate for free instead of paying motel.
    P = sum((f.get("currentPopulation") or 0) for f in facs if f.get("buildingType") == "Community") or 120
    shelter_cap = sum((f.get("populationCapacity") or 0) for f in facs if f.get("buildingType") == "Shelter")
    n_kitchens = sum(1 for f in facs if f.get("buildingType") == "Kitchen")
    n_casework = sum(1 for f in facs if f.get("buildingType") == "CaseworkSite")
    tasks_by_id = {t["taskId"]: t for t in (gs.get("allActiveTasks") or [])}

    def is_lodging(t):
        return t and any(k in (t.get("taskTitle") or "") for k in ("Relocation", "Population", "Lodging"))

    def find_build(btype):
        cands = [(i, a) for i, a in enumerate(va) if a.get("action_type") == "construction"
                 and (a.get("construction") or {}).get("building_type") == btype]
        return min(cands, key=lambda x: x[1].get("cost") or 0) if cands else None

    # ════════════════════════════════════════════════════════════════════════════
    # DEMAND-SUPPLY MODE (POT_MODE=demandsupply): grounded in the Unity audit —
    #   Motel = $0 upfront but $200/person/DAY recurring (MotelCostManager).
    #   Shelter = $1000 + 4 workers, then $0/day forever (10 beds).
    # So the cost-optimal housing is a STAFFED shelter, and the cheapest *reliable*
    # way to fill it is the $3000 immediate Helicopter-to-Shelters (~$75/person once
    # for a 40-person community) — vs the "free" motel that bills $200/person/day.
    # Policy: build+staff shelter capacity toward demand (≈ community pop P), then
    # route each relocation into shelter space via the reliable immediate helicopter
    # while shelter beds last; spill to the motel only when shelters are full.
    # ════════════════════════════════════════════════════════════════════════════
    if _POT_MODE == "demandsupply":
        shel_free = sum(max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0))
                        for f in facs if f.get("buildingType") == "Shelter"
                        and f.get("buildingStatus") == "InUse")
        pop_by_fac = {f.get("facilityName"): (f.get("currentPopulation") or 0) for f in facs}

        def _pick(cs, *kws, paid=None):
            for c in cs:
                txt = (c.get("choiceText") or "").lower()
                if all(k in txt for k in kws):
                    has_cost = bool(impacts_dict(c).get("Budget"))
                    if paid is None or has_cost == paid:
                        return c
            return None

        for ch in choices:
            t = tasks_by_id.get(ch["taskId"])
            if not is_lodging(t):
                continue
            cs = t.get("choices") or []
            need = pop_by_fac.get(t.get("affectedFacility")) or 40
            if shel_free >= need:
                # reliable immediate evac INTO a staffed shelter ($0/day thereafter)
                pick = (_pick(cs, "helicopter", "shelter", paid=True)
                        or _pick(cs, "evacuation", "shelter")
                        or _pick(cs, "shelter", paid=False))
                if pick:
                    ch["choiceId"] = pick["choiceId"]
                    shel_free -= need
            # else: shelters full → leave greedy's choice (motel/helicopter spill)

        # build+staff shelters toward demand coverage, then kitchens for food + workers
        if rounds_left >= _POT_MIN_HORIZON and budget >= _POT_BUDGET_RESERVE:
            target = None
            if shelter_cap < _POT_DS_COVERAGE * P:
                target = find_build("Shelter")
            if target is None and n_kitchens < _POT_KITCHEN_TARGET:
                target = find_build("Kitchen")
            if target and (target[1].get("cost") or 0) <= budget - _POT_BUDGET_RESERVE:
                actions.append(target[0])
                budget -= (target[1].get("cost") or 0)

        wf = gs.get("workforceState", {}) or {}
        free_workers = int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
        need_w = sum(max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
                     for f in facs if f.get("buildingStatus") == "NeedWorker")
        if need_w > free_workers and budget >= _POT_BUDGET_RESERVE:
            for i, a in enumerate(va):
                if (a.get("action_type") == "worker"
                        and (a.get("worker") or {}).get("worker_action_type") == "hire_untrained"
                        and (a.get("cost") or 0) <= budget - _POT_BUDGET_RESERVE):
                    actions.append(i)
                    break
        fill_shelters(env, actions)
        return {"choices": choices, "actions": actions, "note": "potential-ds",
                "reasoning": f"demandsupply: P={P} shelterCap={shelter_cap} shelFree={shel_free} kitchens={n_kitchens}"}

    # ── NO motel-routing override. We tried forcing the $3000 immediate Helicopter-to-Motel
    # for every lodging task; it REGRESSED reward (1.44 -> 1.35) and pinned cost_lodging at the
    # cap (1.0 = $5000+ spent per person housed). cost_lodging is NOT structurally capped — it
    # caps only when you overspend per fulfilled relocation. Greedy's myopic choice already
    # PREFERS the free "Send to Shelters/Motel" (cost 0 → scores higher than the −$0.6 helicopter),
    # which houses people at $0 when it completes, keeping cost_lodging ~0.78 (uncapped). The real
    # lodging bottleneck is FULFILLMENT reliability (lodgingFulfilled ~6 of ~14 resolved), which
    # caps sat_lodging AND inflates $/person together — not the cost term itself. ──

    # ── free-shelter-when-ready (demand-aware): for lodging tasks, switch from greedy's
    # reliable paid choice to the free "Send to Shelters" option ONLY when free shelter
    # space fully covers that task's relocation need — so the deferred relocation
    # completes (no partial-fulfillment loss). Need = the affected community's population
    # (no fixed guess). θ=_POT_SHELTER_COVERAGE dials aggressive(<1) ↔ conservative(=1). ──
    free_shelter_space = sum(max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0))
                             for f in facs if f.get("buildingType") == "Shelter")
    if free_shelter_space > 0:
        pop_by_facility = {f.get("facilityName"): (f.get("currentPopulation") or 0) for f in facs}
        for ch in choices:
            t = tasks_by_id.get(ch["taskId"])
            if not is_lodging(t):
                continue
            need = pop_by_facility.get(t.get("affectedFacility")) or 40  # 40 = a community
            if free_shelter_space >= _POT_SHELTER_COVERAGE * need:
                free_shelter = next((c for c in (t.get("choices") or [])
                                     if c.get("destinationCategory") == "Shelter"
                                     and not impacts_dict(c).get("Budget")), None)
                if free_shelter:
                    ch["choiceId"] = free_shelter["choiceId"]   # override to free option
                    free_shelter_space -= need

    # ── build (the potential term): only with enough horizon + budget headroom ──
    def find_build(btype):
        cands = [(i, a) for i, a in enumerate(va) if a.get("action_type") == "construction"
                 and (a.get("construction") or {}).get("building_type") == btype]
        return min(cands, key=lambda x: x[1].get("cost") or 0) if cands else None
    # Build order: shelters toward demand (free relocation destinations) + kitchens (food+workers)
    # EARLY, then ONE casework site once round >= _POT_CASEWORK_BUILD_ROUND — deferring it keeps the
    # capped workforce on shelters during the early relocation waves, then staffs casework just before
    # the first return-home requests (~round 13). Once it's up, greedy's choice logic routes
    # "Casework Request" tasks to it (no smarter policy needed).
    if rounds_left >= _POT_MIN_HORIZON and budget >= _POT_BUDGET_RESERVE:
        target = None
        if n_casework < 1 and rnd >= _POT_CASEWORK_BUILD_ROUND:
            target = find_build("CaseworkSite")
        if target is None and shelter_cap < P:
            target = find_build("Shelter")
        if target is None and n_kitchens < _POT_KITCHEN_TARGET:
            target = find_build("Kitchen")
        if target and (target[1].get("cost") or 0) <= budget - _POT_BUDGET_RESERVE:
            actions.append(target[0])
            budget -= (target[1].get("cost") or 0)

    # (worker assignments are already in `base` from greedy_decision — don't redo them)

    # ── hire (untrained) if buildings need more workers than we have free ──
    wf = gs.get("workforceState", {}) or {}
    free_workers = int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
    need = sum(max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
               for f in facs if f.get("buildingStatus") == "NeedWorker")
    if need > free_workers and budget >= _POT_BUDGET_RESERVE:
        for i, a in enumerate(va):
            if (a.get("action_type") == "worker"
                    and (a.get("worker") or {}).get("worker_action_type") == "hire_untrained"
                    and (a.get("cost") or 0) <= budget - _POT_BUDGET_RESERVE):
                actions.append(i)
                break

    fill_shelters(env, actions)
    return {"choices": choices, "actions": actions, "note": "rules-based",
            "reasoning": f"rules-based: P={P} shelterCap={shelter_cap} kitchens={n_kitchens} casework={n_casework} roundsLeft={rounds_left}"}
