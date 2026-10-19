"""rules: a rules-based policy for study build 1.0.2, acting through the same typed tool calls
a model uses (task / build / staff / hire), so it meets exactly the executor and refusals a
model does.

Each turn:
  * every funding offer is taken (budget never enters the score; 1.0.2 does not cap Storm
    Funding, so money is effectively unlimited -- the study build as released);
  * every food request is answered: kitchens first, the Rapid Response Vehicle ($72/meal)
    when no kitchen can serve it and `rrv` allows;
  * every relocation / evacuation goes to a shelter when one is offered (offered only when a
    shelter has room), else the Motel when `motel` allows;
  * every casework request is sent once a casework site is fully staffed;
  * buildings are added toward targets -- `kitchens`, `casework_sites`, and shelters sized to
    the people the Motel holds (`shelter_per_motel`) up to `max_shelters` -- on the free sites
    nearest the communities;
  * every building waiting for workers is staffed, and untrained workers are hired to cover
    exactly what that staffing and this turn's builds need (idle workers cost S_WorkerUse).

The strategy knobs are PARAMS; bench.baselines.tune searches them on held-out seeds.
"""
from __future__ import annotations

from cora.env.game import DECISIONS
from cora.observation import task_tokens

# Tuned by bench.baselines.tune on surrogate seeds 9000-9015 (held-out 9016-9031: 1.543 +- 0.007,
# 2026-10-14). The untuned starting point (Motel allowed, 4 kitchens, 1 casework site) held 1.321.
PARAMS = {
    "kitchens": 2,            # kitchens to build
    "casework_sites": 2,      # casework sites to build
    "max_shelters": 8,        # shelter cap
    "shelter_per_motel": 0,   # one more shelter per this many people in the Motel (0 = none)
    "shelters_first": 3,      # shelters built up front, before anyone is in the Motel
    "rrv": "fallback",        # "never" | "fallback" (no kitchen choice) | "always"
    "motel": False,           # relocate into the Motel when no shelter has room
    "build_until": 2,         # last decisions before the end with no new building
    "hire_trained": True,     # hire trained workers (2 units, $1000) instead of untrained ($300)
    "partial_food": 50,       # send to kitchens when at least this much stock is free (0 = the whole order)
    "budget_food": False,     # track free kitchen stock within the turn (else ask the kitchens regardless)
    "budget_casework": True,  # send casework only while a site has room (else send whenever one is open)
}

_UNITS = {"trained": 2, "untrained": 1}
_BUILD_TYPE = {"Kitchen": "kitchen", "Shelter": "shelter", "CaseworkSite": "casework"}


def _dist(a, b):
    if not a or not b:
        return 0.0
    return sum(((a.get(k) or 0) - (b.get(k) or 0)) ** 2 for k in ("x", "y", "z")) ** 0.5


def _budget_gain(c):
    for i in c.get("impacts") or []:
        if i.get("type") == "Budget":
            return float(i.get("value") or 0)
    return 0.0


def _available(c):
    return not (c.get("unavailableReason") or "")


def _qty(c):
    return int(c.get("deliveryQuantity") or 0)


def _answer(task, cfg, room):
    """The choiceId to answer `task` with, or None to leave it. `room` holds what this turn can
    still give out -- free kitchen stock ("food") and casework places ("casework") -- and is
    drawn down as tasks are answered, so a request the kitchens or sites cannot cover goes to
    the fallback instead of into the game's refusal."""
    cs = [c for c in task.get("choices") or [] if _available(c)]
    if not cs:
        return None
    gains = [c for c in cs if _budget_gain(c) > 0]
    if gains:                                       # funding: the largest offer
        return max(gains, key=_budget_gain)["choiceId"]
    text = lambda c: (c.get("choiceText") or "").lower()
    food = [c for c in cs if "meal" in text(c)]
    if food:
        kitchen = [c for c in food if not c.get("immediateDelivery")]
        rrv = [c for c in food if c.get("immediateDelivery")]
        if cfg["rrv"] == "always" and rrv:
            return rrv[0]["choiceId"]
        if kitchen and room["food"] >= max(1, min(_qty(kitchen[0]), cfg["partial_food"] or _qty(kitchen[0]))):
            room["food"] -= _qty(kitchen[0])
            return kitchen[0]["choiceId"]
        if cfg["rrv"] == "fallback" and rrv:
            return rrv[0]["choiceId"]
        return None
    casework = [c for c in cs if c.get("destinationCategory") == "CaseworkSite"]
    if casework:
        if room["casework"] <= 0:
            return None
        room["casework"] -= max(1, _qty(casework[0]))
        return casework[0]["choiceId"]
    shelter = [c for c in cs if c.get("destinationCategory") == "Shelter"]
    motel = [c for c in cs if c.get("destinationCategory") == "Motel"]
    if shelter:
        return shelter[0]["choiceId"]
    if motel and cfg["motel"]:
        return motel[0]["choiceId"]
    return None


def rules_calls(env, rnd=0, rounds_total=DECISIONS, cfg=None):
    """This turn's tool calls."""
    cfg = dict(PARAMS, **(cfg or {}))
    gs = env.game_state or {}
    ms = gs.get("mapState") or {}
    facs = ms.get("facilities") or []
    by_type = lambda t: [f for f in facs if f.get("buildingType") == t]
    calls = []
    # What this turn can hand out: kitchen stock not already promised to a delivery, and room at
    # operational casework sites (capacity - population - people already walking there).
    inbound = sum(d.get("quantity") or 0 for d in (gs.get("logistics") or {}).get("activeDeliveries") or []
                  if "food" in str(d.get("cargoType", "")).lower())
    stock = sum((f.get("resources") or {}).get("foodPacks") or 0
                for f in by_type("Kitchen") if f.get("isOperational"))
    room = {"food": max(0, stock - inbound) if cfg["budget_food"] else float("inf"),
            "casework": float("inf") if not cfg["budget_casework"] else sum(max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0)
                                - (f.get("incomingPopulation") or 0))
                            for f in by_type("CaseworkSite") if f.get("isOperational"))}
    if not cfg["budget_casework"] and not any(f.get("isOperational") for f in by_type("CaseworkSite")):
        room["casework"] = 0

    tokens = task_tokens(gs.get("allActiveTasks") or [])
    for t in gs.get("allActiveTasks") or []:
        cid = _answer(t, cfg, room)
        if cid is not None:
            calls.append(("task", {"task_id": tokens[t["taskId"]], "choice_id": int(cid)}))

    # ── building ──
    rounds_left = rounds_total - rnd
    builds = []
    if rounds_left > cfg["build_until"]:
        motel_pop = sum(f.get("currentPopulation") or 0 for f in by_type("Motel"))
        want_shelters = cfg["shelters_first"]
        if cfg["shelter_per_motel"]:
            want_shelters += -(-motel_pop // cfg["shelter_per_motel"])
        want = {"Kitchen": cfg["kitchens"], "CaseworkSite": cfg["casework_sites"],
                "Shelter": min(cfg["max_shelters"], want_shelters)}
        sites = [s for s in (gs.get("constructionState") or {}).get("availableSites") or []
                 if s.get("isAvailable")]
        comm = [c.get("position") for c in by_type("Community") if c.get("position")]
        sites.sort(key=lambda s: min((_dist(s.get("position"), p) for p in comm), default=0.0))
        cost = float((gs.get("constructionState") or {}).get("buildingConstructionCost") or 0)
        budget = float((gs.get("satisfactionAndBudget") or {}).get("budget") or 0)
        for btype in ("Kitchen", "CaseworkSite", "Shelter"):
            for _ in range(max(0, want[btype] - len(by_type(btype)))):
                if not sites or budget < cost:
                    break
                site = sites.pop(0)
                builds.append(btype)
                budget -= cost
                calls.append(("build", {"type": _BUILD_TYPE[btype], "site_id": int(site["siteId"])}))

    # ── staffing and hiring ──
    wf = gs.get("workforceState") or {}
    free_units = 2 * (wf.get("freeTrainedWorkers") or 0) + (wf.get("freeUntrainedWorkers") or 0)
    arriving = 2 * (wf.get("trainedWorkersNotArrived") or 0) + (wf.get("untrainedWorkersNotArrived") or 0)
    waiting = [f for f in facs if f.get("buildingStatus") == "NeedWorker"]
    need = 0
    for f in waiting:
        gap = max(0, (f.get("requiredWorkforce") or 0) - (f.get("assignedWorkforce") or 0))
        need += gap
        if gap <= free_units:
            calls.append(("staff", {"site": f["facilityName"]}))
            free_units -= gap
            need -= gap
    need += 4 * len(builds)                       # this turn's builds, staffed next turn
    short = need - free_units - arriving
    if short > 0:
        kind = "trained" if cfg["hire_trained"] else "untrained"
        count = -(-short // _UNITS[kind])
        calls.append(("hire", {"kind": kind, "count": int(count)}))
    return calls


def rules(env, rnd=0, rounds_total=DECISIONS, cfg=None):
    """The POLICIES entry: a decision carrying its tool calls directly (see common.tool_calls)."""
    return {"choices": [], "actions": [], "tool_calls": rules_calls(env, rnd, rounds_total, cfg),
            "note": "rules"}
