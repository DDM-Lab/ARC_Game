#!/usr/bin/env python3
"""Portable game checkpoints for CORA: save/load a position as a JSON file.

WHY REPLAY, NOT A UNITY SNAPSHOT (YET)
Seeded episodes are byte-identical given the same decisions -- verified with a
deterministic policy: same seed twice produced identical trajectories including final
score, while an unseeded control pair diverged. That makes

    position  ==  (seed, ordered list of mutating RPCs since reset)

a COMPLETE description of a game state, so a checkpoint is just that list plus metadata.
Restoring is reset_game(seed) then resending the calls. Zero Unity changes, correct by
the determinism the seeding work established, and the checkpoint is plain JSON -- it can
be mailed to an annotator, committed next to a paper, or replayed on another machine
with the same binary.

The cost is O(rounds) per load rather than O(1). A native Unity snapshot will eventually
be faster; when it lands it goes behind THIS SAME API and this module becomes its test
oracle -- a snapshot load must reproduce the trajectory that replay reproduces.

WHAT A CHECKPOINT CARRIES
  - seed + the replay journal          -> the game position, exactly
  - conversation                       -> the LLM's message history at that point, so a
                                          model can be re-run from the same context, not
                                          just the same board
  - obs / score / round                -> human-readable, for picking scenarios to annotate
  - meta                               -> model, prompt variant, source run

USAGE
    env = SearchableEnv(unity_exe_path=EXE, unity_port=9876, seed=1000)
    env.reset()
    env.save_checkpoint("ckpt/r00.json")          # portable JSON
    env.advance_round()
    env.load_checkpoint("ckpt/r00.json")          # exact rewind

    # search: branch, evaluate, backtrack
    root = env.save_state()
    for a in candidate_actions:
        env.load_state(root); env.execute(a); env.advance_round()
        value = env.score()
"""
from __future__ import annotations

import copy
import json
import os
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional

from arc_game_gym_env_tcp import ARCGameGymEnv
import reward_scoring

CHECKPOINT_VERSION = 1

# RPCs that change game state, and therefore must be replayed to restore a position.
# Reads (get_game_state, get_valid_actions) are deliberately excluded -- replaying them
# would be harmless but would make every load slower for no benefit.
MUTATING = {"execute_action", "select_task_choice", "advance_time"}


@dataclass(frozen=True)
class StateToken:
    """In-memory handle to a position. Cheap to copy; safe to hold in a search tree."""
    seed: Optional[int]
    reset_count: int
    calls: tuple = field(default=())

    @property
    def depth(self) -> int:
        """Rounds advanced from the episode start."""
        return sum(1 for c in self.calls if c.get("type") == "advance_time")


class SearchableEnv(ARCGameGymEnv):
    """ARCGameGymEnv + save/load, so a search can branch, backtrack and checkpoint."""

    def __init__(self, *args, **kwargs):
        if kwargs.get("seed") is None:
            raise ValueError(
                "SearchableEnv requires seed=<int>. Replay-restore is only correct for a "
                "seeded game; unseeded, a 'restore' would land in a different scenario.")
        super().__init__(*args, **kwargs)
        self._journal: List[Dict[str, Any]] = []
        # Conversation is supplied by the caller (the benchmark owns the LLM loop); the env
        # just carries it so it lands in the checkpoint alongside the position.
        self.conversation: List[Dict[str, Any]] = []
        self.meta: Dict[str, Any] = {}
        self.restore_count = 0
        self.replayed_calls = 0

    # ── recording ────────────────────────────────────────────────────────────────────
    def _send_request(self, request):                       # type: ignore[override]
        resp = super()._send_request(request)
        if isinstance(request, dict) and request.get("type") in MUTATING:
            self._journal.append(copy.deepcopy(request))
        return resp

    def reset(self, *args, **kwargs):                       # type: ignore[override]
        out = super().reset(*args, **kwargs)
        self._journal.clear()                               # reset defines a new origin
        self.conversation = []
        return out

    # ── in-memory save/load (the hot path for search) ────────────────────────────────
    def save_state(self) -> StateToken:
        return StateToken(seed=self.seed_value, reset_count=self._reset_count,
                          calls=tuple(copy.deepcopy(c) for c in self._journal))

    def load_state(self, token: StateToken) -> None:
        if token.seed != self.seed_value:
            raise ValueError(f"token seed {token.seed} != env seed {self.seed_value}; a "
                             f"token is only valid in the env that produced it")
        # Send reset_game EXPLICITLY rather than going through reset(). reset() skips the
        # RPC entirely when _reset_count == 0 (the first episode runs on the launch seed
        # and needs no reset), so rewinding the counter and calling reset() performs NO
        # reset at all and the replay stacks on top of the live game. Measured: that made
        # every equivalence test fail.
        #
        # Episode index i uses seed base+i and is captured with reset_count == i+1, so the
        # seed to restore is base + (reset_count - 1).
        episode_seed = int(self.seed_value) + (token.reset_count - 1)
        resp = super()._send_request({"type": "reset_game", "seed": episode_seed})
        if resp.get("type") != "reset_done":
            raise RuntimeError(f"reset_game failed during load_state: {resp}")
        self.active_seed = resp.get("seed", episode_seed)
        self._reset_count = token.reset_count
        self.current_step = 0
        self.current_round = 0
        self._journal.clear()
        for call in token.calls:
            super()._send_request(copy.deepcopy(call))
            self._journal.append(copy.deepcopy(call))
        self.restore_count += 1
        self.replayed_calls += len(token.calls)

    # ── portable JSON checkpoints ────────────────────────────────────────────────────
    def checkpoint_dict(self, include_obs: bool = True) -> Dict[str, Any]:
        tok = self.save_state()
        ck: Dict[str, Any] = {
            "cora_checkpoint": CHECKPOINT_VERSION,
            "seed": tok.seed,
            "reset_count": tok.reset_count,
            "round": tok.depth,
            "calls": [dict(c) for c in tok.calls],
            "conversation": copy.deepcopy(self.conversation),
            "meta": copy.deepcopy(self.meta),
        }
        if include_obs:
            gs = self._game_state_dict()
            ck["score"] = self.score()
            ck["obs"] = {
                "day": (gs.get("sessionInfo") or {}).get("currentDay"),
                "round": (gs.get("sessionInfo") or {}).get("currentRound"),
                "budget": (gs.get("satisfactionAndBudget") or {}).get("budget"),
                "satisfaction": (gs.get("satisfactionAndBudget") or {}).get("satisfaction"),
                "tasks": gs.get("allActiveTasks"),
                "facilities": (gs.get("mapState") or {}).get("facilities"),
            }
        return ck

    def save_checkpoint(self, path: str, include_obs: bool = True) -> str:
        os.makedirs(os.path.dirname(os.path.abspath(path)) or ".", exist_ok=True)
        with open(path, "w") as fh:
            json.dump(self.checkpoint_dict(include_obs), fh, indent=2, default=str)
        return path

    def load_checkpoint(self, path: str) -> Dict[str, Any]:
        with open(path) as fh:
            ck = json.load(fh)
        if ck.get("cora_checkpoint") != CHECKPOINT_VERSION:
            raise ValueError(f"unsupported checkpoint version {ck.get('cora_checkpoint')}")
        if ck.get("seed") != self.seed_value:
            raise ValueError(
                f"checkpoint seed {ck.get('seed')} != env seed {self.seed_value}. Construct "
                f"the env with seed={ck.get('seed')} to replay this checkpoint.")
        self.load_state(StateToken(seed=ck["seed"], reset_count=ck["reset_count"],
                                   calls=tuple(ck["calls"])))
        # The conversation comes back too, so a model resumes with the same context it had.
        self.conversation = copy.deepcopy(ck.get("conversation") or [])
        self.meta = copy.deepcopy(ck.get("meta") or {})
        return ck

    # ── convenience for callers ──────────────────────────────────────────────────────
    def advance_round(self):
        return self._send_request({"type": "advance_time"})

    def execute(self, action_json: str):
        return self._send_request({"type": "execute_action", "action": action_json})

    def choose(self, task_id: int, choice_id: int):
        return self._send_request({"type": "select_task_choice",
                                   "taskId": task_id, "choiceId": choice_id})

    def _game_state_dict(self) -> Dict[str, Any]:
        resp = self._send_request({"type": "get_game_state"})
        payload = resp.get("game_state")
        return json.loads(payload) if isinstance(payload, str) else (payload or {})

    def components(self) -> Dict[str, float]:
        rm = self._game_state_dict().get("rewardMetrics") or {}
        return reward_scoring.compute_score_components(rm)

    def score(self) -> float:
        """Normalised score (raw / 4), the scale every figure and the deck report."""
        return float(self.components().get("score", 0.0)) / 4.0
