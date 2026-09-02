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

_CTX = re.compile(r"\[RNGCTX\] \S+ flood:enter (\{.*?\}) (\{.*\})\s*$")
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
        st, ctx = json.loads(m.group(1)), json.loads(m.group(2))
        rd = {"source": source,
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
                        "selected": json.loads(m.group(2))["weather"]})
            pending = None
    return out


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    out_path, logs = argv[1], argv[2:]
    rounds, weather = [], []
    for path in logs:
        tag = path.split("/")[-1]
        r, w = parse_log(path, tag), parse_weather(path, tag)
        print(f"  {tag}: {len(r)} flood rounds, {len(w)} weather draws")
        rounds += r
        weather += w
    payload = {"source": "headless ARC build, ARC_SNAPSHOT_DEBUG=1",
               "logs": [p.split("/")[-1] for p in logs],
               "rounds": rounds, "weather": weather}
    json.dump(payload, open(out_path, "w"))
    seen = sorted({r["weather"] for r in rounds})
    print(f"wrote {out_path}: {len(rounds)} rounds, {len(weather)} weather draws, "
          f"weathers exercised: {', '.join(seen)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
