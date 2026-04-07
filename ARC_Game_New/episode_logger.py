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
            "satisfaction_delta": satisfaction_after - satisfaction_before,
            "budget_before": budget_before,
            "budget_after": budget_after,
            "budget_delta": budget_after - budget_before,
            "llm_raw_response": llm_raw_response,
            "conv_history_length": conv_history_length,
            "tokens_used": tokens_used,
            "timestamp": datetime.now(timezone.utc).isoformat(),
        }
        with open(self.log_path, "a") as f:
            f.write(json.dumps(record) + "\n")
