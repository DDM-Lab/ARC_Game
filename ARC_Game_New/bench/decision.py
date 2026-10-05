"""Decision-model policy: a CORA turn as a SystemOne request, the answers back as tool calls.

Decision models (Clef, Jev and its open students, Kev, Laya) do not generate text. Given a `state`
and a mapping of typed `questions` (`choice` over named options, `score` over ordered options,
`noul` true/false) they return a probability for every option of every question in one forward
pass, on the `POST /v1/systemone` request/response shape the TypeSafe Jev API defined:

    request   {"model", "state", "questions": {qid: {"type", "instructions", "criteria"}}}
    response  {"answers": {qid: {"choice" | "score" | ..., "probabilities": ...}}, "usage"}

One turn becomes one request. The state is the observation the LLM arms see (cora.observation);
the questions are the turn's action menu:

    task_<TOKEN>       choice   one per open task with an available choice: its choices, or "skip"
    build              choice   none / shelter / kitchen / casework
    build_site         choice   one of available.buildSites (read only when build != none; asked
                                only when more than one site is free)
    hire_trained       score    0..HIRE_MAX workers
    hire_untrained     score    0..HIRE_MAX workers
    train              score    0..min(HIRE_MAX, trainUntrainedMax)
    staff_<n>          noul     one per building in available.needStaff: staff it fully now?

The answers become ordinary tool calls (cora.tools) executed by cora.executor, exactly like an
LLM's, so scores are comparable across arms. `pick` chooses each option: "argmax" (benchmark) or
"sample" (exploration / RL, from the returned probabilities).
"""
from __future__ import annotations

import json
import random
import urllib.request
from typing import Any, Optional

HIRE_MAX = 5
BUILD_TYPES = {"none": "Do not start a building this turn.",
               "shelter": "Start a shelter: houses residents up to its capacity.",
               "kitchen": "Start a kitchen: produces food for deliveries.",
               "casework": "Start a casework site: processes residents so they can return home."}


def _task_questions(obs: dict) -> tuple[dict, dict]:
    questions, decode = {}, {}
    for t in obs.get("tasks") or []:
        offered = [c for c in t.get("choices") or [] if not c.get("unavailable")]
        if not offered:
            continue
        qid = f"task_{t['token']}"
        criteria = {"skip": "Do not answer this task this turn."}
        for c in offered:
            impacts = c.get("impacts")
            criteria[f"c{c['choiceId']}"] = c["text"] + (f" (impacts: {json.dumps(impacts)})" if impacts else "")
        questions[qid] = {"type": "choice", "criteria": criteria,
                          "instructions": f"Task '{t['title']}' ({t.get('roundsLeft', '?')} rounds left): "
                                          f"{t.get('desc', '')} Which response, if any, should be taken now?"}
        decode[qid] = ("task", t["token"])
    return questions, decode


def build_request(obs: dict, model: str) -> tuple[dict, dict]:
    """(SystemOne request body, decode map qid -> how its answer becomes a tool call)."""
    questions, decode = _task_questions(obs)
    avail = obs.get("available") or {}
    sites = list(avail.get("buildSites") or [])
    if sites:
        questions["build"] = {"type": "choice", "criteria": dict(BUILD_TYPES),
                              "instructions": "Should a new building be started this turn, and which type?"}
        if len(sites) > 1:          # a one-option choice is not a question: the only site is used
            questions["build_site"] = {"type": "choice", "criteria": {f"s{s}": f"Build site {s}" for s in sites},
                                       "instructions": "If a building is started this turn, on which site?"}
        decode["build"] = ("build", None if len(sites) > 1 else f"s{sites[0]}")
    if avail.get("hire"):
        for kind in ("trained", "untrained"):
            if kind in avail["hire"]:
                qid = f"hire_{kind}"
                questions[qid] = {"type": "score", "criteria": [f"hire {n}" for n in range(HIRE_MAX + 1)],
                                  "instructions": f"How many {kind} workers should be hired this turn?"}
                decode[qid] = ("hire", kind)
    tmax = min(HIRE_MAX, int(avail.get("trainUntrainedMax") or 0))
    if tmax > 0:
        questions["train"] = {"type": "score", "criteria": [f"train {n}" for n in range(tmax + 1)],
                              "instructions": "How many untrained workers should be trained this turn?"}
        decode["train"] = ("train", None)
    for i, name in enumerate(sorted(avail.get("needStaff") or {})):
        qid = f"staff_{i}"
        questions[qid] = {"type": "noul", "instructions": f"Staff '{name}' fully from the free workers now?"}
        decode[qid] = ("staff", name)
    return {"model": model, "state": obs, "questions": questions}, decode


def _pick(options: list, probs: list, mode: str, rng: random.Random):
    if mode == "sample":
        return rng.choices(options, weights=probs, k=1)[0]
    return options[max(range(len(options)), key=lambda i: probs[i])]


def _choice(ans: dict, mode: str, rng) -> Optional[str]:
    probs = ans.get("probabilities")
    if isinstance(probs, dict) and probs:
        opts = list(probs)
        return _pick(opts, [float(probs[o]) for o in opts], mode, rng)
    return ans.get("choice")


def _score(ans: dict, mode: str, rng) -> int:
    probs = ans.get("probabilities")
    if isinstance(probs, list) and probs:
        return int(_pick(list(range(len(probs))), [float(p) for p in probs], mode, rng))
    if isinstance(probs, dict) and probs:                     # {"0": p, "1": p, ...}
        opts = sorted(probs, key=lambda k: int(k))
        return int(_pick(opts, [float(probs[o]) for o in opts], mode, rng))
    return int(round(float(ans.get("score", 0))))


def _noul(ans: dict, mode: str, rng) -> bool:
    for key in ("probability", "p_true", "true"):
        if isinstance(ans.get(key), (int, float)):
            p = float(ans[key]); break
    else:
        probs = ans.get("probabilities")
        if isinstance(probs, dict) and "true" in probs:
            p = float(probs["true"])
        elif isinstance(ans.get("value"), bool):
            return ans["value"]
        else:
            return False
    return (rng.random() < p) if mode == "sample" else (p >= 0.5)


def tool_calls(answers: dict, decode: dict, mode: str = "argmax", rng: Optional[random.Random] = None) -> list:
    """The answers as (tool, args) calls, in the executor's order: task answers, then actions."""
    rng = rng or random.Random(0)
    calls = []
    for qid, (kind, arg) in decode.items():
        ans = answers.get(qid)
        if not isinstance(ans, dict):
            continue
        if kind == "task":
            c = _choice(ans, mode, rng)
            if c and c != "skip":
                calls.append(("task", {"task_id": arg, "choice_id": int(str(c).lstrip("c"))}))
        elif kind == "build":
            t = _choice(ans, mode, rng)
            site = arg or _choice(answers.get("build_site") or {}, mode, rng)
            if t and t != "none" and site:
                calls.append(("build", {"type": t, "site_id": int(str(site).lstrip("s"))}))
        elif kind == "hire":
            n = _score(ans, mode, rng)
            if n > 0:
                calls.append(("hire", {"kind": arg, "count": n}))
        elif kind == "train":
            n = _score(ans, mode, rng)
            if n > 0:
                calls.append(("train", {"count": n}))
        elif kind == "staff":
            if _noul(ans, mode, rng):
                calls.append(("staff", {"site": arg}))
    return calls


class Client:
    """POST /v1/systemone on a local or remote decision-model server."""

    def __init__(self, base_url: str, api_key: Optional[str] = None, timeout: float = 120.0):
        self.url = base_url.rstrip("/") + ("" if base_url.rstrip("/").endswith("/v1/systemone") else "/v1/systemone")
        self.api_key, self.timeout = api_key, timeout

    def __call__(self, body: dict) -> dict:
        req = urllib.request.Request(self.url, json.dumps(body).encode(), {"Content-Type": "application/json"})
        if self.api_key:
            req.add_header("Authorization", f"Bearer {self.api_key}")
        with urllib.request.urlopen(req, timeout=self.timeout) as r:
            return json.load(r)


def act(obs: dict, model: str, client, mode: str = "argmax", rng=None) -> tuple[list, dict]:
    """One turn: (tool calls, the raw response) for the observation `obs`."""
    body, decode = build_request(obs, model)
    if not body["questions"]:
        return [], {"answers": {}, "note": "nothing to decide"}
    resp = client(body)
    return tool_calls(resp.get("answers") or {}, decode, mode, rng), resp
