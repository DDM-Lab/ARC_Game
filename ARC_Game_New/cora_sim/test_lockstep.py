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


def capture_dirs():
    """CORA_SIM_VALIDATE names one set; otherwise every cora_sim/runs/validate* set is ratcheted."""
    if os.environ.get("CORA_SIM_VALIDATE"):
        return [str(VALIDATE_DIR)]
    if not os.path.isdir(P.RUNS):
        return []
    return sorted(os.path.join(P.RUNS, d) for d in os.listdir(P.RUNS)
                  if d.startswith("validate") and os.path.isdir(os.path.join(P.RUNS, d)))


def seeds(vdir=None):
    vdir = str(vdir or VALIDATE_DIR)
    if not os.path.isdir(vdir):
        return []
    out = []
    for f in os.listdir(vdir):
        m = re.match(r"staff_(\d+)\.json$", f)
        if m and os.path.exists(os.path.join(vdir, f"staff_{m.group(1)}.log")):
            out.append(int(m.group(1)))
    return sorted(out)


def ratchet_dir(vdir):
    ss = seeds(vdir)
    if not ss:
        print(f"  {vdir}: no staff_<seed>.json+.log present; nothing to ratchet")
        return 0, 0
    failed = 0
    for s in ss:
        try:
            sess = Session(s, validate_dir=vdir)
        except Exception as e:                      # a half-written capture must not hide a real failure
            print(f"  seed {s}: ERROR loading/replaying: {e!r}"); failed += 1; continue
        ok = sess.first_diff is None and sess.draw_div is None
        print(f"  seed {s}: {'exact' if ok else 'DIVERGES'} -- "
              f"first counter/budget diff at step {sess.first_diff}; "
              f"draw stream {'identical' if sess.draw_div is None else 'diverges at ' + str(sess.draw_div[0])}")
        failed += 0 if ok else 1
    print(f"  {os.path.basename(vdir)}: {len(ss) - failed}/{len(ss)} validated seeds exact")
    return len(ss), failed


def main():
    dirs = capture_dirs()
    if not dirs:
        print("  (no cora_sim/runs/validate*/ capture set present; nothing to ratchet)")
        return 0
    total = failed = 0
    for d in dirs:
        print(f"  -- {d}")
        n, f = ratchet_dir(d)
        total += n; failed += f
    print(f"  {total - failed}/{total} validated seeds exact across {len(dirs)} capture set(s)")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
