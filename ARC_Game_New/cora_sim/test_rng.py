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
from cora_sim.rng import UnityRandom, threshold_for, M32


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


if __name__ == "__main__":
    log = sys.argv[1] if len(sys.argv) > 1 else os.environ.get("RNGMARK_LOG", "")
    print("cora_sim.rng equivalence vs Unity")
    results = [test_batch_equals_sequential(), test_threshold_matches_value(),
               test_seeding_is_not_silently_wrong()]
    results.insert(0, test_transitions(log if log and os.path.exists(log) else None))
    print("\nRESULT:", "ALL PASS" if all(results) else "FAILURES PRESENT")
    sys.exit(0 if all(results) else 1)
