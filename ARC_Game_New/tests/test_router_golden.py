"""Golden recording of what the officer router sends to Unity and returns to the model.

Scripted officer tool calls run through the router's tool dispatcher on real captured game states
(tests/fixtures/game_states.jsonl.gz), with Unity simulated: every action succeeds except those
named in FAIL, and a director answers proposals and standing-order cards. Two things are recorded
per scenario:

    frames   every frame the router sends to the client (execute_action, select_task_choice,
             proposal cards, chat bubbles). These are the Unity contract: a router refactor must
             keep them byte-identical so the deployed game needs no rebuild.
    results  every tool result returned to the officer (model-facing text). Intentional wording
             changes are reviewed and re-recorded.

Re-record after an intended change:  GOLDEN_UPDATE=1 pytest tests/test_router_golden.py
"""
import asyncio
import gzip
import json
import os
import tempfile

import pytest

import agent_router
from agent_config import load_config
from agent_router import Session
from cora.actions import enumerate_actions

HERE = os.path.dirname(__file__)
GOLDEN = os.path.join(HERE, "fixtures", "router_golden.json")
with gzip.open(os.path.join(HERE, "fixtures", "game_states.jsonl.gz"), "rt") as _f:
    STATES = {r["step"]: r["game_state"] for r in map(json.loads, _f)}

FAIL = {"build_Shelter_9"}                      # Unity refuses these action ids

# Other router tests replace module functions with fakes and do not restore them; captured at
# collection (before any test runs) and pinned for each recording.
_REAL = {name: getattr(agent_router, name)
         for name in ("_enumerate_actions", "run_tool_step", "filter_actions", "officer_text")}


def _clean(o):
    """Drop run-specific values (timestamps, random proposal ids)."""
    if isinstance(o, dict):
        return {k: ("<id>" if k == "proposal_id" else _clean(v)) for k, v in o.items() if k != "timestamp"}
    if isinstance(o, list):
        return [_clean(v) for v in o]
    return o


DOMAIN = "config/continuous_agents_domain.json"            # four domain officers (no task groups)
ALL_OFFICERS = "config/continuous_all_officers_qwen.json"  # five officers that answer task groups


class Harness:
    def __init__(self, td, step, config):
        self.cfg = load_config(config)
        self.sess = Session(self.cfg, "sess-golden", "test", os.path.join(td, "log.jsonl"), websocket=None)
        self.state = STATES[step]
        self.sess._latest_game_state = self.state
        self.sess._latest_all_actions = enumerate_actions(self.state)
        self.sess._task_choice_supported = True          # as a client declares at handshake
        self.frames, self.results = [], []
        self.director = None            # callback(frame) -> reply message for a card
        self.sess._send = self._send

    def officer(self, name):
        return next(a for a in self.cfg.agents if a.subagent_name == name)

    async def _send(self, payload):
        self.frames.append(_clean(payload))
        t = payload.get("type")
        if t == "execute_action":
            aid = payload["action"]["action_id"]
            reply = {"type": "action_result", "action_id": aid, "success": aid not in FAIL,
                     "error_message": "Site is not available" if aid in FAIL else "",
                     "game_state": self.state}
            asyncio.get_event_loop().call_later(0.001, lambda: asyncio.ensure_future(
                self.sess._handle_action_result(reply)))
        elif t == "select_task_choice":
            reply = {"type": "action_result", "action_id": f"choice_{payload['taskId']}_{payload['choiceId']}",
                     "success": True, "error_message": "", "game_state": self.state}
            asyncio.get_event_loop().call_later(0.001, lambda: asyncio.ensure_future(
                self.sess._handle_action_result(reply)))
        elif t in ("agent_message_with_choices", "autonomy_proposal") and self.director:
            reply = self.director(payload)
            handler = (self.sess._handle_choice_made if t == "agent_message_with_choices"
                       else self.sess._handle_autonomy_decision)
            if asyncio.iscoroutinefunction(handler):
                asyncio.get_event_loop().call_later(0.001, lambda: asyncio.ensure_future(handler(reply)))
            else:
                asyncio.get_event_loop().call_later(0.001, handler, reply)

    async def call(self, officer, tool, args, brief_only=False):
        agent = self.officer(officer)
        all_actions = enumerate_actions(self.state)
        filtered = agent_router.filter_actions(all_actions, agent.subaction_space)
        text, *_ = await self.sess._dispatch_continuous_tool(
            agent, {"name": tool, "arguments": args}, self.state, all_actions, filtered,
            brief_only=brief_only)
        self.results.append({"officer": officer, "tool": tool, "args": args, "result": text})


def _site(step, kind):
    """The first open site id (string-free) for building `kind` in a fixture state."""
    return next(a["construction"]["site_id"] for a in enumerate_actions(STATES[step])
                if a["action_type"] == "construction" and a["construction"]["building_type"] == kind)


async def s_build_and_staff(h):
    await h.call("Food Officer", "build", {"type": "kitchen", "site_id": _site(1, "Kitchen")})
    await h.call("Food Officer", "staff", {"site": "Kitchen Alpha"})
    await h.call("Lodging Officer", "staff", {"site": "Shelter Alpha"})


async def s_workforce(h):
    await h.call("Workforce Officer", "hire", {"kind": "untrained", "count": 3})
    await h.call("Workforce Officer", "train", {"count": 2})
    await h.call("Workforce Officer", "hire", {"kind": "trained", "count": 1})


async def s_refused_and_invalid(h):
    await h.call("Lodging Officer", "build", {"type": "shelter", "site_id": 9})       # Unity refuses
    await h.call("Lodging Officer", "build", {"type": "shelter", "site_id": 999})     # no such site
    await h.call("Lodging Officer", "build", {"type": "kitchen", "site_id": 8})       # outside scope
    await h.call("Lodging Officer", "staff", {"site": "Shelter, Alpha"})              # delimiter


async def s_tasks(h):
    await h.call("Food Mass Care Officer", "task", {"task_id": "FOOD_S3", "choice_id": 0})
    await h.call("Lodging Mass Care Officer", "task", {"task_id": "RELOC_CCHARLESTON", "choice_id": 0})
    await h.call("External Relationship Officer", "task", {"task_id": "BUDGET_DAILY", "choice_id": 0})
    await h.call("Food Mass Care Officer", "task", {"task_id": "RELOC_CAMHERST", "choice_id": 0})  # out of scope
    await h.call("Food Mass Care Officer", "task", {"task_id": "NO_SUCH_TASK", "choice_id": 0})


async def s_deconstruct(h):
    await h.call("Lodging Officer", "deconstruct", {"site": "Shelter Bravo"})


async def s_reads(h):
    for tool in ("read_state", "get_facilities", "get_workforce", "get_tasks", "get_logistics"):
        await h.call("Lodging Officer", tool, {})


async def s_brief_only(h):
    await h.call("Food Officer", "build", {"type": "kitchen", "site_id": 8}, brief_only=True)
    await h.call("Food Officer", "send_message", {"to": "Director", "message": "Kitchen stock is low."},
                 brief_only=True)


async def s_propose(h):
    h.director = lambda frame: {"type": "choice_made", "package_index": 0,
                                "execution_results": [{"action_id": "build_Shelter_8", "success": True}],
                                "game_state": h.state}
    await h.call("Lodging Officer", "propose_choices", PROPOSAL)


async def s_standing_order(h):
    h.director = lambda frame: {"type": "autonomy_decision", "proposal_id": frame["proposal_id"],
                                "decision": "accept"}
    h.sess._turn_ctx["Workforce Officer"] = {"triggered_by_director": True}   # the Director asked
    await h.call("Workforce Officer", "add_to_autonomy_list",
                 {"tool": "hire", "args": {"kind": "untrained"}, "context": "free workers fall below 4",
                  "reason": "keep staffing ahead of builds"})
    await h.call("Workforce Officer", "hire", {"kind": "untrained", "count": 2}, brief_only=True)
    await h.call("Workforce Officer", "hire", {"kind": "trained", "count": 1}, brief_only=True)


# The proposal as the officer writes it (the tool's package format).
PROPOSAL = {"reasoning": "Two ways to house the next relocation.",
            "packages": [{"label": "Build a shelter", "description": "One more shelter at site 8.",
                          "calls": [{"tool": "build", "args": {"type": "shelter", "site_id": 8}}]},
                         {"label": "Build two shelters", "description": "Sites 8 and 10.",
                          "calls": [{"tool": "build", "args": {"type": "shelter", "site_id": 8}},
                                    {"tool": "build", "args": {"type": "shelter", "site_id": 10}}]}]}

SCENARIOS = [("build_and_staff", 1, DOMAIN, s_build_and_staff), ("workforce", 2, DOMAIN, s_workforce),
             ("refused_and_invalid", 17, DOMAIN, s_refused_and_invalid), ("tasks", 17, ALL_OFFICERS, s_tasks),
             ("deconstruct", 17, DOMAIN, s_deconstruct), ("reads", 17, DOMAIN, s_reads),
             ("brief_only", 17, DOMAIN, s_brief_only), ("propose", 17, DOMAIN, s_propose),
             ("standing_order", 17, DOMAIN, s_standing_order)]


async def _record(step, config, fn):
    for name, real in _REAL.items():
        setattr(agent_router, name, real)
    with tempfile.TemporaryDirectory() as td:
        h = Harness(td, step, config)
        await fn(h)
        return {"frames": h.frames, "results": h.results}


def record_all():
    return {name: asyncio.run(_record(step, config, fn)) for name, step, config, fn in SCENARIOS}


@pytest.fixture(scope="module")
def recorded():
    rec = record_all()
    if os.environ.get("GOLDEN_UPDATE") or not os.path.exists(GOLDEN):
        with open(GOLDEN, "w") as f:
            json.dump(rec, f, indent=1, sort_keys=True)
    with open(GOLDEN) as f:
        return rec, json.load(f)


@pytest.mark.parametrize("name", [s[0] for s in SCENARIOS])
def test_unity_frames_unchanged(recorded, name):
    rec, golden = recorded
    assert _norm(rec[name]["frames"]) == golden[name]["frames"]


@pytest.mark.parametrize("name", [s[0] for s in SCENARIOS])
def test_model_results_unchanged(recorded, name):
    rec, golden = recorded
    assert _norm(rec[name]["results"]) == golden[name]["results"]


def _norm(o):
    return json.loads(json.dumps(o, sort_keys=True))
