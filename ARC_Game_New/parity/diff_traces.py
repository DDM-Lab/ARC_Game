#!/usr/bin/env python3
"""Compare two ParityProbe traces and report the FIRST round where two builds diverge.

    python3 parity/diff_traces.py baseline.jsonl candidate.jsonl

Why the RNG cursor is checked before anything else
--------------------------------------------------
Every stochastic system in the game draws from one global UnityEngine.Random stream, so the
`rng` field is a running checksum of every draw taken so far. Two builds that agree on it have
consumed identical draws in identical order; two that disagree diverged during that round.

That ordering matters for diagnosis. A skipped draw (the D1 casework bug) changes the RNG
cursor IMMEDIATELY but may not change any visible number for several rounds. Reporting the
first RNG mismatch therefore points at the round that actually contains the bug, rather than
the later round where somebody finally noticed a different flood.

So a report reads one of three ways:

  rng diverges, state still matches   -> a draw was taken/skipped; effects not surfaced yet
  rng matches, state diverges         -> NOT an RNG bug: same draws, different arithmetic or
                                         ordering (e.g. a changed score scale)
  both diverge                        -> the common case once an RNG break has had time to show
"""
import json
import sys


def load(path):
    rows = []
    with open(path) as fh:
        for n, line in enumerate(fh, 1):
            line = line.strip()
            if not line:
                continue
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError as e:
                print(f"  ! {path}:{n} unparseable ({e}); skipped")
    return rows


def key(r):
    return (r.get("day"), r.get("segment"))


def check_env(a, b):
    """Refuse to compare two episodes that were not run under the same conditions.

    This guard exists because of a real false result. The first run of this harness reported
    an RNG divergence at the first advanced round, which looks exactly like the D1 skipped-draw
    bug. It was nothing of the sort: one build had pulled a 67-object map from a map server
    that happened to be running on the machine while the other kept its built-in scene layout,
    the two were reading different parameter sheets, and one was connected to the live LLM
    router. Comparing those traces answers no question at all.

    A missing `env` block is treated as a refusal too, not as a pass: traces recorded before
    provenance existed cannot be shown to be comparable, and "unknown" must not read as "fine".
    """
    # Compare the LAST line, not the boot line. Both confounds this guard exists to catch land
    # AFTER boot: upstream downloads and applies its map several seconds into startup (the boot
    # line still shows the untouched scene), and our parameter sheet arrives asynchronously. A
    # guard that read the boot line would have waved through the exact D9 run that motivated it.
    ea, eb = a[-1].get("env"), b[-1].get("env")
    if ea is None or eb is None:
        print("\nREFUSING TO COMPARE: at least one trace has no `env` provenance block.")
        print("   It predates the provenance fields, so there is no evidence the two runs")
        print("   used the same map, parameters and LLM setting. Re-record both traces.")
        return 3
    # "absent" and "off" are the SAME experimental condition — the teammates are not attached.
    # main-bugfixes reports "absent" because the LLM code is not in the build at all, and that
    # must not by itself block the very comparison this harness exists to make. Only a live
    # router connection disqualifies a run.
    norm = lambda e: {**e, "llm": "off" if e.get("llm") in ("absent", "off") else e.get("llm")}
    ea, eb = norm(ea), norm(eb)

    # `objects` is excluded from the drift check and only from the drift check. It reads 0 until
    # startup settles and is frozen after, so it legitimately changes once per episode; it still
    # has to MATCH between the two traces, which is what actually catches a map swap.
    stable = lambda e: {k: v for k, v in e.items() if k != "objects"}
    drift = [(n, t[0].get("env"), t[-1].get("env"))
             for n, t in (("baseline", a), ("candidate", b))
             if stable(t[0].get("env", {})) != stable(t[-1].get("env", {}))]

    if ea == eb and not drift:
        return 0

    if drift:
        # An env that CHANGES mid-episode is its own red flag, even when both traces end up
        # agreeing: it means the run was still reconfiguring itself while rounds were being
        # recorded. A map applied over the scene at second three, or a router that connected
        # late, both look like this and both make the early rounds incomparable.
        print("\nREFUSING TO COMPARE: the environment changed DURING at least one episode.")
        for name, first, last in drift:
            print(f"   {name}: boot {first}")
            print(f"   {' ' * len(name)}  end  {last}")
        print("   -> the run was still configuring itself while rounds were recorded.")
        return 3
    print("\nREFUSING TO COMPARE: the two runs were not the same experiment.")
    for k in sorted(set(ea) | set(eb)):
        va, vb = ea.get(k, "<missing>"), eb.get(k, "<missing>")
        mark = "  " if va == vb else "->"
        print(f" {mark} {k}: baseline {va!r}  candidate {vb!r}")
    print("\n   params = InitialBudget/InitialSatisfaction/InitialGameDays as the game actually")
    print("            read them; differing values mean different parameter sources.")
    print("   llm    = 'absent' (no LLM code in the build), 'off', or 'CONNECTED'. A parity run")
    print("            must never be CONNECTED — pass -no-llm.")
    print("   objects = every Transform in the scene; differing counts mean different maps,")
    print("            usually a map server reachable for one build and not the other.")
    return 3


def main(a_path, b_path):
    a, b = load(a_path), load(b_path)
    print(f"baseline : {a_path}  ({len(a)} rounds)")
    print(f"candidate: {b_path}  ({len(b)} rounds)")
    if not a or not b:
        print("\nFAIL: one trace is empty — was -parity-probe passed to both builds?")
        return 2

    rc = check_env(a, b)
    if rc:
        return rc

    # Align on (day, segment) rather than line number: a build that emits an extra boot line,
    # or stops early, would otherwise report every later round as divergent.
    bi = {key(r): r for r in b}
    first_rng = first_state = None
    compared = 0

    for ra in a:
        k = key(ra)
        rb = bi.get(k)
        if rb is None:
            continue
        compared += 1
        if first_rng is None and ra.get("rng") != rb.get("rng"):
            first_rng = (k, ra.get("rng"), rb.get("rng"))
        fields = ("budget", "satisfaction", "efficiency", "facilities")
        diffs = [f for f in fields if ra.get(f) != rb.get(f)]
        if first_state is None and diffs:
            first_state = (k, diffs, {f: (ra.get(f), rb.get(f)) for f in diffs})
        # Deliberately no early exit: the loop runs to the end so `compared` is the real number
        # of rounds checked. Stopping at the first pair of findings made the report say
        # "compared 3 rounds" for a 5-round trace, which reads as a truncated comparison.

    print(f"\ncompared {compared} rounds present in both traces")
    only_a = [key(r) for r in a if key(r) not in bi]
    if only_a:
        print(f"  ! {len(only_a)} round(s) only in baseline, e.g. {only_a[:4]}")

    if not first_rng and not first_state:
        print("\nPARITY HOLDS across every compared round: identical RNG cursor and state.")
        return 0

    print()
    if first_rng:
        k, va, vb = first_rng
        print(f"RNG DIVERGES first at day {k[0]} segment {k[1]}")
        print(f"   baseline  {va}")
        print(f"   candidate {vb}")
        print("   -> a draw was taken on one build and not the other, at or before this round.")
    else:
        print("RNG cursor matches everywhere compared — the draws are identical.")

    if first_state:
        k, diffs, vals = first_state
        print(f"\nSTATE DIVERGES first at day {k[0]} segment {k[1]}: {', '.join(diffs)}")
        for f, (va, vb) in vals.items():
            sa, sb = str(va), str(vb)
            if len(sa) > 110:
                sa, sb = sa[:110] + "...", sb[:110] + "..."
            print(f"   {f}:\n     baseline  {sa}\n     candidate {sb}")
        if not first_rng:
            print("   -> same draws, different result: arithmetic or ordering, not RNG.")
    return 1


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1], sys.argv[2]))
