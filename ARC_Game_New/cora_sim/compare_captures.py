"""Compare two headless capture sets seed by seed: the first round where any rewardMetrics
counter, budget or score differs between staff_<seed>.json in A and in B. Used to see what
a Unity change (a merge, a rebuild) did to the recorded episodes, independent of the port.

    python -m cora_sim.compare_captures cora_sim/runs/validate cora_sim/runs/validate_v1
"""
from __future__ import annotations

import json
import os
import re
import sys


def first_diff(a, b):
    for i, (ra, rb) in enumerate(zip(a, b)):
        ma, mb = ra["after"]["rewardMetrics"], rb["after"]["rewardMetrics"]
        d = {k: (ma.get(k), mb.get(k)) for k in set(ma) | set(mb) if ma.get(k) != mb.get(k)}
        ba, bb = ra["after"]["satisfactionAndBudget"]["budget"], rb["after"]["satisfactionAndBudget"]["budget"]
        if ba != bb:
            d["budget"] = (ba, bb)
        if d:
            return i, d
    if len(a) != len(b):
        return min(len(a), len(b)), {"rounds": (len(a), len(b))}
    return None, {}


def main():
    da, db = sys.argv[1], sys.argv[2]
    seeds = sorted(int(m.group(1)) for f in os.listdir(db) for m in [re.match(r"staff_(\d+)\.json$", f)] if m
                   and os.path.exists(os.path.join(da, f)))
    for s in seeds:
        a = json.load(open(os.path.join(da, f"staff_{s}.json")))
        b = json.load(open(os.path.join(db, f"staff_{s}.json")))
        i, d = first_diff(a, b)
        print(f"  seed {s}: {'identical' if i is None else f'first diff at round {i}: {d}'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
