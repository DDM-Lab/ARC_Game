#!/usr/bin/env python3
"""Runs CoraUpload.jslib in a real (headless) browser against fault_server.py.

    python tests/upload_browser/run_browser_tests.py [--browser PATH] [--keep-logs]

This tests the browser half of the upload (compression, progress, stall / no-reply / hard-limit
timeouts, errors, abort, the leave warning) with the real save_game_logs.py behind it. It does
not test the C# half or a real WebGL build: see docs/upload-reliability.md for that.
"""

import argparse
import glob
import json
import os
import queue
import shutil
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import fault_server  # noqa: E402

BROWSERS = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
]


def find_browser(explicit):
    if explicit:
        return explicit
    for path in BROWSERS:
        if os.path.exists(path):
            return path
    sys.exit("No Chrome/Edge found; pass --browser PATH")


def kill(proc):
    if proc.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], capture_output=True)
    else:
        proc.kill()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--browser")
    parser.add_argument("--timeout", type=float, default=300)
    parser.add_argument("--keep-logs", action="store_true", help="print the server's log directory and keep it")
    args = parser.parse_args()

    browser = find_browser(args.browser)
    server, log_dir, cleanup = fault_server.start(0)
    port = server.server_address[1]
    profile = tempfile.mkdtemp(prefix="cora_upload_profile_")
    url = "http://127.0.0.1:{}/harness.html".format(port)
    proc = subprocess.Popen([browser, "--headless=new", "--disable-gpu", "--no-first-run",
                             "--no-default-browser-check", "--disable-extensions",
                             "--user-data-dir=" + profile, url],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    started = time.time()
    try:
        report = server.reports.get(timeout=args.timeout)
    except queue.Empty:
        kill(proc)
        cleanup()
        sys.exit("No report from the browser within {} s".format(args.timeout))
    finally:
        kill(proc)
        shutil.rmtree(profile, ignore_errors=True)

    print("Browser: {}".format(report["userAgent"]))
    print("Ran {} scenarios in {:.0f} s\n".format(len(report["results"]), time.time() - started))
    failures = 0
    for r in report["results"]:
        detail = r.get("detail") or {}
        res = detail.get("result") or {}
        extra = []
        if res:
            extra.append("state={} error={} status={} {:.1f}s".format(
                res.get("state"), res.get("error") or "-", res.get("status"), detail.get("ms", 0) / 1000.0))
        if detail.get("ratio"):
            extra.append("gzip {}x ({} -> {} bytes)".format(detail["ratio"], detail["payloadBytes"], res.get("bodyBytes")))
        if detail.get("distinctProgress"):
            extra.append("{} progress steps".format(detail["distinctProgress"]))
        print("{} {}  {}".format("PASS" if r["pass"] else "FAIL", r["name"], "; ".join(extra)))
        for problem in r["problems"]:
            print("       - " + problem)
        failures += 0 if r["pass"] else 1

    # Server side: the gzip upload and its duplicate.
    run = report["run"]
    base = os.path.join(log_dir, "browser_gz{}_final".format(run))
    problems = []
    if not os.path.exists(base + ".csv") or not os.path.exists(base + ".upload.json.gz"):
        problems.append("gzip upload's CSV / .upload.json.gz not on disk")
    else:
        with open(base + ".upload_log.jsonl", encoding="utf-8") as f:
            log = [json.loads(line) for line in f if '"event": "upload"' in line]
        results = [(e["result"], e["attempt"]) for e in log]
        if results != [("saved", 1), ("duplicate", 2)]:
            problems.append("upload log: {}".format(results))
    stray = [os.path.basename(p) for p in glob.glob(os.path.join(log_dir, "browser_gz{}_*.csv".format(run)))]
    if stray != ["browser_gz{}_final.csv".format(run)]:
        problems.append("expected one final CSV for the session, found {}".format(stray))
    print("{} server kept one file set for the session, upload log = saved, duplicate".format(
        "FAIL" if problems else "PASS"))
    for problem in problems:
        print("       - " + problem)
    failures += 1 if problems else 0

    if args.keep_logs:
        print("\nServer log directory kept: " + log_dir)
        server.shutdown()
    else:
        cleanup()
    print("\n{} failed".format(failures) if failures else "\nAll passed")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
