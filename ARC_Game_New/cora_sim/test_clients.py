"""Client-stay equivalence, and a standing reminder of how it was nearly missed.

THE MISS. The first draw census concluded the economy consumed no randomness. It was run on
IDLE episodes, and on a played episode it is wrong by ~1500 draws per game across two thirds
of all rounds -- every one of them from this subsystem. The lesson is not "the census was a
bad idea"; the census is what FOUND it, once pointed at an episode where somebody acts. The
lesson is that a measurement only covers the states it visited.

WHAT IS CHECKED. The two thresholds, against every client draw in a played capture:
  * caseworkNeed fires at the exported scene rate (one Bernoulli per PERSON, not per group);
  * stayDuration lands inside the exported [min, max] and actually spans it.
Both use the reversed/low-23-bit semantics pinned elsewhere, so a regression in rng.py
surfaces here too.
"""
import collections
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.clients import C, _NEED_THRESHOLD            # noqa: E402
from cora_sim.rng import UnityRandom                       # noqa: E402

_FIXTURE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "corpus", "flood_rounds.json")


def _rng(row):
    st = row["rng"]
    return UnityRandom(state=(st["s0"], st["s1"], st["s2"], st["s3"]))


def main():
    rows = json.load(open(_FIXTURE)).get("clients", [])
    if not rows:
        print("  no client draws in fixture -- rebuild it from a PLAYED capture "
              "(an idle episode contains none, which is how they were missed)")
        print("\nRESULT: SKIPPED")
        return 0

    print("cora_sim.clients equivalence vs Unity")
    ok = True
    by_site = collections.Counter(r["site"] for r in rows)
    print(f"  draw sites observed     : {dict(by_site)}")

    need = [r for r in rows if r["site"] == "caseworkNeed"]
    if need:
        fires = sum(1 for r in need if _rng(r).value_lt(_NEED_THRESHOLD))
        rate = 100.0 * fires / len(need)
        # 3 sigma on a Bernoulli of this size; a wrong threshold misses by far more than
        # sampling error (the 40 in the .cs source would land ~17 points out).
        sigma = (C["casework_need_pct"] * (100 - C["casework_need_pct"]) / len(need)) ** 0.5
        within = abs(rate - C["casework_need_pct"]) <= 3 * sigma
        print(f"  caseworkNeed rate       : {rate:.1f}% over {len(need)} draws vs "
              f"{C['casework_need_pct']}% exported (3 sigma = {3*sigma:.1f}pp)"
              f"{'' if within else '  <-- THRESHOLD WRONG'}")
        ok &= within

    stay = [r for r in rows if r["site"] == "stayDuration"]
    if stay:
        durs = [_rng(r).range_int(C["min_stay"], C["max_stay"] + 1) for r in stay]
        in_range = all(C["min_stay"] <= d <= C["max_stay"] for d in durs)
        spread = len(set(durs))
        print(f"  stayDuration            : {len(durs)} draws, values "
              f"{dict(sorted(collections.Counter(durs).items()))}, "
              f"range [{C['min_stay']},{C['max_stay']}]"
              f"{'' if in_range else '  <-- OUT OF RANGE'}")
        if not in_range:
            ok = False
        elif spread < 2:
            print("  stayDuration            : only one distinct value - fixture does not "
                  "discriminate the range")

    print("\nRESULT:", "ALL PASS" if ok else "FAILURES PRESENT")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
