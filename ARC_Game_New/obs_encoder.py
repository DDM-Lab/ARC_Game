"""Grounded observation encoder for the live agent router.

Ports the benchmark's rich observation encoding (`llm_smoke_test.summarize` /
`render_state_compact`) into the live router path so that BOTH the auto and
choices agents see real, engine-computed game facts — facility state, worker
pools, open tasks with their *per-choice impacts*, logistics, and cumulative
spend — instead of the previous four-fact snapshot (day/segment/satisfaction/
budget). The goal is grounding: the model should quote numbers the engine
already computed, not invent them.

Design notes:
  * Takes a plain ``game_state`` dict (the router already has this) rather than
    an ``env`` object, so it has no coupling to the gym/benchmark harness.
  * Reads the authoritative ``GameStatePayload`` field names (see
    Assets/Scripts/LLM/GameStateStructures.cs): ``satisfactionAndBudget``,
    ``workforceState``, ``mapState.facilities``, ``allActiveTasks[...].choices
    [...].impacts``, ``logistics.availableVehicles``, ``constructionState``,
    ``rewardMetrics``.
  * Every section is GUARDED on the presence of its top-level source key, so a
    restricted ``subobservation_space`` (which drops keys via
    ``filter_observation``) degrades to fewer sections instead of emitting
    misleading zeros. Full-payload (``["all"]``) configs — every choices agent —
    get the complete view.

This module is intentionally standalone; the benchmark keeps its own copy of the
encoding (with its A/B ablation flags) untouched.
"""
from __future__ import annotations

MOTEL_COST_PER_PERSON_PER_DAY = 200  # mirror of MotelCostManager.costPerPersonPerDay (display hint only)

# Static action costs, mirrored from ActionEnumerator, shown so the model can
# reason about affordability without inventing prices.
_STATIC_COSTS = {"hireUntrained": 100, "hireTrained": 300, "train": 500}


def _num(v, default=0):
    return v if isinstance(v, (int, float)) else default


def build_observation(game_state: dict) -> dict:
    """Compress the router's game_state into a structured observation dict.

    Mirrors ``llm_smoke_test.summarize`` (with its enrichments always on) but
    reads the game_state directly and omits sections whose source key is absent.
    """
    gs = game_state or {}
    obs: dict = {}

    session = gs.get("sessionInfo") or {}
    obs["day"] = session.get("currentDay")
    if session.get("finalDay") is not None:
        obs["finalDay"] = session.get("finalDay")

    if "satisfactionAndBudget" in gs:
        sb = gs.get("satisfactionAndBudget") or {}
        obs["budget"] = sb.get("budget")
        obs["satisfaction"] = sb.get("satisfaction")

    if "workforceState" in gs:
        wf = gs.get("workforceState") or {}
        obs["workers"] = {
            "freeTrained": wf.get("freeTrainedWorkers"),
            "freeUntrained": wf.get("freeUntrainedWorkers"),
            "working": _num(wf.get("workingTrainedWorkers")) + _num(wf.get("workingUntrainedWorkers")),
            "inTraining": wf.get("untrainedWorkersInTraining"),
        }

    if "logistics" in gs:
        obs["logistics"] = {"vehiclesFree": (gs.get("logistics") or {}).get("availableVehicles")}

    facs = []
    if "mapState" in gs:
        for f in (gs.get("mapState") or {}).get("facilities", []) or []:
            facs.append({
                "name": f.get("facilityName"), "type": f.get("buildingType"),
                "status": f.get("buildingStatus"), "workers": f.get("assignedWorkforce"),
                "needWorkers": f.get("requiredWorkforce"),
                "food": (f.get("resources") or {}).get("foodPacks"),
                "pop": f.get("currentPopulation"), "cap": f.get("populationCapacity"),
            })
        obs["facilities"] = facs

    if "allActiveTasks" in gs:
        tasks = []
        fac_names = {f.get("name") for f in facs}
        for t in gs.get("allActiveTasks") or []:
            ch = []
            for c in (t.get("choices") or []):
                o = {"choiceId": c.get("choiceId"), "text": (c.get("choiceText") or "")[:200]}
                if c.get("impacts"):
                    # compact: e.g. {"Budget": 5000, "Satisfaction": 10}
                    o["impacts"] = {i.get("type"): i.get("value") for i in c["impacts"]}
                ch.append(o)
            td = {"taskId": t.get("taskId"), "type": t.get("taskType"),
                  "title": t.get("taskTitle"), "roundsLeft": t.get("roundsRemaining"),
                  "choices": ch}
            # Strip the internal "|CLIENT_GROUP_ID:..." routing suffix before showing the model.
            desc = (t.get("taskDescription") or "").split("|", 1)[0].strip()
            if desc:
                td["desc"] = desc[:120]
            # Only surface `affects` when it names a facility that actually exists this
            # round — some tasks carry a generic placeholder matching no real facility.
            aff = t.get("affectedFacility")
            if aff and (not facs or aff in fac_names):
                td["affects"] = aff
            tasks.append(td)
        obs["tasks"] = tasks

    if "constructionState" in gs:
        cs = gs.get("constructionState") or {}
        obs["costs"] = {"build": cs.get("buildingConstructionCost", 1000), **_STATIC_COSTS}
        sites = [{"id": s.get("siteId"), "name": s.get("siteName")}
                 for s in cs.get("availableSites", []) or [] if s.get("isAvailable")]
        if sites:
            obs["sites"] = sites

    if "rewardMetrics" in gs:
        rm = gs.get("rewardMetrics") or {}
        spend = {k: rm.get(src) for k, src in
                 (("food", "foodSpend"), ("lodging", "lodgingSpend"),
                  ("worker", "workerSpend"), ("casework", "caseworkSpend"))
                 if rm.get(src) is not None}
        if spend:
            obs["spend"] = spend

    # Current daily motel burn = motel residents x per-day rate (recurring; not on any choice).
    if facs:
        motel_pop = sum(_num(f.get("pop")) for f in facs
                        if "motel" in (str(f.get("type", "")) + str(f.get("name", ""))).lower())
        if motel_pop > 0:
            obs["motelDailyCost"] = motel_pop * MOTEL_COST_PER_PERSON_PER_DAY

    return obs


def _fac_row(f):
    st = f.get("status") or "-"
    return (f"{f.get('name')} {f.get('type')} {st} "
            f"{_num(f.get('workers'))}/{_num(f.get('needWorkers'))} food:{_num(f.get('food'))} "
            f"pop:{_num(f.get('pop'))}/{_num(f.get('cap'))}")


def _render_scalars(obs):
    day = obs.get("day")
    horizon = f" of {obs['finalDay']}" if obs.get("finalDay") is not None else ""
    head = f"day {day}{horizon}"
    if "budget" in obs:
        head += f" | budget ${_num(obs.get('budget')):,} | satisfaction {obs.get('satisfaction')}"
    if obs.get("motelDailyCost"):
        head += f" | motelDailyCost ${obs['motelDailyCost']:,}/day"
    L = [head]
    w = obs.get("workers")
    if w is not None:
        L.append(f"workers: freeTrained {_num(w.get('freeTrained'))} "
                 f"freeUntrained {_num(w.get('freeUntrained'))} "
                 f"working {_num(w.get('working'))} inTraining {_num(w.get('inTraining'))}")
    if "logistics" in obs:
        L.append(f"logistics: vehiclesFree {_num(obs['logistics'].get('vehiclesFree'))}")
    if obs.get("spend"):
        L.append("spend so far: " + " ".join(f"{k} ${_num(v):,}" for k, v in obs["spend"].items()))
    if obs.get("costs"):
        L.append("unit costs: " + " ".join(f"{k} ${_num(v):,}" for k, v in obs["costs"].items()))
    return L


def _render_facilities(obs):
    facs = obs.get("facilities")
    if not facs:
        return []
    L = ["facilities [name type status workers/need food pop/cap]:"]
    L += ["  " + _fac_row(f) for f in facs]
    return L


def _render_tasks(obs):
    tasks = obs.get("tasks")
    if not tasks:
        return []
    L = ["open tasks [id type \"title\" affects (roundsLeft)]:"]
    for t in tasks:
        L.append(f"  [{t.get('taskId')}] {t.get('type')} \"{t.get('title')}\" "
                 f"{t.get('affects','')} ({t.get('roundsLeft')} left)")
        if t.get("desc"):
            L.append(f"    {t['desc']}")
        for ch in t.get("choices", []):
            imp = ch.get("impacts")
            imps = (" -> " + " ".join(f"[{k} {v:+d}]" if isinstance(v, int) else f"[{k} {v}]"
                                      for k, v in imp.items())) if imp else ""
            L.append(f"    choice {ch.get('choiceId')}: {ch.get('text')}{imps}")
    return L


def render_state_text(game_state: dict) -> str:
    """Render the router game_state as a compact, grounded text block.

    Drop-in replacement for the old threadbare ``state_text`` in
    ``llm_query._build_prompt``. Sections absent from the (possibly filtered)
    game_state are simply omitted.
    """
    obs = build_observation(game_state)
    lines = _render_scalars(obs) + _render_facilities(obs) + _render_tasks(obs)
    return "\n".join(lines) if lines else "(no observation available)"
