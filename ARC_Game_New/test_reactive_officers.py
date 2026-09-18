"""Hermetic test for reactive autonomy ("activate when spoken to").

No Unity, no LLM, no network. Monkeypatches run_tool_step (scripted officer) and
_enumerate_actions (fixed menu), simulates Unity via _handle_action_result. All
officers run with opening_mode="reactive" (set on the loaded config in-process; no
config file is touched). Verifies:

  1. UNPROMPTED (begin_round) turn is BRIEF-ONLY: the palette handed to the model
     has NO acting tools (execute_commands/propose_choices stripped), the turn
     message carries the brief-only closing, and even a model that TRIES to call
     execute_commands is refused at dispatch — zero execute_action frames reach Unity.
  2. SPOKEN-TO (director_message) turn MAY ACT: the palette includes execute_commands,
     the closing says "do exactly what was asked", and a command tag executes for real.
  3. ONE-MESSAGE CAP: a brief-only turn stops after the first talk_to_director even
     if the model would send a second — exactly one director-facing message.
  4. NON-REACTIVE UNCHANGED: an "emergent" officer keeps the full palette on an
     unprompted turn and can act (no regression to existing behavior).

Run:
  env -u ALL_PROXY -u all_proxy -u HTTPS_PROXY -u https_proxy \
      -u HTTP_PROXY -u http_proxy ./.venv/bin/python test_reactive_officers.py
"""
import asyncio
import os
import tempfile

import agent_router
from agent_router import Session
from agent_config import load_config


# The officer's ACTING surface after the typed-tool cutover (B2): the 7 typed action tools
# plus the legacy execute_commands (still dispatchable) and propose_choices. Asserting on
# this set rather than the literal "execute_commands" keeps these tests meaningful — the
# palette now offers typed tools, so a bare `"execute_commands" not in tools` check would
# pass vacuously and stop catching an acting-tool leak.
ACTING_TOOLS = {"build", "hire", "train", "staff", "deconstruct", "task", "transfer",
                "execute_commands", "propose_choices"}


def fake_enumerate(game_state):
    return [
        {"action_id": "build_Kitchen_1", "action_type": "construction", "cost": 1000,
         "description": "Build Kitchen at Site1",
         "construction": {"building_type": "Kitchen", "site_id": 1, "site_name": "Site1"}},
        {"action_id": "hire_untrained_1", "action_type": "worker", "cost": 100,
         "description": "Hire 1 untrained worker",
         "worker": {"worker_action_type": "hire_untrained", "quantity": 1}},
    ]


def base_state(v=0):
    return {
        "_v": v,
        "sessionInfo": {"currentDay": 1, "currentSegment": 0},
        "satisfactionAndBudget": {"satisfaction": 50.0, "budget": 100000,
                                  "overallSatisfaction": 50.0, "currentBudget": 100000},
        "dailyMetrics": {"currentBudget": 100000},
        "workforceState": {"freeTrainedWorkers": 4, "freeUntrainedWorkers": 4},
        "workers": {"free": 8},
        "buildings": {}, "tasks": [], "constructionState": {}, "mapState": {"facilities": []},
        "logistics": {},
    }


def make_session(td, reactive=True):
    cfg = load_config("config/continuous_agents_domain.json")
    for a in cfg.agents:
        a.opening_mode = "reactive" if reactive else "emergent"
    sess = Session(cfg, "sess-reactive", "test", os.path.join(td, "log.jsonl"), websocket=None)
    sess._latest_game_state = base_state(1)
    sess._latest_all_actions = fake_enumerate(base_state(1))
    return cfg, sess


def food_officer(cfg):
    return next(a for a in cfg.agents if a.subagent_name == "Food Officer")


def instrument(sess):
    """Capture every execute_action frame + every agent_response text."""
    executed, responses = [], []

    async def fake_send(payload):
        if payload.get("type") == "execute_action":
            action = payload["action"]
            executed.append(action["action_id"])

            async def resolve():
                await asyncio.sleep(0.002)
                await sess._handle_action_result({
                    "success": True, "action_id": action["action_id"],
                    "game_state": base_state(2),
                })
            asyncio.create_task(resolve())
    sess._send = fake_send

    orig = sess._send_agent_response

    async def rec_response(agent, text, kind):
        responses.append(text)
        return await orig(agent, text, kind)
    sess._send_agent_response = rec_response
    return executed, responses


async def test_unprompted_brief_only():
    """begin_round tick on a reactive officer: brief-only, cannot act."""
    with tempfile.TemporaryDirectory() as td:
        cfg, sess = make_session(td, reactive=True)
        executed, responses = instrument(sess)
        seen_tools, seen_closing = {}, {}

        step = {}

        def fake_run_tool_step(messages, tools, agent_cfg, tool_mode):
            name = agent_cfg["subagent_name"]
            s = step.get(name, 0)
            step[name] = s + 1
            if s == 0:
                # The step-0 user turn is the last message: record the closing here.
                seen_tools[name] = [t["function"]["name"] for t in tools]
                seen_closing[name] = messages[-1]["content"]
                # A misbehaving model TRIES to act anyway — must be refused, not executed.
                return {"content": "trying to build",
                        "tool_calls": [{"id": f"{name}-0", "name": "execute_commands",
                                        "arguments": {"commands": "<build>Kitchen,1</build>"}}]}
            return {"content": "giving up, briefing instead", "tool_calls": []}
        agent_router.run_tool_step = fake_run_tool_step
        agent_router._enumerate_actions = fake_enumerate

        await sess._run_continuous_concurrent(food_officer(cfg))

        tools = seen_tools["Food Officer"]
        leaked = ACTING_TOOLS & set(tools)
        assert not leaked, f"acting tool leaked into brief-only palette: {sorted(leaked)}"
        assert "propose_choices" not in tools, f"propose_choices leaked into palette: {tools}"
        assert "talk_to_director" in tools, f"brief tool missing: {tools}"
        assert executed == [], f"brief-only turn executed actions: {executed}"
        assert "act only when the director speaks" in seen_closing["Food Officer"], \
            f"brief-only closing missing: {seen_closing['Food Officer'][-200:]}"
        print(f"[1] UNPROMPTED brief-only ok: palette={tools}, 0 executed, refused the build")


async def test_spoken_to_may_act():
    """director_message on a reactive officer: full palette, acts for real."""
    with tempfile.TemporaryDirectory() as td:
        cfg, sess = make_session(td, reactive=True)
        executed, responses = instrument(sess)
        seen_tools, seen_closing = {}, {}
        step = {}

        def fake_run_tool_step(messages, tools, agent_cfg, tool_mode):
            name = agent_cfg["subagent_name"]
            s = step.get(name, 0)
            step[name] = s + 1
            if s == 0:
                seen_tools[name] = [t["function"]["name"] for t in tools]
                seen_closing[name] = messages[-1]["content"]
                return {"content": "acting on order",
                        "tool_calls": [{"id": f"{name}-0", "name": "execute_commands",
                                        "arguments": {"commands": "<build>Kitchen,1</build>"}}]}
            return {"content": "done", "tool_calls": []}
        agent_router.run_tool_step = fake_run_tool_step
        agent_router._enumerate_actions = fake_enumerate

        await sess._run_continuous_for_message(food_officer(cfg))

        tools = seen_tools["Food Officer"]
        assert ACTING_TOOLS & set(tools), f"acting tool missing when spoken-to: {tools}"
        assert executed == ["build_Kitchen_1"], f"spoken-to turn did not act: {executed}"
        # Case-insensitive: this asserts the SCOPING instruction is present, not one exact
        # phrasing. The prompt is tuned often, and an exact-substring check turns every benign
        # reword into a spurious failure (it already drifted from "Do EXACTLY" to "do EXACTLY").
        assert "exactly what they asked" in seen_closing["Food Officer"].lower(), \
            f"scoped closing missing: {seen_closing['Food Officer'][-200:]}"
        print(f"[2] SPOKEN-TO may-act ok: palette has acting tools, executed={executed}")


async def test_one_message_cap():
    """Brief-only turn: two talk_to_director calls collapse to exactly one."""
    with tempfile.TemporaryDirectory() as td:
        cfg, sess = make_session(td, reactive=True)
        executed, responses = instrument(sess)
        step = {}

        def fake_run_tool_step(messages, tools, agent_cfg, tool_mode):
            name = agent_cfg["subagent_name"]
            s = step.get(name, 0)
            step[name] = s + 1
            # The model would happily send a briefing then a filler follow-up.
            return {"content": f"msg {s}",
                    "tool_calls": [{"id": f"{name}-{s}", "name": "talk_to_director",
                                    "arguments": {"message": f"brief #{s}"}}]}
        agent_router.run_tool_step = fake_run_tool_step
        agent_router._enumerate_actions = fake_enumerate

        await sess._run_continuous_concurrent(food_officer(cfg))

        briefs = [r for r in responses if r.startswith("brief #")]
        assert briefs == ["brief #0"], f"one-message cap failed, got: {briefs}"
        print(f"[3] ONE-MESSAGE CAP ok: only the first brief sent ({briefs})")


async def test_no_director_echo():
    """Executing via execute_commands must NOT post a per-action 🔨 chat bubble."""
    with tempfile.TemporaryDirectory() as td:
        cfg, sess = make_session(td, reactive=True)
        executed, responses = instrument(sess)
        step = {}

        def fake_run_tool_step(messages, tools, agent_cfg, tool_mode):
            name = agent_cfg["subagent_name"]
            s = step.get(name, 0)
            step[name] = s + 1
            if s == 0:
                return {"content": None,
                        "tool_calls": [{"id": f"{name}-0", "name": "execute_commands",
                                        "arguments": {"commands": "<build>Kitchen,1</build>"}}]}
            # The officer's OWN words are allowed; the robotic 🔨 echo is not.
            return {"content": "Built the kitchen as ordered.", "tool_calls": []}
        agent_router.run_tool_step = fake_run_tool_step
        agent_router._enumerate_actions = fake_enumerate

        await sess._run_continuous_for_message(food_officer(cfg))

        assert executed == ["build_Kitchen_1"], f"action did not run: {executed}"
        hammer = [r for r in responses if r.startswith("🔨") or r.startswith("🗳️")]
        assert not hammer, f"per-action echo leaked to director: {hammer}"
        assert "Built the kitchen as ordered." in responses, \
            f"officer's own summary should still reach the director: {responses}"
        print(f"[5] NO DIRECTOR ECHO ok: 0 🔨 bubbles, officer's own words delivered")


async def test_telemetry_wired():
    """The turn record carries real tokens + execution_results + attempts (F5)."""
    with tempfile.TemporaryDirectory() as td:
        cfg, sess = make_session(td, reactive=True)
        executed, responses = instrument(sess)
        captured = {}
        orig_log = sess._log_turn

        def spy_log_turn(agent, fs, fa, packages, sel, results, sat_b, gs, bud_b, raw, tokens):
            captured.update(packages=packages, results=results, raw=raw, tokens=tokens)
            return orig_log(agent, fs, fa, packages, sel, results, sat_b, gs, bud_b, raw, tokens)
        sess._log_turn = spy_log_turn

        step = {}

        def fake_run_tool_step(messages, tools, agent_cfg, tool_mode):
            name = agent_cfg["subagent_name"]
            s = step.get(name, 0)
            step[name] = s + 1
            if s == 0:
                return {"content": "acting", "usage": {"total_tokens": 123},
                        "tool_calls": [{"id": f"{name}-0", "name": "execute_commands",
                                        "arguments": {"commands": "<build>Kitchen,1</build>"}}]}
            return {"content": "Done.", "usage": {"total_tokens": 77}, "tool_calls": []}
        agent_router.run_tool_step = fake_run_tool_step
        agent_router._enumerate_actions = fake_enumerate

        await sess._run_continuous_for_message(food_officer(cfg))

        assert captured["tokens"] == 200, f"tokens not summed across steps: {captured['tokens']}"
        res = captured["results"]
        assert any(r.get("action_id") == "build_Kitchen_1" and r.get("success")
                   for r in res), f"execution_results missing the build: {res}"
        assert any(p.get("tool") == "execute_commands" for p in captured["packages"]), \
            f"attempts missing the execute_commands call: {captured['packages']}"
        assert captured["raw"] == "Done.", f"raw should be the officer's final text: {captured['raw']}"
        print(f"[6] TELEMETRY WIRED ok: tokens={captured['tokens']}, "
              f"results={len(res)} rec(s), attempts={len(captured['packages'])}")


async def test_emergent_unchanged():
    """emergent officer on an unprompted turn keeps the full palette and can act."""
    with tempfile.TemporaryDirectory() as td:
        cfg, sess = make_session(td, reactive=False)  # emergent
        executed, responses = instrument(sess)
        seen_tools = {}
        step = {}

        def fake_run_tool_step(messages, tools, agent_cfg, tool_mode):
            name = agent_cfg["subagent_name"]
            seen_tools[name] = [t["function"]["name"] for t in tools]
            s = step.get(name, 0)
            step[name] = s + 1
            if s == 0:
                return {"content": "acting",
                        "tool_calls": [{"id": f"{name}-0", "name": "execute_commands",
                                        "arguments": {"commands": "<build>Kitchen,1</build>"}}]}
            return {"content": "done", "tool_calls": []}
        agent_router.run_tool_step = fake_run_tool_step
        agent_router._enumerate_actions = fake_enumerate

        await sess._run_continuous_concurrent(food_officer(cfg))

        assert ACTING_TOOLS & set(seen_tools["Food Officer"]), \
            f"emergent palette lost its acting tools: {seen_tools['Food Officer']}"
        assert executed == ["build_Kitchen_1"], \
            f"emergent officer failed to act unprompted (regression): {executed}"
        print(f"[4] EMERGENT unchanged ok: full palette, executed={executed}")


async def main():
    await test_unprompted_brief_only()
    await test_spoken_to_may_act()
    await test_one_message_cap()
    await test_no_director_echo()
    await test_telemetry_wired()
    await test_emergent_unchanged()
    print("\nALL REACTIVE-AUTONOMY TESTS PASSED ✓")


if __name__ == "__main__":
    asyncio.run(main())
