"""
Append-only JSONL logger for ARC Game multi-agent episode data.
Each line is one agent turn. Used for offline RL analysis and reward modeling.
"""
import json
import uuid
from datetime import datetime, timezone

# Route reward through the SHARED scorer so offline RL and the live router agree on
# the objective. Import from reward_scoring (dependency-free) — NOT arc_game_gym_env_tcp,
# whose top-level gymnasium/numpy imports fail in the router env, which silently nulled
# the scorer and forced the legacy fallback (reward=0, reward_components=None) on every
# record. Keep the guard purely defensive.
try:
    from reward_scoring import compute_score_components
except ImportError:
    compute_score_components = None


class EpisodeLogger:
    def __init__(self, log_path: str = "episode_log.jsonl"):
        self.log_path = log_path

    def new_episode(self) -> str:
        """Generate and return a new episode UUID."""
        return str(uuid.uuid4())

    def log_turn(
        self,
        episode_id: str,
        round_num: int,
        day: int,
        segment: int,
        agent_name: str,
        role: str,
        actor_type: str,
        subobservation: dict,
        subactions_available: int,
        proposed_packages: list,
        selected_package_index,
        execution_results: list,
        satisfaction_before: float,
        satisfaction_after: float,
        budget_before: float,
        budget_after: float,
        llm_raw_response: str,
        conv_history_length: int,
        tokens_used: int,
        game_state_after: dict = None,
    ) -> None:
        """Append one agent turn record to the JSONL log."""
        # Calculate metrics
        satisfaction_delta = satisfaction_after - satisfaction_before
        budget_delta = budget_after - budget_before

        # Reward: route through the shared gym scorer for parity with the RL env.
        # `compute_score_components(rewardMetrics)` returns the composite `score`
        # (satisfaction - cost_efficiency). We record the components alongside the
        # scalar. Fall back to the legacy ad-hoc formula only when rewardMetrics is
        # absent or the gym module could not be imported (so tests without a real
        # game_state still work).
        reward_components = None
        reward_metrics = (game_state_after or {}).get("rewardMetrics")
        if compute_score_components is not None and reward_metrics:
            reward_components = compute_score_components(reward_metrics)
            reward = reward_components.get("score", 0.0)
        else:
            # Legacy fallback: satisfaction-weighted, budget stability secondary.
            reward = (satisfaction_delta * 0.7) + (budget_delta * 0.0003)

        # Calculate action success metrics. Exclude non-execution summary rows
        # (e.g. a propose_choices summary that carries no `success` key) so
        # proposals are never counted as attempted/failed actions.
        exec_action_rows = [
            r for r in execution_results
            if isinstance(r, dict) and "success" in r and r.get("kind") != "propose_choices"
        ]
        total_actions_attempted = len(exec_action_rows)
        successful_actions = sum(1 for r in exec_action_rows if r.get("success", False))
        failed_actions = total_actions_attempted - successful_actions
        action_success_rate = successful_actions / total_actions_attempted if total_actions_attempted > 0 else 0.0

        # Extract action details
        action_ids = [r.get("action_id", "unknown") for r in execution_results]
        error_messages = [r.get("error_message", "") for r in execution_results if not r.get("success", False)]

        record = {
            "episode_id": episode_id,
            "round": round_num,
            "day": day,
            "segment": segment,
            "agent_name": agent_name,
            "role": role,
            "actor_type": actor_type,
            "subobservation": subobservation,
            "subactions_available": subactions_available,
            "proposed_packages": proposed_packages,
            "selected_package_index": selected_package_index,
            "execution_results": execution_results,
            "satisfaction_before": satisfaction_before,
            "satisfaction_after": satisfaction_after,
            "satisfaction_delta": satisfaction_delta,
            "budget_before": budget_before,
            "budget_after": budget_after,
            "budget_delta": budget_delta,
            "reward": reward,
            "reward_components": reward_components,
            "total_actions_attempted": total_actions_attempted,
            "successful_actions": successful_actions,
            "failed_actions": failed_actions,
            "action_success_rate": action_success_rate,
            "action_ids": action_ids,
            "error_messages": error_messages,
            "llm_raw_response": llm_raw_response,
            "conv_history_length": conv_history_length,
            "tokens_used": tokens_used,
            "timestamp": datetime.now(timezone.utc).isoformat(),
        }
        with open(self.log_path, "a") as f:
            f.write(json.dumps(record) + "\n")

    def log_event(self, event_data: dict) -> None:
        """Append a general event record to the JSONL log (e.g., conversation messages)."""
        event_data["timestamp"] = datetime.now(timezone.utc).isoformat()
        with open(self.log_path, "a") as f:
            f.write(json.dumps(event_data) + "\n")

    def log_conversation_message(
        self,
        episode_id: str,
        round_num: int,
        from_agent: str,
        to_agent: str,
        message_type: str,
        content: str,
    ) -> None:
        """Log a conversation message between agents."""
        record = {
            "event_type": "conversation",
            "episode_id": episode_id,
            "round": round_num,
            "from": from_agent,
            "to": to_agent,
            "message_type": message_type,
            "content": content,
            "timestamp": datetime.now(timezone.utc).isoformat(),
        }
        with open(self.log_path, "a") as f:
            f.write(json.dumps(record) + "\n")

    def log_episode_end(
        self,
        episode_id: str,
        termination_reason: str,
        total_rounds: int,
        final_satisfaction: float,
        final_budget: float,
        total_reward: float,
    ) -> None:
        """Log episode termination summary."""
        record = {
            "event_type": "episode_end",
            "episode_id": episode_id,
            "termination_reason": termination_reason,
            "total_rounds": total_rounds,
            "final_satisfaction": final_satisfaction,
            "final_budget": final_budget,
            "total_reward": total_reward,
            "timestamp": datetime.now(timezone.utc).isoformat(),
        }
        with open(self.log_path, "a") as f:
            f.write(json.dumps(record) + "\n")
