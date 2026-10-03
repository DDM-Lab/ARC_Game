"""CoraEnv: the game as a tool-call environment, for any RL trainer and for the benchmark.

One step is one decision under the turn contract (docs/ARCHITECTURE.md): the policy is shown the
system prompt, the tool schema and the turn's user message, may reason, and answers with tool calls
(game actions only; they return nothing to the model). The calls run through cora.executor, the game
advances to the next decision, and the reward is the change in score (cora.scoring), so an episode's
rewards sum to its final score.

    env = CoraEnv(CoraEnvConfig(seed=7000))
    messages = [{"role": "system", "content": env.system_prompt}]
    user, info = env.reset()
    while True:
        calls = policy(env.system_prompt, env.tools, user)        # [(name, args)] or tool_call objects
        user, reward, terminated, truncated, info = env.step(calls)
        if terminated or truncated:
            break
    env.close()

The benchmark (bench/episode.py) plays its episodes through this class, so a benchmark score and an
RL return are the same quantity on the same prompt, observation, tools and execution.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Optional

from cora import executor
from cora import prompts as cora_prompts
from cora.env import GameEnv
from cora.env.unity_process import default_exe
from cora.observation import ObsConfig, observe, user_message
from cora.tools import openai_tools


@dataclass(frozen=True)
class CoraEnvConfig:
    prompt: str = cora_prompts.DEFAULT_PACK
    ablation: str = ""                    # cora.prompt_ablation spec
    image_mode: str = "none"              # prompt variant for image arms (the images are the caller's)
    manual_transfers: bool = False        # False = the human GUI's rule (transfers only via tasks)
    show_impacts: bool = True
    obs_encoding: str = "compact"         # compact | json | delta
    history: int = 1                      # turns the policy sees; delta diffs only when > 1
    max_steps: int = 40                   # decision cap; a full game is cora.env.DECISIONS (29) and ends itself
    skip_end_of_day: bool = True          # roll through the end-of-day report stop (GameEnv)
    seed: Optional[int] = None
    map_config: Optional[str] = None      # map JSON, or "none" for the scene's built-in layout
    param_config: Optional[str] = None    # parameter CSV (default: the build's bundled sheet)
    unity_exe: Optional[str] = None       # default: cora.env.unity_process.default_exe()
    port: int = 9900
    unity_log: Optional[str] = None
    frame_capture: str = "off"            # real camera frames (render build): off | step | game_time
    frame_dir: Optional[str] = None


class CoraEnv:
    def __init__(self, config: CoraEnvConfig = CoraEnvConfig()):
        self.config = c = config
        pack = cora_prompts.load_pack(c.prompt)
        self.pack_name = pack.name
        self.system_prompt = cora_prompts.render(pack, manual_transfers=c.manual_transfers,
                                                 image_mode=c.image_mode)
        if c.ablation:
            from cora.prompt_ablation import ablate
            self.system_prompt = ablate(self.system_prompt, c.ablation)
        self.prompt_sha = cora_prompts.prompt_sha(self.system_prompt)
        self.tools = openai_tools(manual_transfers=c.manual_transfers)
        # The pack declares the observation features its text relies on (minimal_v6_1: marked choices).
        self.obs_config = ObsConfig(show_impacts=c.show_impacts,
                                    mark_unavailable_choices=bool(pack.observation.get("mark_unavailable_choices")))
        self.game: Optional[GameEnv] = None           # the Unity game, launched by the first reset()
        self.observation: Optional[dict] = None       # this decision's observation dict
        self.previous_observation: Optional[dict] = None

    # ── the turn ──
    def _observe(self) -> str:
        self.previous_observation, self.observation = self.observation, \
            observe(self.game.game_state, self.game.get_valid_actions(), self.obs_config)
        # A delta is only readable when the previous turn is in the policy's view.
        prev = self.previous_observation if self.config.history > 1 else None
        return user_message(self.observation, self.config.obs_encoding, prev)

    def reset(self, seed: Optional[int] = None) -> tuple:
        """Start a game: (the first user message, info with the scenario the game reports)."""
        c = self.config
        if self.game is None:
            self.game = GameEnv(unity_exe_path=c.unity_exe or default_exe(render=c.frame_capture != "off"),
                                unity_port=c.port, max_episode_steps=c.max_steps, seed=c.seed,
                                map_config=c.map_config, param_config=c.param_config,
                                unity_log_path=c.unity_log, manual_transfers=c.manual_transfers,
                                frame_capture=c.frame_capture, frame_dir=c.frame_dir,
                                skip_end_of_day=c.skip_end_of_day)
        self.observation = None
        _, info = self.game.reset(seed=seed)
        info = dict(info, scenario=(self.game.game_state or {}).get("scenario"),
                    prompt_sha=self.prompt_sha)
        return self._observe(), info

    def step(self, tool_calls) -> tuple:
        """Run one decision's tool calls and advance to the next decision.

        tool_calls: (name, args) pairs, {"name", "arguments"} dicts or OpenAI tool_call objects;
        none = do nothing this decision. info["calls"] holds each call's outcome (executed /
        refused / invalid, with the reason); info["malformed"] is True if any call named an
        unknown tool or had unreadable arguments."""
        results, (_, reward, terminated, truncated, info) = executor.execute_turn(self.game, tool_calls or [])
        info = dict(info, call_results=results, calls=[r.as_dict() for r in results],
                    malformed=any(r.malformed for r in results))
        return self._observe(), reward, terminated, truncated, info

    def close(self):
        if self.game is not None:
            self.game.close()
            self.game = None
