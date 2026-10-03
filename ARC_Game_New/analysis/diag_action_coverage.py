#!/usr/bin/env python3
"""Scripted action-coverage + parity driver for the CORA gym (diagnostic, local only).

Plays one seeded episode with a FIXED script that exercises every action surface the LLM
benchmark exposes, going through exactly the benchmark's path:

    typed tool calls -> cora.executor.execute_turn (resolve, answer tasks, run actions, advance)

Per round it records every call (tags, parse errors, resolved?, engine result) and a canonical
state snapshot. Run it with the same --seed on different builds (Mac headless, Mac render,
Linux server) and compare the JSON outputs with --compare to check game-state parity.

  python analysis/diag_action_coverage.py --exe <unity exe> --seed 7000 --rounds 32 --out a.json
  python analysis/diag_action_coverage.py --compare a.json b.json [c.json]
"""
import argparse, gzip, hashlib, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def _task_ref(t):
    tid = t.get("token") or t.get("id") or t.get("taskId")
    chs = t.get("choices") or []
    ids = []
    for c in chs:
        cid = c.get("id", c.get("choiceId")) if isinstance(c, dict) else None
        if cid is not None:
            ids.append(cid)
    return tid, ids


def script_calls(rnd, obs, built, deconstructed):
    """The fixed policy: (tool_name, args) list for this round."""
    calls = []
    av = obs.get("available") or {}
    sites = list(av.get("buildSites") or [])
    if rnd == 0:
        for typ in ("shelter", "kitchen", "casework"):
            if sites:
                calls.append(("build", {"type": typ, "site_id": sites.pop(0)}))
        calls.append(("hire", {"kind": "untrained", "count": 4}))
        calls.append(("hire", {"kind": "trained", "count": 2}))
        calls.append(("train", {"count": 1}))
    if rnd == 6 and sites:
        calls.append(("build", {"type": "shelter", "site_id": sites.pop(0)}))
        calls.append(("hire", {"kind": "untrained", "count": 8}))
    for name in (av.get("needStaff") or {}):
        calls.append(("staff", {"site": name}))
    for t in obs.get("tasks") or []:
        tid, ids = _task_ref(t)
        if tid is not None and ids:
            # rotate through the offered choices across rounds so every choice slot gets used
            calls.append(("task", {"task_id": str(tid), "choice_id": int(ids[rnd % len(ids)])}))
    if rnd == 24 and not deconstructed:
        kit = [f["name"] for f in obs.get("facilities") or [] if f.get("type") == "Kitchen"]
        if kit:
            calls.append(("deconstruct", {"site": kit[0]}))
    return calls


def snapshot(obs, info):
    fac = sorted(((f.get("name"), f.get("type"), f.get("status"), f.get("workers"), f.get("needWorkers"),
                   f.get("food"), f.get("pop"), f.get("cap")) for f in obs.get("facilities") or []))
    tasks = sorted((str(_task_ref(t)[0]), t.get("type"), t.get("roundsLeft")) for t in obs.get("tasks") or [])
    s = {"day": obs.get("day"), "budget": obs.get("budget"), "satisfaction": obs.get("satisfaction"),
         "workers": obs.get("workers"), "logistics": obs.get("logistics"), "spend": obs.get("spend"),
         "facilities": fac, "tasks": tasks, "walking": len(obs.get("walking") or []),
         "score": info.get("score") if info else None}
    s["hash"] = hashlib.sha1(json.dumps(s, sort_keys=True, default=str).encode()).hexdigest()[:12]
    return s


def run(args):
    from cora import executor
    from cora.env import GameEnv
    from cora.observation import ObsConfig, observe
    # Unavailable choices are marked so refusals can be checked against the marks.
    obs_config = ObsConfig(mark_unavailable_choices=True)
    env = GameEnv(unity_exe_path=args.exe, unity_port=args.port, auto_start_unity=True,
                        max_episode_steps=args.rounds + 5, manual_transfers=False, seed=args.seed,
                        unity_log_path=args.unity_log,
                        skip_end_of_day=False)   # every Unity stop, comparable with older dumps
    out = {"exe": args.exe, "seed": args.seed, "rounds": [], "first_task_example": None}
    built, deconstructed = set(), False
    states = gzip.open(args.states, "wt") if args.states else None
    try:
        env.reset()
        info = None
        for rnd in range(args.rounds):
            obs = observe(env.game_state, env.get_valid_actions(), obs_config)
            if out["first_task_example"] is None and obs.get("tasks"):
                out["first_task_example"] = obs["tasks"][0]
            calls = script_calls(rnd, obs, built, deconstructed)
            rec = {"r": rnd, "pre": snapshot(obs, info)}
            if states is not None:
                states.write(json.dumps({"step": rnd, "game_state": env.game_state,
                                         "actions": env.get_valid_actions()}) + "\n")
            results, (obs2, reward, term, trunc, info) = executor.execute_turn(
                env, [(n, a) for n, a in calls])
            unavail = {(str(t.get("taskId")), str(c.get("choiceId"))): c.get("unavailable")
                       for t in obs.get("tasks") or [] for c in t.get("choices") or []}
            rec["results"] = [dict(r.as_dict(), marked_unavailable=unavail.get(
                (str((r.choice or {}).get("taskId")), str((r.choice or {}).get("choiceId"))))
                if r.choice else None) for r in results]
            rec["reward"] = reward
            if any(n == "deconstruct" for n, _ in calls):
                deconstructed = True
            out["rounds"].append(rec)
            if term or trunc:
                break
        out["final"] = snapshot(observe(env.game_state, env.get_valid_actions(), obs_config), info)
        if states is not None:
            states.write(json.dumps({"step": len(out["rounds"]), "game_state": env.game_state,
                                     "actions": env.get_valid_actions()}) + "\n")
    finally:
        env.close()
        if states is not None:
            states.close()
    json.dump(out, open(args.out, "w"), indent=1, default=str)
    report(out)


def report(out):
    from collections import Counter
    by = Counter((x["tool"], x["status"]) for r in out["rounds"] for x in r["results"])
    print("OUTCOMES (tool, status):", dict(sorted(by.items())))
    bad = [(r["r"], x["tool"], x["reason"]) for r in out["rounds"] for x in r["results"]
           if x["status"] != "executed"]
    print(f"not executed: {len(bad)}")
    for b in bad[:20]:
        print("  ", b)
    print("final:", json.dumps(out.get("final"), default=str)[:800])


def compare(paths):
    runs = [json.load(open(p)) for p in paths]
    n = min(len(r["rounds"]) for r in runs)
    first = None
    for i in range(n):
        hs = [r["rounds"][i]["pre"]["hash"] for r in runs]
        if len(set(hs)) > 1:
            first = i
            break
    print("compared", paths, "rounds", n)
    if first is None:
        print("PARITY: every round's pre-action state identical across builds")
        fh = [r.get("final", {}).get("hash") for r in runs]
        print("final hashes:", fh)
        return
    print(f"FIRST DIVERGENCE at round {first}")
    a = runs[0]["rounds"][first]["pre"]
    for j, r in enumerate(runs[1:], 1):
        b = r["rounds"][first]["pre"]
        for k in a:
            if k != "hash" and a[k] != b[k]:
                print(f"  [{paths[0]} vs {paths[j]}] {k}:\n     {json.dumps(a[k], default=str)[:400]}\n     {json.dumps(b[k], default=str)[:400]}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe")
    ap.add_argument("--seed", type=int, default=7000)
    ap.add_argument("--rounds", type=int, default=32)
    ap.add_argument("--port", type=int, default=23200)
    ap.add_argument("--out", default="diag_coverage.json")
    ap.add_argument("--unity-log", default=None)
    ap.add_argument("--compare", nargs="+")
    ap.add_argument("--states", default=None,
                    help="also write every decision's full game state and action menu to this .jsonl.gz "
                         "(how tests/fixtures/game_states.jsonl.gz is regenerated after a build)")
    a = ap.parse_args()
    if a.compare:
        compare(a.compare)
    else:
        run(a)
