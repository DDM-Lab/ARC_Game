"""GameEnv: the game as a Gymnasium environment over the Unity gym server's TCP protocol.

One step is one human decision point (see docs/ARCHITECTURE.md, turn contract): the actions run,
then the game advances to the next decision. A full game is 36 steps.

    observation  the game state dict Unity exports (GameStatePayload)
    action       indices into get_valid_actions() (cora.actions' menu for this state), as a list
                 or a comma-separated string ("5,12,3"); empty = no game action this step.
                 Task answers are not menu actions: answer them with select_task_choice() first.
                 Turning a model's tool calls into both is cora.executor's job.
    reward       the change in score this step (cora.scoring), so an episode's rewards sum to
                 the final score
    terminated   the game is over (finalDay complete)
    truncated    max_episode_steps reached

Protocol: one JSON object per line each way; request() sends any request type the gym server
understands (get_game_state, execute_action, select_task_choice, advance_time, reset_game,
configure_render, map_grid, pathfind_matrix, ...).
"""
from __future__ import annotations

import atexit
import json
import socket
import string
import time
from typing import Any, Dict, List, Optional, Tuple

import gymnasium as gym

from cora.actions import enumerate_actions
from cora.env import unity_process
from cora.scoring import COMPONENTS, score_components


class GameEnv(gym.Env):
    metadata = {"render_modes": []}

    def __init__(
        self,
        unity_exe_path: Optional[str] = None,
        unity_port: int = 9876,
        max_episode_steps: int = 100,
        param_config: Optional[str] = None,
        map_config: Optional[str] = None,
        auto_start_unity: bool = True,
        connection_timeout: float = 30.0,
        unity_log_path: Optional[str] = None,
        frame_capture: str = "off",
        frame_dir: Optional[str] = None,
        frame_resolution: Tuple[int, int] = (640, 360),
        frame_include_base64: bool = False,
        manual_transfers: bool = True,
        seed: Optional[int] = None,
    ):
        """
        unity_exe_path      headless build to launch (None: connect to a running server)
        unity_port          the gym server's TCP port
        max_episode_steps   truncate after this many steps
        param_config        parameter CSV for this Unity process (ARC_PARAM_CONFIG); default:
                            the build's bundled CSV
        map_config          map JSON path, or "none" for the scene's built-in layout
                            (ARC_MAP_CONFIG); default: the build's pinned map
        unity_log_path      Unity's log file (default: discarded)
        frame_capture       real camera frames into info["frame_path"]: "off" | "step" (every
                            step) | "game_time" (when the day/round changes). Needs the
                            render-capable build (HeadlessBuildScript.BuildMacOSRender).
        frame_dir, frame_resolution, frame_include_base64   capture output options
        manual_transfers    offer standalone resource transfers in the menu. False is the
                            human GUI's rule: transfers happen only through task choices.
        seed                scenario seed: the launch uses it, and each later reset uses
                            seed + reset count, so a run is a reproducible sequence of scenarios
        """
        super().__init__()
        self.max_episode_steps = max_episode_steps
        self.unity_port = unity_port
        self.connection_timeout = connection_timeout
        self.frame_capture = (frame_capture or "off").lower()
        self.frame_dir = frame_dir
        self.frame_resolution = frame_resolution
        self.frame_include_base64 = frame_include_base64
        self.manual_transfers = manual_transfers
        self.seed_value = seed

        self.game_state: Optional[dict] = None
        self.valid_actions: List[dict] = []
        self.current_step = 0
        self.previous_satisfaction = 0.0
        self.previous_score = 0.0
        self.last_choice_error: Optional[str] = None
        self.active_seed = -1
        self._reset_count = 0
        self.sock: Optional[socket.socket] = None

        self.observation_space = gym.spaces.Dict({})
        self.action_space = gym.spaces.Text(max_length=1024, charset=string.digits + ",")

        self.unity_process = None
        if auto_start_unity and unity_exe_path:
            self.unity_process = unity_process.launch(
                unity_exe_path, unity_port, seed=seed, log_path=unity_log_path,
                graphics=self.frame_capture != "off", param_config=param_config, map_config=map_config)
        self._connect()
        self._configure_render()
        atexit.register(self.close)

    # ── protocol ──
    def _connect(self):
        deadline, last_error = time.time() + self.connection_timeout, None
        while time.time() < deadline:
            try:
                self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                self.sock.settimeout(5.0)
                self.sock.connect(("localhost", self.unity_port))
                # A step simulates a whole round server-side; leave headroom for heavy delivery
                # rounds under multi-worker CPU contention.
                self.sock.settimeout(120.0)
                return
            except OSError as e:
                last_error = e
                self.sock.close()
                self.sock = None
                time.sleep(1)
        raise ConnectionError(f"no Unity gym server on port {self.unity_port} after "
                              f"{self.connection_timeout}s: {last_error}")

    def request(self, payload: dict) -> dict:
        """Send one request to the gym server and return its response."""
        if self.sock is None:
            raise ConnectionError("not connected to the Unity gym server")
        try:
            self.sock.sendall((json.dumps(payload) + "\n").encode("utf-8"))
            buf = b""
            while not buf.endswith(b"\n"):
                chunk = self.sock.recv(4096)
                if not chunk:
                    raise ConnectionError("Unity closed the connection")
                buf += chunk
            response = json.loads(buf.decode("utf-8").strip())
        except socket.timeout:
            raise TimeoutError("timed out waiting for Unity")
        except json.JSONDecodeError as e:
            raise RuntimeError(f"invalid JSON from Unity: {e}")
        if response.get("type") == "error":
            raise RuntimeError(f"Unity error: {response.get('error', 'unknown')}")
        return response

    def _configure_render(self):
        if self.frame_capture == "off" and not self.frame_dir:
            return
        w, h = self.frame_resolution
        try:
            self.request({"type": "configure_render", "renderMode": self.frame_capture,
                          "renderWidth": int(w), "renderHeight": int(h),
                          "renderDir": self.frame_dir or "",
                          "renderIncludeBase64": bool(self.frame_include_base64)})
        except Exception as e:      # builds without the handler cannot capture; text mode still works
            print(f"⚠️  configure_render failed (frame capture may be unavailable): {e}")

    def _read_state(self, response: dict) -> dict:
        self.game_state = json.loads(response.get("game_state", "{}"))
        actions = enumerate_actions(self.game_state)
        if not self.manual_transfers:
            actions = [a for a in actions if a.get("action_type") != "resource_transfer"]
        self.valid_actions = actions
        return self.game_state

    # ── gym API ──
    def reset(self, seed: Optional[int] = None, options: Optional[Dict[str, Any]] = None):
        """Start an episode. The first reset uses the freshly launched game; later resets reload
        the scene in-process (reset_game), with seed + reset count when a seed is set."""
        super().reset(seed=seed)
        self.current_step = 0
        if seed is not None:
            self.seed_value = seed
        if self._reset_count > 0:
            req = {"type": "reset_game"}
            if self.seed_value is not None:
                req["seed"] = int(self.seed_value) + self._reset_count
            resp = self.request(req)
            if resp.get("type") != "reset_done":
                raise RuntimeError(f"expected reset_done from reset_game, got {resp.get('type')}")
            self.active_seed = resp.get("seed", -1)
        else:
            self.active_seed = int(self.seed_value) if self.seed_value is not None else -1
        self._reset_count += 1

        resp = self.request({"type": "get_game_state"})
        if resp.get("type") != "game_state":
            raise RuntimeError(f"expected game_state, got {resp.get('type')}")
        self._read_state(resp)
        sab = self.game_state.get("satisfactionAndBudget", {})
        self.previous_satisfaction = float(sab.get("satisfaction", 0.0))
        self.previous_score = score_components(self.game_state.get("rewardMetrics"))["score"]
        session = self.game_state.get("sessionInfo", {})
        comps = score_components(self.game_state.get("rewardMetrics"))
        return self.game_state, {
            "day": session.get("currentDay", 1), "round": session.get("currentRound", 0),
            "budget": sab.get("budget", 0.0), "satisfaction": self.previous_satisfaction,
            "metrics": _metrics(self.previous_satisfaction, sab, 0.0, 0.0, comps),
            "valid_action_count": len(self.valid_actions), "step": 0}

    def step(self, action):
        """Run the given menu actions in order (stopping at the first one the game refuses), then
        advance to the next decision point."""
        self.current_step += 1
        parts = action if isinstance(action, (list, tuple)) else \
            [p for p in str(action or "").split(",") if p.strip()]
        to_run = [self.valid_actions[int(p)] for p in parts if 0 <= int(p) < len(self.valid_actions)]

        executed, results = [], []
        for a in to_run:
            resp = self.request({"type": "execute_action", "action": json.dumps(a)})
            if resp.get("type") != "action_result":
                print(f"❌ unexpected response to execute_action: {resp.get('type')}")
                break
            results.append(resp)
            if not resp.get("success"):
                print(f"❌ Action failed: {resp.get('error', 'unknown error')}")
                break
            executed.append(a)

        # advance_time returns "game_over" (with the unchanged terminal state) when the game
        # has already ended; both carry a valid state.
        resp = self.request({"type": "advance_time"})
        if resp.get("type") not in ("game_state", "game_over"):
            raise RuntimeError(f"advance_time failed: {resp.get('type')}")
        self._read_state(resp)

        sab = self.game_state.get("satisfactionAndBudget", {})
        satisfaction = float(sab.get("satisfaction", 0.0))
        satisfaction_delta, self.previous_satisfaction = satisfaction - self.previous_satisfaction, satisfaction
        comps = score_components(self.game_state.get("rewardMetrics"))
        reward, self.previous_score = comps["score"] - self.previous_score, comps["score"]

        session = self.game_state.get("sessionInfo", {})
        terminated = bool(session.get("isGameOver", False))
        truncated = self.current_step >= self.max_episode_steps
        info = {
            "day": session.get("currentDay", 1), "round": session.get("currentRound", 0),
            "final_day": session.get("finalDay", 0), "game_over": terminated,
            "budget": sab.get("budget", 0.0), "satisfaction": satisfaction,
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
        for key in ("frame_path", "frame_base64"):
            if resp.get(key):
                info[key] = resp[key]
        return self.game_state, reward, terminated, truncated, info

    def select_task_choice(self, task_id: int, choice_id: int) -> bool:
        """Answer a choice task the way the UI does (impacts + delivery). On refusal the game's
        reason is kept in last_choice_error (no meals, no shelter space, task already gone)."""
        resp = self.request({"type": "select_task_choice", "taskId": int(task_id), "choiceId": int(choice_id)})
        ok = bool(resp.get("success", False))
        self.last_choice_error = None if ok else (resp.get("error") or "refused (no reason given)")
        return ok

    def get_valid_actions(self) -> List[dict]:
        return self.valid_actions

    def close(self):
        if self.sock is not None:
            try:
                self.sock.close()
            except OSError:
                pass
            self.sock = None
        unity_process.stop(self.unity_process)
        self.unity_process = None


def _metrics(satisfaction, sab, satisfaction_delta, reward, comps) -> dict:
    """Flat game/* scalars for WandB, the same keys at reset and on every step: Verlog averages
    info["metrics"] over a rollout and the benchmark logs the same keys, so RL and benchmark runs
    overlay directly."""
    return {
        "game/satisfaction": satisfaction,
        "game/budget": float(sab.get("budget", 0.0)),
        "game/satisfaction_delta": satisfaction_delta,
        "game/reward": reward,
        "game/satisfaction_score": comps["satisfaction"],
        **{f"game/{k}": comps[k] for k in COMPONENTS if k != "satisfaction"},
    }
