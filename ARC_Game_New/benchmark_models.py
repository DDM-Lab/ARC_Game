"""
Flagship-model benchmark for the ARC gym environment.

Runs N full episodes per model with the SAME rules-only prompt + rich observation
as llm_smoke_test.py, then reports per-model performance and a shared "mistake"
profile. The point is to separate three explanations for poor play:
  (a) some models play well and others don't  -> model decision-making differs
  (b) all models fail the SAME way            -> prompt/observation/env issue
  (c) all models play well                    -> the game is easy / obs is fine

Each episode gets a FRESH headless Unity process (clean game) — the gym server has
no in-place reset, so we relaunch per episode on a per-worker port. Episodes can run
concurrently across workers (each worker owns one port + one Unity process).

This is an EVAL harness: it reuses ARCGameGymEnv.reset()/step() and the smoke-test's
summarize()/ask()/prompt verbatim — it is not a new rollout engine and does not patch
the env. All reward scoring stays in arc_game_gym_env_tcp.compute_score.

Usage:
  python benchmark_models.py [--episodes N] [--rounds R] [--workers K]
                             [--models m1,m2,...] [--out DIR] [--validate]

  --validate runs ONE 2-round no-LLM (no-op) episode to confirm the fresh-process
  lifecycle works before spending any API budget.
"""
import os, sys, json, argparse, traceback, queue
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed

sys.path.insert(0, str(Path(__file__).parent))
from arc_game_gym_env_tcp import ARCGameGymEnv
import llm_smoke_test as smoke
import openai

HEADLESS_EXE = "Build/Headless/macOS/ARC_Headless.app/Contents/MacOS/ARC_DisasterSimulation"
BASE_PORT = 9900

# Cross-vendor flagships available on the CMU gateway (edit via --models).
DEFAULT_MODELS = [
    "us.anthropic.claude-opus-4-8",
    "us.anthropic.claude-sonnet-4-6",
    "gpt-5.5",
    "gpt-5.4-pro",
    "gemini/gemini-3.1-pro-preview",
    "gemini-2.5-pro",
]


# ── Robust chat: gateway models disagree on token-limit param name ──────────
def chat(client, model, messages, max_tokens=2000):
    """OpenAI-compatible call that tolerates per-vendor param quirks.

    Returns (content, reasoning_trace) — reasoning_trace is the provider's hidden
    chain-of-thought when the gateway surfaces it (reasoning_content / reasoning),
    else None. content is the visible message text (may itself contain <think>…).
    """
    try:
        r = client.chat.completions.create(model=model, max_tokens=max_tokens, messages=messages)
    except Exception as e:
        msg = str(e).lower()
        # gpt-5 / reasoning models want max_completion_tokens instead of max_tokens.
        if "max_tokens" in msg or "max_completion_tokens" in msg:
            r = client.chat.completions.create(
                model=model, max_completion_tokens=max_tokens, messages=messages)
        else:
            raise
    m = r.choices[0].message
    content = m.content or ""
    # Provider reasoning tokens land in non-standard fields depending on vendor/gateway.
    extra = getattr(m, "model_extra", None) or {}
    rtrace = (getattr(m, "reasoning_content", None) or getattr(m, "reasoning", None)
              or extra.get("reasoning_content") or extra.get("reasoning"))
    return content, (rtrace if isinstance(rtrace, str) else None)


def _parse_decision(content):
    """Lenient JSON extraction from an LLM response. Models (esp. gemini) sometimes
    wrap JSON in code fences or emit trailing commas; a single malformed response
    should degrade to a no-op round, never crash the episode."""
    import re
    m = re.search(r"\{.*\}", content or "", re.S)
    if not m:
        return {"choices": [], "actions": []}, False
    raw = m.group(0)
    for candidate in (raw, re.sub(r",\s*([}\]])", r"\1", raw)):  # try as-is, then strip trailing commas
        try:
            return json.loads(candidate), True
        except Exception:
            continue
    return {"choices": [], "actions": []}, False


def ask(client, model, state):
    """Return (decision_dict, raw_content, reasoning_trace, parsed_ok). raw_content is
    the full visible response (kept verbatim so we can inspect any prose/think emitted)."""
    content, rtrace = chat(client, model, [
        {"role": "system", "content": smoke.SYSTEM_PROMPT},
        {"role": "user", "content": "State:\n" + json.dumps(state) + "\n\nJSON decision:"}])
    dec, ok = _parse_decision(content)
    return dec, content, rtrace, ok


# ── One episode ─────────────────────────────────────────────────────────────
def run_episode(model, ep_idx, rounds, port, client, validate=False, port_pool=None, log_dir=None, show_impacts=True):
    """Fresh Unity process -> play `rounds` -> structured per-episode record.

    If port_pool (a Queue) is given, lease a unique port for the lifetime of this
    episode so concurrent episodes never collide on a port (scheduling-safe)."""
    if port_pool is not None:
        port = port_pool.get()
    ulog = None
    if log_dir:
        safe = model.replace("/", "_").replace(":", "_")
        ulog = str((Path(log_dir) / f"unity_{safe}_ep{ep_idx}.log").resolve())
    env = ARCGameGymEnv(unity_exe_path=HEADLESS_EXE, unity_port=port,
                        auto_start_unity=True, max_episode_steps=rounds + 5,
                        unity_log_path=ulog)
    rec = {"model": model, "episode": ep_idx, "rounds": [], "error": None, "show_impacts": show_impacts}
    try:
        env.reset()
        total = 0.0
        actions_requested = actions_executed = action_failures = invalid_idx = 0
        min_budget = float("inf")
        built = hired = False
        for rnd in range(rounds):
            state = smoke.summarize(env, show_impacts=show_impacts)
            n_valid = len(state["actions"])
            raw = rtrace = None
            if validate:
                dec = {"choices": [], "actions": []}            # no-op
            else:
                try:
                    dec, raw, rtrace, parsed_ok = ask(client, model, state)
                    if not parsed_ok:
                        # one unparseable response -> no-op this round, keep playing
                        rec["parse_failures"] = rec.get("parse_failures", 0) + 1
                except Exception as e:
                    # hard API/network error: end the episode
                    rec["error"] = f"LLM error r{rnd}: {e}"
                    break
            # task choices
            nsel = 0
            for c in dec.get("choices", []):
                try:
                    if env.select_task_choice(int(c["taskId"]), int(c["choiceId"])):
                        nsel += 1
                except Exception:
                    pass
            # actions: count requested / invalid; tally the per-turn category mix
            # (the model's intended strategy: game-action types + task-choice by task type)
            req = [int(a) for a in dec.get("actions", []) if str(a).lstrip("-").isdigit()]
            actions_requested += len(req)
            invalid_idx += sum(1 for a in req if a < 0 or a >= n_valid)
            act_cats = {}
            tmap = {t["taskId"]: t.get("type", "?") for t in state.get("tasks", [])}
            for c in dec.get("choices", []):
                try:
                    cat = "choice:" + str(tmap.get(int(c["taskId"]), "?"))
                except Exception:
                    cat = "choice:?"
                act_cats[cat] = act_cats.get(cat, 0) + 1
            for a in req:
                if 0 <= a < n_valid:
                    at = state["actions"][a].get("type") or "?"
                    act_cats[at] = act_cats.get(at, 0) + 1
                    if at == "construction": built = True
                    if at == "worker": hired = True
            obs, reward, term, trunc, info = env.step(",".join(str(a) for a in req))
            total += reward
            exres = info.get("execution_results") or []
            actions_executed += sum(1 for r in exres if r.get("success"))
            action_failures += sum(1 for r in exres if not r.get("success"))
            min_budget = min(min_budget, info.get("budget", 0.0))
            rm = info.get("reward_metrics") or {}
            rec["rounds"].append({
                "r": rnd, "reward": round(reward, 4), "sumR": round(total, 4),
                "sat": info["satisfaction"], "budget": info["budget"],
                "satScore": round(info["satisfaction_score"], 4),
                "costEff": round(info["cost_efficiency"], 4),
                # full reward breakdown (cumulative-to-date) for per-component graphing
                "comps": {k: round(v, 4) for k, v in (info.get("score_components") or {}).items()},
                "foodFul": rm.get("foodFulfilled"), "foodRes": rm.get("foodResolved"),
                "lodgFul": rm.get("lodgingFulfilled"), "lodgRes": rm.get("lodgingResolved"),
                "nSel": nsel, "nReq": len(req), "nFail": sum(1 for r in exres if not r.get("success")),
                "actCats": act_cats,   # {category: count attempted this turn} — strategy mix

                "note": (dec.get("note") or "")[:80],
                "reasoning": (dec.get("reasoning") or "")[:1500],   # model's own rationale (JSON field)
                "raw": (raw or "")[:4000],                          # full visible response verbatim
                "reasoningTrace": (rtrace or "")[:4000] or None,    # provider hidden CoT if surfaced
            })
            if term or trunc:
                rec["terminated"] = bool(term)
                break
        # ── episode aggregates (the mistake profile) ──
        last = rec["rounds"][-1] if rec["rounds"] else {}
        fr, ff = last.get("foodRes") or 0, last.get("foodFul") or 0
        lr, lf = last.get("lodgRes") or 0, last.get("lodgFul") or 0
        rec["summary"] = {
            "totalReward": round(total, 4),
            "finalSat": last.get("sat"), "finalBudget": last.get("budget"),
            "finalScore": round(last.get("sumR", 0.0), 4),
            "foodFulfillRate": round(ff / fr, 3) if fr else None,
            "lodgingFulfillRate": round(lf / lr, 3) if lr else None,
            "foodResolved": fr, "lodgingResolved": lr,
            "actionsRequested": actions_requested, "actionsExecuted": actions_executed,
            "actionFailures": action_failures, "invalidIndices": invalid_idx,
            "minBudget": None if min_budget == float("inf") else min_budget,
            "wentNegative": (min_budget < 0) if min_budget != float("inf") else None,
            "everBuilt": built, "everHired": hired,
            "terminated": rec.get("terminated", False),
            "roundsPlayed": len(rec["rounds"]),
        }
    except Exception as e:
        rec["error"] = f"{e}\n{traceback.format_exc()}"
    finally:
        env.close()
        if port_pool is not None:
            port_pool.put(port)
    return rec


# ── Aggregation ─────────────────────────────────────────────────────────────
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


_WB_COMP = ["sat_food", "sat_lodging", "sat_worker_use", "cost_food", "cost_lodging", "cost_worker"]


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
                   "step/game/cost_efficiency": avg([rd.get("costEff") for rd in at]),
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


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--episodes", type=int, default=20)
    ap.add_argument("--rounds", type=int, default=18)
    ap.add_argument("--workers", type=int, default=1)
    ap.add_argument("--models", default=",".join(DEFAULT_MODELS))
    ap.add_argument("--out", default="benchmark_results")
    ap.add_argument("--validate", action="store_true")
    # Ablation toggle: show each task choice's sparse impacts (Budget/Satisfaction/...)
    # in the observation. Unity always sends them; this controls what the model sees.
    ap.add_argument("--no-impacts", dest="impacts", action="store_false",
                    help="hide choice impacts from the observation (ablation baseline)")
    ap.set_defaults(impacts=True)
    # WandB: log one run per model with game/* + behavior metrics matching the RL runs,
    # so benchmark and Verlog RL are directly comparable on the same WandB project.
    ap.add_argument("--wandb", action="store_true", help="log results to Weights & Biases")
    ap.add_argument("--wandb-project", default="cpulling/CORA_RL",
                    help="entity/project (default cpulling/CORA_RL)")
    args = ap.parse_args()

    if not Path(HEADLESS_EXE).exists():
        sys.exit(f"Headless build not found: {HEADLESS_EXE}")

    if args.validate:
        print("=== VALIDATE: 1 no-LLM episode (2 rounds), fresh process ===")
        rec = run_episode("validate", 0, 2, BASE_PORT, None, validate=True)
        print(json.dumps(rec.get("summary") or {"error": rec.get("error")}, indent=2))
        return

    models = [m.strip() for m in args.models.split(",") if m.strip()]
    client = openai.OpenAI(api_key=smoke.load_env_key(), base_url=smoke.GATEWAY_BASE)
    outdir = Path(args.out); outdir.mkdir(parents=True, exist_ok=True)
    jsonl = outdir / "episodes.jsonl"

    jobs = [(m, e) for m in models for e in range(args.episodes)]
    print(f"=== Benchmark: {len(models)} models x {args.episodes} eps x {args.rounds} rounds "
          f"= {len(jobs)} episodes, {args.workers} worker(s) ===")
    print(f"    models: {models}")
    print(f"    observation choice-impacts: {'SHOWN' if args.impacts else 'HIDDEN (ablation)'}")

    port_pool = queue.Queue()
    for w in range(args.workers):
        port_pool.put(BASE_PORT + w)

    ulog_dir = outdir / "unity_logs"; ulog_dir.mkdir(exist_ok=True)

    records = []
    with open(jsonl, "w") as fh, ThreadPoolExecutor(max_workers=args.workers) as ex:
        futs = {}
        for model, ep in jobs:
            futs[ex.submit(run_episode, model, ep, args.rounds, None, client,
                           False, port_pool, str(ulog_dir), args.impacts)] = (model, ep)
        for fut in as_completed(futs):
            model, ep = futs[fut]
            rec = fut.result()
            records.append(rec)
            fh.write(json.dumps(rec) + "\n"); fh.flush()
            s = rec.get("summary") or {}
            print(f"  done {model} ep{ep}: reward={s.get('totalReward')} "
                  f"sat={s.get('finalSat')} food={s.get('foodFulfillRate')} "
                  f"lodg={s.get('lodgingFulfillRate')} {'ERR:'+rec['error'].splitlines()[0] if rec.get('error') else ''}")

    agg = aggregate(records)
    (outdir / "summary.json").write_text(json.dumps(agg, indent=2))
    print_table(agg)
    print(f"\nPer-episode: {jsonl}\nSummary:     {outdir/'summary.json'}")

    if args.wandb:
        cond = "impacts" if args.impacts else "no_impacts"
        log_wandb(records, args.wandb_project, cond, args.episodes, args.rounds)


if __name__ == "__main__":
    main()
