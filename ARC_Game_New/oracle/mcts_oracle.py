"""UCT search over the surrogate to estimate an oracle upper bound and extract strategies.

The environment is STOCHASTIC and offers no seed or snapshot, so a plan is only meaningful as an
expectation. Two consequences shape this driver:
  * Common random numbers -- every candidate is scored on the SAME fixed seed ensemble, so
    differences between plans are signal rather than seed luck.
  * Open-loop macro plan -- a node is a per-round macro (build, hire, food rule, reloc rule) rather
    than a raw action set. The raw space (which task, which choice, how many hires) is far too wide
    to search to depth 32, and the per-incident choices are already pinned by the mechanics: one
    vehicle carries one 100-meal load, and the paid option needs no vehicle.

Reported bound is an estimate of E[score] for the best plan found, NOT a proof of optimality.
"""
from __future__ import annotations
import sys, os, math, json, random, argparse
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from arc_surrogate import ArcSurrogate, ROUNDS

BUILDS = (None, "Kitchen", "Shelter", "CaseworkSite")
HIRES = (0, 4)
FOOD_RULES = ("kitchen10", "paid")
RELOC_RULES = ("motel", "shelter")
CASEWORK = (0, 1)          # answer outstanding casework requests this round, or not
ACTIONS = [(b, h, f, r, c) for b in BUILDS for h in HIRES for f in FOOD_RULES
           for r in RELOC_RULES for c in CASEWORK]


def apply(sim, macro):
    """Expand a macro into the concrete per-round action the surrogate consumes."""
    b, h, food_rule, reloc_rule, cw = macro
    answer = []
    for i, t in enumerate(sim.tasks):
        if t.answered or t.resolved:
            continue
        if t.kind == "food":
            answer.append((i, food_rule))
        else:
            answer.append((i, reloc_rule))
    return {"build": b, "hire": h, "answer": answer, "casework": cw}


# Solvency constraint. The reward prices spend only as spend-per-unit-FULFILLED, and deficit
# spending is unblocked, so an unconstrained optimum happily ends deeply negative -- the budget is
# nearly free. MIN_BUDGET lets the search ask the separate question "what is the best plan that
# stays solvent?", which is the operationally meaningful one.
def rollout(plan, seeds, tail_random=True, rng=None, min_budget=None, ret_budget=False):
    """Score a macro plan on the seed ensemble; unspecified rounds are filled randomly."""
    total = 0.0; budgets = []
    for sd in seeds:
        sim = ArcSurrogate(sd)
        r = 0
        while not sim.done():
            if r < len(plan):
                macro = plan[r]
            elif tail_random and min_budget is None:
                macro = (rng or random).choice(ACTIONS)
            elif tail_random:
                # Under a budget constraint the RANDOM tail is what makes every rollout look
                # infeasible: it keeps picking paid deliveries and rapid evacuations, so a prefix
                # can never demonstrate solvency and the feasible set looks empty. Use the cheap
                # deterministic tail instead -- free kitchen food, shelter routing, no building.
                macro = (None, 0, "kitchen10", "shelter", 1)
            else:
                # The DETERMINISTIC tail must match the one the search assumed, or a plan that is
                # feasible during search is scored against a different (expensive) continuation at
                # evaluation time -- which reported a "feasible" plan ending at -262,700.
                macro = ((None, 0, "kitchen10", "shelter", 1) if min_budget is not None
                         else (None, 0, "kitchen10", "motel", 1))
            sim.step(apply(sim, macro))
            r += 1
        sc = sim.score()
        if min_budget is not None and sim.budget < min_budget:
            # hard-infeasible: rank strictly below any solvent plan, scaled by how far under.
            sc -= 1.0 + min(5.0, (min_budget - sim.budget) / 100_000.0)
        total += sc; budgets.append(sim.budget)
    if ret_budget:
        return total / len(seeds), sum(budgets) / len(budgets)
    return total / len(seeds)


class Node:
    __slots__ = ("plan", "kids", "untried", "n", "w")
    def __init__(self, plan, rng=None):
        self.plan, self.kids, self.n, self.w = plan, {}, 0, 0.0
        self.untried = None          # lazily filled with a shuffled action list


def uct(parent, child, c=1.2):
    if child.n == 0: return float("inf")
    return child.w / child.n + c * math.sqrt(math.log(max(parent.n, 1)) / child.n)


# Progressive widening: with 64 macros per round and depth 32 the branching factor is far too
# large to expand exhaustively, so a node may only open ceil(WIDEN_C * n^WIDEN_A) children. This
# also FIXES a degenerate search: the previous loop expanded a random action and, if that action
# already existed, simply descended into it -- so the tree grew as a single CHAIN (root had one
# child after 400 iterations), reached depth 32, then re-evaluated one fixed plan forever. That is
# why `best` plateaued at iteration ~3,500 and why two independent runs returned byte-identical
# greedy plans: with a fixed RNG seed there was only ever one path.
WIDEN_C, WIDEN_A = 2.0, 0.5


def search(iters=4000, seeds=8, depth=ROUNDS, c=1.2, seed0=0, log_every=500, min_budget=None,
           pareto=None):
    rng = random.Random(12345)
    ens = list(range(seed0, seed0 + seeds))
    root = Node(())
    best = (-9e9, None)
    best_feas = (-9e9, None)
    for it in range(1, iters + 1):
        node, path = root, [root]
        # --- selection with progressive widening ---
        while len(node.plan) < depth:
            if node.untried is None:
                node.untried = list(ACTIONS)
                rng.shuffle(node.untried)
            allowed = max(1, int(math.ceil(WIDEN_C * (max(node.n, 1) ** WIDEN_A))))
            if node.untried and len(node.kids) < allowed:
                a = node.untried.pop()
                child = Node(node.plan + (a,))
                node.kids[a] = child
                node = child
                path.append(node)
                break                          # expand exactly one new node per iteration
            if not node.kids:
                break
            a, node = max(node.kids.items(), key=lambda kv: uct(path[-1], kv[1], c))
            path.append(node)
        # --- simulation + backprop ---
        val, bud = rollout(node.plan, ens, rng=rng, min_budget=min_budget, ret_budget=True)
        if pareto is not None and node.plan:
            # unpenalised score for the frontier: the penalty is a search device, not a result
            true_val = val if (min_budget is None or bud >= min_budget) else rollout(node.plan, ens, rng=rng)
            pareto.append((true_val, bud, node.plan))
        for nd in path:
            nd.n += 1; nd.w += val
        if val > best[0]:
            best = (val, node.plan)
        # Best FEASIBLE plan tracked separately: reporting only the global best let a constrained
        # run return an infeasible answer, because the penalty ranks infeasible plans below
        # feasible ones but the max is still taken over everything.
        if min_budget is not None and bud >= min_budget and node.plan and val > best_feas[0]:
            best_feas = (val, node.plan)
        if it % log_every == 0:
            fb = f" feasible={best_feas[0]:+.3f}" if min_budget is not None else ""
            print(f"  iter {it:6d}  best={best[0]:+.3f}  depth={len(best[1] or ())}"
                  f"  root_kids={len(root.kids)}{fb}", flush=True)
    return best, best_feas, root



def pareto_front(points):
    """Non-dominated set maximising BOTH score and final budget.

    A plan is on the frontier when nothing else is at least as good on both axes and strictly
    better on one. This is the interesting object: the two searches only find the endpoints, and
    the shape between them says how much score a solvency requirement actually costs.
    """
    pts = sorted(points, key=lambda t: (-t[0], -t[1]))     # best score first
    front, best_bud = [], float("-inf")
    for sc, bud, plan in pts:
        if bud > best_bud:                                  # strictly better budget at lower score
            front.append((sc, bud, plan))
            best_bud = bud
    return front


def greedy_extract(root, depth=ROUNDS):
    """Most-visited path = the strategy the search actually converged on."""
    plan, node = [], root
    while node.kids and len(plan) < depth:
        a, node = max(node.kids.items(), key=lambda kv: kv[1].n)
        plan.append(a)
    return tuple(plan)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--iters", type=int, default=4000)
    ap.add_argument("--seeds", type=int, default=8)
    ap.add_argument("--eval-seeds", type=int, default=200)
    ap.add_argument("--out", default="/zfsauton/scratch/cpulling/arc_benchmarks/oracle")
    ap.add_argument("--min-budget", type=float, default=None,
                    help="require final budget >= this (e.g. 0 for a solvent plan)")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    print(f"UCT: iters={a.iters} ensemble={a.seeds} actions/round={len(ACTIONS)} depth={ROUNDS}")
    pareto_pts = []
    (bv, bplan), (fv, fplan), root = search(iters=a.iters, seeds=a.seeds, min_budget=a.min_budget,
                                            pareto=pareto_pts)
    gplan = greedy_extract(root)
    held = list(range(10_000, 10_000 + a.eval_seeds))          # held-out seeds
    res = {
        "best_rollout_value": bv,
        "best_plan_heldout": rollout(bplan, held, tail_random=False, min_budget=a.min_budget) if bplan else None,
        "min_budget": a.min_budget,
        "greedy_plan_heldout": rollout(gplan, held, tail_random=False, min_budget=a.min_budget) if gplan else None,
        "best_plan": [list(x) for x in (bplan or ())],
        "greedy_plan": [list(x) for x in gplan],
    }
    # trajectory of the greedy plan on one seed, for inspection
    if fplan:
        fsc, fbud = rollout(fplan, held, tail_random=False, ret_budget=True, min_budget=a.min_budget)
        res["feasible_plan"] = [list(x) for x in fplan]
        res["feasible_heldout"] = fsc
        res["feasible_budget"] = fbud
        print(f"  FEASIBLE best: score {fsc:+.3f}  mean final budget {fbud:+,.0f}")
    for nm, pl in (("best", bplan), ("greedy", gplan)):
        if pl:
            sc, bud = rollout(pl, held, tail_random=False, ret_budget=True, min_budget=a.min_budget)
            res[f"{nm}_final_budget"] = bud
            print(f"  {nm} plan: score {sc:+.3f}  mean final budget {bud:+,.0f}")
    sim = ArcSurrogate(10_000); traj = []
    r = 0
    while not sim.done():
        macro = gplan[r] if r < len(gplan) else (None, 0, "kitchen10", "motel", 1)
        traj.append({"round": r, "macro": list(macro), "budget": sim.budget,
                     "motel_pop": sim.motel_pop,
                     "buildings": [b.kind for b in sim.buildings]})
        sim.step(apply(sim, macro)); r += 1
    res["components"] = sim.components()
    res["trajectory"] = traj
    front = pareto_front(pareto_pts)
    # re-score the frontier on held-out seeds so the curve is not an artefact of the search ensemble
    res["pareto"] = []
    for sc, bud, plan in front:
        hsc, hbud = rollout(plan, held, tail_random=False, ret_budget=True, min_budget=a.min_budget)
        res["pareto"].append({"score": hsc, "budget": hbud, "plan": [list(x) for x in plan]})
    res["pareto"].sort(key=lambda d: d["budget"])
    print(f"\n  pareto frontier: {len(front)} non-dominated plans from {len(pareto_pts)} evaluated")
    for d in res["pareto"]:
        print(f"     budget {d['budget']:+12,.0f}   score {d['score']:+.3f}   plan_len {len(d['plan'])}")
    with open(os.path.join(a.out, "oracle_result.json"), "w") as f:
        json.dump(res, f, indent=1)
    print(f"\nbest (search ensemble):   {bv:+.3f}")
    print(f"best plan on held-out:    {res['best_plan_heldout']:+.3f}")
    print(f"greedy plan on held-out:  {res['greedy_plan_heldout']:+.3f}")
    print("components:", {k: round(v, 3) for k, v in res["components"].items() if v})
    print("saved ->", os.path.join(a.out, "oracle_result.json"))


if __name__ == "__main__":
    main()
