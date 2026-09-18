"""Magnitude-aware replay summary: sum over ALL rounds of |unity - port| per counter.

The exact-trace ratchet counts traces that match at every round, and first-divergence
reports name the earliest bad round. Neither can tell 'off by 1 at round 6' from 'off by
5000 at round 9', and fixing the earliest error makes the NEXT pre-existing error look like a
regression. For a transition function what matters is total state error, so this reports it.
"""
import glob
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.diag_marks import replay_steps                 # noqa: E402
from cora_sim.test_replay_forward import TRACKED, _DEFAULT   # noqa: E402


def main():
    paths = sorted(glob.glob(os.environ.get("STAFF_TRACES", _DEFAULT)))
    grand = 0
    per_counter = {k: 0 for k in TRACKED}
    rows = []
    for path in paths:
        log = path.replace(".json", ".log")
        if not os.path.exists(log):
            continue
        tot = 0
        bad_rounds = 0
        for step, w in replay_steps(path, log):
            got = w.economy.metrics()
            want = step["after"].get("rewardMetrics") or {}
            err = 0
            for k in TRACKED:
                if k in want and got.get(k) is not None:
                    d = abs(int(want[k]) - int(got[k]))
                    err += d
                    per_counter[k] += d
            if err:
                bad_rounds += 1
            tot += err
        grand += tot
        rows.append((os.path.basename(path), tot, bad_rounds))
    for name, tot, br in rows:
        print(f"  {name:<18} total|err|={tot:>8}  bad rounds={br}")
    print(f"\n  GRAND TOTAL |err| = {grand}")
    print("  by counter: " + ", ".join(f"{k}={v}" for k, v in per_counter.items() if v))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
