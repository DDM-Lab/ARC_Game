#!/usr/bin/env python3
"""cora — the one script a collaborator runs.

Run it bare for a guided menu, or use a subcommand directly:

    python cora.py                      # interactive menu
    python cora.py doctor               # is my setup working?
    python cora.py new  yourlab/terse   # scaffold an experiment
    python cora.py check bundles/...    # validate + authoring warnings
    python cora.py push  bundles/...    # upload to the server
    python cora.py data                 # list / download your sessions
    python cora.py sft   corpus.tar.gz  # corpus -> SFT pairs

This is a THIN front-end. Validation is bundle.load_bundle + bundle.config_warnings, the same
functions the upload endpoint runs; plugin checks shell out to cora_plugin.py; SFT conversion
to export_sft.py. Nothing here reimplements a rule, so the CLI and the server can never
disagree about whether a bundle is acceptable.

Config comes from the environment (or the flags):
    CORA_URL   default http://localhost:9876
    CORA_KEY   default dev-local-key   (the unrestricted local dev key)
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

from bundle import BundleError, config_warnings, load_bundle
from cora_bundle import _tls_safe

DEFAULT_URL = os.environ.get("CORA_URL", "http://localhost:9876")
DEFAULT_KEY = os.environ.get("CORA_KEY", "dev-local-key")

# Terminal styling, disabled when not a TTY or when NO_COLOR is set (piping to a file should
# not embed escape codes).
_TTY = sys.stdout.isatty() and not os.environ.get("NO_COLOR")


def _c(code: str, s: str) -> str:
    return f"\033[{code}m{s}\033[0m" if _TTY else s


def ok(s: str) -> str: return _c("32", s)
def warn(s: str) -> str: return _c("33", s)
def bad(s: str) -> str: return _c("31", s)
def dim(s: str) -> str: return _c("2", s)
def bold(s: str) -> str: return _c("1", s)


# ── HTTP ────────────────────────────────────────────────────────────────────
def api(path: str, url: str, key: str, method: str = "GET", body: bytes | None = None,
        raw: bool = False):
    """One request. Returns (status, parsed_or_bytes). Never raises for HTTP errors — the
    caller decides how to present them, since 401/403 are expected, diagnosable states."""
    target, extra = _tls_safe(url.rstrip("/") + path)
    req = urllib.request.Request(
        target, data=body, method=method,
        headers={"Authorization": f"Bearer {key}",
                 "Content-Type": "application/json", **extra})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            data = r.read()
            if raw:
                return r.status, data
            try:
                return r.status, json.loads(data)
            except json.JSONDecodeError:
                return r.status, data.decode(errors="replace")
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")
        try:
            detail = json.loads(detail).get("detail", detail)
        except Exception:
            pass
        return e.code, detail
    except urllib.error.URLError as e:
        return 0, f"cannot reach {url}: {e.reason}"


# ── doctor ──────────────────────────────────────────────────────────────────
def cmd_doctor(args) -> int:
    """Answer 'is my setup working?' in one shot.

    Ordered so the FIRST failure is the real one: an unreachable server makes every later
    check fail for an unrelated-looking reason, so we stop rather than print a cascade.
    """
    url, key = args.url, args.key
    print(bold("CORA setup check"))
    print(f"  url  {url}")
    print(f"  key  {key[:6]}…{'  ' + dim('(default dev key)') if key == 'dev-local-key' else ''}")
    print()

    st, health = api("/health", url, key)
    if st == 0:
        print(bad("  ✗ server unreachable") + f"  {health}")
        print(dim("    start one locally:  python agent_router.py --port 9876"))
        return 1
    if st != 200:
        print(bad(f"  ✗ /health returned {st}") + f"  {health}")
        return 1
    print(ok("  ✓ server reachable") +
          f"  ({health.get('live_sessions', 0)} live session(s), "
          f"{health.get('configs_available', 0)} configs)")

    st, me = api("/whoami", url, key)
    if st == 401:
        print(bad("  ✗ key rejected") + "  — check CORA_KEY, or ask for a new one")
        return 1
    if st == 404:
        # Older server without /whoami: fall back so doctor still works against it.
        st2, _ = api("/configs", url, key)
        if st2 == 200:
            print(warn("  ~ key valid") + dim("  (server predates /whoami — upgrade it for detail)"))
            return 0
        print(bad(f"  ✗ key rejected ({st2})"))
        return 1
    if st != 200:
        print(bad(f"  ✗ /whoami returned {st}") + f"  {me}")
        return 1

    print(ok("  ✓ key valid") + f"  label={me.get('label')}  role={me.get('role')}")
    scope = me.get("config_scope")
    print(f"    configs visible : {scope if scope == 'all' else len(scope)}")
    print(f"    upload configs  : {ok('yes') if me.get('can_upload_configs') else bad('no')}")
    print(f"    upload plugins  : "
          f"{ok('yes') if me.get('can_upload_code') else dim('no (needs upload_code)')}")
    print(f"    mint keys       : "
          f"{ok('yes') if me.get('can_mint_keys') else dim('no (needs mint)')}")

    st, cfgs = api("/configs", url, key)
    if st == 200:
        names = [c.get("name", c) if isinstance(c, dict) else c
                 for c in (cfgs.get("configs") if isinstance(cfgs, dict) else cfgs) or []]
        print(ok("  ✓ config catalog") + f"  {len(names)} available")
        for n in names[:8]:
            print(dim(f"      {n}"))
        if len(names) > 8:
            print(dim(f"      … and {len(names) - 8} more"))
    print()
    print(ok("Ready.") + dim("  next:  python cora.py new yourlab/my-experiment"))
    return 0


# ── bundle lifecycle (delegates to cora_bundle) ─────────────────────────────
def _run(argv: list[str]) -> int:
    """Invoke another project CLI in-process so exceptions and exit codes behave."""
    import cora_bundle
    return cora_bundle.main(argv)


def cmd_new(args) -> int:
    argv = ["new", args.name, "--author", args.author or os.environ.get("USER", "")]
    if args.delta:
        argv.append("--delta")
    if args.provider:
        argv += ["--provider", args.provider]
    if args.out:
        argv += ["--out", args.out]
    rc = _run(argv)
    if rc == 0:
        owner, slug = args.name.split("/", 1)
        path = args.out or f"bundles/{owner}/{slug}.json"
        print()
        print(bold("next:"))
        print(f"  1. edit   {path}")
        print(f"  2. check  python cora.py check {path}")
        print(f"  3. push   python cora.py push  {path}")
    return rc


def cmd_check(args) -> int:
    argv = ["validate", args.bundle]
    if args.base:
        argv += ["--base", args.base]
    return _run(argv)


def cmd_push(args) -> int:
    argv = ["push", args.bundle, "--url", args.url, "--key", args.key]
    if args.base:
        argv += ["--base", args.base]
    rc = _run(argv)
    if rc == 0:
        print()
        print(bold("uploaded.") + " It is private to your key and now selectable in the game's")
        print("config picker. Open the game and pick it to play-test:")
        print(f"  {args.url.replace(':9876', ':8000')}")
    return rc


def cmd_plugin(args) -> int:
    """Offline plugin check, then optional upload (staged, not activated)."""
    rc = subprocess.call([sys.executable, "cora_plugin.py", "check", args.file])
    if rc != 0 or not args.upload:
        return rc
    name = Path(args.file).stem
    st, resp = api(f"/plugins?name={urllib.parse.quote(name)}", args.url, args.key,
                   method="POST", body=Path(args.file).read_bytes())
    if st != 200:
        print(bad(f"upload failed ({st}): {resp}"), file=sys.stderr)
        return 1
    print(json.dumps(resp, indent=2))
    print()
    print(warn("STAGED, not running.") + " Activation imports and executes your code on the")
    print("server, so a maintainer reviews it first, then runs:")
    print(dim("  curl -X POST http://127.0.0.1:9877/admin/plugins/reload"))
    return 0


# ── data ────────────────────────────────────────────────────────────────────
def cmd_data(args) -> int:
    if args.session:
        st, blob = api(f"/my/sessions/{urllib.parse.quote(args.session)}",
                       args.url, args.key, raw=True)
        if st != 200:
            print(bad(f"({st}) {blob}"), file=sys.stderr)
            return 1
        out = Path(args.out or f"{args.session}.jsonl")
        out.write_bytes(blob)
        print(ok(f"wrote {out}") + dim(f"  ({len(blob):,} bytes)"))
        return 0

    if args.export:
        q = "format=tar" + (f"&config={urllib.parse.quote(args.config)}" if args.config else "")
        st, blob = api(f"/my/sessions/export?{q}", args.url, args.key, raw=True)
        if st != 200:
            print(bad(f"({st}) {blob}"), file=sys.stderr)
            return 1
        out = Path(args.out or "corpus.tar.gz")
        out.write_bytes(blob)
        print(ok(f"wrote {out}") + dim(f"  ({len(blob):,} bytes)"))
        print(dim(f"  -> python cora.py sft {out}"))
        return 0

    st, resp = api("/my/sessions", args.url, args.key)
    if st != 200:
        print(bad(f"({st}) {resp}"), file=sys.stderr)
        return 1
    sessions = resp.get("sessions", []) if isinstance(resp, dict) else []
    print(bold(f"cohort {resp.get('label')}") + f"  —  {resp.get('count', len(sessions))} session(s)")
    for s in sessions[:25]:
        print(f"  {s.get('session_id','?'):<38} {s.get('config','?'):<26} {s.get('started_at','')}")
    if len(sessions) > 25:
        print(dim(f"  … and {len(sessions) - 25} more"))
    if sessions:
        print()
        print(dim("  one:  python cora.py data --session <id>"))
        print(dim("  all:  python cora.py data --export"))
    return 0


def cmd_sft(args) -> int:
    argv = [sys.executable, "export_sft.py", "--from-sessions", args.corpus, "--out", args.out]
    if args.agent:
        argv += ["--agent", args.agent]
    if args.min_reward is not None:
        argv += ["--min-reward", str(args.min_reward)]
    return subprocess.call(argv)


# ── interactive menu ────────────────────────────────────────────────────────
MENU = [
    ("Check my setup", "doctor"),
    ("Create a new experiment", "new"),
    ("Check an experiment file", "check"),
    ("Upload it to the server", "push"),
    ("Check / upload a plugin", "plugin"),
    ("List or download my data", "data"),
    ("Quit", "quit"),
]


def _ask(prompt: str, default: str = "") -> str:
    try:
        v = input(f"{prompt}{dim(f' [{default}]') if default else ''}: ").strip()
    except (EOFError, KeyboardInterrupt):
        print()
        raise SystemExit(0)
    return v or default


def _pick_bundle() -> str | None:
    """Offer existing bundles rather than making someone type a path from memory."""
    found = sorted(str(p) for p in Path("bundles").rglob("*.json")) if Path("bundles").is_dir() else []
    if not found:
        return _ask("path to your bundle .json") or None
    print()
    for i, f in enumerate(found[:20], 1):
        print(f"  {i}) {f}")
    print(dim("  (or type a path)"))
    v = _ask("which", "1")
    if v.isdigit() and 1 <= int(v) <= min(len(found), 20):
        return found[int(v) - 1]
    return v or None


def interactive(args) -> int:
    print(bold("\nCORA — collaborator console"))
    print(dim(f"  {args.url}   key {args.key[:6]}…\n"))
    while True:
        for i, (label, _) in enumerate(MENU, 1):
            print(f"  {i}) {label}")
        choice = _ask("\nchoose", "1")
        if not choice.isdigit() or not 1 <= int(choice) <= len(MENU):
            print(bad("  pick a number from the list\n"))
            continue
        action = MENU[int(choice) - 1][1]
        print()
        if action == "quit":
            return 0
        if action == "doctor":
            cmd_doctor(args)
        elif action == "new":
            name = _ask("experiment name (owner/slug)", "yourlab/my-experiment")
            if "/" not in name:
                print(bad("  name must be owner/slug\n")); continue
            ns = argparse.Namespace(name=name, author=os.environ.get("USER", ""),
                                    delta=False, provider=None, out=None)
            cmd_new(ns)
        elif action in ("check", "push"):
            b = _pick_bundle()
            if not b:
                continue
            ns = argparse.Namespace(bundle=b, base=None, url=args.url, key=args.key)
            (cmd_check if action == "check" else cmd_push)(ns)
        elif action == "plugin":
            f = _ask("path to your plugin .py")
            if not f:
                continue
            up = _ask("upload after checking? (y/N)", "N").lower().startswith("y")
            cmd_plugin(argparse.Namespace(file=f, upload=up, url=args.url, key=args.key))
        elif action == "data":
            cmd_data(argparse.Namespace(session=None, export=False, config=None,
                                        out=None, url=args.url, key=args.key))
        print()


# ── parser ──────────────────────────────────────────────────────────────────
def build_parser() -> argparse.ArgumentParser:
    # --url/--key live on a PARENT parser attached to every subcommand as well as the top
    # level, so both `cora --url X doctor` and `cora doctor --url X` work. argparse otherwise
    # accepts only the first form, and "unrecognized arguments: --url" is a hostile way to
    # greet someone on their first command.
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--url", default=DEFAULT_URL, help=f"router URL (default {DEFAULT_URL})")
    common.add_argument("--key", default=DEFAULT_KEY, help="your API key (env CORA_KEY)")

    p = argparse.ArgumentParser(
        prog="cora", parents=[common],
        description="CORA collaborator workflow (run bare for a menu)")
    sub = p.add_subparsers(dest="cmd")

    sub.add_parser("doctor", parents=[common],
                   help="check server, key and capabilities").set_defaults(func=cmd_doctor)

    pn = sub.add_parser("new", parents=[common], help="scaffold an experiment bundle")
    pn.add_argument("name", help="owner/slug")
    pn.add_argument("--author", default=None)
    pn.add_argument("--delta", action="store_true", help="override a base config instead")
    pn.add_argument("--provider", default=None)
    pn.add_argument("--out", default=None)
    pn.set_defaults(func=cmd_new)

    pc = sub.add_parser("check", parents=[common], help="validate a bundle + authoring warnings")
    pc.add_argument("bundle")
    pc.add_argument("--base", default=None, help="base config (delta bundles only)")
    pc.set_defaults(func=cmd_check)

    pp = sub.add_parser("push", parents=[common], help="upload a bundle to the server")
    pp.add_argument("bundle")
    pp.add_argument("--base", default=None)
    pp.set_defaults(func=cmd_push)

    pl = sub.add_parser("plugin", parents=[common], help="check (and optionally stage) a plugin")
    pl.add_argument("file")
    pl.add_argument("--upload", action="store_true", help="stage it on the server after checking")
    pl.set_defaults(func=cmd_plugin)

    pd = sub.add_parser("data", parents=[common], help="list or download your sessions")
    pd.add_argument("--session", default=None, help="download one session by id")
    pd.add_argument("--export", action="store_true", help="download the whole cohort as tar.gz")
    pd.add_argument("--config", default=None, help="filter the export to one config")
    pd.add_argument("--out", default=None)
    pd.set_defaults(func=cmd_data)

    ps = sub.add_parser("sft", parents=[common], help="turn a downloaded corpus into SFT pairs")
    ps.add_argument("corpus")
    ps.add_argument("--out", default="sft.jsonl")
    ps.add_argument("--agent", default=None)
    ps.add_argument("--min-reward", type=float, default=None, dest="min_reward")
    ps.set_defaults(func=cmd_sft)
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if not args.cmd:
        return interactive(args)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
