"""
Observation and action space filters for the ARC Game agent router.
Applies subobservation_space and subaction_space constraints from agent config.
"""
import copy


# The config obs vocabulary uses friendly aliases that don't match the raw
# game_state keys; translate them so the requested slice is actually copied. Without
# this, "workers"/"buildings" matched no key and were silently dropped (officers were
# blind to workforce/facilities). Raw keys (workforceState/mapState/...) pass through
# unchanged, so either name works.
_OBS_KEY_ALIASES = {"workers": "workforceState", "buildings": "mapState"}


def filter_observation(game_state: dict, obs_keys: list) -> dict:
    """
    Return a filtered copy of game_state containing only the specified top-level keys.
    If obs_keys contains "all", returns a shallow-copied full state.
    Alias keys are translated to their raw game_state key; keys not present are skipped.
    """
    if "all" in obs_keys:
        return {k: copy.copy(v) for k, v in game_state.items()}
    out = {}
    for k in obs_keys:
        raw = _OBS_KEY_ALIASES.get(k, k)
        if raw in game_state:
            out[raw] = copy.copy(game_state[raw])
    return out


def _building_token_of(action: dict) -> str:
    """Best-effort building-type/name token for building-scoped filtering.

    The enumerator nests building identity per action_type:
      - construction:    action['construction']['building_type']  ("Kitchen"/"Shelter"/"CaseworkSite")
      - worker_assignment: action['assignment']['building_name']  ("Kitchen Alpha")
      - deconstruction:  action['deconstruction']['building_name'] ("Shelter Beta")
    Returns "" for actions with no building identity (worker hire/train, transfer).
    """
    c = action.get("construction")
    if isinstance(c, dict) and c.get("building_type"):
        return str(c["building_type"])
    for key in ("assignment", "deconstruction"):
        d = action.get(key)
        if isinstance(d, dict) and d.get("building_name"):
            return str(d["building_name"])
    # Flat fallbacks (in case an action isn't nested).
    return str(action.get("building_type") or action.get("building_name") or "")


def _action_matches_entry(action: dict, entry: dict) -> bool:
    """True if `action` is admitted by a single subaction_space entry.

    An entry is {"category": <cat>} optionally plus {"building_types": [...]}, or
    (for task_choice) an optional {"group": <slug>} sub-predicate.
    - category "all" admits everything (sub-predicates ignored — "all" means all).
    - Otherwise action_type must equal the category.
    - task_choice: if "group" is given, the action's task_choice group must equal
      it (one coarse group per officer domain). No "group" → any task_choice.
    - Other categories: if building_types is given, the action's building token must
      contain one of them (case-insensitive substring, so "Kitchen" matches
      "Kitchen Alpha"). An action with no building token is excluded then.
    """
    cat = entry.get("category")
    if cat == "all":
        return True
    if action.get("action_type") != cat:
        return False
    if cat == "task_choice":
        group = entry.get("group")
        if not group:
            return True
        return (action.get("task_choice") or {}).get("group") == group
    btypes = entry.get("building_types")
    if not btypes:
        return True
    token = _building_token_of(action).lower()
    if not token:
        return False
    return any(str(b).lower() in token for b in btypes)


def filter_actions(actions: list, subaction_space: list) -> list:
    """
    Return copies of actions admitted by the agent's subaction_space.

    Each entry is {"category": <cat>} with an OPTIONAL {"building_types": [...]}
    sub-predicate. An action is admitted if it matches ANY entry (OR semantics).
    Backward compatible: an entry without building_types behaves exactly as the
    old category-only filter. If any entry is {"category": "all"}, all actions
    pass. Preserves original list order.
    """
    if any(entry.get("category") == "all" for entry in subaction_space):
        return [copy.copy(a) for a in actions]
    return [copy.copy(a) for a in actions
            if any(_action_matches_entry(a, entry) for entry in subaction_space)]
