#!/usr/bin/env python3
"""cora-plugin — local dev tooling for CORA tool/hook plugins (Phase 2).

`check` validates a plugin OFFLINE against MockToolContext before it ever reaches a router:
imports it, confirms it registers well-formed tools/hooks, and smoke-runs each against a fixture
(the plugin may expose `check_fixtures()` for representative state/args/events), with a per-call
time budget. Catches the everyday bugs (import errors, missing deps, bad schema, exceptions,
infinite loops) in seconds with no Unity/LLM/network. See docs/phase2-plugin-spec.md.

Usage:
  python cora_plugin.py check plugins/example_tools.py
"""
from __future__ import annotations

import argparse
import asyncio
import importlib.util
import sys
from pathlib import Path

import cora_ext
from cora_ext import MockToolContext, run_tool, run_hooks


def _load_module(path: str):
    spec = importlib.util.spec_from_file_location(f"cora_check_{Path(path).stem}", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)  # side effect: register_* run
    return module


def cmd_check(args: argparse.Namespace) -> int:
    cora_ext.clear_registry()
    print(f"checking {args.plugin} ...")
    problems: list[str] = []

    # 1. import
    try:
        module = _load_module(args.plugin)
    except Exception as e:
        print(f"  FAIL  import: {type(e).__name__}: {e}")
        return 1
    print("  ok    import")

    # 2. registration
    tools = cora_ext.all_tools()
    hook_counts = {e: len(cora_ext.get_hooks(e)) for e in cora_ext.HOOK_EVENTS}
    n_hooks = sum(hook_counts.values())
    if not tools and not n_hooks:
        print("  FAIL  registered no tools or hooks")
        return 1
    print(f"  ok    registered {len(tools)} tool(s) {list(tools)}, "
          f"{n_hooks} hook(s) { {k:v for k,v in hook_counts.items() if v} }")

    # 3. schema well-formedness
    for name, spec in tools.items():
        sname = (spec.schema.get("function") or {}).get("name") or spec.schema.get("name")
        if not sname:
            problems.append(f"tool {name!r}: schema has no resolvable name")
        elif sname != name:
            print(f"  warn  tool {name!r}: schema name {sname!r} != registered name")

    # 4. smoke run against fixtures (or empty args)
    fx = getattr(module, "check_fixtures", None)
    state, tool_args, events = {}, {}, []
    if callable(fx):
        try:
            f = fx()
            state, tool_args, events = f.get("state", {}), f.get("tool_args", {}), f.get("events", [])
        except Exception as e:
            problems.append(f"check_fixtures() raised: {type(e).__name__}: {e}")

    async def _smoke():
        for name, spec in tools.items():
            ctx = MockToolContext(state=state)
            res = await run_tool(spec, ctx, tool_args.get(name, {}), timeout=args.timeout)
            errored = res.text.startswith("ERROR")
            print(f"  {'ERROR' if errored else 'ok   '} tool {name}() -> {res.text[:90]!r}"
                  + (f"  (emitted={ctx.emitted} proposed={len(ctx.proposed)})" if (ctx.emitted or ctx.proposed) else ""))
            if errored and callable(fx):
                problems.append(f"tool {name!r} errored on its fixture: {res.text}")
        for ev in events:
            ctx = MockToolContext(state=state)
            await run_hooks(ev.get("event"), ctx, ev.get("obj", {}))
            print(f"  ok    hook {ev.get('event')} fired (logs={len(ctx.logs)}, "
                  f"session_store={ctx.session_store})")

    asyncio.run(_smoke())

    if problems:
        print("\nFAILED:")
        for p in problems:
            print("  -", p)
        return 1
    print("\nPASSED")
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="cora-plugin", description="CORA plugin dev tooling")
    sub = ap.add_subparsers(dest="cmd", required=True)
    pc = sub.add_parser("check", help="validate a plugin offline against MockToolContext")
    pc.add_argument("plugin")
    pc.add_argument("--timeout", type=float, default=5.0, help="per-call time budget (s)")
    pc.set_defaults(func=cmd_check)
    args = ap.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
