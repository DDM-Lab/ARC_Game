"""Does the surrogate grey out the same task choices Unity does?

Replays plans captured on Unity (oracle.sim.capture --calls: replay_<seed>/staff_<seed>.json beside
plan_<seed>.json) on SimEnv and, before every decision, compares each offered choice's
(feasible, unavailableReason) with Unity's. Only decisions before the replay's first state
divergence count -- after that the two games differ and so do their choices.

    python -m oracle.sim.feasibility_parity RUN_DIR [RUN_DIR ...] [--json out.json]

Reports agreement on the flag (a confusion matrix) and on the exact reason string, per reason.
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import sys
from collections import Counter


def _choices(gs):
    out = {}
    for t in gs.get("allActiveTasks") or []:
        for c in t.get("choices") or []:
            out[(t.get("taskId"), c.get("choiceId"))] = (bool(c.get("feasible", True)), c.get("unavailableReason") or "",
                                                         t.get("taskTitle") or "")
    return out


def check_plan(run, seed):
    from cora import executor
    from cora.scoring import score_components
    from oracle.rollout import DECISIONS
    from oracle.sim.env import SimEnv
    plan = json.load(open(os.path.join(run, f"plan_{seed}.json")))
    trace = json.load(open(os.path.join(run, f"replay_{seed}", f"staff_{seed}.json")))
    steps = [st for st in trace if st.get("decision") is not None]
    env = SimEnv(seed=seed, max_episode_steps=DECISIONS + 4, manual_transfers=False)
    env.reset()
    rows, diverged = [], None
    for st in steps:
        d = st["decision"]
        su = score_components(st["before"].get("rewardMetrics"))
        ss = score_components(env.game_state.get("rewardMetrics"))
        budget = lambda gs: (gs.get("satisfactionAndBudget") or {}).get("budget")
        if abs(su["score"] - ss["score"]) > 1e-4 or budget(st["before"]) != budget(env.game_state):
            diverged = d
            break
        u, s = _choices(st["before"]), _choices(env.game_state)
        for key in set(u) | set(s):
            rows.append({"seed": seed, "decision": d, "key": key, "unity": u.get(key), "sim": s.get(key)})
        if d >= len(plan):
            break
        _, (_, _, term, trunc, _) = executor.execute_turn(env, [(t, a) for t, a in plan[d]])
        if term or trunc:
            break
    return rows, diverged


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("runs", nargs="+")
    ap.add_argument("--json")
    ap.add_argument("--max-plans", type=int, default=0)
    a = ap.parse_args(argv)
    flag, reason, missing, n_plans, n_dec, div = Counter(), Counter(), Counter(), 0, 0, 0
    examples = []
    for run in a.runs:
        seeds = sorted(int(os.path.basename(p)[5:-5]) for p in glob.glob(os.path.join(run, "plan_*.json")))
        for seed in seeds:
            if not os.path.exists(os.path.join(run, f"replay_{seed}", f"staff_{seed}.json")):
                continue
            if a.max_plans and n_plans >= a.max_plans:
                break
            rows, diverged = check_plan(run, seed)
            n_plans += 1
            div += diverged is not None
            n_dec += len({r["decision"] for r in rows})
            for r in rows:
                if r["unity"] is None or r["sim"] is None:
                    missing["only unity" if r["sim"] is None else "only sim"] += 1
                    continue
                (fu, ru, title), (fs, rs, _) = r["unity"], r["sim"]
                flag[(fu, fs)] += 1
                if not fu or not fs:
                    reason[(ru, rs)] += 1
                    if (fu, ru) != (fs, rs) and len(examples) < 20:
                        examples.append({"run": os.path.basename(run), "seed": r["seed"], "decision": r["decision"],
                                         "task": title, "choice": r["key"][1], "unity": [fu, ru], "sim": [fs, rs]})
    tot = sum(flag.values()) or 1
    print(f"{n_plans} plans, {n_dec} decisions compared ({div} plans diverged before the end)")
    print(f"choices compared: {tot}; offered by one side only: {dict(missing)}")
    print("flag (unity feasible, sim feasible):", {f"{k}": v for k, v in sorted(flag.items())})
    agree = flag[(True, True)] + flag[(False, False)]
    print(f"flag agreement {agree / tot:.4f}; Unity infeasible {flag[(False, True)] + flag[(False, False)]}, "
          f"sim infeasible {flag[(True, False)] + flag[(False, False)]}")
    print("reasons (unity | sim):")
    for (ru, rs), n in reason.most_common(25):
        print(f"  {n:6}  {'==' if ru == rs else '!='}  {ru[:70]!r} | {rs[:70]!r}")
    if examples:
        print("disagreements (first 20):")
        for e in examples:
            print("  ", json.dumps(e))
    if a.json:
        json.dump({"flag": {str(k): v for k, v in flag.items()}, "reasons": {str(k): v for k, v in reason.items()},
                   "plans": n_plans, "examples": examples}, open(a.json, "w"), indent=1)


if __name__ == "__main__":
    main(sys.argv[1:])
