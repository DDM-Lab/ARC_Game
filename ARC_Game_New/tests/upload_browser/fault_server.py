#!/usr/bin/env python3
"""A local upload endpoint with injectable faults, for testing the game's log upload in a browser.

POST /save runs the real save_game_logs.py (as a CGI process, logs in a temp directory) unless the
query string asks for a fault:

    fault=slow&rate=<bytes/s>       read the body at that rate, then save normally
    fault=stall&after=<bytes>       read that many bytes, then stop reading (hold the connection)
    fault=hang                      read the whole body, then never reply (hold the connection)
    fault=status&code=<n>           read the body, reply with HTTP <n>
    fault=reset&after=<bytes>       read that many bytes, then drop the connection (TCP reset)
    fault=bad_ack                   reply 200 with an ack whose byte count is wrong
    fault=old_server                refuse gzip bodies the way the pre-gzip script did (400)
    fault=fail_first&mode=<m>       fail the client's first attempt with <m> (stall, hang, status503,
                                    reset), then save normally. "First attempt" is every request
                                    with attempt=1 in the query string (LogSender sends it), so a
                                    browser's own silent resend of that attempt fails too; without
                                    one, the first request of each uploadId.

Other routes: POST /report stores a JSON test report; GET serves the browser harness, the plugin
under test (/CoraUpload.jslib), and, with --serve-dir, a WebGL build.

Used by run_browser_tests.py. To test a WebGL build by hand:

    python tests/upload_browser/fault_server.py --serve-dir Build/UploadSelfTest --port 8770
"""

import argparse
import json
import os
import queue
import shutil
import socket
import struct
import subprocess
import sys
import tempfile
import threading
import time
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
SAVE_SCRIPT = os.path.join(REPO, "save_game_logs.py")
JSLIB = os.path.join(REPO, "Assets", "Plugins", "WebGL", "CoraUpload.jslib")

HOLD_SECONDS = 90  # how long a stalled/hung request is held open before the server gives up

CONTENT_TYPES = {
    ".html": "text/html; charset=utf-8", ".js": "application/javascript", ".jslib": "application/javascript",
    ".json": "application/json", ".wasm": "application/wasm", ".data": "application/octet-stream",
    ".css": "text/css", ".png": "image/png", ".ico": "image/x-icon", ".txt": "text/plain",
}


class FaultServer(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True

    def __init__(self, address, log_dir, serve_dir=None):
        self.log_dir = log_dir
        self.serve_dir = serve_dir
        self.reports = queue.Queue()
        self.requests = []          # (path, query) of every POST /save, for assertions
        self.attempts = {}          # uploadId -> requests seen
        self.lock = threading.Lock()
        super().__init__(address, Handler)

    def server_bind(self):
        # Small receive buffer: a throttled or stalled read must hold the client back quickly,
        # instead of the OS accepting megabytes on the server's behalf.
        self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 32 * 1024)
        super().server_bind()

    def handle_error(self, request, client_address):
        # The client abandoning a stalled or held request is what most faults are testing.
        if isinstance(sys.exc_info()[1], (ConnectionError, TimeoutError)):
            return
        super().handle_error(request, client_address)


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):  # quiet
        pass

    # ── helpers ──────────────────────────────────────────────────────────────────────────────

    def reply(self, status, body, content_type="application/json"):
        data = body if isinstance(body, bytes) else body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def read_body(self, limit=None, rate=None):
        """Reads up to `limit` bytes of the body (all of it by default), at `rate` bytes/s if given."""
        length = int(self.headers.get("Content-Length") or 0)
        want = length if limit is None else min(limit, length)
        chunks, got = [], 0
        chunk = 16 * 1024
        while got < want:
            part = self.rfile.read(min(chunk, want - got))
            if not part:
                break
            chunks.append(part)
            got += len(part)
            if rate:
                time.sleep(len(part) / float(rate))
        return b"".join(chunks), length

    def hold(self):
        """Keeps the connection open without reading or replying, until the client gives up."""
        self.close_connection = True
        deadline = time.time() + HOLD_SECONDS
        while time.time() < deadline:
            time.sleep(0.25)

    def reset(self):
        self.close_connection = True
        try:
            self.connection.setsockopt(socket.SOL_SOCKET, socket.SO_LINGER, struct.pack("ii", 1, 0))
        except OSError:
            pass
        self.connection.close()

    def run_cgi(self, body, query):
        env = dict(os.environ, REQUEST_METHOD="POST", CONTENT_LENGTH=str(len(body)), QUERY_STRING=query,
                   GATEWAY_INTERFACE="CGI/1.1", ARC_LOG_DIR=self.server.log_dir,
                   CONTENT_TYPE=self.headers.get("Content-Type", ""),
                   HTTP_USER_AGENT=self.headers.get("User-Agent", ""))
        out = subprocess.run([sys.executable, SAVE_SCRIPT], input=body, capture_output=True, env=env).stdout
        out = out.replace(b"\r\n", b"\n")
        head, _, payload = out.partition(b"\n\n")
        status = 500
        for line in head.decode("utf-8", "replace").split("\n"):
            if line.lower().startswith("status:"):
                status = int(line.split()[1])
        self.reply(status, payload.strip())

    # ── routes ───────────────────────────────────────────────────────────────────────────────

    def do_OPTIONS(self):
        self.send_response(200)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_GET(self):
        path = urllib.parse.urlparse(self.path).path
        if path == "/CoraUpload.jslib":
            return self.send_file(JSLIB)
        if path in ("/harness.html", "/harness_shim.js"):
            return self.send_file(os.path.join(HERE, path.lstrip("/")))
        if self.server.serve_dir:
            rel = urllib.parse.unquote(path.lstrip("/")) or "index.html"
            full = os.path.realpath(os.path.join(self.server.serve_dir, rel))
            if full.startswith(os.path.realpath(self.server.serve_dir)) and os.path.isfile(full):
                return self.send_file(full)
        self.reply(404, "not found", "text/plain")

    def send_file(self, full):
        with open(full, "rb") as f:
            data = f.read()
        ext = os.path.splitext(full)[1].lower()
        self.reply(200, data, CONTENT_TYPES.get(ext, "application/octet-stream"))

    def do_POST(self):
        parsed = urllib.parse.urlparse(self.path)
        query = urllib.parse.parse_qs(parsed.query)

        def q(name, default=""):
            return query.get(name, [default])[0]

        if parsed.path == "/report":
            body, _ = self.read_body()
            self.server.reports.put(json.loads(body.decode("utf-8")))
            return self.reply(200, '{"ok": true}')

        if parsed.path != "/save":
            return self.reply(404, "not found", "text/plain")

        with self.server.lock:
            self.server.requests.append(parsed.query)
            upload_id = q("uploadId") or q("tag")
            seen = self.server.attempts.get(upload_id, 0)
            self.server.attempts[upload_id] = seen + 1

        fault = q("fault", "none")
        if fault == "fail_first":
            # Chromium silently resends a POST once when a reused keep-alive connection is reset
            # before any reply, so "the first request" is not the same as "the client's first attempt".
            first_attempt = q("attempt") == "1" if q("attempt") else seen == 0
            fault = q("mode") if first_attempt else "none"

        if fault == "none":
            body, _ = self.read_body()
            return self.run_cgi(body, parsed.query)
        if fault == "slow":
            body, _ = self.read_body(rate=int(q("rate", "200000")))
            return self.run_cgi(body, parsed.query)
        if fault == "stall":
            self.read_body(limit=int(q("after", "65536")))
            return self.hold()
        if fault == "hang":
            self.read_body()
            return self.hold()
        if fault in ("status", "status503"):
            self.read_body()
            code = 503 if fault == "status503" else int(q("code", "500"))
            return self.reply(code, json.dumps({"ok": False, "message": "injected HTTP {}".format(code)}))
        if fault == "reset":
            self.read_body(limit=int(q("after", "65536")))
            return self.reset()
        if fault == "bad_ack":
            body, _ = self.read_body()
            return self.reply(200, json.dumps({"ok": True, "bytes": len(body) + 1}))
        if fault == "old_server":
            body, _ = self.read_body()
            if body[:2] == b"\x1f\x8b":
                return self.reply(400, json.dumps({"ok": False, "status": "error",
                                                   "message": "Request body is not valid UTF-8"}))
            return self.run_cgi(body, parsed.query)
        self.reply(400, json.dumps({"ok": False, "message": "unknown fault " + fault}))


def start(port=0, log_dir=None, serve_dir=None):
    """Starts the server on a background thread. Returns (server, log_dir, cleanup)."""
    own_dir = log_dir is None
    log_dir = log_dir or tempfile.mkdtemp(prefix="cora_upload_logs_")
    server = FaultServer(("127.0.0.1", port), log_dir, serve_dir)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()

    def cleanup():
        server.shutdown()
        server.server_close()
        if own_dir:
            shutil.rmtree(log_dir, ignore_errors=True)

    return server, log_dir, cleanup


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=8770)
    parser.add_argument("--serve-dir", help="also serve this directory (e.g. a WebGL build)")
    parser.add_argument("--log-dir", help="where save_game_logs.py writes (default: a temp dir)")
    args = parser.parse_args()
    server, log_dir, cleanup = start(args.port, args.log_dir, args.serve_dir)
    print("Serving on http://127.0.0.1:{}/  (logs: {})".format(server.server_address[1], log_dir))
    try:
        while True:
            report = server.reports.get()
            print(json.dumps(report, indent=2))
    except KeyboardInterrupt:
        pass
    finally:
        cleanup()


if __name__ == "__main__":
    main()
