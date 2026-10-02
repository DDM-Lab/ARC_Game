"""Lockstep checking: replay a captured Unity game (oracle/sim/capture.py) into the surrogate.

    seed_state(log)       the RNG state the capture's first decision starts from
    rng_states(log)       Unity's RNG state at the start of every decision ([RNGCTX] round:advance)
    with_facility(step)   what Unity accepted at a decision, with each answer's facility, client
                          group and (for tasks built in code) the port's task id
    replay_step(w, step)  apply exactly that to the surrogate, before sim.step
    Session               a whole capture replayed: per-decision worlds and the first decision
                          whose RNG state parts from Unity's

obs_diff (field-by-field report), parity (fixtures) and the tests build on these.
"""
from __future__ import annotations

import json
import os
import re

from oracle.sim import sim
from oracle.sim.floodmap import FloodMap


def seed_state(log_path) -> tuple:
    """Unity's RNG state at the first round:advance: the state the first decision begins with."""
    rx = re.compile(r"\[RNGCTX\] \S+ round:advance (\{.*?\}) ")
    for line in open(log_path, errors="ignore"):
        m = rx.search(line)
        if m:
            st = json.loads(m.group(1))
            return tuple(st[k] & 0xFFFFFFFF for k in ("s0", "s1", "s2", "s3"))
    return None


def rng_states(log_path) -> dict:
    """{decision number (1-based gym step): Unity's RNG state at its round:advance}."""
    out = {}
    for line in open(log_path, errors="ignore"):
        m = re.search(r"\[RNGCTX\] s(\d+)\S* round:advance (\{.*?\})", line)
        if m:
            st = json.loads(m.group(2))
            out.setdefault(int(m.group(1)), [st[k] & 0xFFFFFFFF for k in ("s0", "s1", "s2", "s3")])
    return out


def with_facility(step) -> list:
    """`taken` with each task answer's facility, its client group (a casework task's
    |CLIENT_GROUP_ID:n) and, for a task built in code, the port's task id by title."""
    tasks = {t.get("taskId"): t for t in (step.get("before") or {}).get("allActiveTasks") or []}

    def fill(a):
        t = tasks.get(a.get("taskId")) or {}
        sid = a.get("stableTaskId") or sim.CODE_BUILT_TASKS.get(str(t.get("taskTitle")), "")
        out = dict(a, facility=a.get("facility", str(t.get("affectedFacility") or "")), stableTaskId=sid)
        desc = str(t.get("taskDescription") or "")
        if "|CLIENT_GROUP_ID:" in desc:
            out["group"] = int(desc.split("|CLIENT_GROUP_ID:", 1)[1])
        return out
    return [fill(a) if a.get("kind") == "choice" else a for a in step.get("taken") or []]


def replay_step(w, step):
    """Apply one tool-call capture step (oracle/sim/capture.py) to the port, before sim.step.

    Pure replay: the task answers Unity accepted, matched to the port's open tasks by task
    type in offer order, then the game actions Unity accepted, in the order they were sent.
    Nothing else -- a task the policy left alone stays unanswered (declining is a move), and
    nothing is staffed that the policy did not staff."""
    # Answers match the port's open tasks by (task type, facility): two open requests of one
    # type for different communities are different tasks.
    taken = with_facility(step) if step.get("before") else (step.get("taken") or [])
    import re as _re
    by_site = {(b["type"], b.get("site_id")): b["name"] for b in w.economy.buildings}

    def _port_name(fac):
        """Unity names a built building's GameObject <Type>_<site>; the port by display name."""
        m = _re.fullmatch(r"(\w+?)_(\d+)", fac or "")
        return by_site.get((m.group(1), int(m.group(2))), fac) if m else (fac or "")
    queues = {}
    for a in taken:
        if a.get("kind") == "choice":
            where = f"group:{a['group']}" if a.get("group") is not None else _port_name(a.get("facility"))
            queues.setdefault((a.get("stableTaskId") or "", where), []).append(a.get("choiceId"))
    offered = {}
    for tid, cid in sim.open_choices(w):
        offered.setdefault(tid, []).append(cid)
    for tid, cids in offered.items():
        spec = w.generated_specs.get(tid) or ("", "", {})
        key = "Repair" if tid in w.tasks.repair_for else spec[0]
        gid = (spec[2] or {}).get("_gid")
        q = queues.get((key, f"group:{gid}" if gid is not None else str(spec[1] or "")))
        if not q and not spec[1]:   # global tasks: Unity reports a facility type, the port none
            q = next((v for (k, _f), v in queues.items() if k == key and v), None)
        if q:
            want = q.pop(0)
            sim.answer(w, tid, want if want in cids else cids[0])
    for a in taken:
        if a.get("kind") not in ("menu", "staff") or not a.get("ok"):
            continue
        sim.apply_menu_action(w, a.get("payload") or {})


class Session:
    """One capture replayed: `trace` (Unity, per decision), `before`/`after` (the port's world
    at the start/end of each decision) and `rng_div`, the decision in which the RNG streams part
    (the one before the first decision whose starting state differs from Unity's; None when they
    agree throughout)."""

    def __init__(self, seed, capture_dir):
        path = os.path.join(str(capture_dir), f"staff_{seed}.json")
        self.seed = seed
        self.trace = json.load(open(path))
        log = path.replace(".json", ".log")
        unity = rng_states(log)
        w = sim.new_world(seed_state(log), FloodMap.load())
        self.before, self.after, self.rng_div = [], [], None
        for i, step in enumerate(self.trace):
            want = unity.get(i + 1)
            if self.rng_div is None and want is not None and list(w.rng.get_state()) != want:
                self.rng_div = max(0, i - 1)
            self.before.append(w.clone())
            replay_step(w, step)
            sim.step(w)
            self.after.append(w.clone())
