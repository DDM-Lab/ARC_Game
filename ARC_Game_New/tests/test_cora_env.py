"""rl.CoraEnv: the same turn contract as the benchmark (prompt, tools, observation, executor), with
the game replaced by a fake that replays captured states."""
import gzip
import json
import os

from cora import prompts
from cora.observation import ObsConfig, observe, user_message
from cora.tools import openai_tools
from rl import CoraEnv, CoraEnvConfig

_FIXTURE = os.path.join(os.path.dirname(__file__), "fixtures", "game_states.jsonl.gz")
with gzip.open(_FIXTURE, "rt") as _f:
    ROWS = [json.loads(line) for line in _f]


class FakeGame:
    """Replays fixture states: each step advances to the next one."""
    manual_transfers = False

    def __init__(self):
        self.i, self.sent = 0, []

    @property
    def game_state(self):
        return ROWS[self.i]["game_state"]

    def get_valid_actions(self):
        return ROWS[self.i]["actions"]

    def reset(self, seed=None):
        self.i = 0
        return self.game_state, {"day": 1}

    def select_task_choice(self, task_id, choice_id):
        self.sent.append(("choice", task_id, choice_id))
        return True

    def step(self, action):
        self.sent.append(("step", action))
        self.i += 1
        done = self.i == len(ROWS) - 1
        return self.game_state, 0.25, done, False, {"execution_results": [{"success": True}] * len(
            [p for p in str(action).split(",") if p]), "score": 0.25}

    def close(self):
        pass


def _env(**kw):
    env = CoraEnv(CoraEnvConfig(**kw))
    env.game = FakeGame()
    return env


def test_prompt_and_tools_match_the_benchmark():
    env = CoraEnv(CoraEnvConfig(prompt="minimal_v6_1", manual_transfers=False))      # no game launched
    assert env.system_prompt == prompts.system_prompt("minimal_v6_1", manual_transfers=False)
    assert env.prompt_sha == prompts.prompt_sha(env.system_prompt)
    assert env.tools == openai_tools(manual_transfers=False)
    assert env.game is None


def test_reset_and_step_follow_the_turn_contract():
    env = _env()
    user, info = env.reset()
    pack = prompts.load_pack(prompts.DEFAULT_PACK)
    cfg = ObsConfig(mark_unavailable_choices=bool(pack.observation.get("mark_unavailable_choices")))
    assert user == user_message(observe(ROWS[0]["game_state"], ROWS[0]["actions"], cfg))
    assert info["prompt_sha"] == env.prompt_sha
    user2, reward, term, trunc, info = env.step([("hire", {"kind": "untrained", "count": 2}),
                                                 ("nonsense", {})])
    assert user2 == user_message(env.observation) and reward == 0.25
    statuses = [c["status"] for c in info["calls"]]
    assert statuses == ["executed", "invalid"] and info["malformed"]
    assert env.game.sent[-1][0] == "step"


def test_delta_needs_the_previous_turn_in_view():
    stateless = _env(obs_encoding="delta", history=1)
    with_history = _env(obs_encoding="delta", history=2)
    for env in (stateless, with_history):
        env.reset()
    a, *_ = stateless.step([])
    b, *_ = with_history.step([])
    assert a == user_message(stateless.observation, "delta", None)
    assert b == user_message(with_history.observation, "delta", with_history.previous_observation)
