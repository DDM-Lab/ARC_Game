"""
Append-only JSONL logger for ARC Game multi-agent episode data.
Each line is one agent turn. Used for offline RL analysis and reward modeling.
"""
import json
import uuid
from datetime import datetime, timezone


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
    ) -> None:
        """Append one agent turn record to the JSONL log."""
        # Calculate metrics
        satisfaction_delta = satisfaction_after - satisfaction_before
        budget_delta = budget_after - budget_before

        # Calculate reward (weighted combination of satisfaction and budget change)
        # Satisfaction is more important (0.7 weight), budget stability is secondary (0.3 weight)
        reward = (satisfaction_delta * 0.7) + (budget_delta * 0.0003)  # Budget scaled to similar range

        # Calculate action success metrics
        total_actions_attempted = len(execution_results)
        successful_actions = sum(1 for r in execution_results if r.get("success", False))
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
