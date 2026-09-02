"""Flood port equivalence -- self-contained, from Unity's own instrumentation.

Each round Unity emits a [RNGCTX] flood:enter mark carrying the RNG state AND the exact
inputs the flood update reads (weather, rain intensity, the ordered tile set), followed by
the [RNGMARK] draw-site sequence for that round.

The port is run from those exact inputs and must emit the identical ORDERED sequence of
draw-site labels. Matching the label sequence proves the control flow matches: same
guards, same early returns, same number of draws at each site -- which is the property the
rest of the simulator depends on, since flood sets the stream position for everything
after it.

Earlier versions of this test paired the marks with a separate between-rounds snapshot and
had to guess which weather and tile set the mechanic saw. It guessed wrong. Hence RNGCTX.
"""
from __future__ import annotations

import json, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.floodmap import FloodMap, pack
from cora_sim.flood import FloodState, update_flood
from cora_sim.rng import UnityRandom

M32 = 0xFFFFFFFF


def parse(log_path):
    """-> [(rng_state, context, [draw labels])] one entry per flood update."""
    out, cur, ctx, state = [], None, None, None
    for line in open(log_path, errors="ignore"):
        m = re.search(r"\[RNGCTX\] \S+ flood:enter (\{.*?\}) (\{.*\})\s*$", line)
        if m:
            if cur is not None:
                out.append((state, ctx, cur))
            st = json.loads(m.group(1))
            state = tuple(st[k] & M32 for k in ("s0", "s1", "s2", "s3"))
            ctx = json.loads(m.group(2))
            cur = []
            continue
        m2 = re.search(r"\[RNGMARK\] \S+ (draw:Flood\.\d+)", line)
        if m2 and cur is not None:
            cur.append(m2.group(1))
    if cur is not None:
        out.append((state, ctx, cur))
    return out


def main(log_path):
    fmap = FloodMap.load()
    rounds = parse(log_path)
    print(f"flood equivalence vs Unity   {len(rounds)} flood updates\n")
    print(f"{'#':<4} {'weather':<11} {'in':>4} {'unity draws':>12} {'port draws':>11} {'match':>7}  divergence")
    ok = True
    for i, (state, ctx, labels) in enumerate(rounds):
        tiles = {pack(x, y) for x, y in ctx["tiles"]}
        # lastWeather comes from the mark, NOT assumed equal to the current weather. An
        # earlier version defaulted it to the current weather, which forced
        # weather_changed=False and made the port skip the rain-spawn branch entirely --
        # a bug in the test that looked exactly like a bug in the port.
        fs = FloodState(tiles, ctx.get("lastWeather", ctx["weather"]))
        r = UnityRandom(state=state)
        marks = []
        update_flood(fs, fmap, r, ctx["weather"], ctx["rain"], marks)
        same = marks == labels
        ok &= same
        div = ""
        if not same:
            for j, (a, b) in enumerate(zip(labels, marks)):
                if a != b:
                    div = f"idx {j}: unity={a} port={b}"
                    break
            else:
                div = f"length {len(labels)} vs {len(marks)}"
        print(f"{i:<4} {ctx['weather']:<11} {len(tiles):>4} {len(labels):>12} "
              f"{len(marks):>11} {str(same):>7}  {div}")
    print("\nRESULT:", "PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
