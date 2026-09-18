"""Weather-selection equivalence, and the reversed-range finding that came out of it.

Each fixture row is a Weather.select draw: the RNG state immediately before it, and the
weather the simulation went on to use. Fifteen rows across three episodes.

WHAT THIS TEST IS REALLY GUARDING. Unity's Random.Range(float, float) is
`min * t + (1 - t) * max` -- a lerp with t running backwards, so t = 0 gives max. The
natural `min + (max - min) * t` produces the mirror-image number from the same draw and
still agrees near the middle of the range, which is exactly the region a small fixture
samples most. Here it scores 5/15 while the reversed form scores 15/15, and the rows it
gets wrong are the tails: Sunny and Storm swap.

The same reversal governs `Random.Range(0f, 1f) < p`, which is how every ProbabilityTrigger
in the game rolls -- same marginal probability, different outcome on the same draw. So this
test asserts BOTH that the shipped form matches and that the forward form does not.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.rng import UnityRandom, f32, f32add     # noqa: E402
from cora_sim.weather import TABLE, generate_weather  # noqa: E402

_FIXTURE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "corpus", "flood_rounds.json")


def _rng(row):
    st = row["rng"]
    return UnityRandom(state=(st["s0"], st["s1"], st["s2"], st["s3"]))


def _forward_select(rng, table):
    """The plausible-but-wrong form, kept so the test can prove it is rejected."""
    total = f32add(*[p for _, p in table])
    t = f32((rng.next_uint() & 0x7FFFFF) / 8388607.0)
    value = f32(0.0 + f32(total * t))
    cumulative = 0.0
    for name, p in table:
        cumulative = f32add(cumulative, p)
        if value <= cumulative:
            return name
    return "Sunny"


def main():
    rows = json.load(open(_FIXTURE)).get("weather", [])
    if not rows:
        print("no weather rows in fixture -- rebuild it with cora_sim/build_corpus.py")
        return 1

    truth = [r["selected"] for r in rows]
    got = [generate_weather(_rng(r)) for r in rows]
    fwd = [_forward_select(_rng(r), TABLE) for r in rows]

    hits = sum(a == b for a, b in zip(got, truth))
    fwd_hits = sum(a == b for a, b in zip(fwd, truth))
    seen = sorted(set(truth))

    print("cora_sim.weather equivalence vs Unity")
    print(f"  reversed RangedRandom   : {hits}/{len(rows)} selections match")
    print(f"  forward lerp (rejected) : {fwd_hits}/{len(rows)}")
    print(f"  weathers observed       : {', '.join(seen)}")
    missing = [w for w, _ in TABLE if w not in seen]
    if missing:
        print(f"  NOT YET OBSERVED        : {', '.join(missing)} -- selection of these is "
              f"untested; widen the capture before relying on them")

    ok = hits == len(rows)
    if not ok:
        for r, g, t in zip(rows, got, truth):
            if g != t:
                print(f"    {r['source']}: unity={t} port={g}")
    if ok and fwd_hits == len(rows):
        print("  forward lerp ALSO matches - this fixture no longer discriminates")
        ok = False
    print("\nRESULT:", "ALL PASS" if ok else "FAILURES PRESENT")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
