"""
PROTOTYPE (read-only): re-score saved rollouts under a CONVEX budget-cost curve and compare to
the current linear cost. NO env change — this just relabels existing behavior to see how the
curve's SHAPE would reorder policies and (de)value hoarding vs spending. It does NOT show how
agents would *behave* under the new reward (that needs re-running with the agent optimizing it).

Current reward:  score = satisfaction - cost_efficiency
  satisfaction = sat_food+sat_lodging+sat_worker_use+casework_processing_sat   (each clamped, linear)
  cost_efficiency = cost_food+cost_lodging+cost_worker+casework_efficiency      (linear $/service, capped 1)
  -> marginal cost of a dollar is CONSTANT; bankruptcy is barely worse than thrift.

Convex prototype:  score_cx = satisfaction - convex_budget_cost(final_budget)
  convex_budget_cost(b) = W * (max(0, (B_SAFE - b)) / B_SAFE) ** P
  -> 0 penalty while b >= B_SAFE (so spending a healthy surplus is "free" -> no reason to hoard),
     grows convexly (power P) as b falls below B_SAFE, and explodes for negative b (bankruptcy).
"""
import json, glob, statistics as st

B_SAFE = 5000.0   # budget below which risk starts mattering
P = 2.0           # convexity (2 = quadratic hinge; raise for sharper)
W = 1.0           # weight (scale of the budget-risk term)

def convex_budget_cost(b):
    return W * (max(0.0, (B_SAFE - b)) / B_SAFE) ** P

POLICIES = [("rules-based","v3_rules-based"),("greedy","v3_greedy"),
            ("random","v3_random"),("noop","v3_noop")]

def load(d):
    rs=[json.loads(l) for l in open(f"benchmark_results/{d}/episodes.jsonl")]
    return [r for r in rs if r.get("summary") and not r.get("error") and r.get("rounds")]

def ep_stats(r):
    fc=r["rounds"][-1].get("comps") or {}
    sat=fc.get("satisfaction", 0.0)
    cost=fc.get("cost_efficiency", 0.0)
    fb=r["summary"].get("finalBudget", 0.0) or 0.0
    minb=min((rd.get("budget") for rd in r["rounds"] if rd.get("budget") is not None), default=fb)
    return sat, cost, fb, minb

def summarize(name, eps):
    S=[ep_stats(r) for r in eps]
    sat=st.mean(x[0] for x in S); cost=st.mean(x[1] for x in S)
    fb=st.mean(x[2] for x in S); minb=st.mean(x[3] for x in S)
    cur = sat - cost
    cx_cost_fb  = st.mean(convex_budget_cost(x[2]) for x in S)   # on final budget
    cx_cost_min = st.mean(convex_budget_cost(x[3]) for x in S)   # on min budget reached
    cx_fb  = sat - cx_cost_fb
    cx_min = sat - cx_cost_min
    return dict(name=name,n=len(eps),sat=sat,cost=cost,cur=cur,fb=fb,minb=minb,
                cxcost_fb=cx_cost_fb,cx_fb=cx_fb,cxcost_min=cx_cost_min,cx_min=cx_min)

rows=[]
for label,d in POLICIES:
    try: rows.append(summarize(label, load(d)))
    except FileNotFoundError: pass
# cheap LLMs pooled per model
from collections import defaultdict
bym=defaultdict(list)
for l in open("benchmark_results/v3_cheap/episodes.jsonl"):
    r=json.loads(l)
    if r.get("summary") and not r.get("error") and r.get("rounds"): bym[r["model"].split("/")[-1][:14]].append(r)
for m,eps in bym.items(): rows.append(summarize(m, eps))

print(f"Convex curve: cost = {W}*(max(0,({B_SAFE:.0f}-b))/{B_SAFE:.0f})^{P}  (0 while budget>= {B_SAFE:.0f})\n")
print(f"{'policy':14}{'sat':>6}{'finBud':>9}{'minBud':>9} | {'curCost':>8}{'curScore':>9} | {'cxCost(fb)':>11}{'cxScore(fb)':>12}{'cxScore(min)':>13}")
for r in sorted(rows, key=lambda x:-x['cur']):
    print(f"{r['name']:14}{r['sat']:>6.2f}{r['fb']:>9.0f}{r['minb']:>9.0f} | {r['cost']:>8.2f}{r['cur']:>9.2f} | {r['cxcost_fb']:>11.2f}{r['cx_fb']:>12.2f}{r['cx_min']:>13.2f}")
print("\nRanking by CURRENT score:", [r['name'] for r in sorted(rows,key=lambda x:-x['cur'])])
print("Ranking by CONVEX(min) :", [r['name'] for r in sorted(rows,key=lambda x:-x['cx_min'])])
