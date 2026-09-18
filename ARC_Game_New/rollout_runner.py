"""
Rollout Runner - Parallel Episode Collection for ARC Game

Orchestrates multiple Unity + agent_router pairs to collect Claude gameplay episodes.
Supports parallel execution for high-throughput data collection on clusters.

Usage:
    # Sequential (single episode)
    python rollout_runner.py --episodes 1 --unity-build ./Build/ARC.exe

    # Parallel (10 episodes, 4 workers)
    python rollout_runner.py --episodes 10 --parallel 4 --unity-build ./Build/ARC.exe

    # Cluster mode (128 episodes, 64 workers)
    python rollout_runner.py --episodes 128 --parallel 64 --unity-build ./Build/ARC.x86_64
"""

import subprocess
import json
import time
import argparse
import sys
import os
import signal
from pathlib import Path
from datetime import datetime, timezone
from typing import List, Dict, Optional, Tuple
from concurrent.futures import ProcessPoolExecutor, as_completed
from dataclasses import dataclass, asdict
import threading

@dataclass
class EpisodeResult:
    """Results from a single episode"""
    episode_id: int
    success: bool
    termination_reason: str
    final_satisfaction: float
    final_budget: float
    final_day: int
    total_turns: int
    duration_seconds: float
    error_message: Optional[str] = None


class RolloutRunner:
    """
    Orchestrates parallel episode collection using Unity + agent_router + Claude.

    Architecture:
        For each parallel worker:
          Unity (port base_port + worker_id) ← WebSocket → agent_router ← Claude API
    """

    def __init__(
        self,
        unity_build_path: str,
        agent_config_path: str = "config/claude_director_config.json",
        output_dir: str = "rollouts",
        base_port: int = 8000,
        max_parallel: int = 4,
        max_turns_per_episode: Optional[int] = 100,
        max_days: int = 30,
        episode_timeout_seconds: int = 1800,  # 30 minutes per episode
    ):
        """
        Args:
            unity_build_path: Path to Unity headless executable
            agent_config_path: Path to agent configuration JSON
            output_dir: Directory to save rollout JSONL files
            base_port: Base port for WebSocket (workers use base_port + worker_id)
            max_parallel: Maximum number of parallel episodes
            max_turns_per_episode: Turn limit per episode (None = no limit)
            max_days: Unity-side day limit
            episode_timeout_seconds: Kill episode if it exceeds this duration
        """
        self.unity_build_path = Path(unity_build_path)
        self.agent_config_path = Path(agent_config_path)
        self.output_dir = Path(output_dir)
        self.base_port = base_port
        self.max_parallel = max_parallel
        self.max_turns_per_episode = max_turns_per_episode
        self.max_days = max_days
        self.episode_timeout_seconds = episode_timeout_seconds

        # Validation
        if not self.unity_build_path.exists():
            raise FileNotFoundError(f"Unity build not found: {self.unity_build_path}")
        if not self.agent_config_path.exists():
            raise FileNotFoundError(f"Agent config not found: {self.agent_config_path}")

        # Create output directory
        self.output_dir.mkdir(parents=True, exist_ok=True)

        # Logging
        self.log_lock = threading.Lock()
        timestamp = datetime.now(timezone.utc).strftime("%Y%m%d_%H%M%S")
        self.log_file = self.output_dir / f"rollout_log_{timestamp}.txt"

    def log(self, message: str, worker_id: Optional[int] = None):
        """Thread-safe logging"""
        with self.log_lock:
            timestamp = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S")
            worker_prefix = f"[Worker {worker_id}]" if worker_id is not None else "[Main]"
            log_line = f"{timestamp} {worker_prefix} {message}"
            print(log_line)
            with open(self.log_file, "a") as f:
                f.write(log_line + "\n")

    def run_episodes_parallel(self, num_episodes: int) -> List[EpisodeResult]:
        """
        Run multiple episodes in parallel using ProcessPoolExecutor.

        Returns:
            List of EpisodeResult objects
        """
        self.log(f"Starting {num_episodes} episodes with {self.max_parallel} parallel workers")

        results = []
        with ProcessPoolExecutor(max_workers=self.max_parallel) as executor:
            # Submit all episodes
            futures = {
                executor.submit(
                    run_single_episode_worker,
                    episode_id=episode_id,
                    worker_id=episode_id % self.max_parallel,
                    unity_build_path=str(self.unity_build_path),
                    agent_config_path=str(self.agent_config_path),
                    output_dir=str(self.output_dir),
                    port=self.base_port + (episode_id % self.max_parallel),
                    max_turns=self.max_turns_per_episode,
                    max_days=self.max_days,
                    timeout_seconds=self.episode_timeout_seconds,
                ): episode_id
                for episode_id in range(num_episodes)
            }

            # Collect results as they complete
            for future in as_completed(futures):
                episode_id = futures[future]
                try:
                    result = future.result()
                    results.append(result)
                    self.log(
                        f"Episode {result.episode_id} completed: "
                        f"{result.termination_reason}, turns={result.total_turns}, "
                        f"satisfaction={result.final_satisfaction:.1f}"
                    )
                except Exception as e:
                    self.log(f"Episode {episode_id} failed with exception: {e}")
                    results.append(EpisodeResult(
                        episode_id=episode_id,
                        success=False,
                        termination_reason="exception",
                        final_satisfaction=0.0,
                        final_budget=0.0,
                        final_day=0,
                        total_turns=0,
                        duration_seconds=0.0,
                        error_message=str(e)
                    ))

        self.log(f"All {num_episodes} episodes completed")
        self._write_summary(results)
        return results

    def _write_summary(self, results: List[EpisodeResult]):
        """Write summary statistics to JSON"""
        summary_file = self.output_dir / "rollout_summary.json"

        successful = [r for r in results if r.success]
        failed = [r for r in results if not r.success]

        summary = {
            "total_episodes": len(results),
            "successful": len(successful),
            "failed": len(failed),
            "avg_satisfaction": sum(r.final_satisfaction for r in successful) / len(successful) if successful else 0,
            "avg_turns": sum(r.total_turns for r in successful) / len(successful) if successful else 0,
            "avg_duration_seconds": sum(r.duration_seconds for r in successful) / len(successful) if successful else 0,
            "termination_reasons": {
                reason: sum(1 for r in results if r.termination_reason == reason)
                for reason in set(r.termination_reason for r in results)
            },
            "episodes": [asdict(r) for r in results]
        }

        with open(summary_file, "w") as f:
            json.dump(summary, f, indent=2)

        self.log(f"Summary written to {summary_file}")
        self.log(f"Success rate: {len(successful)}/{len(results)} ({100*len(successful)/len(results):.1f}%)")

        # Convert JSONL trajectories to single JSON per episode
        self._convert_trajectories_to_json()

    def _convert_trajectories_to_json(self):
        """Convert episode JSONL files to single JSON files for easier loading"""
        trajectories_dir = self.output_dir / "trajectories"
        trajectories_dir.mkdir(exist_ok=True)

        for jsonl_file in self.output_dir.glob("episode_*.jsonl"):
            episode_id = jsonl_file.stem.replace("episode_", "")

            # Read all turns from JSONL
            turns = []
            with open(jsonl_file, "r") as f:
                for line in f:
                    if line.strip():
                        turns.append(json.loads(line))

            if not turns:
                continue

            # Create structured trajectory
            trajectory = {
                "episode_id": episode_id,
                "num_turns": len(turns),
                "final_satisfaction": turns[-1].get("satisfaction_after", 0),
                "final_budget": turns[-1].get("budget_after", 0),
                "final_day": turns[-1].get("day", 0),
                "turns": turns
            }

            # Write to JSON
            json_file = trajectories_dir / f"episode_{episode_id}.json"
            with open(json_file, "w") as f:
                json.dump(trajectory, f, indent=2)

        self.log(f"Converted {len(list(trajectories_dir.glob('*.json')))} trajectories to JSON format")
        self.log(f"Trajectories saved to: {trajectories_dir}")


def run_single_episode_worker(
    episode_id: int,
    worker_id: int,
    unity_build_path: str,
    agent_config_path: str,
    output_dir: str,
    port: int,
    max_turns: Optional[int],
    max_days: int,
    timeout_seconds: int,
) -> EpisodeResult:
    """
    Worker function to run a single episode.
    Runs in separate process via ProcessPoolExecutor.

    Process lifecycle:
        1. Start Unity headless on specified port
        2. Start agent_router connected to that port
        3. Monitor episode progress
        4. Detect termination condition
        5. Cleanup processes
        6. Return results
    """
    start_time = time.time()
    unity_process = None
    router_process = None

    try:
        # Start agent_router FIRST so it's ready to accept connections
        router_log = Path(output_dir) / f"router_ep{episode_id}.log"
        router_process = subprocess.Popen(
            [
                sys.executable,  # Python interpreter
                "-u",  # Unbuffered output
                "agent_router.py",
                "--config", agent_config_path,
                "--port", str(port),
                "--log", str(Path(output_dir) / f"episode_{episode_id}.jsonl"),
            ],
            stdout=open(router_log, "w"),
            stderr=subprocess.STDOUT,
        )

        # Give router time to start and begin listening
        time.sleep(3)

        # Now start Unity headless - it will connect to the already-running router
        unity_log = Path(output_dir) / f"unity_ep{episode_id}.log"
        unity_process = subprocess.Popen(
            [
                unity_build_path,
                "-batchmode",
                "-nographics",
                f"-websocket-port", str(port),
                f"-logFile", str(unity_log),
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )

        # Monitor episode (simplified - real implementation would parse logs/WebSocket)
        # For now, just wait for timeout or process completion
        router_process.wait(timeout=timeout_seconds)

        # Parse results from episode log
        episode_log = Path(output_dir) / f"episode_{episode_id}.jsonl"
        result = _parse_episode_results(episode_id, episode_log, start_time)

        return result

    except subprocess.TimeoutExpired:
        return EpisodeResult(
            episode_id=episode_id,
            success=False,
            termination_reason="timeout",
            final_satisfaction=0.0,
            final_budget=0.0,
            final_day=0,
            total_turns=0,
            duration_seconds=time.time() - start_time,
            error_message=f"Episode exceeded {timeout_seconds}s timeout"
        )
    except Exception as e:
        return EpisodeResult(
            episode_id=episode_id,
            success=False,
            termination_reason="error",
            final_satisfaction=0.0,
            final_budget=0.0,
            final_day=0,
            total_turns=0,
            duration_seconds=time.time() - start_time,
            error_message=str(e)
        )
    finally:
        # Cleanup processes
        if router_process:
            router_process.terminate()
            try:
                router_process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                router_process.kill()

        if unity_process:
            unity_process.terminate()
            try:
                unity_process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                unity_process.kill()


def _parse_episode_results(episode_id: int, log_path: Path, start_time: float) -> EpisodeResult:
    """
    Parse episode JSONL log to extract final results.

    Expected log format (from episode_logger.py):
        Each line is a JSON object with turn data
        Final line should contain termination info
    """
    if not log_path.exists():
        return EpisodeResult(
            episode_id=episode_id,
            success=False,
            termination_reason="no_log",
            final_satisfaction=0.0,
            final_budget=0.0,
            final_day=0,
            total_turns=0,
            duration_seconds=time.time() - start_time,
            error_message="Episode log file not found"
        )

    # Parse JSONL
    turns = []
    with open(log_path, "r") as f:
        for line in f:
            if line.strip():
                turns.append(json.loads(line))

    if not turns:
        return EpisodeResult(
            episode_id=episode_id,
            success=False,
            termination_reason="no_data",
            final_satisfaction=0.0,
            final_budget=0.0,
            final_day=0,
            total_turns=0,
            duration_seconds=time.time() - start_time,
            error_message="No turns recorded in episode log"
        )

    # Extract final state from last turn
    final_turn = turns[-1]
    game_state = final_turn.get("game_state", {})
    session_info = game_state.get("sessionInfo", {})

    return EpisodeResult(
        episode_id=episode_id,
        success=True,
        termination_reason=final_turn.get("termination_reason", "completed"),
        final_satisfaction=session_info.get("satisfaction", 0.0),
        final_budget=session_info.get("budget", 0.0),
        final_day=session_info.get("currentDay", 0),
        total_turns=len(turns),
        duration_seconds=time.time() - start_time,
    )


def main():
    parser = argparse.ArgumentParser(description="Run ARC Game rollouts with Claude")
    parser.add_argument("--episodes", type=int, default=10, help="Number of episodes to collect")
    parser.add_argument("--parallel", type=int, default=4, help="Number of parallel workers")
    parser.add_argument("--unity-build", required=True, help="Path to Unity headless executable")
    parser.add_argument("--config", default="config/claude_director_config.json", help="Agent config path")
    parser.add_argument("--output-dir", default="rollouts", help="Output directory for logs")
    parser.add_argument("--base-port", type=int, default=8000, help="Base WebSocket port")
    parser.add_argument("--max-turns", type=int, default=100, help="Max turns per episode")
    parser.add_argument("--max-days", type=int, default=30, help="Max days per episode")
    parser.add_argument("--timeout", type=int, default=1800, help="Episode timeout in seconds")

    args = parser.parse_args()

    runner = RolloutRunner(
        unity_build_path=args.unity_build,
        agent_config_path=args.config,
        output_dir=args.output_dir,
        base_port=args.base_port,
        max_parallel=args.parallel,
        max_turns_per_episode=args.max_turns,
        max_days=args.max_days,
        episode_timeout_seconds=args.timeout,
    )

    results = runner.run_episodes_parallel(args.episodes)

    # Print summary
    successful = sum(1 for r in results if r.success)
    print(f"\n{'='*60}")
    print(f"ROLLOUT COMPLETE")
    print(f"{'='*60}")
    print(f"Total episodes: {len(results)}")
    print(f"Successful: {successful} ({100*successful/len(results):.1f}%)")
    print(f"Results saved to: {runner.output_dir}")
    print(f"{'='*60}\n")


if __name__ == "__main__":
    main()
