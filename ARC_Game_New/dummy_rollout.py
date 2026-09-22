"""
Minimal 10-turn dummy-action rollout against the running headless gym server.

Picks the first valid action each step (or "" if none), and writes a JSONL log
matching the existing EpisodeLogger conventions: one record per step plus a
final episode_end summary record.
"""
import argparse
import json
import sys
import uuid
from datetime import datetime, timezone
from pathlib import Path

from arc_game_gym_env_tcp import ARCGameGymEnv


def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=9876)
    parser.add_argument("--steps", type=int, default=10)
    parser.add_argument("--log", default="rollouts/dummy_rollout.jsonl")
    args = parser.parse_args()

    log_path = Path(args.log)
    log_path.parent.mkdir(parents=True, exist_ok=True)

    env = ARCGameGymEnv(
        unity_port=args.port,
        max_episode_steps=args.steps,
        auto_start_unity=False,
    )
    episode_id = str(uuid.uuid4())

    obs, info = env.reset()
    valid_count = info.get("valid_action_count", 0)
    print(f"[rollout] episode={episode_id} reset: "
          f"satisfaction={info.get('satisfaction')}, budget={info.get('budget')}, "
          f"valid_actions={valid_count}")

    total_reward = 0.0
    terminated = False
    truncated = False
    last_info = info

    with log_path.open("w") as f:
        # Episode header
        f.write(json.dumps({
            "event_type": "episode_start",
            "episode_id": episode_id,
            "policy": "dummy_first_valid",
            "max_steps": args.steps,
            "initial_satisfaction": info.get("satisfaction"),
            "initial_budget": info.get("budget"),
            "initial_valid_action_count": valid_count,
            "timestamp": _now(),
        }) + "\n")

        for step_idx in range(args.steps):
            # Dummy policy: pick index 0 if any valid, else no-op
            action_str = "0" if env.valid_actions else ""
            action_desc = (
                env.valid_actions[0].get("description", "")
                if env.valid_actions else "(no valid actions)"
            )
            action_type = (
                env.valid_actions[0].get("actionType", "")
                if env.valid_actions else ""
            )
            sat_before = float(last_info.get("satisfaction", 0.0))
            budget_before = float(last_info.get("budget", 0.0))

            obs, reward, terminated, truncated, info = env.step(action_str)
            total_reward += reward

            executed = info.get("executed_actions", [])
            exec_results = info.get("execution_results", [])
            successes = [r for r in exec_results if r.get("success")]

            record = {
                "event_type": "step",
                "episode_id": episode_id,
                "step": step_idx + 1,
                "day": info.get("day"),
                "round": info.get("round"),
                "segment": info.get("segment"),
                "action_input": action_str,
                "action_index": 0 if action_str else None,
                "action_description": action_desc,
                "action_type": action_type,
                "executed_actions": executed,
                "execution_results": exec_results,
                "successful_actions": len(successes),
                "failed_actions": len(exec_results) - len(successes),
                "satisfaction_before": sat_before,
                "satisfaction_after": float(info.get("satisfaction", 0.0)),
                "satisfaction_delta": reward,
                "budget_before": budget_before,
                "budget_after": float(info.get("budget", 0.0)),
                "reward": reward,
                "terminated": terminated,
                "truncated": truncated,
                "valid_action_count_after": info.get("valid_action_count"),
                "timestamp": _now(),
            }
            f.write(json.dumps(record) + "\n")

            print(f"[rollout] step {step_idx+1}/{args.steps}: "
                  f"action='{action_str}' ({action_type}) -> "
                  f"reward={reward:+.2f}, sat={info.get('satisfaction')}, "
                  f"budget={info.get('budget')}, "
                  f"executed={len(successes)}/{len(exec_results)}")

            last_info = info
            if terminated or truncated:
                break

        # Episode footer
        f.write(json.dumps({
            "event_type": "episode_end",
            "episode_id": episode_id,
            "terminated": terminated,
            "truncated": truncated,
            "total_steps": step_idx + 1,
            "final_satisfaction": float(last_info.get("satisfaction", 0.0)),
            "final_budget": float(last_info.get("budget", 0.0)),
            "total_reward": total_reward,
            "termination_reason": (
                "satisfaction<=0" if terminated
                else "max_steps" if truncated
                else "completed"
            ),
            "timestamp": _now(),
        }) + "\n")

    env.close()
    print(f"[rollout] done. total_reward={total_reward:+.2f}, "
          f"final_satisfaction={last_info.get('satisfaction')}, "
          f"log={log_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
