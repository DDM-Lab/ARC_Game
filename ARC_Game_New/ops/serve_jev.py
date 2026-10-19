"""Serve a JEV checkpoint (autotrust/JEV-9B, JEV-27B) on POST /v1/systemone, so bench.decision
drives it exactly like Clef.

JEV's System 1 runs inside vLLM: the checkpoint's `adapter_vllm` LoRA ("jev-decision") turns one
prefill into a decision, read back as the log-probabilities of the option tokens
(`allowed_token_ids`), plus the head's per-slot bias, divided by the per-kind temperature from
calibration.json (the recipe in the model card, "System 1: typed decisions"). One JEV request asks
one question; this bridge fans a SystemOne request's questions out concurrently and answers in
the SystemOne shape bench.decision reads:

    choice   {"choice": key, "probabilities": {key: p}}         (at most 16 options)
    score    {"score": i, "probabilities": [p0, p1, ...]}        (0..5)
    noul     {"noul": P(true), "probabilities": {"false": p, "true": p}}

Start vLLM first (see ops/benchmark_decision.sbatch), then:

    python ops/serve_jev.py /path/to/JEV-9B --vllm http://127.0.0.1:8000 --port 8810

`--backend hf` runs System 1 in-process instead (the card's "Plain transformers + peft" recipe:
the backbone with the `adapter` LoRA merged, the last token's hidden state through the 24-slot
head in head.safetensors, divided by the per-kind temperature). It needs no vLLM; vLLM 0.27 cannot
load adapter_vllm (it rejects a LoRA on lm_head).

    python ops/serve_jev.py /path/to/JEV-9B --backend hf --port 8810
"""
from __future__ import annotations

import argparse
import json
import math
import os
import threading
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

LETTERS = "ABCDEFGHIJKLMNOP"


def _post(url, body, timeout=300):
    req = urllib.request.Request(url, json.dumps(body).encode(), {"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.load(r)


class Jev:
    def __init__(self, ckpt, vllm_url, lora="jev-decision"):
        self.head = json.load(open(os.path.join(ckpt, "adapter_vllm", "decision_head.json")))
        self.temp = json.load(open(os.path.join(ckpt, "calibration.json")))["per_kind"]
        self.url, self.lora = vllm_url.rstrip("/") + "/v1/completions", lora

    def decide(self, kind, state, question, options):
        """The calibrated distribution over `options` (texts), as a list of probabilities."""
        lines = options if kind != "choice" else [f"{LETTERS[i]}) {o}" for i, o in enumerate(options)]
        prompt = (f"[kind] {kind}\n[state] {state}\n[question] {question}\n[options]\n"
                  + "\n".join(lines) + "\n[decision]:")
        s = self.head["slots"]["ranges"][kind][0]
        ids = self.head["verbalizer_ids"][s:s + len(options)]
        r = _post(self.url, {"model": self.lora, "prompt": prompt, "max_tokens": 1, "temperature": 1.0,
                             "logprobs": len(options), "allowed_token_ids": ids,
                             "add_special_tokens": False, "return_tokens_as_token_ids": True})
        lp = {int(k.split(":")[1]): v for k, v in r["choices"][0]["logprobs"]["top_logprobs"][0].items()}
        z = [(lp.get(t, -1e9) + self.head["bias"][s + i]) / self.temp[kind] for i, t in enumerate(ids)]
        e = [math.exp(x - max(z)) for x in z]
        return [x / sum(e) for x in e], r.get("usage") or {}

    def answer(self, state, qid, q):
        return _answer(self, state, q)


class JevHF(Jev):
    """System 1 in-process with transformers + peft (one GPU, one question at a time)."""

    def __init__(self, ckpt):
        import torch
        from peft import PeftModel
        from safetensors.torch import load_file
        from transformers import AutoModelForCausalLM, AutoTokenizer
        self.torch = torch
        self.tok = AutoTokenizer.from_pretrained(ckpt)
        base = AutoModelForCausalLM.from_pretrained(ckpt, dtype=torch.bfloat16, device_map="cuda")
        self.model = PeftModel.from_pretrained(base, os.path.join(ckpt, "adapter")).merge_and_unload().eval()
        head = load_file(os.path.join(ckpt, "head.safetensors"))
        self.W, self.b = head["proj.weight"].cuda().float(), head["proj.bias"].cuda().float()
        self.cfg = json.load(open(os.path.join(ckpt, "judge_config.json")))
        self.temp = json.load(open(os.path.join(ckpt, "calibration.json")))["per_kind"]
        self.lock = threading.Lock()

    def decide(self, kind, state, question, options):
        torch = self.torch
        lines = options if kind != "choice" else [f"{LETTERS[i]}) {o}" for i, o in enumerate(options)]
        text = (f"[kind] {kind}\n[state] {state}\n[question] {question}\n[options]\n"
                + "\n".join(lines) + "\n[decision]:")
        ids = self.tok(text, return_tensors="pt", add_special_tokens=False).to("cuda")
        with self.lock, torch.no_grad(), torch.autocast("cuda", dtype=torch.bfloat16):
            h = self.model.model(**ids).last_hidden_state[0, -1].float()
        z = (self.W @ h + self.b) / self.temp[kind]
        s = self.cfg["slots"]["ranges"][kind][0]
        p = torch.softmax(z[s:s + len(options)], 0).tolist()
        return p, {"prompt_tokens": int(ids["input_ids"].shape[1])}


def _answer(jev, state, q):
    """One SystemOne question answered by JEV, in the shape bench.decision reads."""
    kind = q["type"]
    if kind == "noul":
        p, u = jev.decide("noul", state, q["instructions"], ["false", "true"])
        return {"type": "noul", "noul": p[1], "probabilities": {"false": p[0], "true": p[1]}}, u
    if kind == "score":
        opts = list(q["criteria"])[:6]                   # JEV's score scale is 0..5
        p, u = jev.decide("score", state, q["instructions"], [str(i) for i in range(len(opts))])
        return {"type": "score", "score": max(range(len(p)), key=p.__getitem__), "probabilities": p}, u
    keys = list(q["criteria"])
    shown = keys[:16]                                    # JEV reads at most 16 options
    p, u = jev.decide("choice", state, q["instructions"], [str(q["criteria"][k]) for k in shown])
    probs = {k: (p[i] if i < len(shown) else 0.0) for i, k in enumerate(keys)}
    out = {"type": "choice", "choice": max(probs, key=probs.get), "probabilities": probs}
    if len(keys) > 16:
        out["truncated_options"] = len(keys) - 16
    return out, u


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("checkpoint", help="local JEV snapshot (holds adapter_vllm/ and calibration.json)")
    ap.add_argument("--vllm", default="http://127.0.0.1:8000", help="the vLLM server holding the jev-decision LoRA")
    ap.add_argument("--port", type=int, default=8810)
    ap.add_argument("--name", default="jev-9b")
    ap.add_argument("--threads", type=int, default=16)
    ap.add_argument("--backend", choices=["vllm", "hf"], default="vllm")
    a = ap.parse_args(argv)
    jev = JevHF(a.checkpoint) if a.backend == "hf" else Jev(a.checkpoint, a.vllm)
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
                return self._send(200, {"status": "ok", "model": a.name, **stats})
            self._send(404, {"error": "not found"})

        def do_POST(self):
            if self.path.rstrip("/") != "/v1/systemone":
                return self._send(404, {"error": "not found"})
            try:
                req = json.loads(self.rfile.read(int(self.headers.get("Content-Length") or 0)))
                st = req.get("state")
                state = st if isinstance(st, str) else json.dumps(st, separators=(",", ":"))
                t0 = time.time()
                qs = req.get("questions") or {}
                futs = {qid: pool.submit(_answer, jev, state, q) for qid, q in qs.items()}
                answers, tokens = {}, 0
                for qid, f in futs.items():
                    answers[qid], u = f.result()
                    tokens += int(u.get("prompt_tokens") or 0)
                dt = time.time() - t0
                with lock:
                    stats["requests"] += 1; stats["seconds"] += dt
                self._send(200, {"answers": answers,
                                 "usage": {"total_tokens": tokens, "latency_s": round(dt, 4)}})
            except Exception as e:                  # a bad request must not kill the server
                self._send(400, {"error": f"{type(e).__name__}: {e}"})

        def log_message(self, *args):
            pass

    print(f"[serve_jev] {a.checkpoint} via {a.vllm}, listening on :{a.port}", flush=True)
    ThreadingHTTPServer(("0.0.0.0", a.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
