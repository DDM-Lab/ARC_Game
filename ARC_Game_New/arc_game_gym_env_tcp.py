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
        connection_timeout: float = 30.0
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
            self.unity_process = subprocess.Popen(
                [
                    str(unity_path),
                    "-batchmode",
                    "-nographics",
                    "-gym-server",  # Enable gym server mode
                    "-gym-port", str(self.unity_port),
                    "-logFile", "-",  # Log to stdout
                ],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
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

        # Request updated game state
        response = self._send_request({"type": "get_game_state"})

        if response.get("type") != "game_state":
            raise RuntimeError("Failed to get game state after action execution")

        game_state_json = response.get("game_state", "{}")
        self.game_state = json.loads(game_state_json)

        # Calculate reward (satisfaction delta)
        sat_budget = self.game_state.get("satisfactionAndBudget", {})
        current_satisfaction = float(sat_budget.get("satisfaction", 0.0))
        reward = current_satisfaction - self.previous_satisfaction
        self.previous_satisfaction = current_satisfaction

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
            "satisfaction_delta": reward,
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
