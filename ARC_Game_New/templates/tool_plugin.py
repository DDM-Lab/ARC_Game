"""Template CORA plugin. Copy to plugins/<yourlab>_tools.py, edit, then validate:
    python -m router.plugin_cli check plugins/<yourlab>_tools.py
A tool/hook reaches the game ONLY through the injected `ctx`. Full surface: docs/phase2-plugin-spec.md.
"""
from router.plugin_api import register_tool, register_hook, ToolResult

_MY_TOOL_SCHEMA = {
    "type": "function",
    "function": {
        "name": "my_tool",
        "description": "TODO: what the officer would call this for.",
        "parameters": {"type": "object", "properties": {}},
    },
}


@register_tool("my_tool", _MY_TOOL_SCHEMA)          # add acting=True if it calls execute/propose
def my_tool(ctx, args):
    # read facts:   ctx.state, ctx.get_facilities(), await ctx.refresh_state()
    # act (only!):  await ctx.execute([("hire", {"kind": "untrained", "count": 4})]) / ctx.propose_choices([...])
    return ToolResult("hello from my_tool")


@register_hook("on_choice_resolved")                # on_round_start | on_action_executed | on_session_end
def on_choice(ctx, event):
    # per-game state -> ctx.session_store ; durable per-participant state -> ctx.persist
    ctx.log("my_event", {"choice": event.get("choiceId")})


def check_fixtures():
    """Sample inputs used by `python -m router.plugin_cli check`."""
    return {
        "state": {"mapState": {"facilities": []}},
        "tool_args": {"my_tool": {}},
        "events": [{"event": "on_choice_resolved", "obj": {"choiceId": 1}}],
    }
