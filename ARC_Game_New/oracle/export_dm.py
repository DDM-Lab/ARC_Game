"""Training examples for decision-model heads, one per (state, question), from everything logged.

    python -m oracle.export_dm --search DATA.jsonl.gz ... --breadth B.jsonl ... \
        --annotations RUN_DIR ... --out examples.jsonl.gz

Every example is a typed question on a state, in the shape the models are asked
(bench.decision.build_request, schema v2 unless the source asked v1), with its labels:

    option_value   (oracle.breadth, oracle.annotate) each option's mean final score q, its share
                   of the k imagined worlds on which it is best (p_best, ties shared), its chance of a
                   top-10% run (p_top), and all k scores (world-aligned). A one-turn deviation from
                   the behaviour's answer continued by the rules policy: it understates options
                   the continuation repairs next turn (staffing).
    search_policy  (oracle.search --log-data) visits and mean score per option, from the root's
                   candidate turns mapped onto each question (oracle.breadth.answers_from_calls);
                   options no candidate took have visits 0 and q null (unexplored, not bad).
    value          the state's eventual final score and its band (oracle.dm_leaf.BANDS): search
                   decisions and sampled rollout states (outcome under that search / rollout), and
                   breadth states (the behaviour game's final, and the rules continuation's mean).

Each example carries `obs` (the observation dict), `question` ({type, instructions, criteria}),
`options` (ids in order), `labels`, and provenance (`source`, `policy`, `seed`, `decision`). The
rules text is `rules: "minimal_v7"` (oracle.dm_leaf.rules_text), not repeated per example.
"""
from __future__ import annotations

import argparse
import glob
import gzip
import json
import os
import sys
from collections import defaultdict


def _open(p, mode="rt"):
    return gzip.open(p, mode) if p.endswith(".gz") else open(p, mode[0])


def _band(score):
    from oracle.dm_leaf import BANDS
    for i, (lo, hi) in enumerate(BANDS):
        if (lo is None or score >= lo) and (hi is None or score < hi):
            return i
    return len(BANDS) - 1


def _option_ids(q):
    if q["type"] == "noul":
        return ["false", "true"]
    if q["type"] == "score":
        return list(range(len(q.get("criteria") or [])))
    return list(q["criteria"])


def _answer_option(q, ans):
    """The option a certain answer picks."""
    if q["type"] == "noul":
        return "true" if float(ans.get("noul", 0)) >= 0.5 else "false"
    p = ans.get("probabilities")
    if isinstance(p, dict):
        return max(p, key=p.get)
    return max(range(len(p)), key=p.__getitem__)


def option_value_example(row, obs, question, source, policy=None):
    k = row["k"]
    opts = row["options"]
    wins = [0.0] * len(opts)
    for w in range(k):
        col = [o["scores"][w] for o in opts]
        m = max(col)
        best = [i for i, v in enumerate(col) if v == m]
        for i in best:
            wins[i] += 1 / len(best)
    return {"label_kind": "option_value", "source": source, "policy": policy, "seed": row["seed"],
            "decision": row["decision"], "qid": row["question"], "rules": "minimal_v7", "obs": obs,
            "question": question, "options": [o["option"] for o in opts],
            "labels": {"q": [o["q"] for o in opts], "p_best": [x / k for x in wins],
                       "p_top": [o["p_top"] for o in opts], "baseline": [o["same_as_chosen"] for o in opts],
                       "model_p": [o.get("p") for o in opts], "scores": [o["scores"] for o in opts], "k": k}}


def from_breadth(paths, out, stats):
    for p in paths:
        states, finals, rows = {}, {}, []
        for line in _open(p):
            r = json.loads(line)
            if r["kind"] == "state":
                states[(r["policy"], r["seed"], r["decision"])] = r
            elif r["kind"] == "final":
                finals[(r["policy"], r["seed"])] = r["parts"]["score"]
            else:
                rows.append(r)
        from bench.decision import build_request
        qcache = {}
        for r in rows:
            key = (r["policy"], r["seed"], r["decision"])
            st = states[key]
            if key not in qcache:
                qcache[key] = build_request(st["obs"], "", schema="v2")[0]["questions"]
            q = qcache[key].get(r["question"])
            if q is None:
                continue
            out.write(json.dumps(option_value_example(r, st["obs"], q, "breadth", r["policy"])) + "\n")
            stats["option_value/breadth"] += 1
        for key, st in states.items():
            fin = finals.get(key[:2])
            base = [r for r in rows if (r["policy"], r["seed"], r["decision"]) == key]
            cont = next((o["q"] for r in base[:1] for o in r["options"] if o["same_as_chosen"]), None)
            if fin is None:
                continue
            out.write(json.dumps({"label_kind": "value", "source": "breadth", "policy": key[0], "seed": key[1],
                                  "decision": key[2], "rules": "minimal_v7", "obs": st["obs"],
                                  "labels": {"final": fin, "band": _band(fin), "score_now": st["score_now"],
                                             "rules_continuation": cont}}) + "\n")
            stats["value/breadth"] += 1


def from_search(paths, out, stats):
    from bench.decision import build_request
    from oracle.breadth import answers_from_calls
    for p in paths:
        for line in _open(p):
            r = json.loads(line)
            obs, src = r["obs"], f"search/{r['algo']}_{r['info']}"
            fin = r["final"]["score"]
            out.write(json.dumps({"label_kind": "value", "source": src, "seed": r["seed"], "decision": r["decision"],
                                  "rules": "minimal_v7", "obs": obs,
                                  "labels": {"final": fin, "band": _band(fin),
                                             "score_now": (r.get("parts_now") or {}).get("score")}}) + "\n")
            stats["value/search"] += 1
            for s in r.get("samples") or []:
                f = s["parts"]["score"]
                out.write(json.dumps({"label_kind": "value", "source": src + "/rollout", "seed": r["seed"],
                                      "decision": s["decision"], "rules": "minimal_v7", "obs": s["obs"],
                                      "labels": {"final": f, "band": _band(f)}}) + "\n")
                stats["value/rollout"] += 1
            body, decode = build_request(obs, "", schema="v2")
            qs = body["questions"]
            agg = {qid: defaultdict(lambda: [0, 0.0]) for qid in qs}
            for c in r.get("root") or []:
                if not c["n"]:
                    continue
                ans = answers_from_calls([(t, a) for t, a in c["calls"]], qs, decode)
                for qid, q in qs.items():
                    o = _answer_option(q, ans[qid])
                    agg[qid][o][0] += c["n"]
                    agg[qid][o][1] += c["n"] * (c["q"] or 0.0)
            for qid, q in qs.items():
                ids = _option_ids(q)
                if sum(1 for o in ids if agg[qid][o][0]) < 2:      # the search never compared options here
                    continue
                out.write(json.dumps({"label_kind": "search_policy", "source": src, "seed": r["seed"],
                                      "decision": r["decision"], "qid": qid, "rules": "minimal_v7", "obs": obs,
                                      "question": q, "options": ids,
                                      "labels": {"visits": [agg[qid][o][0] for o in ids],
                                                 "q": [agg[qid][o][1] / agg[qid][o][0] if agg[qid][o][0] else None
                                                       for o in ids]}}) + "\n")
                stats["search_policy"] += 1


def from_annotations(run_dirs, out, stats):
    for run in run_dirs:
        eps = {}
        for ep in glob.glob(os.path.join(run, "**", "episodes.jsonl"), recursive=True):
            for line in open(ep):
                e = json.loads(line)
                if e.get("rounds"):
                    eps[e["seed"]] = e
        ann = os.path.join(run, "annotations.jsonl")
        if not os.path.exists(ann):
            continue
        for line in open(ann):
            r = json.loads(line)
            rd = eps[r["seed"]]["rounds"][r["decision"]]
            raw = json.loads(rd.get("raw") or "{}")
            q = (raw.get("questions") or {}).get(r["question"])
            if q is None:
                continue
            out.write(json.dumps(option_value_example(r, rd["obs"], q, "annotate", r.get("model"))) + "\n")
            stats["option_value/annotate"] += 1


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--search", nargs="*", default=[])
    ap.add_argument("--breadth", nargs="*", default=[])
    ap.add_argument("--annotations", nargs="*", default=[], help="decision-model run directories")
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)
    stats = defaultdict(int)
    with _open(a.out, "wt") as out:
        from_breadth(a.breadth, out, stats)
        from_annotations(a.annotations, out, stats)
        from_search(a.search, out, stats)
    print(json.dumps(dict(stats)))


if __name__ == "__main__":
    main(sys.argv[1:])
