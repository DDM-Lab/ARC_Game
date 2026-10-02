"""FORWARD fidelity: the port run unaided against Unity, compared as DISTRIBUTIONS.

WHY DISTRIBUTIONS AND NOT A SINGLE RUN. The port and Unity cannot share an RNG stream when
each drives itself -- different actions consume different numbers of draws, so the streams
separate on the first divergent decision. Comparing one port run against one Unity run
therefore conflates model error with ordinary stochastic spread, and CORA has plenty:
Community_TransportRequest carries a 0.5 probability trigger, so which communities ask for
relocation on a given pass is a coin flip per facility per pass.

What can be compared is the distribution each side produces over seeds under the SAME
policy. A metric passes when Unity's spread and the port's overlap; it fails when the port
is systematically biased, which is visible as a narrow port spread sitting off to one side.

This is the honest acceptance test for search: RHEA needs the surrogate to RANK plans as
Unity would, and a systematic bias in demand volume changes what a planner invests in even
when every mechanic is present.
"""
import glob
import json
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from reward_scoring import compute_score_components                   # noqa: E402

METRICS = ("score", "lodgingResolved", "lodgingFulfilled",
           "foodResolved", "foodFulfilled", "caseworkRequested")
_DEFAULT = ("/private/tmp/claude-501/-Users-cpulling-Work-CORA/"
            "b762a1aa-9f0c-4053-9897-bfd6aeeb9623/scratchpad/staff_*.json")


def unity_samples(pattern=None):
    out = []
    for path in sorted(glob.glob(pattern or os.environ.get("STAFF_TRACES", _DEFAULT))):
        rm = json.load(open(path))[-1]["after"]["rewardMetrics"]
        row = {k: rm.get(k, 0) for k in METRICS if k != "score"}
        row["score"] = compute_score_components(rm)["score"]
        out.append(row)
    return out


def port_samples(n=40, rounds=24, seed=7):
    import random
    from cora_sim.economy import STATUS_NEED_WORKER
    from cora_sim.floodmap import FloodMap
    from cora_sim.rng import UnityRandom
    import cora_sim.sim as S

    fmap = FloodMap.load()
    rng = random.Random(seed)
    out = []
    for _ in range(n):
        st = tuple(rng.randrange(1, 2 ** 32) for _ in range(4))
        w = S.World(rng=UnityRandom(state=st), weather="Sunny", fmap=fmap)
        w.use_generation = True
        w.tasks.has_supplier = lambda tag: True
        for r in range(rounds):
            if r == 0:
                w.economy.build("Kitchen", 0)
            if r < 8:
                w.economy.hire("untrained", 5, 500)
            for i, b in enumerate(w.economy.buildings):
                if b["status"] == STATUS_NEED_WORKER:
                    w.economy.staff(i, count=max(1, 4 - b["assigned"]))
            seen = set()
            for tid, cid in S.open_choices(w):
                if tid in seen:
                    continue
                seen.add(tid)
                S.answer(w, tid, cid)
            S.step_round(w)
        m = w.economy.metrics()
        row = {k: m[k] for k in METRICS if k != "score"}
        row["score"] = compute_score_components(m)["score"]
        out.append(row)
    return out


def _span(vals):
    v = sorted(vals)
    if len(v) < 3:
        return min(v), max(v)
    return v[int(.1 * len(v))], v[int(.9 * len(v)) - 1]


def main():
    u = unity_samples()
    if not u:
        print("  no Unity build-and-staff captures found")
        print("\nRESULT: SKIPPED")
        return 0
    p = port_samples()

    print(f"cora_sim FORWARD fidelity  ({len(u)} Unity runs vs {len(p)} port runs, same policy)")
    print(f"  {'metric':<20}{'unity':>18}{'port':>18}   overlap")
    ok = True
    for k in METRICS:
        uu, pp = [r[k] for r in u], [r[k] for r in p]
        ulo, uhi = (min(uu), max(uu)) if len(uu) < 3 else _span(uu)
        plo, phi = _span(pp)
        overlap = not (phi < ulo or plo > uhi)
        ok &= overlap
        fmt = ".3f" if k == "score" else ".0f"
        print(f"  {k:<20}{format(ulo, fmt) + '-' + format(uhi, fmt):>18}"
              f"{format(plo, fmt) + '-' + format(phi, fmt):>18}   "
              f"{'yes' if overlap else 'NO'}")
    if len(u) < 3:
        print(f"  NOTE: only {len(u)} Unity run(s) -- its spread is not yet characterised, so "
              f"a non-overlap here is suggestive rather than conclusive.")
    print("\nRESULT:", "ALL PASS" if ok else "BIASES PRESENT")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
