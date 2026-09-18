"""Example CORA plugin (Phase 2 reference). Drop-in under plugins/; loaded at router startup.

Demonstrates the full pattern with NO heavy deps:
  - a read-only aggregation TOOL (`unmet_needs`) that reads game facts via ctx,
  - an observer HOOK (`on_choice_resolved`) that accumulates director preference signal in the
    shared session_store and writes a custom log entry,
  - an acting TOOL (`preference_choices`) that pulls fresh state and proposes via ctx.

A tool/hook reaches the game ONLY through `ctx` — it never imports agent_router. Swap the toy
counting model for a real Bayesian model (numpy/pymc) to get the elicitation use case.
"""
from cora_ext import register_tool, register_hook, ToolResult


def _facilities(state: dict) -> list:
    ms = state.get("mapState") or {}
    return ms.get("facilities") or state.get("facilities") or []


def _shortfall(f: dict) -> int:
    return int(f.get("needed", 0) or 0) - int(f.get("have", 0) or 0)


_UNMET_SCHEMA = {
    "type": "function",
    "function": {
        "name": "unmet_needs",
        "description": "Summarize the facilities with the largest unmet needs, ranked worst-first.",
        "parameters": {
            "type": "object",
            "properties": {"top": {"type": "integer", "description": "how many to list (default 3)"}},
        },
    },
}


@register_tool("unmet_needs", _UNMET_SCHEMA)
def unmet_needs(ctx, args):
    top = int(args.get("top") or 3)
    ranked = sorted((f for f in _facilities(ctx.state) if _shortfall(f) > 0),
                    key=_shortfall, reverse=True)[:top]
    if not ranked:
        return ToolResult("No facilities have unmet needs right now.")
    lines = [f"- {f.get('name', '?')}: short {_shortfall(f)}" for f in ranked]
    return ToolResult("Top unmet needs:\n" + "\n".join(lines))


@register_hook("on_choice_resolved")
def observe_choice(ctx, event):
    # Accumulate in the SHARED session store (one human per session, regardless of which officer
    # surfaced the choice). Swap this counter for a real posterior update.
    model = ctx.session_store.setdefault("pref_model", {"count": 0})
    model["count"] += 1
    ctx.log("preference_update", {"count": model["count"], "choice": event.get("choiceId")})


_PREF_SCHEMA = {
    "type": "function",
    "function": {
        "name": "preference_choices",
        "description": "Propose choice packages weighted by the director's observed preferences.",
        "parameters": {"type": "object", "properties": {"k": {"type": "integer"}}},
    },
}


@register_tool("preference_choices", _PREF_SCHEMA, acting=True)
async def preference_choices(ctx, args):
    await ctx.refresh_state()
    n = ctx.session_store.get("pref_model", {}).get("count", 0)
    packages = [{"label": f"Preference-weighted option (from {n} observed choices)"}]
    return await ctx.propose_choices(packages)


def check_fixtures():
    """Representative inputs for `cora-plugin check`."""
    return {
        "state": {"mapState": {"facilities": [
            {"name": "Kitchen Alpha", "needed": 10, "have": 3},
            {"name": "Shelter Bravo", "needed": 5, "have": 5},
            {"name": "Motel", "needed": 8, "have": 1},
        ]}},
        "tool_args": {"unmet_needs": {"top": 2}, "preference_choices": {"k": 2}},
        "events": [{"event": "on_choice_resolved", "obj": {"choiceId": 1}},
                   {"event": "on_choice_resolved", "obj": {"choiceId": 2}}],
    }
