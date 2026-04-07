"""
Observation and action space filters for the ARC Game agent router.
Applies subobservation_space and subaction_space constraints from agent config.
"""
import copy


def filter_observation(game_state: dict, obs_keys: list) -> dict:
    """
    Return a filtered copy of game_state containing only the specified top-level keys.
    If obs_keys contains "all", returns a shallow-copied full state.
    Keys not present in game_state are silently skipped.
    """
    if "all" in obs_keys:
        return {k: copy.copy(v) for k, v in game_state.items()}
    return {k: copy.copy(game_state[k]) for k in obs_keys if k in game_state}


def filter_actions(actions: list, subaction_space: list) -> list:
    """
    Return copies of actions whose action_type is in subaction_space categories.
    If subaction_space contains {"category": "all"}, returns copies of all actions.
    Preserves original list order.
    """
    categories = {entry["category"] for entry in subaction_space}
    if "all" in categories:
        return [copy.copy(a) for a in actions]
    return [copy.copy(a) for a in actions if a.get("action_type") in categories]
