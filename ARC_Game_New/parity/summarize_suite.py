#!/usr/bin/env python3
"""Summarise a multi-seed parity suite: how often two builds agree, and by how much.

    python3 parity/summarize_suite.py <outdir> [seeds]

Reports per-seed and in aggregate. The distinction that matters is between the two questions
the harness exists to separate:

  RNG AGREEMENT   did the two builds consume identical draws?  A bit-exact property.
  SCORE AGREEMENT do budget / satisfaction / efficiency / facilities agree, and if not, by how
                  much?  This is the practical bar -- "the LLM code is off and the game plays
                  the same" -- and a build can meet it while failing the first.

A suite is reported rather than a single verdict because divergences here are seed-selective:
one seed that happens never to generate a relocation task will agree perfectly while another
diverges on day 3. The useful statement is a rate and a worst case, not a yes/no.
"""
import json
import os
import statistics
import sys

FIELDS = ("budget", "satisfaction", "efficiency")


def load(path):
    rows = []
    if not os.path.exists(path):
        return rows
    with open(path) as fh:
        for line in fh:
            line = line.strip()
            if line:
                try:
                    rows.append(json.loads(line))
                except json.JSONDecodeError:
                    pass
    return rows


def key(r):
    return (r.get("day"), r.get("segment"))


def compare(a, b):
    """Return per-seed stats, or None when the pair is not comparable."""
    if not a or not b:
        return None
    ea, eb = a[-1].get("env"), b[-1].get("env")
    if ea is None or eb is None:
        return {"skip": "no env provenance"}
    norm = lambda e: {k: ("off" if k == "llm" and v in ("absent", "off") else v)
                      for k, v in e.items()}
    if norm(ea) != norm(eb):
        return {"skip": f"env differs: {norm(ea)} vs {norm(eb)}"}

    bi = {key(r): r for r in b}
    n = rng_same = 0
    worst = {f: 0.0 for f in FIELDS}
    fac_same = 0
    first_rng = first_state = None
    for ra in a:
        rb = bi.get(key(ra))
        if rb is None:
            continue
        n += 1
        if ra.get("rng") == rb.get("rng"):
            rng_same += 1
        elif first_rng is None:
            first_rng = key(ra)
        if ra.get("facilities") == rb.get("facilities"):
            fac_same += 1
        elif first_state is None:
            first_state = key(ra)
        for f in FIELDS:
            try:
                d = abs(float(ra.get(f, 0)) - float(rb.get(f, 0)))
            except (TypeError, ValueError):
                continue
            if d > worst[f]:
                worst[f] = d
            if d > 0 and first_state is None:
                first_state = key(ra)
    if n == 0:
        return {"skip": "no overlapping rounds"}
    return {"rounds": n, "rng_same": rng_same, "fac_same": fac_same,
            "worst": worst, "first_rng": first_rng, "first_state": first_state}


def acted(logpath):
    """How many confirms the policy actually landed. A suite where this is 0 everywhere is a
    clock-only run wearing a policy's clothes, and its parity result means much less."""
    if not os.path.exists(logpath):
        return None
    for line in open(logpath, errors="replace"):
        if "[ParityDriver] policy acted:" in line:
            try:
                return int(line.split("policy acted:")[1].split("confirm")[0].strip())
            except (IndexError, ValueError):
                return None
    return None


def main(outdir, seeds):
    per_seed, skipped = [], []
    total_confirms = 0
    for s in range(1, seeds + 1):
        a = load(os.path.join(outdir, f"baseline_{s}.jsonl"))
        b = load(os.path.join(outdir, f"candidate_{s}.jsonl"))
        r = compare(a, b)
        if r is None:
            skipped.append((s, "missing trace"))
            continue
        if "skip" in r:
            skipped.append((s, r["skip"]))
            continue
        c = acted(os.path.join(outdir, f"log_candidate_{s}.txt"))
        r["seed"] = s
        r["confirms"] = c
        total_confirms += c or 0
        per_seed.append(r)

    print(f"seeds compared : {len(per_seed)}")
    if skipped:
        print(f"seeds skipped  : {len(skipped)}")
        for s, why in skipped[:6]:
            print(f"    seed {s}: {why}")
    if not per_seed:
        print("\nNOTHING COMPARABLE — check the env provenance lines above.")
        return 2

    print(f"policy confirms: {total_confirms} across the suite "
          f"({statistics.mean([r['confirms'] or 0 for r in per_seed]):.1f}/seed)")
    if total_confirms == 0:
        print("  !! the policy never confirmed anything — this is effectively a clock-only")
        print("     suite, and D1/D5 remain unreachable. Treat the result as weak.")

    exact = [r for r in per_seed if r["rng_same"] == r["rounds"]]
    print(f"\nRNG bit-exact for the WHOLE episode : {len(exact)}/{len(per_seed)} seeds")
    rates = [r["rng_same"] / r["rounds"] for r in per_seed]
    print(f"  per-seed matching-round rate      : "
          f"min {min(rates):.0%}  median {statistics.median(rates):.0%}  max {max(rates):.0%}")

    print("\nSCORE / STATE AGREEMENT (the practical bar)")
    for f in FIELDS:
        w = [r["worst"][f] for r in per_seed]
        clean = sum(1 for x in w if x == 0)
        print(f"  {f:12} exact on {clean}/{len(per_seed)} seeds; "
              f"worst abs diff {max(w):.4f}; median worst {statistics.median(w):.4f}")
    facexact = sum(1 for r in per_seed if r["fac_same"] == r["rounds"])
    print(f"  {'facilities':12} exact on {facexact}/{len(per_seed)} seeds")

    bad = [r for r in per_seed if r["first_rng"] or r["first_state"]]
    if bad:
        print(f"\nfirst divergence by seed (showing up to 10 of {len(bad)}):")
        for r in bad[:10]:
            print(f"  seed {r['seed']:>3}  rng {str(r['first_rng']):>8}   "
                  f"state {str(r['first_state']):>8}   "
                  f"worst sat {r['worst']['satisfaction']:.3f}")
    return 0 if not bad else 1


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 32))
