"""Lockstep diff: the port driving an evolved plan against the headless run of that same
plan (validate_plan.py output), draw for draw and counter for counter.

    python -m cora_sim.diag_lockstep runs/evo14.jsonl 5901 [--trace runs/validate/staff_5901.json]

Reports the first gym step whose draw stream differs, the first round each rewardMetrics
key differs, the first budget difference, and both final scores. The replay harness cannot
do this: it replays Unity's recorded actions, whereas here the port makes its own choices
and the question is whether Unity, fed those same choices, ends up in the same state."""
import argparse
import json
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.actions import CoraActions           # noqa: E402
from cora_sim.evolve import fresh_world, captured_seeds   # noqa: E402
from cora_sim.floodmap import FloodMap             # noqa: E402
from cora_sim.validate_plan import best_row        # noqa: E402
import cora_sim.diag_marks as D                    # noqa: E402
import cora_sim.sim as S                           # noqa: E402

_KEYS = ("foodResolved", "foodFulfilled", "lodgingResolved", "lodgingFulfilled", "cumWorkingWorkers",
         "cumIdleWorkers", "cumTrainingWorkers", "totalWorkers", "caseworkRequested", "caseworkProcessed",
         "foodSpend", "lodgingSpend", "workerSpend", "caseworkSpend")


def main():
    import reward_scoring
    ap = argparse.ArgumentParser()
    ap.add_argument("log"); ap.add_argument("unity_seed", type=int)
    ap.add_argument("--trace", default=None)
    args = ap.parse_args()
    trace = args.trace or f"runs/validate/staff_{args.unity_seed}.json"
    log = trace.replace(".json", ".log")
    row = best_row(args.log, args.unity_seed)
    t = json.load(open(trace))
    um = D.unity_marks(log) if os.path.exists(log) else {}
    s0 = D.seed_step(log) if os.path.exists(log) else None
    w = fresh_world(dict(captured_seeds())[args.unity_seed], FloodMap.load())
    m = CoraActions(random.Random(0))
    first, firstd, bd, exact = {}, None, None, 0
    for i, step in enumerate(t):
        gene = row["plan"][i] if i < len(row["plan"]) else {"choices": {}, "menu": []}
        m.apply(w, ("turn", gene)); marks = []; S.step_round(w, marks=marks)
        um_, sm = step["after"]["rewardMetrics"], w.economy.metrics()
        for k in _KEYS:
            if um_.get(k) != sm.get(k) and k not in first:
                first[k] = (i, um_.get(k), sm.get(k))
        ub = step["after"]["satisfactionAndBudget"]["budget"]
        if ub != w.economy.budget and bd is None:
            bd = (i, ub, w.economy.budget)
        s = i + 1
        if um and s0 is not None and s > s0 and firstd is None:
            u = um.get(s, [])
            if marks != u:
                k = next((j for j, (a, b) in enumerate(zip(marks, u)) if a != b), min(len(marks), len(u)))
                firstd = (s, k, len(u), len(marks), u[k] if k < len(u) else "<end>", marks[k] if k < len(marks) else "<end>")
            else:
                exact += 1
    print(f"draws: {exact} exact steps; first divergence {firstd}")
    print(f"metrics: first divergence per key (round, unity, port): {first or 'none'}")
    print(f"budget: first difference {bd}")
    print(f"score: unity {reward_scoring.compute_score(um_)[2]:.4f}  port {reward_scoring.compute_score(sm)[2]:.4f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
