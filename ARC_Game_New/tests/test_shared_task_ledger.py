"""Officers and humans act on ONE record of the game: Unity's state.

Playtest 2026-10-07: a $50,000 Storm Funding choice landed twice (officer + director), officers
reported actions against a stale view, and officers moved community food into kitchens, which no
player can do. The router no longer keeps its own "committed this phase" ledger; it re-reads
Unity's state after every commit (Unity's action_result carries no state), and the shared executor
answers a task at most once per turn.
"""
import asyncio
import gzip
import json
import os
import tempfile

from cora import executor
from router.common import _enumerate_actions
from router.config import load_config
from router.session import Session

HERE = os.path.dirname(__file__)
with gzip.open(os.path.join(HERE, "fixtures", "game_states.jsonl.gz"), "rt") as _f:
    _STATES = [json.loads(line)["game_state"] for line in _f]
STATE = _STATES[0]
TASK_STATE = next(gs for gs in _STATES if gs.get("allActiveTasks"))


class _Unity:
    """Unity over the websocket as WebSocketManager answers: results without state; state only on
    get_game_state (as game_state_response)."""

    def __init__(self, sess, fresh):
        self.sess, self.fresh, self.frames = sess, fresh, []

    async def send(self, payload):
        self.frames.append(payload.get("type"))
        t = payload.get("type")
        if t == "execute_action":
            reply = {"success": True, "action_id": payload["action"]["action_id"], "error_message": ""}
        elif t == "select_task_choice":
            reply = {"success": True, "action_id": f"choice_{payload['taskId']}_{payload['choiceId']}",
                     "error_message": ""}
        elif t == "get_game_state":
            reply = {"type": "game_state_response", "game_state": self.fresh}
        else:
            return
        asyncio.get_event_loop().call_later(
            0.001, lambda: asyncio.ensure_future(self.sess._handle_message(json.dumps(reply))))


def _session(td):
    cfg = load_config("config/continuous_all_officers_qwen.json")
    sess = Session(cfg, "sess-ledger", "test", os.path.join(td, "log.jsonl"), websocket=None)
    sess._latest_game_state = STATE
    return sess


def test_router_rereads_state_after_each_commit():
    fresh = dict(STATE, satisfactionAndBudget=dict(STATE["satisfactionAndBudget"], budget=-1234))
    action = next(a for a in _enumerate_actions(STATE) if a["action_type"] == "construction")

    async def run(td):
        sess = _session(td)                       # the Session needs a running loop
        unity = _Unity(sess, fresh)
        sess._send = unity.send
        results, gs = await sess.execute_resolved([{"kind": "action", "action": action}], game_state=STATE)
        return sess, unity, results, gs

    with tempfile.TemporaryDirectory() as td:
        sess, unity, results, gs = asyncio.run(run(td))
    assert results[0]["success"]
    assert unity.frames == ["execute_action", "get_game_state"]
    assert gs["satisfactionAndBudget"]["budget"] == -1234
    assert sess._latest_game_state["satisfactionAndBudget"]["budget"] == -1234


def test_no_router_side_ledger():
    import inspect
    import router.session
    assert not hasattr(Session, "_record_committed")
    assert "_committed_this_phase" not in inspect.getsource(router.session)


def test_officers_get_no_standalone_transfers():
    from router.officer_llm import TOOL_SCHEMAS
    assert "transfer" not in TOOL_SCHEMAS
    assert not any(a["action_type"] == "resource_transfer" for a in _enumerate_actions(STATE))


def test_executor_answers_a_task_once_per_turn():
    tid = TASK_STATE["allActiveTasks"][0]["taskId"]
    resolved, _ = executor.plan_turn([("task", {"task_id": tid, "choice_id": 0}),
                                      ("task", {"task_id": tid, "choice_id": 1})],
                                     executor.Menu(_enumerate_actions(TASK_STATE), TASK_STATE))
    assert resolved[0].status == "resolved"
    assert "already answered" in resolved[1].reason


def test_executor_answers_each_task_that_shares_a_token():
    """Three Casework Requests share CASEWORK_MOTEL: each is answerable by its own id in one turn,
    and the bare token is refused as ambiguous rather than answering the first one."""
    gs = dict(TASK_STATE)
    gs["allActiveTasks"] = [{"taskId": i, "taskTitle": "Casework Request", "affectedFacility": "Motel",
                             "taskType": "Advisory", "choices": [{"choiceId": 1, "choiceText": "Send"}]}
                            for i in (41, 43, 45)]
    menu = executor.Menu(_enumerate_actions(gs), gs)
    resolved, _ = executor.plan_turn([("task", {"task_id": f"CASEWORK_MOTEL#{i}", "choice_id": 1})
                                      for i in (41, 43, 45)] + [("task", {"task_id": "CASEWORK_MOTEL",
                                                                          "choice_id": 1})], menu)
    assert [r.status for r in resolved[:3]] == ["resolved"] * 3
    assert sorted(r.choice["taskId"] for r in resolved[:3]) == [41, 43, 45]
    assert "names 3 active tasks" in resolved[3].reason
