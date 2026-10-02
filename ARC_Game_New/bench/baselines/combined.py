"""combined: choice_lookahead's task answers (long-term value, pricing the motel's recurring bill)
with build_potential's building and staffing."""
from __future__ import annotations

from cora.env.game import DECISIONS

from bench.baselines.build_potential import _POT_KITCHEN_TARGET, build_potential
from bench.baselines.common import (CHOICE_COST_WEIGHT, fill_shelters, impacts_dict, motel_rate,
                                    rounds_per_day, shelter_beds, workforce_per_building)
from bench.baselines.greedy import greedy


# ── Improved rules-based ("rules-based-v2") ──────────────────────────────────
# Addresses the four documented flaws of the baseline rules-based policy:
#   (1) multi-action turns: build several buildings AND hire several workers in one turn
#       (the env executes a list of actions; the baseline self-capped at 1 build + 1 hire);
#   (2) forward capital investment: size shelter capacity to the FULL known displaced
#       population P up front (ahead of the demand waves), not one shelter at a time;
#   (3) geographic site selection: rank available build sites by proximity to the
#       communities they serve (shorter, safer relocation routes), de-prioritizing
#       flood-blocked sites where identifiable — the baseline picked an arbitrary site;
#   (4) deploy reserves: invest the budget down to a small operating buffer instead of
#       hoarding a fixed reserve that never gets spent.
# Principled bound: STAFFING is the bottleneck (~4 workers/building), so building is paced by a
# staffable pipeline — never queue more buildings than the workforce can plausibly clear over the
# horizon. That bound is enforced by `budget_buildings` (max_buildings - existing) below.
# INCOME PACING (_V2_BUILD_PER_TURN): building is also capped per turn. This is NOT arbitrary
# throttling — funding arrives at ~$2-3k/round and a building+staffing costs ~$1k, so deploying
# faster than ~3/turn exhausts starting capital before the disaster peaks, leaving no cash to build
# shelters as population crests. Removing this cap halved lodging (0.85 -> 0.40) and cut reward 20%
# (n=10). The separate concurrent-unstaffed ("pipeline") cap was REMOVED as redundant — with
# per-turn pacing plus multi-worker hiring, each turn's new buildings staff within a round; n=10
# confirmed no regression. The operating buffer is a FLAT reserve held before discretionary
# building, sized to fund a reactive paid-fulfilment wave (food airlift ~$1-3k); a demand-SCALED
# buffer was tested and regressed lodging 0.85 -> 0.51 (it ballooned to ~$5.7k mean during food
# waves and starved shelter construction at the population peak), so the flat value is kept.
_V2_BUILD_PER_TURN = 3      # income-paced: max buildings deployed per turn (see note above)
_V2_OP_BUFFER = 3000        # flat cash reserve kept before discretionary building (see note above)


def _vec_dist(a, b):
    if not a or not b:
        return 0.0
    dx = (a.get("x", 0) or 0) - (b.get("x", 0) or 0)
    dy = (a.get("y", 0) or 0) - (b.get("y", 0) or 0)
    dz = (a.get("z", 0) or 0) - (b.get("z", 0) or 0)
    return (dx * dx + dy * dy + dz * dz) ** 0.5


def _lt_choice_value(c, demand, rounds_left, gs):
    """Greedy choice value with LONG-TERM cost built in.

    Identical to the myopic greedy value, EXCEPT the motel's recurring per-person daily bill is
    charged over the remaining days of the episode and added to the choice's effective cost.
    Over a long horizon this makes the (one-time, then-free) shelter dominate the motel, so
    relocation routing into shelters emerges from the value function itself — no separate
    shelter-routing override needed. Late in the episode (few days left) the motel's small
    remaining bill makes it acceptable again, exactly as it should be.

    Returns (value, fulfils_demand, is_shelter).
    """
    imp = impacts_dict(c)
    b = float(imp.get("Budget", 0) or 0)
    s = float(imp.get("Satisfaction", 0) or 0)
    if b > 0:                                    # funding choice
        return (b / 10000.0 + 0.01 * s, False, False)
    cost = -b                                    # immediate upfront cost ($)
    dest = c.get("destinationCategory")          # structured field from Unity; None for non-delivery
    is_motel = dest == "Motel"
    is_shelter = dest == "Shelter"
    # SAME acting predicate as greedy: a real (reliable) action costs money or grants real
    # satisfaction. Free "send to shelter/motel" and free "request from kitchens" choices are
    # non-acting (v contribution 0) — they often fail to complete, so we don't credit them.
    # Routing to shelters instead of the motel comes purely from the motel's recurring penalty
    # below (which sinks paid/free motel options), leaving the reliable shelter option on top.
    acting = (cost > 0) or (s >= 10)
    recurring = 0.0
    if is_motel:                                 # lifetime motel bill over the remaining days
        people = float(c.get("deliveryQuantity") or 20)
        days_left = max(1.0, rounds_left / rounds_per_day(gs))
        recurring = motel_rate(gs) * people * days_left
    v = (1.0 if (acting and demand) else 0.0) + 0.01 * s - CHOICE_COST_WEIGHT * (cost + recurring)
    return (v, acting and demand, is_shelter)


def choice_lookahead(env, rnd=0, rounds_total=DECISIONS):
    gs = env.game_state or {}
    va = env.valid_actions or []
    ms = gs.get("mapState", {}) or {}
    facs = ms.get("facilities", []) or []
    budget = float((gs.get("satisfactionAndBudget") or {}).get("budget", 0) or 0)
    rounds_left = max(0, rounds_total - rnd)

    # reactive core: keep greedy's free worker assignments, but REPLACE its myopic choices
    # with long-term-aware ones (the motel's recurring cost is priced in by _lt_choice_value,
    # so relocations route into free shelters automatically — when those shelters have space).
    base = greedy(env)
    actions = list(base["actions"])

    pop_by_fac = {f.get("facilityName"): (f.get("currentPopulation") or 0) for f in facs}
    free_shelter_space = sum(max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0))
                             for f in facs if f.get("buildingType") == "Shelter"
                             and f.get("buildingStatus") == "InUse")
    choices = []
    for t in (gs.get("allActiveTasks") or []):
        cs = t.get("choices") or []
        if not cs:
            continue
        demand = t.get("taskType") in ("Demand", "Emergency")
        people = float(pop_by_fac.get(t.get("affectedFacility")) or 20)
        best = None  # (choiceId, value, is_shelter, fulfils)
        for c in cs:
            v, fdem, is_shel = _lt_choice_value(c, demand, rounds_left, gs)
            # don't route into a shelter that lacks space for this relocation (it would
            # defer/fail and lose fulfilment) — push it below the motel fallback instead.
            if is_shel and free_shelter_space < people:
                v -= 100.0
            if best is None or v > best[1]:
                best = (c["choiceId"], v, is_shel, fdem)
        if best and (best[1] > 0 or best[3]):    # take if positive, or it fulfils a real demand
            choices.append({"taskId": t["taskId"], "choiceId": best[0]})
            if best[2] and free_shelter_space >= people:
                free_shelter_space -= people

    op_buffer = float(_V2_OP_BUFFER)   # flat reserve before discretionary building (see constant note)

    communities = [f for f in facs if f.get("buildingType") == "Community"]
    P = sum((f.get("currentPopulation") or 0) for f in communities) or 120
    shelter_cap = sum((f.get("populationCapacity") or 0) for f in facs if f.get("buildingType") == "Shelter")
    n_kitchens = sum(1 for f in facs if f.get("buildingType") == "Kitchen")
    n_casework = sum(1 for f in facs if f.get("buildingType") == "CaseworkSite")

    # (2) FORWARD INVESTMENT, staffing-aware. Food is a FULL reward point but is hard-gated by a
    # kitchen (no kitchen -> no food packs -> 0 food), so kitchens come BEFORE shelters. And we
    # never queue more capacity than we can plausibly STAFF over the horizon: hiring is capped at
    # 5/day and each building needs ~4 workers, so chasing all of P (12 shelters) just creates
    # unstaffable buildings. Cap total buildings to (current workers + future hires) / 4.
    wf = gs.get("workforceState", {}) or {}
    total_workers = (int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
                     + int(wf.get("workingTrainedWorkers", 0) or 0) + int(wf.get("workingUntrainedWorkers", 0) or 0))
    days_left = max(1, -(-rounds_left // rounds_per_day(gs)))         # ceil(rounds_left / rounds per day)
    max_workers = total_workers + 5 * days_left                       # 5 hires/day cap
    max_buildings = max_workers // workforce_per_building(gs)
    n_shelters = sum(1 for f in facs if f.get("buildingType") == "Shelter")
    existing_buildings = n_casework + n_kitchens + n_shelters
    budget_buildings = max(0, max_buildings - existing_buildings)     # how many MORE we can staff

    beds = shelter_beds(gs)
    shelters_needed = max(0, (max(0, P - shelter_cap) + beds - 1) // beds)
    want = []
    if n_casework < 1:
        want.append("CaseworkSite")
    if n_kitchens < _POT_KITCHEN_TARGET:                              # kitchens BEFORE shelters
        want += ["Kitchen"] * (_POT_KITCHEN_TARGET - n_kitchens)
    want += ["Shelter"] * shelters_needed
    want = want[:budget_buildings]                                    # cap to what we can staff

    # (3) GEOGRAPHY: rank available sites by distance to nearest community; avoid blocked routes.
    cstate = gs.get("constructionState", {}) or {}
    sites = [s for s in (cstate.get("availableSites") or []) if s.get("isAvailable")]
    blocked = set((ms.get("floodState", {}) or {}).get("blockedRoutes", []) or [])
    comm_pos = [c.get("position") for c in communities if c.get("position")]

    def _rank(s):
        p = s.get("position")
        d = min((_vec_dist(p, cp) for cp in comm_pos), default=0.0) if (p and comm_pos) else 0.0
        return d + (1e6 if s.get("siteName") in blocked else 0.0)

    ranked_ids = [s.get("siteId") for s in sorted(sites, key=_rank)]
    build_by = {}
    for i, a in enumerate(va):
        if a.get("action_type") == "construction":
            c = a.get("construction") or {}
            build_by.setdefault(c.get("building_type"), {})[c.get("site_id")] = (i, a.get("cost") or 0)

    # (1)+(4): build up to the income-paced per-turn limit, taking each building whose cost clears
    # the (demand-scaled) operating buffer. `want` is already truncated to the staffable headroom
    # (budget_buildings), and the per-turn cap keeps construction in step with funding inflow, so we
    # never front-load all capital into round 0. No separate concurrent-unstaffed ("pipeline") cap:
    # the per-turn pace plus multi-worker hiring (below) keep new buildings staffed within a round.
    unstaffed = sum(1 for f in facs if f.get("buildingStatus") in ("UnderConstruction", "NeedWorker"))
    used = set()
    if rounds_left >= 2:
        for btype in want:
            if len(used) >= _V2_BUILD_PER_TURN:    # income pacing — don't outrun funding inflow
                break
            avail = build_by.get(btype, {})
            sid = next((s for s in ranked_ids if s in avail and s not in used), None) \
                or next((s for s in avail if s not in used), None)
            if sid is None:
                continue
            idx, cost = avail[sid]
            if cost <= budget - op_buffer:
                actions.append(idx)
                budget -= cost
                used.add(sid)

    # (1): hire enough UNTRAINED workers to staff current NeedWorker buildings + the ones queued
    # this turn. The game has NO per-day hiring ceiling (cora.actions: hiring is budget-limited;
    # each hire action bundles up to 5), so we append AS MANY hire actions as needed to close the gap,
    # each bounded by the cash above the operating buffer. The env executes cached action indices in
    # order and re-checks budget live per action, so reusing the largest affordable bundle hires
    # repeatedly; we keep `budget` accurate as we go so no appended action trips the no-debt gate
    # (a server-side failure would abort every later action in the same step).
    wf = gs.get("workforceState", {}) or {}
    free_workers = int(wf.get("freeTrainedWorkers", 0) or 0) + int(wf.get("freeUntrainedWorkers", 0) or 0)
    need_now = sum(max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
                   for f in facs if f.get("buildingStatus") == "NeedWorker")
    gap = max(0, need_now + 4 * len(used) - free_workers)
    hire_gap0 = gap                                   # remember the original gap for the log line
    workers_hired = 0
    hire_actions = 0
    # untrained-hire actions offered this turn, keyed by bundle quantity -> (index, cost)
    hire_by_q = {}
    for i, a in enumerate(va):
        wk = a.get("worker") or {}
        if a.get("action_type") == "worker" and wk.get("worker_action_type") == "hire_untrained":
            q = int(wk.get("quantity") or 0)
            if q > 0:
                hire_by_q[q] = (i, int(a.get("cost") or 0))
    if gap > 0 and hire_by_q:
        qs = sorted(hire_by_q)
        unit = hire_by_q[qs[0]][1] / qs[0]            # $ per worker (constant across bundles)
        max_bundle = qs[-1]
        guard = 0
        while gap > 0 and unit > 0 and guard < 64:
            guard += 1
            afford_q = int((budget - op_buffer) // unit)   # bundle the remaining cash can fund
            q = min(gap, max_bundle, afford_q)
            if q <= 0:
                break
            if q not in hire_by_q:                    # fall back to the largest enumerated bundle <= q
                q = max([x for x in qs if x <= q], default=0)
                if q <= 0:
                    break
            idx, cost = hire_by_q[q]
            actions.append(idx)
            budget -= cost
            gap -= q
            workers_hired += q
            hire_actions += 1

    fill_shelters(env, actions)
    return {"choices": choices, "actions": actions, "note": "rules-based-v2",
            "reasoning": (f"v2: P={P} shelterCap={shelter_cap} wantBuilds={len(want)} "
                          f"built={len(used)} hireGap={hire_gap0} hired={workers_hired}/{hire_actions}act "
                          f"unstaffed={unstaffed} opBuf={int(op_buffer)} rl={rounds_left}")}


def combined(env, rnd=0, rounds_total=DECISIONS):
    """Both hand-written strategies at once.

    The two rules-based policies improve OPPOSITE halves of a turn and neither touches the
    other's half, so they compose without conflict:

      * potential_decision      — keeps greedy's choices, ADDS building (shelters/kitchens
                                  toward a demand target). Improves the ACTION side.
      * improved_rules_based_.. — keeps greedy's worker assignments, REPLACES the choices with
                                  long-term-value ones that price in the motel's recurring
                                  $200/person/day. Improves the CHOICE side.

    So: take the choices from the long-term-value policy and the actions from the building
    policy. Action lists are indices into the same env.valid_actions, so the merge is a
    de-duplicated union that preserves each policy's ordering.

    Both sub-policies already append shelter-filling transfers, so the union inherits those
    too; dedup keeps a transfer from being issued twice.
    """
    lt = choice_lookahead(env, rnd, rounds_total)
    pot = build_potential(env, rnd, rounds_total)

    seen, actions = set(), []
    for i in list(pot.get("actions") or []) + list(lt.get("actions") or []):
        if i not in seen:
            seen.add(i); actions.append(i)

    return {"choices": lt.get("choices") or [], "actions": actions, "note": "combined",
            "reasoning": "lt-value choices + potential building + shelter transfers"}
