#!/usr/bin/env python3
"""Runs the upload self-test WebGL build (Editor/UploadSelfTestBuild.cs) in a headless browser.

    python tests/upload_browser/run_webgl_selftest.py --build-dir Build/UploadSelfTest [--browser PATH]

Serves the build and the fault-injecting /save endpoint (fault_server.py) from one origin, opens
the build, and prints the per-scenario report that UploadSelfTest posts back.
"""

import argparse
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
from run_browser_tests import find_browser, kill  # noqa: E402


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--build-dir", required=True)
    parser.add_argument("--browser")
    parser.add_argument("--timeout", type=float, default=900)
    parser.add_argument("--headed", action="store_true", help="show the browser window")
    args = parser.parse_args()

    if not os.path.isfile(os.path.join(args.build_dir, "index.html")):
        sys.exit("No index.html in " + args.build_dir)

    browser = find_browser(args.browser)
    server, log_dir, cleanup = fault_server.start(0, serve_dir=os.path.abspath(args.build_dir))
    port = server.server_address[1]
    profile = tempfile.mkdtemp(prefix="cora_selftest_profile_")
    # Headless has no GPU: render WebGL in software so the Unity build can start.
    flags = [] if args.headed else ["--headless=new", "--use-angle=swiftshader", "--enable-unsafe-swiftshader"]
    proc = subprocess.Popen([browser] + flags + [
        "--no-first-run", "--no-default-browser-check", "--disable-extensions",
        "--autoplay-policy=no-user-gesture-required", "--user-data-dir=" + profile,
        "http://127.0.0.1:{}/index.html".format(port)],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    started = time.time()
    try:
        report = server.reports.get(timeout=args.timeout)
    except queue.Empty:
        sys.exit("No report from the WebGL build within {} s".format(args.timeout))
    finally:
        kill(proc)
        shutil.rmtree(profile, ignore_errors=True)

    print("WebGL self-test: platform {}, run {}, {:.0f} s\n".format(report["platform"], report["run"], time.time() - started))
    failures = 0
    for r in report["results"]:
        print("{} {}".format("PASS" if r["pass"] else "FAIL", r["name"]))
        print("       " + r["details"])
        if r["problems"]:
            print("       - " + r["problems"])
        failures += 0 if r["pass"] else 1
    cleanup()
    print("\n{} failed".format(failures) if failures else "\nAll passed")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
