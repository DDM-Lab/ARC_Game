"""Token-budget transcript compaction.

Compaction used to trigger on a TURN COUNT, which cannot know how big a turn is: a
talk-only activation costs a few hundred tokens and a four-step tool turn costs
thousands. Tuned at 12 turns against a 16k window, the transcript blew the window at
about 5 activations and the server rejected the request outright (400 "maximum context
length is 16384"), so compaction never ran at all. These tests pin the token-driven
behaviour that replaced it.

Run: .venv/bin/python test_token_compaction.py
"""
from agent_router import Session
from continuous_agent import _est_prompt_tokens, known_ctx_limit, _CTX_LIMIT_CACHE


def _transcript(n_turns, tool_result_chars=4000):
    """system + n activation turns, each user + assistant(tool_call) + fat tool result."""
    msgs = [{"role": "system", "content": "SYS" * 100}]
    for n in range(n_turns):
        msgs.append({"role": "user", "content": f"turn {n} LEDGER:{n} " + "x" * 400})
        msgs.append({"role": "assistant", "content": None,
                     "tool_calls": [{"id": f"c{n}", "type": "function",
                                     "function": {"name": "read_state", "arguments": "{}"}}]})
        msgs.append({"role": "tool", "tool_call_id": f"c{n}",
                     "content": f"state {n} " + "y" * tool_result_chars})
    return msgs


def _pairs_intact(out):
    open_ids = set()
    for m in out:
        if m["role"] == "assistant":
            for tc in m.get("tool_calls", []):
                open_ids.add(tc["id"])
        elif m["role"] == "tool":
            assert m["tool_call_id"] in open_ids, f"orphaned tool result {m['tool_call_id']}"


def test_under_high_water_untouched():
    """Below the high watermark nothing moves -- a stable prefix is a cacheable prefix."""
    msgs = _transcript(2)
    budget = _est_prompt_tokens(msgs, None) * 3      # way under
    out = Session._compact_transcript(list(msgs), 8, 12, token_budget=budget)
    assert out == msgs, "transcript compacted while under the high watermark"
    print("[1] under high water: untouched")


def test_compacts_to_low_water():
    msgs = _transcript(20)
    before = _est_prompt_tokens(msgs, None)
    budget = before // 4                              # firmly over HIGH
    out = Session._compact_transcript(list(msgs), 8, 12, token_budget=budget,
                                      high_water=0.80, low_water=0.50)
    after = _est_prompt_tokens(out, None)
    assert after < before, "no compaction happened"
    assert after <= budget * 0.80, f"still above high water: {after} > {budget*0.8:.0f}"
    assert out[0]["role"] == "system", "system message lost"
    assert out[1]["role"] == "user", f"cut mid-turn: first body msg is {out[1]['role']}"
    assert out[-1] == msgs[-1], "newest turn not preserved"
    _pairs_intact(out)
    print(f"[2] compacted to low water: ~{before} -> ~{after} tok (budget {budget})")


def test_big_turns_shed_more_than_small_turns():
    """The whole point: identical TURN COUNTS, different sizes, different cut depth."""
    budget = 6000
    fat = Session._compact_transcript(_transcript(20, tool_result_chars=8000), 8, 12,
                                      token_budget=budget)
    thin = Session._compact_transcript(_transcript(20, tool_result_chars=100), 8, 12,
                                       token_budget=budget)
    fat_turns = sum(1 for m in fat if m["role"] == "user")
    thin_turns = sum(1 for m in thin if m["role"] == "user")
    assert thin_turns > fat_turns, \
        f"token budget ignored turn size: fat kept {fat_turns}, thin kept {thin_turns}"
    print(f"[3] size-aware: fat turns kept {fat_turns}, thin turns kept {thin_turns}")


def test_min_turns_floor():
    """An absurd budget must still leave a usable transcript, not an empty one."""
    msgs = _transcript(20)
    out = Session._compact_transcript(list(msgs), 8, 12, token_budget=1, min_turns=2)
    turns = sum(1 for m in out if m["role"] == "user")
    assert turns >= 2, f"floor breached: {turns} turns left"
    assert out[-1] == msgs[-1], "newest turn dropped"
    _pairs_intact(out)
    print(f"[4] min-turns floor held at {turns} turns under an impossible budget")


def test_falls_back_to_turn_count_without_budget():
    """No window discovered -> legacy behaviour, byte for byte."""
    msgs = _transcript(20)
    out = Session._compact_transcript(list(msgs), 8, 12)
    assert len(out) == 1 + 8 * 3, f"fallback changed: {len(out)} msgs"
    print("[5] fallback to turn counting intact when the window is unknown")


def test_known_ctx_limit_prefers_live_discovery():
    _CTX_LIMIT_CACHE.clear()
    cfg = {"llm_endpoint": "http://x/v1", "context_window": 8192}
    assert known_ctx_limit(cfg) == 8192, "configured window not used on a cold cache"
    _CTX_LIMIT_CACHE["http://x/v1"] = 32768
    assert known_ctx_limit(cfg) == 32768, "live-discovered window did not win"
    assert known_ctx_limit({"llm_endpoint": "http://none/v1"}) is None, \
        "unknown endpoint should report None, not a guess"
    _CTX_LIMIT_CACHE.clear()
    print("[6] known_ctx_limit: live discovery > config > None")


if __name__ == "__main__":
    test_under_high_water_untouched()
    test_compacts_to_low_water()
    test_big_turns_shed_more_than_small_turns()
    test_min_turns_floor()
    test_falls_back_to_turn_count_without_budget()
    test_known_ctx_limit_prefers_live_discovery()
    print("\nALL TOKEN-COMPACTION TESTS PASSED ✓")
