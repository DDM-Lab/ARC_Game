"""SimEnv: the surrogate behind cora.env.GameEnv's interface.

Anything that plays Unity through GameEnv -- cora.executor.execute_turn, rl.CoraEnv's step, the
bench baselines, bench.baselines.explore -- plays the surrogate unchanged, ~10^4 times faster:

    env = SimEnv(seed=5503)
    env.reset()
    while True:
        calls = tool_calls(env, POLICIES["combined"](env, i, DECISIONS))
        results, (_, reward, terminated, truncated, info) = executor.execute_turn(env, calls)
        if terminated or truncated:
            break

The state handed out is oracle.sim.export.game_state (Unity's get_game_state payload), the menu
is cora.actions.enumerate_actions on it, task answers go through sim.answer and menu actions
through sim.apply_menu_action, and a step ends at the next decision (sim.step) -- the same
decision points, 36 a game, as the headless build.
"""
from __future__ import annotations

from typing import Any, Dict, List, Optional

from cora import actions as _menu
from cora.env.game import _metrics            # the same game/* keys as the Unity env
from cora.scoring import score_components

import oracle.sim.sim as S
from .export import game_state
from .floodmap import FloodMap
from .rng import game_start


_FMAP = None


def _flood_map() -> FloodMap:
    """The terrain is immutable and shared (World.clone shares it too); load it once."""
    global _FMAP
    if _FMAP is None:
        _FMAP = FloodMap.load()
    return _FMAP


class SimEnv:
    def __init__(self, seed: Optional[int] = None, max_episode_steps: int = 100,
                 manual_transfers: bool = True, **_ignored):
        """seed: the Unity seed to play (-seed / ARC_SEED); reset(seed) overrides it.
        Unity-only GameEnv options (exe path, port, logs, frame capture) are accepted and ignored."""
        self.seed_value = seed
        self.max_episode_steps = max_episode_steps
        self.manual_transfers = manual_transfers
        self.world = None
        self.game_state: Dict[str, Any] = {}
        self.valid_actions: List[dict] = []
        self.current_step = 0
        self.previous_satisfaction = 0.0
        self.previous_score = 0.0
        self.last_choice_error = None
        self.active_seed = -1
        self._fmap = _flood_map()

    # ── state ──
    def _read_state(self) -> dict:
        self.game_state = game_state(self.world)
        self.game_state["scenario"]["seed"] = self.active_seed
        gs = self.game_state
        if self.manual_transfers:
            self.valid_actions = _menu.enumerate_actions(gs)
        else:
            # enumerate_actions minus its transfers, without building them only to drop them:
            # the same generators in the same order (they are most of a decision's cost).
            self.valid_actions = [*_menu._construction(gs), *_menu._worker(gs),
                                  *_menu._assignments(gs), *_menu._deconstructions(gs)]
        return self.game_state

    def get_valid_actions(self) -> List[dict]:
        return self.valid_actions

    # ── gym API ──
    def reset(self, seed: Optional[int] = None, options: Optional[Dict[str, Any]] = None):
        if seed is not None:
            self.seed_value = seed
        if self.seed_value is None:
            raise ValueError("SimEnv needs a seed (the surrogate has no unseeded mode)")
        self.active_seed = int(self.seed_value)
        self.world = S.new_world(game_start(self.active_seed).get_state(), self._fmap)
        self.current_step = 0
        self._read_state()
        sab = self.game_state["satisfactionAndBudget"]
        self.previous_satisfaction = float(sab["satisfaction"])
        self.previous_score = score_components(self.game_state.get("rewardMetrics"))["score"]
        session = self.game_state["sessionInfo"]
        return self.game_state, {
            "day": session["currentDay"], "round": session["currentRound"],
            "budget": sab["budget"], "satisfaction": self.previous_satisfaction,
            "metrics": _metrics(self.previous_satisfaction, sab, 0.0, 0.0,
                                score_components(self.game_state.get("rewardMetrics"))),
            "valid_action_count": len(self.valid_actions), "step": 0}

    def select_task_choice(self, task_id: int, choice_id: int) -> bool:
        ok = bool(S.answer(self.world, int(task_id), int(choice_id)))
        self.last_choice_error = None if ok else "refused by the surrogate (no route, room, stock or task)"
        return ok                     # like GameEnv: the state is re-read when the step ends

    def step(self, action):
        """Run the given menu actions in order (stopping at the first one refused), then advance
        to the next decision point -- GameEnv.step's contract."""
        self.current_step += 1
        parts = action if isinstance(action, (list, tuple)) else \
            [p for p in str(action or "").split(",") if p.strip()]
        to_run = [self.valid_actions[int(p)] for p in parts if 0 <= int(p) < len(self.valid_actions)]
        executed, results = [], []
        for a in to_run:
            ok = bool(S.apply_menu_action(self.world, a))
            results.append({"type": "action_result", "success": ok,
                            "error": "" if ok else "refused by the surrogate"})
            if not ok:
                break
            executed.append(a)
        S.step(self.world)
        self._read_state()

        sab = self.game_state["satisfactionAndBudget"]
        satisfaction = float(sab["satisfaction"])
        satisfaction_delta, self.previous_satisfaction = satisfaction - self.previous_satisfaction, satisfaction
        comps = score_components(self.game_state.get("rewardMetrics"))
        reward, self.previous_score = comps["score"] - self.previous_score, comps["score"]
        session = self.game_state["sessionInfo"]
        terminated = bool(session["isGameOver"])
        truncated = self.current_step >= self.max_episode_steps
        info = {
            "day": session["currentDay"], "round": session["currentRound"],
            "final_day": session["finalDay"], "game_over": terminated,
            "budget": sab["budget"], "satisfaction": satisfaction,
            "satisfaction_delta": satisfaction_delta, "reward": reward, "score": comps["score"],
            "satisfaction_score": comps["satisfaction"], "efficiency": comps["efficiency"],
            "score_components": comps,
            "metrics": _metrics(satisfaction, sab, satisfaction_delta, reward, comps),
            "reward_metrics": self.game_state.get("rewardMetrics"),
            "executed_actions": [a.get("description", "") for a in executed],
            "execution_results": results,
            "valid_action_count": len(self.valid_actions),
            "step": self.current_step,
        }
        return self.game_state, reward, terminated, truncated, info

    def clone(self) -> "SimEnv":
        """An independent copy at the same decision, for search to branch from: the world is
        cloned (World.clone); the state and menu are shared until the next step re-reads them,
        which is safe because both are replaced, never mutated, by step()."""
        e = SimEnv.__new__(SimEnv)
        e.__dict__.update(self.__dict__)
        e.world = self.world.clone()
        return e

    def request(self, payload: dict) -> dict:
        """The gym protocol's reads, for callers that use it directly."""
        kind = (payload or {}).get("type")
        if kind == "get_game_state":
            import json
            return {"type": "game_state", "game_state": json.dumps(self.game_state)}
        return {"type": "error", "error": f"SimEnv does not serve {kind!r}"}

    def close(self):
        self.world = None

