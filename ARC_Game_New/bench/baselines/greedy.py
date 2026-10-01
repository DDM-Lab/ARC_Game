"""greedy: myopic, reward-mirrored task answers plus staffing; never builds, hires or trains."""
from __future__ import annotations

from bench.baselines.common import CHOICE_COST_WEIGHT, impacts_dict


def greedy(env, rnd=0, rounds_total=32):
    """Myopic, reward-mirrored greedy baseline (no learning, no API).

    Choices: per task pick the choice maximizing a reward-mirrored value built from
      the exposed impacts — funding (Budget>0) is scaled, demand fulfillment is worth
      ~w_food/w_lodging, costs are penalized with the reward's w_*_cost. Take the best
      if its value > 0; skip otherwise.
    Actions: assign free workers to NeedWorker buildings (cost 0, immediately enables
      InUse -> fulfillment + worker-use). Deliberately does NOT build/hire/train — those
      cost now and pay later, so a strictly myopic policy skips them (the under-investment
      is the intended diagnostic; the discounted-flow variant adds them)."""
    gs = env.game_state or {}
    va = env.valid_actions or []
    choices = []
    for t in gs.get("allActiveTasks", []) or []:
        tcs = t.get("choices") or []
        if not tcs:
            continue
        demand = t.get("taskType") in ("Demand", "Emergency")
        best, best_v = None, 0.0
        for c in tcs:
            imp = impacts_dict(c)
            b = float(imp.get("Budget", 0) or 0)
            s = float(imp.get("Satisfaction", 0) or 0)
            if b > 0:                                   # funding choice
                v = b / 10000.0 + 0.01 * s
            else:                                       # acting / waiting
                cost = -b
                acting = (cost > 0) or (s >= 10)
                v = (1.0 if (acting and demand) else 0.0) + 0.01 * s - CHOICE_COST_WEIGHT * cost
            if v > best_v:
                best_v, best = v, c
        if best is not None:
            choices.append({"taskId": t["taskId"], "choiceId": best["choiceId"]})

    # worker assignment: worker_assignment actions nest their fields under
    # a["assignment"] (to_dict pops the top-level building_name/worker_type/quantity).
    # The enumerator only emits these for buildings that need workers AND when free
    # workers exist, so take them directly (prefer trained; fill each building once).
    wf = gs.get("workforceState", {}) or {}
    ft = int(wf.get("freeTrainedWorkers", 0) or 0)
    fu = int(wf.get("freeUntrainedWorkers", 0) or 0)
    by_building = {}
    for i, a in enumerate(va):
        if a.get("action_type") == "worker_assignment":
            asg = a.get("assignment") or {}
            by_building.setdefault(asg.get("building_name"), []).append((i, asg))
    actions = []
    for bname, cands in by_building.items():
        for i, asg in sorted(cands, key=lambda x: (x[1].get("worker_type") != "trained",
                                                   -(x[1].get("quantity") or 0))):
            wt, q = asg.get("worker_type"), int(asg.get("quantity") or 0)
            avail = ft if wt == "trained" else fu
            if 0 < q <= avail:
                actions.append(i)
                if wt == "trained":
                    ft -= q
                else:
                    fu -= q
                break
    return {"choices": choices, "actions": actions,
            "note": "greedy", "reasoning": "myopic reward-mirrored: fulfill+fund via best choice, staff NeedWorker buildings"}
