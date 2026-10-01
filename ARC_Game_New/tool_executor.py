"""Execute a model's typed tool calls against the game — no text round-trip.

THE BASIC-BENCHMARK CONTRACT
One turn = one model call. The model sees the system prompt, the tool descriptions and the
current observation, may reason, then emits any number of tool calls (zero = do nothing this
round). The tools are the game actions only, and they return NOTHING to the model: the next
turn's observation is the only feedback. The harness still records, per call, what happened —
for logs, metrics and later feedback variants — as a CallResult.

TOOLS (the canonical schema lives in cora_tools; this module resolves and runs them)
  build(type, site_id)    type kitchen|shelter|casework; site_id must be a free site offered for
                          that type this turn.                       -> one construction action
  hire(kind, count)       kind trained|untrained; count is covered with the hire bundles the game
                          offers (largest first).                   -> one or more worker actions
  train(count)            untrained -> trained, same bundle covering; trainees leave the free pool
                          until they finish.                        -> one or more worker actions
  staff(site, count?)     assign free workforce, in WORKFORCE UNITS (trained 2, untrained 1), to a
                          built facility that still needs workers. count omitted = staff fully;
                          a partial count is refused (buildings only run fully staffed). Workers
                          are drawn trained-first from what earlier calls this turn left.
                                                                    -> one worker_assignment action
  deconstruct(site)       tear down a built facility (case-insensitive substring of its name).
                                                                    -> one deconstruction action
  task(task_id, choice_id) answer an active task; task_id is the bracketed token from the
                          observation (or the raw integer id).      -> one task choice
  transfer(resource, source, dest, qty)  only when the env enumerates manual transfers.

ORDER
Calls are resolved and run in a fixed order regardless of how the model wrote them — task answers,
then deconstruct, build, hire, train, staff, transfer — so "hire, then staff those workers" works in
one turn. (Task answers go first because that is what the benchmark has always done: they are
committed before env.step advances time.) Within a kind, the model's order is kept.

OUTCOMES (CallResult.status)
  executed  the game carried it out
  refused   it resolved, but the game said no (e.g. "No meals available across any kitchen")
  invalid   it could not be resolved this turn (unknown tool, bad arguments, no such site/task,
            not enough free workers) — nothing was sent to the game
"""
from __future__ import annotations

import json
from dataclasses import dataclass, field
from typing import Any, Iterable, Optional

ORDER = {"task": 0, "deconstruct": 1, "build": 2, "hire": 3, "train": 4, "staff": 5, "transfer": 6}

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


@dataclass
class CallResult:
    call_id: Optional[str]
    tool: str
    args: dict
    status: str = "invalid"            # executed | refused | invalid
    reason: str = ""                   # why it was refused / invalid ("" when executed)
    summary: str = ""                  # what it resolved to, e.g. "build Kitchen at site 3"
    action_indices: list = field(default_factory=list)   # indices into the turn's action list
    choice: Optional[dict] = None      # {"taskId", "choiceId"} for task calls
    malformed: bool = False            # unknown tool or unreadable arguments (a format error,
                                       # as opposed to a well-formed call that cannot apply now)

    def as_dict(self) -> dict:
        return {"id": self.call_id, "tool": self.tool, "args": self.args, "status": self.status,
                "reason": self.reason, "summary": self.summary}


def _bundle_indices(candidates, n):
    """Cover quantity n with offered (qty, index) bundles, largest first; a repeated index runs
    that bundle again. Falls back to the smallest bundle when even it overshoots."""
    by_q = sorted(candidates, key=lambda qi: -qi[0])
    out, remaining = [], n
    while remaining > 0:
        pick = next((qi for qi in by_q if qi[0] <= remaining), None)
        if pick is None:
            pick = by_q[-1] if by_q else None
            if pick is None:
                break
        out.append(pick[1]); remaining -= pick[0]
    return out


def _int(v, what):
    try:
        return int(float(v))
    except (TypeError, ValueError):
        raise ValueError(f"{what} must be a number, got {v!r}")


class TurnResolver:
    """Resolves one turn's calls against a snapshot of the game, tracking what earlier calls this
    turn will use up (workers hired, trained or assigned; workforce still needed per building).

    `env` needs `game_state` and `get_valid_actions()`; resolving appends synthesized
    worker_assignment actions to `self.actions` (a private copy of the menu), so callers must run
    the returned indices against `self.actions`, not their own list.
    """

    def __init__(self, env):
        self.gs = getattr(env, "game_state", None) or {}
        self.actions = list(env.get_valid_actions() or [])
        self.by_type = {}
        for i, a in enumerate(self.actions):
            self.by_type.setdefault(a.get("action_type"), []).append((i, a))
        wf = self.gs.get("workforceState", {}) or {}
        self.free_tr = wf.get("freeTrainedWorkers", 0) or 0
        self.free_un = wf.get("freeUntrainedWorkers", 0) or 0
        self.sites_taken = set()            # build sites claimed by earlier calls this turn
        self.need = {}
        for f in (self.gs.get("mapState", {}) or {}).get("facilities", []) or []:
            if f.get("buildingStatus") in ("NeedWorker", "InUse"):
                rem = (f.get("requiredWorkforce", 4) or 0) - (f.get("assignedWorkforce", 0) or 0)
                if rem > 0 and f.get("facilityName"):
                    self.need[f["facilityName"]] = rem

    @property
    def free_units(self):
        return self.free_tr * 2 + self.free_un

    # ── per-tool resolvers: fill r.action_indices / r.choice + r.summary, or raise ValueError ──
    def build(self, r, type, site_id):
        btype = _BUILD_ALIASES.get(str(type).lower().replace(" ", ""))
        if not btype:
            raise ValueError(f"unknown building type {type!r} (kitchen, shelter or casework)")
        site = _int(site_id, "site_id")
        hit = next((i for i, a in self.by_type.get("construction", [])
                    if a["construction"]["building_type"] == btype
                    and int(a["construction"]["site_id"]) == site), None)
        if hit is None:
            raise ValueError(f"site {site} is not available to build a {btype} this turn")
        if site in self.sites_taken:
            raise ValueError(f"site {site} is already being built on by an earlier call this turn")
        self.sites_taken.add(site)
        r.action_indices = [hit]; r.summary = f"build {btype} at site {site}"

    def hire(self, r, kind, count):
        trained = str(kind).lower() in ("trained", "true", "t", "1", "yes")
        wat = "hire_trained" if trained else "hire_untrained"
        n = _int(count, "count")
        if n <= 0:
            raise ValueError("count must be at least 1")
        cands = [(a["worker"]["quantity"], i) for i, a in self.by_type.get("worker", [])
                 if a["worker"]["worker_action_type"] == wat]
        got = _bundle_indices(cands, n)
        if not got:
            raise ValueError(f"no {wat.replace('_', ' ')} offers this turn")
        hired = sum({i: q for q, i in cands}[i] for i in got)
        if trained:
            self.free_tr += hired
        else:
            self.free_un += hired
        r.action_indices = got; r.summary = f"{wat} {hired}"

    def train(self, r, count):
        n = _int(count, "count")
        if n <= 0:
            raise ValueError("count must be at least 1")
        cands = [(a["worker"]["quantity"], i) for i, a in self.by_type.get("worker", [])
                 if a["worker"]["worker_action_type"] == "train_untrained"]
        got = _bundle_indices(cands, n)
        if not got:
            raise ValueError("no training offers this turn")
        trainees = min(self.free_un, sum({i: q for q, i in cands}[i] for i in got))
        self.free_un -= trainees            # in training: not assignable until they finish
        r.action_indices = got; r.summary = f"train {trainees}"

    def staff(self, r, site, count=None):
        label = str(site or "").strip().lower()
        match = next((nm for nm in self.need if self.need[nm] > 0 and label and label in nm.lower()), None)
        if match is None:
            raise ValueError(f"{site!r} is not a built facility that still needs workers")
        need = self.need[match]
        n = _int(count, "count") if count not in (None, "") else 0
        if 0 < n < need:
            raise ValueError(f"{match} needs {need} workforce units and only runs fully staffed; "
                             f"count={n} would be partial")
        if self.free_units < need:
            raise ValueError(f"{match} needs {need} workforce units but only {self.free_units} are free "
                             f"({self.free_tr} trained, {self.free_un} untrained)")
        # Worker COUNT for the executor, which assigns trained-first from the free pool.
        use_tr = min(self.free_tr, -(-need // 2))
        use_un = max(0, need - use_tr * 2)
        if use_un > self.free_un:
            raise ValueError(f"{match} needs {need} workforce units; {self.free_tr} trained and "
                             f"{self.free_un} untrained free cannot make that up")
        qty = use_tr + use_un
        self.actions.append({"action_id": f"assign_{match}_{qty}", "action_type": "worker_assignment",
                             "description": f"Assign {qty} worker(s) ({need} workforce) to {match}",
                             "cost": 0, "assignment": {"building_name": match, "quantity": qty}})
        self.free_tr -= use_tr; self.free_un -= use_un; self.need[match] = 0
        r.action_indices = [len(self.actions) - 1]; r.summary = f"staff {match} ({need} units)"

    def deconstruct(self, r, site):
        label = str(site or "").strip().lower()
        hit = next((i for i, a in self.by_type.get("deconstruction", [])
                    if label and label in a["deconstruction"]["building_name"].lower()), None)
        if hit is None:
            raise ValueError(f"no built facility matching {site!r}")
        r.action_indices = [hit]; r.summary = f"deconstruct {self.actions[hit]['deconstruction']['building_name']}"

    def task(self, r, task_id, choice_id):
        raw = str(task_id).strip().strip("[]")
        tasks = self.gs.get("allActiveTasks", []) or []
        tid = None
        try:
            tid = int(float(raw))
        except ValueError:
            from cora.observation import task_token
            for t in tasks:
                if task_token(t) == raw:
                    tid = int(t["taskId"]); break
        if tid is None or not any(int(t.get("taskId", -1)) == tid for t in tasks):
            raise ValueError(f"no active task {raw!r} this turn")
        r.choice = {"taskId": tid, "choiceId": _int(choice_id, "choice_id")}
        r.summary = f"task {raw} choice {r.choice['choiceId']}"

    def transfer(self, r, resource, source, dest, qty):
        rtype = _TRANSFER_RESOURCE.get(str(resource).lower().replace(" ", ""))
        if not rtype:
            raise ValueError(f"unknown resource {resource!r} (food or people)")
        src, dst, q = str(source).lower(), str(dest).lower(), _int(qty, "qty")
        cands = [(t["transfer"]["quantity"], i) for i, t in self.by_type.get("resource_transfer", [])
                 if t["transfer"]["resource_type"] == rtype and src in t["transfer"]["source_facility"].lower()
                 and dst in t["transfer"]["destination_facility"].lower()]
        if not cands:
            raise ValueError(f"no {rtype} transfer {source}->{dest} this turn (needs a free vehicle)")
        hit = min(cands, key=lambda qi: (abs(qi[0] - q), -qi[0]))[1]
        r.action_indices = [hit]; r.summary = f"transfer {rtype} {source}->{dest} ~{q}"

    def resolve(self, calls: Iterable[Any]) -> list:
        """Resolve all calls (in ORDER); returns CallResults in the model's original order."""
        results = []
        for k, call in enumerate(calls or []):
            cid, name, raw = _unpack(call, k)
            results.append((k, CallResult(cid, name or "", {})))
            r = results[-1][1]
            if name not in ORDER:
                r.reason, r.malformed = f"unknown tool {name!r}", True
                continue
            try:
                r.args = json.loads(raw) if isinstance(raw, str) else dict(raw or {})
                if not isinstance(r.args, dict):
                    raise ValueError
            except (ValueError, TypeError):
                r.args, r.reason, r.malformed = {}, "arguments are not a JSON object", True
                continue
        for k, r in sorted(results, key=lambda kr: (ORDER.get(kr[1].tool, 99), kr[0])):
            if r.reason:
                continue
            try:
                getattr(self, r.tool)(r, **r.args)
                r.status = "resolved"
            except TypeError as e:
                r.reason, r.malformed = f"bad arguments for {r.tool}: {e}", True
            except (ValueError, KeyError, IndexError) as e:
                r.reason = str(e)
        return [r for _, r in results]


def _unpack(call, k):
    """(id, name, arguments) from an OpenAI tool_call object, a dict, or a (name, args) tuple."""
    fn = getattr(call, "function", None)
    if fn is not None:
        return getattr(call, "id", None) or f"call_{k}", fn.name, fn.arguments
    if isinstance(call, dict):
        f = call.get("function") or call
        return call.get("id") or f"call_{k}", f.get("name"), f.get("arguments", f.get("args"))
    name, args = call
    return f"call_{k}", name, args


def plan_turn(calls, env):
    """Resolve without executing: (results, resolver). Used by execute_turn and by the command-tag
    front end (cmd_parser), which hands the resolved indices to its own caller."""
    tr = TurnResolver(env)
    return tr.resolve(calls), tr


def execute_turn(env, calls):
    """Resolve and run one turn's tool calls, then advance the game one round.

    Returns (results, (obs, reward, terminated, truncated, info)). Nothing here is shown to the
    model; the results are for logs and metrics.
    """
    results, tr = plan_turn(calls, env)
    return _run(env, results, tr.actions)


# Action-list entries -> the tool that would have produced them (for a non-LLM policy's decision).
def _tool_for(action):
    t = action.get("action_type")
    if t == "worker":
        return "train" if (action.get("worker") or {}).get("worker_action_type") == "train_untrained" else "hire"
    return {"construction": "build", "worker_assignment": "staff", "deconstruction": "deconstruct",
            "resource_transfer": "transfer"}.get(t, t or "action")


def execute_indices(env, choices, indices):
    """Run a non-LLM policy's decision — task choices plus indices into env.get_valid_actions() —
    through the same execution and bookkeeping as tool calls, so baselines and models are logged
    and scored identically."""
    actions = list(env.get_valid_actions() or [])
    results = []
    for k, c in enumerate(choices or []):
        ch = {"taskId": int(c["taskId"]), "choiceId": int(c["choiceId"])}
        results.append(CallResult(f"choice_{k}", "task", dict(ch), status="resolved", choice=ch,
                                  summary=f"task {ch['taskId']} choice {ch['choiceId']}"))
    for k, i in enumerate(indices or []):
        i = int(i)
        if not 0 <= i < len(actions):
            results.append(CallResult(f"action_{k}", "action", {"index": i},
                                      reason=f"action index {i} is out of range"))
            continue
        results.append(CallResult(f"action_{k}", _tool_for(actions[i]), {"index": i}, status="resolved",
                                  action_indices=[i], summary=actions[i].get("description", "")))
    return _run(env, results, actions)


def _run(env, results, actions):
    """Commit task answers (keeping the game's refusal reason), then run the game actions in
    ORDER with env.step — which also simulates the round — and record each call's outcome."""
    ordered = sorted([r for r in results if r.status == "resolved"], key=lambda r: ORDER.get(r.tool, 99))
    for r in ordered:
        if r.choice is None:
            continue
        try:
            ok = bool(env.select_task_choice(r.choice["taskId"], r.choice["choiceId"]))
        except Exception as e:          # a dead socket is a harness failure, not a game refusal
            r.status, r.reason = "invalid", f"select_task_choice raised: {e}"
            continue
        r.status = "executed" if ok else "refused"
        if not ok:
            r.reason = getattr(env, "last_choice_error", None) or "refused"
    # env.step indexes env.valid_actions; it must see the resolver's list, which holds the
    # synthesized staff actions.
    env.valid_actions = actions
    owners = [(r, i) for r in ordered if r.choice is None for i in r.action_indices]
    step = env.step(",".join(str(i) for _, i in owners))
    exres = (step[4] or {}).get("execution_results") or []
    for r in ordered:
        if r.choice is None:
            r.status = "executed"
    # One execution result per index, in order; a failed index marks its call refused. If the
    # game stopped early, the indices it never reached have no result: those calls did not run.
    for k, (r, _) in enumerate(owners):
        res = exres[k] if k < len(exres) else None
        if res is None:
            r.status, r.reason = "refused", r.reason or "not run: the game stopped after an earlier failure"
        elif not res.get("success"):
            r.status = "refused"
            r.reason = res.get("error") or res.get("error_message") or "refused"
    return results, step
