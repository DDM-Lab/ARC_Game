#!/usr/bin/env python3
"""One-shot migration: replace raw llm_provider/llm_endpoint/api_key_env in config/*.json with a
single `provider` enum (provider_registry). Dry-run by default — prints a unified diff per file and
writes NOTHING until `--apply`.

Mapping is derived from PROVIDER_REGISTRY, so it can never disagree with the runtime resolution.
Any (provider, endpoint, key_env) combo that doesn't map to a registered provider is REPORTED and
left untouched — nothing is silently mis-migrated.

Usage:
  python migrate_configs_to_provider.py            # dry-run: show diffs
  python migrate_configs_to_provider.py --apply     # write the changes
"""
from __future__ import annotations

import argparse
import difflib
import json
import re
import sys
from pathlib import Path

from provider_registry import PROVIDER_REGISTRY

# Default key env per backend, matching the code's os.environ.get fallbacks, so a config that omits
# api_key_env still maps to the right provider.
_DEFAULT_KEY_ENV = {"anthropic": "ANTHROPIC_API_KEY", "openai": "OPENAI_API_KEY", "ollama": None}

# Reverse lookup: (backend, base_url, key_env) -> provider enum name.
_REVERSE = {(spec.backend, spec.base_url, spec.key_env): p.value
            for p, spec in PROVIDER_REGISTRY.items()}


def _provider_for(agent: dict) -> str | None:
    prov = (agent.get("llm_provider") or "").lower()
    if not prov:
        return None
    if prov == "ollama":                     # ollama is unique by backend regardless of url/key
        return "ollama-local"
    endpoint = agent.get("llm_endpoint")     # None means provider default base_url
    key_env = agent.get("api_key_env") or _DEFAULT_KEY_ENV.get(prov)
    return _REVERSE.get((prov, endpoint, key_env))


# Line-surgery patterns: only quoted-value fields (the director's `"llm_provider": null` is left
# alone). Groups: (indent)(trailing comma+whitespace).
_LLM_PROV_RE = re.compile(r'^(\s*)"llm_provider":\s*"[^"]+"(\s*,?\s*)$')
_DROP_RE = re.compile(r'^\s*"(?:llm_endpoint|api_key_env)":\s*"[^"]*"\s*,?\s*$')


def _json_migrate(data: dict) -> tuple[dict, list[str]]:
    """Reference migration on the parsed dict (used only as a correctness oracle for the surgical
    text edit below)."""
    reasons: list[str] = []
    agents = []
    for a in data["agents"]:
        if not a.get("llm_provider"):
            agents.append(a)
            continue
        enum = _provider_for(a)
        if enum is None:
            reasons.append(f"{a.get('subagent_name','?')}: unmapped combo "
                           f"provider={a.get('llm_provider')!r} endpoint={a.get('llm_endpoint')!r} "
                           f"key_env={a.get('api_key_env')!r}")
            agents.append(a)
            continue
        na = {("provider" if k == "llm_provider" else k): (enum if k == "llm_provider" else v)
              for k, v in a.items() if k not in ("llm_endpoint", "api_key_env")}
        agents.append(na)
    return {**data, "agents": agents}, reasons


def migrate_file(path: Path) -> tuple[str | None, list[str]]:
    """Surgical, minimal-diff migration: rewrite ONLY the provider field lines, leaving all other
    bytes (indentation, compaction, trailing newline) untouched. Returns (new_text|None, reasons).

    Safety: the surgically-edited text is re-parsed and compared to the reference dict migration;
    a mismatch raises rather than emitting a subtly-wrong file.
    """
    raw = path.read_text()
    data = json.loads(raw)
    if not isinstance(data, dict) or "agents" not in data:
        return None, []

    expected, reasons = _json_migrate(data)
    enums = [_provider_for(a) for a in data["agents"] if a.get("llm_provider")]

    out: list[str] = []
    i = 0
    changed = False
    for line in raw.splitlines(keepends=True):
        m = _LLM_PROV_RE.match(line)
        if m:
            enum = enums[i] if i < len(enums) else None
            i += 1
            if enum is None:
                out.append(line)               # unmatched combo: leave as-is
            else:
                out.append(f'{m.group(1)}"provider": "{enum}"{m.group(2)}')
                changed = True
            continue
        if _DROP_RE.match(line):
            changed = True
            continue
        out.append(line)

    if not changed:
        return None, reasons
    new_text = "".join(out)
    if json.loads(new_text) != expected:                 # correctness oracle
        raise RuntimeError(f"surgical migration of {path} diverged from reference; refusing")
    return new_text, reasons


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="write changes (default: dry-run diff)")
    ap.add_argument("--dir", default="config", help="config directory")
    args = ap.parse_args(argv)

    files = sorted(p for p in Path(args.dir).glob("*.json") if not p.name.startswith("keys"))
    total_changed = 0
    total_unmatched = 0
    for path in files:
        new_text, reasons = migrate_file(path)
        for r in reasons:
            print(f"  ⚠️  UNMATCHED {path.name}: {r}", file=sys.stderr)
            total_unmatched += 1
        if new_text is None:
            continue
        total_changed += 1
        if args.apply:
            path.write_text(new_text)
            print(f"WROTE  {path}")
        else:
            old = path.read_text().splitlines(keepends=True)
            diff = difflib.unified_diff(old, new_text.splitlines(keepends=True),
                                        fromfile=str(path), tofile=str(path) + " (migrated)")
            sys.stdout.writelines(diff)
    mode = "APPLIED" if args.apply else "DRY-RUN (no files written; use --apply)"
    print(f"\n{mode}: {total_changed} file(s) to change, {total_unmatched} unmatched agent(s).")
    return 1 if total_unmatched else 0


if __name__ == "__main__":
    raise SystemExit(main())
