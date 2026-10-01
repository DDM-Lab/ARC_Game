"""bench.export_sft rebuilds each decision exactly as the model saw it and as it answered."""
import gzip
import json
import os
import subprocess
import sys

from cora.observation import observe, user_message

_FIXTURE = os.path.join(os.path.dirname(__file__), "fixtures", "game_states.jsonl.gz")
with gzip.open(_FIXTURE, "rt") as _f:
    ROWS = [json.loads(line) for line in _f]


def test_export_reproduces_prompt_and_tool_calls(tmp_path):
    obs = [observe(r["game_state"], r["actions"]) for r in ROWS[:2]]
    calls = [{"id": "c1", "tool": "hire", "args": {"kind": "untrained", "count": 2}, "status": "executed"}]
    menu = [{"id": "action_0", "tool": "build", "args": {"index": 3}, "status": "executed"}]
    rec = {"model": "m", "policy": "llm", "episode": 0, "error": None, "system_prompt": "SYS",
           "transfers": "task_only", "obs_encoding": "delta", "summary": {"finalScore": 0.5},
           "rounds": [{"r": 0, "obs": obs[0], "calls": calls, "raw": "", "reward": 0.1, "parsed_ok": True},
                      {"r": 1, "obs": obs[1], "calls": menu, "raw": "", "reward": 0.0, "parsed_ok": True}]}
    (tmp_path / "episodes.jsonl").write_text(json.dumps(rec) + "\n")
    subprocess.run([sys.executable, "-m", "bench.export_sft", str(tmp_path)], check=True,
                   cwd=os.path.dirname(os.path.dirname(__file__)), capture_output=True)
    lines = [json.loads(l) for l in (tmp_path / "sft.jsonl").read_text().splitlines()]
    assert len(lines) == 1                               # the menu-index decision is not exported
    system, user, assistant = lines[0]["messages"]
    assert system["content"] == "SYS"
    assert user["content"] == user_message(obs[0], "delta", None)
    assert assistant["tool_calls"][0]["function"] == {"name": "hire",
                                                      "arguments": json.dumps({"kind": "untrained", "count": 2})}
    assert {t["function"]["name"] for t in lines[0]["tools"]} >= {"build", "hire", "staff", "task"}
