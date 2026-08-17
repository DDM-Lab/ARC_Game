"""Shared observation encoder — the SINGLE source of truth for BOTH the offline
benchmark and the live agent router.

Design decision (2026-07-08, user directive): the router and the benchmark must
use the *same* obs encoder, and the benchmark's encoder is canonical. This module
is therefore a VERBATIM port of the benchmark encoder that previously lived in
``llm_smoke_test.py`` (``summarize`` / ``summarize_commands`` / ``render_state_*``),
with only two mechanical changes so it can be shared:

  1. the two A/B ablation module globals ``_NEW`` / ``_V2`` are lifted into explicit
     ``new`` / ``v2`` keyword parameters, and
  2. the ``env`` argument is replaced by explicit ``(game_state, actions)`` — where
     ``actions`` is exactly ``env.get_valid_actions()`` — so the module has no
     coupling to the gym/benchmark harness and the router (which holds only a raw
     ``game_state`` dict) can call it directly.

Consumers:
  * benchmark: ``llm_smoke_test.py`` keeps thin ``summarize(env)`` /
    ``render_state_compact(obs)`` wrappers that forward its ``_NEW`` / ``_V2``
    globals here — so benchmark output is BYTE-IDENTICAL to before (guarded by a
    golden equivalence test; the benchmark A/B toggles are unchanged).
  * router: ``llm_query`` / ``choices_reliability`` call ``render_state_text`` /
    ``build_observation`` here with ``new=True, v2=True`` (the enriched, fixed view).

Field names are the authoritative ``GameStatePayload`` keys (see
Assets/Scripts/LLM/GameStateStructures.cs): ``satisfactionAndBudget``,
``workforceState``, ``mapState.facilities``, ``allActiveTasks[...].choices[...].impacts``,
``logistics.availableVehicles``, ``constructionState``, ``rewardMetrics``.
"""
from __future__ import annotations

import os

MOTEL_COST_PER_PERSON_PER_DAY = 200  # mirror of MotelCostManager.costPerPersonPerDay (display hint only)


def _num(v, default=0):
    """Router-facing numeric coercion for $-formatting in llm_query / choices_reliability:
    isinstance-based so a stray string/None formats as ``default`` instead of raising.
    NOTE: the canonical obs-builder below deliberately does NOT use this — it mirrors the
    benchmark's inline ``.get(..., 0)`` — so keep this helper only for router callers."""
    return v if isinstance(v, (int, float)) else default


# ─────────────────────────────────────────────────────────────────────────────
# CANONICAL ENCODER  (verbatim port of the benchmark's summarize/render_* — _NEW->new,
# _V2->v2, env.game_state->game_state, env.get_valid_actions()->actions)
# ─────────────────────────────────────────────────────────────────────────────

def compact_action(i, a, *, new):
    desc = a.get("description", "")
    o = {"i": i, "type": a.get("action_type"), "desc": desc if new else desc[:48], "cost": a.get("cost")}
    if a.get("action_type") == "construction":
        o["required_workers"] = a.get("required_workers", 4)
    return o


def build_observation(game_state, actions=None, *, new=True, v2=True,
                      show_impacts=True, rounds_left=None):
    """Compress the game state into the observation the LLM sees (== benchmark ``summarize``).

    ``actions`` is the list from ``env.get_valid_actions()``; the enumerated ``actions``
    menu is included iff it is provided (the router passes ``None`` — it formats the
    action menu separately — and reads only the state fields).

    show_impacts: when True, each task choice includes its sparse impacts list
    (e.g. Budget +5000, Satisfaction +10) so the agent can reason about funding /
    cost tradeoffs. Toggle off for the no-observation-impacts ablation.
    rounds_left: rounds remaining in the EPISODE (game horizon), so the model can size
    recurring costs against the time left. None -> field omitted.
    """
    gs = game_state or {}
    sb = gs.get("satisfactionAndBudget", {})
    wf = gs.get("workforceState", {})
    facs = []
    for f in gs.get("mapState", {}).get("facilities", []):
        facs.append({"name": f.get("facilityName"), "type": f.get("buildingType"),
                     "status": f.get("buildingStatus"), "workers": f.get("assignedWorkforce"),
                     "needWorkers": f.get("requiredWorkforce"),
                     "food": (f.get("resources") or {}).get("foodPacks"),
                     "pop": f.get("currentPopulation"), "cap": f.get("populationCapacity"),
                     # The site this building occupies. `sites` lists only FREE sites, so without
                     # this a policy cannot tell which site any standing building is on — it can
                     # only infer occupancy from an id's absence. Passive fixtures (Communities,
                     # Motel) were never built on a site and report none.
                     "site": f.get("originalSiteId")})
    tasks = []
    for t in gs.get("allActiveTasks", []):
        ch = []
        for c in (t.get("choices") or []):
            o = {"choiceId": c["choiceId"],
                 "text": (c["choiceText"][:(200 if v2 else 90)] if new else c["choiceText"][:70])}
            if show_impacts and c.get("impacts"):
                # compact: e.g. {"Budget": 5000, "Satisfaction": 10}
                o["impacts"] = {i["type"]: i["value"] for i in c["impacts"]}
            ch.append(o)
        td = {"taskId": t["taskId"], "type": t["taskType"], "title": t["taskTitle"],
              "roundsLeft": t.get("roundsRemaining"), "choices": ch}
        if new:
            # taskDescription explains the task (esp. casework: "in shelter for N rounds...").
            # Strip the internal "|CLIENT_GROUP_ID:..." routing suffix before showing the model.
            desc = (t.get("taskDescription") or "").split("|", 1)[0].strip()
            if desc:
                td["desc"] = desc[:120]
            # minimal_v2: surface `affects` only when it names a facility that actually exists
            # this round — some tasks (e.g. the daily budget Advisory) carry a generic placeholder
            # like "Shelter" that matches no real facility (a confusing dangling reference). Plain
            # `minimal` keeps the raw affectedFacility (the un-fixed behavior) for the A/B control.
            _aff = t.get("affectedFacility")
            if _aff and (not v2 or _aff in {f.get("name") for f in facs}):
                td["affects"] = _aff
        # Stable identity token — computed from the RAW affectedFacility, NOT the
        # v2-guarded display `affects` above. cmd_parser (cmd_parser.py:189) and the
        # router (_norm_task_for_token) both recompute the token from raw
        # affectedFacility when resolving a <task> tag; the display guard only hides
        # dangling facility refs for readability and must not mangle task identity.
        # Without this, a community food request renders as FOOD_X (affects stripped)
        # while the parser expects FOOD_C01 → the <task> tag never resolves.
        td["token"] = stable_task_token({"title": t.get("taskTitle"),
                                         "affects": t.get("affectedFacility"),
                                         "taskId": t.get("taskId")})
        tasks.append(td)
    obs = {
        "day": gs.get("sessionInfo", {}).get("currentDay"),
        "budget": sb.get("budget"), "satisfaction": sb.get("satisfaction"),
        "workers": {"freeTrained": wf.get("freeTrainedWorkers"), "freeUntrained": wf.get("freeUntrainedWorkers"),
                    "working": wf.get("workingTrainedWorkers", 0) + wf.get("workingUntrainedWorkers", 0),
                    "inTraining": wf.get("untrainedWorkersInTraining")},
        "logistics": {"vehiclesFree": gs.get("logistics", {}).get("availableVehicles")},
        "facilities": facs,
        "tasks": tasks,
    }
    if actions is not None:
        obs["actions"] = [compact_action(i, a, new=new) for i, a in enumerate(actions)]
    if new:
        if rounds_left is not None:
            obs["roundsLeft"] = rounds_left
        # Cumulative per-category spend (already in the reward-metrics payload) so the model can
        # see where its budget went — esp. lodging (which silently accrues the motel per-day charge).
        rm = gs.get("rewardMetrics") or {}
        spend = {k: rm.get(src) for k, src in
                 (("food", "foodSpend"), ("lodging", "lodgingSpend"),
                  ("worker", "workerSpend"), ("casework", "caseworkSpend"))
                 if rm.get(src) is not None}
        if spend:
            obs["spend"] = spend
        # Current daily motel burn = motel residents x per-day rate (recurring; not on the choice).
        motel_pop = sum((f.get("pop") or 0) for f in facs
                        if "motel" in (str(f.get("type", "")) + str(f.get("name", ""))).lower())
        if motel_pop > 0:
            obs["motelDailyCost"] = motel_pop * MOTEL_COST_PER_PERSON_PER_DAY
    return obs


def build_observation_commands(game_state, actions, *, new=True, v2=True,
                               show_impacts=True, rounds_left=None):
    """State-only observation for the command-tag format (== benchmark ``summarize_commands``):
    identical game facts as ``build_observation``, but WITHOUT the enumerated ``actions`` menu.
    Instead exposes the slots a command can target — available construction ``sites`` (id+name)
    and a fixed ``costs`` block — so the model can form <build>/<hire>/<staff>/<task> commands
    from state alone. The big token saving is dropping the ~70-entry action list."""
    obs = build_observation(game_state, actions, new=new, v2=v2,
                            show_impacts=show_impacts, rounds_left=rounds_left)
    obs.pop("actions", None)
    cs = (game_state or {}).get("constructionState", {})
    obs["sites"] = [{"id": s.get("siteId"), "name": s.get("siteName")}
                    for s in cs.get("availableSites", []) if s.get("isAvailable")]
    obs["costs"] = {"build": cs.get("buildingConstructionCost", 1000),
                    "hireUntrained": 100, "hireTrained": 300, "train": 500}
    # AFFORDANCE BLOCK — what is actually executable THIS round, derived from the same
    # valid-action set the parser/menu use (so it can never contradict them). This is the
    # compact replacement for the dropped idx menu: it tells the model which commands will
    # land instead of making it infer availability from raw state (the cause of the bulk of
    # cmd rejections: staff with no free workers, transfer with no idle vehicle, hire when
    # no bundle is offered). `staffNow` reflects CURRENT free workers; workers you <hire>
    # this turn become staffable too (executed after the hire — see the prompt).
    # `needStaff` is the AUTHORITATIVE set of valid <staff> targets: every BUILT building still
    # short of workers, by EXACT facility name, regardless of whether free workers exist yet. The
    # model must staff only names from here (kills both residual staff-error classes: invented
    # names copied from the example, and staffing already-full / not-yet-built buildings).
    need_staff = {}
    for f in obs.get("facilities", []):
        if f.get("status") in ("NeedWorker", "InUse"):
            rem = (f.get("needWorkers") or 0) - (f.get("workers") or 0)
            if rem > 0 and f.get("name"):
                need_staff[f["name"]] = rem
    hire_kinds, train_max, build_sites = set(), 0, set()
    staff_now, transfers = {}, set()
    for a in actions:
        t = a.get("action_type")
        # Type-specific fields are nested (worker/assignment/construction/transfer), mirroring
        # parse_commands and the *Action.to_dict() shapes — read them the same way here.
        if t == "worker":
            w = a.get("worker", {})
            wt = w.get("worker_action_type")
            if wt == "hire_untrained":   hire_kinds.add("untrained")
            elif wt == "hire_trained":   hire_kinds.add("trained")
            elif wt == "train_untrained": train_max = max(train_max, w.get("quantity", 0))
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
            transfers.add((tr.get("resource_type"), tr.get("source_facility"),
                           tr.get("destination_facility")))
    obs["available"] = {
        "hire": sorted(hire_kinds),                 # kinds you can hire (budget-permitting)
        "trainUntrainedMax": train_max,             # untrained workers you can train now
        "needStaff": need_staff,                    # {building: workforce still needed} — the ONLY valid <staff> targets
        "staffNow": staff_now,                      # subset assignable RIGHT NOW from current free workers
        "buildSites": sorted(build_sites),          # site ids you can build on
        "transfers": [{"resource": r, "from": s, "to": d}   # valid transfers (empty if no idle vehicle)
                      for (r, s, d) in sorted(transfers, key=lambda x: tuple(map(str, x)))],
    }
    return obs


def _fac_row(f, *, v2):
    """One schema-once facilities row: 'name type status workers/need food pop/cap site'."""
    st = f.get("status") or ("Passive" if v2 else "-")
    site = f.get("site")
    return (f"{f.get('name')} {f.get('type')} {st} "
            f"{f.get('workers',0)}/{f.get('needWorkers',0)} {f.get('food',0)} "
            f"{f.get('pop',0)}/{f.get('cap',0)} "
            f"{'-' if site is None or site < 0 else site}")


def _fac_dyn(f):
    """The dynamic (turn-to-turn mutable) fields of a facility — identity (name/type) excluded.
    Two facilities with equal _fac_dyn render an identical row, so a row is 'unchanged' iff these match."""
    return (f.get("status"), f.get("workers", 0), f.get("needWorkers", 0),
            f.get("food", 0), f.get("pop", 0), f.get("cap", 0))


def _num0(d, key):
    """Read a numeric field, treating a PRESENT-but-null value as 0.

    `dict.get(key, 0)` only substitutes when the key is ABSENT. These keys are always present
    — build_observation sets them from `wf.get("freeTrainedWorkers")` etc., which yields None
    whenever Unity omits the field — so the default never fired and officers were told
    `workers: freeTrained None freeUntrained None`. Observed live 2026-08-13 in a game where
    the engine simultaneously reported 5 free trained workers: the data existed, the render
    lost it, and the officer had to reason about a workforce of "None".
    """
    v = d.get(key)
    return 0 if v is None else v


def _render_scalars(obs):
    g = obs.get
    L = [f"day {g('day')} | budget {g('budget')} | satisfaction {g('satisfaction')} | "
         f"roundsLeft {g('roundsLeft')}"
         + (f" | motelDailyCost {obs['motelDailyCost']}" if obs.get("motelDailyCost") else "")]
    w = obs.get("workers", {})
    L.append(f"workers: freeTrained {_num0(w,'freeTrained')} freeUntrained {_num0(w,'freeUntrained')} "
             f"working {_num0(w,'working')} inTraining {_num0(w,'inTraining')}")
    L.append(f"logistics: vehiclesFree {_num0(obs.get('logistics',{}),'vehiclesFree')}")
    # Emit spend/costs only when non-empty. A bare "spend:" with nothing after it was printed
    # every turn — it reads as a section the model failed to receive rather than one that is
    # simply empty, and it costs tokens to say nothing.
    for label in ("spend", "costs"):
        body = " ".join(f"{k} {v}" for k, v in (obs.get(label) or {}).items())
        if body:
            L.append(f"{label}: {body}")
    return L


def _render_facilities(obs, *, v2):
    facs = obs.get("facilities", [])
    if not facs:
        return []
    L = ["facilities [name type status workers/need food pop/cap site]:"]
    L += ["  " + _fac_row(f, v2=v2) for f in facs]
    return L


# Enable stable, title-based task IDs (e.g. BUDGET_DAILY, FOOD_C01) instead of
# the drifting integer taskId Unity assigns each turn. Same title→token across
# ALL turns so the policy can learn "BUDGET_DAILY = free money" once, rather
# than re-discovering it every day when its integer id changes. Parser
# (cmd_parser.parse_commands) accepts either form. ON by default; set
# ARC_STABLE_TASK_TOKENS=0 to restore the legacy drifting-integer rendering
# (e.g. to keep an in-flight benchmark on its original numeric baseline).
_STABLE_TASK_TOKENS = os.environ.get("ARC_STABLE_TASK_TOKENS", "1").strip() == "1"


def _short_affects(a: str) -> str:
    """Compact facility-name suffix used by stable_task_token, e.g. Community01→C01,
    Shelter_0→S0, Motel→MOTEL, CaseworkSite_2→CS2. Deterministic across turns."""
    if not a:
        return "X"
    a = a.strip()
    up = a.upper()
    if up == "MOTEL":
        return "MOTEL"
    if up == "MAINTENANCE":
        return "MAINT"
    if up.startswith("COMMUNITY"):
        tail = a[len("Community"):]
        return f"C{tail}" if tail.isdigit() or tail == "" else f"C{tail.upper()}"
    if up.startswith("SHELTER"):
        tail = a[len("Shelter"):].lstrip("_")
        return f"S{tail}" if tail else "S"
    if up.startswith("KITCHEN"):
        tail = a[len("Kitchen"):].lstrip("_")
        return f"K{tail}" if tail else "K"
    if up.startswith("CASEWORKSITE"):
        tail = a[len("CaseworkSite"):].lstrip("_")
        return f"CS{tail}" if tail else "CS"
    if up.startswith("CASEWORK"):
        return "CASE"
    # unknown facility label → uppercase, alphanumeric-only
    return "".join(c for c in a.upper() if c.isalnum()) or "X"


def stable_task_token(t: dict) -> str:
    """Map (title, affects) → stable identifier that is constant across game days.
    Falls back to `TASK_<taskId>` for unknown titles so we never break the API."""
    title = (t.get("title") or "")
    affects = t.get("affects") or ""
    tl = title.lower()
    if "daily budget" in tl:
        return "BUDGET_DAILY"
    if "emergency budget" in tl:
        return "BUDGET_EMERGENCY"
    if "storm funding" in tl:
        return "FUND_STORM"
    if "training recommendation" in tl:
        return "TRAIN_REC"
    if "worker shortage" in tl:
        return "WORKER_ADVICE"
    if "workforce optimization" in tl:
        return "ALERT_WORKFORCE"
    if "flood alert" in tl:
        return "ALERT_FLOOD"
    if "food request from community" in tl:
        return f"FOOD_{_short_affects(affects)}"
    if "food request from shelter" in tl:
        return f"FOOD_{_short_affects(affects)}"
    if "population relocation" in tl:
        return f"RELOC_{_short_affects(affects)}"
    if "community emergency evacuation" in tl:
        return f"EVAC_{_short_affects(affects)}"
    if "casework request" in tl:
        return f"CASEWORK_{_short_affects(affects)}"
    if "vehicle repair" in tl:
        return "REPAIR_VEHICLE"
    if "shelter flood damage" in tl:
        return f"FLOOD_{_short_affects(affects)}"
    if "start of day report" in tl:
        return "REPORT_DAY"
    return f"TASK_{t.get('taskId', 'X')}"  # unknown title → back-compat


# TaskOfficer routing — mirrors the AUTHORITATIVE Unity hardcoding. Unity assigns
# each task a TaskOfficer at creation (per-generator hardcoding in
# FloodTaskGenerator / WorkerRequestSystem / WorkerTrainingSystem / ClientStayTracker,
# plus per-TaskData asset fields for budget/food) but does NOT serialize taskOfficer
# into game_state (TaskContext omits it), so it cannot be read back. We reconstruct
# the same routing from the task title. The returned strings are the exact TaskOfficer
# enum names — identical to the config `talkinghead_endpoint` values — so the router
# can hand each officer only its jurisdiction's tasks.
_TASK_OFFICER_DEFAULT = "DisasterOfficer"


def task_officer(t: dict) -> str:
    """Return the TaskOfficer enum name that owns task ``t``, mirroring the Unity
    hardcoded routing. Reads either the obs-shaped ``title`` or the raw ``taskTitle``.
    Unknown / global tasks fall back to DisasterOfficer (the Unity default)."""
    tl = (t.get("title") or t.get("taskTitle") or "").lower()
    # External Relationship: daily/emergency budget + external storm funding.
    if "budget" in tl or "funding" in tl:
        return "ExternalRelationship"
    # Workforce Service: training recommendations, worker-shortage advice, requests.
    if "training" in tl or "worker" in tl or "workforce" in tl:
        return "WorkforceService"
    # Food Mass Care: food requests from communities/shelters.
    if "food request" in tl:
        return "FoodMassCare"
    # Lodging Mass Care: sheltering — relocation, evacuation, casework, flood repairs.
    if ("flood" in tl or "relocation" in tl or "evacuation" in tl
            or "casework" in tl or "repair" in tl or "road blockage" in tl):
        return "LodgingMassCare"
    return _TASK_OFFICER_DEFAULT


# Coarse task-group slugs — one bucket per officer domain. Used for config-driven
# gating of choice-tasks in BOTH spaces: subaction_space entries
# {"category": "task_choice", "group": <slug>} and subobservation_space entries
# "tasks:<slug>". The coarse group is a 1:1 slug of the TaskOfficer that owns the
# task (task_officer above), so gating by group stays consistent with the
# authoritative Unity jurisdiction routing instead of introducing a second source
# of truth.
_OFFICER_TASK_GROUP = {
    "ExternalRelationship": "budget",
    "WorkforceService": "workforce",
    "FoodMassCare": "food",
    "LodgingMassCare": "lodging",
    "DisasterOfficer": "disaster",
}


def task_group(t: dict) -> str:
    """Return the coarse task-group slug that owns task ``t`` — one per officer
    domain: budget / workforce / food / lodging / disaster. A thin slug over
    task_officer, so group-gating matches the authoritative jurisdiction routing."""
    return _OFFICER_TASK_GROUP.get(task_officer(t), "disaster")


def _render_tasks(obs):
    tasks = obs.get("tasks", [])
    if not tasks:
        # Say "(none)" rather than omitting the section. Every other empty affordance already
        # announces itself ("needStaff: (none)", "staffNow: (none)"); a silently ABSENT tasks
        # block reads as missing information rather than as an empty one, and small models burn
        # real budget on it ("there's no active tasks? Or maybe the tasks are part of the next
        # step?" — qwen3:4b, observed in 5 consecutive rounds).
        return ["tasks: (none)"]
    L = ["tasks [id type \"title\" affects (roundsLeft)]:"]
    for t in tasks:
        tid = (t.get('token') or stable_task_token(t)) if _STABLE_TASK_TOKENS else t.get('taskId')
        L.append(f"  [{tid}] {t.get('type')} \"{t.get('title')}\" "
                 f"{t.get('affects','')} ({t.get('roundsLeft')} left)")
        for ch in t.get("choices", []):
            imp = ch.get("impacts")
            imps = (" " + " ".join(f"[{k} {v}]" for k, v in imp.items())) if imp else ""
            L.append(f"    {ch.get('choiceId')}: {ch.get('text')}{imps}")
    return L


def _render_sites(obs):
    sites = obs.get("sites", [])
    if not sites:
        return []
    return ["sites (ids): " + ",".join(str(s.get("id")) for s in sites)]


def _render_available(obs, *, v2):
    av = obs.get("available", {})
    if not av:
        return []
    L = ["available:"]
    if av.get("hire"):
        L.append(f"  hire: {','.join(av['hire'])} | trainUntrainedMax {av.get('trainUntrainedMax',0)}")
    else:
        L.append(f"  hire: none | trainUntrainedMax {av.get('trainUntrainedMax',0)}")
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
    elif not v2:
        # Plain `minimal` keeps the (often empty) transfers affordance line — the un-fixed control.
        L.append("  transfers: (none)")
    # minimal_v2 drops the empty line entirely: in task_only mode transfers are always empty
    # (relocation is via task choices, not <transfer>), so "(none)" is just a dead reference.
    return L


def render_state_compact(obs, *, v2=True):
    """Render the cmd observation dict as a COMPACT TEXT block instead of json.dumps(obs).
    Same information the policy acts on, but with the structural bloat removed (the four
    culprits found in the token breakdown), per the representation research:
      - facilities: schema-once tabular (legend line + one row each) — kills per-row key repetition.
      - tasks: drop the natural-language `desc` prose (distractor; restates title); compact choices.
      - sites: drop decorative names (build uses the id, which also lives in available.buildSites).
      - available.transfers: collapse the source x destination cross-product to per-resource endpoints.
      - scalars/affordances: key-value lines, decision-relevant fields up top (primacy).
    Lossless w.r.t. what's actionable; only prose and enumerated redundancy are dropped.
    Returns a string ready to drop into the user message in place of json.dumps(obs)."""
    return "\n".join(_render_scalars(obs) + _render_facilities(obs, v2=v2) + _render_tasks(obs)
                     + _render_sites(obs) + _render_available(obs, v2=v2))


def render_state_delta(obs, prev_obs, *, v2=True):
    """History-carrying DELTA rendering: identical to render_state_compact EXCEPT the facilities
    block is diffed against the previous turn (unchanged rows omitted; +new / ~changed shown in
    FULL, -removed by name). Everything actionable — scalars, tasks, the whole `available` block
    (needStaff/staffNow/buildSites/transfers) — stays FULL, so the policy never has to reconstruct
    valid actions from diffs (the error-prone aggregation case). The facilities table is the safe
    diff target: it is low-churn (built once, then persists) and its actionable content is already
    mirrored in available.needStaff/staffNow.

    Cache-safe: a turn's rendering is a pure function of (obs, prev_obs) — both fixed once produced —
    so the same turn serializes byte-identically every time it reappears in the append-only context,
    preserving the prefix KV-cache. prev_obs=None (first turn) falls back to the full compact render."""
    if not prev_obs:
        return render_state_compact(obs, v2=v2)
    facs = obs.get("facilities", []) or []
    prev_by = {f.get("name"): f for f in (prev_obs.get("facilities", []) or [])}
    cur_names = {f.get("name") for f in facs}
    rows = []
    for f in facs:
        pf = prev_by.get(f.get("name"))
        if pf is None:
            rows.append("  + " + _fac_row(f, v2=v2))                 # newly built
        elif _fac_dyn(f) != _fac_dyn(pf):
            rows.append("  ~ " + _fac_row(f, v2=v2))                 # dynamic fields changed
    rows += [f"  - {n}" for n in prev_by if n not in cur_names]     # removed/deconstructed
    fac_block = ["facilities Δ (vs last turn; unchanged omitted; +new ~changed -removed) "
                 "[name type status workers/need food pop/cap site]:"]
    fac_block += rows if rows else ["  (no change)"]
    return "\n".join(_render_scalars(obs) + fac_block + _render_tasks(obs)
                     + _render_sites(obs) + _render_available(obs, v2=v2))


# ─────────────────────────────────────────────────────────────────────────────
# ROUTER CONVENIENCE — the live router holds only a raw game_state dict (no env, no
# episode horizon). This builds the canonical obs and renders it in the SAME compact
# format the benchmark uses. roundsLeft is derived from the session horizon so the
# scalar line shows a real number rather than "roundsLeft None".
# ─────────────────────────────────────────────────────────────────────────────

def _rounds_left(game_state):
    """roundsLeft derived from the session horizon (None if unknown)."""
    si = (game_state or {}).get("sessionInfo", {})
    fd, cd = si.get("finalDay"), si.get("currentDay")
    return _num(fd) - _num(cd) if fd is not None and cd is not None else None


def _router_obs(game_state, *, new=True, v2=True):
    """Build the canonical observation dict for a router game_state, deriving
    roundsLeft from the session horizon. Shared by render_state_text and the
    granular section getters so all of them see one identical observation."""
    return build_observation(game_state, None, new=new, v2=v2,
                             rounds_left=_rounds_left(game_state))


def render_state_text(game_state, *, new=True, v2=True):
    """Render the router's game_state as the canonical compact text block.

    Drop-in replacement for the old threadbare ``state_text`` in
    ``llm_query._build_prompt``, now producing the exact same rendering the
    benchmark policy sees (``render_state_compact``)."""
    return render_state_compact(_router_obs(game_state, new=new, v2=v2), v2=v2) \
        or "(no observation available)"


# ─────────────────────────────────────────────────────────────────────────────
# GRANULAR SECTION GETTERS — one slice of the full observation each, so a
# continuous officer can pull just the detail it needs (facilities / workforce /
# tasks / logistics) instead of re-dumping the whole read_state every time. Each
# renders the SAME section of the SAME canonical obs render_state_text uses, so
# there is no second source of truth. Tasks inherit the stable-token rendering.
# ─────────────────────────────────────────────────────────────────────────────

def render_facilities_text(game_state, *, new=True, v2=True):
    """Just the facilities table (name/type/status/workers/food/pop)."""
    return "\n".join(_render_facilities(_router_obs(game_state, new=new, v2=v2), v2=v2)) \
        or "(no facilities built yet)"


def render_workforce_text(game_state, *, new=True, v2=True):
    """The scalar block: day/budget/satisfaction + workforce pool + spend/costs."""
    return "\n".join(_render_scalars(_router_obs(game_state, new=new, v2=v2))) \
        or "(no workforce data)"


def render_tasks_text(game_state, *, new=True, v2=True):
    """Just the active-tasks block (stable tokens + choices)."""
    return "\n".join(_render_tasks(_router_obs(game_state, new=new, v2=v2))) \
        or "(no active tasks)"


def render_logistics_text(game_state, actions, *, new=True, v2=True):
    """Sites + the `available` affordance block (hire/needStaff/staffNow/
    buildSites/transfers) — what the officer can act on right now. Derived from
    the enumerated ``actions`` (the same valid-action set the parser/menu use, so
    it can never contradict them); render_state's actions=None path leaves this
    block empty, which is why this getter takes the actions explicitly."""
    obs = build_observation_commands(game_state, actions or [], new=new, v2=v2,
                                     rounds_left=_rounds_left(game_state))
    return "\n".join(_render_sites(obs) + _render_available(obs, v2=v2)) \
        or "(no logistics/affordances available)"
