"""Diverse states with every option scored: training data for calibrated decision heads.

A behaviour policy plays a game on the surrogate, acting through the typed questions
(bench.decision, schema v2: one build question per free site). At each decision the state is
logged, and every option of every question is played on the same K imagined worlds and continued
with the rules policy (oracle.annotate.annotate_state) -- an unbiased value for each option
relative to the policy's own answer, instead of the visit counts a search piles on its favourite.
States come from deliberately varied play, so they cover weak and strong positions alike:

    rules       the tuned rules policy, its turn projected onto the questions
    eps<x>      rules, with each question's answer replaced by a uniform random option with
                probability x (eps0.2, eps0.5)
    f7          a random filtered turn (oracle.search._filtered_calls, filters 1-7)
    random      a uniformly random legal turn
    motel       rules with the Motel on and Rapid Response always (the LLMs' habit)

Rows (JSONL):
    {"kind": "state",   seed, policy, decision, obs, score_now, answers}  the state and the
                        policy's answers (the baseline)
    {"kind": "options", seed, policy, decision, question, type, k, options: [...]}  as annotate
    {"kind": "final",   seed, policy, parts}  the score the behaviour policy's game ended with

    python -m oracle.breadth 7000 --policy eps0.5 --k 16 --out b.jsonl [--every 1]
"""
from __future__ import annotations

import argparse
import json
import random
import sys
import time
import zlib

POLICIES = ("rules", "eps0.2", "eps0.5", "f7", "random", "motel")


def answers_from_calls(calls, questions, decode):
    """The typed answers (each certain) that reproduce `calls` as far as the questions can say:
    task choices, a build type per site, hire and train counts (capped at the scale), staffing."""
    by = {}
    for name, args in calls:
        by.setdefault(name, []).append(dict(args))
    ans = {}
    for qid, q in questions.items():
        kind, arg = decode.get(qid, (None, None))
        if q["type"] == "choice":
            keys = list(q["criteria"])
            pick = None
            if kind == "task":
                c = next((a for a in by.get("task", []) if str(a["task_id"]) == str(arg)), None)
                pick = f"c{c['choice_id']}" if c and f"c{c['choice_id']}" in keys else "skip"
            elif kind == "build_at":
                b = next((a for a in by.get("build", []) if int(a["site_id"]) == int(arg)), None)
                pick = b["type"] if b and b["type"] in keys else "none"
            elif kind == "build":
                b = (by.get("build") or [None])[0]
                pick = b["type"] if b and b["type"] in keys else "none"
            elif qid == "build_site":
                b = (by.get("build") or [None])[0]
                pick = f"s{b['site_id']}" if b and f"s{b['site_id']}" in keys else keys[0]
            ans[qid] = {"probabilities": {pick or keys[0]: 1.0}}
        elif q["type"] == "score":
            n = len(q.get("criteria") or [])
            if kind == "hire":
                cnt = sum(int(a.get("count") or 1) for a in by.get("hire", []) if a.get("kind") == arg)
            elif kind == "train":
                cnt = sum(int(a.get("count") or 1) for a in by.get("train", []))
            else:
                cnt = 0
            ans[qid] = {"probabilities": [1.0 if i == min(cnt, n - 1) else 0.0 for i in range(n)]}
        else:                                                         # noul: staff this building?
            ans[qid] = {"noul": 1.0 if any(a.get("site") == arg for a in by.get("staff", [])) else 0.0}
    return ans


def _random_answer(q, rr):
    if q["type"] == "choice":
        return {"probabilities": {rr.choice(list(q["criteria"])): 1.0}}
    if q["type"] == "score":
        n = len(q.get("criteria") or [])
        i = rr.randrange(n)
        return {"probabilities": [1.0 if j == i else 0.0 for j in range(n)]}
    return {"noul": float(rr.random() < 0.5)}


def behaviour(env, d, policy, questions, decode, rr):
    """The policy's answers in this state."""
    from bench.baselines.rules import rules_calls
    from oracle.rollout import DECISIONS
    from oracle.search import _filtered_calls, _random_calls, _thaw
    if policy == "f7":
        calls = _thaw(_filtered_calls(env, rr, strategy=True, empty=True, premium=False))
    elif policy == "random":
        calls = _thaw(_random_calls(env, rr))
    elif policy == "motel":
        calls = rules_calls(env, d, DECISIONS, {"motel": True, "rrv": "always"})
    else:
        calls = rules_calls(env, d, DECISIONS)
    ans = answers_from_calls(calls, questions, decode)
    if policy.startswith("eps"):
        eps = float(policy[3:])
        for qid, q in questions.items():
            if rr.random() < eps:
                ans[qid] = _random_answer(q, rr)
    return ans


def run(seed, policy, k=16, every=1.0, out=None, log=None, top=0.10):
    from bench.decision import build_request, tool_calls
    from cora import executor
    from cora.scoring import score_components
    from oracle.annotate import annotate_state
    from oracle.dm_leaf import observation
    from oracle.rollout import DECISIONS
    from oracle.sim.env import SimEnv
    env = SimEnv(seed=seed, max_episode_steps=DECISIONS + 4, manual_transfers=False)
    env.reset()
    rr = random.Random(zlib.crc32(f"{policy}/{seed}".encode()))
    pick = random.Random(zlib.crc32(f"pick/{policy}/{seed}".encode()))
    write = (lambda row: out.write(json.dumps(row) + "\n")) if out else (lambda row: None)
    for d in range(DECISIONS):
        obs = observation(env)
        body, decode = build_request(obs, "", schema="v2")
        questions = body["questions"]
        answers = behaviour(env, d, policy, questions, decode, rr)
        calls = tool_calls(answers, decode)
        now = score_components(env.game_state.get("rewardMetrics"))["score"]
        if questions and pick.random() < every:
            t0 = time.time()
            write({"kind": "state", "seed": seed, "policy": policy, "decision": d, "obs": obs,
                   "score_now": now, "answers": answers, "calls": calls})
            rows, n_turns = annotate_state(env, d, seed, questions, answers, decode, k, top, tag=f"{policy}/{seed}")
            for row in rows:
                write({"kind": "options", "policy": policy, **row})
            if log:
                print(f"  {policy} {seed} d{d:02d}: {len(rows)} questions, {n_turns} turns, "
                      f"{time.time() - t0:.1f}s, score {now:.3f}", file=log, flush=True)
        _, (_, _, term, trunc, _) = executor.execute_turn(env, calls)
        if term or trunc:
            break
    parts = score_components(env.game_state.get("rewardMetrics"))
    write({"kind": "final", "seed": seed, "policy": policy, "parts": parts})
    return parts["score"]


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("seed", type=int)
    ap.add_argument("--policy", choices=POLICIES, default="eps0.2")
    ap.add_argument("--k", type=int, default=16, help="imagined worlds per state")
    ap.add_argument("--every", type=float, default=1.0, help="share of decisions annotated")
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)
    with open(a.out, "w") as out:
        score = run(a.seed, a.policy, a.k, a.every, out, sys.stderr)
    print(json.dumps({"seed": a.seed, "policy": a.policy, "score": round(score, 4)}))


if __name__ == "__main__":
    main(sys.argv[1:])
