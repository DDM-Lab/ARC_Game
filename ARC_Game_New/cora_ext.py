"""cora_ext — the stable extension API for CORA tool/hook plugins.

A plugin module imports ONLY this module (never `agent_router`). It registers **tools**
(LLM-callable, `(ctx, args) -> ToolResult`) and **hooks** (event-driven, `(ctx, event) -> None`)
via the decorators below. Both reach the running game exclusively through the injected
`ToolContext` (`ctx`) — the host constructs a concrete `ctx` per call and passes it in. See
docs/phase2-plugin-spec.md.

This module is dependency-light on purpose (stdlib only) so plugins and the offline test harness
can import it without pulling in the router/Unity/LLM stack.
"""
from __future__ import annotations

import asyncio
import collections
import importlib
import importlib.util
import inspect
import traceback
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Optional

API_VERSION = "0.1"

# --- Diagnostics: load-time failures + a ring buffer of recent runtime errors ---------------
_LOAD_ERRORS: list = []                                    # populated by load_plugins()
_RUNTIME_ERRORS: "collections.deque" = collections.deque(maxlen=200)


def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


def record_error(kind: str, name: str, exc: BaseException) -> None:
    """Capture a plugin runtime error (tool/hook) with its traceback for later inspection."""
    _RUNTIME_ERRORS.append({"ts": _now(), "kind": kind, "name": name,
                            "error": f"{type(exc).__name__}: {exc}",
                            "traceback": traceback.format_exc()})


def recent_errors(limit: int = 50) -> list:
    return list(_RUNTIME_ERRORS)[-limit:]


def load_errors() -> list:
    return list(_LOAD_ERRORS)


# --------------------------------------------------------------------------------------------
# ToolResult
# --------------------------------------------------------------------------------------------
@dataclass
class ToolResult:
    """What a tool handler returns. `text` is shown to the officer; `executed` counts actions
    committed to the game this call; `finish` ends the officer's turn."""
    text: str
    executed: int = 0
    finish: bool = False


# --------------------------------------------------------------------------------------------
# Tool registry
# --------------------------------------------------------------------------------------------
@dataclass
class ToolSpec:
    name: str
    schema: dict                 # OpenAI function-format schema (data the LLM sees)
    handler: Callable            # (ctx, args) -> ToolResult | Awaitable[ToolResult]
    acting: bool = False         # True => an "acting" tool (gated on reactive brief-only turns)
    override_of: Optional[str] = None   # names a built-in/other tool this replaces
    module: str = ""


_TOOLS: dict[str, ToolSpec] = {}


def register_tool(name: str, schema: dict, *, acting: bool = False,
                  override_of: Optional[str] = None) -> Callable:
    """Decorator: register a tool handler under `name`. Collision on `name` is an error unless
    this registration (or the existing one) declares `override_of` — that makes replacement
    explicit rather than accidental."""
    def deco(fn: Callable) -> Callable:
        existing = _TOOLS.get(name)
        if existing is not None and override_of is None and existing.override_of is None:
            raise ValueError(
                f"tool {name!r} already registered by {existing.module or '?'}; "
                f"pass override_of= to replace it intentionally")
        _TOOLS[name] = ToolSpec(name, schema, fn, acting, override_of,
                                getattr(fn, "__module__", ""))
        return fn
    return deco


def get_tool(name: str) -> Optional[ToolSpec]:
    return _TOOLS.get(name)


def all_tools() -> dict[str, ToolSpec]:
    return dict(_TOOLS)


def tool_schemas(names: Optional[list[str]] = None) -> list[dict]:
    """OpenAI-format schemas for registered plugin tools (optionally filtered to `names`)."""
    items = _TOOLS.values() if names is None else (_TOOLS[n] for n in names if n in _TOOLS)
    return [t.schema for t in items]


# --------------------------------------------------------------------------------------------
# Hook registry
# --------------------------------------------------------------------------------------------
HOOK_EVENTS = ("on_round_start", "on_choice_resolved", "on_action_executed", "on_session_end")
_HOOKS: dict[str, list[Callable]] = {e: [] for e in HOOK_EVENTS}


def register_hook(event: str) -> Callable:
    """Decorator: register a hook for one of HOOK_EVENTS. Fires on that game event even when no
    tool was called (e.g. update a Bayesian posterior on every `on_choice_resolved`)."""
    if event not in _HOOKS:
        raise ValueError(f"unknown hook event {event!r}; one of {HOOK_EVENTS}")

    def deco(fn: Callable) -> Callable:
        _HOOKS[event].append(fn)
        return fn
    return deco


def get_hooks(event: str) -> list[Callable]:
    return list(_HOOKS.get(event, []))


def clear_registry() -> None:
    """Test helper: wipe tool + hook registrations."""
    _TOOLS.clear()
    for e in _HOOKS:
        _HOOKS[e].clear()


# --------------------------------------------------------------------------------------------
# ToolContext — the interface plugins are handed (concrete impls: host + MockToolContext)
# --------------------------------------------------------------------------------------------
class _KV:
    """Minimal durable-KV interface. The host backs this with SQLite (ctx.persist); the mock
    backs it with a dict."""
    def __init__(self, data: Optional[dict] = None):
        self._d = data if data is not None else {}

    def get(self, key: str, default: Any = None) -> Any:
        return self._d.get(key, default)

    def set(self, key: str, value: Any) -> None:
        self._d[key] = value

    def setdefault(self, key: str, default: Any) -> Any:
        return self._d.setdefault(key, default)


class ToolContext:
    """Interface a tool/hook uses to reach the game. The host injects a concrete subclass bound
    to the live Session; `MockToolContext` implements it against a fixture for offline tests.

    Reads are instant (off the latest cached snapshot). `refresh_state`/`emit_commands`/
    `propose_choices`/`run_blocking` are async and routed through the host. State persists across
    calls at three scopes: `agent_store` (this officer), `session_store` (whole game), `persist`
    (durable, cross-game). Actions can ONLY be composed via `emit_commands` (canonical cmd tags).
    """
    agent: Any = None
    participant_id: Optional[str] = None
    session_id: Optional[str] = None
    round: int = 0
    agent_store: dict
    session_store: dict
    persist: _KV
    session_lock: "asyncio.Lock"

    # --- reads (sync) ---
    @property
    def state(self) -> dict:
        raise NotImplementedError

    def get_facilities(self) -> str: raise NotImplementedError
    def get_workforce(self) -> str: raise NotImplementedError
    def get_tasks(self) -> str: raise NotImplementedError
    def get_logistics(self) -> str: raise NotImplementedError
    def enumerate_actions(self) -> list: raise NotImplementedError
    def enumerate_choice_packages(self) -> list: raise NotImplementedError

    # --- async: pull / act / offload ---
    async def refresh_state(self) -> dict: raise NotImplementedError
    async def emit_commands(self, tags: str) -> ToolResult: raise NotImplementedError
    async def propose_choices(self, packages: list) -> ToolResult: raise NotImplementedError

    async def run_blocking(self, fn: Callable, *args, **kwargs) -> Any:
        """Offload heavy/synchronous work (e.g. MCMC) off the event loop. Default runs in the
        default executor; the mock runs inline."""
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, lambda: fn(*args, **kwargs))

    # --- logging ---
    def log(self, event_type: str, payload: Optional[dict] = None) -> None:
        raise NotImplementedError


class MockToolContext(ToolContext):
    """Fixture-backed ToolContext for offline plugin tests (`cora-plugin check`). Records what a
    tool would emit/propose/log so a smoke test can assert on it, with no Unity/LLM/network."""
    def __init__(self, state: Optional[dict] = None, *, agent: Any = None,
                 actions: Optional[list] = None, participant_id: str = "test-participant",
                 session_id: str = "test-session", round: int = 0):
        self._state = state or {}
        self._actions = actions or []
        self.agent = agent
        self.participant_id = participant_id
        self.session_id = session_id
        self.round = round
        self.agent_store: dict = {}
        self.session_store: dict = {}
        self.persist = _KV()
        self.session_lock = asyncio.Lock()
        # recordings for assertions
        self.emitted: list[str] = []
        self.proposed: list[list] = []
        self.logs: list[tuple] = []

    @property
    def state(self) -> dict:
        return self._state

    def _slice(self, key: str) -> str:
        import json
        return json.dumps(self._state.get(key, {}), default=str)

    def get_facilities(self) -> str: return self._slice("mapState")
    def get_workforce(self) -> str: return self._slice("workforceState")
    def get_tasks(self) -> str: return self._slice("tasks")
    def get_logistics(self) -> str: return self._slice("logistics")
    def enumerate_actions(self) -> list: return list(self._actions)
    def enumerate_choice_packages(self) -> list:
        return [a for a in self._actions if a.get("action_type") == "task_choice"]

    async def refresh_state(self) -> dict:
        return self._state

    async def emit_commands(self, tags: str) -> ToolResult:
        self.emitted.append(tags)
        return ToolResult(text=f"[mock] emitted: {tags}", executed=1)

    async def propose_choices(self, packages: list) -> ToolResult:
        self.proposed.append(packages)
        return ToolResult(text=f"[mock] proposed {len(packages)} package(s)")

    async def run_blocking(self, fn: Callable, *args, **kwargs) -> Any:
        return fn(*args, **kwargs)  # inline for deterministic tests

    def log(self, event_type: str, payload: Optional[dict] = None) -> None:
        self.logs.append((event_type, payload or {}))


# --------------------------------------------------------------------------------------------
# Invocation helpers (used by the host dispatch and by the test harness)
# --------------------------------------------------------------------------------------------
async def run_tool(spec: "ToolSpec | Callable", ctx: ToolContext, args: dict,
                   *, timeout: Optional[float] = 10.0) -> ToolResult:
    """Invoke a tool handler with sync/async support, a wall-clock timeout, and exception
    isolation. A handler that raises or overruns yields an error ToolResult rather than
    propagating — so one buggy plugin degrades to a tool error, never a router crash."""
    handler = spec.handler if isinstance(spec, ToolSpec) else spec
    name = spec.name if isinstance(spec, ToolSpec) else getattr(handler, "__name__", "tool")
    try:
        async def _call():
            res = handler(ctx, args)
            if inspect.isawaitable(res):
                res = await res
            return res
        result = await asyncio.wait_for(_call(), timeout=timeout) if timeout else await _call()
        if not isinstance(result, ToolResult):
            raise TypeError(f"tool {name!r} returned {type(result).__name__}, expected ToolResult")
        return result
    except asyncio.TimeoutError:
        return ToolResult(text=f"ERROR: tool {name!r} exceeded its {timeout}s time budget.")
    except Exception as e:  # exception isolation
        record_error("tool", name, e)
        print(f"[cora_ext] tool {name!r} raised — full traceback:")
        traceback.print_exc()          # full stack to the router log for debugging
        return ToolResult(text=f"ERROR: tool {name!r} failed: {type(e).__name__}: {e}")


async def run_hooks(event: str, ctx: ToolContext, event_obj: Any) -> None:
    """Fire all hooks for `event`, each isolated so one failure doesn't stop the others or the
    game. Sync or async handlers both supported."""
    for fn in get_hooks(event):
        try:
            res = fn(ctx, event_obj)
            if inspect.isawaitable(res):
                await res
        except Exception as e:
            # Best-effort: capture + log full stack; never let a hook break the game loop.
            hook_name = getattr(fn, "__name__", "?")
            record_error("hook", f"{hook_name}@{event}", e)
            print(f"[cora_ext] hook {hook_name} for {event} failed: "
                  f"{type(e).__name__}: {e} — full traceback:")
            traceback.print_exc()


# --------------------------------------------------------------------------------------------
# Plugin discovery
# --------------------------------------------------------------------------------------------
def load_plugins(dirs: "list[str | Path]", *, entry_point_group: str = "cora.plugins") -> list[str]:
    """Import every ``*.py`` under each dir (recursively) plus any installed ``cora.plugins``
    entry points, triggering their register_* decorators. Returns the loaded module names.
    Import errors in one plugin are logged and skipped, not fatal."""
    loaded: list[str] = []
    _LOAD_ERRORS.clear()
    for d in dirs:
        base = Path(d)
        if not base.exists():
            continue
        for path in sorted(base.rglob("*.py")):
            if path.name.startswith("_"):
                continue
            mod_name = f"cora_plugin_{path.stem}"
            try:
                spec = importlib.util.spec_from_file_location(mod_name, path)
                module = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(module)  # side effect: register_* run
                loaded.append(mod_name)
            except Exception as e:
                _LOAD_ERRORS.append({"plugin": str(path), "error": f"{type(e).__name__}: {e}",
                                     "traceback": traceback.format_exc()})
                print(f"[cora_ext] failed to load plugin {path}: {type(e).__name__}: {e}")
    try:
        from importlib.metadata import entry_points
        for ep in entry_points(group=entry_point_group):
            try:
                ep.load()
                loaded.append(ep.name)
            except Exception as e:
                _LOAD_ERRORS.append({"plugin": f"entry-point:{ep.name}",
                                     "error": f"{type(e).__name__}: {e}"})
                print(f"[cora_ext] failed to load entry-point plugin {ep.name}: {e}")
    except Exception:
        pass
    return loaded
