"""Serve a Clef decision model (Cloudflare/clef, clef-flash) on POST /v1/systemone.

Clef ships its runtime as `joint_schema_model.py` in the checkpoint (load_release_model, systemone)
rather than as a server; this wraps it in the Jev/SystemOne HTTP shape the other decision models
serve natively, so bench.decision drives every model the same way.

    python ops/serve_clef.py /path/to/clef-flash --port 8810
    curl -s localhost:8810/health
"""
from __future__ import annotations

import argparse
import json
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def load_split(path):
    """load_release_model with the backbone split over the visible GPUs (device_map="auto"); the
    joint head sits with the output embeddings, and its inputs are moved there."""
    import json as _json
    from pathlib import Path
    import torch
    from safetensors.torch import load_file
    from transformers import AutoProcessor, Qwen3_5ForConditionalGeneration
    from joint_schema_model import ClefModel, JointSchemaHead
    path = Path(path)
    backbone = Qwen3_5ForConditionalGeneration.from_pretrained(path, dtype=torch.bfloat16, device_map="auto")
    backbone.config.use_cache = False
    head = JointSchemaHead(**_json.loads((path / "joint_head_config.json").read_text()))
    head.load_state_dict(load_file(path / "joint_head.safetensors"), strict=True)
    dev = backbone.get_output_embeddings().weight.device
    head = head.to(device=dev, dtype=torch.bfloat16)
    inner = head.forward

    def forward(hidden, input_ids, attention_mask, records, weight):
        return inner(hidden.to(dev), input_ids.to(dev), attention_mask.to(dev), records, weight.to(dev))
    head.forward = forward
    return ClefModel(backbone, head).eval(), AutoProcessor.from_pretrained(path)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("checkpoint", help="local clef / clef-flash snapshot (holds joint_schema_model.py)")
    ap.add_argument("--port", type=int, default=8810)
    ap.add_argument("--name", default="clef-flash")
    ap.add_argument("--max-length", type=int, default=16384)
    ap.add_argument("--device-map", default=None,
                    help='"auto": split the backbone over every visible GPU (Clef 27B on 2 x 48 GB)')
    a = ap.parse_args(argv)

    sys.path.insert(0, a.checkpoint)
    from joint_schema_model import load_release_model, systemone

    t = time.time()
    model, processor = (load_split(a.checkpoint) if a.device_map == "auto"
                        else load_release_model(a.checkpoint, device="cuda"))
    print(f"[serve_clef] loaded {a.checkpoint} in {time.time() - t:.0f}s", flush=True)
    lock = threading.Lock()                     # one GPU, one forward pass at a time
    stats = {"requests": 0, "seconds": 0.0}

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
                req["model"] = a.name
                t0 = time.time()
                with lock:
                    resp = systemone(model, processor, req, max_length=a.max_length)
                dt = time.time() - t0
                stats["requests"] += 1; stats["seconds"] += dt
                resp.setdefault("usage", {})["latency_s"] = round(dt, 4)
                self._send(200, resp)
            except Exception as e:                  # a bad request must not kill the server
                self._send(400, {"error": f"{type(e).__name__}: {e}"})

        def log_message(self, *args):
            pass

    print(f"[serve_clef] listening on :{a.port}", flush=True)
    ThreadingHTTPServer(("0.0.0.0", a.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
