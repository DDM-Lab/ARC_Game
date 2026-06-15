"""
ARC Game Gymnasium Environment - TCP Socket implementation

This environment connects to Unity's GymServerManager via TCP socket.
Simpler than WebSocket, uses JSON-over-TCP protocol.

Architecture:
    Python Gymnasium Env
        ↓ TCP Socket
    Unity GymServerManager (TCP Server on port 9876)
        ↓
    Unity Game Systems (ActionExecutor, TaskSystem, etc.)

Observation: Full game state dict (GameStatePayload from Unity)
Action: CSV string of action indexes (e.g., "5,12,3") or single int
Reward: Satisfaction delta from previous step
Termination: satisfaction <= 0 OR max_episode_steps reached

Requirements:
    pip install gymnasium numpy

Usage:
    env = ARCGameGymEnv(
        unity_exe_path="Build/Headless/Windows/ARC_Headless.exe",
        unity_port=9876,
        max_episode_steps=100
    )
    obs, info = env.reset()
    obs, reward, terminated, truncated, info = env.step("5")
"""

import gymnasium as gym
import numpy as np
from typing import Dict, Tuple, Any, Optional, List
from pathlib import Path
import sys
import json
import subprocess
import time
import atexit
import socket

# Import action enumerator
sys.path.append(str(Path(__file__).parent))
from action_enumerator import ActionEnumerator


# ── Reward weights (TUNE THESE) ───────────────────────────────────────────────
# All scoring lives here in Python so it can be retuned without a Unity rebuild.
# Satisfaction is higher-better; Cost-Efficiency is lower-better and is SUBTRACTED.
REWARD_WEIGHTS = {
    # Satisfaction (needs-met ratios are clamped to [0,1])
    "w_food": 1.0,
    "w_lodging": 1.0,
    "w_workeruse": 1.0,
    # Worker-use blend (utilization > training > idle; idle = 0 per design)
    "w_working": 1.0,
    "w_training": 0.5,
    "w_idle": 0.0,
    # Cost-efficiency ($ per unit service). Small weights bring $/service into the
    # same scale as satisfaction; each cost term is capped (NOT clamped to 1).
    "w_food_cost": 0.0002,
    "w_lodging_cost": 0.0002,
    "w_worker_cost": 0.0002,
    "cost_term_cap": 1.0,   # max contribution of any single cost term
}


def _clamp01(x: float) -> float:
    return 0.0 if x < 0 else (1.0 if x > 1.0 else x)


def compute_score_components(rm: dict, w: dict = REWARD_WEIGHTS) -> dict:
    """Full breakdown of the composite reward from Unity's rewardMetrics.

    Returns every term so each can be logged/graphed independently:
      satisfaction sub-terms: sat_food, sat_lodging, sat_worker_use
      cost-efficiency sub-terms: cost_food, cost_lodging, cost_worker
      aggregates: satisfaction, cost_efficiency, score (= satisfaction - cost_efficiency)
    The per-step reward is the delta of `score` between rounds (telescopes to the
    final score). All values are cumulative-to-date (so deltas are per-round).
    """
    keys = ["sat_food", "sat_lodging", "sat_worker_use",
            "cost_food", "cost_lodging", "cost_worker",
            "satisfaction", "cost_efficiency", "score"]
    if not rm:
        return {k: 0.0 for k in keys}

    def ratio(num, den):
        return (num / den) if den else 0.0

    # ── Satisfaction (higher better) ──
    food = _clamp01(ratio(rm.get("foodFulfilled", 0), rm.get("foodResolved", 0))) * w["w_food"]
    lodging = _clamp01(ratio(rm.get("lodgingFulfilled", 0), rm.get("lodgingResolved", 0))) * w["w_lodging"]

    days = max(rm.get("daysCompleted", 1), 1)
    total_workers = max(rm.get("totalWorkers", 0), 1)
    worker_capacity = days * total_workers
    worker_use = _clamp01(
        (w["w_working"] * rm.get("cumWorkingWorkers", 0)
         + w["w_training"] * rm.get("cumTrainingWorkers", 0)
         + w["w_idle"] * rm.get("cumIdleWorkers", 0)) / worker_capacity
    ) * w["w_workeruse"]

    satisfaction = food + lodging + worker_use

    # ── Cost-efficiency (lower better; capped, not clamped-to-1) ──
    def cost_term(spend, service, weight):
        # service==0 with spend>0 => maximally inefficient => hits the cap.
        val = (spend / max(service, 1)) * weight
        return min(val, w["cost_term_cap"])

    c_food = cost_term(rm.get("foodSpend", 0), rm.get("foodFulfilled", 0), w["w_food_cost"])
    c_lodging = cost_term(rm.get("lodgingSpend", 0), rm.get("lodgingFulfilled", 0), w["w_lodging_cost"])
    c_worker = cost_term(rm.get("workerSpend", 0), rm.get("cumWorkingWorkers", 0), w["w_worker_cost"])
    cost_efficiency = c_food + c_lodging + c_worker

    return {
        "sat_food": food, "sat_lodging": lodging, "sat_worker_use": worker_use,
        "cost_food": c_food, "cost_lodging": c_lodging, "cost_worker": c_worker,
        "satisfaction": satisfaction, "cost_efficiency": cost_efficiency,
        "score": satisfaction - cost_efficiency,
    }


def compute_score(rm: dict, w: dict = REWARD_WEIGHTS):
    """Backward-compatible: (satisfaction, cost_efficiency, score)."""
    c = compute_score_components(rm, w)
    return c["satisfaction"], c["cost_efficiency"], c["score"]


class ARCGameGymEnv(gym.Env):
    """
    Gymnasium environment for ARC Game using TCP socket communication

    Connects to Unity's GymServerManager and communicates via simple JSON protocol.
    """

    metadata = {"render_modes": ["human", "ansi"], "render_fps": 4}

    def __init__(
        self,
        unity_exe_path: Optional[str] = None,
        unity_port: int = 9876,
        max_days: int = 30,
        max_episode_steps: int = 100,
        render_mode: Optional[str] = None,
        auto_start_unity: bool = True,
        connection_timeout: float = 30.0,
        unity_log_path: Optional[str] = None
    ):
        """
        Initialize the ARC Game Gym Environment

        Args:
            unity_exe_path: Path to Unity headless executable (None = connect to existing instance)
            unity_port: TCP port for Unity gym server
            max_days: Maximum days before truncation (Unity-side limit)
            max_episode_steps: Maximum steps before truncation (gym-side limit)
            render_mode: Render mode ('human', 'ansi', or None)
            auto_start_unity: Whether to automatically start Unity process
            connection_timeout: Seconds to wait for Unity connection
            unity_log_path: If set, Unity writes its log to this file; otherwise the
                log is discarded. Either way the process's stdout/stderr go to
                DEVNULL so the (unread) pipe can never fill and deadlock Unity.
        """
        super().__init__()

        self.render_mode = render_mode
        self.max_days = max_days
        self.max_episode_steps = max_episode_steps
        self.unity_port = unity_port
        self.connection_timeout = connection_timeout

        # Unity process management
        self.unity_process = None
        self.unity_exe_path = unity_exe_path
        self.auto_start_unity = auto_start_unity
        self.unity_log_path = unity_log_path

        # TCP socket connection
        self.sock = None
        self.connected = False

        # Game state tracking
        self.previous_satisfaction = 50.0  # Default starting satisfaction
        self.game_state = None
        self.valid_actions = []
        self.action_enumerator = None
        self.current_step = 0
        self.current_round = 0

        # Define spaces
        self.observation_space = gym.spaces.Dict({})  # Flexible dict
        self.action_space = gym.spaces.Text(max_length=100, charset="0123456789,")

        # Connect to Unity
        if auto_start_unity and unity_exe_path:
            self._start_unity_process()

        self._connect_socket()

        # Register cleanup
        atexit.register(self.close)

    def _start_unity_process(self):
        """Start Unity headless build process with gym server mode"""
        if not self.unity_exe_path:
            raise ValueError("unity_exe_path required when auto_start_unity=True")

        unity_path = Path(self.unity_exe_path)
        if not unity_path.exists():
            raise FileNotFoundError(f"Unity executable not found: {unity_path}")

        print(f"🎮 Starting Unity with Gym Server...")
        print(f"   Executable: {unity_path}")
        print(f"   Port: {self.unity_port}")

        try:
            # Unity logs voluminously (e.g. per-frame TMP warnings). Route the log to
            # a file (or discard it), and send the process stdout/stderr to DEVNULL.
            # NEVER use an unread subprocess.PIPE here: Unity blocks once the ~64KB
            # OS pipe buffer fills, which freezes its main thread mid-episode and the
            # gym times out (looks like a day-transition hang). Writing to -logFile
            # decouples logging from the pipe entirely.
            log_arg = self.unity_log_path if self.unity_log_path else "-"
            self.unity_process = subprocess.Popen(
                [
                    str(unity_path),
                    "-batchmode",
                    "-nographics",
                    "-gym-server",  # Enable gym server mode
                    "-gym-port", str(self.unity_port),
                    "-logFile", log_arg,
                ],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                stdin=subprocess.DEVNULL
            )
            print(f"✅ Unity process started (PID: {self.unity_process.pid})")
            print(f"   Waiting for gym server to initialize...")

            # Give Unity time to initialize
            time.sleep(8)

        except Exception as e:
            raise RuntimeError(f"Failed to start Unity process: {e}")

    def _connect_socket(self):
        """Connect to Unity gym server via TCP socket"""
        print(f"🔌 Connecting to Unity Gym Server...")
        print(f"   Host: localhost:{self.unity_port}")

        start_time = time.time()
        last_error = None

        while time.time() - start_time < self.connection_timeout:
            try:
                self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                self.sock.settimeout(5.0)
                self.sock.connect(("localhost", self.unity_port))
                self.connected = True
                # Raise the per-request timeout now that we're connected: a step
                # advances a full simulation round server-side. Generous headroom for
                # heavy delivery rounds under multi-worker CPU contention.
                self.sock.settimeout(120.0)
                print(f"✅ Connected to Unity Gym Server")
                return

            except (ConnectionRefusedError, socket.timeout, OSError) as e:
                last_error = str(e)
                if self.sock:
                    self.sock.close()
                    self.sock = None
                time.sleep(1)

        raise ConnectionError(
            f"Failed to connect to Unity gym server after {self.connection_timeout}s: {last_error}"
        )

    def _send_request(self, request_dict: dict) -> dict:
        """Send request to Unity and wait for response"""
        if not self.sock or not self.connected:
            raise ConnectionError("Not connected to Unity gym server")

        try:
            # Send request as JSON + newline
            request_json = json.dumps(request_dict)
            self.sock.sendall((request_json + "\n").encode('utf-8'))

            # Receive response (read until newline)
            response_bytes = b''
            while not response_bytes.endswith(b'\n'):
                chunk = self.sock.recv(4096)
                if not chunk:
                    raise ConnectionError("Unity closed connection")
                response_bytes += chunk

            # Parse response
            response_json = response_bytes.decode('utf-8').strip()
            response = json.loads(response_json)

            # Check for errors
            if response.get('type') == 'error':
                raise RuntimeError(f"Unity error: {response.get('error', 'Unknown error')}")

            return response

        except socket.timeout:
            raise TimeoutError("Timeout waiting for Unity response")
        except json.JSONDecodeError as e:
            raise RuntimeError(f"Invalid JSON from Unity: {e}")

    def reset(
        self,
        seed: Optional[int] = None,
        options: Optional[Dict[str, Any]] = None
    ) -> Tuple[Dict[str, Any], Dict[str, Any]]:
        """
        Reset environment to initial state

        Returns:
            observation: Game state dict from Unity
            info: Additional information
        """
        super().reset(seed=seed)

        # Reset step counter
        self.current_step = 0
        self.current_round = 0

        # Request game state from Unity
        response = self._send_request({"type": "get_game_state"})

        if response.get("type") != "game_state":
            raise RuntimeError(f"Expected game_state, got: {response.get('type')}")

        # Parse game state (it's nested JSON)
        game_state_json = response.get("game_state", "{}")
        self.game_state = json.loads(game_state_json)

        # Extract satisfaction
        sat_budget = self.game_state.get("satisfactionAndBudget", {})
        self.previous_satisfaction = float(sat_budget.get("satisfaction", 50.0))

        # Composite reward baseline (Satisfaction - CostEfficiency); reward is its delta.
        _, _, self.previous_score = compute_score(self.game_state.get("rewardMetrics") or {})

        # Enumerate valid actions
        self.action_enumerator = ActionEnumerator(self.game_state)
        self.valid_actions = self.action_enumerator.enumerate_all_actions()

        # Build info
        session = self.game_state.get("sessionInfo", {})
        info = {
            "day": session.get("currentDay", 1),
            "round": session.get("currentRound", 0),
            "segment": session.get("currentTimeSegment", 0),
            "budget": sat_budget.get("budget", 10000.0),
            "satisfaction": self.previous_satisfaction,
            "valid_action_count": len(self.valid_actions),
            "step": self.current_step
        }

        return self.game_state, info

    def step(
        self, action: str
    ) -> Tuple[Dict[str, Any], float, bool, bool, Dict[str, Any]]:
        """
        Execute action(s) and get next state

        Args:
            action: CSV string of action indexes (e.g., "5,12,3") or single int

        Returns:
            observation: New game state from Unity
            reward: Satisfaction delta
            terminated: Whether episode ended (satisfaction <= 0)
            truncated: Whether episode was cut short (max steps)
            info: Additional information
        """
        self.current_step += 1

        # Parse action string
        action_indexes = self._parse_action_string(action)

        if not action_indexes:
            print("⚠️  No valid action indexes, executing no-op (action 0)")
            action_indexes = [0] if self.valid_actions else []

        # Execute actions via Unity
        executed_actions = []
        execution_results = []

        for idx in action_indexes:
            if idx < 0 or idx >= len(self.valid_actions):
                print(f"⚠️  Invalid action index: {idx} (valid range: 0-{len(self.valid_actions)-1})")
                continue

            action_dict = self.valid_actions[idx]

            # Send execute_action request
            response = self._send_request({
                "type": "execute_action",
                "action": json.dumps(action_dict)  # Nested JSON
            })

            if response.get("type") == "action_result":
                if response.get("success"):
                    executed_actions.append(action_dict)
                    execution_results.append(response)
                else:
                    error_msg = response.get("error", "Unknown error")
                    print(f"❌ Action failed: {error_msg}")
                    execution_results.append(response)
                    break  # Stop on failure
            else:
                print(f"❌ Unexpected response type: {response.get('type')}")
                break

        # Advance the simulation by one round. All actions for this turn have been
        # executed above; advancing runs the round's dynamics (construction
        # completion, deliveries, demand, satisfaction) decoupled from real time,
        # and returns the post-advance game state.
        response = self._send_request({"type": "advance_time"})

        if response.get("type") != "game_state":
            raise RuntimeError("Failed to advance simulation / get game state")

        game_state_json = response.get("game_state", "{}")
        self.game_state = json.loads(game_state_json)
        self.current_round += 1

        # Composite reward: per-step delta of (Satisfaction - CostEfficiency),
        # computed in Python from Unity's raw rewardMetrics (telescopes to the
        # final score over the episode). Raw satisfaction delta kept in info.
        sat_budget = self.game_state.get("satisfactionAndBudget", {})
        current_satisfaction = float(sat_budget.get("satisfaction", 0.0))
        satisfaction_delta = current_satisfaction - self.previous_satisfaction
        self.previous_satisfaction = current_satisfaction

        comps = compute_score_components(self.game_state.get("rewardMetrics") or {})
        satisfaction_score, cost_efficiency, score = comps["satisfaction"], comps["cost_efficiency"], comps["score"]
        reward = score - getattr(self, "previous_score", 0.0)
        self.previous_score = score

        # Re-enumerate valid actions
        self.action_enumerator = ActionEnumerator(self.game_state)
        self.valid_actions = self.action_enumerator.enumerate_all_actions()

        # Check termination conditions
        terminated = current_satisfaction <= 0
        truncated = self.current_step >= self.max_episode_steps

        # Build info
        session = self.game_state.get("sessionInfo", {})
        info = {
            "day": session.get("currentDay", 1),
            "round": session.get("currentRound", 0),
            "segment": session.get("currentTimeSegment", 0),
            "budget": sat_budget.get("budget", 0.0),
            "satisfaction": current_satisfaction,
            "satisfaction_delta": satisfaction_delta,
            "reward": reward,
            "score": score,
            "satisfaction_score": satisfaction_score,
            "cost_efficiency": cost_efficiency,
            "score_components": comps,   # sat_food/sat_lodging/sat_worker_use/cost_food/cost_lodging/cost_worker
            "reward_metrics": self.game_state.get("rewardMetrics"),
            "executed_actions": [a.get("description", "") for a in executed_actions],
            "execution_results": execution_results,
            "valid_action_count": len(self.valid_actions),
            "step": self.current_step
        }

        return self.game_state, reward, terminated, truncated, info

    def _parse_action_string(self, action_str: str) -> List[int]:
        """Parse CSV action string to list of integers"""
        import re

        # Convert to string if it's an int
        if isinstance(action_str, int):
            return [action_str]

        action_str = str(action_str).strip()
        numbers = re.findall(r'\b\d+\b', action_str)

        try:
            return [int(n) for n in numbers]
        except ValueError:
            print(f"⚠️  Failed to parse action: '{action_str}'")
            return []

    def select_task_choice(self, task_id: int, choice_id: int) -> bool:
        """Answer a choice task: select choice_id on task_id and complete it.

        This is the decision lever for choice tasks (e.g. Food Requests), applying
        the choice's impacts + delivery via the same path the UI uses. Distinct
        from the construction/worker/transfer GameActions in step()'s action list.
        Returns True on success.
        """
        response = self._send_request({
            "type": "select_task_choice",
            "taskId": int(task_id),
            "choiceId": int(choice_id),
        })
        return bool(response.get("success", False))

    def get_valid_actions(self) -> List[Dict[str, Any]]:
        """Get list of currently valid actions"""
        return self.valid_actions

    def get_action_descriptions(self) -> List[str]:
        """Get human-readable action descriptions"""
        return [f"{i}. {action.get('description', 'Unknown')} (Cost: ${action.get('cost', 0)})"
                for i, action in enumerate(self.valid_actions)]

    def render(self):
        """Render current state"""
        if self.render_mode in ["ansi", "human"] and self.game_state:
            session = self.game_state.get("sessionInfo", {})
            sat_budget = self.game_state.get("satisfactionAndBudget", {})

            print("\n" + "="*80)
            print(f"STEP {self.current_step}/{self.max_episode_steps} | "
                  f"DAY {session.get('currentDay', 0)}, "
                  f"SEGMENT {session.get('currentTimeSegment', 0)}")
            print("="*80)
            print(f"💰 Budget: ${sat_budget.get('budget', 0):,.0f}")
            print(f"😊 Satisfaction: {sat_budget.get('satisfaction', 0):.1f}/100")

            if "workforceState" in self.game_state:
                ws = self.game_state["workforceState"]
                print(f"\n👷 WORKFORCE:")
                print(f"  Free Trained: {ws.get('freeTrainedWorkers', 0)}, "
                      f"Working: {ws.get('workingTrainedWorkers', 0)}")
                print(f"  Free Untrained: {ws.get('freeUntrainedWorkers', 0)}, "
                      f"Working: {ws.get('workingUntrainedWorkers', 0)}")

            if "mapState" in self.game_state:
                facilities = self.game_state["mapState"].get("facilities", [])
                print(f"\n🏢 FACILITIES: {len(facilities)}")
                for facility in facilities[:5]:
                    status = facility.get("buildingStatus", "Unknown")
                    name = facility.get("facilityName", "Unknown")
                    workers = facility.get("assignedWorkforce", 0)
                    required = facility.get("requiredWorkforce", 4)
                    icon = "✅" if status == "InUse" else "⚠️ "
                    print(f"  {icon} {name}: {workers}/{required} workers ({status})")

            print(f"\n🎯 VALID ACTIONS: {len(self.valid_actions)}")
            print("="*80 + "\n")

    def close(self):
        """Clean up resources"""
        print("\n🔌 Closing ARC Game Gym Environment...")

        # Close socket
        if self.sock:
            try:
                self.sock.close()
            except:
                pass
            self.sock = None
            self.connected = False

        # Terminate Unity process
        if self.unity_process:
            print(f"🛑 Terminating Unity process (PID: {self.unity_process.pid})")
            try:
                self.unity_process.terminate()
                self.unity_process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                print("⚠️  Unity process did not terminate, forcing kill")
                self.unity_process.kill()
            except:
                pass
            self.unity_process = None

        print("✅ Environment closed")


if __name__ == "__main__":
    print("=" * 80)
    print("ARC Game Gym Environment (TCP) - Test")
    print("=" * 80)
    print("\nConnecting to Unity gym server on port 9876...")
    print("Make sure Unity is running with GymServerManager enabled!\n")

    try:
        env = ARCGameGymEnv(
            unity_exe_path=None,  # Connect to existing Unity
            unity_port=9876,
            max_episode_steps=5,
            render_mode="human",
            auto_start_unity=False
        )

        print("\n✅ Environment created, testing reset...")
        obs, info = env.reset()
        print(f"✅ Reset successful, satisfaction: {info['satisfaction']:.1f}")

        env.render()

        print("\n✅ Testing step with action 0...")
        obs, reward, terminated, truncated, info = env.step("0")
        print(f"✅ Step successful, reward: {reward:+.1f}, satisfaction: {info['satisfaction']:.1f}")

        env.close()
        print("\n✅ All tests passed!")

    except Exception as e:
        print(f"\n❌ Error: {e}")
        import traceback
        traceback.print_exc()
