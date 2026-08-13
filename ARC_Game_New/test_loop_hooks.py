"""Hermetic tests for the LOOP-SHAPING hooks (on_turn_start / on_step_end).

These are the extension points that let a contributor customize an officer's agentic loop
without replacing it: inject context before the first model step, and stop the turn early.
The harness keeps tool-protocol pairing, action execution, the reply guarantee and turn
logging, so every loop variant still yields identical action semantics.

Covered here:
  1. on_turn_start injects context into the officer's messages.
  2. on_turn_start refuses assistant/tool roles (they would break tool_call_id pairing).
  3. on_step_end returning "stop" ENDS the turn early — the officer's later steps never run.
  4. on_step_end returning None lets the loop continue normally (no behavior change).
  5. Zero registered hooks = zero behavior change (the built-in loop pays nothing).

Run: ./.venv/bin/python test_loop_hooks.py
"""
import asyncio
import os
import tempfile

import agent_router
import cora_ext
# Reuse the existing hermetic officer harness (no network, no Unity, no LLM).
from test_reactive_officers import make_session, food_officer, instrument, fake_enumerate


def _reset_hooks():
    cora_ext.clear_registry()


async def _run_turn(sess, agent, steps_script):
    """Drive one director-triggered turn with a scripted model. `steps_script` is a list of
    fake model responses, one per step; we record how many were actually consumed."""
    consumed = {"n": 0}

    def fake_run_tool_step(messages, tools, agent_cfg, tool_mode="auto"):
        i = consumed["n"]
        consumed["n"] += 1
        if i < len(steps_script):
            return steps_script[i]
        return {"content": "done", "tool_calls": []}

    agent_router.run_tool_step = fake_run_tool_step
    await sess._run_continuous_for_message(agent)
    return consumed["n"]


def _talk(step_id):
    return {"content": None,
            "tool_calls": [{"id": f"t{step_id}", "name": "talk_to_director",
                            "arguments": {"message": f"step {step_id}"}}]}


async def test_on_turn_start_injects():
    _reset_hooks()
    seen = {}

    @cora_ext.register_hook("on_turn_start")
    def inject(ctx, ev):
        seen["ev"] = ev
        return "INJECTED-SCRATCHPAD"

    td = tempfile.mkdtemp()
    cfg, sess = make_session(td, reactive=True)
    instrument(sess)
    agent_router._enumerate_actions = fake_enumerate

    await _run_turn(sess, food_officer(cfg), [_talk(0), {"content": "done", "tool_calls": []}])
    msgs = sess._continuous_transcripts.get("Food Officer") or []
    joined = " ".join(str(m.get("content")) for m in msgs)
    assert "INJECTED-SCRATCHPAD" in joined, "on_turn_start context was not injected"
    assert seen["ev"]["agent"] == "Food Officer", f"bad ev: {seen.get('ev')}"
    assert "triggered_by_director" in seen["ev"], "ev missing triggered_by_director"
    print("[1] on_turn_start injects context ok")


async def test_on_turn_start_refuses_unsafe_roles():
    _reset_hooks()

    @cora_ext.register_hook("on_turn_start")
    def inject_bad(ctx, ev):
        # assistant/tool roles would corrupt the strict tool_call_id pairing the providers
        # require — the harness must drop them.
        return [{"role": "assistant", "content": "SHOULD-BE-DROPPED"},
                {"role": "tool", "content": "ALSO-DROPPED"},
                {"role": "system", "content": "SYSTEM-OK"}]

    td = tempfile.mkdtemp()
    cfg, sess = make_session(td, reactive=True)
    instrument(sess)
    agent_router._enumerate_actions = fake_enumerate
    await _run_turn(sess, food_officer(cfg), [{"content": "done", "tool_calls": []}])
    msgs = sess._continuous_transcripts.get("Food Officer") or []
    joined = " ".join(str(m.get("content")) for m in msgs)
    assert "SHOULD-BE-DROPPED" not in joined, "assistant role was injected (breaks pairing)"
    assert "ALSO-DROPPED" not in joined, "tool role was injected (breaks pairing)"
    assert "SYSTEM-OK" in joined, "safe system role was not injected"
    print("[2] on_turn_start refuses assistant/tool roles ok")


async def test_on_step_end_stops_turn():
    """THE STOP PATH: a hook returning 'stop' must end the turn — later steps never run."""
    _reset_hooks()
    calls = {"n": 0}

    @cora_ext.register_hook("on_step_end")
    def stop_immediately(ctx, ev):
        calls["n"] += 1
        assert "step" in ev and "executed_total" in ev, f"bad ev: {ev}"
        return "stop"

    td = tempfile.mkdtemp()
    cfg, sess = make_session(td, reactive=True)
    instrument(sess)
    agent_router._enumerate_actions = fake_enumerate
    # Script THREE talking steps; the hook must halt after the first.
    consumed = await _run_turn(sess, food_officer(cfg), [_talk(0), _talk(1), _talk(2)])
    assert consumed == 1, f"loop did not stop: consumed {consumed} model steps (expected 1)"
    assert calls["n"] == 1, f"hook fired {calls['n']}x (expected 1)"
    print(f"[3] on_step_end 'stop' halts the turn ok (consumed={consumed} step)")


async def test_on_step_end_continue():
    """Returning None must NOT change behavior — the loop runs to its natural end."""
    _reset_hooks()
    calls = {"n": 0}

    @cora_ext.register_hook("on_step_end")
    def keep_going(ctx, ev):
        calls["n"] += 1
        return None

    td = tempfile.mkdtemp()
    cfg, sess = make_session(td, reactive=True)
    instrument(sess)
    agent_router._enumerate_actions = fake_enumerate
    consumed = await _run_turn(sess, food_officer(cfg),
                               [_talk(0), _talk(1), {"content": "done", "tool_calls": []}])
    assert consumed == 3, f"loop stopped early: consumed {consumed} (expected 3)"
    assert calls["n"] == 2, f"hook fired {calls['n']}x (expected 2 tool-call steps)"
    print(f"[4] on_step_end None continues ok (consumed={consumed} steps)")


async def test_no_hooks_no_change():
    """With nothing registered the loop must behave exactly as before (and build no ctx)."""
    _reset_hooks()
    td = tempfile.mkdtemp()
    cfg, sess = make_session(td, reactive=True)
    instrument(sess)
    agent_router._enumerate_actions = fake_enumerate
    consumed = await _run_turn(sess, food_officer(cfg),
                               [_talk(0), {"content": "done", "tool_calls": []}])
    assert consumed == 2, f"baseline changed: consumed {consumed} (expected 2)"
    print("[5] no hooks registered = no behavior change ok")


async def main():
    await test_on_turn_start_injects()
    await test_on_turn_start_refuses_unsafe_roles()
    await test_on_step_end_stops_turn()
    await test_on_step_end_continue()
    await test_no_hooks_no_change()
    _reset_hooks()
    print("\nALL LOOP-HOOK TESTS PASSED ✓")


if __name__ == "__main__":
    asyncio.run(main())
