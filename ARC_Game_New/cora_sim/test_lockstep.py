"""Ratchet: every headless validation run under cora_sim/runs/validate (staff_<seed>.json + .log, written
by validate_plan.py) must replay EXACTLY on the surrogate -- identical draw stream, every
rewardMetrics counter, budget and score at every round.  Whatever is in cora_sim/runs/validate is
the floor; add seeds with

    python -m cora_sim.validate_plan cora_sim/runs/evo14.jsonl <seed> --port 21050

and they are picked up automatically.  Skips (rc 0) when no run is present."""
from __future__ import annotations

import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.debug_lockstep import Session      # noqa: E402

from cora_sim import paths as P      # noqa: E402

VALIDATE_DIR = P.VALIDATE


def seeds():
    if not os.path.isdir(VALIDATE_DIR):
        return []
    out = []
    for f in os.listdir(VALIDATE_DIR):
        m = re.match(r"staff_(\d+)\.json$", f)
        if m and os.path.exists(os.path.join(VALIDATE_DIR, f"staff_{m.group(1)}.log")):
            out.append(int(m.group(1)))
    return sorted(out)


def main():
    ss = seeds()
    if not ss:
        print("  (no cora_sim/runs/validate/staff_<seed>.json+.log present; nothing to ratchet)")
        return 0
    failed = 0
    for s in ss:
        try:
            sess = Session(s)
        except Exception as e:                      # a half-written capture must not hide a real failure
            print(f"  seed {s}: ERROR loading/replaying: {e!r}"); failed += 1; continue
        ok = sess.first_diff is None and sess.draw_div is None
        print(f"  seed {s}: {'exact' if ok else 'DIVERGES'} -- "
              f"first counter/budget diff at step {sess.first_diff}; "
              f"draw stream {'identical' if sess.draw_div is None else 'diverges at ' + str(sess.draw_div[0])}")
        failed += 0 if ok else 1
    print(f"  {len(ss) - failed}/{len(ss)} validated seeds exact")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
