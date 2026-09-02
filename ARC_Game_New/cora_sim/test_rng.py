"""Equivalence tests for the bit-exact RNG port.

The corpus is real: (before, after) state pairs captured from the live Unity headless
build via SnapshotDebug [RNGMARK] instrumentation. This is the first rung of the
validation ladder -- the port's transition function must reproduce Unity's exactly, or
nothing built on top of it can be trusted.
"""
from __future__ import annotations

import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from cora_sim.rng import f32add, f32mul, UnityRandom, threshold_for, M32


def load_corpus(path=None):
    """Committed golden corpus -- the drift-control artifact. Regenerate deliberately
    when Unity mechanics, the scene, or the build change."""
    path = path or os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "corpus", "rng_transitions.json")
    d = json.load(open(path))
    return [(tuple(p["before"]), tuple(p["after"])) for p in d["pairs"]]


def load_pairs(log_path):
    """Consecutive draw-site marks differ by exactly one draw, giving a transition pair."""
    pairs, prev = [], None
    for line in open(log_path, errors="ignore"):
        m = re.search(r"\[RNGMARK\] \S+ (\S+) (\{.*\})", line)
        if not m:
            continue
        label, st = m.group(1), json.loads(m.group(2))
        s = tuple(st[k] & M32 for k in ("s0", "s1", "s2", "s3"))
        if prev and prev[0].startswith("draw:") and label.startswith("draw:"):
            pairs.append((prev[1], s))
        prev = (label, s)
    return pairs


def test_transitions(log_path=None):
    pairs = load_pairs(log_path) if log_path else load_corpus()
    assert pairs, "no transition pairs available"
    bad = 0
    for before, after in pairs:
        r = UnityRandom(state=before)
        r.next_uint()
        if r.get_state() != after:
            bad += 1
    print(f"  single-draw transitions : {len(pairs)-bad}/{len(pairs)} match"
          f"{'' if not bad else f'  ({bad} MISMATCH)'}")
    return bad == 0


def test_batch_equals_sequential():
    """next_batch(k) must leave the identical state as k calls to next_uint()."""
    start = (878108152, 3570879193, 3354241182, 3355245330)
    for k in (1, 2, 7, 50, 331):
        a = UnityRandom(state=start); a.next_batch(k)
        b = UnityRandom(state=start)
        for _ in range(k):
            b.next_uint()
        if a.get_state() != b.get_state() or a.draws != b.draws:
            print(f"  batch vs sequential     : MISMATCH at k={k}")
            return False
    print("  batch vs sequential     : identical for k in 1..331")
    return True


def test_threshold_matches_value():
    """value_lt(threshold_for(c)) must agree with value() < c on every draw."""
    start = (878108152, 3570879193, 3354241182, 3355245330)
    for chance in (0.0, 0.05, 0.25, 0.5, 0.9, 1.0):
        t = threshold_for(chance)
        a = UnityRandom(state=start)
        b = UnityRandom(state=start)
        for _ in range(2000):
            if a.value_lt(t) != (b.value() < chance):
                print(f"  integer threshold       : MISMATCH at chance={chance}")
                return False
    print("  integer threshold       : agrees with float compare over 2000 draws x 6 chances")
    return True


def test_seeding_is_not_silently_wrong():
    """Seeding is NOT pinned yet; it must raise rather than return plausible garbage."""
    try:
        UnityRandom(seed=1234)
    except NotImplementedError:
        print("  unpinned seeding        : raises (correct - not silently wrong)")
        return True
    print("  unpinned seeding        : DID NOT RAISE - would produce unvalidated streams")
    return False


def test_value_mapping_is_pinned_to_unity():
    """The raw -> Random.value mapping, against Unity ground truth.

    Two rain-spawn rounds in the flood fixture draw once per river tile at a known chance
    and Unity logs how many succeeded. That is a 108-bit measurement of the mapping, and
    the plausible alternatives disagree with it:

        mapping                     round 4 (p=0.82)   round 8 (p=0.90)
        (raw & 0x7FFFFF)/(2^23-1)         84 = truth         96 = truth
        raw / 2^32                        83                 91
        (raw >> 9) / 2^23                 83                 91

    The 0.90 row is what makes it conclusive: a 5-tile gap is not float rounding. This
    test asserts BOTH that the shipped mapping reproduces the truth and that the rivals
    do not, so a "harmless simplification" back to raw/2^32 fails here instead of showing
    up later as an unexplained divergence in some other mechanic."""
    fixture = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                           "corpus", "flood_rounds.json")
    rounds = json.load(open(fixture))["rounds"]
    rows = [(r, r["unity"]["spawned"]) for r in rounds if "spawned" in r.get("unity", {})]
    if not rows:
        print("  value mapping           : NO GROUND-TRUTH ROWS in flood_rounds.json")
        return False

    rivals = {"raw/2^32": lambda w: w / 4294967296.0,
              "(raw>>9)/2^23": lambda w: (w >> 9) / 8388608.0}
    ok = True
    for rd, truth in rows:
        st = rd["rng"]
        chance = f32add(0.7, f32mul(rd["rain"], 0.2))   # floodSpawnChance + rain*bonus
        thr = threshold_for(chance)
        r = UnityRandom(state=(st["s0"], st["s1"], st["s2"], st["s3"]))
        got = sum(r.value_lt(thr) for _ in range(108))
        if got != truth:
            print(f"  value mapping           : chance={chance:.3f} unity={truth} port={got}")
            ok = False
        for name, fn in rivals.items():
            r = UnityRandom(state=(st["s0"], st["s1"], st["s2"], st["s3"]))
            rival = sum(1 for _ in range(108) if fn(r.next_uint()) < chance)
            if rival == truth:
                print(f"  value mapping           : rival {name} ALSO matches "
                      f"(chance={chance:.3f}) - this test no longer discriminates")
                ok = False
    if ok:
        print(f"  value mapping           : {len(rows)} Unity spawn rows reproduced exactly; "
              f"{len(rivals)} rival mappings rejected")
    return ok


if __name__ == "__main__":
    log = sys.argv[1] if len(sys.argv) > 1 else os.environ.get("RNGMARK_LOG", "")
    print("cora_sim.rng equivalence vs Unity")
    results = [test_batch_equals_sequential(), test_threshold_matches_value(),
               test_value_mapping_is_pinned_to_unity(),
               test_seeding_is_not_silently_wrong()]
    results.insert(0, test_transitions(log if log and os.path.exists(log) else None))
    print("\nRESULT:", "ALL PASS" if all(results) else "FAILURES PRESENT")
    sys.exit(0 if all(results) else 1)
