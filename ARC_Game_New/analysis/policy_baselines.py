#!/usr/bin/env python3
"""Load the scripted-policy baselines as rows shaped like components_per_run.csv / spend_per_run.csv.

WHY THIS FILE EXISTS
The two analysis CSVs cover LLM runs only -- no scripted policy appears in either. The policy
scores that do exist (analysis_assets_v2/benchmark_v3_all.csv) carry no dollar figures at all,
and the cost_* components cannot be converted into dollars: measured against the LLM runs the
dollars-per-cost-unit ratio spans 5.0k-3.5M for lodging, so it is episode-relative, not a
constant. The dollars have to come from the episode records.

They do exist, in `rounds[-1].obs.spend` -- the cumulative {food, lodging, worker, casework}
block the observation encoder emits.

WHICH BASELINE SET, AND WHY IT MATTERS A LOT
Use baselines_35118 (2026-08-21, 32 episodes x 32 rounds, all six policies). It is
CONTEMPORANEOUS with the LLM cluster runs, which land 2026-08-21 16:36 through 2026-08-25.

Do NOT use baselines_33217 (2026-08-18). It predates the LLM runs and is an outlier on both
axes: build-potential scores 0.706 there versus 0.790-0.800 in six later runs, and spends
$41.7k versus $376-380k. Something in the game changed between Aug 18 and Aug 21. Comparing
33217 against the LLM runs is comparing across that change, and it inverts the headline --
it makes the policies look 6x cheaper than the models when on matched runs they are not.

An earlier version of this file used 33217. That was wrong.

"""
import glob, json, os, statistics as st

BASELINE_DIR = "benchmark_results/cluster_api/baselines_35118"
COMPS = ["sat_food", "sat_lodging", "sat_worker_use", "casework_processing_sat",
         "cost_food", "cost_lodging", "cost_worker", "casework_efficiency",
         "satisfaction", "cost_efficiency", "score"]
CATS = ["food", "lodging", "worker", "casework"]


def _sem(v):
    return st.stdev(v) / (len(v) ** 0.5) if len(v) > 1 else 0.0


def load_policies(base=BASELINE_DIR):
    """-> (component_rows, spend_rows), same column names as the two analysis CSVs."""
    comp_rows, spend_rows = [], []
    for d in sorted(glob.glob(os.path.join(base, "*"))):
        path = os.path.join(d, "episodes.jsonl")
        if not os.path.exists(path):
            continue
        name = os.path.basename(d)
        per = {k: [] for k in COMPS}
        sp = {k: [] for k in CATS}
        n = 0
        for line in open(path):
            ep = json.loads(line)
            rounds = ep.get("rounds") or []
            if not rounds:
                continue
            n += 1
            last = rounds[-1]                      # components and spend are both cumulative
            for k in COMPS:
                v = (last.get("comps") or {}).get(k)
                if v is not None:
                    per[k].append(float(v))
            s = (last.get("obs") or {}).get("spend") or {}
            for k in CATS:
                if s.get(k) is not None:
                    sp[k].append(float(s[k]))
        if not n:
            continue
        base_row = {"dir": f"baselines_33217/{name}", "model": f"policy: {name}",
                    "cfg": "scripted", "n": str(n)}
        c = dict(base_row)
        for k in COMPS:
            if per[k]:
                c[f"{k}_mean"] = str(st.mean(per[k]))
                c[f"{k}_sem"] = str(_sem(per[k]))
        comp_rows.append(c)

        s_row = dict(base_row)
        totals = []
        for k in CATS:
            if sp[k]:
                s_row[f"{k}_mean"] = str(st.mean(sp[k]))
                s_row[f"{k}_sem"] = str(_sem(sp[k]))
        per_ep_tot = [sum(sp[k][i] for k in CATS if i < len(sp[k])) for i in range(n)]
        s_row["total_mean"] = str(st.mean(per_ep_tot)) if per_ep_tot else "0"
        s_row["total_sem"] = str(_sem(per_ep_tot)) if per_ep_tot else "0"
        spend_rows.append(s_row)
    return comp_rows, spend_rows


if __name__ == "__main__":
    c, s = load_policies()
    print(f"{'policy':<20} {'n':>3} {'score/4':>8} {'total $k':>9}")
    for cr, sr in zip(c, s):
        print(f"{cr['model']:<20} {cr['n']:>3} {float(cr['score_mean'])/4:>8.3f} "
              f"{float(sr['total_mean'])/1000:>9.1f}")
