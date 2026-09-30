import json, sys, statistics as st
from collections import Counter


def load(d):
    rs = [json.loads(l) for l in open(f"benchmark_results/{d}/episodes.jsonl")]
    return [r for r in rs if r.get("summary") and r.get("rounds") and not r.get("error")]


def analyze(eps):
    n = len(eps)

    def sm(k):
        v = [e["summary"].get(k) for e in eps if e["summary"].get(k) is not None]
        return st.mean(v) if v else 0

    fc = [e["rounds"][-1]["comps"] for e in eps if e["rounds"][-1].get("comps")]
    comp = lambda k: st.mean([c.get(k, 0) for c in fc]) if fc else 0
    acts = Counter()
    for e in eps:
        for rd in e["rounds"]:
            for k, v in (rd.get("actCats") or {}).items():
                acts[k] += v
    d = {"n": n, "reward": sm("totalReward"),
         "sat": comp("satisfaction"), "cost": comp("cost_efficiency"),
         "sat_food": comp("sat_food"), "sat_lodging": comp("sat_lodging"), "sat_worker": comp("sat_worker_use"),
         "cost_food": comp("cost_food"), "cost_lodging": comp("cost_lodging"), "cost_worker": comp("cost_worker"),
         "foodFul": sm("foodResolved") * sm("foodFulfillRate"), "foodRes": sm("foodResolved"),
         "lodgFul": sm("lodgingResolved") * sm("lodgingFulfillRate"), "lodgRes": sm("lodgingResolved"),
         "finBud": sm("finalBudget"), "minBud": sm("minBudget"), "finSat": sm("finalSat"),
         "builds": acts.get("construction", 0) / n, "hires": acts.get("worker", 0) / n}
    d["actCats"] = {k: round(v / n, 2) for k, v in acts.items()}
    return d


gname, pname = sys.argv[1], sys.argv[2]
g, p = analyze(load(gname)), analyze(load(pname))
print(f"GREEDY={gname} (n={g['n']})   POTENTIAL={pname} (n={p['n']})\n")
print(f"{'metric':16}{'GREEDY':>11}{'POTENTIAL':>12}{'D(P-G)':>10}")
for k in ["reward", "sat", "cost", "sat_food", "sat_lodging", "sat_worker",
          "cost_food", "cost_lodging", "cost_worker", "finSat", "finBud", "minBud", "builds", "hires"]:
    print(f"{k:16}{g[k]:11.3f}{p[k]:12.3f}{p[k]-g[k]:+10.3f}")
print()
for nm, a in (("food", "food"), ("lodg", "lodg")):
    gf, gr = g[f"{nm}Ful"], g[f"{nm}Res"]
    pf, pr = p[f"{nm}Ful"], p[f"{nm}Res"]
    print(f"  {nm:5} GREEDY {gf:.1f}/{gr:.1f} ({gf/max(gr,1)*100:.0f}%)   POTENTIAL {pf:.1f}/{pr:.1f} ({pf/max(pr,1)*100:.0f}%)")
print(f"\ngreedy actCats/ep: {g['actCats']}")
print(f"potl   actCats/ep: {p['actCats']}")
