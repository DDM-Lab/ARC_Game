#!/usr/bin/env python3
"""cora-bundle — contributor CLI for CORA experiment bundles.

Front-end #1 over the shared validation core (cora_schema + bundle.py). The live upload endpoint
and the eventual dashboard reuse the SAME core, so nothing here is throwaway.

Subcommands:
  new       scaffold a bundle skeleton (full or --delta) you can edit
  validate  validate a bundle (and, for a delta, its --base), printing per-field errors
  render    compose + emit the runtime config a full/delta bundle resolves to

`run` (validate → launch a benchmark with the bundle) is intentionally NOT here yet: it depends on
the provider-enum migration that teaches the router/benchmark to consume a bundle. It lands with
that wiring (docs/contributor-platform-design.md, Phase 1). Until then, `render` produces the exact
composed config that path will consume.

Usage:
  python cora_bundle.py new cmu-lab/food-terse --author "Morgan" [--delta]
  python cora_bundle.py validate bundles/cmu-lab/food-terse.json [--base config/continuous_all_officers_ddmlab.json]
  python cora_bundle.py render   bundles/cmu-lab/food-terse.json --base config/... [-o out.json]
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from bundle import BundleError, config_warnings, load_bundle
from cora_schema import CORA_API_VERSION, Bundle
from provider_registry import valid_names


def _full_template(name: str, author: str) -> dict:
    return {
        "manifest": {
            "name": name, "author": author, "version": "0.1.0",
            "cora_api_version": CORA_API_VERSION,
            "description": "TODO: one-line description",
        },
        # Defaults chosen so the UNEDITED scaffold actually plays. The previous skeleton was
        # schema-valid but produced an officer that was invisible (no talkinghead_endpoint =>
        # no UI slot), inert (task_choice-only => "no in-scope actions — skipping" at round 0,
        # before any task exists), and silent (reactive => says nothing until spoken to). A
        # first-timer saw "OK" from validate and a dead officer in game.
        "config": {
            "agent_order_rule": "sequential",
            "agents": [
                {"subagent_name": "Director", "role": "director", "actor_type": "manual"},
                {
                    "subagent_name": "Food Officer", "role": "subagent", "actor_type": "continuous",
                    "provider": "anthropic-ddmlab",      # one of: %s
                    "llm_model": "claude-sonnet-5",
                    # Which of the 5 fixed GUI slots this officer speaks into. Without it the
                    # officer runs but its messages can never render.
                    "talkinghead_endpoint": "FoodMassCare",
                    # Keep at least one always-available category (construction) alongside
                    # task_choice, or the officer is skipped in any round with no food task.
                    "subaction_space": [
                        {"category": "task_choice", "group": "food"},
                        {"category": "construction", "building_types": ["kitchen"]},
                        {"category": "resource_transfer"},
                    ],
                    "subobservation_space": ["sessionInfo", "satisfactionAndBudget",
                                             "tasks:food", "logistics"],
                    "system_prompt": "You are the Food Officer. TODO: write the persona.",
                    # emergent = briefs the director from round 1, so you can see it working.
                    # Switch to "reactive" once you want it to speak only when addressed.
                    "opening_mode": "emergent",
                },
            ],
        },
    }


def _delta_template(name: str, author: str) -> dict:
    return {
        "manifest": {
            "name": name, "author": author, "version": "0.1.0",
            "cora_api_version": CORA_API_VERSION,
            "description": "TODO: what this delta changes vs the base",
        },
        "delta": {
            "agents": [
                {"subagent_name": "Food Officer",
                 "system_prompt": "TERSE food officer. TODO.",
                 "provider": "anthropic-ddmlab"},
            ],
        },
    }


def cmd_new(args: argparse.Namespace) -> int:
    tmpl = (_delta_template if args.delta else _full_template)(args.name, args.author)
    if args.provider:
        key = "delta" if args.delta else "config"
        tmpl[key]["agents"][-1]["provider"] = args.provider
    owner, slug = args.name.split("/", 1)
    out = Path(args.out) if args.out else Path("bundles") / owner / f"{slug}.json"
    if out.exists() and not args.force:
        print(f"refusing to overwrite {out} (use --force)", file=sys.stderr)
        return 1
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(tmpl, indent=2) + "\n")
    print(f"wrote {'delta' if args.delta else 'full'} bundle skeleton -> {out}")
    print(f"providers available: {', '.join(valid_names())}")
    return 0


def cmd_validate(args: argparse.Namespace) -> int:
    # Structural validation first (clear errors even for a delta with no base).
    try:
        Bundle.model_validate(json.loads(Path(args.bundle).read_text()))
    except Exception as e:
        print(f"INVALID  {args.bundle}\n{e}", file=sys.stderr)
        return 1
    try:
        cfg = load_bundle(args.bundle, base_config=args.base)
    except BundleError as e:
        print(f"INVALID  {args.bundle}\n{e}", file=sys.stderr)
        return 1
    n = len(cfg["agents"])
    llm = [a["subagent_name"] for a in cfg["agents"] if a.get("provider")]
    print(f"OK  {args.bundle}  ->  {n} agents ({len(llm)} LLM-driven)")
    # Schema-valid is not the same as playable. The same authoring checks the upload endpoint
    # runs go here too, so a contributor sees "your officer has no UI slot / can never act"
    # BEFORE uploading rather than after a confusing playtest. Warnings never fail the command.
    warns = config_warnings(cfg)
    for w in warns:
        print(f"  ⚠️  {w}", file=sys.stderr)
    if warns:
        print(f"  ({len(warns)} warning(s) — valid, but check these before you upload)",
              file=sys.stderr)
    return 0


def cmd_render(args: argparse.Namespace) -> int:
    try:
        cfg = load_bundle(args.bundle, base_config=args.base)
    except BundleError as e:
        print(f"INVALID  {args.bundle}\n{e}", file=sys.stderr)
        return 1
    text = json.dumps(cfg, indent=2) + "\n"
    if args.out:
        Path(args.out).write_text(text)
        print(f"wrote composed config -> {args.out}")
    else:
        sys.stdout.write(text)
    return 0


def cmd_push(args: argparse.Namespace) -> int:
    """POST a bundle to a running router's /bundles endpoint."""
    import urllib.error
    import urllib.request

    # Validate locally first so we fail fast with a good message.
    try:
        load_bundle(args.bundle, base_config=args.base)
    except BundleError as e:
        print(f"INVALID  {args.bundle}\n{e}", file=sys.stderr)
        return 1

    body = Path(args.bundle).read_bytes()
    url = args.url.rstrip("/") + "/bundles"
    if args.base:
        # a delta bundle: tell the server which config to layer onto
        import json as _json
        base_name = Path(args.base).stem
        url += "?base=" + urllib.request.quote(base_name)
    req = urllib.request.Request(url, data=body, method="POST",
                                 headers={"Authorization": f"Bearer {args.key}",
                                          "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req) as resp:
            body = resp.read().decode()
            # Surface server-side authoring warnings prominently. They are the difference
            # between "uploaded fine" and "uploaded fine but your officer will look inert" —
            # the failure mode is otherwise invisible until you play the config.
            # (Use the module-level `json`: the `_json` alias is imported only inside the
            #  delta branch above, so referencing it here NameErrors on a normal push.)
            try:
                data = json.loads(body)
            except json.JSONDecodeError:
                print(body)
            else:
                warns = data.pop("warnings", None) or []
                print(json.dumps(data))
                for w in warns:
                    print(f"  ⚠️  {w}", file=sys.stderr)
        return 0
    except urllib.error.HTTPError as e:
        print(f"HTTP {e.code}: {e.read().decode()}", file=sys.stderr)
        return 1
    except urllib.error.URLError as e:
        print(f"connection failed: {e}", file=sys.stderr)
        return 1


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="cora-bundle", description="CORA experiment-bundle CLI")
    sub = p.add_subparsers(dest="cmd", required=True)

    pn = sub.add_parser("new", help="scaffold a bundle skeleton")
    pn.add_argument("name", help="namespaced bundle name: owner/slug")
    pn.add_argument("--author", default="", help="author name")
    pn.add_argument("--delta", action="store_true", help="scaffold an override bundle (needs a base)")
    pn.add_argument("--out", help="output path (default bundles/<owner>/<slug>.json)")
    pn.add_argument("--provider", choices=valid_names(),
                    help="LLM provider for the scaffolded officer (default: anthropic-ddmlab)")
    pn.add_argument("--force", action="store_true", help="overwrite an existing file")
    pn.set_defaults(func=cmd_new)

    pv = sub.add_parser("validate", help="validate a bundle")
    pv.add_argument("bundle")
    pv.add_argument("--base", help="base config path (required for a delta bundle)")
    pv.set_defaults(func=cmd_validate)

    pr = sub.add_parser("render", help="emit the composed runtime config")
    pr.add_argument("bundle")
    pr.add_argument("--base", help="base config path (required for a delta bundle)")
    pr.add_argument("-o", "--out", help="write to a file instead of stdout")
    pr.set_defaults(func=cmd_render)

    pp = sub.add_parser("push", help="upload a bundle to a running router")
    pp.add_argument("bundle")
    pp.add_argument("--url", default="http://localhost:9876", help="router base URL")
    pp.add_argument("--key", default="dev-local-key", help="API key (Bearer)")
    pp.add_argument("--base", help="base config path (required for a delta bundle)")
    pp.set_defaults(func=cmd_push)
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
