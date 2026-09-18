"""
Message Queue for Agent Conversations

Manages isolated conversation threads between agents and the director.
Each agent has separate conversation histories with different partners,
enabling private conversations and future agent-to-agent communication.

Architecture:
    - Nested dict: {agent_name: {partner_name: [messages]}}
    - Messages stored in both participants' threads for easy lookup
    - Episode-level persistence (cleared on new episode)
    - All messages logged with type, round, timestamp

Usage:
    queue = MessageQueue()
    queue.send_message("Agent1", "Director", "Hello", "question", round_num=1)
    conversation = queue.get_conversation("Agent1", "Director")
"""

import time
import uuid
from typing import Dict, List, Optional


class MessageQueue:
    """
    Manages conversation threads between agents.

    Each agent can have multiple conversation threads (one per partner).
    Messages are stored in both participants' threads for symmetric access.
    """

    def __init__(self):
        """Initialize empty message queue."""
        # Structure: {agent_name: {partner_name: [message_dicts]}}
        self.agent_conversations: Dict[str, Dict[str, List[dict]]] = {}

    def send_message(
        self,
        from_agent: str,
        to_agent: str,
        content: str,
        msg_type: str,
        round_num: int
    ) -> dict:
        """
        Send a message between two agents.

        Stores the message in both participants' conversation threads.

        Args:
            from_agent: Name of agent sending message
            to_agent: Name of agent receiving message
            content: Message content
            msg_type: Type of message (e.g., "question", "response", "action_summary")
            round_num: Current round number

        Returns:
            The created message dict with id, timestamp, etc.
        """
        msg = {
            "id": str(uuid.uuid4()),
            "from": from_agent,
            "to": to_agent,
            "content": content,
            "type": msg_type,
            "round": round_num,
            "timestamp": time.time()
        }

        # Store in both participants' conversation threads
        self._add_to_conversation(from_agent, to_agent, msg)
        self._add_to_conversation(to_agent, from_agent, msg)

        return msg

    def _add_to_conversation(self, agent_name: str, partner_name: str, msg: dict):
        """
        Internal: Add message to agent's conversation with partner.

        Args:
            agent_name: Agent whose conversation to update
            partner_name: The conversation partner
            msg: Message dict to add
        """
        if agent_name not in self.agent_conversations:
            self.agent_conversations[agent_name] = {}
        if partner_name not in self.agent_conversations[agent_name]:
            self.agent_conversations[agent_name][partner_name] = []

        self.agent_conversations[agent_name][partner_name].append(msg)

    def get_conversation(self, agent_name: str, partner_name: str) -> List[dict]:
        """
        Get conversation history between two agents.

        Args:
            agent_name: First agent
            partner_name: Second agent

        Returns:
            List of message dicts in chronological order, or empty list if no conversation
        """
        return self.agent_conversations.get(agent_name, {}).get(partner_name, [])

    def get_all_conversations_for(self, agent_name: str) -> Dict[str, List[dict]]:
        """
        Get all conversation threads for an agent.

        Useful for future agent-to-agent conversations where an agent
        needs to see all their ongoing conversations.

        Args:
            agent_name: Agent name

        Returns:
            Dict mapping partner names to conversation lists
        """
        return self.agent_conversations.get(agent_name, {})

    def clear_all(self):
        """Clear all conversation histories (called at episode boundaries)."""
        self.agent_conversations = {}

    def get_unread_count(self, agent_name: str, partner_name: str, since_round: int) -> int:
        """
        Get count of unread messages in a conversation since a given round.

        Useful for UI notifications.

        Args:
            agent_name: Agent checking for messages
            partner_name: Conversation partner
            since_round: Only count messages after this round

        Returns:
            Number of new messages
        """
        conversation = self.get_conversation(agent_name, partner_name)
        return sum(1 for msg in conversation if msg["round"] > since_round)


# Message type constants for reference
MESSAGE_TYPES = {
    "action_summary": "Auto agent posts summary of actions taken",
    "question": "Agent asks director a question",
    "choice_proposal": "Choice agent proposes choices to director",
    "choice_revision": "Choice agent repropose choices after feedback",
    "feedback": "Director provides feedback on agent's proposal",
    "response": "General conversational response",
    "approval": "Director approves action/choice",
    "rejection": "Director rejects action/choice",
}
