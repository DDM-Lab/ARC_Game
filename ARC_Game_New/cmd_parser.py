"""Command-tag adapter for the officer router (temporary).

The router still carries officer actions as command tags (<build>Kitchen,3</build>): typed calls
are turned into tags (translate_tool_calls) and proposal packages store tag strings, and
parse_commands turns tags back into typed calls resolved by cora.executor, so resolution is
shared. This module is deleted when the router calls the executor directly
(docs/ARCHITECTURE.md, migration status). Nothing outside agent_router/continuous_agent uses it.

ParserEnv is the minimal env shape the resolver reads: get_valid_actions(), game_state, and
valid_actions (a private copy that resolved staff actions are appended to).
"""
from __future__ import annotations

import json
import re
from typing import Any, Iterable, Optional

from cora import executor
from cora.tools import TOOL_BY_NAME, param_names


class ParserEnv:
    """The round's action menu + game_state, in the shape parse_commands reads. valid_actions is
    a private copy: resolved staff actions are appended to it, and the caller runs the returned
    indices against this object's valid_actions, not its own list."""

    def __init__(self, valid_actions, game_state):
        self.valid_actions = list(valid_actions)
        self.game_state = game_state

    def get_valid_actions(self):
        return self.valid_actions


# ── typed call -> tag ──
# A tag body is comma-joined and the tag is angle-bracket delimited, neither escaped, so an
# argument containing one of these would change the call's arity (staff(site="Kitchen, 0") would
# read as site="Kitchen", count=0: a wrong action, not a no-op). Such calls are rejected rather
# than sanitized, since stripping the comma would match a different facility.
_TAG_UNSAFE = ",<>"


class TagArgError(ValueError):
    """A typed call's argument contains a tag delimiter (see _TAG_UNSAFE)."""


def tag_for(name: str, args: dict) -> Optional[str]:
    """One typed call -> its tag, params in schema order; None if the tool name is unknown.
    Raises TagArgError if an argument contains a delimiter."""
    tool = TOOL_BY_NAME.get(name)
    if tool is None:
        return None
    parts = []
    for p in param_names(tool):
        v = str(args.get(p, "")).strip()
        hit = [c for c in _TAG_UNSAFE if c in v]
        if hit:
            raise TagArgError(
                f"{name}.{p}={v!r} contains {' '.join(repr(c) for c in hit)}, which the "
                f"command grammar uses as a delimiter — re-issue with a plain value "
                f"(one facility/id, no commas or angle brackets)")
        parts.append(v)
    return f"<{name}>{','.join(parts)}</{name}>"


def translate_tool_calls(tool_calls: Iterable[Any]) -> tuple[str, dict]:
    """Typed calls ((name, args) tuples or {"name", "arguments"} dicts; arguments may be a JSON
    string) -> (space-joined tags, meta). meta counts {received, valid, unknown_name, bad_args}
    and `errors` carries a readable reason per rejected call, for the model."""
    meta = {"received": 0, "valid": 0, "unknown_name": 0, "bad_args": 0, "errors": []}
    tags = []
    for call in (tool_calls or []):
        meta["received"] += 1
        if isinstance(call, (tuple, list)) and len(call) == 2:
            name, raw_args = call
        elif isinstance(call, dict):
            name, raw_args = call.get("name"), call.get("arguments", call.get("args"))
        else:
            meta["bad_args"] += 1
            continue
        if name not in TOOL_BY_NAME:
            meta["unknown_name"] += 1
            meta["errors"].append(f"unknown tool {name!r}")
            continue
        try:
            args = json.loads(raw_args) if isinstance(raw_args, str) else dict(raw_args or {})
            if not isinstance(args, dict):
                raise ValueError("arguments must be an object")
            tags.append(tag_for(name, args))
        except TagArgError as e:
            meta["bad_args"] += 1
            meta["errors"].append(str(e))
            continue
        except (ValueError, TypeError) as e:
            meta["bad_args"] += 1
            meta["errors"].append(f"{name}: unreadable arguments ({e})")
            continue
        meta["valid"] += 1
    return " ".join(tags), meta


# ── tags -> resolved actions ──
_CMD_RE = re.compile(r"<\s*(build|hire|train|staff|task|deconstruct|transfer)\s*>(.*?)<\s*[\\/]\s*\1\s*>",
                     re.I | re.S)


def parse_commands(text, env):
    """Resolve the command tags in `text` against the round's menu with cora.executor.

    Each tag becomes the equivalent typed call (<staff>Shelter Alpha,</staff> ->
    staff(site="Shelter Alpha")). Synthesized staff actions are appended to env.valid_actions, so
    callers execute the returned indices against it.

    Returns {actions: [idx...], choices: [{taskId, choiceId}...], parsed: [...], errors: [...]}.
    """
    calls = []
    for m in _CMD_RE.finditer(text or ""):
        cmd, body = m.group(1).lower(), m.group(2).strip()
        names = param_names(TOOL_BY_NAME[cmd])
        parts = [p.strip() for p in body.replace("\n", " ").split(",")]
        calls.append((cmd, {n: parts[i] for i, n in enumerate(names) if i < len(parts) and parts[i] != ""}))
    results, tr = executor.plan_turn(calls, env)
    if hasattr(env, "valid_actions"):
        env.valid_actions = tr.actions
    actions, choices, parsed, errors = [], [], [], []
    for r in sorted(results, key=lambda r: executor.ORDER.get(r.tool, 99)):
        if r.status != "resolved":
            errors.append(f"{r.tool}: {r.reason}")
        elif r.choice is not None:
            choices.append(r.choice); parsed.append(r.summary)
        else:
            actions.extend(r.action_indices); parsed.append(r.summary)
    return {"actions": actions, "choices": choices, "parsed": parsed, "errors": errors}
