"""The plugin ToolContext bound to a live Session (router.plugin_api is the interface)."""
from __future__ import annotations

from typing import TYPE_CHECKING, List, Optional

if TYPE_CHECKING:
    from router.session import Session


from router.config import AgentConfig
from cora.tools import TOOLS
# The typed action tools (build/hire/train/staff/deconstruct/task/transfer) the officer emits;
# Session._execute_calls runs them through cora.executor.
_CORA_ACTION_TOOLS = {t["name"] for t in TOOLS}
from router import plugin_api
from cora.observation import officer_text


def _num(v, default=0):
    """A number for $-formatting; anything else formats as `default`."""
    return v if isinstance(v, (int, float)) else default
from router.common import SYSTEM_ACTOR


def _tool_schema_name(s: dict) -> Optional[str]:
    return (s.get("function") or {}).get("name") or s.get("name")


def _plugin_tool_schemas_for(agent: AgentConfig, brief_only: bool,
                             existing: List[dict]) -> List[dict]:
    """Registered plugin tool schemas this agent may use: filtered by the per-agent allowlist
    (if any), the reactive acting-strip, and de-duped against built-ins already offered."""
    allow = set(agent.tools) if getattr(agent, "tools", None) else None
    have = {_tool_schema_name(t) for t in existing}
    out: List[dict] = []
    for name, spec in plugin_api.all_tools().items():
        if allow is not None and name not in allow:
            continue
        if brief_only and spec.acting:
            continue
        if name in have:
            continue
        out.append(spec.schema)
    return out


class _SessionToolContext(plugin_api.ToolContext):
    """Live ToolContext backed by a Session — the concrete `ctx` handed to plugin tools/hooks.

    Reads route to the session's filtered latest snapshot; the three store scopes and the
    session lock live on the Session. Acting (`execute`/`propose_choices`) is wired in a
    follow-up slice (the built-in execute path is extracted into a reusable helper there).
    """
    def __init__(self, session: "Session", agent: AgentConfig,
                 game_state: dict, all_actions: List[dict], filtered_actions: List[dict]):
        self._s = session
        self.agent = agent
        self._game_state = game_state
        self.all_actions = all_actions
        self.filtered_actions = filtered_actions
        self.participant_id = getattr(session, "player_id", None)
        self.session_id = getattr(session, "session_id", None)
        self.round = getattr(session, "round_num", 0)
        # agent is None for session-level hook contexts (a choice/round event isn't tied to one
        # officer); agent_store then shares a "__session__" bucket.
        _akey = agent.subagent_name if agent is not None else "__session__"
        self.agent_store = session._plugin_agent_stores.setdefault(_akey, {})
        self.session_store = session._plugin_session_store
        self.persist = session._plugin_persist
        self.session_lock = session._plugin_session_lock

    @property
    def state(self) -> dict:
        return self._s._latest_game_state or self._game_state

    def _fs(self) -> dict:
        if self.agent is None:
            return self.state
        return self._s._filter_state(self.state, self.agent)

    def get_facilities(self) -> str: return officer_text(self._fs(), "facilities")
    def get_workforce(self) -> str: return officer_text(self._fs(), "workforce")
    def get_tasks(self) -> str: return officer_text(self._fs(), "tasks")
    def get_logistics(self) -> str: return officer_text(self._fs(), "logistics", self.filtered_actions)
    def enumerate_actions(self) -> list: return list(self.filtered_actions)

    def enumerate_choice_packages(self) -> list:
        return [a for a in self.filtered_actions if a.get("action_type") == "task_choice"]

    async def refresh_state(self) -> dict:
        return await self._s._fetch_fresh_state()

    async def execute(self, calls: list) -> plugin_api.ToolResult:
        """Run typed action calls through the SAME path as the officer's own action tools
        (cora.executor → Unity), with all its ledger/scope logic."""
        if self.agent is None:
            raise RuntimeError("execute requires an officer context (not a session hook)")
        calls = [(c["tool"], c.get("args") or {}) if isinstance(c, dict) else tuple(c) for c in calls]
        text, gs, all_a, filt, meta = await self._s._execute_calls(
            self.agent, calls, self._game_state, self.all_actions, self.filtered_actions,
            {"executed": 0, "finish": False})
        self._game_state, self.all_actions, self.filtered_actions = gs, all_a, filt
        return plugin_api.ToolResult(text=text, executed=meta.get("executed", 0),
                                   finish=meta.get("finish", False))

    async def propose_choices(self, packages: list) -> plugin_api.ToolResult:
        """Send the director a choice set via the SAME path as the propose_choices tool."""
        if self.agent is None:
            raise RuntimeError("propose_choices requires an officer context")
        text, gs, all_a, filt, executed, superseded, _rows = await self._s._continuous_propose(
            self.agent, {"packages": packages}, self._game_state, self.all_actions,
            self.filtered_actions)
        self._game_state, self.all_actions, self.filtered_actions = gs, all_a, filt
        return plugin_api.ToolResult(text=text, executed=executed, finish=bool(superseded))

    def log(self, event_type: str, payload: Optional[dict] = None) -> None:
        """Emit a plugin event through the session chokepoint (Session._emit).

        The plugin-supplied payload is NESTED under `payload` (never splatted), so a
        plugin cannot collide with or overwrite record fields (actor/event_type/ids).
        """
        if self.agent is None:
            self._s._emit(event_type, {"from": "system", "payload": payload or {}},
                          actor=SYSTEM_ACTOR)
        else:
            self._s._emit(event_type, {"from": self.agent.subagent_name, "payload": payload or {}},
                          agent=self.agent)
