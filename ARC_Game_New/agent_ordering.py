"""
Agent turn ordering strategies for the ARC Game multi-agent router.

Extensibility contract: to add a new rule, implement a new function with
the same signature as _order_sequential() and add it to _ORDER_RULES.
The router calls get_agent_order() — nothing else needs to change.
"""
import random as _random
from agent_config import AgentConfig


def _order_sequential(
    subagents: list,
    game_state: dict,
    round_num: int,
    history: list,
) -> list:
    """Return subagents in config file order."""
    return list(subagents)


def _order_random(
    subagents: list,
    game_state: dict,
    round_num: int,
    history: list,
) -> list:
    """Return subagents in a random shuffle each round."""
    shuffled = list(subagents)
    _random.shuffle(shuffled)
    return shuffled


# Registry: add new rules here only
_ORDER_RULES = {
    "sequential": _order_sequential,
    "random":     _order_random,
    # Future: "priority": _order_priority,
    # Future: "relevance_ranked": _order_relevance,
}


def get_agent_order(
    rule: str,
    agents: list,
    game_state: dict,
    round_num: int,
    history: list,
) -> list:
    """
    Return ordered list of subagents to act this round.
    Commanders are always excluded — they act after subagents via separate logic.

    Args:
        rule:       The agent_order_rule string from agents_config.json
        agents:     Full agent list (subagents + director)
        game_state: Current full game state dict
        round_num:  Current round number (for stateful future rules)
        history:    Episode log so far (for stateful future rules)

    Returns:
        Ordered list of subagent AgentConfig objects (no directors).
    """
    if rule not in _ORDER_RULES:
        raise ValueError(f"Unknown agent_order_rule '{rule}'. "
                         f"Valid rules: {list(_ORDER_RULES.keys())}")
    subagents = [a for a in agents if a.role == "subagent"]
    return _ORDER_RULES[rule](subagents, game_state, round_num, history)
