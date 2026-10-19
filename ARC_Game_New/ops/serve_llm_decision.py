"""Serve any chat LLM as a decision model: POST /v1/systemone answered from next-token
probabilities, so bench.decision, oracle.annotate and oracle.search --leaf model drive a plain
LLM exactly like Clef or JEV.

Each typed question becomes one multiple-choice prompt: the game's rules as the system message,
the turn's state and then the question with labelled options as the user message, thinking off.
The model's distribution over the labels' first token (vLLM logprobs restricted to the label
tokens) is the answer -- one prefill per question and no generated text. A turn's questions share
the rules-and-state prefix, so vLLM's prefix cache computes it once.

    choice   options lettered A, B, ...        {"choice": key, "probabilities": {key: p}}
    score    options numbered 0, 1, ...        {"score": i, "probabilities": [p0, p1, ...]}
    noul     A) yes  B) no                     {"noul": P(yes), "probabilities": {...}}

The state is shown as the LLM arms see it (`--state compact`: cora.observation.user_message, the
default) or as the JSON the decision models get (`--state json`).

Start vLLM with logprobs over the restricted tokens, then the bridge:

    vllm serve Qwen/Qwen3.8-27B --tensor-parallel-size 2 --logprobs-mode processed_logprobs \\
        --max-logprobs 32 --enable-prefix-caching --port 8000
    python ops/serve_llm_decision.py Qwen/Qwen3.8-27B --vllm http://127.0.0.1:8000 --port 8810
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
import threading
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))   # the repo, for cora
LETTERS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"


def _post(url, body, timeout=600):
    req = urllib.request.Request(url, json.dumps(body).encode(), {"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.load(r)


class LLM:
    def __init__(self, model, vllm_url, state_format="compact"):
        from transformers import AutoTokenizer
        self.tok = AutoTokenizer.from_pretrained(model)
        self.model, self.url, self.state_format = model, vllm_url.rstrip("/") + "/v1/completions", state_format
        self.ids = {}
        for lab in list(LETTERS) + [str(i) for i in range(10)]:
            t = self.tok.encode(lab, add_special_tokens=False)
            if len(t) == 1:
                self.ids[lab] = t[0]

    def _state_text(self, state):
        obs = state.get("observation") if isinstance(state, dict) and "observation" in state else state
        if self.state_format == "compact" and isinstance(obs, dict):
            from cora.observation import user_message
            return user_message(obs, "compact")
        return json.dumps(obs, separators=(",", ":"))

    def decide(self, state, question, labels, texts):
        """The distribution over `labels` (one token each) for a question with option `texts`."""
        rules = state.get("rules") if isinstance(state, dict) else None
        opts = "\n".join(f"{l}) {t}" for l, t in zip(labels, texts))
        user = (f"{self._state_text(state)}\n\nQUESTION: {question}\nOPTIONS:\n{opts}\n\n"
                f"Answer with the label of one option ({', '.join(labels)}) and nothing else.")
        msgs = ([{"role": "system", "content": rules}] if rules else []) + [{"role": "user", "content": user}]
        prompt = self.tok.apply_chat_template(msgs, tokenize=False, add_generation_prompt=True,
                                              enable_thinking=False)
        ids = [self.ids[l] for l in labels]
        r = _post(self.url, {"model": self.model, "prompt": prompt, "max_tokens": 1, "temperature": 1.0,
                             "logprobs": len(ids), "allowed_token_ids": ids, "add_special_tokens": False,
                             "return_tokens_as_token_ids": True})
        lp = {int(k.split(":")[1]): v for k, v in r["choices"][0]["logprobs"]["top_logprobs"][0].items()}
        z = [lp.get(t, -1e9) for t in ids]
        e = [math.exp(x - max(z)) for x in z]
        return [x / sum(e) for x in e], r.get("usage") or {}


def answer(llm, state, q):
    """One SystemOne question, in the shape bench.decision reads."""
    kind = q["type"]
    if kind == "noul":
        p, u = llm.decide(state, q["instructions"], ["A", "B"], ["yes", "no"])
        return {"type": "noul", "noul": p[0], "probabilities": {"true": p[0], "false": p[1]}}, u
    if kind == "score":
        texts = [str(t) for t in q["criteria"]][:10]
        p, u = llm.decide(state, q["instructions"], [str(i) for i in range(len(texts))], texts)
        return {"type": "score", "score": max(range(len(p)), key=p.__getitem__), "probabilities": p}, u
    keys = list(q["criteria"])[:26]
    p, u = llm.decide(state, q["instructions"], list(LETTERS[:len(keys)]), [str(q["criteria"][k]) for k in keys])
    probs = {k: (p[i] if i < len(keys) else 0.0) for i, k in enumerate(q["criteria"])}
    return {"type": "choice", "choice": max(probs, key=probs.get), "probabilities": probs}, u


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("model", help="the model vLLM serves (its tokenizer and chat template are read here)")
    ap.add_argument("--vllm", default="http://127.0.0.1:8000")
    ap.add_argument("--port", type=int, default=8810)
    ap.add_argument("--name", default=None)
    ap.add_argument("--state", choices=["compact", "json"], default="compact")
    ap.add_argument("--threads", type=int, default=32)
    a = ap.parse_args(argv)
    llm = LLM(a.model, a.vllm, a.state)
    pool = ThreadPoolExecutor(a.threads)
    stats, lock = {"requests": 0, "seconds": 0.0}, threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        def _send(self, code, obj):
            body = json.dumps(obj).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if self.path.rstrip("/") == "/health":
                return self._send(200, {"status": "ok", "model": a.name or a.model, **stats})
            self._send(404, {"error": "not found"})

        def do_POST(self):
            if self.path.rstrip("/") != "/v1/systemone":
                return self._send(404, {"error": "not found"})
            try:
                req = json.loads(self.rfile.read(int(self.headers.get("Content-Length") or 0)))
                state = req.get("state")
                t0 = time.time()
                futs = {qid: pool.submit(answer, llm, state, q) for qid, q in (req.get("questions") or {}).items()}
                answers, tokens = {}, 0
                for qid, f in futs.items():
                    answers[qid], u = f.result()
                    tokens += int(u.get("prompt_tokens") or 0)
                dt = time.time() - t0
                with lock:
                    stats["requests"] += 1; stats["seconds"] += dt
                self._send(200, {"answers": answers, "usage": {"total_tokens": tokens, "latency_s": round(dt, 4)}})
            except Exception as e:                  # a bad request must not kill the server
                import traceback
                traceback.print_exc()
                sys.stderr.flush()
                self._send(400, {"error": f"{type(e).__name__}: {e}"})

        def log_message(self, *args):
            pass

    print(f"[serve_llm_decision] {a.model} via {a.vllm} (state {a.state}), listening on :{a.port}", flush=True)
    ThreadingHTTPServer(("0.0.0.0", a.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
