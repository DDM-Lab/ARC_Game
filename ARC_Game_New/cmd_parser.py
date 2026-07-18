"""
Shared command-grammar parser for the ARC game.

The `<build>/<hire>/<train>/<staff>/<deconstruct>/<task>/<transfer>` command grammar
resolves natural intent → concrete action indices against a FRESH per-round action
enumeration (so it dodges stale-index failures). This is the ONE parser: the benchmark
(`llm_smoke_test.py` / `benchmark_models.py`), the RL harness, and the live agent router
all import `parse_commands` from here.

It is env-shaped but env-agnostic: it only needs an object exposing
  * `get_valid_actions()` -> list[action_dict]   (enumerated menu for the round)
  * `game_state`          -> dict                (raw game state)
  * `valid_actions`       -> list                (the underlying action list; <staff>
                                                  synthesizes a worker_assignment action
                                                  and appends it so it can be executed
                                                  by index this turn)
Both the gym env and a thin router-side shim satisfy this contract, so `parse_commands`
runs unmodified against either. When the cluster-unified parser lands it replaces this
file's body and every caller updates together.
"""
import re

# Aliases the model may type for each building type -> canonical enumerator building_type.
_BUILD_ALIASES = {
    "kitchen": "Kitchen", "kitchens": "Kitchen",
    "shelter": "Shelter", "shelters": "Shelter",
    "casework": "CaseworkSite", "caseworksite": "CaseworkSite",
    "caseworks": "CaseworkSite", "case": "CaseworkSite",
}

_TRANSFER_RESOURCE = {
    "food": "FoodPacks", "foodpacks": "FoodPacks", "foodpack": "FoodPacks", "packs": "FoodPacks",
    "people": "Population", "population": "Population", "pop": "Population", "persons": "Population",
}


def _action_index(env):
    """{action_type: [(index, action_dict), ...]} over the round's enumerated actions."""
    idx = {}
    for i, a in enumerate(env.get_valid_actions()):
        idx.setdefault(a.get("action_type"), []).append((i, a))
    return idx


def _bundle_indices(candidates, n):
    """Greedily cover quantity `n` using available (quantity -> index) bundles, largest first.
    Repeating an index re-executes that bundle (the env executes each listed index in order)."""
    by_q = sorted(candidates, key=lambda qi: -qi[0])   # [(qty, index), ...] desc
    out, remaining = [], n
    while remaining > 0:
        pick = next((qi for qi in by_q if qi[0] <= remaining), None)
        if pick is None:
            pick = by_q[-1] if by_q else None           # smallest bundle, if even that overshoots
            if pick is None:
                break
        out.append(pick[1]); remaining -= pick[0]
    return out


_CMD_RE = re.compile(r"<\s*(build|hire|train|staff|task|deconstruct|transfer)\s*>(.*?)<\s*[\\/]\s*\1\s*>",
                     re.I | re.S)


def parse_commands(text, env):
    """Map command tags in `text` to (action_indices, choices) against the round's enumeration.

    Returns dict: {actions:[idx...], choices:[{taskId,choiceId}...], parsed:[...], errors:[...]}.
    Pure w.r.t. the env beyond reading its enumerated actions, so it is unit-testable on a snapshot
    via a stub exposing get_valid_actions()/game_state.
    """
    idx = _action_index(env)
    choices, parsed, errors = [], [], []

    # Commonsense execution order: regardless of the order the model writes the tags, we execute
    # deconstruct -> build -> hire -> train -> staff -> transfer. This makes the obvious plan
    # "hire, then staff the workers you just hired" work in a single turn (the gym executes the
    # action list in order). emit() tags each resolved menu index with its category priority;
    # the final `actions` list is the indices sorted by that priority (stable within a category).
    _PRIO = {"deconstruct": 0, "build": 1, "hire": 2, "train": 3, "staff": 4, "transfer": 5}
    act_items = []  # (priority, action_index)

    def emit(cmd_name, *idxs):
        for i in idxs:
            act_items.append((_PRIO[cmd_name], i))

    # Simulated free-workforce pool, in WORKFORCE UNITS (trained=2, untrained=1), so a <staff>
    # issued the same turn as a <hire> can see the newly-hired workers. ActionExecutor creates
    # hired workers Free (immediately assignable), and TryAssignWorkersToBuilding pulls from the
    # global free pool, so this models execution faithfully. Staff is resolved AFTER the main
    # pass (see staff_cmds) once every <hire> has been counted, independent of textual order.
    gs = env.game_state
    _wf = gs.get("workforceState", {})
    sim_wf = (_wf.get("freeTrainedWorkers", 0) or 0) * 2 + (_wf.get("freeUntrainedWorkers", 0) or 0)
    # building -> remaining workforce need this turn (consumed as we staff, so two <staff> tags to
    # the same building don't both claim the full need).
    need = {}
    for f in gs.get("mapState", {}).get("facilities", []):
        if f.get("buildingStatus") in ("NeedWorker", "InUse"):
            rem = (f.get("requiredWorkforce", 4) or 0) - (f.get("assignedWorkforce", 0) or 0)
            if rem > 0 and f.get("facilityName"):
                need[f["facilityName"]] = rem
    staff_cmds = []  # deferred (raw_label, N) resolved after all hires are counted

    def split(body, n):
        parts = [p.strip() for p in body.replace("\n", " ").split(",")]
        return parts if len(parts) >= n else None

    for m in _CMD_RE.finditer(text or ""):
        cmd = m.group(1).lower()
        body = m.group(2).strip()
        try:
            if cmd == "build":
                p = split(body, 2)
                if not p:
                    errors.append(f"build: need TYPE,SITE_ID got '{body}'"); continue
                btype = _BUILD_ALIASES.get(p[0].lower().replace(" ", ""))
                site = int(float(p[1]))
                if not btype:
                    errors.append(f"build: unknown type '{p[0]}'"); continue
                hit = next((i for i, a in idx.get("construction", [])
                            if a["construction"]["building_type"] == btype
                            and int(a["construction"]["site_id"]) == site), None)
                if hit is None:
                    errors.append(f"build: no available {btype} at site {site}"); continue
                emit("build", hit); parsed.append(f"build {btype}@{site}")

            elif cmd == "hire":
                p = split(body, 2)
                if not p:
                    errors.append(f"hire: need KIND,N got '{body}'"); continue
                kind = p[0].lower()
                trained = kind in ("trained", "true", "t", "1", "yes")
                wat = "hire_trained" if trained else "hire_untrained"
                n = int(float(p[1]))
                cands = [(a["worker"]["quantity"], i) for i, a in idx.get("worker", [])
                         if a["worker"]["worker_action_type"] == wat]
                got = _bundle_indices(cands, n)
                if not got:
                    errors.append(f"hire: no {wat} bundles available"); continue
                # Count the workers actually hired into the simulated free pool so a same-turn
                # <staff> can assign them (trained=2 workforce units, untrained=1).
                qmap = {i: q for q, i in cands}
                hired = sum(qmap.get(i, 0) for i in got)
                sim_wf += hired * (2 if trained else 1)
                emit("hire", *got); parsed.append(f"hire {wat} x{n}->{len(got)}act")

            elif cmd == "train":
                p = split(body, 1)
                n = int(float(p[0])) if p else 0
                cands = [(a["worker"]["quantity"], i) for i, a in idx.get("worker", [])
                         if a["worker"]["worker_action_type"] == "train_untrained"]
                got = _bundle_indices(cands, n)
                if not got:
                    errors.append("train: no train bundles available"); continue
                emit("train", *got); parsed.append(f"train x{n}->{len(got)}act")

            elif cmd == "staff":
                p = split(body, 2)
                if not p:
                    errors.append(f"staff: need BUILDING,N got '{body}'"); continue
                # Defer: resolve after the whole text is scanned so workers hired THIS turn
                # (in any textual order) are counted into sim_wf before we assign them.
                staff_cmds.append((p[0], int(float(p[1]))))

            elif cmd == "deconstruct":
                bname = body.strip().lower()
                hit = next((i for i, a in idx.get("deconstruction", [])
                            if bname in a["deconstruction"]["building_name"].lower()), None)
                if hit is None:
                    errors.append(f"deconstruct: no building matching '{body}'"); continue
                emit("deconstruct", hit); parsed.append(f"deconstruct {body}")

            elif cmd == "task":
                p = split(body, 2)
                if not p:
                    errors.append(f"task: need TASK_ID,CHOICE_ID got '{body}'"); continue
                choices.append({"taskId": int(float(p[0])), "choiceId": int(float(p[1]))})
                parsed.append(f"task {p[0]}/{p[1]}")

            elif cmd == "transfer":
                # Manual resource transfer (only enumerated when the env runs with manual_transfers).
                # <transfer>RESOURCE,SOURCE,DEST,QTY</transfer> e.g. food,Community01,Motel,25
                p = split(body, 4)
                if not p:
                    errors.append(f"transfer: need RESOURCE,SOURCE,DEST,QTY got '{body}'"); continue
                rtype = _TRANSFER_RESOURCE.get(p[0].lower().replace(" ", ""))
                if not rtype:
                    errors.append(f"transfer: unknown resource '{p[0]}'"); continue
                src, dst = p[1].strip().lower(), p[2].strip().lower()
                qty = int(float(p[3]))
                cands = [(t["transfer"]["quantity"], i) for i, t in idx.get("resource_transfer", [])
                         if t["transfer"]["resource_type"] == rtype
                         and src in t["transfer"]["source_facility"].lower()
                         and dst in t["transfer"]["destination_facility"].lower()]
                if not cands:
                    errors.append(f"transfer: no {rtype} route {p[1]}->{p[2]} "
                                  f"(needs a free vehicle and a valid facility pair)"); continue
                # pick the offered quantity closest to the requested amount (ties -> larger)
                hit = min(cands, key=lambda qi: (abs(qi[0] - qty), -qi[0]))[1]
                emit("transfer", hit); parsed.append(f"transfer {rtype} {p[1]}->{p[2]} ~{qty}")
        except (ValueError, KeyError, IndexError) as e:
            errors.append(f"{cmd}: parse error '{body}' ({e})")

    # Resolve deferred <staff> now that every <hire> this turn is counted into sim_wf. We synthesize
    # the worker_assignment action directly (Unity's ExecuteAssignment only needs building_name +
    # quantity-in-workforce-units; it greedily pulls from the free pool and ignores worker_type) and
    # append it to env.valid_actions so the gym can execute it by index THIS turn. Capping quantity at
    # the available workforce guarantees TryAssignWorkersToBuilding (all-or-nothing) succeeds rather
    # than failing and aborting the rest of the turn's plan.
    for raw_label, n in staff_cmds:
        bname = raw_label.strip().lower()
        match = next((nm for nm in need if need[nm] > 0 and bname in nm.lower()), None)
        if match is None:
            errors.append(f"staff: '{raw_label}' is not staffable now "
                          f"(must be a built building still needing workers)")
            continue
        want = min(n if n > 0 else need[match], need[match], sim_wf)
        if want <= 0:
            errors.append(f"staff: no free workers for '{raw_label}' "
                          f"(hire workers this turn, or none are available)")
            continue
        synth = {"action_id": f"assign_{match}_{want}", "action_type": "worker_assignment",
                 "description": f"Assign workforce {want} to {match}", "cost": 0,
                 "assignment": {"building_name": match, "worker_type": "untrained", "quantity": want}}
        env.valid_actions.append(synth)
        emit("staff", len(env.valid_actions) - 1)
        sim_wf -= want
        need[match] -= want
        parsed.append(f"staff {match} wf{want}")

    actions = [i for _, i in sorted(act_items, key=lambda kv: kv[0])]
    return {"actions": actions, "choices": choices, "parsed": parsed, "errors": errors}
