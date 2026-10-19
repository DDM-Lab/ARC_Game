"""A decision model as a leaf evaluator: one typed question about how the game will end.

Decision models (bench.decision) answer questions about actions; a search leaf needs a value. The
value is asked as one more SystemOne question on the same state the benchmark gives them -- the
minimal_v7 rules beside the observation -- so it costs one prefill and reads back a calibrated
distribution, not generated text:

    final     score 0..5   which band the final score will land in (bands spelled out)
    final+    the same, with the score so far (its nine parts) in the question
    ontrack   noul         is the response on track to meet the residents' needs?

`LeafValue(client, variant)(env)` is the expected final score: the band midpoints (MIDPOINTS)
weighted by the model's probabilities, or 2 * P(true) for a noul. `ask` gives the raw value on
0..1 (the mean band / 5, or P(true)).

`python -m oracle.dm_leaf probe` asks every variant on logged search states (oracle.search
--log-data) and reports how well each tracks the score the state actually ended with -- whether a
leaf value is worth searching with at all.
"""
from __future__ import annotations

import argparse
import gzip
import json
import math
import random
import statistics as st
import sys
import time

BANDS = [(None, 0.8), (0.8, 1.1), (1.1, 1.3), (1.3, 1.45), (1.45, 1.6), (1.6, None)]
_BAND_TEXT = ["below 0.8", "0.8-1.1", "1.1-1.3", "1.3-1.45", "1.45-1.6", "above 1.6"]
_FINAL = ("At the end of the game the response is scored as the SCORE section of the rules describes "
          "(satisfaction plus cost efficiency, on a 0-2 scale). If play continues sensibly from this "
          "state, which band will the final score fall in? "
          + "; ".join(f"{i}: {t}" for i, t in enumerate(_BAND_TEXT)) + ".")
MIDPOINTS = [0.6, 0.95, 1.2, 1.375, 1.525, 1.7]
VARIANTS = ("final", "final+", "ontrack")

_RULES = None


def rules_text() -> str:
    """The benchmark's decision-model rules: the minimal_v7 system prompt (bench.episode)."""
    global _RULES
    if _RULES is None:
        from cora import prompts
        _RULES = prompts.render(prompts.load_pack("minimal_v7"))
    return _RULES


def observation(env) -> dict:
    """The observation the benchmark's decision models read (CoraEnv with minimal_v7)."""
    from cora.observation import ObsConfig, observe
    return observe(env.game_state, env.valid_actions, ObsConfig(show_impacts=True, mark_unavailable_choices=True))


def question(variant: str, parts: dict | None = None) -> dict:
    if variant == "ontrack":
        return {"type": "noul", "instructions": "Is this response on track to meet the residents' food, "
                "lodging and casework needs by the end of the game, at a reasonable cost?"}
    text = _FINAL
    if variant == "final+" and parts:
        shown = {k: round(v, 3) for k, v in parts.items() if isinstance(v, (int, float))}
        text = f"The score so far is {json.dumps(shown)}. " + text
    return {"type": "score", "criteria": [f"band {i}: {t}" for i, t in enumerate(_BAND_TEXT)], "instructions": text}


def value_of(answer: dict, kind: str) -> float:
    """An answer as a value on 0..1."""
    if kind == "noul":
        p = answer.get("noul")
        if not isinstance(p, (int, float)):
            p = (answer.get("probabilities") or {}).get("true", 0.0)
        return float(p)
    probs = answer.get("probabilities") or []
    if isinstance(probs, dict):
        probs = [probs.get(str(i), 0.0) for i in range(len(BANDS))]
    z = sum(probs) or 1.0
    return sum(i * p for i, p in enumerate(probs)) / z / (len(BANDS) - 1)


def ask(client, obs: dict, variant: str, parts: dict | None = None, model: str = "dm") -> float:
    q = question(variant, parts)
    resp = client({"model": model, "state": {"rules": rules_text(), "observation": obs}, "questions": {"value": q}})
    return value_of((resp.get("answers") or {}).get("value") or {}, q["type"])


class LeafValue:
    """`(env) -> expected final score` from a decision-model server; counts calls and time."""

    def __init__(self, client, variant: str = "final", model: str = "dm"):
        self.client, self.variant, self.model = client, variant, model
        self.calls, self.seconds = 0, 0.0

    def __call__(self, env) -> float:
        from cora.scoring import score_components
        t0 = time.time()
        parts = score_components((env.game_state or {}).get("rewardMetrics")) if self.variant == "final+" else None
        u = ask(self.client, observation(env), self.variant, parts, self.model)
        if self.variant == "ontrack":
            v = 2.0 * u
        else:                           # u = mean band / 5: interpolate between band midpoints
            x = u * (len(MIDPOINTS) - 1)
            i = min(int(x), len(MIDPOINTS) - 2)
            v = MIDPOINTS[i] + (x - i) * (MIDPOINTS[i + 1] - MIDPOINTS[i])
        self.calls += 1
        self.seconds += time.time() - t0
        return v


def _rank(xs):
    o = sorted(range(len(xs)), key=lambda i: xs[i]); r = [0.0] * len(xs); i = 0
    while i < len(o):
        j = i
        while j + 1 < len(o) and xs[o[j + 1]] == xs[o[i]]:
            j += 1
        for k in range(i, j + 1):
            r[o[k]] = (i + j) / 2
        i = j + 1
    return r


def spearman(a, b):
    ra, rb = _rank(a), _rank(b)
    ma, mb = st.mean(ra), st.mean(rb)
    num = sum((x - ma) * (y - mb) for x, y in zip(ra, rb))
    den = math.sqrt(sum((x - ma) ** 2 for x in ra) * sum((y - mb) ** 2 for y in rb))
    return num / den if den else float("nan")


def _states(paths, n, rng):
    """Logged states with the score each ended with: the search's own decisions (with the score
    so far) and its sampled rollout states (no score so far logged)."""
    out = []
    for p in paths:
        for line in (gzip.open(p, "rt") if p.endswith(".gz") else open(p)):
            r = json.loads(line)
            out.append({"src": f"{r['algo']}/decision", "decision": r["decision"], "obs": r["obs"],
                        "parts": r.get("parts_now"), "final": r["final"]["score"]})
            for s in r.get("samples") or []:
                out.append({"src": f"{r['algo']}/rollout", "decision": s["decision"], "obs": s["obs"],
                            "parts": None, "final": s["parts"]["score"]})
    by = {}
    for s in out:
        by.setdefault(s["src"], []).append(s)
    pick = []
    for src, xs in sorted(by.items()):
        rng.shuffle(xs)
        pick += xs[:n]
    return pick


def probe(a):
    from bench.decision import Client
    client = Client(a.url, timeout=600)
    rng = random.Random(0)
    states = _states(a.data, a.per_source, rng)
    print(f"{len(states)} states from {len(a.data)} files", file=sys.stderr, flush=True)
    rows, t0 = [], time.time()
    for i, s in enumerate(states):
        row = {"src": s["src"], "decision": s["decision"], "outcome": s["final"]}
        row["score_now"] = (s["parts"] or {}).get("score")
        for v in VARIANTS:
            if v == "final+" and not s["parts"]:
                continue
            row[v] = ask(client, s["obs"], v, s["parts"])
        rows.append(row)
        if i % 20 == 0:
            print(f"  {i}/{len(states)}  {(time.time() - t0) / (i + 1):.2f}s/state", file=sys.stderr, flush=True)
    with open(a.out, "w") as f:
        for r in rows:
            f.write(json.dumps(r) + "\n")
    report(rows)


def report(rows):
    print(f"{'source':22}{'n':>5}" + "".join(f"{v:>10}" for v in VARIANTS + ("score_now",)) + "   (spearman with final score)")
    groups = {}
    for r in rows:
        groups.setdefault(r["src"], []).append(r)
    groups["ALL"] = rows
    for g, rs in groups.items():
        line = f"{g:22}{len(rs):>5}"
        for v in VARIANTS + ("score_now",):
            xs = [(r[v], r["outcome"]) for r in rs if r.get(v) is not None]
            line += f"{spearman(*zip(*xs)):>10.2f}" if len(xs) > 5 else f"{'-':>10}"
        print(line)
    # Within one stage of the game, as a leaf compares states: rank by decision-index thirds.
    print("within game stage (decisions 0-9 / 10-19 / 20-28), all sources:")
    for lo, hi in ((0, 10), (10, 20), (20, 29)):
        rs = [r for r in rows if lo <= r["decision"] < hi]
        line = f"  d{lo:02d}-{hi - 1:02d}{'':14}{len(rs):>5}"
        for v in VARIANTS + ("score_now",):
            xs = [(r[v], r["outcome"]) for r in rs if r.get(v) is not None]
            line += f"{spearman(*zip(*xs)):>10.2f}" if len(xs) > 5 else f"{'-':>10}"
        print(line)


def main(argv):
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("probe", help="ask every value variant on logged search states")
    p.add_argument("data", nargs="+", help="oracle.search --log-data files")
    p.add_argument("--url", required=True, help="decision-model server (POST /v1/systemone)")
    p.add_argument("--per-source", type=int, default=90)
    p.add_argument("--out", required=True)
    r = sub.add_parser("report", help="re-print a probe's table")
    r.add_argument("rows")
    a = ap.parse_args(argv)
    if a.cmd == "probe":
        probe(a)
    else:
        report([json.loads(l) for l in open(a.rows)])


if __name__ == "__main__":
    main(sys.argv[1:])
