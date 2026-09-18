"""A basic Bayesian preference model that learns the HUMAN director's choices and lets the agents
adapt to them.

The `on_choice_resolved` hook differentiates human vs AI via `event["is_human"]`:
  * human picks   -> update the shared human-preference model in ctx.persist ("pref/human/<pid>")
  * officer picks -> recorded separately per officer ("pref/officer/<name>"), not used for proposals

`preferred_choices` (callable by any agent) reads the HUMAN model, so every agent proposes options
weighted by the director's own revealed preferences. Dirichlet-categorical, Dirichlet(1) prior.
Durable (SQLite) so it persists across sessions/restarts.
"""
from cora_ext import register_tool, register_hook, ToolResult


def _human_key(ctx):
    return f"pref/human/{ctx.participant_id or 'anon'}"


def _officer_key(actor):
    return f"pref/officer/{actor}"


def _posterior(model):
    counts, total = model["counts"], model["total"]
    slots = sorted(int(k) for k in counts) or [0, 1]
    K = len(slots)
    return {k: (counts.get(str(k), 0) + 1) / (total + K) for k in slots}


def _bump(store, key, choice):
    m = store.get(key, {"counts": {}, "total": 0})
    m["counts"][str(choice)] = m["counts"].get(str(choice), 0) + 1
    m["total"] += 1
    store.set(key, m)
    return m


@register_hook("on_choice_resolved")
def update_preference(ctx, event):
    choice = event.get("choice")
    if choice is None:
        return
    if event.get("is_human"):
        m = _bump(ctx.persist, _human_key(ctx), choice)      # learn the director's preference
        ctx.log("human_preference", {"participant": ctx.participant_id, "n": m["total"],
                                     "posterior": {str(k): round(v, 3) for k, v in _posterior(m).items()}})
    else:
        # officer's own choice — recorded separately, not used to steer proposals
        _bump(ctx.persist, _officer_key(event.get("actor", "?")), choice)


_SCHEMA = {
    "type": "function",
    "function": {
        "name": "preferred_choices",
        "description": "Propose options weighted by the director's learned choice preferences.",
        "parameters": {"type": "object", "properties": {}},
    },
}


@register_tool("preferred_choices", _SCHEMA, acting=True)
async def preferred_choices(ctx, args):
    model = ctx.persist.get(_human_key(ctx), {"counts": {}, "total": 0})
    if model["total"] == 0:
        return ToolResult("No director-preference data yet.")
    post = _posterior(model)
    ranked = sorted(post, key=post.get, reverse=True)
    pkgs = [{"label": f"Option like choice {k} (director preference {post[k]:.0%}, n={model['total']})"}
            for k in ranked]
    return await ctx.propose_choices(pkgs)


def check_fixtures():
    return {"state": {}, "tool_args": {"preferred_choices": {}},
            "events": [{"event": "on_choice_resolved", "obj": {"is_human": True, "choice": 1}},
                       {"event": "on_choice_resolved", "obj": {"is_human": False, "actor": "Food Officer", "choice": 0}}]}
