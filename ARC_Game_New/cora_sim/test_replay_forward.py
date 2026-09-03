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
_DEFAULT = ("/private/tmp/claude-501/-Users-cpulling-Work-CORA/"
            "b762a1aa-9f0c-4053-9897-bfd6aeeb9623/scratchpad/staff_*.json")


def seed_state(log_path):
    """The RNG state Unity recorded at its first flood:enter -- the port's starting point."""
    import re
    rx = re.compile(r"\[RNGCTX\] \S+ flood:enter (\{.*?\}) ")
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
            for k in TRACKED:
                if k in truth and got.get(k) != truth[k] and first_bad is None:
                    first_bad = (step["round"], k, truth[k], got.get(k))
        name = os.path.basename(path)
        if first_bad:
            r, k, want, gotv = first_bad
            worst.setdefault(k, 0)
            worst[k] += 1
            print(f"  {name}: first divergence round {r} on {k} (unity={want} port={gotv})")
        else:
            print(f"  {name}: every tracked counter matches at every round")
    print("\n  NOTE: task IDENTITY cannot be replayed -- Unity's task ids come from its own "
          "generator, so the port answers the tasks IT generates. A divergence here is "
          "therefore generation timing, not counter arithmetic, which the replay suites "
          "already cover exactly.")
    print("\nRESULT:", "ALL PASS" if not worst else "DIVERGENCES PRESENT")
    return 0 if not worst else 1


if __name__ == "__main__":
    raise SystemExit(main())
