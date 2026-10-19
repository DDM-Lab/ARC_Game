"""Score every option a decision model was asked about, by simulation from the exact game state.

A decision-model game (bench --policy decision) records, per turn, the observation, every typed
question with its options and the model's full distribution, and the calls Unity executed. The
surrogate replays the game call for call (it matches Unity), so the state before each turn is
known exactly. For each question of that turn and each of its options, the turn is replayed with
that question's answer forced to the option (all other answers as the model gave them), and the
game is continued from there with a fixed continuation policy on K imagined worlds. The K worlds
are shared by all options of the turn (common random numbers: oracle.sim.rng.StreamedRandom), so
options are compared on the same floods, weather, arrivals and requests.

Per option it reports:
    p          the model's probability for the option
    q          mean final score over the K continuations
    p_top      share of its continuations that finish in the top 10% of every continuation
               simulated from this state (pooled over all of the turn's options): the chance the
               option leads to one of the best runs achievable from here, whatever the dice do
    same_as_chosen   whether the option gives the same calls as the model's answer
    scores     all K final scores, world by world (index i is the same imagined world for every
               option of the turn), so any other measure can be computed afterwards: the whole
               outcome distribution and its quantiles, the chance of beating the model's own
               choice on the same world (probability of improvement), expected improvement,
               stochastic dominance, CVaR
Options that turn into the same calls are simulated once.

    python -m oracle.annotate results/<model>/episodes.jsonl --out ann.jsonl [--episode 3] [--k 32]
"""
from __future__ import annotations

import argparse
import json
import sys
import zlib


def _forced(answers: dict, qid: str, q: dict, option) -> dict:
    """`answers` with question `qid` answered `option` for certain."""
    a = dict(answers)
    if q["type"] == "noul":
        a[qid] = {"noul": 1.0 if option == "true" else 0.0}
    elif q["type"] == "score":
        n = len(q.get("criteria") or [])
        a[qid] = {"probabilities": [1.0 if i == option else 0.0 for i in range(n)]}
    else:
        a[qid] = {"probabilities": {option: 1.0}}
    return a


def _options(q: dict, ans: dict) -> list:
    """[(option, model probability)] for one question and the model's answer to it."""
    if q["type"] == "noul":
        p = None
        for key in ("noul", "probability", "p_true", "true"):
            if isinstance(ans.get(key), (int, float)):
                p = float(ans[key]); break
        if p is None and isinstance(ans.get("probabilities"), dict):
            p = float(ans["probabilities"].get("true", 0.0))
        return [("false", 1.0 - (p or 0.0)), ("true", p or 0.0)]
    probs = ans.get("probabilities")
    if q["type"] == "score":
        n = len(q.get("criteria") or [])
        if isinstance(probs, dict):
            probs = [float(probs.get(str(i), 0.0)) for i in range(n)]
        probs = list(probs or [0.0] * n) + [0.0] * n
        return [(i, float(probs[i])) for i in range(n)]
    keys = list(q.get("criteria") or {})
    probs = probs if isinstance(probs, dict) else {}
    return [(k, float(probs.get(k, 0.0))) for k in keys]


def _continue(env, d, decisions):
    """Play the rules policy from decision d to the end; the final score."""
    from cora import executor
    from cora.scoring import score_components
    from oracle.search import move_calls
    while d < decisions:
        _, (_, _, term, trunc, _) = executor.execute_turn(env, move_calls(env, d, "rules"))
        if term or trunc:
            break
        d += 1
    return score_components(env.game_state.get("rewardMetrics"))["score"]


def annotate_state(env, d, seed, questions, answers, decode, k=32, top=0.10, tag=None):
    """Rows for one state: every option of every question, forced in turn (other answers kept),
    played on the same k imagined worlds (bases crc32(f"{tag or seed}/{d}/{i}")) and continued with
    the rules policy. `answers` are the policy's own answers (the baseline every option deviates
    from); `decode` maps question ids to calls (bench.decision.build_request)."""
    from bench.decision import tool_calls
    from cora import executor
    from cora.scoring import score_components
    from oracle.rollout import DECISIONS
    from oracle.sim.rng import StreamedRandom
    bases = [zlib.crc32(f"{seed if tag is None else tag}/{d}/{i}".encode()) for i in range(k)]
    sims = {}                              # calls -> [final score on world i]

    def value(calls):
        key = json.dumps(calls, sort_keys=True)
        if key not in sims:
            vals = []
            for b in bases:
                e = env.clone()
                e.world.rng = StreamedRandom(b)
                _, (_, _, term, trunc, _) = executor.execute_turn(e, calls)
                vals.append(score_components(e.game_state.get("rewardMetrics"))["score"]
                            if term or trunc else _continue(e, d + 1, DECISIONS))
            sims[key] = vals
        return sims[key]

    per_q = {}
    for qid, q in questions.items():
        if (qid not in decode and qid != "build_site") or not isinstance(answers.get(qid), dict):
            continue
        opts = _options(q, answers[qid])
        chosen = tool_calls(answers, decode)
        per_q[qid] = [(o, p, tool_calls(_forced(answers, qid, q, o), decode), chosen) for o, p in opts]
        for _, _, calls, _ in per_q[qid]:
            value(calls)
    pooled = sorted(v for vals in sims.values() for v in vals)
    cut = pooled[min(len(pooled) - 1, int((1 - top) * len(pooled)))] if pooled else None
    rows = []
    for qid, opts in per_q.items():
        chosen_key = json.dumps(opts[0][3], sort_keys=True)
        row = {"seed": seed, "decision": d, "question": qid,
               "type": questions[qid]["type"], "k": k, "top_cut": cut, "options": []}
        for o, p, calls, _ in opts:
            vals = value(calls)
            row["options"].append({"option": o, "p": p, "q": sum(vals) / len(vals),
                                   "p_top": sum(v >= cut for v in vals) / len(vals),
                                   "same_as_chosen": json.dumps(calls, sort_keys=True) == chosen_key,
                                   "scores": [round(v, 5) for v in vals], "calls": calls})
        rows.append(row)
    return rows, len(sims)


def annotate_episode(rec: dict, k: int = 32, top: float = 0.10, out=None, log=None):
    from bench.decision import build_request
    from cora import executor
    from cora.scoring import score_components
    from oracle.rollout import DECISIONS
    from oracle.sim.env import SimEnv
    env = SimEnv(seed=rec["seed"], max_episode_steps=DECISIONS + 4,
                 manual_transfers=rec.get("transfers") == "manual")
    env.reset()
    rows = []
    for d, rd in enumerate(rec["rounds"]):
        recorded = [(c["tool"], c["args"]) for c in rd.get("calls") or []]
        raw = json.loads(rd.get("raw") or "{}")
        answers, questions = (raw.get("answers"), raw.get("questions")) if "answers" in raw else (raw, None)
        obs = rd.get("obs")
        if answers and obs:
            body, decode = build_request(obs, rec.get("model", ""))
            questions = questions or body["questions"]
            # build_site has no decode entry of its own: the build answer reads it
            if set(decode) - set(questions) or set(questions) - set(decode) - {"build_site"}:
                questions = None                   # asked something else than this build asks
        if answers and questions:
            st_rows, n_turns = annotate_state(env, d, rec["seed"], questions, answers, decode, k, top)
            for row in st_rows:
                row = {"seed": row.pop("seed"), "model": rec.get("model"), **row}
                rows.append(row)
                if out:
                    out.write(json.dumps(row) + "\n")
            if log:
                print(f"  seed {rec['seed']} d{d:02d}: {len(st_rows)} questions, {n_turns} distinct turns",
                      file=log, flush=True)
        _, (_, _, term, trunc, _) = executor.execute_turn(env, recorded)
        if rd.get("comps"):
            got = score_components(env.game_state.get("rewardMetrics"))["score"]
            if abs(got - rd["comps"]["score"]) > 1e-4:     # the replay left Unity's game: stop here
                if log:
                    print(f"  seed {rec['seed']} d{d:02d}: replay diverged from Unity "
                          f"({got:.4f} vs {rd['comps']['score']:.4f}); stopping", file=log, flush=True)
                break
        if term or trunc:
            break
    return rows


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("episodes", help="a decision-model run's episodes.jsonl")
    ap.add_argument("--out", required=True)
    ap.add_argument("--episode", type=int, help="only this line (0-based) of the file")
    ap.add_argument("--k", type=int, default=32, help="imagined worlds per turn")
    ap.add_argument("--top", type=float, default=0.10, help="the top share of runs p_top counts")
    a = ap.parse_args(argv)
    recs = [json.loads(l) for l in open(a.episodes) if l.strip()]
    recs = [r for r in recs if r.get("rounds") and not r.get("error")]
    if a.episode is not None:
        recs = recs[a.episode:a.episode + 1]
    with open(a.out, "w") as out:
        for r in recs:
            annotate_episode(r, a.k, a.top, out, sys.stderr)


if __name__ == "__main__":
    main(sys.argv[1:])
