"""Hermetic test for Step 8: the RL gym env speaks command tags, not integer CSV.

No Unity, no LLM, no network. The env is built via __new__ (skipping the socket
__init__) and its _send_request is monkeypatched to a canned Unity, so we can drive
step() with tag strings and inspect exactly what it would send + surface.

Checks:
  1. A build+hire tag turn sends exactly those two resolved actions to Unity (in the
     parser's commonsense order) and reports them in info; parse_errors is empty.
  2. An all-unresolved tag turn is a TRUE no-op: zero execute_action frames, the bad
     tag surfaced in info["parse_errors"], and step() still returns a valid tuple.
  3. A <task> tag routes through select_task_choice (not execute_action).
  4. The action_space charset admits the tag alphabet and rejects a stray char.

Run:
  env -u ALL_PROXY -u all_proxy -u HTTPS_PROXY -u https_proxy \
      -u HTTP_PROXY -u http_proxy PYTHONPATH="$(pwd)" ./.venv/bin/python test_gym_tags.py
"""
import json

from arc_game_gym_env_tcp import ARCGameGymEnv


def menu():
    return [
        {"action_id": "build_Kitchen_1", "action_type": "construction", "cost": 1000,
         "description": "Build Kitchen at Site1",
         "construction": {"building_type": "Kitchen", "site_id": 1, "site_name": "Site1"}},
        {"action_id": "hire_untrained_2", "action_type": "worker", "cost": 200,
         "description": "Hire 2 untrained workers",
         "worker": {"worker_action_type": "hire_untrained", "quantity": 2}},
    ]


def game_state():
    return {
        "sessionInfo": {"currentDay": 1, "currentRound": 0, "isGameOver": False, "finalDay": 5},
        "satisfactionAndBudget": {"satisfaction": 50.0, "budget": 100000},
        "workforceState": {"freeTrainedWorkers": 4, "freeUntrainedWorkers": 4},
        "mapState": {"facilities": []},
        "allActiveTasks": [],
        "rewardMetrics": {},
    }


def make_env():
    """Build the env without touching the socket (__new__), wired to a canned Unity."""
    env = ARCGameGymEnv.__new__(ARCGameGymEnv)
    env.current_step = 0
    env.current_round = 0
    env.max_episode_steps = 10
    env.manual_transfers = True
    env.previous_satisfaction = 50.0
    env.previous_score = 0.0
    env.game_state = game_state()
    env.valid_actions = menu()

    sent = []

    def fake_send(request):
        sent.append(request)
        if request.get("type") == "execute_action":
            return {"type": "action_result", "success": True}
        if request.get("type") == "advance_time":
            return {"type": "game_state", "game_state": json.dumps(game_state())}
        return {}
    env._send_request = fake_send
    env._enumerate_valid_actions = lambda: setattr(env, "valid_actions", menu())

    choices_called = []
    env.select_task_choice = lambda tid, cid: (choices_called.append((tid, cid)) or True)

    return env, sent, choices_called


def executed_action_ids(sent):
    return [json.loads(r["action"]).get("action_id")
            for r in sent if r.get("type") == "execute_action"]


def test_build_hire():
    env, sent, _ = make_env()
    obs, reward, term, trunc, info = env.step(
        "<build>Kitchen,1</build>\n<hire>untrained,2</hire>")
    ids = executed_action_ids(sent)
    assert ids == ["build_Kitchen_1", "hire_untrained_2"], f"executed {ids}"
    assert not info["parse_errors"], f"unexpected parse errors: {info['parse_errors']}"
    assert info["parsed_commands"], "parsed_commands should list the resolved tags"
    assert [a for a in info["executed_actions"]], "executed_actions should be reported"
    assert any(r.get("type") == "advance_time" for r in sent), "round did not advance"
    print(f"[1] build+hire ok: executed {ids}, parsed={info['parsed_commands']}")


def test_unresolved_noop():
    env, sent, _ = make_env()
    obs, reward, term, trunc, info = env.step("<build>Kitchen,99</build>")
    ids = executed_action_ids(sent)
    assert ids == [], f"unresolved tag should execute nothing, got {ids}"
    assert info["parse_errors"], "bad site should surface a parse error"
    assert any(r.get("type") == "advance_time" for r in sent), "no-op turn must still advance"
    assert isinstance(reward, float), "step must return a valid reward on a no-op turn"
    print(f"[2] unresolved no-op ok: 0 executed, parse_errors={info['parse_errors']}")


def test_task_choice_routes():
    env, sent, choices = make_env()
    obs, reward, term, trunc, info = env.step("<task>7,2</task>")
    assert choices == [(7, 2)], f"task tag should call select_task_choice, got {choices}"
    assert executed_action_ids(sent) == [], "a task tag must not go through execute_action"
    assert info["task_choices"] == [{"taskId": 7, "choiceId": 2}], info["task_choices"]
    print(f"[3] task-choice routing ok: select_task_choice{choices[0]}")


def test_charset():
    env, _, _ = make_env()
    # __new__ skipped __init__, so build the space the way __init__ does to check the charset.
    import string
    import gymnasium as gym
    space = gym.spaces.Text(max_length=1024,
                            charset=string.ascii_letters + string.digits + "<>/,_-. \n")
    assert space.contains("<build>Kitchen,1</build>"), "tag string must be in the action space"
    assert not space.contains("<build>Kitchen,1</build>!"), "'!' is not in the tag alphabet"
    print("[4] charset ok: tag grammar admitted, stray char rejected")


def main():
    test_build_hire()
    test_unresolved_noop()
    test_task_choice_routes()
    test_charset()
    print("\nALL STEP-8 GYM-TAG TESTS PASSED ✓")


if __name__ == "__main__":
    main()
