"""EXACT replay: same seed, same actions, same numbers -- or a named divergence.

WHY THIS IS THE RIGHT BAR, AND WHY DISTRIBUTION OVERLAP IS NOT.
Two stochastic systems making their own decisions cannot match run-for-run: the moment they
choose differently they consume different numbers of draws and the streams separate. That
is why the forward test compares distributions. But it is a WEAK bar -- two models can have
overlapping score distributions and disagree about every individual episode.

The strong bar is available here because the surrogate holds Unity's RNG stream exactly
(the draw census closes at zero unexplained draws) and the economy is deterministic. So:
seed the port from the RNG state Unity recorded at its first flood:enter, feed it Unity's
OWN per-round actions, and every counter should match at every round. Any divergence is a
real modelling error with a round number attached, not stochastic spread.

This is the test that would let RHEA's plans transfer, and it is the one that says exactly
where the port is still wrong.
"""
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.economy import (STATUS_NEED_WORKER, action_from_id,       # noqa: E402
                              apply_action, from_game_state)
from cora_sim.floodmap import FloodMap                                  # noqa: E402
from cora_sim.rng import UnityRandom                                    # noqa: E402
import cora_sim.sim as S                                                # noqa: E402

TRACKED = ("foodResolved", "foodFulfilled", "lodgingResolved", "lodgingFulfilled",
           "caseworkRequested", "workerSpend", "foodSpend", "lodgingSpend",
           "cumWorkingWorkers", "roundsCompleted")
# Floor for the exact-trace ratchet: raise this whenever a trace becomes exact, never
# lower it to make a change pass.
_MIN_EXACT_TRACES = 11      # every capture matches at every round; never lower this to pass

_DEFAULT = ("/private/tmp/claude-501/-Users-cpulling-Work-CORA/"
            "b762a1aa-9f0c-4053-9897-bfd6aeeb9623/scratchpad/staff_*.json")


def seed_state(log_path):
    """The RNG state Unity recorded at the first `round:advance` -- the port's starting point.

    It used to seed from the first `flood:enter`, which sits MID-step: after that step's
    generation pass. The port then replayed a pass Unity had already run, so its stream
    carried three spurious TaskTrigger draws at the head and every draw comparison was
    off by that much (the diff tooling papered over it by dropping Unity's whole first
    step). `round:advance` is the clean boundary: the state there is the state the step
    begins with, so port step 0 and Unity's first step describe the same instant."""
    import re
    for name in ("round:advance", "flood:enter"):
        rx = re.compile(r"\[RNGCTX\] \S+ " + name + r" (\{.*?\}) ")
        for line in open(log_path, errors="ignore"):
            m = rx.search(line)
            if m:
                st = json.loads(m.group(1))
                return tuple(st[k] & 0xFFFFFFFF for k in ("s0", "s1", "s2", "s3"))
    return None


def main():
    paths = sorted(glob.glob(os.environ.get("STAFF_TRACES", _DEFAULT)))
    if not paths:
        print("  no capture found")
        print("\nRESULT: SKIPPED")
        return 0

    fmap = FloodMap.load()
    print("cora_sim EXACT replay (Unity seed + Unity actions)")
    worst = {}
    worst_traces = set()
    for path in paths:
        trace = json.load(open(path))
        log = path.replace(".json", ".log")
        st = seed_state(log) if os.path.exists(log) else None
        if st is None:
            continue
        w = S.World(rng=UnityRandom(state=st), weather="Sunny", fmap=fmap)
        w.use_generation = True
        w.economy = from_game_state(trace[0]["before"])
        w.economy.buildings = type(w.economy).default_prebuilts()
        w.tasks.has_supplier = lambda tag: True

        first_bad = None
        for step in trace:
            for act in step["taken"]:
                if act.get("error") or act.get("ok") is False:
                    continue
                if act.get("kind") == "menu":
                    apply_action(w.economy, act.get("payload")
                                 or action_from_id(act.get("action_id"), act.get("cost") or 0))
                elif act.get("kind") == "staff":
                    a = (act.get("payload") or {}).get("assignment") or {}
                    idx = next((i for i, b in enumerate(w.economy.buildings)
                                if w.economy.can_staff(i)), -1)
                    w.economy.staff(idx, count=int(a.get("quantity") or 0))
            # The port answers its OWN generated tasks; Unity's task ids do not transfer.
            seen = set()
            for tid, cid in S.open_choices(w):
                if tid in seen:
                    continue
                seen.add(tid)
                S.answer(w, tid, cid)
            S.step_round(w)

            truth = step["after"].get("rewardMetrics") or {}
            got = w.economy.metrics()
            # EVERY counter that diverges at the first bad round, not just the first one in
            # TRACKED order. Reporting one key made a fix that merely reordered the
            # divergence look like a fix that removed it -- "food now matches" can simply
            # mean lodging started diverging first and masked it.
            if first_bad is None:
                bad = [(k, truth[k], got.get(k)) for k in TRACKED
                       if k in truth and got.get(k) != truth[k]]
                if bad:
                    first_bad = (step["round"], bad)
        name = os.path.basename(path)
        if first_bad:
            r, bad = first_bad
            for k, want, gotv in bad:
                worst[k] = worst.get(k, 0) + 1
            worst_traces.add(name)
            detail = ", ".join(f"{k} unity={want} port={gotv}" for k, want, gotv in bad)
            print(f"  {name}: first divergence round {r} on {len(bad)} counter(s): {detail}")
        else:
            print(f"  {name}: every tracked counter matches at every round")
    # RATCHET. Two traces match Unity at every round, and I lost an hour tonight to a
    # summary metric that penalised depth: a change that took two traces to exact scored
    # WORSE and was reverted. This refuses that silently ever again -- any future change
    # that trades away an exact trace fails here rather than looking like an improvement.
    exact = len(paths) - len(worst_traces)
    if exact < _MIN_EXACT_TRACES:
        print(f"\n  RATCHET FAILED: {exact} traces exact at every round, "
              f"floor is {_MIN_EXACT_TRACES}. A change has traded away an exact trace.")
        rc = 1
    else:
        print(f"\n  ratchet: {exact} traces exact at every round (floor {_MIN_EXACT_TRACES})")

    print("\n  NOTE: task IDENTITY cannot be replayed -- Unity's task ids come from its own "
          "generator, so the port answers the tasks IT generates. A divergence here is "
          "therefore generation timing, not counter arithmetic, which the replay suites "
          "already cover exactly.")
    print("\nRESULT:", "ALL PASS" if not worst else "DIVERGENCES PRESENT")
    return 0 if not worst else 1


if __name__ == "__main__":
    raise SystemExit(main())
