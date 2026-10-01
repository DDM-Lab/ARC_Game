"""The officers' read tools (get_facilities / get_workforce / get_tasks / get_logistics) return
their slice of the observation on a real game state, through the router's tool dispatcher."""
import gzip
import json
import os
import tempfile

import pytest

from router.config import load_config
from agent_router import Session
from cora.actions import enumerate_actions

_FIXTURE = os.path.join(os.path.dirname(__file__), "fixtures", "game_states.jsonl.gz")
with gzip.open(_FIXTURE, "rt") as _f:
    STATE = [json.loads(line) for line in _f][2]["game_state"]


@pytest.mark.parametrize("tool", ["get_facilities", "get_workforce", "get_tasks", "get_logistics"])
async def test_getter_returns_its_section(tool):
    cfg = load_config("config/continuous_agents_domain.json")
    with tempfile.TemporaryDirectory() as td:
        sess = Session(cfg, "sess-getters", "test", os.path.join(td, "log.jsonl"), websocket=None)
        sess._latest_game_state = STATE
        agent = cfg.get_subagents()[0]
        actions = enumerate_actions(STATE)
        text, *_ = await sess._dispatch_continuous_tool(
            agent, {"name": tool, "arguments": {}}, STATE, actions, actions)
    assert isinstance(text, str) and text.strip()
    assert not text.startswith("ERROR"), text[:200]
