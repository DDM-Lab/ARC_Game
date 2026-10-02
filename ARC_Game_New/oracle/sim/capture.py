"""Capture a seeded headless game for lockstep validation of the surrogate.

Plays one game through rl.CoraEnv -- the same prompt-free tool-call path the benchmark and RL
use, resolved and executed by cora.executor -- with Unity's draw instrumentation on
(ARC_SNAPSHOT_DEBUG=1: [RNGMARK] / [RNGCTX] lines in the log), and writes the trace the
lockstep tools read (obs_diff, debug_lockstep, diag_marks):

    <out>/staff_<seed>.json        one entry per decision: before / taken / after / calls
    <out>/staff_<seed>.log         the Unity log with the draw stream
    <out>/staff_<seed>.meta.json   build GUID, seed, decisions, plan source

`taken` lists what the game was actually sent, in the order it was sent: task answers
({"kind": "choice", taskId, choiceId, stableTaskId}) and game actions ({"kind": "menu" | "staff",
action_id, action_type, cost, ok, payload}). The surrogate replays exactly that.

Plans:
    python -m oracle.sim.capture 5503 --noop
    python -m oracle.sim.capture 5503 --calls plan.json          # [[ [tool, args], ... ], ...] per decision
    python -m oracle.sim.capture 5503 --episode benchmark_results/x/episodes.jsonl --index 0
                                                                   # replay a recorded episode's calls
    python -m oracle.sim.capture 5503 --policy combined           # a benchmark baseline, live

Needs the headless build (cora.env.unity_process.default_exe) and an unsandboxed shell.
"""
from __future__ import annotations

import argparse
import copy
import json
import os
import sys

from oracle.sim import paths as P


def _plan(args) -> list:
    """Per-decision lists of (tool, args)."""
    if args.noop:
        return []
    if args.calls:
        return [[(c[0], c[1]) for c in turn] for turn in json.load(open(args.calls))]
    with open(args.episode) as f:
        rec = [json.loads(line) for line in f if line.strip()][args.index]
    return [[(c["tool"], c["args"]) for c in (r.get("calls") or [])] for r in rec["rounds"]]


def _taken(before: dict, info: dict) -> list:
    stable = {t.get("taskId"): t.get("stableTaskId") or "" for t in before.get("allActiveTasks") or []}
    taken = []
    for r in info.get("call_results") or []:          # task answers are committed first
        if r.choice is not None and r.status == "executed":
            taken.append({"kind": "choice", "taskId": r.choice["taskId"], "choiceId": r.choice["choiceId"],
                          "stableTaskId": stable.get(r.choice["taskId"], "")})
    results = info.get("execution_results") or []
    for k, a in enumerate(info.get("dispatched") or []):
        res = results[k] if k < len(results) else None
        taken.append({"kind": "staff" if a.get("action_type") == "worker_assignment" else "menu",
                      "action_id": a.get("action_id"), "action_type": a.get("action_type"),
                      "cost": a.get("cost"), "ok": bool(res and res.get("success")),
                      "sent": res is not None, "payload": a})
    return taken


def capture(seed: int, plan, out_dir: str, port: int, source: str, max_steps: int = 40) -> str:
    """`plan` is per-decision lists of (tool, args), or a callable(game, decision) -> calls."""
    from rl import CoraEnv, CoraEnvConfig
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.abspath(os.path.join(out_dir, f"staff_{seed}.json"))
    if os.path.exists(out):
        raise SystemExit(f"refusing to overwrite {out}")
    os.environ["ARC_SNAPSHOT_DEBUG"] = "1"            # Unity reads it at startup
    env = CoraEnv(CoraEnvConfig(seed=seed, port=port, max_steps=max_steps,
                                unity_log=out.replace(".json", ".log")))
    trace = []
    try:
        env.reset()
        i = 0
        while True:
            before = copy.deepcopy(env.game.game_state)
            calls = plan(env.game, i) if callable(plan) else (plan[i] if i < len(plan) else [])
            _, reward, terminated, truncated, info = env.step(calls)
            trace.append({"round": i, "before": before, "taken": _taken(before, info),
                          "after": copy.deepcopy(env.game.game_state), "calls": info["calls"],
                          "reward": reward})
            sab = env.game.game_state.get("satisfactionAndBudget") or {}
            print(f"  d{i:02d} sent={[t.get('action_id') or t.get('choiceId') for t in trace[-1]['taken']]} "
                  f"budget={sab.get('budget')} sat={sab.get('satisfaction')}", flush=True)
            i += 1
            if terminated or truncated:
                break
        consts = env.game.request({"type": "sim_constants"}) or {}
    finally:
        env.close()
    json.dump(trace, open(out, "w"))
    json.dump({"buildGUID": consts.get("buildGUID"), "seed": seed, "decisions": len(trace),
               "source": source}, open(out.replace(".json", ".meta.json"), "w"))
    print(f"wrote {out} ({len(trace)} decisions)")
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("seed", type=int)
    src = ap.add_mutually_exclusive_group(required=True)
    src.add_argument("--noop", action="store_true")
    src.add_argument("--calls")
    src.add_argument("--episode")
    src.add_argument("--policy", help="a bench.baselines policy (greedy, combined, ...)")
    ap.add_argument("--index", type=int, default=0, help="episode line in --episode")
    ap.add_argument("--out", default=P.CAPTURES)
    ap.add_argument("--port", type=int, default=21050)
    a = ap.parse_args()
    if a.policy:
        from bench.baselines import POLICIES
        from bench.baselines.common import tool_calls
        policy = POLICIES[a.policy]
        plan = lambda game, i: tool_calls(game, policy(game, i, 36))
        source = f"policy:{a.policy}"
    else:
        plan = _plan(a)
        source = "noop" if a.noop else (a.calls or f"{a.episode}#{a.index}")
    capture(a.seed, plan, a.out, a.port, source)
    return 0


if __name__ == "__main__":
    sys.exit(main())
