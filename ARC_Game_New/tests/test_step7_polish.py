"""Hermetic test for Step 7 polish: (a) explicit no-action note, (c) transcript
compaction. No Unity, no LLM, no network.

Checks:
  1. _compact_transcript bounds a runaway transcript to system + last N turns,
     cuts ONLY at a user boundary (no orphaned tool result), keeps the newest
     turns (so the committed ledger, which rides in each user turn, survives).
  2. A continuous turn that commits nothing appends an explicit "[note] ... no
     game action taken" grounding message to the persistent transcript (visible
     to the next activation), and does NOT send it to the director.

Run:
  env -u ALL_PROXY -u all_proxy -u HTTPS_PROXY -u https_proxy \
      -u HTTP_PROXY -u http_proxy PYTHONPATH="$(pwd)" ./.venv/bin/python test_step7_polish.py
"""
import asyncio
import os
import tempfile

import agent_router
from agent_router import Session
from agent_config import load_config


def test_compaction():
    # system + 20 activation turns; each turn = user(re-ground) + assistant(tool) + tool(result)
    msgs = [{"role": "system", "content": "SYS"}]
    for n in range(20):
        msgs.append({"role": "user", "content": f"turn {n} LEDGER:{n}"})
        msgs.append({"role": "assistant", "content": None,
                     "tool_calls": [{"id": f"c{n}", "type": "function",
                                     "function": {"name": "read_state", "arguments": "{}"}}]})
        msgs.append({"role": "tool", "tool_call_id": f"c{n}", "content": f"state {n}"})

    keep = 8
    out = Session._compact_transcript(list(msgs), keep)

    assert out[0]["role"] == "system" and out[0]["content"] == "SYS", "system message lost"
    # Cut at a user boundary → first body msg is a user re-grounding (no orphan tool result).
    assert out[1]["role"] == "user", f"first body msg is {out[1]['role']} (orphaned tool call?)"
    # Exactly system + last `keep` turns (3 msgs each).
    assert len(out) == 1 + keep * 3, f"expected {1 + keep*3} msgs, got {len(out)}"
    # Newest turns retained → the ledger from the last turn survives.
    assert out[-1] == msgs[-1], "newest turn not preserved"
    assert any("LEDGER:19" in m.get("content", "") for m in out if m["role"] == "user"), \
        "most-recent ledger dropped"
    assert not any("LEDGER:5" in (m.get("content") or "") for m in out), \
        "old turn should have been shed"

    # Protocol integrity: every tool result's id has a preceding assistant tool_call.
    open_ids = set()
    for m in out:
        if m["role"] == "assistant":
            for tc in m.get("tool_calls", []):
                open_ids.add(tc["id"])
        elif m["role"] == "tool":
            assert m["tool_call_id"] in open_ids, f"orphaned tool result {m['tool_call_id']}"

    # Below-threshold transcripts are returned untouched.
    short = [{"role": "system", "content": "S"}, {"role": "user", "content": "u"}]
    assert Session._compact_transcript(list(short), keep) == short, "short transcript mutated"
    print(f"[1] compaction ok: 61→{len(out)} msgs, cut at user boundary, "
          "newest ledger kept, old turns shed, tool pairs intact")


async def test_no_action_note():
    cfg = load_config("config/continuous_agents_domain.json")
    with tempfile.TemporaryDirectory() as td:
        sess = Session(cfg, "sess-noop", "test", os.path.join(td, "log.jsonl"), websocket=None)
        agent = cfg.get_subagents()[0]

        sent = []

        async def fake_send(payload):
            sent.append(payload)
        sess._send = fake_send

        # Model does nothing: no tool call, no closing text → executed_total stays 0.
        def fake_run_tool_step(messages, tools, agent_cfg, tool_mode):
            return {"content": "", "tool_calls": []}
        agent_router.run_tool_step = fake_run_tool_step
        agent_router.render_state_text = lambda s: "STATE"

        gs = {"sessionInfo": {}, "satisfactionAndBudget": {}, "allActiveTasks": []}
        await sess._run_continuous_inner(agent, {"tasks": []}, [], gs, [])

        transcript = sess._continuous_transcripts[agent.subagent_name]
        note = transcript[-1]
        assert note["role"] == "user" and "no game action taken" in note["content"], \
            f"no-action note missing; last msg = {note}"
        # The grounding note is model-facing only — never pushed to the director channel.
        agent_frames = [p for p in sent if p.get("type") == "agent_response"]
        assert not any("no game action taken" in (p.get("content") or "") for p in agent_frames), \
            "no-action note leaked to the director"
        print("[2] no-action note ok: explicit grounding note appended, not sent to director")


async def main():
    test_compaction()
    await test_no_action_note()
    print("\nALL STEP-7 POLISH TESTS PASSED ✓")


if __name__ == "__main__":
    asyncio.run(main())
