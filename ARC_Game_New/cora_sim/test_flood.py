"""Flood-port equivalence against Unity, at three levels of strictness.

WHY THREE LEVELS. A port can match the final tile count while taking a different path to
it, and it can match the draw COUNT while drawing at the wrong sites. Only the mark
sequence pins the RNG stream position, and only the stream position keeps every mechanic
that draws AFTER flood in sync. So:

  level 1  mark sequence        every draw site, in order         -- pins the RNG stream
  level 2  per-phase counts     spawned / candidates / expansions / removed
  level 3  resulting tile set   compared against the NEXT round's captured input

Level 3 is the one that would catch a port that draws correctly but writes the wrong tile,
and it is free: consecutive rounds come from one episode, so round k's output must be
round k+1's input, byte for byte.

FIXTURE: corpus/flood_rounds.json, captured from the headless build with
ARC_SNAPSHOT_DEBUG=1. Each round carries the RNG state, the exact inputs UpdateFlood saw
(weather, lastWeather, rain, ordered tiles), its [RNGMARK] draw sequence, and the counts
Unity printed itself. Regenerate it whenever FloodSystem.cs or the scene's FloodParameters
change -- like the RNG corpus, it is ground truth, not a snapshot of the port's opinion.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.flood import FloodState, update_flood          # noqa: E402
from cora_sim.floodmap import FloodMap, pack                 # noqa: E402
from cora_sim.rng import UnityRandom                         # noqa: E402

_FIXTURE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "corpus", "flood_rounds.json")


def load_rounds():
    return json.load(open(_FIXTURE))["rounds"]


def replay(rd, fmap, marks=None, stats=None):
    st = rd["rng"]
    rng = UnityRandom(state=(st["s0"], st["s1"], st["s2"], st["s3"]))
    fs = FloodState({pack(x, y) for x, y in rd["tiles"]}, rd["lastWeather"])
    update_flood(fs, fmap, rng, rd["weather"], rd["rain"], marks, stats)
    return fs


def draw_census(fmap, rounds):
    """Every draw in a round is accounted for -- not just flood's.

    THE GAP THIS CLOSES. The per-round test replays each round from its own captured entry
    state, so it is blind to anything that draws BETWEEN one flood exit and the next flood
    entry. A port can pass it round after round while an uninstrumented system quietly
    consumes randoms, and the failure only appears once rounds are chained instead of
    replayed -- which is exactly when the surrogate becomes useful and exactly when the
    cause is hardest to find.

    The check needs no new instrumentation. Advance the port's stream from round k's exit
    state by the number of non-flood draws recorded between the marks; if that lands on
    round k+1's captured ENTRY state, the interval is fully explained. Landing anywhere
    else means an undiscovered draw site, and the shortfall is its draw count.

    Result on the current fixture: every interval is explained by flood plus
    TaskTrigger.probability plus Weather.select. There is no third mystery drawer -- and
    that is a measurement, so re-run it on episodes that exercise clients and deliveries
    before assuming it holds there too."""
    ok = True
    for k, rd in enumerate(rounds[:-1]):
        nxt = rounds[k + 1]["rng"]
        target = (nxt["s0"], nxt["s1"], nxt["s2"], nxt["s3"])
        st = rd["rng"]
        rng = UnityRandom(state=(st["s0"], st["s1"], st["s2"], st["s3"]))
        fs = FloodState({pack(x, y) for x, y in rd["tiles"]}, rd["lastWeather"])
        update_flood(fs, fmap, rng, rd["weather"], rd["rain"])
        inter = rd.get("interRoundDraws", [])
        for _ in inter:
            rng.next_uint()
        if rng.get_state() != target:
            # Report the size of the hole, not just its existence.
            probe = UnityRandom(state=rng.get_state())
            extra = None
            for d in range(1, 5001):
                probe.next_uint()
                if probe.get_state() == target:
                    extra = d
                    break
            print(f"  draw census             : round {k} unexplained "
                  f"({'+' + str(extra) + ' draws' if extra else 'state not reachable within 5000'})")
            ok = False
    if ok:
        sites = sorted({d for r in rounds for d in r.get("interRoundDraws", [])})
        print(f"  draw census             : all {len(rounds)-1} inter-round intervals "
              f"explained by flood + {', '.join(s.split(':')[1] for s in sites)}")
    return ok


def range_int_is_discriminated(fmap, rounds):
    """Assert the fixture still PINS Random.Range(int, int), and does not merely tolerate
    the shipped implementation.

    A passing equivalence test proves nothing about a code path the fixture cannot see.
    Flood makes 26 Range calls (expansion picks, plus source/direction/distance for random
    expansion), so it should discriminate -- and it does: swapping in either scaled variant
    puts 5 of the 9 comparable rounds on the wrong tiles. If a future fixture stops
    discriminating, this reports that instead of quietly leaving Range unvalidated."""
    import cora_sim.rng as rng_mod

    def scaled_by_value(self, lo, hi):
        n = hi - lo
        if n <= 0:
            return lo
        return min(lo + int(((self.next_uint() & 0x7FFFFF) / 8388607.0) * n), hi - 1)

    def scaled_by_raw(self, lo, hi):
        n = hi - lo
        if n <= 0:
            return lo
        return lo + min(int(self.next_uint() * n / 4294967296.0), n - 1)

    original = rng_mod.UnityRandom.range_int
    out = []
    try:
        for name, impl in (("value-scaled", scaled_by_value), ("raw-scaled", scaled_by_raw)):
            rng_mod.UnityRandom.range_int = impl
            wrong = 0
            for k, rd in enumerate(rounds[:-1]):
                fs = replay(rd, fmap)
                if fs.tiles != {pack(x, y) for x, y in rounds[k + 1]["tiles"]}:
                    wrong += 1
            out.append((name, wrong))
    finally:
        rng_mod.UnityRandom.range_int = original

    dead = [n for n, w in out if w == 0]
    if dead:
        print(f"  Range(int,int)          : NOT DISCRIMINATED - {dead} also passes")
        return False
    print("  Range(int,int)          : pinned as lo + raw % n ("
          + ", ".join(f"{n} breaks {w} rounds" for n, w in out) + ")")
    return True


def main():
    rounds = load_rounds()
    fmap = FloodMap.load()
    failures = []

    for k, rd in enumerate(rounds):
        marks, stats = [], {}
        fs = replay(rd, fmap, marks, stats)
        tag = f"round {k} ({rd['weather']}, in={len(rd['tiles'])})"

        # level 1 -- the draw sequence itself
        if marks != rd["marks"]:
            n = min(len(marks), len(rd["marks"]))
            i = next((j for j in range(n) if marks[j] != rd["marks"][j]), n)
            failures.append(f"{tag}: mark sequence diverges at index {i} "
                            f"(unity={rd['marks'][i:i+1]} port={marks[i:i+1]}); "
                            f"lengths unity={len(rd['marks'])} port={len(marks)}")
            continue

        # level 2 -- the counts Unity printed for each phase
        for key, want in rd["unity"].items():
            got = stats.get(key, 0)
            if got != want:
                failures.append(f"{tag}: {key} unity={want} port={got}")

        # level 3 -- the state itself, against the next round's captured input
        if k + 1 < len(rounds):
            nxt = {pack(x, y) for x, y in rounds[k + 1]["tiles"]}
            if fs.tiles != nxt:
                only_p = sorted(fs.tiles - nxt)[:4]
                only_u = sorted(nxt - fs.tiles)[:4]
                failures.append(f"{tag}: resulting tile set != next round's input "
                                f"(port-only {len(fs.tiles - nxt)} e.g. {only_p}, "
                                f"unity-only {len(nxt - fs.tiles)} e.g. {only_u})")

        print(f"  {tag}: {len(marks)} draws, "
              f"{rd['unity'].get('after', len(fs.tiles))} tiles -- OK")

    if not failures:
        if not range_int_is_discriminated(fmap, rounds):
            failures.append("Random.Range(int,int) is not pinned by this fixture")
        if not draw_census(fmap, rounds):
            failures.append("uninstrumented draw sites exist between rounds")

    total_draws = sum(len(r["marks"]) for r in rounds)
    if failures:
        print(f"\nFAIL ({len(failures)}):")
        for f in failures:
            print("  " + f)
        return 1
    print(f"\nPASS: {len(rounds)}/{len(rounds)} rounds, {total_draws} draws, "
          f"mark sequence + phase counts + tile sets all identical to Unity")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
