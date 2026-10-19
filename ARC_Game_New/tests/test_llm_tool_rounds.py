"""bench.llm.ask_tools against a fake OpenAI client: one reply per decision by default, more replies
for a sequential tool caller (--tool-rounds), and harmony channel headers stripped from tool names."""
from types import SimpleNamespace as NS

from bench.llm import LocalOptions, ask_tools


def _call(i, name):
    return NS(id=f"c{i}", type="function", function=NS(name=name, arguments='{"task_id": "X", "choice_id": 1}'))


class FakeClient:
    """Returns the scripted replies in order (each a list of tool calls; [] = a reply with no call)."""

    def __init__(self, replies):
        self.replies, self.requests = list(replies), []
        self.chat = NS(completions=NS(create=self.create))

    def create(self, **kw):
        self.requests.append(kw)
        tcs = self.replies.pop(0)
        return NS(choices=[NS(message=NS(content="", tool_calls=tcs))], usage=None)


def test_one_reply_per_decision_by_default():
    client, history = FakeClient([[_call(0, "task"), _call(1, "build")], [_call(2, "hire")]]), []
    dec, *_ = ask_tools(client, "m", "sys", [], "turn", history=history)
    assert [tc.function.name for tc in dec["tool_calls"]] == ["task", "build"]
    assert len(client.requests) == 1
    assert [m["role"] for m in history] == ["user", "assistant", "tool", "tool"]
    assert all(m["content"] == "ok" for m in history if m["role"] == "tool")


def test_tool_rounds_collects_sequential_calls_until_a_reply_without_one():
    client, history = FakeClient([[_call(0, "task")], [_call(1, "build")], []]), []
    dec, *_ = ask_tools(client, "m", "sys", [], "turn", history=history, local=LocalOptions(tool_rounds=8))
    assert [tc.function.name for tc in dec["tool_calls"]] == ["task", "build"]
    assert [len(r["messages"]) for r in client.requests] == [2, 4, 6]   # each reply sees the queued acks
    assert history[2]["content"].startswith("queued")


def test_tool_rounds_stop_at_the_cap():
    client = FakeClient([[_call(i, "task")] for i in range(5)])
    dec, *_ = ask_tools(client, "m", "sys", [], "turn", history=[], local=LocalOptions(tool_rounds=3))
    assert len(dec["tool_calls"]) == 3 and len(client.requests) == 3


def test_harmony_channel_header_is_stripped_from_tool_names():
    client, history = FakeClient([[_call(0, "task<|channel|>commentary")], [_call(1, "build<|channel|>json")], []]), []
    dec, *_ = ask_tools(client, "m", "sys", [], "turn", history=history, local=LocalOptions(tool_rounds=8))
    assert [tc.function.name for tc in dec["tool_calls"]] == ["task", "build"]
    assert [m["tool_calls"][0]["function"]["name"] for m in history if m.get("tool_calls")] == ["task", "build"]
