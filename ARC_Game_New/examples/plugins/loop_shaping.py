"""Loop-shaping hooks — customize an officer's agentic loop WITHOUT replacing it.

The harness keeps the parts that must stay invariant (tool-protocol pairing, action
execution through the shared command contract, the reply guarantee, turn logging), so any
loop variant still produces identical action semantics and comparable training records.
What you control here is the *thinking*: what context the officer starts with, and when it
stops.

Two hooks, both optional:

  on_turn_start(ctx, ev) -> str | [{"role": "user"|"system", "content": str}] | None
      Extra context injected after the grounding message, before the first model step.
      Use for: a ReAct scratchpad, retrieved notes, a self-critique preamble, an
      experimental instruction, few-shot exemplars.
      (assistant/tool roles are refused — they would corrupt tool-call pairing.)

  on_step_end(ctx, ev) -> "stop" | True | None
      Called after each step's tool calls are dispatched. Return "stop" to end the turn.
      Use for: custom stopping rules, step budgets, confidence gates, "one action per turn"
      experimental conditions.

`ev` for on_turn_start: agent, round, max_steps, brief_only, triggered_by_director,
                        actions_available
`ev` for on_step_end  : agent, step, max_steps, content, tool_names, executed_total, spoke

Copy this file, edit, and upload with:  POST /plugins?name=<slug>   (cap: upload_code)
Then activate it:                       POST /admin/plugins/reload  (admin plane)
"""
from cora_ext import register_hook


@register_hook("on_turn_start")
def scratchpad_preamble(ctx, ev):
    """Give the officer an explicit reasoning scaffold before it acts (ReAct-ish).

    Returning a string appends it as one user message. Return None to inject nothing —
    e.g. skip on brief-only turns, where the officer is only introducing itself.
    """
    if ev.get("brief_only"):
        return None
    return (
        "Before acting, think it through in one short line: "
        "(a) what changed since last turn, (b) the single biggest need in your remit, "
        "(c) the one action that addresses it. Then act."
    )


@register_hook("on_step_end")
def stop_after_first_action(ctx, ev):
    """Experimental condition: at most ONE executed action per turn.

    Useful as a control arm — it isolates 'many small commits' from 'one considered commit'
    without touching prompts or the tool palette. Returns "stop" once anything executed.
    """
    if ev.get("executed_total", 0) >= 1:
        ctx.log("loop_shaping_stop", {"agent": ev.get("agent"), "step": ev.get("step"),
                                      "reason": "one action per turn"})
        return "stop"
    return None
