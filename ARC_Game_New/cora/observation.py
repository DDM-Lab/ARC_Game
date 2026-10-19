"""The observation: what a model (benchmark, RL policy or GUI officer) is shown of the game.

One builder and one renderer for every front end:

    obs  = observe(game_state, actions, ObsConfig(...))   # game state -> observation dict
    text = render(obs)                                     # compact text block for the prompt
    text = render(obs, prev=prev_obs)                      # same, facilities diffed vs last turn

`game_state` is the GameStatePayload JSON the game exports (Assets/Scripts/LLM/
GameStateStructures.cs); `actions` is the enumerated action list for the turn (cora action
enumerator), from which the `available` block is derived. Options are explicit in ObsConfig;
nothing here reads globals or the environment.

Every number shown comes from the game state (costs, capacities, workforce, the motel rate, the
rounds left). Task ids are stable tokens (task_token) so a task keeps its name across turns;
tasks that share a token are told apart by their game task number (task_tokens).
"""
from __future__ import annotations

import json
from collections import Counter
from dataclasses import dataclass


@dataclass(frozen=True)
class ObsConfig:
    """What the observation includes.

    show_impacts              each task choice lists its budget/satisfaction impacts
    mark_unavailable_choices  choices the player's task panel would grey out are marked, with the
                              game's reason (a prompt pack that mentions this sets it)
    officer_extras            GUI officers only: people already on their way, pending effects with
                              timing, distances from open sites to landmarks, the motel's per-person
                              rate, and the last two daily reports
    """
    show_impacts: bool = True
    mark_unavailable_choices: bool = False
    officer_extras: bool = False


# ── building the observation ────────────────────────────────────────────────

def observe(game_state: dict, actions: list | None = None, config: ObsConfig = ObsConfig()) -> dict:
    """The observation dict. With `actions`, it also carries the `available` block (what can be
    done this turn, derived from the same action list the executor resolves against)."""
    gs = game_state or {}
    sb = gs.get("satisfactionAndBudget", {})
    wf = gs.get("workforceState", {})
    names = _display_names(gs)
    facs = []
    for f in gs.get("mapState", {}).get("facilities", []):
        facs.append({"name": f.get("facilityName"), "type": f.get("buildingType"),
                     "status": f.get("buildingStatus"), "workers": f.get("assignedWorkforce"),
                     "needWorkers": f.get("requiredWorkforce"),
                     "food": (f.get("resources") or {}).get("foodPacks"),
                     "pop": f.get("currentPopulation"), "cap": f.get("populationCapacity"),
                     # People already on their way here (walking or on a vehicle): the game counts
                     # them against free space, so a destination reading 0/50 with 31 incoming has
                     # room for 19. Without it the benchmark refused casework sends the model could
                     # not see were over capacity (18-32 a game, 1.0.2 benchmark, 2026-10-14).
                     "incoming": f.get("incomingPopulation") or 0,
                     # The site a building stands on (the free-site list only shows FREE sites).
                     # Passive fixtures (Communities, Motel) report none.
                     "site": f.get("originalSiteId")})
    obs = {
        "day": gs.get("sessionInfo", {}).get("currentDay"),
        "budget": sb.get("budget"), "satisfaction": sb.get("satisfaction"),
        "workers": {"freeTrained": wf.get("freeTrainedWorkers"), "freeUntrained": wf.get("freeUntrainedWorkers"),
                    # Hired but not here yet: workers arrive a few rounds after the hire.
                    "arrivingTrained": wf.get("trainedWorkersNotArrived"),
                    "arrivingUntrained": wf.get("untrainedWorkersNotArrived"),
                    "working": wf.get("workingTrainedWorkers", 0) + wf.get("workingUntrainedWorkers", 0),
                    "inTraining": wf.get("untrainedWorkersInTraining")},
        "logistics": {"vehiclesFree": gs.get("logistics", {}).get("availableVehicles")},
        # Clients relocating on foot: destination space already counts them.
        # Named as the facilities table names them (the game reports "CaseworkSite_2" for
        # "Casework Alpha"), so a walk can be matched to its destination's row.
        "walking": [{"n": r.get("quantity"), "from": names.get(r.get("source"), r.get("source")),
                     "to": names.get(r.get("destination"), r.get("destination")),
                     "rounds": r.get("roundsRemaining")}
                    for r in (gs.get("logistics", {}).get("pendingRelocations") or [])],
        "facilities": facs,
        "tasks": _tasks(gs, facs, config),
    }
    rounds_left = _rounds_left(gs)
    if rounds_left is not None:
        obs["roundsLeft"] = rounds_left
    # Cumulative spend per category: where the budget went (lodging silently accrues the motel).
    rm = gs.get("rewardMetrics") or {}
    spend = {k: rm.get(src) for k, src in (("food", "foodSpend"), ("lodging", "lodgingSpend"),
                                          ("worker", "workerSpend"), ("casework", "caseworkSpend"))
             if rm.get(src) is not None}
    if spend:
        obs["spend"] = spend
    # Today's motel bill: residents x the game's per-person daily rate (recurring, not on any choice).
    rate = gs.get("motelCostPerPersonPerDay")
    motel_pop = sum((f.get("pop") or 0) for f in facs
                    if "motel" in (str(f.get("type", "")) + str(f.get("name", ""))).lower())
    if motel_pop > 0 and rate:
        obs["motelDailyCost"] = motel_pop * int(rate)
    obs["costs"] = _costs(gs)
    vcap = vehicle_capacity(gs)
    if vcap:
        obs["logistics"]["vehicleCapacity"] = vcap
    if actions is not None:
        obs["available"] = _available(facs, actions)
    if config.officer_extras:
        _add_officer_extras(obs, gs)
    return obs


def _tasks(gs, facs, config):
    names = {f.get("name") for f in facs}
    tokens = task_tokens(gs.get("allActiveTasks", []))
    casework_open = _casework_site_in_use(gs)
    tasks = []
    for t in gs.get("allActiveTasks", []):
        choices = []
        for c in (t.get("choices") or []):
            o = {"choiceId": c["choiceId"], "text": c["choiceText"][:200]}
            if config.show_impacts and c.get("impacts"):
                o["impacts"] = {i["type"]: i["value"] for i in c["impacts"]}
            if config.mark_unavailable_choices and c.get("feasible") is False:
                o["unavailable"] = (c.get("unavailableReason") or "cannot be carried out right now")[:140]
            elif (config.mark_unavailable_choices and not casework_open
                  and c.get("destinationCategory") == "CaseworkSite"):
                o["unavailable"] = _NO_CASEWORK_SITE
            if config.officer_extras and c.get("displayIndex"):
                o["screen"] = c["displayIndex"]       # the card's place in the player's panel
            choices.append(o)
        td = {"taskId": t["taskId"], "type": t["taskType"], "title": t["taskTitle"],
              "roundsLeft": t.get("roundsRemaining"), "choices": choices}
        # The description without the internal "|CLIENT_GROUP_ID:..." routing suffix.
        desc = (t.get("taskDescription") or "").split("|", 1)[0].strip()
        if desc:
            td["desc"] = desc[:120]
        # `affects` only when it names a facility that exists (the daily budget task carries a
        # generic placeholder such as "Shelter" that matches nothing).
        affected = t.get("affectedFacility")
        if affected and affected in names:
            td["affects"] = affected
        # Identity comes from the RAW task (affectedFacility, not the display `affects` above), so a
        # task resolves to the same token in the observation and in the executor.
        td["token"] = tokens[t["taskId"]]
        tasks.append(td)
    return tasks


# The game's task panel does not grey out a casework choice (TaskDetailUI.IsChoiceFeasibleFor
# exempts it), but confirming one refuses it unless an OPERATIONAL casework site exists
# (ValidateSpecificBuildingPopulationDestination; its message says "currently built", but a built,
# unstaffed site is refused too). Without the mark, models answered every Casework Request into
# that refusal -- 93 refused calls in one qwen3.5:4b game (local benchmark, 2026-10-13).
_NO_CASEWORK_SITE = "no casework site is in use (one must be built and fully staffed)"


def _casework_site_in_use(gs) -> bool:
    return any(f.get("buildingType") == "CaseworkSite" and f.get("isOperational")
               for f in (gs.get("mapState") or {}).get("facilities", []))


def _rounds_left(gs):
    """Simulated rounds left in the game (None when the build does not export the horizon)."""
    si = gs.get("sessionInfo") or {}
    day, rnd, final, per_day = (si.get("currentDay"), si.get("currentRound"), si.get("finalDay"),
                                si.get("roundsPerDay"))
    if None in (day, rnd, final) or not per_day:
        return None
    return max(0, (final - day) * per_day + (per_day - rnd))


def _costs(gs) -> dict:
    """The price list (build / hireUntrained / hireTrained / train), read live from the game.
    An absent key is omitted rather than guessed."""
    cs = gs.get("constructionState") or {}
    wf = gs.get("workforceState") or {}
    c = {"build": cs.get("buildingConstructionCost"), "hireUntrained": wf.get("untrainedWorkerCost"),
         "hireTrained": wf.get("trainedWorkerCost"), "train": wf.get("trainingCostPerWorker")}
    return {k: v for k, v in c.items() if v is not None}


def vehicle_capacity(gs):
    """One vehicle's load, in the unit food is reported in (None if the fleet is empty). A transfer
    larger than one load is split across several vehicles."""
    caps = [v.get("maxCapacity") for v in (gs.get("mapState") or {}).get("vehicles") or [] if v.get("maxCapacity")]
    return max(caps) if caps else None


def _available(facs, actions):
    """What can be done this turn, from the enumerated actions (so it never contradicts them).
    needStaff is every built facility still short of workers, by exact name — the valid staff
    targets; staffNow is the subset staffable from the current free pool."""
    need_staff = {}
    for f in facs:
        if f.get("status") in ("NeedWorker", "InUse"):
            rem = (f.get("needWorkers") or 0) - (f.get("workers") or 0)
            if rem > 0 and f.get("name"):
                need_staff[f["name"]] = rem
    hire_kinds, train_max, build_sites, staff_now, transfers = set(), 0, set(), {}, set()
    for a in actions:
        t = a.get("action_type")
        if t == "worker":
            w = a.get("worker", {})
            kind = w.get("worker_action_type")
            if kind == "hire_untrained":
                hire_kinds.add("untrained")
            elif kind == "hire_trained":
                hire_kinds.add("trained")
            elif kind == "train_untrained":
                train_max = max(train_max, w.get("quantity", 0))
        elif t == "worker_assignment":
            asg = a.get("assignment", {})
            b = asg.get("building_name")
            if b is not None:
                staff_now[b] = max(staff_now.get(b, 0), asg.get("quantity", 0))
        elif t == "construction":
            sid = a.get("construction", {}).get("site_id")
            if sid is not None:
                build_sites.add(sid)
        elif t == "resource_transfer":
            tr = a.get("transfer", {})
            transfers.add((tr.get("resource_type"), tr.get("source_facility"), tr.get("destination_facility")))
    return {"hire": sorted(hire_kinds), "trainUntrainedMax": train_max, "needStaff": need_staff,
            "staffNow": staff_now, "buildSites": sorted(build_sites),
            "transfers": [{"resource": r, "from": s, "to": d}
                          for (r, s, d) in sorted(transfers, key=lambda x: tuple(map(str, x)))]}


# ── officer extras (GUI officers) ───────────────────────────────────────────
# Added after Talos session 1bb785d1, where the officers' observation explained most of their
# mistakes: relocations named by GameObject name, no timing for anything pending, no locations,
# the motel's daily total but not its rate, no daily report. Each line renders only when its
# data is present.

def _display_names(gs):
    """GameObject name ("Shelter_1") -> display name ("Shelter Bravo")."""
    out = {}
    for f in (gs.get("mapState") or {}).get("facilities") or []:
        bt, sid, nm = f.get("buildingType"), f.get("originalSiteId"), f.get("facilityName")
        if f.get("facilityType") == "Building" and bt and sid is not None and nm:
            out[f"{bt}_{sid}"] = nm
    return out


def _pos(p):
    try:
        return float(p["x"]), float(p["y"])
    except (TypeError, KeyError, ValueError):
        return None


def _add_officer_extras(obs, gs):
    names = _display_names(gs)
    for w in obs.get("walking") or []:
        for k in ("to", "from"):
            if w.get(k) in names:
                w[k] = names[w[k]]
    facs = (gs.get("mapState") or {}).get("facilities") or []
    # People already on their way to each facility: the game's figure (reserved deliveries + walking),
    # or the walking list on builds that do not export it. Folded into one "incoming" line.
    incoming = {}
    if any("incomingPopulation" in f for f in facs):
        for f in facs:
            if (f.get("incomingPopulation") or 0) > 0:
                incoming[f.get("facilityName")] = f["incomingPopulation"]
    else:
        for w in obs.get("walking") or []:
            incoming[w.get("to")] = incoming.get(w.get("to"), 0) + (w.get("n") or 0)
    if incoming:
        pop = {f.get("facilityName"): (f.get("currentPopulation"), f.get("populationCapacity")) for f in facs}
        walks = {}
        for w in obs.get("walking") or []:
            walks.setdefault(w.get("to"), []).append(w)
        obs["incoming"] = [{"name": n, "n": q, "pop": pop.get(n, (None, None))[0],
                            "cap": pop.get(n, (None, None))[1], "walks": walks.get(n, [])}
                           for n, q in incoming.items()]
        obs.pop("walking", None)
    # Where people can be relocated to right now, by the game's own rule
    # (ClientRelocationHandler.GetDestinationsSorted): a shelter only while InUse (fully staffed),
    # the motel always; free = capacity - population - people already on their way. The facility
    # rows' pop/cap alone overstated room (playtest 2026-10-07: "Only N of N clients could be
    # placed" on shelters that read as half empty).
    lodging = []
    for f in facs:
        if f.get("buildingType") not in ("Shelter", "Motel"):
            continue
        room = max(0, (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0)
                   - (f.get("incomingPopulation") or 0))
        status = f.get("buildingStatus")
        accepting = f.get("buildingType") == "Motel" or status == "InUse"
        lodging.append({"name": names.get(f.get("facilityName"), f.get("facilityName")), "free": room,
                        "accepting": accepting, "status": status})
    if lodging:
        obs["lodging"] = lodging
    if gs.get("pendingEffects"):
        obs["pending"] = gs["pendingEffects"]
    # Food already on the road (vehicles), by destination: part of "already on the way".
    transit = {}
    for d in (gs.get("logistics") or {}).get("activeDeliveries") or []:
        if "food" in str(d.get("cargoType", "")).lower():
            dest = names.get(d.get("destination"), d.get("destination"))
            transit[dest] = transit.get(dest, 0) + (d.get("quantity") or 0)
    if transit:
        obs["foodInTransit"] = transit
    rm = gs.get("rewardMetrics") or {}
    if rm.get("scoreAvailable"):
        obs["scoreParts"] = {k: rm.get(k) for k in ("sFood", "sLodging", "sWorkerUse", "sWaste", "sCasework",
                                                     "cFood", "cLodging", "cWorker", "cCasework", "liveSatisfaction",
                                                     "liveEfficiency")}
    # Distance from every open site to every landmark (each community and the motel).
    landmarks = [(f.get("facilityName"), _pos(f.get("position"))) for f in facs
                 if str(f.get("buildingType", "")).lower() in ("community", "motel") and _pos(f.get("position"))]
    if landmarks:
        dist = lambda a, b: round(((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2) ** 0.5, 1)
        rows = []
        for s in (gs.get("constructionState") or {}).get("availableSites") or []:
            p = _pos(s.get("position"))
            if p and s.get("isAvailable", True):
                rows.append((s.get("siteId"), [(n, dist(p, q)) for n, q in landmarks]))
        if rows:
            obs["siteDistances"] = sorted(rows, key=lambda t: (t[0] is None, t[0]))
    if gs.get("motelCostPerPersonPerDay") and any("motel" in str(f.get("buildingType", "")).lower() for f in facs):
        obs["motelPerPersonPerDay"] = int(gs["motelCostPerPersonPerDay"])
    if gs.get("dailyReports"):
        obs["dailyReports"] = gs["dailyReports"]


# ── rendering ───────────────────────────────────────────────────────────────

def render(obs: dict, prev: dict | None = None) -> str:
    """The observation as the compact text block a model reads. With `prev`, the facilities table
    shows only what changed since that observation (+new ~changed -removed); everything actionable
    stays in full. Officer extras, when present, follow the main block."""
    facilities = render_facilities_delta(obs, prev) if prev else _facility_lines(obs)
    text = "\n".join(_scalar_lines(obs) + facilities + _task_lines(obs) + _available_lines(obs))
    extras = _officer_extra_lines(obs)
    if text and extras:
        text += "\n" + "\n".join(extras)
    return text


def render_scalars(obs) -> str:
    return "\n".join(_scalar_lines(obs))


def render_facilities(obs) -> str:
    return "\n".join(_facility_lines(obs))


def render_tasks(obs) -> str:
    return "\n".join(_task_lines(obs))


def render_available(obs) -> str:
    return "\n".join(_available_lines(obs))


def _num0(d, key):
    """A numeric field, with a present-but-null value shown as 0 (not "None")."""
    v = d.get(key)
    return 0 if v is None else v


def _scalar_lines(obs):
    g = obs.get
    L = [f"day {g('day')} | budget {g('budget')} | satisfaction {g('satisfaction')} | roundsLeft {g('roundsLeft')}"
         + (f" | motelDailyCost {obs['motelDailyCost']}" if obs.get("motelDailyCost") else "")]
    w = obs.get("workers", {})
    L.append(f"workers: freeTrained {_num0(w, 'freeTrained')} freeUntrained {_num0(w, 'freeUntrained')} "
             f"arrivingTrained {_num0(w, 'arrivingTrained')} arrivingUntrained {_num0(w, 'arrivingUntrained')} "
             f"working {_num0(w, 'working')} inTraining {_num0(w, 'inTraining')}")
    lg = obs.get("logistics", {}) or {}
    cap = lg.get("vehicleCapacity")
    L.append(f"logistics: vehiclesFree {_num0(lg, 'vehiclesFree')}" + (f" vehicleLoad {cap}" if cap else ""))
    walking = obs.get("walking") or []
    if walking:
        L.append("walking: " + "; ".join(f"{w.get('n')} {w.get('from')}->{w.get('to')} in {w.get('rounds')}r"
                                          for w in walking))
    for label in ("spend", "costs"):
        body = " ".join(f"{k} {v}" for k, v in (obs.get(label) or {}).items())
        if body:
            L.append(f"{label}: {body}")
    return L


_FACILITY_LEGEND = "[name type status workers/need food pop/cap site; pop+N = N more already on their way]"


def _facility_row(f):
    site = f.get("site")
    return (f"{f.get('name')} {f.get('type')} {f.get('status') or 'Passive'} "
            f"{f.get('workers', 0)}/{f.get('needWorkers', 0)} {f.get('food', 0)} "
            f"{f.get('pop', 0)}{'+' + str(f['incoming']) if f.get('incoming') else ''}/{f.get('cap', 0)} "
            f"{'-' if site is None or site < 0 else site}")


def _facility_lines(obs):
    facs = obs.get("facilities", [])
    if not facs:
        return []
    return [f"facilities {_FACILITY_LEGEND}:"] + ["  " + _facility_row(f) for f in facs]


def _facility_state(f):
    return (f.get("status"), f.get("workers", 0), f.get("needWorkers", 0),
            f.get("food", 0), f.get("pop", 0), f.get("cap", 0), f.get("incoming", 0))


def render_facilities_delta(obs, prev):
    facs = obs.get("facilities", []) or []
    prev_by = {f.get("name"): f for f in (prev.get("facilities", []) or [])}
    names = {f.get("name") for f in facs}
    rows = []
    for f in facs:
        pf = prev_by.get(f.get("name"))
        if pf is None:
            rows.append("  + " + _facility_row(f))
        elif _facility_state(f) != _facility_state(pf):
            rows.append("  ~ " + _facility_row(f))
    rows += [f"  - {n}" for n in prev_by if n not in names]
    return ([f"facilities Δ (vs last turn; unchanged omitted; +new ~changed -removed) {_FACILITY_LEGEND}:"]
            + (rows or ["  (no change)"]))


def _task_lines(obs):
    tasks = obs.get("tasks", [])
    if not tasks:
        return ["tasks: (none)"]          # an absent block reads as missing information
    L = ['tasks [id type "title" affects (roundsLeft)]:']
    for t in tasks:
        L.append(f"  [{t.get('token') or task_token(t)}] {t.get('type')} \"{t.get('title')}\" "
                 f"{t.get('affects', '')} ({t.get('roundsLeft')} left)")
        for ch in t.get("choices", []):
            imp = ch.get("impacts")
            imps = (" " + " ".join(f"[{k} {v}]" for k, v in imp.items())) if imp else ""
            na = f" — UNAVAILABLE now: {ch['unavailable']}" if ch.get("unavailable") else ""
            screen = f" (option {ch['screen']} on screen)" if ch.get("screen") else ""
            L.append(f"    {ch.get('choiceId')}{screen}: {ch.get('text')}{imps}{na}")
    return L


def _available_lines(obs):
    av = obs.get("available", {})
    if not av:
        return []
    L = ["available:"]
    hire = ",".join(av["hire"]) if av.get("hire") else "UNAVAILABLE"
    L.append(f"  hire: {hire} | trainUntrainedMax {av.get('trainUntrainedMax', 0)}")
    ns = av.get("needStaff")
    if ns is not None:
        L.append("  needStaff: " + (" ".join(f"{k}:{v}" for k, v in ns.items()) or "(none)"))
    sn = av.get("staffNow", {})
    L.append("  staffNow: " + (" ".join(f"{k}:{v}" for k, v in sn.items()) or "(none)"))
    bs = av.get("buildSites", [])
    L.append("  buildSites: " + (",".join(str(x) for x in bs) if bs else "(none)"))
    tr = av.get("transfers", [])
    if tr:
        byres = {}
        for e in tr:
            d = byres.setdefault(e.get("resource"), (set(), set()))
            d[0].add(e.get("from")); d[1].add(e.get("to"))
        L.append("  transfers:")
        for res, (frm, to) in byres.items():
            L.append(f"    {res} from[{','.join(sorted(frm))}] to[{','.join(sorted(to))}]")
    return L


def _officer_extra_lines(obs):
    L = []
    if obs.get("incoming"):
        parts = []
        for i in obs["incoming"]:
            src = ", ".join(f"{w.get('n')} walking from {w.get('from')} in {w.get('rounds')}r"
                            for w in i.get("walks") or [])
            walked = sum((w.get("n") or 0) for w in i.get("walks") or [])
            if i["n"] > walked:
                src = (src + ", " if src else "") + f"{i['n'] - walked} by vehicle"
            room = (f"; now {i['pop']}/{i['cap']}, {max(0, i['cap'] - i['pop'] - i['n'])} free"
                    if i.get("pop") is not None and i.get("cap") else "")
            parts.append(f"{i['name']} +{i['n']} ({src}{room})")
        L.append("incoming (counted against free space): " + "; ".join(parts))
    if obs.get("lodging"):
        open_ = [f"{l['name']} {l['free']} free" for l in obs["lodging"] if l["accepting"]]
        closed = [f"{l['name']} ({l['status'] or 'not ready'})" for l in obs["lodging"] if not l["accepting"]]
        line = "room to relocate people into now: " + ("; ".join(open_) if open_ else "none")
        if closed:
            line += " | not taking people until fully staffed: " + ", ".join(closed)
        L.append(line + " (a relocation needs room for everyone it sends; a flooded route can still block a walk)")
    if obs.get("pending"):
        # Identical effects (same kind and timing) are grouped into one line.
        grouped, order = {}, []
        for p in obs["pending"]:
            key = (p.get("kind"), p.get("roundsRemaining"), p.get("daysRemaining"),
                   p.get("description") if p.get("kind") != "construction" else None)
            if key not in grouped:
                grouped[key] = dict(p, _targets=[]); order.append(key)
            elif p.get("kind") in ("workers_arriving", "training"):
                grouped[key]["quantity"] = (grouped[key].get("quantity") or 0) + (p.get("quantity") or 0)
            elif p.get("kind") == "funding":
                grouped[key]["amount"] = (grouped[key].get("amount") or 0) + (p.get("amount") or 0)
            grouped[key]["_targets"].append(p.get("target"))
        parts = []
        for p in (grouped[k] for k in order):
            r, d = p.get("roundsRemaining", -1), p.get("daysRemaining", -1)
            when = ("this round" if r == 0 else f"in {r}r") if r is not None and r >= 0 else \
                   ("later today" if d == 0 else f"in {d}d") if d is not None and d >= 0 else ""
            k = p.get("kind")
            if k == "funding":
                parts.append(f"+${p.get('amount', 0):,} funding {when} ({p.get('description') or 'approved'})")
            elif k == "workers_arriving":
                parts.append(f"{p.get('quantity')} {str(p.get('description') or '').lower()} workers arrive {when}")
            elif k == "training":
                parts.append(f"{p.get('quantity')} workers finish training {when}")
            elif k == "construction":
                names = [t for t in p.get("_targets") or [p.get("target")] if t]
                parts.append(f"{', '.join(names)} finish{'es' if len(names) == 1 else ''} construction {when}")
        for dest, q in (obs.get("foodInTransit") or {}).items():
            parts.append(f"{q} food on the road to {dest}")
        if parts:
            L.append("already on the way (approved or requested earlier; these are NOT open tasks, "
                     "do not request or fund them again): " + "; ".join(parts))
    elif obs.get("foodInTransit"):
        L.append("already on the way (requested earlier; NOT open tasks): " + "; ".join(
            f"{q} food on the road to {d}" for d, q in obs["foodInTransit"].items()))
    if obs.get("siteDistances"):
        L.append("open sites by map distance (closest first): " + "; ".join(
            f"{sid}: " + " ".join(f"{n.replace('Community ', '')} {round(d)}" for n, d in sorted(ds, key=lambda x: x[1]))
            for sid, ds in obs["siteDistances"]))
    if obs.get("motelPerPersonPerDay"):
        d = obs["motelPerPersonPerDay"]
        L.append(f"motel: ${d} per person per day, billed every round (${d / 4:g} per person per round)")
    sp = obs.get("scoreParts")
    if sp:
        f2 = lambda v: f"{(v or 0):.2f}"
        sat = {"food": sp.get("sFood"), "lodging": sp.get("sLodging"), "workers in use": sp.get("sWorkerUse"),
               "waste": sp.get("sWaste"), "casework": sp.get("sCasework")}
        eff = {"food": sp.get("cFood"), "lodging": sp.get("cLodging"), "workers": sp.get("cWorker"),
               "casework": sp.get("cCasework")}
        L.append(f"score so far: satisfaction {round(sp.get('liveSatisfaction') or 0)} + efficiency "
                 f"{round(sp.get('liveEfficiency') or 0)} (out of 1000 each). Satisfaction parts, 0-1, "
                 f"each a fifth of satisfaction: " + ", ".join(f"{k} {f2(v)}" for k, v in sat.items())
                 + "; cost-efficiency parts, 0-1, each a quarter of efficiency: "
                 + ", ".join(f"{k} {f2(v)}" for k, v in eff.items()) + ".")
    for r in (obs.get("dailyReports") or [])[-2:]:
        sc = r.get("satisfactionChange") or 0
        eaten = (f" needed {r.get('foodNeeded')} eaten {r.get('foodConsumed')}"
                 if r.get("foodConsumed") is not None and "foodNeeded" in r else "")
        L.append(
            f"day {r.get('day')} report: tasks {r.get('completedTasks')}/{r.get('totalTasks')} done, "
            f"{r.get('expiredTasks')} expired; food made {r.get('foodProduced')} delivered "
            f"{r.get('foodDelivered')}{eaten} wasted {r.get('foodWasted')} (all food left at day's end, "
            f"kitchens included); shelter occupancy "
            f"{round((r.get('shelterOccupancyRate') or 0) * 100)}%; idle workers {r.get('idleWorkers')}; "
            f"budget {int(r.get('startingBudget') or 0)} -> {int(r.get('endingBudget') or 0)} "
            f"(spent {int(r.get('budgetSpent') or 0)}, received {int(r.get('budgetReceived') or 0)}); "
            f"satisfaction {'+' if sc >= 0 else ''}{round(sc, 1)}")
    return L


# ── the GUI officers' read tools ────────────────────────────────────────────

OFFICER = ObsConfig(officer_extras=True)

_EMPTY = {"all": "(no observation available)", "facilities": "(no facilities built yet)",
          "workforce": "(no workforce data)", "tasks": "(no active tasks)",
          "logistics": "(no logistics/affordances available)"}


def officer_text(game_state: dict, section: str = "all", actions: list | None = None) -> str:
    """What an officer's read tool returns: the whole observation (`all`, read_state) or one slice
    of it (`facilities`, `workforce`, `tasks`, `logistics`). `logistics` is the available block and
    needs the turn's enumerated actions."""
    if section == "logistics":
        text = render_available(observe(game_state, actions or []))
    else:
        obs = observe(game_state, None, OFFICER)
        text = {"all": render, "facilities": render_facilities, "workforce": render_scalars,
                "tasks": render_tasks}[section](obs)
    return text or _EMPTY[section]


# ── task identity ───────────────────────────────────────────────────────────

def _squeeze(s: str) -> str:
    return "".join(c for c in s if c.isalnum()) or "X"


def _short_affects(a: str) -> str:
    """Compact facility suffix for a token: Community Trinity -> CTRINITY, Shelter_0 -> S0."""
    if not a:
        return "X"
    a = " ".join("".join(c for c in a.strip() if c.isalnum() or c.isspace()).split())
    up = a.upper()
    if up == "MOTEL":
        return "MOTEL"
    if up == "MAINTENANCE":
        return "MAINT"
    for prefix, short in (("COMMUNITY", "C"), ("SHELTER", "S"), ("KITCHEN", "K"), ("CASEWORKSITE", "CS")):
        if up.startswith(prefix):
            tail = a[len(prefix):].lstrip("_")
            if prefix == "COMMUNITY":
                return _squeeze(f"C{tail.upper()}")
            return _squeeze(f"{short}{tail.upper()}") if tail else short
    if up.startswith("CASEWORK"):
        return "CASE"
    return _squeeze(up)


_TITLE_TOKENS = (                    # (title fragment, token or token prefix + facility suffix)
    ("daily budget", "BUDGET_DAILY"), ("emergency budget", "BUDGET_EMERGENCY"),
    ("storm funding", "FUND_STORM"), ("training recommendation", "TRAIN_REC"),
    ("worker shortage", "WORKER_ADVICE"), ("workforce optimization", "ALERT_WORKFORCE"),
    ("flood alert", "ALERT_FLOOD"),
    ("follow-up food request", "FOODF_*"), ("food request", "FOOD_*"),
    ("population relocation", "RELOC_*"), ("relocation request", "RELOC_*"),
    ("community emergency evacuation", "EVAC_*"), ("flood damage evacuation", "EVAC_*"),
    ("flood damage relocation", "FLOODRELOC_*"), ("casework request", "CASEWORK_*"),
    ("vehicle repair", "REPAIR_VEHICLE"), ("shelter flood damage", "FLOOD_*"),
    ("start of day report", "REPORT_DAY"),
)


def task_token(t: dict) -> str:
    """A task's stable id: the same task keeps the same id across turns (BUDGET_DAILY, FOOD_CTRINITY),
    unlike the game's integer taskId. Unknown titles fall back to TASK_<taskId>. Accepts the
    observation shape (title/affects) or the raw shape (taskTitle/affectedFacility)."""
    title = (t.get("title") or t.get("taskTitle") or "").lower()
    affects = t.get("affects") or t.get("affectedFacility") or ""
    for fragment, token in _TITLE_TOKENS:
        if fragment in title:
            return token[:-1] + _short_affects(affects) if token.endswith("*") else token
    return f"TASK_{t.get('taskId', 'X')}"


def task_tokens(tasks) -> dict:
    """taskId -> the id a model is shown for each active task: its task_token, except when several
    active tasks share one (the Motel sends a Casework Request per group of clients, so three can
    be open at once). Each of those gets its game task number appended, CASEWORK_MOTEL#41, so every
    active task can be named; resolve_task_ref accepts either form."""
    base = {t.get("taskId"): task_token(t) for t in tasks}
    n = Counter(base.values())
    return {tid: tok if n[tok] == 1 else f"{tok}#{tid}" for tid, tok in base.items()}


def resolve_task_ref(tasks, ref) -> int:
    """The taskId a model means by `ref`: a token shown by task_tokens (FOOD_CTRINITY or
    CASEWORK_MOTEL#41) or the game's task number. A bare token shared by several active tasks is
    an error naming them, never a guess. Raises ValueError."""
    raw = str(ref).strip().strip("[]")
    by_id = {int(t["taskId"]): t for t in tasks if t.get("taskId") is not None}
    try:
        tid = int(float(raw))
    except ValueError:
        tid = None
    if tid is None:
        base, _, num = raw.partition("#")
        if num.strip().isdigit():
            n = int(num)
            if n in by_id and task_token(by_id[n]).upper() == base.strip().upper():
                return n
        else:
            hits = [i for i, t in by_id.items() if task_token(t).upper() == raw.upper()]
            if len(hits) == 1:
                return hits[0]
            if hits:
                raise ValueError(f"{raw!r} names {len(hits)} active tasks; use one of "
                                 + ", ".join(f"{raw}#{i}" for i in sorted(hits)))
    elif tid in by_id:
        return tid
    raise ValueError(f"no active task {raw!r} this turn")


# Which officer owns a task. The game assigns this when it creates the task but does not export
# it, so it is reconstructed from the title; the names are the TaskOfficer enum values (the
# configs' talkinghead_endpoint).
def task_officer(t: dict) -> str:
    title = (t.get("title") or t.get("taskTitle") or "").lower()
    if "budget" in title or "funding" in title:
        return "ExternalRelationship"
    if "training" in title or "worker" in title or "workforce" in title:
        return "WorkforceService"
    if "food request" in title:
        return "FoodMassCare"
    if any(w in title for w in ("flood", "relocation", "evacuation", "casework", "repair", "road blockage")):
        return "LodgingMassCare"
    return "DisasterOfficer"


_OFFICER_GROUP = {"ExternalRelationship": "budget", "WorkforceService": "workforce", "FoodMassCare": "food",
                  "LodgingMassCare": "lodging", "DisasterOfficer": "disaster"}


def task_group(t: dict) -> str:
    """Coarse task group (budget / workforce / food / lodging / disaster) for config gating."""
    return _OFFICER_GROUP.get(task_officer(t), "disaster")


def user_message(obs, encoding="compact", prev=None) -> str:
    """The turn's user message for an observation, exactly as a model receives it: compact text
    (default), JSON, or delta (facilities diffed against `prev`). The benchmark, RL and the SFT
    export all build the message with this function."""
    if encoding == "delta":
        rendered = render(obs, prev=prev)
    elif encoding == "compact":
        rendered = render(obs)
    else:
        rendered = json.dumps(obs)
    return "State:\n" + rendered + "\n\nAct by calling the tools."
