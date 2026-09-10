"""Play an evolved plan on the REAL headless server and capture it in the replay-harness
format, so test_replay_forward / diag_magnitude judge whether the surrogate is exact on a
trajectory it was never calibrated on.

    python -m cora_sim.validate_plan cora_sim/runs/evo14.jsonl 5901 --port 21050

Picks the best-scoring row for that Unity seed, launches headless with the same seed, and
each round does exactly what the surrogate's CoraActions.apply did: answer every open task
(repair -> choice 1, else the plan's choice for that task type if offered, else the first),
staff every NeedWorker building, then execute the menu actions the surrogate actually
executed that round, in order. Writes staff_<seed>.json/.log next to --out."""
from __future__ import annotations

import argparse

from cora_sim import paths as P
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.actions import REPAIR_CHOICE   # noqa: E402
from cora_sim.economy import REQUIRED_WORKFORCE   # noqa: E402

# The bundle's executable is named after productName, which has changed once already;
# ARC_HEADLESS_EXE overrides for a non-standard layout (the cluster's Linux build).
EXE = os.environ.get("ARC_HEADLESS_EXE") or (
    "Build/Headless/macOS/ARC_Headless.app/Contents/MacOS/"
    "Collaborative Operations And Resource Management with Agentic AI")


def best_row(log, unity_seed):
    best = None
    with open(log) as f:
        for line in f:
            r = json.loads(line)
            if r.get("unity_seed") == unity_seed and (best is None or r["score"] > best["score"]):
                best = r
    if best is None:
        raise SystemExit(f"no rows for unity seed {unity_seed} in {log}")
    return best


def play(row, port, out_dir, rounds=32, replay=None):
    """The surrogate drives Unity in lockstep: each round the surrogate applies the gene
    first, and whatever it did -- which tasks it answered with what, which buildings it
    staffed in which order, which menu actions survived legality -- is sent to Unity in the
    same order. Then both advance and their counters are compared. Order matters: staffing
    the Kitchen before the Shelter puts the trained workers in a different building.
    `replay` (a previous staff_<seed>.json) sends THAT capture's recorded actions instead of the
    surrogate's live decisions, so two Unity builds can be driven with identical inputs and
    their captures diffed (compare_captures): the only differences left are the game's."""
    import random
    from cora_sim.searchable_env import SearchableEnv
    from cora_sim.actions import CoraActions
    from cora_sim.evolve import fresh_world, captured_seeds
    from cora_sim.floodmap import FloodMap
    import cora_sim.sim as S
    seed = row["unity_seed"]
    out = os.path.abspath(os.path.join(out_dir, f"staff_{seed}.json"))
    if os.path.exists(out):
        raise SystemExit(f"refusing to overwrite {out}")
    if row.get("seed_state"):                 # evolve.py stores the xorshift state per row;
        state = tuple(int(x) & 0xFFFFFFFF for x in row["seed_state"])   # the old captures are gone
    else:
        state = dict(captured_seeds())[seed]
    w = fresh_world(state, FloodMap.load())
    model = CoraActions(random.Random(0))
    os.environ["ARC_SNAPSHOT_DEBUG"] = "1"      # RNGCTX marks: diag_marks needs them to find the first divergent draw
    env = SearchableEnv(unity_exe_path=EXE, unity_port=port, seed=seed, auto_start_unity=True,
                        connection_timeout=120, unity_log_path=out.replace(".json", ".log"))
    env.reset()
    trace, first_diff = [], None
    for i in range(rounds):
        before = env._game_state_dict()
        gene = row["plan"][i] if i < len(row["plan"]) else {"choices": {}, "menu": []}
        taken = []
        rec = replay[i]["taken"] if replay is not None and i < len(replay) else None

        def run(a, label):
            try:
                r = env.execute(json.dumps(a))
                taken.append({"kind": label, "action_id": a.get("action_id"), "action_type": a.get("action_type"),
                              "cost": a.get("cost"), "ok": bool(r and r.get("success", True)), "payload": a})
            except Exception as e:
                taken.append({"kind": label, "action_id": a.get("action_id"), "error": str(e)})

        # 1. the surrogate's turn, recording what it decided (or the recorded turn, replayed)
        econ = w.economy
        answered = {}
        if rec is not None:
            from cora_sim.diag_lockstep import drive_step
            drive_step(w, model, replay[i], gene)          # the port takes the recorded actions too
            by_type = {}
            for x in rec:
                if x.get("kind") == "choice" and "choiceId" in x:
                    by_type.setdefault(x.get("stableTaskId") or "", []).append(x["choiceId"])
            staffed = []
            executed = [x.get("action_id") for x in rec if x.get("kind") == "menu" and x.get("action_id")]
        else:
            offered = {}
            for tid, cid in S.open_choices(w):
                offered.setdefault(tid, []).append(cid)
            for tid, cids in offered.items():
                spec = (w.generated_specs.get(tid) or ("Repair",))[0]
                if tid in w.tasks.repair_for:
                    answered[tid] = ("Repair", REPAIR_CHOICE)
                else:
                    want = gene["choices"].get(spec)
                    answered[tid] = (spec, want if want in cids else cids[0])
            assigned_before = [b.get("assigned", 0) for b in econ.buildings]
            executed = model.apply(w, ("turn", gene)) or []
            staffed = [(b.get("site_id"), b["type"], b.get("assigned", 0) - a0)
                       for b, a0 in zip(econ.buildings, assigned_before) if b.get("assigned", 0) != a0]
            by_type = {}
            for spec, cid in answered.values():
                by_type.setdefault(spec, []).append(cid)

        # 2. the same turn on Unity: choices by task type, staffing by site in the same order
        for t in (before.get("allActiveTasks") or []):
            cids = [c.get("choiceId") for c in (t.get("choices") or [])]
            if not cids:
                continue
            sid = t.get("stableTaskId") or ""
            if not sid:
                sid = S.CODE_BUILT_TASKS.get(str(t.get("taskTitle")), "")   # built in code, no TaskData id
            key = "Repair" if ("Repair" in sid or "Repair" in str(t.get("taskTitle"))) else sid
            queue = by_type.get(key)
            want = queue.pop(0) if queue else (REPAIR_CHOICE if key == "Repair" else cids[0])
            want = want if want in cids else cids[0]
            try:
                env.choose(t.get("taskId"), want)
                taken.append({"kind": "choice", "taskId": t.get("taskId"), "choiceId": want, "stableTaskId": sid})
            except Exception as e:
                taken.append({"kind": "choice", "error": str(e)})
        facilities = (before.get("mapState") or {}).get("facilities") or []
        for site, btype, _delta in staffed:
            f = next((f for f in facilities if f.get("originalSiteId") == site and f.get("buildingType") == btype
                      and f.get("buildingStatus") == "NeedWorker"), None)
            if f is None:
                taken.append({"kind": "staff", "error": f"no NeedWorker {btype} at site {site} in Unity"})
                continue
            run({"action_type": "worker_assignment", "action_id": f"staff_{f.get('facilityName')}", "cost": 0,
                 "assignment": {"building_name": f.get("facilityName"), "quantity": REQUIRED_WORKFORCE}}, "staff")
        if rec is not None:
            for x in rec:                            # recorded staffing, verbatim and in order
                if x.get("kind") == "staff" and x.get("payload"):
                    run(x["payload"], "staff")
        acts = {a.get("action_id"): a for a in (env.get_valid_actions() or [])}
        for aid in executed:
            if aid.startswith("staff_"):
                continue                     # already sent, in the surrogate's order
            a = acts.get(aid)
            if a is None:
                taken.append({"kind": "menu", "action_id": aid, "error": "not offered by Unity"})
                continue
            run(a, "menu")

        # 3. advance both, compare
        S.step_round(w)
        env.advance_round()
        after = env._game_state_dict()
        trace.append({"round": i, "before": before, "taken": taken, "after": after})
        um, sm = after.get("rewardMetrics") or {}, w.economy.metrics()
        ub, sb = (after.get("satisfactionAndBudget") or {}).get("budget"), w.economy.budget
        diffs = [k for k in ("foodResolved", "foodFulfilled", "lodgingResolved", "lodgingFulfilled", "cumWorkingWorkers",
                             "totalWorkers", "caseworkProcessed") if um.get(k) != sm.get(k)]
        if ub != sb:
            diffs.append(f"budget {ub} vs {sb}")
        if diffs and first_diff is None:
            first_diff = (i, diffs)
        print(f"  r{i:02d} budget U={ub} S={sb} work U={um.get('cumWorkingWorkers')} S={sm.get('cumWorkingWorkers')} "
              f"food {um.get('foodFulfilled')}/{um.get('foodResolved')} lodging {um.get('lodgingFulfilled')}/{um.get('lodgingResolved')} "
              f"sent={[x.get('action_id') or x.get('choiceId') for x in taken]}" + ("  <-- DIFF " + ",".join(map(str, diffs)) if diffs else ""),
              flush=True)
    # Stamp the capture with the build that produced it, so obs_diff can refuse to compare a
    # corpus exported from one build against a capture taken from another.
    try:
        _consts = env._send_request({"type": "sim_constants"}) or {}
    except Exception:
        _consts = {}
    env.close()
    json.dump(trace, open(out, "w"))
    json.dump({"buildGUID": _consts.get("buildGUID"), "seed": row.get("unity_seed"), "rounds": len(trace)},
              open(out.replace(".json", ".meta.json"), "w"))
    import reward_scoring
    print(f"wrote {out}")
    print(f"surrogate score {model.value(w):.4f}   unity score {reward_scoring.compute_score(after.get('rewardMetrics') or {})[2]:.4f}"
          f"   first divergence: {first_diff}")
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("log"); ap.add_argument("unity_seed", type=int)
    ap.add_argument("--port", type=int, default=21050)
    ap.add_argument("--out", default=P.VALIDATE)
    ap.add_argument("--rounds", type=int, default=32)
    ap.add_argument("--param-config", default=None, help="CSV parameter sheet exported to the Unity process as ARC_PARAM_CONFIG")
    ap.add_argument("--replay", default=None, help="staff_<seed>.json whose recorded actions are sent instead of the surrogate's")
    args = ap.parse_args()
    if args.param_config:
        os.environ["ARC_PARAM_CONFIG"] = os.path.abspath(args.param_config)
    os.makedirs(args.out, exist_ok=True)
    row = best_row(args.log, args.unity_seed)
    print(f"seed {args.unity_seed}: surrogate score {row['score']:.4f}; executed rounds "
          f"{[(i, a) for i, a in enumerate(row.get('executed', [])) if a]}")
    replay = json.load(open(args.replay)) if args.replay else None
    play(row, args.port, args.out, args.rounds, replay=replay)
    return 0


if __name__ == "__main__":
    sys.exit(main())
