"""Rebuild the equivalence fixtures from headless [RNGMARK]/[RNGCTX] logs.

    ARC_SNAPSHOT_DEBUG=1 <run the headless build>          # produces the log
    python cora_sim/build_corpus.py corpus/flood_rounds.json cap_555.log cap_777.log ...

The fixtures are GROUND TRUTH, so they are rebuilt deliberately and reviewed in the diff,
never regenerated as a side effect of a test run. Regenerate after any change to
FloodSystem.cs, WeatherSystem.cs, or the scene's serialized parameters -- a stale fixture
turns a real divergence into a passing test.

Each round records the RNG state at flood entry, the exact inputs UpdateFlood saw, its
draw-site sequence, the counts Unity printed, and the non-flood draws that follow it before
the next round (which is what lets the census prove no draw site is missing).
"""
import json
import re
import sys

_CTX = re.compile(r"\[RNGCTX\] d(\d+)r(\d+) flood:enter (\{.*?\}) (\{.*\})\s*$")
_MARK = re.compile(r"\[RNGMARK\] \S+ (\S+)(?: (\{.*\}))?")
_COUNTS = ((re.compile(r"Spawned flood at (\d+)/"), "spawned"),
           (re.compile(r"Expansion candidates: \d+ -> (\d+)"), "cands"),
           (re.compile(r"Actual expansions: (\d+)/"), "expansions"),
           (re.compile(r"Flood shrinkage: Removed (\d+) tiles"), "removed"),
           (re.compile(r"Flood tiles before: \d+, after: (\d+),"), "after"))
_M32 = 0xFFFFFFFF


def parse_log(path, source):
    lines = open(path, errors="ignore").read().split("\n")
    enters = [i for i, L in enumerate(lines) if "[RNGCTX]" in L and "flood:enter" in L]
    rounds = []
    for k, a in enumerate(enters):
        b = enters[k + 1] if k + 1 < len(enters) else len(lines)
        m = _CTX.search(lines[a])
        st, ctx = json.loads(m.group(3)), json.loads(m.group(4))
        rd = {"source": source,
              # day/segment come from the mark tag, and the chaining test needs them: the
              # generation schedule depends on the segment, not on the round index.
              "day": int(m.group(1)), "segment": int(m.group(2)),
              "rng": {w: st[w] & _M32 for w in ("s0", "s1", "s2", "s3")},
              "weather": ctx["weather"], "lastWeather": ctx["lastWeather"],
              "rain": ctx["rain"], "tiles": ctx["tiles"],
              "marks": [], "unity": {}, "interRoundDraws": []}
        seen_non_flood = False
        for j in range(a + 1, b):
            mm = _MARK.search(lines[j])
            if mm and mm.group(1).startswith("draw:"):
                label = mm.group(1)
                if label.startswith("draw:Flood.") and not seen_non_flood:
                    rd["marks"].append(label)
                else:
                    seen_non_flood = True
                    rd["interRoundDraws"].append(label)
                continue
            for rx, key in _COUNTS:
                c = rx.search(lines[j])
                if c:
                    rd["unity"][key] = int(c.group(1))
        rounds.append(rd)
    return rounds


def parse_weather(path, source):
    """(pre-draw RNG state, weather that the next round then reports) pairs.

    The outcome is read from the FOLLOWING flood:enter rather than from a log line, so it
    is the weather the simulation actually went on to use, not a print of an intermediate."""
    out, pending = [], None
    for L in open(path, errors="ignore"):
        m = _MARK.search(L)
        if m and m.group(1) == "draw:Weather.select" and m.group(2):
            pending = json.loads(m.group(2))
            continue
        m = _CTX.search(L)
        if m and pending is not None:
            out.append({"source": source,
                        "rng": {w: pending[w] & _M32 for w in ("s0", "s1", "s2", "s3")},
                        "selected": json.loads(m.group(4))["weather"]})
            pending = None
    return out


_PASS = "Checking for triggered tasks per facility"
_SUITABLE = re.compile(r"Found (\d+) suitable facilities for ([^:]+): (.*)$")
_FIRED = re.compile(r"Task triggered for (\w+): (\w+)")
_PROB = re.compile(r"\[RNGMARK\] \S+ draw:TaskTrigger\.probability (\{.*\})")


def parse_triggers(path, source):
    """One row per task-generation pass.

    Records the pre-draw RNG state of every ProbabilityTrigger roll in the pass, the
    facility ORDER Unity used (from its own "Found N suitable facilities" line -- the port
    must reproduce FindObjectsOfType order, which is not sorted and not creation order),
    and which task-facility pairs fired.

    A firing is a ONE-DIRECTIONAL fact: these tasks use requireAllTriggers, so a fired task
    proves its probability roll passed, while a non-firing proves nothing (another trigger
    may have vetoed it). That is still enough to pin the reversed Range(0f,1f), because the
    forward form predicts the opposite on every one of them."""
    lines = open(path, errors="ignore").read().split("\n")
    starts = [i for i, L in enumerate(lines) if _PASS in L]
    rows = []
    for a, b in zip(starts, starts[1:] + [len(lines)]):
        states, order, fired = [], {}, []
        for L in lines[a:b]:
            m = _PROB.search(L)
            if m:
                st = json.loads(m.group(1))
                states.append({w: st[w] & _M32 for w in ("s0", "s1", "s2", "s3")})
                continue
            m = _SUITABLE.search(L)
            if m:
                order[m.group(2).strip()] = [f.strip() for f in m.group(3).split(",") if f.strip()]
                continue
            m = _FIRED.search(L)
            if m:
                fired.append([m.group(1), m.group(2)])
        rows.append({"source": source, "draws": states,
                     "facilityOrder": order, "fired": fired})
    return rows


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    out_path, logs = argv[1], argv[2:]
    rounds, weather, triggers = [], [], []
    for path in logs:
        tag = path.split("/")[-1]
        r, w, t = parse_log(path, tag), parse_weather(path, tag), parse_triggers(path, tag)
        print(f"  {tag}: {len(r)} flood rounds, {len(w)} weather draws, "
              f"{len(t)} task-generation passes")
        rounds += r
        weather += w
        triggers += t
    payload = {"source": "headless ARC build, ARC_SNAPSHOT_DEBUG=1",
               "logs": [p.split("/")[-1] for p in logs],
               "rounds": rounds, "weather": weather, "triggers": triggers}
    json.dump(payload, open(out_path, "w"))
    seen = sorted({r["weather"] for r in rounds})
    print(f"wrote {out_path}: {len(rounds)} rounds, {len(weather)} weather draws, "
          f"weathers exercised: {', '.join(seen)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
