"""Benchmark results: per-model aggregates, the console table, and WandB logging (game/* metrics
named like the RL runs' so the two overlay on one project)."""
from __future__ import annotations


def mean(xs):
    xs = [x for x in xs if x is not None]
    return sum(xs) / len(xs) if xs else None


def aggregate(records):
    by_model = {}
    for r in records:
        by_model.setdefault(r["model"], []).append(r)
    out = {}
    for model, recs in by_model.items():
        ok = [r for r in recs if r.get("summary") and not r.get("error")]
        s = [r["summary"] for r in ok]
        out[model] = {
            "episodes": len(recs), "completed": len(ok),
            "errors": [r["error"].splitlines()[0] for r in recs if r.get("error")][:5],
            "meanTotalReward": mean([x["totalReward"] for x in s]),
            "meanFinalSat": mean([x["finalSat"] for x in s]),
            "meanFinalBudget": mean([x["finalBudget"] for x in s]),
            "meanFoodFulfill": mean([x["foodFulfillRate"] for x in s]),
            "meanLodgingFulfill": mean([x["lodgingFulfillRate"] for x in s]),
            "meanActionFailures": mean([x["actionFailures"] for x in s]),
            "meanInvalidIdx": mean([x["invalidIndices"] for x in s]),
            "fracWentNegative": mean([1.0 if x["wentNegative"] else 0.0 for x in s]),
            "fracTerminated": mean([1.0 if x["terminated"] else 0.0 for x in s]),
            "fracNeverBuilt": mean([0.0 if x["everBuilt"] else 1.0 for x in s]),
            "fracNeverHired": mean([0.0 if x["everHired"] else 1.0 for x in s]),
        }
    return out


def print_table(agg):
    cols = [("model", 34), ("ep", 4), ("reward", 8), ("sat", 6), ("food%", 7),
            ("lodg%", 7), ("fail", 6), ("neg%", 6), ("term%", 6)]
    hdr = "".join(name.ljust(w) for name, w in cols)
    print("\n" + hdr); print("-" * len(hdr))
    for model, a in sorted(agg.items(), key=lambda kv: -(kv[1]["meanTotalReward"] or -1e9)):
        def f(v, p="{:.2f}"): return "-" if v is None else p.format(v)
        row = [model[:33], f"{a['completed']}/{a['episodes']}", f(a["meanTotalReward"]),
               f(a["meanFinalSat"], "{:.0f}"), f(a["meanFoodFulfill"]), f(a["meanLodgingFulfill"]),
               f(a["meanActionFailures"], "{:.1f}"), f(a["fracWentNegative"]), f(a["fracTerminated"])]
        print("".join(str(c).ljust(w) for c, (_, w) in zip(row, cols)))


_WB_COMP = ["sat_food", "sat_lodging", "sat_worker_use", "sat_waste", "sat_casework",
            "eff_food", "eff_lodging", "eff_worker"]


def log_wandb(records, project, condition, episodes, rounds):
    """Log one WandB run per model with game/* metrics matching the Verlog RL runs,
    so benchmark and RL overlay on the same project. Two series per run:
      - per-step (step/*): mean across episodes at each round   (x-axis = round)
      - per-episode (ep/*): each episode's summary               (x-axis = episode)
    Metric names mirror the env's info['metrics'] game/* keys."""
    try:
        import wandb
    except ImportError:
        print("⚠️  wandb not installed; skipping WandB logging (pip install wandb)")
        return
    entity, _, proj = project.partition("/")
    if not proj:
        entity, proj = None, project

    def avg(vals):
        vals = [v for v in vals if v is not None]
        return sum(vals) / len(vals) if vals else None

    by = {}
    for r in records:
        by.setdefault(r["model"], []).append(r)

    for model, recs in by.items():
        ok = [r for r in recs if r.get("rounds") and not r.get("error")]
        if not ok:
            continue
        short = (model.split("/")[-1].replace("us.anthropic.", "")
                 .replace("-20251001-v1:0", "").replace(":0", ""))
        wandb.init(entity=entity, project=proj, reinit=True,
                   name=f"bench-{short}-{condition}", group=f"benchmark-{condition}",
                   job_type="benchmark", tags=["benchmark", condition, short],
                   config={"model": model, "condition": condition, "episodes": episodes,
                           "rounds": rounds, "n_completed": len(ok), "source": "llm_benchmark"})
        wandb.define_metric("round"); wandb.define_metric("step/*", step_metric="round")
        wandb.define_metric("episode"); wandb.define_metric("ep/*", step_metric="episode")

        # ── per-step series: mean across episodes at each round ──
        maxr = max(len(r["rounds"]) for r in ok)
        for t in range(maxr):
            at = [r["rounds"][t] for r in ok if len(r["rounds"]) > t]
            if not at:
                continue
            row = {"round": t,
                   "step/game/satisfaction": avg([rd.get("sat") for rd in at]),
                   "step/game/budget": avg([rd.get("budget") for rd in at]),
                   "step/game/satisfaction_score": avg([rd.get("satScore") for rd in at]),
                   "step/game/efficiency": avg([rd.get("eff") for rd in at]),
                   "step/game/reward": avg([rd.get("reward") for rd in at]),
                   "step/game/score": avg([rd.get("sumR") for rd in at])}
            for c in _WB_COMP:
                row["step/game/" + c] = avg([(rd.get("comps") or {}).get(c) for rd in at])
            wandb.log({k: v for k, v in row.items() if v is not None})

        # ── per-episode series ──
        for r in sorted(ok, key=lambda r: r["episode"]):
            s = r["summary"]; rds = r["rounds"]; last = rds[-1].get("comps") or {}
            row = {"episode": r["episode"],
                   "ep/game/score": s.get("finalScore"), "ep/totalReward": s.get("totalReward"),
                   "ep/game/satisfaction_final": s.get("finalSat"),
                   "ep/game/satisfaction_mean": avg([rd.get("sat") for rd in rds]),
                   "ep/game/finalBudget": s.get("finalBudget"), "ep/game/minBudget": s.get("minBudget"),
                   "ep/game/foodFulfill": s.get("foodFulfillRate"),
                   "ep/game/lodgingFulfill": s.get("lodgingFulfillRate"),
                   "ep/actionFailures": s.get("actionFailures"),
                   "ep/wentNegative": 1.0 if s.get("wentNegative") else 0.0,
                   "ep/terminated": 1.0 if s.get("terminated") else 0.0}
            for c in _WB_COMP:
                if c in last:
                    row["ep/game/" + c + "_final"] = last[c]
            wandb.log({k: v for k, v in row.items() if v is not None})

        # ── run-level summary (means over episodes) ──
        S = [r["summary"] for r in ok]
        for src, dst in [("totalReward", "totalReward"), ("finalSat", "finalSat"),
                         ("foodFulfillRate", "foodFulfill"), ("lodgingFulfillRate", "lodgingFulfill"),
                         ("minBudget", "minBudget"), ("finalBudget", "finalBudget")]:
            wandb.run.summary["mean/" + dst] = avg([x.get(src) for x in S])
        wandb.finish()

    print(f"WandB: logged {len(by)} model run(s) to {project} (condition={condition})")
