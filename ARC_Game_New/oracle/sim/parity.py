"""Parity fixtures: a captured Unity game reduced to what the surrogate must reproduce.

A capture (oracle/sim/capture.py) is ~2 MB of raw state and log. A fixture keeps only:
  seed_state   the xorshift state at the first round:advance (the port's start)
  steps        per decision, what Unity accepted (`taken`, as replay_step reads it)
  rng          per decision, Unity's RNG state at the start of the decision
  expected     per decision, Unity's state projected to the canonical observation
               (obs_diff.project_unity: budget, satisfaction, score, facilities, workers,
               board, walks, counters)
so tests/test_surrogate_parity.py can replay every fixture in seconds with no Unity.

    python -m oracle.sim.parity oracle/sim/runs/v6_explore05 [more capture dirs ...]
        -> tests/fixtures/sim_parity/<policy>_<seed>.json.gz
"""
from __future__ import annotations

import glob
import gzip
import json
import os
import re
import sys

FIXTURES = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                        "tests", "fixtures", "sim_parity")


def _rng_states(log_path) -> dict:
    out = {}
    for line in open(log_path, errors="ignore"):
        m = re.search(r"\[RNGCTX\] s(\d+)\S* round:advance (\{.*?\})", line)
        if m:
            st = json.loads(m.group(2))
            out.setdefault(int(m.group(1)), [st[k] & 0xFFFFFFFF for k in ("s0", "s1", "s2", "s3")])
    return out


def _with_facility(step) -> list:
    """`taken` with each task answer's facility (captures before it was recorded)."""
    where = {t.get("taskId"): str(t.get("affectedFacility") or "")
             for t in (step.get("before") or {}).get("allActiveTasks") or []}
    return [dict(a, facility=a.get("facility", where.get(a.get("taskId"), ""))) if a.get("kind") == "choice" else a
            for a in step.get("taken") or []]


def make_fixture(capture_json: str) -> dict:
    from oracle.sim.obs_diff import project_unity
    from oracle.sim.test_replay_forward import seed_state
    log = capture_json.replace(".json", ".log")
    trace = json.load(open(capture_json))
    rng = _rng_states(log)
    meta_path = capture_json.replace(".json", ".meta.json")
    meta = json.load(open(meta_path)) if os.path.exists(meta_path) else {}
    return {"seed": meta.get("seed"), "source": meta.get("source"), "buildGUID": meta.get("buildGUID"),
            "seed_state": list(seed_state(log)),
            "steps": [{"taken": _with_facility(s)} for s in trace],
            "rng": [rng.get(i + 1) for i in range(len(trace))],
            "expected": [project_unity(s["after"]) for s in trace]}


def replay(fixture: dict):
    """Play a fixture on the port: (per-decision RNG states at decision start, projections)."""
    from oracle.sim.diag_lockstep import replay_step
    from oracle.sim.evolve import fresh_world
    from oracle.sim.floodmap import FloodMap
    from oracle.sim.obs_diff import project_port
    import oracle.sim.sim as S
    w = fresh_world(tuple(fixture["seed_state"]), FloodMap.load())
    states, got = [], []
    for step in fixture["steps"]:
        states.append(list(w.rng.get_state()))
        replay_step(w, step)
        S.step(w)
        got.append(project_port(w))
    return states, got


def _canonical(obs: dict) -> dict:
    """Projections compare as JSON would store them, with board/walk entries as tuples."""
    o = json.loads(json.dumps(obs))
    for k in ("board", "walks"):
        o[k] = [tuple(x) for x in o[k]]
    return o


def check(fixture: dict) -> list:
    """[(decision, what differs)] -- empty when the port reproduces the capture exactly."""
    from oracle.sim.obs_diff import diff
    states, got = replay(fixture)
    out = []
    for i, (u, st, p) in enumerate(zip(fixture["expected"], states, got)):
        want = fixture["rng"][i]
        if want is not None and list(want) != st:
            out.append((i, ["rng"]))
            break                                        # everything after is noise
        d = diff(_canonical(u), _canonical(p))
        if d:
            out.append((i, [f for f, _a, _b in d]))
    return out


def _label(source: str) -> str:
    """'policy:combined+explore(eps=0.05,seed=3)' -> 'combined-explore05'; 'noop' -> 'noop'."""
    m = re.match(r"policy:([\w-]+)(?:\+explore\(eps=([\d.]+))?", source)
    if not m:
        return re.sub(r"\W+", "-", source).strip("-") or "capture"
    eps = m.group(2)
    return m.group(1) + (f"-explore{round(float(eps) * 100):02d}" if eps else "")


def main(argv):
    os.makedirs(FIXTURES, exist_ok=True)
    for d in argv:
        for path in sorted(glob.glob(os.path.join(d, "staff_*.json"))):
            if path.endswith(".meta.json"):
                continue
            fx = make_fixture(path)
            name = f"{_label(fx.get('source') or '')}_{fx['seed']}.json.gz"
            with gzip.open(os.path.join(FIXTURES, name), "wt") as f:
                json.dump(fx, f, separators=(",", ":"))
            print(f"{name}: {len(fx['steps'])} decisions, {len(check(fx))} differing")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
