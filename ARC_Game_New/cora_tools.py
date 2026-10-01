"""CORA canonical tool schema — the single source of truth for the typed action tools.

Phase B of the three-wing unification. One place defines the game's action tools
(build/hire/train/staff/deconstruct/task/transfer) as TYPED function tools; everything
else is generated from it:

  * `anthropic_tools()` / `openai_tools()` — the officer's (live) tool schema, in either
    provider's function-tool shape.
  * `arc_tools_yaml()` — the Verlog RL `arc_tools.yaml` (sglang tool_config), so the RL
    policy trains on the IDENTICAL tool surface the live officer offers.
  * `translate_tool_calls()` — typed calls -> cmd-tag text. Only the officer router still uses
    it, until it moves to tool_executor (which runs typed calls directly); then it is deleted.

Layering (see docs/three-wing-unification-plan.md):
    tool_calls ──translate_tool_calls──▶ cmd-tags ──cmd_parser──▶ resolved stream ──▶ execute_resolved

A tool's ordered `params` map 1:1 to its cmd-tag body (comma-joined), so `build(type, site_id)`
becomes `<build>{type},{site_id}</build>`. Descriptions/enums mirror the RL schema so the two
wings are byte-comparable. This module holds NO execution logic — it only defines + translates.
"""
from __future__ import annotations

import json as _json
from typing import Any, Iterable, Optional


def _task_id_desc() -> str:
    """Task-id description matching the live rendering (mirrors cora_prompts._task_id_line)."""
    import os
    if os.environ.get("ARC_STABLE_TASK_TOKENS", "1").strip() == "1":
        return ("The task's id string, copied exactly as shown in this turn's observation (e.g. "
                "BUDGET_DAILY). Must match a task actually offered this turn — ids not listed "
                "this turn are dropped.")
    return ("The task's integer id, copied exactly as shown in this turn's observation. Unity "
            "reassigns these every turn, so read it from the CURRENT observation rather than "
            "reusing one from an earlier turn. Ids not listed this turn are dropped.")


# ── Canonical typed tool definitions ────────────────────────────────────────
# Each tool: name (== cmd-tag name), description, and ORDERED params. `required`
# defaults to all params. `manual_only` tools are omitted unless manual transfers
# are enabled (parity with the cmd grammar's <transfer> gating).
_TOOLS: list[dict] = [
    {
        "name": "build",
        "description": "Start construction of a new building at an available site.",
        "params": [
            ("type", {"type": "string", "enum": ["kitchen", "shelter", "casework"],
                      "description": "The building type to construct."}),
            ("site_id", {"type": "integer",
                         "description": "An integer site id from `available.buildSites` this turn."}),
        ],
    },
    {
        "name": "hire",
        # Pure-rules policy (see minimal_v6 block-comment in cora_prompts.py):
        # description states what the action does, not tradeoffs. "cost more" and
        # "cheap/expensive" are biasing framings and are omitted — the price and
        # workforce-unit numbers already appear in the kind param description and
        # in the WORKFORCE section of the system prompt, so redundant + biased
        # phrasing is strictly worse than a neutral rule.
        "description": "Hire N workers of the given kind.",
        "params": [
            ("kind", {"type": "string", "enum": ["untrained", "trained"],
                      "description": ("untrained: 1 workforce unit at `costs.hireUntrained`. "
                                      "trained: 2 workforce units at `costs.hireTrained`. "
                                      "Both prices print under `costs:` each turn.")}),
            ("count", {"type": "integer", "description": "Number of workers to hire, 1-127."}),
        ],
    },
    {
        "name": "train",
        "description": "Train N of your existing untrained workers into trained workers.",
        "params": [
            ("count", {"type": "integer", "description": "Number of untrained workers to promote to trained, 1-127."}),
        ],
    },
    {
        "name": "staff",
        # Measured failure (Talos session 1bb785d1): officers read `count` as a number of
        # WORKERS, sent count=2 meaning two trained workers (= 4 units, exactly the need), got it
        # converted to one worker and rejected, concluded "partial staffing isn't allowed", and
        # left three buildings unstaffed with the workers free. So `count` is now optional and
        # omitting it staffs the building fully, which is the only staffing the game accepts.
        "description": ("Staff an already-built facility from the free pool. A building only runs when "
                        "FULLY staffed, so normally omit count and it is staffed with exactly the "
                        "workforce it needs (trained workers count 2 units, untrained 1). A worker hired "
                        "this same turn IS available to staff."),
        "params": [
            ("site", {"type": "string",
                      "description": ("The facility name, copied exactly as printed in `available.needStaff` this "
                                      "turn (names look like 'Shelter Alpha', 'Kitchen Bravo', 'Casework Charlie'). "
                                      "Matching is case-insensitive substring. A facility not listed in `needStaff` "
                                      "is either fully staffed or not yet built, and the call is dropped.")}),
            ("count", {"type": "integer",
                       "description": ("Optional, in workforce UNITS (not workers): trained = 2 units, untrained = 1. "
                                       "Leave it out to staff fully. A count below the building's need is refused, "
                                       "because partial staffing is not allowed.")}),
        ],
        "required": ["site"],
    },
    {
        "name": "deconstruct",
        "description": "Tear down an existing building. Frees the site and refunds nothing.",
        "params": [
            ("site", {"type": "string",
                      "description": ("The facility name, copied exactly as printed in this turn's facilities "
                                      "list (e.g. 'Shelter Alpha'). Matching is case-insensitive substring.")}),
        ],
    },
    {
        "name": "task",
        "description": ("Respond to an active task by selecting one of its offered choices. Tasks and their "
                        "choices are enumerated at the top of each observation."),
        "params": [
            # The id FORM must track what obs_encoder actually renders (ARC_STABLE_TASK_TOKENS), exactly
            # as the system prompt's task line does. This description used to name three stable tokens
            # while the benchmark ran with =0 and rendered integers -- and those three literals came
            # back as the three most-rejected ids in the n=32 cell (RELOC_C02 299, FOOD_C01 287,
            # BUDGET_DAILY 279). A tool schema conditions the model harder than prose, so a stale
            # example here is worse than a stale sentence in the prompt.
            ("task_id", {"type": "string", "description": _task_id_desc()}),
            # NOT 0-based and NOT contiguous: choice ids are the ids Unity prints for that task this
            # turn, and infeasible options are dropped from the list rather than renumbered, so a task
            # routinely offers e.g. {1,3}. The old "0-based" claim contradicted the system prompt.
            ("choice_id", {"type": "integer",
                           "description": ("The integer choice_id printed for that choice in this turn's task list. "
                                           "Ids are not 0-based and not contiguous — a task may offer only {1,3}. "
                                           "Use exactly the ids shown for that task this turn.")}),
        ],
    },
    {
        "name": "transfer",
        "description": ("Move a quantity of a resource from one facility to another using a free vehicle. "
                        "Only available when manual_transfers is enabled."),
        "manual_only": True,
        "params": [
            ("resource", {"type": "string", "enum": ["food", "people"],
                          "description": "Which resource to move."}),
            ("source", {"type": "string", "description": "Source facility name (substring match)."}),
            ("dest", {"type": "string", "description": "Destination facility name (substring match)."}),
            ("qty", {"type": "integer", "description": "Quantity to move; snapped to the nearest offered amount."}),
        ],
    },
]

_TOOL_BY_NAME: dict[str, dict] = {t["name"]: t for t in _TOOLS}


def tools(manual_transfers: bool = False) -> list[dict]:
    """The canonical tool defs active for this mode (drops manual-only tools unless enabled)."""
    return [t for t in _TOOLS if manual_transfers or not t.get("manual_only")]


def _param_names(tool: dict) -> list[str]:
    return [name for name, _ in tool["params"]]


def _json_schema(tool: dict) -> dict:
    """JSON-Schema object for a tool's arguments (shared by every provider shape)."""
    props = {name: dict(spec) for name, spec in tool["params"]}
    return {"type": "object", "properties": props,
            "required": list(tool.get("required") or _param_names(tool))}


# ── Generators: one schema → each wing's shape ──────────────────────────────
def anthropic_tools(manual_transfers: bool = False) -> list[dict]:
    """Officer/live tool schema in Anthropic shape: {name, description, input_schema}."""
    return [
        {"name": t["name"], "description": t["description"], "input_schema": _json_schema(t)}
        for t in tools(manual_transfers)
    ]


def openai_tools(manual_transfers: bool = False) -> list[dict]:
    """Officer/live tool schema in OpenAI shape: {type: function, function: {...}}."""
    return [
        {"type": "function",
         "function": {"name": t["name"], "description": t["description"], "parameters": _json_schema(t)}}
        for t in tools(manual_transfers)
    ]


def arc_tools_yaml(manual_transfers: bool = False,
                   class_name: str = "verl.tools.arc_env_tool.ArcEnvTool") -> str:
    """Generate Verlog's arc_tools.yaml (sglang tool_config) from the canonical schema.

    Emitted as a build artifact — the Verlog fork consumes this file at rollout so the RL
    policy is shown the SAME tools the live officer offers. Never hand-edit the yaml.
    """
    import yaml  # local import: only needed when generating the RL artifact
    entries = []
    for t in tools(manual_transfers):
        entries.append({
            "class_name": class_name,
            "config": {"type": "native"},
            "tool_schema": {
                "type": "function",
                "function": {"name": t["name"], "description": t["description"],
                             "parameters": _json_schema(t)},
            },
        })
    return yaml.safe_dump({"tools": entries}, sort_keys=False, width=100)


# ── Translator: typed tool_calls → cmd-tags (the shared front-end→resolver bridge) ──
# Characters that CANNOT appear inside an argument value: the tag body is comma-joined and
# the tag itself is angle-bracket delimited, and neither is escaped. A value carrying one of
# these silently changes the call's arity — `staff(site="Kitchen, 0", count=2)` renders
# `<staff>Kitchen, 0,2</staff>`, which cmd_parser's `split(body, n)` accepts (it takes
# `len(parts) >= n`) and reads as site="Kitchen", count=0. That is a WRONG action, not a
# no-op, and it was previously reported as {valid: 1, bad_args: 0} — silently corrupt in the
# very corpus we train on. Five params are free-text (staff.site, deconstruct.site,
# transfer.source/dest, task.task_id), so a model can reach this with ordinary output.
# Reject rather than sanitize: stripping the comma would produce a DIFFERENT facility for
# substring matching, i.e. guessing at intent. Rejecting hands the model an honest error it
# can retry against.
_TAG_UNSAFE = ",<>"


class TagArgError(ValueError):
    """A typed tool call's argument value contains a cmd-tag delimiter (see _TAG_UNSAFE)."""


def tag_for(name: str, args: dict) -> Optional[str]:
    """One typed tool call → its cmd-tag string, or None if the tool name is unknown.
    Params are emitted in canonical order, comma-joined (the cmd-tag body grammar).

    Raises TagArgError if any argument value contains a delimiter, which would corrupt the
    tag's arity. `None` still means only "unknown tool name" so callers can tell the two apart.
    """
    tool = _TOOL_BY_NAME.get(name)
    if tool is None:
        return None
    parts: list[str] = []
    for p in _param_names(tool):
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
    """Turn a model's typed tool_calls into cmd-tag text for cmd_parser.parse_commands.

    Accepts either (name, args) tuples or {"name": ..., "arguments": ...} dicts; `arguments`
    may be a JSON string or a dict. Returns (tag_text, meta) where tag_text is the space-joined
    tags in call order and meta counts outcomes so callers can surface bad calls as
    reward-visible signal (NOT silently dropped, per the Phase B B3 fix):
        {received, valid, unknown_name, bad_args, errors}
    `errors` carries a human-readable reason per rejected call so the live path can hand the
    model something actionable instead of a bare "empty commands".
    """
    meta = {"received": 0, "valid": 0, "unknown_name": 0, "bad_args": 0, "errors": []}
    tags: list[str] = []
    for call in (tool_calls or []):
        meta["received"] += 1
        if isinstance(call, (tuple, list)) and len(call) == 2:
            name, raw_args = call
        elif isinstance(call, dict):
            name, raw_args = call.get("name"), call.get("arguments", call.get("args"))
        else:
            meta["bad_args"] += 1
            continue
        if name not in _TOOL_BY_NAME:
            meta["unknown_name"] += 1
            meta["errors"].append(f"unknown tool {name!r}")
            continue
        try:
            args = _json.loads(raw_args) if isinstance(raw_args, str) else dict(raw_args or {})
            if not isinstance(args, dict):
                raise ValueError("arguments must be an object")
        except Exception as e:
            meta["bad_args"] += 1
            meta["errors"].append(f"{name}: unreadable arguments ({e})")
            continue
        try:
            tag = tag_for(name, args)
        except TagArgError as e:
            meta["bad_args"] += 1
            meta["errors"].append(str(e))
            continue
        if tag is None:
            meta["unknown_name"] += 1
            meta["errors"].append(f"unknown tool {name!r}")
            continue
        tags.append(tag)
        meta["valid"] += 1
    return " ".join(tags), meta
