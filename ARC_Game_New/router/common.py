"""Helpers shared by the router's session modules: the action menu with task choices, actor
records, and small formatting utilities."""
from __future__ import annotations

import re
from datetime import datetime, timezone


from cora.actions import enumerate_actions
from cora.tools import TOOLS
# The typed action tools (build/hire/train/staff/deconstruct/task/transfer) the officer emits;
# Session._execute_calls runs them through cora.executor.
_CORA_ACTION_TOOLS = {t["name"] for t in TOOLS}
from cora.observation import task_group


def _num(v, default=0):
    """A number for $-formatting; anything else formats as `default`."""
    return v if isinstance(v, (int, float)) else default
import re

# Action types that are site/target-bound and NOT legitimately repeatable within a
# planning phase (building a site, demolishing it, assigning workers to a specific
# building are idempotent). Quantity actions — hiring more workers, transferring
# more people — CAN legitimately repeat, so ledger_mode="block" leaves them alone.
_NON_REPEATABLE_TYPES = {"construction", "deconstruction", "worker_assignment"}



def _enumerate_actions(game_state: dict) -> list[dict]:
    """The round's action menu (cora.actions) plus the task_choice pseudo-actions below."""
    try:
        actions = enumerate_actions(game_state)
    except Exception as e:
        print(f"[router] action enumeration error: {e}")
        actions = []
    # Choice-tasks become first-class 'task_choice' pseudo-actions so officers can
    # answer them through the same index-based menu (execute_game_action) and the
    # same subaction_space gate as every other action — no bespoke task path.
    return actions + _enumerate_task_choices(game_state)


# Belt-and-suspenders for the "stop name-badging" behavior: officers are told (in
# the prompt) to introduce themselves once and then just talk, but they tend to
# re-prefix every reply with "<Role> Officer:" / "<Role> Officer here —". The role
# is already shown by the on-screen avatar, so strip a leading self-label badge from
# each conversational reply. Conservative: only fires when the message OPENS with a
# short label ending in "officer" (optionally "... here") followed by a separator,
# so ordinary prose is never touched.
_SELF_LABEL_RE = re.compile(
    r"^\s*\*{0,2}\s*[A-Za-z0-9 &/.'-]{0,40}?officer(?:\s+here)?\s*\*{0,2}\s*[:—–-]\s+",
    re.IGNORECASE,
)


def _strip_self_label(text: str) -> str:
    """Remove a single leading '<Role> Officer:'-style self-label badge, if present."""
    if not text:
        return text
    return _SELF_LABEL_RE.sub("", text, count=1)


def _enumerate_task_choices(game_state: dict) -> list[dict]:
    """Enumerate each active choice-task's options as 'task_choice' pseudo-actions.

    One row per (task, choice), tagged with the coarse task_group so the ordinary
    filter_actions path ({"category":"task_choice","group":<slug>}) gates which
    officer may answer which task — jurisdiction enforced by the normal action
    filter, not a separate check. Carries taskId/choiceId for execution.
    """
    out = []
    for t in (game_state.get("allActiveTasks") or []):
        tid = t.get("taskId")
        grp = task_group(t)
        title = t.get("taskTitle") or t.get("title") or f"task {tid}"
        for c in (t.get("choices") or []):
            cid = c.get("choiceId")
            text = (c.get("choiceText") or c.get("text") or "").strip()
            out.append({
                "action_type": "task_choice",
                "description": f'answer task "{title}" → choice {cid}: {text}',
                "cost": 0,
                "task_choice": {"taskId": tid, "choiceId": cid,
                                "group": grp, "taskTitle": title},
            })
    return out


# Canonical actor block for the human player (the manual director). Agent-driven
# actors are built per-agent by Session._actor_for(). See the "action" event
# schema in the per-actor logging design.
HUMAN_DIRECTOR_ACTOR = {
    "kind": "human",
    "name": "Director",
    "role": "director",
    "actor_type": "manual",
}

# System actor for engine-/server-originated events with no human or agent behind them.
SYSTEM_ACTOR = {
    "kind": "system",
    "name": "system",
    "role": "system",
    "actor_type": "system",
}

# Sentinel so Session._emit can tell "agent not passed" from the valid value agent=None
# (which _actor_for maps to the human Director).
_UNSET = object()


def _wrap_actor(name: str) -> dict:
    """Normalize a bare string actor (e.g. "system") into the canonical actor dict block."""
    return {"kind": name, "name": name, "role": name, "actor_type": name}



def _num_free_vehicles(game_state):
    """Vehicles idle right now, or None if the state doesn't say."""
    lg = (game_state or {}).get("logistics") or {}
    v = lg.get("availableVehicles")
    if v is None:
        v = lg.get("vehiclesFree")
    try:
        return int(v) if v is not None else None
    except (TypeError, ValueError):
        return None


# How many peer-triggered officer activations one round may spawn. Generous on purpose: the
# point is to let inter-agent conversation actually happen and to SEE how far it runs, not to
# clip it at the first exchange. It exists so a pathological loop ends the round instead of
# the session.
PEER_TRIGGER_BUDGET_PER_ROUND = 12





# ── Utilities ────────────────────────────────────────────────────

def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


def _get_satisfaction(state: dict) -> float:
    return state.get("satisfactionAndBudget", {}).get("satisfaction", 0)


def _get_budget(state: dict) -> float:
    return state.get("satisfactionAndBudget", {}).get("budget", 0)
