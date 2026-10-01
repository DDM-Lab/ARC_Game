"""
ARC Game Multi-Agent Router (multi-tenant service).

Runs as a FastAPI service that hosts many concurrent Unity clients. Each
client opens a WebSocket, sends a ``hello`` frame with its API key + chosen
config name, and the server constructs an isolated :class:`Session` to drive
that game.

Flow per session:
  1. WebSocket accepted; first frame must be ``{type: hello, api_key, config}``
  2. Server validates the key, loads the named config, creates a Session
  3. Server replies ``{type: hello_ack, session_id, ...}``
  4. From that point the existing message protocol takes over:
     begin_round, choice_made, director_message, request_reproposal, etc.

Usage:
    python agent_router.py --keys-file config/keys.json \
                           --config-dir config/ \
                           --port 9876 \
                           --log-dir logs/sessions
"""
from __future__ import annotations

import asyncio
import json
import argparse
import difflib
import hashlib
import os
import re
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Tuple, Optional

from fastapi import FastAPI, WebSocket, WebSocketDisconnect, Header, HTTPException, Request, Response
from fastapi.middleware.cors import CORSMiddleware
from starlette.websockets import WebSocketState
import uvicorn

import agent_config
from agent_config import AgentConfig, RouterConfig, load_config
from agent_filters import filter_observation, filter_actions
from agent_ordering import get_agent_order
from episode_logger import EpisodeLogger
from agent_config import load_global_prompt
from continuous_agent import (build_tools, run_tool_step, DEFAULT_TOOLS,
                              known_ctx_limit, _est_prompt_tokens)
from cora.actions import enumerate_actions
from cora.tools import TOOLS, TOOL_BY_NAME
# The typed action tools (build/hire/train/staff/deconstruct/task/transfer) the officer emits.
# Each is translated to its command tag and routed through the execute_commands path.
_CORA_ACTION_TOOLS = {t["name"] for t in TOOLS}
import bundle as bundle_mod
from bundle import load_bundle, BundleError
import cora_ext
import plugin_store
import key_store
from cmd_parser import parse_commands, translate_tool_calls, ParserEnv  # router-only tag adapter
from cora.scoring import REWARD_WEIGHTS, score_components
from cora.observation import officer_text, task_officer, task_group, task_token, vehicle_capacity


def _num(v, default=0):
    """A number for $-formatting; anything else formats as `default`."""
    return v if isinstance(v, (int, float)) else default
from message_queue import MessageQueue
import re

# Action types that are site/target-bound and NOT legitimately repeatable within a
# planning phase (building a site, demolishing it, assigning workers to a specific
# building are idempotent). Quantity actions — hiring more workers, transferring
# more people — CAN legitimately repeat, so ledger_mode="block" leaves them alone.
_NON_REPEATABLE_TYPES = {"construction", "deconstruction", "worker_assignment"}


# The router's parser shim IS the shared cmd_parser.ParserEnv — one shim across
# every arm (router, gym, benchmark). Kept as a named alias because call sites and
# comments reference _CmdParseShim; the isolation semantics (private valid_actions
# copy so <staff> synth-append never touches the router's real list) live there.
_CmdParseShim = ParserEnv


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


class Session:
    """One isolated game session for a single connected Unity client.

    Owns all per-user state: WebSocket, message queue, episode logger, pending
    choice/action futures, choice context, and round counter. Many sessions
    can run concurrently inside the same FastAPI process.
    """

    def __init__(
        self,
        config: RouterConfig,
        session_id: str,
        api_key_label: str,
        log_path: str,
        websocket: WebSocket,
        director_policy=None,
    ):
        """director_policy: for sessions with no human Director (the headless harness), a
        callable (packages, game_state, reasoning) -> package index or None that answers
        officers' proposals; standing-order cards are then declined. None = a human Director
        answers through the client."""
        self.config = config
        self._director_policy = director_policy
        self.session_id = session_id
        self.api_key_label = api_key_label
        self.logger = EpisodeLogger(log_path)
        self.message_queue = MessageQueue()
        self.episode_id: str = self.logger.new_episode()
        self.round_num: int = 0
        self.day: int = 1
        self.segment: int = 0
        self._websocket: WebSocket = websocket
        self._pending_choice: Optional[asyncio.Future] = None
        self._pending_action: Optional[asyncio.Future] = None
        # The action_id the armed future is waiting for, or None when the in-flight frame
        # carries no action_id (select_task_choice). See _handle_action_result: without this,
        # a result that arrives AFTER its sender timed out is handed to whichever officer is
        # waiting next, because results correlate by timing alone.
        self._pending_action_key: Optional[str] = None
        # Correlated response slot for an on-demand get_game_state pull (see
        # _fetch_fresh_state). Unity only pushes state on begin_round + execute
        # results, so we pull to see changes the router didn't cause.
        self._pending_state: Optional[asyncio.Future] = None
        # Whether this transport can execute a task-choice answer (select_task_choice).
        # Live WebSocket clients handle it (WebSocketManager.select_task_choice ->
        # TaskDetailUI.SelectTaskChoiceHeadless), so enable it whenever a real
        # websocket is attached. The headless harness constructs a Session with
        # websocket=None and sets this True itself once it wires the gym-TCP bridge.
        self._task_choice_supported: bool = websocket is not None
        # Freshest game state/action enumeration seen this session. Needed so a
        # continuous agent can re-enter its tool loop on a mid-round
        # director_message (which carries no game_state of its own).
        self._latest_game_state: dict = {}
        self._latest_all_actions: List[dict] = []
        # Per-officer turn locks (lazily created in _agent_lock). Serialize two
        # turns of the SAME officer (transcript integrity) while letting DIFFERENT
        # officers run concurrently. Replaces the old session-global
        # _continuous_turn_lock, which serialized ALL officers and so precluded the
        # concurrency we now want. Cross-officer Unity contention is handled at the
        # finer boundaries below.
        self._agent_turn_locks: Dict[str, asyncio.Lock] = {}
        # Serializes ONLY the Unity mutation critical section (create-future → send
        # → await result). Unity processes one execute_action at a time and results
        # correlate by timing, not id, so at most one request may be in flight —
        # otherwise concurrent officers clobber the single-slot _pending_action and
        # scramble each other's results. Short-held: the slow LLM tool-loop thinking
        # runs OUTSIDE this lock, so officers still overlap where it matters.
        self._unity_commit_lock: asyncio.Lock = asyncio.Lock()
        # --- Plugin (cora_ext) per-session state: three store scopes + a lock for shared
        # writes. In-memory for now; ctx.persist becomes SQLite-backed in a later slice. ---
        self._plugin_session_store: dict = {}
        self._plugin_agent_stores: dict = {}          # subagent_name -> dict
        self._plugin_persist = plugin_store.default_store()   # durable, process-wide, cross-game
        self._plugin_session_lock: asyncio.Lock = asyncio.Lock()
        # Serializes the human's ATTENTION for propose_choices. Only one proposal
        # can sit on the single-slot _pending_choice + one modal UI at a time. Held
        # for the whole time a proposal is pending on screen (up to 5min) — but does
        # NOT block other officers' execute_game_action commits (that's the separate
        # commit lock), so a parked proposal never freezes the acting officers.
        self._director_attention_lock: asyncio.Lock = asyncio.Lock()
        # Ledger of actions a continuous agent has committed during the current
        # paused planning phase. The observable game state is FROZEN while paused
        # (budget/population/facilities don't move until the round simulates), so a
        # re-reading agent can't see its own queued work and re-proposes duplicates
        # (which then fail "site not available"). We surface this ledger in each
        # turn's opening context and clear it when the round advances (world catches
        # up). Grounding only — never gates the agent's choices.
        self._committed_this_phase: List[str] = []
        # Running $ total of what has been committed this planning phase. The frozen
        # observation cannot show it (actions are queued, not resolved), so without this
        # the officer reasons about spend against a budget that never moves.
        self._committed_spend_this_phase: float = 0.0
        # Persistent per-agent tool-loop transcript for continuous agents. Unlike
        # every other actor type (which rebuilds its prompt from the MessageQueue
        # each turn), a continuous agent carries ONE growing OpenAI-shape
        # conversation for the whole game: every step's reasoning, tool call, and
        # tool result stays visible across activations AND across rounds. Keyed by
        # subagent_name; reset only on game_start.
        self._continuous_transcripts: Dict[str, List[dict]] = {}
        # How many Director→agent messages we've already folded into each agent's
        # transcript. Re-entry injects only NEW director input — the agent's own
        # outputs are already present as assistant/tool turns, so re-pulling the
        # whole conversation would duplicate them.
        # (name, partner) -> how many of that partner's messages this officer has already
        # seen. Replaces the director-only counter so peer threads are tracked too.
        self._msg_injected_count: dict = {}
        # Monotonic count of game-state snapshots the router has adopted. An officer's turn
        # records the version it observed; each action it executes records the version live
        # at dispatch. The gap is how stale the decision was -- a bad decision and an
        # out-of-date one look identical in the log without it.
        self._state_version: int = 0
        # agent name -> provenance of the turn it is running now ({turn_id, caused_by}).
        # Per-agent turns are serialized by _agent_lock, so one slot per name is exact.
        # Messages sent mid-turn read it to record which turn (and which message) caused them.
        self._turn_ctx: Dict[str, dict] = {}
        # Standing orders (add_to_autonomy_list). officer name -> accepted rules, each
        # {rule_id, tool, args, context, original_context, edited, round, proposal_id}.
        # Ids are session-wide (R1, R2, ...) so the Director can name one unambiguously.
        # Held for the life of this session only; a checkpoint does not carry them.
        self._autonomy_rules: Dict[str, List[dict]] = {}
        self._autonomy_seq: int = 0
        # proposal_id of the standing-order card on screen now. It shares _pending_choice
        # (and _director_attention_lock) with propose_choices, so one card at a time, and
        # every existing supersede path (new round, new instruction, checkpoint load) also
        # withdraws a pending standing-order card.
        self._pending_autonomy_id: Optional[str] = None
        # Developer-panel prompt edits for THIS session only (never written into the shared
        # config objects, which other sessions may hold). None = use the config / default.
        # Read by _resolve_global_prompt and _continuous_system_message; an officer picks a
        # change up at the start of its next turn, keeping its conversation history.
        self._prompt_overrides: dict = {"global_behavior": None, "global_manual": None,
                                        "tool_policy": None, "agents": {}}
        # agent name -> {state: idle|queued|thinking|acting, since, turn_id, trigger, ...}
        # for the developer panel's live view. Purely observational.
        self._agent_status: Dict[str, dict] = {}
        # request_id -> future resolved by the client's checkpoint_load_result.
        self._pending_checkpoint_loads: Dict[str, asyncio.Future] = {}
        self._director_agent: Optional[AgentConfig] = self._find_director()

    def _find_director(self) -> Optional[AgentConfig]:
        """Find the director agent in the configuration."""
        for agent in self.config.agents:
            if agent.role == "director":
                return agent
        return None

    # ── Per-actor action logging ─────────────────────────────────

    def _actor_for(self, agent: Optional[AgentConfig]) -> dict:
        """Build the `actor` block for a logged action.

        Mapping: subagent → llm_agent; director → human, or auto_director when a
        director_policy answers for it. Falls back to the human director if the agent
        can't be resolved.
        """
        if agent is None:
            return dict(HUMAN_DIRECTOR_ACTOR)
        if agent.role == "director":
            kind = "auto_director" if self._director_policy else "human"
        else:
            kind = "llm_agent"
        return {
            "kind": kind,
            "name": agent.subagent_name,
            "role": agent.role,
            "actor_type": agent.actor_type,
        }

    def _emit(self, event_type: str, fields: Optional[dict] = None, *,
              actor=None, agent=_UNSET, client_ts=None) -> None:
        """Sole chokepoint for structured event logging — the ONLY caller of logger.log_event.

        Centralizes three things every event needs to be consistent:
          * actor — always the canonical dict block. Pass `agent=<AgentConfig|None>` to
            derive it via `_actor_for`, or `actor=<dict|str>` directly (strings are
            wrapped). `agent=None` maps to the human Director.
          * core session ids (session_id/episode_id/round/day/segment).
          * time — `client_ts` (client-side stamp) is kept DISTINCT from the server
            `timestamp` that log_event stamps; they are never conflated.
        Event-specific fields pass through via `fields`. Untrusted payloads (e.g. the
        plugin ctx.log) must be passed as fields={"payload": ...} so they nest instead
        of splatting arbitrary keys into the record.
        """
        if actor is None and agent is not _UNSET:
            actor = self._actor_for(agent)
        elif isinstance(actor, str):
            actor = _wrap_actor(actor)
        record = {
            "event_type": event_type,
            "schema_version": 1,
            "session_id": self.session_id,
            "episode_id": self.episode_id,
            "round": self.round_num,
            "day": self.day,
            "segment": self.segment,
        }
        if actor is not None:
            record["actor"] = actor
        if client_ts is not None:
            record["client_ts"] = client_ts
        if fields:
            record.update(fields)
        self.logger.log_event(record)

    def _log_action(self, actor: dict, category: str, name: str,
                    payload: dict, click_seq=None, client_ts=None) -> None:
        """Append one unified, actor-tagged `action` event to the session JSONL.

        `timestamp` (added by log_event) is server-receive time — authoritative for
        ordering. `client_ts` is the Unity-side UTC stamp from the originating
        frame, for precise human-action timing (e.g. time-to-complete-task).
        """
        self._emit("action", {
            "category": category,
            "name": name,
            "payload": payload,
            "click_seq": click_seq,
        }, actor=actor, client_ts=client_ts)

    # ── Action-outcome instrumentation ───────────────────────────
    # These enrich every logged continuous-agent action with an engine-truth
    # `outcome` plus factual state deltas, so post-hoc analysis can separate
    # three failure classes that all look identical in the old logs:
    #   • can't-execute  → outcome in {"invalid","rejected"}
    #   • misunderstanding (inert success) → outcome=="ok" but state_changed
    #        is False (or, for a task_choice, task_closed is False)
    #   • model judgment → outcome=="ok" + state_changed, read via the deltas
    #        and cross-agent conflicts
    # `outcome` is engine truth, NOT interpretation:
    #   invalid  = never reached the engine (bad index / out-of-scope / no such
    #              task) — the model referenced something outside its space
    #   rejected = reached the engine, engine refused (success=False)
    #   ok       = engine accepted (success=True)
    @staticmethod
    def _state_metrics(gs: dict) -> dict:
        """Snapshot the factual signals an action can move."""
        tasks = gs.get("allActiveTasks") or []
        return {
            "budget": _get_budget(gs),
            "satisfaction": _get_satisfaction(gs),
            "active_tasks": frozenset(
                t.get("taskId") for t in tasks if t.get("taskId") is not None),
        }

    @staticmethod
    def _outcome_fields(outcome: str, before: dict | None = None,
                        after: dict | None = None, *,
                        is_choice: bool = False, tid=None) -> dict:
        """Build the outcome/delta payload fragment merged into a logged action.

        `before`/`after` are _state_metrics snapshots taken around the engine
        call. For pre-engine failures (invalid), pass neither — deltas are null.
        """
        f = {"outcome": outcome}
        if before is not None and after is not None:
            d_budget = after["budget"] - before["budget"]
            d_sat = after["satisfaction"] - before["satisfaction"]
            tasks_changed = before["active_tasks"] != after["active_tasks"]
            f["budget_delta"] = d_budget
            f["satisfaction_delta"] = d_sat
            # `state_changed` is a fact (did any tracked signal move), NOT a
            # judgment. A success with state_changed=False is the inert-success
            # signature of a game misunderstanding.
            f["state_changed"] = bool(d_budget or d_sat or tasks_changed)
            if is_choice:
                # A choice closes iff its task left the active set. success=True
                # with task_closed=False = the deferred-choice "inert" case.
                f["task_closed"] = (tid is not None
                                    and tid not in after["active_tasks"])
        return f

    # ── WebSocket Handler ────────────────────────────────────────

    async def run(self):
        """Drive the per-session receive loop. Caller has already accepted
        the WebSocket and completed the hello handshake.
        """
        print(f"[router][{self.api_key_label}] session {self.session_id[:8]} active.")
        try:
            while True:
                raw_msg = await self._websocket.receive_text()
                await self._handle_message(raw_msg)
        except Exception as e:
            print(f"[router][{self.api_key_label}] WebSocket error: {e}")
        finally:
            try:
                self._emit("session_end", {
                    "label": self.api_key_label,
                    "rounds_played": self.round_num,
                })
            except Exception:
                pass
            try:
                await self._fire_hooks("on_session_end", {"rounds_played": self.round_num})
            except Exception:
                pass
            print(f"[router][{self.api_key_label}] session {self.session_id[:8]} closed.")

    # ── Message Dispatch ─────────────────────────────────────────

    async def _handle_message(self, raw: str):
        try:
            msg = json.loads(raw)
        except json.JSONDecodeError:
            print(f"[router] Non-JSON message ignored: {raw[:80]}")
            return

        msg_type = msg.get("type")
        if msg_type is None:
            # Messages without 'type' field are execute_action results
            if "success" in msg and "action_id" in msg:
                await self._handle_action_result(msg)
                return
            print(f"[router] Message missing 'type' field: {raw[:200]}")
            return
        if msg_type == "game_state_response":
            # Correlated reply to a get_game_state pull (see _fetch_fresh_state).
            if self._pending_state is not None and not self._pending_state.done():
                self._pending_state.set_result(msg)
            return
        if msg_type == "checkpoint_load_result":
            # Correlated reply to a developer-panel load_checkpoint (see dev_load_checkpoint).
            fut = self._pending_checkpoint_loads.pop(msg.get("request_id"), None)
            if fut is not None and not fut.done():
                fut.set_result(msg)
            return
        print(f"[router] Received message type: {msg_type}")

        if msg_type == "game_end":
            self._emit_round_state(msg.get("game_state") or {}, phase="game_end")
        elif msg_type == "provenance":
            self._handle_provenance(msg)
        elif msg_type == "begin_round":
            # Run as background task so receive loop stays active for choice_made messages
            _t = asyncio.create_task(self._handle_begin_round(msg))
            _t.add_done_callback(self._on_round_task_done)
        elif msg_type == "request_agent_decision":
            # Legacy single-agent message; GlobalClock still emits it alongside begin_round.
            # Ignore to avoid running the round twice per simulation tick.
            print("[router] Ignoring legacy 'request_agent_decision' (begin_round drives the round).")
        elif msg_type == "game_start":
            self._handle_game_start(msg)
        elif msg_type == "choice_made":
            await self._handle_choice_made(msg)
        elif msg_type == "autonomy_decision":
            self._handle_autonomy_decision(msg)
        elif msg_type == "director_message":
            await self._handle_director_message(msg)
        elif msg_type == "round_end":
            self._handle_round_end(msg)
        elif msg_type == "client_event":
            self._handle_client_event(msg)
        elif msg_type == "gui_event":
            self._handle_gui_event(msg)
        else:
            print(f"[router] Unknown message type: {msg_type}")

    # ── Round Orchestration ──────────────────────────────────────

    async def _handle_begin_round(self, msg: dict):
        self.round_num += 1
        # Peer-triggered activations left this round. A message to a colleague WAKES them,
        # exactly like a director message does, so officers can actually converse. That makes
        # an A->B->A chain possible by design -- it is a model behaviour to observe (and train
        # out), not something to prevent in the harness. What the harness does owe you is that
        # a runaway chain cannot silently consume the whole session: the budget bounds it,
        # every hop is logged, and exhaustion is announced rather than swallowed. Raise or
        # lower it per config with `peer_trigger_budget`.
        self._peer_triggers_left = int(
            getattr(self.config, "peer_trigger_budget", None) or PEER_TRIGGER_BUDGET_PER_ROUND)
        self.day = msg.get("day", self.day)
        self.segment = msg.get("segment", self.segment)
        game_state = msg.get("game_state", {})
        print(f"\n[router] === Round {self.round_num} | "
              f"Day {msg.get('day', 1)} Seg {msg.get('segment', 0)} ===")

        # A new round means the human advanced without acting on any proposal
        # still on screen. Release an officer parked at propose_choices so it stops
        # holding _director_attention_lock (else the next proposer — and, if it is
        # the same officer, this round's turn behind its per-agent lock — would wait
        # out the full 5min proposal timeout, appearing frozen).
        self._supersede_pending_choice("new round started")
        await self._fire_hooks("on_round_start",
                               {"round": self.round_num, "day": self.day, "segment": self.segment})

        # The round advanced: the world simulates and the fresh game_state now
        # reflects everything queued last phase. The committed-this-phase ledger is
        # stale — drop it so it doesn't double-count into the new phase.
        if self._committed_this_phase:
            print(f"[router]   🧾 Clearing planning-phase ledger "
                  f"({len(self._committed_this_phase)} committed action(s)).")
            self._committed_this_phase = []
            self._committed_spend_this_phase = 0.0

        # Validate game state has required fields
        self._validate_game_state(game_state)
        # Per-round state record. Written for EVERY session, LLM or not: before this, a
        # human-only game logged clicks and nothing about how the game was going round by
        # round, so it could not be compared with a benchmark episode at all.
        self._emit_round_state(game_state, phase="round_start")

        # Enumerate full action space from current state
        all_actions = _enumerate_actions(game_state)

        # Get ordered subagents
        ordered = get_agent_order(
            self.config.agent_order_rule,
            self.config.agents,
            game_state,
            self.round_num,
            []
        )

        # Stash the freshest state so a mid-round director_message to a
        # continuous agent can re-enter its tool loop (see _handle_director_message)
        # and so the concurrent officers below read a consistent starting snapshot.
        self._latest_game_state = game_state
        self._latest_all_actions = all_actions
        self._state_version += 1

        # Split by actor_type. Non-continuous actors (auto/choices/coach) keep the
        # sequential, state-threading semantics they were designed around — they run
        # first, one after another. Continuous officers then run their tool-loops
        # CONCURRENTLY: each reads the freshest shared snapshot and publishes its
        # result, while the Unity socket is arbitrated by the commit/attention locks.
        continuous = [a for a in ordered if a.actor_type == "continuous"]
        others = [a for a in ordered if a.actor_type != "continuous"]

        for agent in others:
            game_state, all_actions = await self._run_subagent(
                agent, game_state, all_actions
            )
            self._latest_game_state = game_state
            self._latest_all_actions = all_actions
            self._state_version += 1

        if continuous:
            print(f"[router] Running {len(continuous)} continuous officer(s) "
                  f"concurrently: {[a.subagent_name for a in continuous]}")
            results = await asyncio.gather(
                *[self._run_continuous_concurrent(agent) for agent in continuous],
                return_exceptions=True,
            )
            for agent, res in zip(continuous, results):
                if isinstance(res, Exception):
                    import traceback
                    print(f"[router] ❌ officer {agent.subagent_name} FAILED: "
                          f"{type(res).__name__}: {res}")
                    traceback.print_exception(type(res), res, res.__traceback__)
            # Officers published their results into _latest_* as they finished; the
            # director_turn should carry the post-officers world.
            game_state = self._latest_game_state
            all_actions = self._latest_all_actions

        # Signal director turn
        await self._send({"type": "director_turn", "game_state": game_state,
                          "timestamp": _now()})
        print("[router] director_turn sent.")

    def _on_round_task_done(self, task: "asyncio.Task"):
        """Surface exceptions from the fire-and-forget begin_round task.

        Without this, any error inside _handle_begin_round is swallowed and the
        round dies silently (no re-proposal on later turns). Log it loudly.
        """
        if task.cancelled():
            print("[router] ⚠️  begin_round task was CANCELLED")
            return
        exc = task.exception()
        if exc is not None:
            import traceback
            print(f"[router] ❌ begin_round task FAILED: {type(exc).__name__}: {exc}")
            traceback.print_exception(type(exc), exc, exc.__traceback__)

    async def _run_subagent(
        self,
        agent: AgentConfig,
        game_state: dict,
        all_actions: List[dict],
    ) -> Tuple[dict, List[dict]]:
        """Run one subagent turn. Returns updated (game_state, all_actions)."""
        print(f"[router] Subagent: {agent.subagent_name} ({agent.actor_type})")

        filtered_state = self._filter_state(game_state, agent)
        filtered_actions = filter_actions(all_actions, agent.subaction_space)

        if not filtered_actions:
            print(f"[router]   No valid actions in subaction_space — skipping.")
            return game_state, all_actions

        if agent.actor_type == "continuous":
            game_state, all_actions = await self._run_continuous(
                agent, filtered_state, filtered_actions, game_state, all_actions
            )

        return game_state, all_actions

    async def _execute_one_action_via_unity(
        self,
        action: dict,
        game_state: dict,
    ) -> Tuple[dict, dict]:
        """Send ONE game action to Unity and await its engine-truth result.

        Single-in-flight discipline: hold the commit lock across arm-future → send →
        await so concurrent officers can't clobber the single-slot _pending_action.
        Returns (result, game_state) with game_state refreshed from the result. This is
        the per-action primitive shared by _execute_actions_via_unity and execute_resolved.
        """
        async with self._unity_commit_lock:
            loop = asyncio.get_event_loop()
            self._pending_action = loop.create_future()
            self._pending_action_key = action.get("action_id")

            # Send execute_action to Unity
            await self._send({
                "type": "execute_action",
                "action": action,
                "timestamp": _now(),
            })

            try:
                result_msg = await asyncio.wait_for(self._pending_action, timeout=30.0)
            except asyncio.TimeoutError:
                result_msg = None
            finally:
                self._pending_action = None
                self._pending_action_key = None

        if result_msg is not None:
            result = {
                "action_id": action.get("action_id", "unknown"),
                "success": result_msg.get("success", False),
                "error_message": result_msg.get("error_message", ""),
            }
            # Update game state from result
            if "game_state" in result_msg:
                game_state = result_msg["game_state"]
        else:
            print(f"[router]   ⚠️  Timeout executing action {action.get('action_id', 'unknown')}")
            result = {
                "action_id": action.get("action_id", "unknown"),
                "success": False,
                "error_message": "Timeout waiting for Unity execution",
            }
        return result, game_state

    async def _execute_actions_via_unity(
        self,
        actions: List[dict],
        game_state: dict
    ) -> Tuple[List[dict], dict]:
        """Execute a list of game actions in order (thin loop over the per-action primitive).

        Per-action (not whole-batch) commit-lock discipline so a long package doesn't block
        other officers longer than necessary. Publishes the freshest global state at the end.
        """
        exec_results = []
        for action in actions:
            r, game_state = await self._execute_one_action_via_unity(action, game_state)
            exec_results.append(r)
        # Publish freshest global state.
        self._publish_state(game_state)
        return exec_results, game_state

    async def execute_resolved(
        self,
        items: List[dict],
        *,
        game_state: dict,
        scope_agent: Optional["AgentConfig"] = None,
    ) -> Tuple[List[dict], dict]:
        """The one executor every wing shares — run a resolved, ORDERED stream as-chosen.

        Each item is either {"kind": "action", "action": {...}} or
        {"kind": "choice", "taskId": int, "choiceId": int}. Items execute in the order
        given; Unity is the sole judge (NO local pre-validation/skip; failures recorded as
        engine truth, never remapped); execution CONTINUES past failures; game_state is
        refreshed between items so later items see earlier effects (e.g. hire-then-staff).

        Front-ends (tool_calls / cmd-tags / idx) resolve to this ordered stream via the
        shared resolver (cmd_parser's _PRIO reorder + sim_wf); this method does NOT reorder.
        `scope_agent` is advisory (actor tag / caller logging); scope filtering is applied
        by the caller BEFORE building the item stream.
        """
        results: List[dict] = []
        for item in items:
            if item.get("kind") == "choice":
                r, game_state = await self._execute_choice_via_unity(
                    int(item["taskId"]), int(item["choiceId"]), game_state)
            else:
                r, game_state = await self._execute_one_action_via_unity(
                    item["action"], game_state)
            results.append(r)
        # Publish freshest global state.
        self._publish_state(game_state)
        return results, game_state

    def _may_answer_task(self, agent: "AgentConfig", task: dict) -> bool:
        """True if `agent`'s subaction_space admits this task's coarse group.

        Probes the SAME filter every action goes through (a synthetic task_choice
        row), so a {"category":"all"} director and a
        {"category":"task_choice","group":<slug>} officer are both handled by one
        code path — the config is the single source of truth for who answers what.
        """
        probe = {"action_type": "task_choice",
                 "task_choice": {"group": task_group(task)}}
        return bool(filter_actions([probe], agent.subaction_space))

    async def _execute_choice_via_unity(
        self,
        task_id: int,
        choice_id: int,
        game_state: dict,
    ) -> Tuple[dict, dict]:
        """Answer a choice task via Unity (select_task_choice) and await the result.

        Mirrors _execute_actions_via_unity's single-in-flight discipline (hold the
        commit lock across arm-future → send → await so concurrent officers can't
        clobber the single-slot _pending_action). The frame carries `taskId`/`choiceId`
        (camelCase) PLUS `stableId` — Unity matches a task by its stable id when the
        transient int has gone stale (a peer's commit re-issued task ids). We also
        re-resolve the transient int against the FRESHEST state at send time and, on a
        hard 'not found', re-resolve once more and retry before failing terminally.
        Returns (result_msg, game_state) with game_state refreshed from the result.
        """
        def _find_by_tid(state, tid):
            for t in (state.get("allActiveTasks") or []):
                if t.get("taskId") == tid:
                    return t
            return None

        def _tid_for_stable(state, stable):
            if not stable:
                return None
            for t in (state.get("allActiveTasks") or []):
                if t.get("stableTaskId") == stable:
                    return t.get("taskId")
            return None

        # Stable id comes from the task row the caller resolved `task_id` against.
        src_task = _find_by_tid(game_state, task_id)
        stable_id = (src_task or {}).get("stableTaskId") or ""

        # Re-resolve the transient int against the freshest state we have.
        fresh = self._latest_game_state or game_state
        resolved_tid = _tid_for_stable(fresh, stable_id)
        if resolved_tid is None:
            resolved_tid = task_id

        async def _send_once(tid):
            async with self._unity_commit_lock:
                loop = asyncio.get_event_loop()
                self._pending_action = loop.create_future()
                # WebSocketManager.cs replies with action_id = "choice_{taskId}_{choiceId}",
                # so this frame DOES carry one. Correlating on it stops a stray result from a
                # previously timed-out execute_action landing on this waiter.
                self._pending_action_key = f"choice_{int(tid)}_{int(choice_id)}"
                await self._send({
                    "type": "select_task_choice",
                    "taskId": int(tid),
                    "choiceId": int(choice_id),
                    "stableId": stable_id,  # empty string ⇒ Unity uses no fallback
                    "timestamp": _now(),
                })
                try:
                    return await asyncio.wait_for(self._pending_action, timeout=30.0)
                except asyncio.TimeoutError:
                    return None
                finally:
                    self._pending_action = None
                    self._pending_action_key = None

        def _is_not_found(m):
            return (m is not None and not m.get("success")
                    and "not found" in (m.get("error_message") or "").lower())

        result_msg = await _send_once(resolved_tid)

        # Retry guard for a hard 'not found': re-resolve ONCE against the freshest
        # state and retry. If it still fails, return a terminal, non-retryable result
        # (coordinates with the per-turn retry cap so this isn't re-attempted).
        if _is_not_found(result_msg):
            latest = self._latest_game_state or fresh
            retry_tid = _tid_for_stable(latest, stable_id)
            if retry_tid is None:
                retry_tid = resolved_tid
            result_msg = await _send_once(retry_tid)
            if _is_not_found(result_msg):
                game_state = result_msg.get("game_state") or game_state
                self._publish_state(game_state)
                return ({"success": False, "terminal": True,
                         "error_message": result_msg.get("error_message", "task not found")},
                        game_state)

        if result_msg is not None and "game_state" in result_msg:
            game_state = result_msg["game_state"]
        self._publish_state(game_state)
        return (result_msg or {"success": False,
                               "error_message": "Timeout selecting task choice"}), game_state

    async def _await_director_choice(
        self,
        packages: List[dict],
        filtered_actions: List[dict],
        game_state: dict,
        reasoning: str,
    ) -> Tuple[Optional[int], List[dict], dict, bool]:
        """Resolve which package the director picks and gather its execution results.

        Shared by _run_choices (Task Center) and _continuous_propose (inline). For an
        autonomous director we select + execute via Unity here; for a manual director
        the client executes and returns execution_results in choice_made, so we only
        arm _pending_choice and await it.

        Returns (selected_idx, exec_results, game_state, superseded). selected_idx is
        None when nothing landed (invalid pick, timeout, or a superseded proposal).
        """
        # No human Director (headless harness): the policy picks and we execute immediately.
        if self._director_policy is not None:
            selected_idx = self._director_policy(packages, game_state, reasoning)
            if selected_idx is not None and 0 <= selected_idx < len(packages):
                print(f"[router]   ✅ Director selected package {selected_idx}")
                actions_to_execute = [filtered_actions[i]
                                      for i in packages[selected_idx]["action_indices"]
                                      if i < len(filtered_actions)]
                exec_results, game_state = await self._execute_actions_via_unity(
                    actions_to_execute, game_state)
            else:
                print(f"[router]   ⚠️  Invalid package index {selected_idx}, skipping execution")
                selected_idx = None
                exec_results = []
            return selected_idx, exec_results, game_state, False

        # Manual director: the client executes; we await its choice_made frame.
        loop = asyncio.get_event_loop()
        fut = loop.create_future()
        self._pending_choice = fut
        print("[router]   ⏳ Awaiting director choice (5min timeout)...")
        try:
            choice_msg = await asyncio.wait_for(self._pending_choice, timeout=300.0)
            if choice_msg.get("superseded"):
                print("[router]   ↩️  Proposal superseded before the director chose.")
                return None, [], game_state, True
            print("[router]   ✅ Received director choice!")
            selected_idx = choice_msg.get("package_index", 0)
            exec_results = choice_msg.get("execution_results", [])
            # The client's choice_made frame normally carries the post-execution
            # game_state. If it omits it, don't silently fall back to the frozen
            # pre-choice snapshot — prefer the freshest state the router has seen
            # (best-effort; no forced Unity round-trip).
            cm_state = choice_msg.get("game_state")
            if cm_state:
                game_state = cm_state
            elif self._latest_game_state:
                game_state = self._latest_game_state
            return selected_idx, exec_results, game_state, False
        except asyncio.TimeoutError:
            print("[router]   ⚠️  Timeout (5min) waiting for choice_made.")
            return None, [], game_state, False
        finally:
            # Clear the slot only if it is still OURS. A second proposer overwrites it, and an
            # unconditional clear here nulls the other waiter's live future, stranding it for
            # the full 300s timeout.
            if self._pending_choice is fut:
                self._pending_choice = None

    # ── Continuous agent (tool-using loop) ───────────────────────────────
    #
    # A single provider-agnostic tool loop. The agent holds the FULL tool
    # palette every step (execute / propose / talk / read / list / finish) and
    # chooses which to use — interaction *style* is emergent from those choices,
    # never imposed by the router. There is no safety floor in Phase 1: an
    # execute is committed on the agent's own judgment and the engine reports
    # the honest result. See continuous_agent.py and CONTINUOUS_AGENT.md.

    # Guidance appended to the system prompt. Encodes the 2026 interaction-style
    # findings as *judgment for the model to weigh*, not as gates in code.
    _CONTINUOUS_TOOL_POLICY = (
        "You are operating as a continuous agent with a full palette of tools. "
        "Each step you may take ONE or more tool calls, or stop. Pick tools by "
        "reading the situation — nothing forces a particular style on you:\n"
        "- Action tools — build / hire / train / staff / deconstruct / task / transfer: act directly "
        "and immediately. Call the typed tool for what you want (e.g. build(type='kitchen', "
        "site_id=3), hire(kind='untrained', count=4), task(task_id='FOOD_C01', choice_id=1)), "
        "using values from the OPTIONS list you were shown. You describe WHAT you want, never "
        "a menu index; each call is resolved against the live state and committed on your own "
        "judgment. You may make several action calls in one step. Use them when you are "
        "confident and the action is within your remit.\n"
        "- propose_choices: hand the decision to the human director as selectable "
        "packages. Use it when the call is genuinely theirs, the stakes or ambiguity "
        "are high, or you want their steer. The director's review time is scarce — "
        "propose only when it adds real value, and keep packages genuinely distinct.\n"
        "- send_message: explain, recommend, ask, or flag something — grounded in the "
        "real state numbers.\n"
        "- read_state / list_actions: refresh your view of the state and the OPTIONS "
        "you can act on. get_facilities / get_workforce / get_tasks / get_logistics pull "
        "one focused slice when you don't need the whole picture.\n"
        "- responsibility_lookup: check who owns an action OR who answers a task, and "
        "whether it is yours, before acting/answering near your role's edge or naming a "
        "colleague. Use it so you name the RIGHT officer instead of guessing.\n"
        "- finish: end your turn when nothing further is worth doing.\n"
        "Ground every number you cite in the state you were given. Never claim to have "
        "built, hired, staffed, moved, or changed anything unless you actually called an "
        "action tool this turn (or the director selected a package you proposed) and saw a "
        "success result; if an action you want is not in your available list, say so plainly "
        "and explain what is blocking it.\n"
        "Stay in your lane for ACTIONS only: build/hire/staff/transfer/etc. must be within your "
        "own remit — if an action the situation needs is not yours, call responsibility_lookup and "
        "tell the director which officer owns it, by name. But ANSWERS are never limited by lane: "
        "always answer a question the director asks — from the shared state, or by naming the owning "
        "officer AND still giving a useful starting number. Never refuse to answer or say 'no action "
        "available'."
    )

    def _agent_lock(self, name: str) -> asyncio.Lock:
        """Per-agent turn lock (lazily created). Serializes turns for ONE officer
        while letting DIFFERENT officers overlap — the granularity that makes
        officers concurrent yet keeps each officer's single persistent transcript
        from being corrupted by two of its own turns interleaving tool_call/tool
        pairs (e.g. a begin_round turn overlapping a director_message turn)."""
        lock = self._agent_turn_locks.get(name)
        if lock is None:
            lock = asyncio.Lock()
            self._agent_turn_locks[name] = lock
        return lock

    async def _run_continuous(
        self,
        agent: AgentConfig,
        filtered_state: dict,
        filtered_actions: List[dict],
        game_state: dict,
        all_actions: List[dict],
        triggered_by_director: bool = False,
        triggered_by_peer: bool = False,
        caused_by_message_id: Optional[str] = None,
    ) -> Tuple[dict, List[dict]]:
        """Serialize turns FOR THIS OFFICER, then drive one turn.

        `triggered_by_director` distinguishes the two activation paths: True when a
        director_message directly addressed this officer (it may act), False for an
        unprompted begin_round tick. In "reactive" opening_mode this is the switch
        that gates whether the officer gets action tools at all this turn.

        Uses a per-agent lock (not a session-global one): two turns for the SAME
        officer never overlap (transcript integrity), but different officers run
        their tool-loops concurrently. Cross-officer contention over the single
        Unity socket is handled at the finer boundaries instead — _unity_commit_lock
        (mutations) and _director_attention_lock (proposals) — so a peer parked at
        propose_choices doesn't freeze the officers still acting. Callers on the
        receive-loop path (director_message) must invoke this from a background task
        so awaiting the lock never blocks the loop (else choice_made could deadlock).
        """
        trigger = ("director" if triggered_by_director
                   else "peer" if triggered_by_peer else "round")
        self._set_agent_status(agent.subagent_name, "queued", trigger=trigger)
        try:
            async with self._agent_lock(agent.subagent_name):
                return await self._run_continuous_inner(
                    agent, filtered_state, filtered_actions, game_state, all_actions,
                    triggered_by_director, triggered_by_peer,
                    caused_by_message_id=caused_by_message_id,
                )
        finally:
            self._set_agent_status(agent.subagent_name, "idle")

    def _set_agent_status(self, name: str, state: str, **fields) -> None:
        """Record what an officer is doing now, for the developer panel. Keeps the previous
        entry's turn_id/trigger unless overridden, so 'idle' still shows the last turn."""
        prev = self._agent_status.get(name, {})
        entry = {k: prev.get(k) for k in ("turn_id", "trigger", "step", "last_outcome")}
        entry.update(fields)
        entry.update({"state": state, "since": _now()})
        self._agent_status[name] = entry
        # Tell the client when an officer starts or stops working, so its "writing…" indicator
        # (and the round button's "officers still writing" check) clears even when the turn ends
        # without a message -- round-start turns may now stay silent. Only busy/idle transitions
        # are sent, not every step.
        busy = state != "idle"
        was_busy = prev.get("state") not in (None, "idle")
        if busy != was_busy:
            agent = self._get_agent_by_name(name)
            frame = {"type": "officer_status", "agent_name": name,
                     "talkinghead_endpoint": getattr(agent, "talkinghead_endpoint", "") or "",
                     "busy": busy, "timestamp": _now()}
            try:
                asyncio.get_event_loop().create_task(self._send(frame))
            except RuntimeError:
                pass   # no running loop (offline tools/tests): nothing to notify

    async def _run_continuous_concurrent(self, agent: AgentConfig) -> None:
        """Drive one continuous officer's turn for a begin_round, reading the
        freshest shared snapshot and publishing its result for peers + the director.

        Spawned once per officer inside an asyncio.gather in _handle_begin_round, so
        all officers' tool-loops overlap. Each officer's LLM thinking runs fully in
        parallel; only the Unity mutation and proposal boundaries serialize (via the
        commit / attention locks inside the tool dispatch). A peer's build lands in
        _latest_game_state, so an officer that acts later in its loop re-enumerates
        against it — and if two officers race the same site, the engine rejects the
        loser with an honest 'site not available' (surfaced, never hidden)."""
        gs = self._latest_game_state
        if not gs:
            return
        all_actions = self._latest_all_actions or _enumerate_actions(gs)
        filtered_state = self._filter_state(gs, agent)
        filtered_actions = filter_actions(all_actions, agent.subaction_space)
        if not filtered_actions:
            print(f"[router]   {agent.subagent_name}: no in-scope actions — skipping.")
            return
        # NB: we do NOT publish this officer's LOCAL end-of-turn game_state to
        # _latest_*. Under concurrency that would regress the snapshot — an officer
        # that executed early but finished late holds a local copy that never saw a
        # peer's later build. Instead _publish_state() in the Unity commit path
        # keeps _latest_* at the freshest GLOBAL state after every mutation, so it
        # is always monotone-fresh for the director_turn and mid-round re-entry.
        # triggered_by_director defaults False: a begin_round tick is UNPROMPTED, so
        # under "reactive" opening_mode this officer briefs only (no action tools).
        await self._run_continuous(
            agent, filtered_state, filtered_actions, gs, all_actions
        )

    def _publish_state(self, game_state: dict) -> None:
        """Record the freshest full game_state seen by the router as the shared
        _latest_* snapshot. Called from the Unity commit chokepoints (every
        execute result carries the authoritative post-mutation global state) so
        concurrent officers and the post-gather director_turn always read the
        latest world, independent of which officer's turn happens to finish last."""
        if game_state:
            self._latest_game_state = game_state
            self._latest_all_actions = _enumerate_actions(game_state)
            self._state_version += 1

    async def _fetch_fresh_state(self) -> dict:
        """Pull Unity's authoritative CURRENT game_state on demand.

        Unity only pushes state on begin_round and execute results, so anything
        the router didn't cause — the human's direct actions, simulation ticks,
        deliveries completing, the daily budget allocation — is invisible until we
        ask. Call this at the start of each officer turn so the observation (and
        the getter tools, which read _latest_game_state) reflect reality. Holds the
        Unity commit lock for single-in-flight discipline. Falls back to the last
        known state on timeout so a turn never hard-fails on a missed pull.
        """
        async with self._unity_commit_lock:
            loop = asyncio.get_event_loop()
            self._pending_state = loop.create_future()
            await self._send({"type": "get_game_state", "timestamp": _now()})
            try:
                msg = await asyncio.wait_for(self._pending_state, timeout=10.0)
            except asyncio.TimeoutError:
                msg = None
            finally:
                self._pending_state = None
        if msg and msg.get("game_state"):
            self._publish_state(msg["game_state"])
            return msg["game_state"]
        return self._latest_game_state

    async def _run_continuous_for_message(self, agent: AgentConfig,
                                          by_peer: bool = False,
                                          cause_message_id: Optional[str] = None) -> None:
        """Drive a continuous turn triggered by a mid-round director_message.

        Recomputes the filtered state/actions from the FRESHEST session snapshot
        at execution time (not at message-arrival time). Because this officer's
        turns serialize on its per-agent lock, this task may run after another of
        its turns (e.g. one that built facilities via propose_choices) has already
        updated _latest_game_state — so the agent sees the result of that turn.
        """
        # Pull Unity's authoritative CURRENT state so this turn reflects everything
        # that changed since the round began — the human's own actions, sim ticks,
        # deliveries, the daily budget — not just router-caused mutations.
        try:
            gs = await self._fetch_fresh_state()
            if not gs:
                return
            filtered_state = self._filter_state(gs, agent)
            all_actions = self._latest_all_actions or _enumerate_actions(gs)
            filtered_actions = filter_actions(all_actions, agent.subaction_space)
            # _latest_* is kept fresh by _publish_state() in the commit path; no
            # end-of-turn local publish here (see _run_continuous_concurrent).
            # triggered_by_director=True: the director addressed this officer, so under
            # "reactive" opening_mode it is now allowed to act (full palette this turn).
            await self._run_continuous(
                agent, filtered_state, filtered_actions, gs, all_actions,
                triggered_by_director=not by_peer,
                triggered_by_peer=by_peer,
                caused_by_message_id=cause_message_id,
            )
        except Exception as e:
            # A director explicitly addressed this officer; an uncaught error must not
            # leave them with a silently-hanging "thinking" bubble and no reply
            # (messaging-flow audit #3). Surface a visible, correctly-routed reply — which
            # also clears the client's generating spinner — and log the traceback here
            # (we swallow rather than re-raise so _on_round_task_done doesn't double-log).
            import traceback
            print(f"[router]   ⚠️  director-turn error for {agent.subagent_name}: {e}")
            traceback.print_exc()
            try:
                await self._send_agent_response(
                    agent, "I hit an internal error handling that request and couldn't "
                    "complete it — mind trying again?", "agent_response", origin="router_template")
            except Exception:
                pass

    # Keep this many most-recent activation turns (each: one user re-grounding +
    # its assistant/tool steps) when compacting a continuous transcript. The system
    # message and the current turn are always retained; only OLD tool-spam is shed.
    _CONTINUOUS_KEEP_TURNS = 8

    # HYSTERESIS, so compaction does not shift the prefix on EVERY turn. Shedding one turn
    # per activation once the window is full moves the cut point each time, which invalidates
    # the whole retained window for prefix caching and re-prefills it. Letting the transcript
    # grow to HIGH and only then cutting back to KEEP makes compaction bite every
    # (HIGH - KEEP) turns instead of every turn -- same context ceiling, ~4x the cross-turn
    # reuse. Measured rationale: proj_dashboard/serve/QWEN27B_SERVING.md section 7.2.
    #
    # TURN COUNTS ARE THE FALLBACK, NOT THE TRIGGER. A turn count cannot know how big a turn
    # is: a talk-only activation costs a few hundred tokens and a four-step tool turn costs
    # thousands, so any fixed count is simultaneously too eager for cheap turns and too lax
    # for expensive ones. Tuned at 12 against a 16k window it was too lax in the direction
    # that actually hurts -- the transcript blew the window at ~5 activations and the server
    # rejected the request outright (400 "maximum context length"), so compaction never ran
    # at all. These two constants now apply only when the endpoint's context window is
    # unknown; when it IS known, the watermarks below drive compaction instead.
    _CONTINUOUS_COMPACT_AT = 12

    # TOKEN WATERMARKS, as a fraction of the usable transcript budget (the context window
    # minus the static head minus the reserved completion budget). Compaction fires once the
    # transcript passes HIGH and cuts back to LOW -- the same hysteresis idea as above, now
    # measured in the unit the server actually enforces.
    #
    # The gap between them is what buys prefix-cache reuse: a rewrite discards every cached
    # block from the cut point down, so compaction must be RARE and LARGE rather than
    # per-turn and small. HIGH is not 1.0 because the estimate is deliberately pessimistic
    # and one more activation lands on top of it before the next compaction check.
    _CONTINUOUS_HIGH_WATER = 0.80
    _CONTINUOUS_LOW_WATER = 0.50

    # Never shed below this many activation turns, whatever the arithmetic says. A turn
    # carries the committed ledger and the director's last exchange; dropping to zero
    # context would make the officer answer the same question twice.
    _CONTINUOUS_MIN_TURNS = 2

    # Held back from the transcript budget for the model's own reply. Sized for a
    # thinking-off tool step (measured 40-300 tokens for a real turn), with headroom.
    _CONTINUOUS_RESERVE_OUTPUT = 1024

    @staticmethod
    def _compact_transcript(messages: List[dict], keep_turns: int,
                            compact_at: int = 0, *,
                            token_budget: Optional[int] = None,
                            high_water: float = 0.80,
                            low_water: float = 0.50,
                            min_turns: int = 2) -> List[dict]:
        """Bound a continuous transcript, by TOKEN BUDGET when the window is known.

        `token_budget` is how many tokens the transcript itself may occupy — the
        context window minus the static head (system prompt + tool schemas) minus a
        reservation for the reply. Given it, this compacts on the same quantity the
        server enforces: over `high_water` triggers a cut back to `low_water`. Without
        it (window unknown) it falls back to the legacy `keep_turns`/`compact_at`
        turn counting, which is why both parameters are still here.

        A continuous officer carries ONE transcript for the whole game; left
        unbounded it accretes step-by-step tool JSON (read_state dumps, action
        lists) that crowds out context and inflates cost. We shed only OLD turns.

        Cut ONLY at a user-role boundary. Each activation's re-grounding is a
        user message; assistant(tool_calls) → tool(result) pairs always sit
        BETWEEN two user messages, so cutting at a user message can never orphan
        a tool result from its call (an OpenAI/Anthropic protocol violation).
        The committed ledger survives because it is re-rendered into every
        activation's user turn (see _continuous_turn_message), so it is always
        inside the retained window — never something we can drop.
        """
        if len(messages) <= 2:
            return messages
        system, body = messages[0], messages[1:]
        starts = [i for i, m in enumerate(body) if m.get("role") == "user"]
        if not starts:
            return messages

        # ---- TOKEN-DRIVEN PATH (preferred): compact against the real window ----------
        if token_budget and token_budget > 0:
            high = int(token_budget * high_water)
            low = int(token_budget * low_water)
            # Measure the BODY only. The caller already subtracted the static head
            # (system message + tool schemas) from the window to form `token_budget`,
            # so counting the system message again here would shrink the effective
            # budget by its own size and fire compaction earlier than intended --
            # defeating the "rare and large" rewrite that keeps the prefix cacheable.
            est = _est_prompt_tokens(body, None)
            if est <= high:
                return messages                      # still inside the watermark

            # Walk user boundaries from the NEWEST backwards, taking as many whole turns
            # as fit under LOW. Cutting only at a user message is what keeps every tool
            # result paired with the assistant tool_call that produced it -- an orphan is
            # a hard 400 from the provider, so this invariant outranks the budget.
            kept, cut_at = 0, starts[-1]
            for i in range(len(starts) - 1, -1, -1):
                seg_end = starts[i + 1] if i + 1 < len(starts) else len(body)
                seg = _est_prompt_tokens(body[starts[i]:seg_end], None)
                turns_taken = len(starts) - i
                # Always take the newest turn, and honour the floor, even if over budget:
                # an over-budget CURRENT turn is the caller's problem to clamp, whereas an
                # empty transcript is a correctness bug.
                if kept + seg > low and turns_taken > max(1, min_turns):
                    break
                kept += seg
                cut_at = starts[i]
            if cut_at == 0:
                return messages                      # nothing sheddable
            shed = len(starts) - sum(1 for x in starts if x >= cut_at)
            print(f"[router]   ✂ compacted transcript: ~{est} -> ~{kept} tok "
                  f"(budget {token_budget}, high {high}, low {low}); shed {shed} turn(s)")
            return [system] + body[cut_at:]

        # ---- TURN-COUNT FALLBACK: only when the context window is unknown -------------
        # Only compact once the transcript has grown PAST the trigger, then cut all the way
        # back to keep_turns. Between rewrites the prefix is stable and cacheable.
        if len(starts) <= max(keep_turns, compact_at):
            return messages
        return [system] + body[starts[-keep_turns]:]

    # Tools that COMMIT to the world (spend, build, hire, transfer) or seize the
    # director's attention with a proposal. Stripped from the palette on an
    # unprompted "reactive" turn so an officer physically cannot act unbidden.
    # The typed action tools count as "acting" (build/hire/…) alongside execute_commands and
    # propose_choices, so the reactive-autonomy guard strips ALL of them on an unprompted turn.
    # add_to_autonomy_list counts too: an officer asks for a standing order only when the
    # Director has spoken to it, never on an unprompted turn.
    _ACTING_TOOLS = frozenset({"execute_commands", "propose_choices", "add_to_autonomy_list"}
                              | _CORA_ACTION_TOOLS)

    async def _run_continuous_inner(
        self,
        agent: AgentConfig,
        filtered_state: dict,
        filtered_actions: List[dict],
        game_state: dict,
        all_actions: List[dict],
        triggered_by_director: bool = False,
        triggered_by_peer: bool = False,
        caused_by_message_id: Optional[str] = None,
    ) -> Tuple[dict, List[dict]]:
        """Drive one turn of the continuous (tool-using) agent."""
        # Reactive autonomy ("activate when spoken to"): on an UNPROMPTED turn a
        # reactive officer may brief the director but must not act — so we hand it a
        # palette with the acting tools removed. It is then structurally impossible
        # to commit anything unbidden; no reliance on prompt adherence. Any other
        # opening_mode (emergent/brief_first) keeps the full configured palette.
        opening_mode = getattr(agent, "opening_mode", "emergent")
        # THREE activation shapes now, not two:
        #   director-triggered -> may act, converses freely
        #   peer-triggered     -> may act, converses freely (a colleague wrote to it)
        #   unprompted tick    -> may NOT act, and briefs only
        # `may_act` and `brief_only` used to be the same boolean; splitting them is what lets an
        # unprompted tick brief without acting.
        #
        # A PEER-TRIGGERED OFFICER KEEPS ITS NORMAL TOOLS. It used to lose them, on the reasoning
        # that only the Director authorises action. That conflated AUTHORITY with CAPABILITY: an
        # officer woken by a colleague is still the same officer at the same desk, and stripping
        # its tools means it cannot even look something up before answering. The "confirm with
        # the Director before acting on a peer's suggestion" rule belongs in the prompt, where it
        # is a judgement the officer makes, not in the harness as a capability it lacks.
        may_act = (opening_mode != "reactive") or triggered_by_director or triggered_by_peer
        brief_only = ((opening_mode == "reactive")
                      and not triggered_by_director and not triggered_by_peer)
        # build_tools only knows built-ins; keep plugin names out of its allowlist (they're
        # added below by _plugin_tool_schemas_for) so it doesn't log "unknown tool".
        # Per-config rewording of built-in tool descriptions (bundle `tool_descriptions`).
        _tdesc = getattr(self.config, "tool_descriptions", None)
        if not may_act:
            base = list(agent.tools) if agent.tools else list(DEFAULT_TOOLS)
            # Standing orders put their own tools back (and only those): the dispatcher
            # still refuses any call outside an order's limits.
            ordered = {r["tool"] for r in self._autonomy_rules.get(agent.subagent_name, [])}
            tools = build_tools([t for t in base
                                 if (t not in self._ACTING_TOOLS or t in ordered)
                                 and cora_ext.get_tool(t) is None],
                                descriptions=_tdesc,
                                recipients=self._recipients_for(agent))
        else:
            _builtins = ([t for t in agent.tools if cora_ext.get_tool(t) is None]
                         if agent.tools else None)
            tools = build_tools(_builtins, descriptions=_tdesc,
                                recipients=self._recipients_for(agent))
        # Append registered plugin (cora_ext) tool schemas this agent may use. Inert when no
        # plugins are loaded; respects the reactive acting-strip and the per-agent allowlist,
        # de-duped against built-ins by name.
        _plug = _plugin_tool_schemas_for(agent, brief_only, tools)
        if _plug:
            tools = tools + _plug
        tools = self._autonomy_tool_schema(tools)
        agent_cfg = vars(agent)  # run_tool_step reads provider/model/endpoint/key/budget
        max_steps = agent.max_steps or 8

        # The continuous agent carries ONE growing transcript for the whole game.
        # Seed the system message once, fold in any director input that arrived
        # since our last activation, then append this activation's live-state turn.
        # The tool loop below appends its assistant/tool turns to this same list,
        # so every prior step stays visible across activations and rounds.
        name = agent.subagent_name
        # Turn provenance. obs_state_version is the snapshot this turn's observation was
        # filtered from (the caller computes filtered_state from _latest_game_state just
        # before entering here).
        turn_id = str(uuid.uuid4())
        obs_state_version = self._state_version
        self._turn_ctx[name] = {"turn_id": turn_id,
                                "caused_by_message_id": caused_by_message_id,
                                # add_to_autonomy_list is accepted only on a turn the
                                # Director started (not a colleague's message).
                                "triggered_by_director": triggered_by_director}
        self._set_agent_status(name, "thinking", turn_id=turn_id, step=0)
        messages = self._continuous_transcripts.setdefault(name, [])
        # The system message is rebuilt every turn and swapped in only if it changed, which
        # today means a developer-panel edit. The rest of the transcript is kept, so the
        # officer continues the same conversation under the new prompt. When nothing changed
        # the message is byte-identical and the provider's prefix cache is unaffected.
        system_msg = self._continuous_system_message(agent)
        if not messages:
            messages.append(system_msg)
        elif messages[0].get("content") != system_msg["content"]:
            messages[0] = system_msg
            print(f"[router]   ✎ {name}: system prompt updated (developer panel) — "
                  f"history kept ({len(messages) - 1} messages).")
        director_entries = [
            e for e in self.message_queue.get_conversation(name, "Director")
            if e.get("from") == "Director"
        ]
        director_has_spoken = len(director_entries) > 0
        # Folds in the Director AND any peer officers this agent can be addressed by.
        # Ids of the messages newly folded into context this turn: what the officer had
        # read when it acted (earlier ones were delivered on its earlier turns).
        seen_message_ids: List[str] = []
        heard_from_peer = self._inject_unseen_messages(agent, messages,
                                                       seen_ids=seen_message_ids)
        messages.append(self._continuous_turn_message(
            agent, filtered_state, filtered_actions, director_has_spoken,
            brief_only=brief_only, triggered_by_director=triggered_by_director,
            heard_from_peer=heard_from_peer, triggered_by_peer=triggered_by_peer))

        # LOOP-SHAPING HOOK: let a plugin add context before the officer's first step —
        # a ReAct scratchpad, retrieved notes, a self-critique preamble, an experimental
        # instruction. Returned user/system messages are appended after the grounding
        # message; assistant/tool roles are refused (they would break tool-call pairing).
        _extra = self._hook_context_messages(await self._fire_hooks_collect(
            "on_turn_start",
            {"agent": agent.subagent_name, "round": self.round_num,
             "max_steps": max_steps, "brief_only": brief_only,
             "triggered_by_director": triggered_by_director,
             "actions_available": len(filtered_actions)},
            agent=agent))
        if _extra:
            messages.extend(_extra)
            print(f"[router]   ＋ on_turn_start injected {len(_extra)} message(s) "
                  f"for {agent.subagent_name}.")

        # Bound the ever-growing per-officer transcript: keep the system message and
        # the last N activation turns (the current one always included), shedding old
        # tool-spam. The committed ledger rides inside each turn message, so it is
        # never dropped. Reassign both the persistent store and the local handle so
        # the loop below appends onto the compacted list.
        # Size the transcript against the ENDPOINT'S REAL WINDOW when we know it. The
        # static head (system prompt + every tool schema) is re-sent on every request but
        # never accumulates, so it comes off the top as a fixed cost; what is left is what
        # the conversation may occupy. Falls back to turn counting when the window is
        # unknown (cold cache on the very first activation, or a provider that does not
        # advertise one) -- see continuous_agent.known_ctx_limit.
        _ctx = known_ctx_limit(agent_cfg)
        _budget = None
        if _ctx:
            _head = _est_prompt_tokens([messages[0]] if messages else [], tools)
            _budget = _ctx - _head - self._CONTINUOUS_RESERVE_OUTPUT
            if _budget < 1024:
                # A head this large leaves no room to converse; compacting cannot fix it.
                # Say so once rather than silently shedding the whole transcript.
                print(f"[router]   ⚠️  {name}: static head ~{_head} tok leaves only "
                      f"{_budget} of {_ctx} for the transcript — narrow the tool palette "
                      f"or raise max-model-len.")
                _budget = None
        messages = self._compact_transcript(
            messages, self._CONTINUOUS_KEEP_TURNS, self._CONTINUOUS_COMPACT_AT,
            token_budget=_budget,
            high_water=self._CONTINUOUS_HIGH_WATER,
            low_water=self._CONTINUOUS_LOW_WATER,
            min_turns=self._CONTINUOUS_MIN_TURNS)
        self._continuous_transcripts[name] = messages

        sat_before = _get_satisfaction(game_state)
        budget_before = _get_budget(game_state)
        executed_total = 0
        # Whether the officer sent the director a message this turn. Answering a
        # question via send_message is real work even though it executes no game
        # action, so a talk-only turn must NOT get the "no action taken" note below —
        # that note seeds a status-report register the model then echoes ("Answered X;
        # no action taken") instead of giving the actual answer.
        talked = False
        # Per-turn retry cap: signatures of tool calls that hard-failed this turn.
        # An identical call is not re-dispatched — it gets a synthetic result telling
        # the officer to surface it or pick a different action (prevents a stuck
        # officer from re-attempting the same failing action every step).
        failed_sigs = set()
        # Per-turn telemetry accumulators (previously hardcoded empty/0 — the F5
        # gap). tokens_total sums every step's provider usage; turn_attempts records
        # each tool call the officer made; turn_results collects the per-action
        # execution outcomes the dispatch reports; last_text is the officer's final
        # natural-language content (the llm_raw_response for this turn record).
        tokens_total = 0
        turn_attempts: List[dict] = []
        turn_results: List[dict] = []
        last_text = None
        # `spoke`: whether a director-FACING agent_message was actually SENT this turn (set
        # on a real _send_agent_response, not merely on a send_message tool NAME — an
        # empty message or a note-less finish sends nothing). `errored`: a provider/tool
        # error broke the loop. Together they drive the post-loop fallback that guarantees a
        # director-TRIGGERED turn always yields exactly one visible reply (no silent hang).
        spoke = False
        errored = False

        print(f"[router]   ▶ Continuous agent {agent.subagent_name}: "
              f"{len(filtered_actions)} actions, up to {max_steps} steps, "
              f"tools={[t['function']['name'] for t in tools]}")

        for step in range(max_steps):
            self._set_agent_status(name, "thinking", step=step)
            resp = await asyncio.to_thread(
                run_tool_step, messages, tools, agent_cfg, agent.tool_mode
            )
            self._set_agent_status(name, "acting", step=step)
            if resp.get("error"):
                print(f"[router]   ⚠️  Continuous step {step} error: {resp['error']}")
                errored = True
                break

            tokens_total += int((resp.get("usage") or {}).get("total_tokens") or 0)
            if resp.get("content"):
                last_text = resp["content"]

            tool_calls = resp.get("tool_calls") or []

            # Record the assistant turn (text + any tool_calls) in OpenAI shape so
            # the next step sees its own reasoning and calls.
            assistant_msg: dict = {"role": "assistant", "content": resp.get("content")}
            if tool_calls:
                assistant_msg["tool_calls"] = [
                    {
                        "id": tc["id"],
                        "type": "function",
                        "function": {"name": tc["name"], "arguments": json.dumps(tc["arguments"])},
                    }
                    for tc in tool_calls
                ]
            messages.append(assistant_msg)

            if not tool_calls:
                # No tool → the agent is done; surface any closing text.
                if resp.get("content"):
                    await self._send_agent_response(agent, resp["content"], "agent_response")
                    spoke = True
                print(f"[router]   ⏹ Continuous agent finished at step {step} (no tool call).")
                break

            stop = False
            for tc in tool_calls:
                sig = (tc["name"], json.dumps(tc.get("arguments") or {}, sort_keys=True))
                if sig in failed_sigs:
                    # Identical call already hard-failed this turn — do NOT re-dispatch.
                    # Still satisfy the protocol: every tool_call id needs a result.
                    messages.append({
                        "role": "tool", "tool_call_id": tc["id"],
                        "content": ("This exact tool call already hard-failed this turn and "
                                    "was not re-run. Surface the failure to the director or "
                                    "pick a different action."),
                    })
                    continue
                turn_attempts.append({"tool": tc["name"], "arguments": tc.get("arguments")})
                if tc["name"] == "send_message":
                    talked = True
                # The assistant message carrying tc is already in the transcript, and that
                # transcript persists for the whole game. An exception escaping the dispatcher
                # would leave the tool_call permanently unanswered, so every later turn would
                # re-send an unpaired tool call -> hard 400 from the provider -> that officer
                # is bricked for the session. Catch here so a result ALWAYS follows.
                exec_state_version = self._state_version
                try:
                    result_str, game_state, all_actions, filtered_actions, meta = \
                        await self._dispatch_continuous_tool(
                            agent, tc, game_state, all_actions, filtered_actions,
                            brief_only=brief_only,
                        )
                except Exception as _e:
                    import traceback as _tb
                    _tb.print_exc()
                    result_str = f"ERROR: {type(_e).__name__}: {_e}"
                    meta = {"executed": 0, "finish": False}
                # Every tool_call id MUST get a matching tool result before the
                # next assistant turn (OpenAI/Anthropic protocol requirement).
                messages.append({"role": "tool", "tool_call_id": tc["id"], "content": result_str})
                executed_total += meta.get("executed", 0)
                spoke = spoke or bool(meta.get("spoke"))
                # Staleness: how many state snapshots landed between what the officer
                # observed and the moment this action was dispatched (0 = acted on current).
                for r in meta.get("results") or []:
                    if isinstance(r, dict):
                        r.setdefault("obs_state_version", obs_state_version)
                        r.setdefault("exec_state_version", exec_state_version)
                turn_results.extend(meta.get("results") or [])
                # Record a hard failure: nothing executed AND at least one result row
                # reports failure. Read-only tools (read_state/get_*/list_actions)
                # carry no failing rows, so they never enter failed_sigs.
                if meta.get("executed", 0) == 0 and any(
                        not r.get("success", True) for r in (meta.get("results") or [])):
                    failed_sigs.add(sig)
                if meta.get("finish"):
                    stop = True
                # Brief-only turns are capped at ONE director-facing message: after
                # the officer briefs, end the turn even if it didn't call finish, so
                # it can't tack on a redundant "awaiting guidance" follow-up.
                if brief_only and tc["name"] == "send_message":
                    stop = True
            # LOOP-SHAPING HOOK: a plugin may end the turn early (custom stopping rule —
            # e.g. "stop once any action executed", a confidence gate, a step budget of its
            # own). Return "stop" or True to halt; anything else continues. Never fires the
            # ctx build when no hook is registered, so the built-in loop is unaffected.
            if not stop:
                _verdicts = await self._fire_hooks_collect(
                    "on_step_end",
                    {"agent": agent.subagent_name, "step": step, "max_steps": max_steps,
                     "content": resp.get("content"),
                     "tool_names": [tc["name"] for tc in tool_calls],
                     "executed_total": executed_total, "spoke": spoke},
                    agent=agent)
                if any(v is True or (isinstance(v, str) and v.strip().lower() == "stop")
                       for v in _verdicts):
                    print(f"[router]   ⏹ on_step_end hook stopped the turn at step {step}.")
                    stop = True

            if stop:
                print(f"[router]   ⏹ Continuous agent called finish at step {step}.")
                break
        else:
            print(f"[router]   ⏹ Continuous agent hit max_steps ({max_steps}).")

        # Make an empty turn EXPLICIT. In text (ReAct) mode a step can end with no
        # parseable tool call and the turn silently commits nothing; without a marker
        # the next activation's transcript looks like the officer simply skipped a
        # beat. Record a grounding note (model-facing only, not sent to the director)
        # so the officer sees it took no action and can decide deliberately next time.
        if executed_total == 0 and not talked:
            messages.append({
                "role": "user",
                "content": ("[note] This turn ended with no game action taken. If that "
                            "was intentional (nothing to do, or waiting on the "
                            "director), fine — otherwise act next turn."),
            })
            print(f"[router]   ⏸ Continuous agent {agent.subagent_name}: "
                  "no action taken this turn (noted).")

        # Guarantee a director-TRIGGERED turn always produces exactly ONE visible reply.
        # Without this, a provider error, an all-read-only turn hitting max_steps, an empty
        # send_message, or a note-less finish ends the turn with nothing sent — the
        # director's "thinking" bubble hangs with no answer and no surfaced error
        # (messaging-flow audit, HIGH #1). Guard on triggered_by_director so unprompted
        # begin_round ticks may still legitimately stay silent.
        if triggered_by_director and not spoke:
            if errored:
                fallback = "I hit an internal error handling that — mind trying again?"
            else:
                fallback = (last_text or "").strip() or "Acknowledged — nothing to add on that just now."
            try:
                await self._send_agent_response(
                    agent, fallback, "agent_response",
                    origin="llm" if (not errored and (last_text or "").strip()) else "router_template")
                print(f"[router]   ↩ Continuous agent {agent.subagent_name}: sent fallback "
                      f"director reply ({'error' if errored else 'silent turn'}).")
            except Exception as _e:
                print(f"[router]   ⚠️  fallback director reply failed: {_e}")

        raw = last_text or f"[continuous] {executed_total} action(s) executed"
        tools_called = [a.get("tool") for a in turn_attempts]
        # What the officer itself chose on this wake-up. Silence is a decision too: a
        # when-to-speak policy cannot be learned from a log that only records messages sent.
        # A harness fallback reply (director-triggered turn that sent nothing) is flagged
        # separately so it is never mistaken for the officer choosing to speak.
        if any(t in self._ACTING_TOOLS for t in tools_called):
            turn_outcome = "act"
        elif talked:
            turn_outcome = "speak"
        else:
            turn_outcome = "pass"
        system_text = (messages[0].get("content") or "") if messages else ""
        self._log_turn(agent, filtered_state, filtered_actions, turn_attempts, None,
                       turn_results, sat_before, game_state, budget_before,
                       raw, tokens_total,
                       trigger=("director" if triggered_by_director
                                else "peer" if triggered_by_peer else "round"),
                       tools_called=tools_called,
                       extra={
                           "turn_id": turn_id,
                           "caused_by_message_id": caused_by_message_id,
                           "seen_message_ids": seen_message_ids,
                           "obs_state_version": obs_state_version,
                           "end_state_version": self._state_version,
                           "turn_outcome": turn_outcome,
                           "harness_fallback_reply": bool(triggered_by_director and not spoke),
                           "brief_only": brief_only,
                           # Which model produced this turn, and under which system prompt.
                           "provider": getattr(agent, "provider", None),
                           "llm_model": getattr(agent, "llm_model", None),
                           "system_prompt_sha": hashlib.sha256(
                               system_text.encode("utf-8")).hexdigest()[:16],
                       })
        self._turn_ctx.pop(name, None)
        self._set_agent_status(name, "acting", last_outcome=turn_outcome)
        print(f"[router]   ✓ Continuous agent {agent.subagent_name}: "
              f"{executed_total} action(s) executed this turn.")
        return game_state, all_actions


    @staticmethod
    def _transfer_trip_note(action: dict, game_state: dict) -> str:
        """For a committed transfer, say how many vehicle trips it actually needs.

        WHY THIS IS A TOOL RESULT AND NOT A PROMPT RULE. DeliverySystem.CreateDeliveryTask
        loops `Mathf.Min(remaining, maxCapacity)` and returns a LIST, so a request larger
        than one vehicle load is split into that many trips, each needing its own vehicle.
        Measured: an officer told to move 400 food with 3 vehicles free committed it and
        reported "Sent 400 food" with no caveat -- yet asked directly, the SAME officer
        computed "4 vehicles (400 / 100 load each), we've got 3 free" correctly off the
        same observation. It could do the arithmetic; it just didn't think to, under a
        direct instruction. Telling it harder in the prompt is the lever that already
        failed, so the count is returned as engine fact alongside the action instead.

        Returns "" when the transfer fits in one trip, when the fleet covers it, or when
        the load/quantity is unknown -- a note that fires on every transfer is noise.
        """
        t = action.get("transfer") or action.get("resource_transfer") or {}
        try:
            qty = int(t.get("quantity") or 0)
        except (TypeError, ValueError):
            return ""
        load = vehicle_capacity(game_state)
        if not qty or not load:
            return ""
        trips = -(-qty // int(load))          # ceil
        if trips <= 1:
            return ""
        free = _num_free_vehicles(game_state)
        note = f" — needs {trips} vehicle trips at {load}/load"
        if free is not None:
            note += (f"; {free} free, so {trips - free} trip(s) wait for a vehicle"
                     if trips > free else f"; {free} free, covered")
        return note


    def _recipients_for(self, agent: AgentConfig) -> List[str]:
        """Who this officer may address: the Director, plus its config's `can_address`.

        `can_address` has existed on AgentConfig since the schema was written and was never
        read by anything -- inter-agent messaging is what it was reserved for. An empty list
        (the default, and what every shipped config has today) means director-only, which is
        exactly the previous behaviour, so nothing changes for configs that don't opt in.

        Names are validated against the live roster: a config naming an officer that isn't in
        this session would otherwise put an unreachable address in the tool's enum, and the
        officer would keep trying it.
        """
        roster = {a.subagent_name for a in self.config.agents}
        peers = [n for n in (getattr(agent, "can_address", None) or [])
                 if n in roster and n != agent.subagent_name]
        return ["Director"] + peers

    def _inject_unseen_messages(self, agent: AgentConfig, messages: List[dict],
                                seen_ids: Optional[List[str]] = None) -> bool:
        """Fold every message this officer has not yet seen into its transcript.

        Covers the Director AND peer officers with one mechanism. Returns True if any PEER
        message was newly injected, which the caller uses to tell the officer it has something
        from a colleague waiting -- a *potential* response, not an obligation.

        This function only DELIVERS; it does not wake anyone. Waking is _send_agent_response's
        job: a peer message spawns the recipient's turn as a background task, capped per round
        by peer_trigger_budget so an A->B->A ping-pong ends the round rather than the session.
        A message whose wake-up was skipped (budget spent) still arrives here, on the
        recipient's next ordinary turn. `seen_ids`, if given, collects the ids delivered.
        """
        name = agent.subagent_name
        got_peer = False
        for partner in self._recipients_for(agent):
            entries = [e for e in self.message_queue.get_conversation(name, partner)
                       if e.get("from") == partner]
            key = (name, partner)
            already = self._msg_injected_count.get(key, 0)
            for e in entries[already:]:
                tag = "[Director]" if partner == "Director" else f"[From: {partner}]"
                messages.append({"role": "user", "content": f"{tag} {e.get('content', '')}"})
                if seen_ids is not None:
                    seen_ids.append(e.get("id"))
                if partner != "Director":
                    got_peer = True
            self._msg_injected_count[key] = len(entries)
        return got_peer

    def _continuous_system_message(self, agent: AgentConfig) -> dict:
        """The system message (role + global prompt + tool policy). Seeds the persistent
        transcript; it only changes mid-game through a developer-panel override."""
        use_global = agent.use_global_prompt
        global_prompt = self._resolve_global_prompt() if use_global else ""
        agent_prompt = (self._prompt_overrides["agents"].get(agent.subagent_name)
                        or agent.system_prompt
                        or "You are an officer in a disaster-relief operation.")
        if global_prompt:
            system = f"{global_prompt}\n\n---\n\nAGENT ROLE: {agent_prompt}"
        else:
            system = agent_prompt
        # The tool policy is the MECHANICAL contract (how the typed action tools are called,
        # the anti-hallucination rule, stay-in-lane). A config may replace it outright —
        # informed collaborators can prompt-engineer the whole surface — but the upload
        # endpoint warns when an override drops the contract's key clauses, since a bad
        # rewrite yields officers that mis-call tools or claim actions they never took.
        policy = (self._prompt_overrides["tool_policy"]
                  or getattr(self.config, "tool_policy", None)
                  or self._CONTINUOUS_TOOL_POLICY)
        system = f"{system}\n\n---\n\n{policy}"
        return {"role": "system", "content": system}

    # The shared prompt is two concerns glued together: behavior/communication rules and the
    # game manual. They are split on this separator so a config can override either half and
    # inherit the other — the whole point being that tweaking personality must not silently
    # delete the game rules (costs, workforce minimums, strategy priorities).
    _PROMPT_SPLIT = "=" * 60

    def _resolve_global_prompt(self) -> str:
        """Compose this session's shared prompt: per-config halves where supplied, server
        defaults otherwise. A legacy whole-blob override short-circuits both halves."""
        cfg = self.config
        ov = self._prompt_overrides
        whole = getattr(cfg, "global_prompt", None)
        if whole and not (ov["global_behavior"] or ov["global_manual"]):
            return whole                      # explicit whole-blob replace
        if whole:
            # A developer edit to one half of a config that ships a whole-blob prompt: split
            # the blob so the untouched half keeps the config's text.
            parts = whole.split(self._PROMPT_SPLIT, 1)
            behavior = ov["global_behavior"] or parts[0]
            manual = ov["global_manual"] or (parts[1] if len(parts) > 1 else "")
            return behavior if not manual.strip() else \
                f"{behavior}\n\n{self._PROMPT_SPLIT}\n{manual}"
        behavior = ov["global_behavior"] or getattr(cfg, "global_prompt_behavior", None)
        manual = ov["global_manual"] or getattr(cfg, "global_prompt_manual", None)
        if not behavior and not manual:
            return load_global_prompt()       # nothing overridden — server default verbatim

        default = load_global_prompt() or ""
        parts = default.split(self._PROMPT_SPLIT, 1)
        def_behavior = parts[0]
        def_manual = parts[1] if len(parts) > 1 else ""
        behavior = behavior if behavior else def_behavior
        manual = manual if manual else def_manual
        if not manual.strip():
            return behavior
        return f"{behavior}\n\n{self._PROMPT_SPLIT}\n{manual}"

    # ── Developer panel: live prompt editing ────────────────────────
    # Scopes a developer may edit. "agent" is per officer; the other three are session-wide
    # and affect every officer that uses them.
    DEV_PROMPT_SCOPES = ("agent", "global_behavior", "global_manual", "tool_policy")

    def _dev_base_layers(self) -> dict:
        """The session-wide layers WITHOUT developer overrides, with where each came from."""
        cfg = self.config
        default = load_global_prompt() or ""
        d_parts = default.split(self._PROMPT_SPLIT, 1)
        whole = getattr(cfg, "global_prompt", None)
        if whole:
            w_parts = whole.split(self._PROMPT_SPLIT, 1)
            behavior = (w_parts[0], "config")
            manual = (w_parts[1] if len(w_parts) > 1 else "", "config")
        else:
            b = getattr(cfg, "global_prompt_behavior", None)
            m = getattr(cfg, "global_prompt_manual", None)
            behavior = (b, "config") if b else (d_parts[0], "default")
            manual = (m, "config") if m else (d_parts[1] if len(d_parts) > 1 else "", "default")
        pol = getattr(cfg, "tool_policy", None)
        policy = (pol, "config") if pol else (self._CONTINUOUS_TOOL_POLICY, "default")
        return {"global_behavior": behavior, "global_manual": manual, "tool_policy": policy}

    def dev_prompt_view(self) -> dict:
        """Everything the developer panel shows for this session: the session-wide layers,
        each continuous officer's own prompt, the exact assembled system prompt it will run
        under on its next turn, and what it is doing right now."""
        base = self._dev_base_layers()
        ov = self._prompt_overrides
        layers = {}
        for scope, (text, source) in base.items():
            if ov[scope]:
                text, source = ov[scope], "override"
            layers[scope] = {"text": text, "source": source}
        officers = []
        for a in self.config.agents:
            if a.actor_type != "continuous":
                continue
            own = ov["agents"].get(a.subagent_name)
            assembled = self._continuous_system_message(a)["content"]
            transcript = self._continuous_transcripts.get(a.subagent_name) or []
            running = (transcript[0].get("content") if transcript else None)
            officers.append({
                "name": a.subagent_name,
                "provider": getattr(a, "provider", None),
                "llm_model": getattr(a, "llm_model", None),
                "uses_global_prompt": bool(a.use_global_prompt),
                "system_prompt": {"text": own or a.system_prompt or "",
                                  "source": "override" if own else "config"},
                "assembled_prompt": assembled,
                "assembled_sha": hashlib.sha256(assembled.encode("utf-8")).hexdigest()[:16],
                # True when an edit is waiting for this officer's next turn to take effect.
                "pending_change": running is not None and running != assembled,
                "transcript_messages": len(transcript),
                "status": self._agent_status.get(a.subagent_name, {"state": "idle"}),
            })
        rules = [dict(r, officer=o) for o, rs in self._autonomy_rules.items() for r in rs]
        return {"session_id": self.session_id, "round": self.round_num, "day": self.day,
                "layers": layers, "officers": officers, "autonomy_rules": rules}

    def dev_set_prompt(self, scope: str, text: Optional[str], editor: str,
                       agent_name: Optional[str] = None) -> dict:
        """Apply (text) or reset (text=None) one prompt layer for this session. Takes effect
        at each affected officer's next turn. Logged as a prompt_change event with a diff."""
        if scope not in self.DEV_PROMPT_SCOPES:
            raise ValueError(f"unknown scope {scope!r}; expected one of {self.DEV_PROMPT_SCOPES}")
        if text is not None and not text.strip():
            raise ValueError("empty prompt text; use reset to go back to the config's text")
        if scope == "agent":
            agent = self._get_agent_by_name(agent_name or "")
            if agent is None or agent.actor_type != "continuous":
                raise ValueError(f"no continuous officer named {agent_name!r} in this session")
            name = agent.subagent_name
            old = self._prompt_overrides["agents"].get(name) or agent.system_prompt or ""
            if text is None:
                self._prompt_overrides["agents"].pop(name, None)
            else:
                self._prompt_overrides["agents"][name] = text
            new = self._prompt_overrides["agents"].get(name) or agent.system_prompt or ""
            affected = [name]
        else:
            base_text = self._dev_base_layers()[scope][0] or ""
            old = self._prompt_overrides[scope] or base_text
            self._prompt_overrides[scope] = text
            new = text or base_text
            affected = [a.subagent_name for a in self.config.agents
                        if a.actor_type == "continuous"
                        and (scope == "tool_policy" or a.use_global_prompt)]
        sha = lambda s: hashlib.sha256(s.encode("utf-8")).hexdigest()[:16]
        diff = "".join(difflib.unified_diff(old.splitlines(True), new.splitlines(True),
                                            "before", "after", n=2))
        record = {"scope": scope, "agent": agent_name if scope == "agent" else None,
                  "action": "reset" if text is None else "set",
                  "editor": editor, "affected_agents": affected,
                  "old_sha": sha(old), "new_sha": sha(new),
                  "diff": diff[:20000], "diff_truncated": len(diff) > 20000,
                  "round_applied_from": self.round_num}
        self._emit("prompt_change", record,
                   actor={"kind": "developer", "name": editor, "role": "developer",
                          "actor_type": "developer_panel"})
        print(f"[router] ✎ prompt_change by {editor}: {scope}"
              f"{' / ' + agent_name if scope == 'agent' else ''} "
              f"({record['action']}, affects {len(affected)} officer(s) from their next turn)")
        return record

    # ── Developer panel: load a checkpoint into the live game ───────
    DEV_OFFICER_MEMORY = ("fresh", "keep")

    def _dev_reset_officers(self) -> None:
        """Start every officer fresh: drop its transcript and mark every message already in
        the queue as seen, so the old game's conversation is not re-delivered afterwards."""
        self._continuous_transcripts.clear()
        for a in self.config.agents:
            if a.actor_type != "continuous":
                continue
            for partner in self._recipients_for(a):
                entries = [e for e in self.message_queue.get_conversation(a.subagent_name, partner)
                           if e.get("from") == partner]
                self._msg_injected_count[(a.subagent_name, partner)] = len(entries)

    async def dev_load_checkpoint(self, checkpoint_text: str, officer_memory: str,
                                  editor: str, timeout: float = 15.0) -> dict:
        """Ask the connected game to load a `.cora` checkpoint, then (officer_memory="fresh")
        start the officers with empty transcripts. Logged as a checkpoint_load event carrying
        the checkpoint's hash, so a moment replayed from a file stays traceable to it."""
        if officer_memory not in self.DEV_OFFICER_MEMORY:
            raise ValueError(f"officer_memory must be one of {self.DEV_OFFICER_MEMORY}")
        label = None
        try:
            parsed = json.loads(checkpoint_text)
            label = parsed.get("label") or parsed.get("note")
        except (ValueError, AttributeError):
            raise ValueError("checkpoint is not valid JSON")
        request_id = str(uuid.uuid4())
        fut = asyncio.get_event_loop().create_future()
        self._pending_checkpoint_loads[request_id] = fut
        await self._send({"type": "load_checkpoint", "request_id": request_id,
                          "checkpoint": checkpoint_text})
        try:
            reply = await asyncio.wait_for(fut, timeout=timeout)
        except asyncio.TimeoutError:
            reply = {"accepted": False, "error": f"no reply from the game within {timeout:.0f}s"}
        finally:
            self._pending_checkpoint_loads.pop(request_id, None)
        accepted = bool(reply.get("accepted"))
        if accepted:
            self._supersede_pending_choice("developer loaded a checkpoint")
            if officer_memory == "fresh":
                self._dev_reset_officers()
            # Game state now comes from the checkpoint; the next begin_round / state pull
            # refreshes the router's snapshot.
            self._peer_triggers_left = int(getattr(self.config, "peer_trigger_budget", None)
                                           or PEER_TRIGGER_BUDGET_PER_ROUND)
        record = {"request_id": request_id, "accepted": accepted, "error": reply.get("error"),
                  "officer_memory": officer_memory, "editor": editor, "label": label,
                  "checkpoint_sha": hashlib.sha256(checkpoint_text.encode("utf-8")).hexdigest()[:16],
                  "checkpoint_bytes": len(checkpoint_text)}
        self._emit("checkpoint_load", record,
                   actor={"kind": "developer", "name": editor, "role": "developer",
                          "actor_type": "developer_panel"})
        print(f"[router] ⤓ checkpoint_load by {editor}: "
              f"{'accepted' if accepted else 'refused — ' + str(reply.get('error'))} "
              f"(officers: {officer_memory})")
        return record

    def _continuous_turn_message(
        self,
        agent: AgentConfig,
        filtered_state: dict,
        filtered_actions: List[dict],
        director_has_spoken: bool,
        brief_only: bool = False,
        triggered_by_director: bool = False,
        heard_from_peer: bool = False,
        triggered_by_peer: bool = False,
    ) -> dict:
        """The per-activation user turn: re-grounds the agent on the LIVE state,
        action list, and planning-phase ledger. Appended fresh each activation on
        top of the persistent transcript, because the world advances between
        activations even though the trajectory before it stays visible."""
        # Opening posture (the human's autonomy dial, kept OUT of the agent prompt).
        #   reactive + unprompted (brief_only): brief the director, cannot act (the
        #     acting tools aren't even in the palette this turn).
        #   reactive + spoken-to: act, but do EXACTLY what was asked — no scaling,
        #     no extra sites/targets (fixes the "transfer 20" → 60 fan-out).
        #   brief_first (until first direction): open with one briefing, don't act.
        #   emergent (default): inject nothing — pure tool-user from step 1.
        opening_mode = getattr(agent, "opening_mode", "emergent")
        capabilities = self._officer_capabilities_phrase(agent)
        title = agent.subagent_name
        closing = "Decide what to do."
        if brief_only:
            # Round-start turns used to ask every officer for a brief EVERY round, re-introducing
            # its office each time: in Talos session 1bb785d1, 70 of 71 round-start turns sent a
            # message (median 49 words against the Director's 8), which participants read as
            # verbose and as talk that delayed action. Now: introduce yourself once, and after
            # that speak at round start only when something needs the Director.
            introduced = any(e.get("from") == title for e in
                             self.message_queue.get_conversation(title, "Director"))
            grounding = (
                "Ground any factual claim (building counts, worker counts, shortfalls) in the "
                "situation above, the 'already committed' ledger, or a read tool (read_state, "
                "get_facilities, get_workforce) — count anything you committed this phase as "
                "pending, and never state a count from memory.")
            if not introduced:
                closing = (
                    "You have NOT been directly addressed this turn, and you act only when the "
                    "director speaks to you. You have NO action tools right now, so do not attempt "
                    "to build, hire, transfer, or propose. Send ONE short send_message (to: "
                    f"Director) that names your office (you are the {title}), says in one line "
                    f"what you can do for the director (you can {capabilities}), and gives the "
                    "single most important need in your domain right now in one sentence — then "
                    "call finish. " + grounding)
            else:
                closing = (
                    "You have NOT been directly addressed this turn, and you act only when the "
                    "director speaks to you. You have NO action tools right now, so do not attempt "
                    "to build, hire, transfer, or propose. You have already introduced yourself. "
                    "Message the director ONLY if something in your domain needs their attention "
                    "now: a new task, a shortfall that is about to cost satisfaction, a decision "
                    "that is due, or a real change since your last message. If so, send ONE "
                    "send_message (to: Director) of one or two sentences, no introduction, then "
                    "call finish. Otherwise call finish without sending anything — staying quiet "
                    "when nothing needs the director is the right call. " + grounding)
        elif opening_mode == "reactive" and triggered_by_director:
            # Per-turn specifics only — the global prompt already owns tone,
            # answer-directly (rule 7), and act-only-on-instruction; don't restate them.
            closing = (
                "The director addressed you — reply to THEM, and do EXACTLY what they "
                "asked: don't change the quantity or add sites, targets, or actions they "
                "didn't name. When you cite a count, reconcile the situation above with "
                "the 'already committed' ledger — anything you queued THIS phase counts "
                "as pending even if the situation still shows it absent. If the ask is "
                "ambiguous or unaffordable, ask ONE short question instead of guessing. "
                "If they asked a question, lead with the answer itself (the number or a "
                "yes/no), not a recap of what you did. Send ONE send_message (to: Director) "
                "message, then finish."
            )
        elif opening_mode == "brief_first" and not director_has_spoken:
            # FIRST message (opening brief): introduce the office by name.
            closing = (
                "The director has not given you any direction yet. Do NOT commit any "
                "builds, hires, or transfers. Send EXACTLY ONE short send_message (to: Director) "
                f"message that opens by naming your office (you are the {title}), "
                "then in at most 3 sentences: the single biggest need, the budget "
                "remaining, and one recommendation — then immediately call finish. Do "
                "NOT send a second message or a status follow-up; wait for the director."
            )
        elif opening_mode == "brief_first" and director_has_spoken:
            # AFTER the opening brief: drop the formal self-introduction and talk
            # to the director conversationally, like a colleague.
            closing = (
                "You have already introduced yourself in your opening brief, so do "
                "NOT restate your office, title, or role again and do NOT prefix your "
                f"message with your name (\"{title} here —\") — the director already "
                "knows who you are. Just reply conversationally and naturally, like a "
                "colleague talking with them: answer their question or do what they "
                "asked directly, in plain language. Keep your assistant posture — "
                "propose consequential actions and act only on a clear instruction — "
                "but drop the formal self-introduction. If they ask a question, lead "
                "with the answer itself (the number or a yes/no), not a recap of what "
                "you did."
            )
        # Per-config override of the AUTHORED half of the turn message. Which variant applies
        # is decided by the harness (it follows from opening_mode / who spoke), but the TEXT
        # of each is a prompt-engineering lever — it is what drives terseness, hesitancy and
        # self-introduction, so an experiment that cannot touch it is missing a real knob.
        # `{title}` and `{capabilities}` are substituted. Omitted key -> harness default.
        which = ("peer_addressed" if (triggered_by_peer and not triggered_by_director)
                 else "brief_only" if brief_only
                 else "addressed" if (opening_mode == "reactive" and triggered_by_director)
                 else "first_brief" if (opening_mode == "brief_first" and not director_has_spoken)
                 else "after_brief" if opening_mode == "brief_first"
                 else "default")
        if triggered_by_peer and not triggered_by_director:
            closing = (
                "A fellow officer has just messaged you (marked \"[From: ...]\" above). You "
                "may reply to them with send_message, raise it with the Director, or let it "
                "go. You have NO action tools this turn: a colleague's request is NOT "
                "authorisation to act. If you think they are right, say so and put it to the "
                "Director — only the Director can tell you to do it. Send at most one message, "
                "then call finish.")
        closing = self._turn_instruction(which, closing, title=title, capabilities=capabilities)
        closing += self._standing_orders_text(title, brief_only)
        # A colleague wrote to this officer since it last ran. Surfaced as an OPTION, not an
        # instruction: the officer decides whether the message is worth answering. It also
        # restates the non-negotiable part -- a peer's suggestion is not authority to act --
        # because this is the exact moment the officer is most likely to treat it as one.
        if heard_from_peer:
            closing += (
                "\n\nAnother officer has messaged you since your last turn (marked "
                "\"[From: ...]\" above). You may reply to them with send_message if it is "
                "worth answering, or ignore it. Either way: another officer's suggestion is "
                "NOT authorisation to act. If you think their idea is right, put it to the "
                "director and wait for their word before doing it.")

        state_text = officer_text(filtered_state)
        action_text = self._render_options_compact(filtered_actions, filtered_state)
        preamble = self._turn_instruction("preamble", "It is your turn. Current situation:",
                                          title=title, capabilities=capabilities)
        actions_header = self._turn_instruction("actions_header", "Actions available to you now:",
                                                title=title, capabilities=capabilities)
        return {
            "role": "user",
            "content": (
                f"{preamble}\n{state_text}\n\n"
                f"{self._committed_ledger_text()}"
                f"{actions_header}\n{action_text}\n\n"
                f"{closing}"
            ),
        }

    def _turn_instruction(self, key: str, default: str, **fmt) -> str:
        """Resolve one authored turn-message string: the config's `turn_instructions[key]`
        if present, else the harness default. Placeholder substitution is best-effort — a
        contributor's stray brace must not crash a live turn, so a bad format string falls
        back to the raw text rather than raising."""
        table = getattr(self.config, "turn_instructions", None) or {}
        text = table.get(key)
        if not isinstance(text, str) or not text.strip():
            return default
        try:
            return text.format(**fmt)
        except (KeyError, IndexError, ValueError):
            print(f"[router]   ⚠️  turn_instructions['{key}'] has an unusable placeholder — "
                  "using it verbatim.")
            return text

    def _committed_ledger_text(self) -> str:
        """Render the planning-phase ledger as a context block (empty if none).

        The paused-phase observation is frozen and doesn't reflect the agent's own
        queued actions, so without this the agent re-proposes what it already
        committed. Ends with a blank line so it slots cleanly between the state and
        the action list in the opening message.
        """
        if not self._committed_this_phase:
            return ""
        items = "\n".join(f"  - {c}" for c in self._committed_this_phase)
        # State the committed spend as a NUMBER. The prose below already warns that the
        # frozen budget excludes these commits, but without the figure the officer has to
        # infer it — and a wrong inference means overcommitting against money it no longer
        # has. (Observed live: an officer built a $1,000 kitchen, was still shown $5,000,
        # and reported "~$4,000" as arithmetic rather than fact.)
        spend = ""
        if self._committed_spend_this_phase:
            spend = (f"Committed spend this phase: ${self._committed_spend_this_phase:,.0f} "
                     f"— subtract it from the budget shown above before deciding what you "
                     f"can still afford.\n")
        return (
            "YOU HAVE ALREADY COMMITTED these actions this planning phase — they are "
            "locked in and real, and take effect when the phase resolves (next "
            "round):\n"
            f"{items}\n"
            f"{spend}"
            "The frozen situation above was captured BEFORE these commits, so its "
            "counts (worker totals, budget) and facility list do NOT include them "
            "yet. When you reason or report to the director, RECONCILE the two: "
            "treat everything listed here as existing/pending. Never tell the "
            "director something doesn't exist if you just committed it — say it is "
            "queued and when it lands (workers you hired are available to assign, "
            "and newly-built facilities finish and become staffable, once this phase "
            "resolves next round). Do NOT re-commit anything listed here.\n\n"
        )

    @staticmethod
    def _action_ledger_key(action: dict) -> str:
        """Canonical ledger string for an action (used to record AND to match)."""
        return f"[{action.get('action_type', '?')}] {action.get('description', '?')}"

    @staticmethod
    def _humanize_committed_action(action: dict) -> str:
        """Plain-English past-tense confirmation of a just-committed action.

        Feeds the director-facing "Action: ..." chat bubble — no emojis, no
        command-tag syntax, no indices. Derives its wording from the action's
        structured sub-dict + cost so the bubble reads like a log line a person
        wrote ("Built Shelter at Riverside for $2,000"). Falls back to the
        action's own description if a type is unrecognized.
        """
        def money(v):
            try:
                v = int(round(float(v)))
            except (TypeError, ValueError):
                return None
            return f"${v:,}" if v > 0 else None

        atype = action.get("action_type")
        cost = money(action.get("cost"))
        if atype == "construction":
            c = action.get("construction") or {}
            btype = c.get("building_type") or "facility"
            site = c.get("site_name") or "an available site"
            s = f"Built {btype} at {site}"
            return s + (f" for {cost}" if cost else "")
        if atype == "worker":
            w = action.get("worker") or {}
            q = w.get("quantity") or 0
            wt = w.get("worker_action_type") or ""
            if wt == "train_untrained":
                s = f"Training {q} untrained worker{'s' if q != 1 else ''}"
            elif wt == "hire_trained":
                s = f"Hired {q} trained worker{'s' if q != 1 else ''}"
            else:  # hire_untrained (and any unknown hire variant)
                s = f"Hired {q} untrained worker{'s' if q != 1 else ''}"
            return s + (f" for {cost}" if cost else "")
        if atype == "resource_transfer":
            t = action.get("transfer") or action.get("resource_transfer") or {}
            q = t.get("quantity") or 0
            res = t.get("resource_type") or "supplies"
            res_label = {"FoodPacks": "food packs", "Population": "people"}.get(res, res)
            src = t.get("source_facility") or "source"
            dst = t.get("destination_facility") or "destination"
            s = f"Transferred {q} {res_label} from {src} to {dst}"
            return s + (f" for {cost}" if cost else "")
        if atype == "worker_assignment":
            a = action.get("assignment") or action.get("worker_assignment") or {}
            q = a.get("quantity") or 0
            bname = a.get("building_name") or "a facility"
            return f"Assigned {q} worker{'s' if q != 1 else ''} to {bname}"
        if atype == "deconstruction":
            d = action.get("deconstruction") or {}
            bname = d.get("building_name") or "a facility"
            return f"Deconstructed {bname}"
        # Unknown type: fall back to the enumerator's own description.
        return str(action.get("description") or "Committed an action")

    @staticmethod
    def _officer_capabilities_phrase(agent: AgentConfig) -> str:
        """Human-readable list of the action kinds this officer's config admits.

        Derived from subaction_space so it stays truthful to what the officer can
        actually commit (no hallucinated capabilities). Feeds the brief so the
        officer can tell the director what it can do.
        """
        verbs = {
            "construction": "construct facilities",
            "deconstruction": "deconstruct facilities",
            "worker": "hire and train workers",
            "worker_assignment": "assign workers to facilities",
            "resource_transfer": "move supplies between sites and bring in external supply",
            "task_choice": "answer the tasks in your domain",
        }
        seen, phrases = set(), []
        for entry in agent.subaction_space:
            cat = entry.get("category")
            if cat in ("all", None) or cat in seen:
                continue
            seen.add(cat)
            if cat in verbs:
                phrases.append(verbs[cat])
        if not phrases:
            return "act within your assigned remit"
        if len(phrases) == 1:
            return phrases[0]
        return ", ".join(phrases[:-1]) + ", and " + phrases[-1]

    def _record_committed(self, action: dict) -> None:
        """Append a succeeded action to the planning-phase ledger (deduped), and add its
        cost to the phase's committed spend so the officer can reconcile a frozen budget."""
        # Spend accumulates OUTSIDE the dedupe. The ledger key is "[type] description", so two
        # legitimate repeats of a repeatable action (worker, resource_transfer — both
        # deliberately excluded from _NON_REPEATABLE_TYPES) collapse onto one line. Counting
        # their cost once understated the very figure this ledger exists to make truthful.
        try:
            self._committed_spend_this_phase += float(action.get("cost") or 0)
        except (TypeError, ValueError):
            pass
        line = self._action_ledger_key(action)
        if line not in self._committed_this_phase:
            self._committed_this_phase.append(line)

    def _render_action_list(self, filtered_actions: List[dict]) -> str:
        """Render the filtered actions as an indexed list (index == execute index).

        Actions already committed this planning phase are flagged INLINE — at the
        exact index the model chooses — because a separate 'do not repeat' block
        upstream isn't decisive enough on its own (the model re-executes anyway).
        The action stays in the list (no gating); it's just truthfully marked.
        """
        if not filtered_actions:
            return "(no valid actions available to you)"
        committed = set(self._committed_this_phase)
        lines = []
        for i, a in enumerate(filtered_actions):
            if self._action_ledger_key(a) in committed:
                done = " ⚠️ ALREADY COMMITTED THIS PHASE — do NOT pick again"
            else:
                done = ""
            lines.append(
                f"{i}. [{a.get('action_type', '?')}] {a.get('description', '?')} "
                f"(cost: ${_num(a.get('cost')):,}){done}"
            )
        return "\n".join(lines)

    def _render_options_compact(self, filtered_actions: List[dict], game_state: dict) -> str:
        """Compact, tag-oriented affordance view: read-surface == write-surface.

        Replaces the indexed `_render_action_list` for the tags-only officers. It
        lists WHAT is available grouped by command (BUILD/HIRE/TRAIN/STAFF/…), and
        the model composes the exact `<tag>` from the grammar in the tool schema —
        so nothing the model reads references a volatile integer index that could
        drift or be hallucinated. Two invariants vs the indexed list:

        (a) TASK rows carry the stable task token (cora.observation.task_token,
            computed from the RAW task so it matches what cmd_parser accepts), not a
            turn-to-turn taskId.
        (b) Committed non-repeatable affordances are pulled OUT of the available set
            and listed under an ALREADY-COMMITTED footer with the ⚠️ marker,
            reusing `_action_ledger_key` so the identity matches the ledger block.

        Everything shown round-trips through cmd_parser.parse_commands back to an
        action in `filtered_actions` (verified hermetically).
        """
        if not filtered_actions:
            return "(no valid actions available to you)"
        committed = set(self._committed_this_phase)
        avail, done = [], []
        for a in filtered_actions:
            (done if self._action_ledger_key(a) in committed else avail).append(a)

        sites: dict = {}          # site_id -> [site_name, {building_types}]
        hire = {"untrained": 0, "trained": 0}
        train = 0
        staff: dict = {}          # building_name -> max assignable quantity
        decon: List[str] = []
        transfer: List[str] = []
        tasks: dict = {}          # taskId -> {token, title, choices:[(cid, text)]}

        for a in avail:
            t = a.get("action_type")
            if t == "construction":
                c = a.get("construction", {})
                sid, bt = c.get("site_id"), c.get("building_type")
                desc = a.get("description", "")
                nm = desc.split(" at ", 1)[-1] if " at " in desc else str(sid)
                sites.setdefault(sid, [nm, set()])[1].add(bt)
            elif t == "worker":
                w = a.get("worker", {})
                wat, q = w.get("worker_action_type"), (w.get("quantity") or 0)
                if wat == "hire_untrained":
                    hire["untrained"] = max(hire["untrained"], q)
                elif wat == "hire_trained":
                    hire["trained"] = max(hire["trained"], q)
                elif wat == "train_untrained":
                    train = max(train, q)
            elif t == "worker_assignment":
                # cora.actions nests these fields under "assignment" (see
                # WorkerAssignmentAction.to_dict), NOT "worker_assignment".
                wa = a.get("assignment", {})
                bn, q = wa.get("building_name"), (wa.get("quantity") or 0)
                if bn:
                    staff[bn] = max(staff.get(bn, 0), q)
            elif t == "deconstruction":
                bn = a.get("deconstruction", {}).get("building_name")
                if bn and bn not in decon:
                    decon.append(bn)
            elif t == "resource_transfer":
                tr = a.get("transfer", {})
                res = "food" if tr.get("resource_type") == "FoodPacks" else "people"
                transfer.append(f"{res},{tr.get('source_facility')},"
                                f"{tr.get('destination_facility')} (up to {tr.get('quantity')})")
            elif t == "task_choice":
                tc = a.get("task_choice", {})
                tid, cid = tc.get("taskId"), tc.get("choiceId")
                if tid not in tasks:
                    raw = next((x for x in (game_state.get("allActiveTasks") or [])
                                if x.get("taskId") == tid), None)
                    tok = (task_token(self._norm_task_for_token(raw))
                           if raw else f"TASK_{tid}")
                    tasks[tid] = {"token": tok, "title": tc.get("taskTitle") or "", "choices": []}
                desc = a.get("description", "")
                marker = f"choice {cid}: "
                text = desc.split(marker, 1)[-1] if marker in desc else ""
                tasks[tid]["choices"].append((cid, text))

        lines = ["What you can do now — write each as a command tag "
                 "(exact grammar is in the execute_commands tool schema):"]
        if sites:
            lines.append("  BUILD  <build>TYPE,SITE</build>:")
            # Collapse consecutive sites that offer the SAME building types into one range.
            # A kitchen-scoped officer was shown fifteen lines that differed only by an id —
            # "site 0 (AbandonedSite (1)): Kitchen" repeated down the page — which was the
            # bulk of its per-turn message and buried the parts that actually varied. The
            # site ids are unchanged and still individually addressable; only the rendering
            # is folded, so nothing the parser accepts is affected.
            ordered = sorted(sites, key=lambda s: (s is None, s))
            run: list = []

            def _flush(run_ids):
                if not run_ids:
                    return
                types = ", ".join(sorted(sites[run_ids[0]][1]))
                if len(run_ids) == 1:
                    sid = run_ids[0]
                    lines.append(f"    site {sid} ({sites[sid][0]}): {types}")
                else:
                    names = {sites[s][0].rstrip("0123456789() ") for s in run_ids}
                    kind = names.pop() if len(names) == 1 else "site"
                    lines.append(f"    sites {run_ids[0]}-{run_ids[-1]} "
                                 f"({len(run_ids)}x {kind}): {types}")

            for sid in ordered:
                same = run and sorted(sites[sid][1]) == sorted(sites[run[-1]][1])
                contiguous = same and isinstance(sid, int) and isinstance(run[-1], int) \
                    and sid == run[-1] + 1
                if contiguous:
                    run.append(sid)
                else:
                    _flush(run)
                    run = [sid]
            _flush(run)
        hires = []
        if hire["untrained"]:
            hires.append(f"untrained up to {hire['untrained']}")
        if hire["trained"]:
            hires.append(f"trained up to {hire['trained']}")
        if hires:
            lines.append("  HIRE  <hire>untrained|trained,N</hire>: " + "  |  ".join(hires))
        if train:
            lines.append(f"  TRAIN  <train>N</train>: up to {train} untrained")
        if staff:
            lines.append("  STAFF  <staff>BUILDING,N</staff>: "
                         + "  ".join(f"{b} (up to {q})" for b, q in staff.items()))
        if decon:
            lines.append("  DECONSTRUCT  <deconstruct>NAME</deconstruct>: " + ", ".join(decon))
        if transfer:
            lines.append("  TRANSFER  <transfer>food|people,SRC,DST,N</transfer>: "
                         + "  ".join(transfer))
        for tid, tk in tasks.items():
            opts = "  ".join(f"[{cid}] {txt}" for cid, txt in tk["choices"])
            lines.append(f'  TASK  <task>{tk["token"]},CHOICE</task>  "{tk["title"]}": {opts}')
        if done:
            lines.append("")
            lines.append("⚠️ ALREADY COMMITTED THIS PHASE — do NOT pick these again:")
            for a in done:
                lines.append(f"  - [{a.get('action_type', '?')}] {a.get('description', '?')}")
        return "\n".join(lines)

    def _tags_to_indices(
        self, commands: str, filtered_actions: List[dict], game_state: dict
    ) -> Tuple[List[int], List[str]]:
        """Resolve command tags to indices INTO filtered_actions (for propose_choices).

        A proposal package bundles `action_indices` that index into filtered_actions
        — the exact list the Unity client renders and executes against. This maps the
        tags-only vocabulary onto those indices so proposals need no client change and
        no separate write-surface. Contrast execute_commands, whose resolved indices
        may point PAST filtered_actions into the shim's <staff> synth-append; here we
        must land every kept action back inside filtered_actions.

        - A non-<staff> tag resolves (via the shared parser) to an index
          < len(filtered_actions): the shim's valid_actions is a copy, so that IS a
          position in filtered_actions — keep it.
        - A <staff> tag makes the parser SYNTHESIZE a worker_assignment action
          appended at index >= len(filtered_actions) (absent from filtered_actions).
          Its prose ("Assign workforce N to X") differs BY CONSTRUCTION from the
          enumerated assignment's prose ("Assign N trained worker(s) to X"), so
          _action_ledger_key is a guaranteed false-negative here; identity-match
          it back STRUCTURALLY on (building_name, quantity) instead. Drop-with-
          reason if no assignment at that quantity is offered this turn (e.g. the
          request outran the free-worker pool).
        - <task> tags land in parsed["choices"] (no home in the action-index
          contract) — dropped with a reason: tasks are answered via execute_commands,
          not bundled into a proposal.
        - Parser errors are surfaced as reasons.

        Returns (indices, reasons): indices into filtered_actions (deduped,
        order-preserving); reasons are human-readable drop notes for logging. Never
        emits an index that mis-points — an unresolved tag is dropped, not guessed.
        """
        shim = _CmdParseShim(filtered_actions, game_state)
        parsed = parse_commands(commands, shim)
        n = len(filtered_actions)
        # Structural index for <staff> synth-match: (building_name, quantity) ->
        # position in filtered_actions. Prefer the untrained variant (the synth is
        # always untrained) but fall back to whatever assignment exists at that
        # (building, quantity). This is the CORRECT identity for worker_assignment
        # — the two code paths render different prose for the same executable action.
        assign_to_idx: dict = {}
        for i, a in enumerate(filtered_actions):
            if a.get("action_type") == "worker_assignment":
                asg = a.get("assignment", {})
                k = (asg.get("building_name"), asg.get("quantity"))
                if k not in assign_to_idx or asg.get("worker_type") == "untrained":
                    assign_to_idx[k] = i
        indices: List[int] = []
        reasons: List[str] = []
        seen: set = set()
        for i in parsed["actions"]:
            if 0 <= i < n:
                idx: Optional[int] = i
            elif 0 <= i < len(shim.valid_actions):
                synth = shim.valid_actions[i]
                asg = synth.get("assignment", {})
                idx = assign_to_idx.get((asg.get("building_name"), asg.get("quantity")))
                if idx is None:
                    reasons.append(
                        f"staffing {asg.get('quantity')} to "
                        f"'{asg.get('building_name')}' — not offered at that "
                        "quantity this turn (check the free-worker pool)")
                    continue
            else:
                continue
            # Keep duplicate indices: a repeated index encodes quantity (see cmd_parser
            # ._bundle_indices), matching execute_commands and the RL gym. Deduping here
            # under-hired/under-built via proposals (e.g. <hire>untrained,10</hire> -> 5).
            indices.append(idx)
        for ch in parsed["choices"]:
            reasons.append(f"task {ch.get('taskId')} choice {ch.get('choiceId')} — "
                           "answer tasks with execute_commands, not a proposal package")
        for e in parsed.get("errors", []):
            reasons.append(str(e))
        return indices, reasons

    # ---- role grounding: who owns which action ---------------------------
    # The construction/staff/deconstruct trio is what a building-scoped officer
    # owns for its building type(s); rendered as one phrase in the roster.
    _CWD_CATS = ("construction", "worker_assignment", "deconstruction")

    def _owning_agents(self, probe: dict) -> List[str]:
        """subagent_names of every NON-director officer whose subaction_space
        admits `probe`.

        Runs the SAME filter_actions gate that governs execution, so the owner
        reported here can never disagree with who may actually run the action.
        Catch-all ({"category":"all"}) agents — the human director — are skipped:
        a fallback that admits everything is nobody's specific owner.
        """
        owners = []
        for a in self.config.agents:
            if a.role == "director":
                continue
            if any(e.get("category") == "all" for e in a.subaction_space):
                continue
            if filter_actions([probe], a.subaction_space):
                owners.append(a.subagent_name)
        return owners

    def _scope_phrase(self, space: List[dict]) -> str:
        """Compact human phrase for one officer's subaction_space."""
        cwd = set(self._CWD_CATS)
        label = {"worker": "hire/train workers",
                 "resource_transfer": "resource transfers"}
        btypes, plain = [], []
        for e in space:
            cat = e.get("category")
            if cat == "all":
                return "everything (director)"
            bt = e.get("building_types")
            if cat in cwd and bt:
                for b in bt:
                    if b not in btypes:
                        btypes.append(b)
            elif cat in cwd:
                plain.append(cat)
            elif cat == "task_choice":
                grp = e.get("group")
                plain.append(f"answer {grp} tasks" if grp else "answer tasks")
            else:
                plain.append(label.get(cat, cat))
        parts = []
        if btypes:
            parts.append("build / staff / deconstruct " + " & ".join(btypes))
        parts += plain
        return "; ".join(parts) if parts else "(nothing)"

    def _roster_lines(self, caller: AgentConfig) -> str:
        rows = []
        for a in self.config.agents:
            if a.role == "director":
                continue
            you = " (you)" if a.subagent_name == caller.subagent_name else ""
            rows.append(f"  • {a.subagent_name}{you} — "
                        f"{self._scope_phrase(a.subaction_space)}")
        return "Officers and what each owns:\n" + "\n".join(rows)

    @staticmethod
    def _norm_task_for_token(t: dict) -> dict:
        """Raw allActiveTasks row → the {title, affects} shape task_token
        and task_officer read, so a token computed here matches what the agent saw
        in its observation."""
        return {"title": t.get("taskTitle") or t.get("title") or "",
                "affects": t.get("affectedFacility") or t.get("affects") or "",
                "taskId": t.get("taskId")}

    def _resolve_task(self, game_state: dict, query: str) -> Optional[dict]:
        """Find the active task the agent means by `query`: a numeric taskId, a
        stable token (FOOD_C01…), or a distinctive title substring. Returns the
        raw task dict, or None if nothing matches."""
        q = str(query).strip()
        ql = q.lower()
        if not ql:
            return None
        tasks = game_state.get("allActiveTasks") or []
        if q.lstrip("-").isdigit():  # 1. exact taskId
            for t in tasks:
                if str(t.get("taskId")) == q:
                    return t
        for t in tasks:            # 2. exact stable token
            if task_token(self._norm_task_for_token(t)).lower() == ql:
                return t
        for t in tasks:            # 3. title substring
            title = (t.get("taskTitle") or t.get("title") or "").lower()
            if title and ql in title:
                return t
        return None

    def _owner_lines(self, agent: AgentConfig, what: str, owners: List[str],
                     roster: str, no_owner_hint: str) -> str:
        """Shared head + your-scope + roster rendering for both lookup modes."""
        mine = agent.subagent_name in owners
        if not owners:
            head = f"{what} → {no_owner_hint}"
            you_line = (f"It is not yours (you are {agent.subagent_name}). Do not do it "
                        f"or claim it — raise it with the director.")
        elif mine:
            head = (f"{what} → owned by {owners[0]}." if len(owners) == 1
                    else f"{what} → owned by {', '.join(owners)}.")
            you_line = f"This IS in your scope — you ({agent.subagent_name}) may handle it."
        else:
            head = (f"{what} → owned by {owners[0]}." if len(owners) == 1
                    else f"{what} → owned by {', '.join(owners)}.")
            to_whom = owners[0] if len(owners) == 1 else "the responsible officer"
            you_line = (f"This is NOT in your scope (you are {agent.subagent_name}). "
                        f"Do not do it or claim it — tell the director it belongs to "
                        f"{to_whom}.")
        return f"{head}\n{you_line}\n\n{roster}"

    def _responsibility_lookup_text(self, agent: AgentConfig, args: dict,
                                    game_state: dict) -> str:
        """Answer a responsibility_lookup tool call as readable text."""
        roster = self._roster_lines(agent)

        # --- task mode: who answers this task ---
        task_q = str(args.get("task") or "").strip()
        if task_q:
            t = self._resolve_task(game_state, task_q)
            if t is None:
                return (f"No active task matches {task_q!r}. Check read_state for the "
                        f"current tasks (by token or id), then look it up.\n\n{roster}")
            grp = task_group(t)
            token = task_token(self._norm_task_for_token(t))
            title = t.get("taskTitle") or t.get("title") or f"task {t.get('taskId')}"
            probe = {"action_type": "task_choice", "task_choice": {"group": grp}}
            owners = self._owning_agents(probe)
            what = f'task "{title}" [{token}, id {t.get("taskId")}], a {grp}-domain task'
            hint = (f"no officer answers {grp}-domain tasks in this scenario — it is "
                    f"the director's call.")
            return self._owner_lines(agent, what, owners, roster, hint)

        # --- action mode: who owns this kind of action ---
        category = str(args.get("category") or "").strip()
        if not category:
            return roster
        building_type = str(args.get("building_type") or "").strip() or None
        probe = {"action_type": category}
        if building_type:
            # flat fallback consumed by agent_filters._building_token_of
            probe["building_type"] = building_type
        owners = self._owning_agents(probe)
        what = category + (f" of {building_type}" if building_type else "")
        hint = "no officer owns it — it may be the director's call, or not in play here."
        return self._owner_lines(agent, what, owners, roster, hint)

    async def _fire_hooks(self, event: str, event_obj: dict,
                          agent: Optional[AgentConfig] = None) -> None:
        """Fire cora_ext hooks for a game event. Inert (no ctx built) when nothing is registered.
        `agent` is None for session-level events (round start, human choice); the hook ctx then
        uses the shared session store and unfiltered state."""
        if not cora_ext.get_hooks(event):
            return
        ctx = _SessionToolContext(self, agent, self._latest_game_state or {}, [], [])
        await cora_ext.run_hooks(event, ctx, event_obj)

    async def _fire_hooks_collect(self, event: str, event_obj: dict,
                                  agent: Optional[AgentConfig] = None) -> list:
        """Fire loop-shaping hooks and RETURN their values. Inert (and free) when none are
        registered, so the built-in loop pays nothing for the extension point."""
        if not cora_ext.get_hooks(event):
            return []
        ctx = _SessionToolContext(self, agent, self._latest_game_state or {},
                                  self._latest_all_actions or [], [])
        return await cora_ext.run_hooks_collect(event, ctx, event_obj)

    @staticmethod
    def _hook_context_messages(results: list) -> List[dict]:
        """Normalize on_turn_start returns into safe extra messages.

        A handler may return a plain string, or a list of {role, content}. Roles are RESTRICTED
        to user/system: injecting an `assistant` or `tool` message would break the strict
        tool_call_id pairing the providers require and corrupt the turn, so those are dropped
        with a warning rather than silently accepted."""
        out: List[dict] = []
        for res in results:
            items = [res] if isinstance(res, (str, dict)) else (res if isinstance(res, list) else [])
            for item in items:
                if isinstance(item, str):
                    if item.strip():
                        out.append({"role": "user", "content": item})
                elif isinstance(item, dict):
                    role = item.get("role", "user")
                    content = item.get("content")
                    if role not in ("user", "system"):
                        print(f"[router]   ⚠️  on_turn_start: dropped a {role!r} message "
                              "(only user/system may be injected — assistant/tool would "
                              "break tool-call pairing).")
                        continue
                    if isinstance(content, str) and content.strip():
                        out.append({"role": role, "content": content})
        return out

    async def _dispatch_continuous_tool(
        self,
        agent: AgentConfig,
        tool_call: dict,
        game_state: dict,
        all_actions: List[dict],
        filtered_actions: List[dict],
        brief_only: bool = False,
        _skip_registry: bool = False,
    ) -> Tuple[str, dict, List[dict], List[dict], dict]:
        """Execute one tool call against the real game backends.

        Returns (result_text, game_state, all_actions, filtered_actions, meta).
        `meta` = {"executed": int, "finish": bool}. No gating EXCEPT the reactive
        brief-only guard: on an unprompted reactive turn the acting tools are not in
        the palette, but a text/ReAct-mode model could still emit one — so we refuse
        it here too rather than trust the palette alone. Otherwise the agent's chosen
        tool is carried out and the honest result is returned to it.
        """
        name = tool_call.get("name")
        # Phase B: typed action tools are the officer's action surface. Translate each to its
        # command tag and route through the SAME execute_commands path (ledger/block gate +
        # execute_resolved) — so the officer and the RL policy share the identical tool schema
        # AND execution semantics. A malformed typed call yields an empty tag (honest no-op).
        # Defined before the _CORA_ACTION_TOOLS branch below, which returns `meta` on its
        # translator-error path. Assigning it after that branch raised UnboundLocalError,
        # which escaped the dispatcher and left a tool_calls message with no matching tool
        # result in the officer's game-long transcript — bricking them for the session.
        meta = {"executed": 0, "finish": False}
        # The call as the officer made it, before a typed tool is rewritten to
        # execute_commands below: standing orders are granted per typed tool + arguments.
        orig_name, orig_args = name, (tool_call.get("arguments") or {})
        if name in _CORA_ACTION_TOOLS:
            _tag, _tmeta = translate_tool_calls(
                [(name, tool_call.get("arguments") or {})])
            # A call the translator refused (delimiter in an argument, unreadable args) would
            # otherwise arrive as an empty tag and come back as the opaque "empty commands"
            # error. Hand the model the actual reason so it can reissue — same honest-result
            # contract as the rest of this dispatcher.
            if not _tag and _tmeta.get("errors"):
                return ("ERROR: " + "; ".join(_tmeta["errors"]),
                        game_state, all_actions, filtered_actions, meta)
            tool_call = dict(tool_call, name="execute_commands", arguments={"commands": _tag})
            name = "execute_commands"
        args = tool_call.get("arguments") or {}

        if brief_only and name in self._ACTING_TOOLS:
            # A standing order the Director approved is the one thing an officer may do on
            # a turn nobody asked it to take: that typed tool, within the order's argument
            # limits. It then runs through the normal (non-brief) path, tagged with the rule.
            rule = (self._autonomy_rule_for(agent, orig_name, orig_args)
                    if orig_name in _CORA_ACTION_TOOLS else None)
            if rule is not None:
                return await self._run_under_standing_order(
                    agent, rule, tool_call, game_state, all_actions, filtered_actions,
                    _skip_registry)
            refusal = ("REFUSED: you have not been directly addressed this turn, so you "
                       "cannot take actions or send proposals. Brief the director via "
                       "send_message to the Director (or call finish); they will tell you "
                       "what to do.")
            if self._autonomy_rules.get(agent.subagent_name):
                refusal = ("REFUSED: this call is not covered by any of your standing orders "
                           "(same tool, within its limits). Unprompted, you may only carry out "
                           "a standing order; for anything else, ask the Director.")
            return (refusal, game_state, all_actions, filtered_actions, meta)

        # Plugin tools (cora_ext registry) take precedence — a contributor tool, or one that
        # overrides a built-in by name, dispatches here. Inert when no plugins are loaded.
        # Acting plugin tools obey the same reactive brief-only gate as built-in acting tools.
        _plugin_spec = None if _skip_registry else cora_ext.get_tool(name)
        if _plugin_spec is not None:
            if brief_only and _plugin_spec.acting:
                return (
                    "REFUSED: you have not been directly addressed this turn, so you cannot "
                    "take actions. Brief the director via send_message (or call finish).",
                    game_state, all_actions, filtered_actions, meta,
                )
            _ctx = _SessionToolContext(self, agent, game_state, all_actions, filtered_actions)
            _res = await cora_ext.run_tool(_plugin_spec, _ctx, args)
            meta["executed"] = _res.executed
            meta["finish"] = _res.finish
            return (_res.text, self._latest_game_state or game_state,
                    _ctx.all_actions, _ctx.filtered_actions, meta)

        if name == "read_state":
            # Read the freshest state the router holds (kept current by every execute
            # commit + the per-turn get_game_state pull), so a look-up reflects reality
            # — including the officer's own just-executed actions — not a stale snapshot.
            fresh = self._latest_game_state or game_state
            return officer_text(self._filter_state(fresh, agent)), \
                game_state, all_actions, filtered_actions, meta

        # Granular getters — one slice of the same filtered observation each, so an
        # officer can pull just the detail it needs without re-dumping read_state.
        # get_logistics needs the officer's enumerated actions (the affordance block
        # is derived from them); the others are pure state slices.
        if name in ("get_facilities", "get_workforce", "get_tasks", "get_logistics"):
            fs = self._filter_state(self._latest_game_state or game_state, agent)
            section = name[len("get_"):]
            text = officer_text(fs, section, filtered_actions if section == "logistics" else None)
            return text, game_state, all_actions, filtered_actions, meta

        if name == "list_actions":
            filtered_actions = filter_actions(all_actions, agent.subaction_space)
            return ("Actions available to you now:\n"
                    + self._render_options_compact(filtered_actions, game_state)), \
                game_state, all_actions, filtered_actions, meta

        if name == "responsibility_lookup":
            return self._responsibility_lookup_text(agent, args, game_state), \
                game_state, all_actions, filtered_actions, meta

        if name == "execute_commands":
            commands = str(args.get("commands") or "").strip()
            if not commands:
                return "ERROR: empty commands.", game_state, all_actions, filtered_actions, meta
            # Resolve intent tags against the agent's CURRENT menu. The shim isolates
            # the parser's <staff> synth-append from the router's real action list;
            # resolved indices point into shim.valid_actions (menu + any synth action).
            shim = _CmdParseShim(filtered_actions, game_state)
            parsed = parse_commands(commands, shim)
            resolved = [i for i in parsed["actions"] if 0 <= i < len(shim.valid_actions)]
            actions_to_run = [shim.valid_actions[i] for i in resolved]
            # ledger_mode="block": staleness-style no-op (à la Claude Code's
            # read-before-edit), ported here from the removed execute_game_action path
            # so the tags surface enforces it too. A NON-repeatable action already
            # committed this phase is NOT re-sent to the engine — the frozen
            # paused-phase state can't reflect the queued action yet, so re-doing it
            # would just fail engine-side. This is grounding (it IS already queued),
            # not style-gating; repeatable actions (hire/train/transfer) are never
            # blocked. The other actions in the same batch still run.
            blocked = []
            if getattr(agent, "ledger_mode", "annotate") == "block":
                committed = set(self._committed_this_phase)
                keep = []
                for a in actions_to_run:
                    if (a.get("action_type") in _NON_REPEATABLE_TYPES
                            and self._action_ledger_key(a) in committed):
                        blocked.append(a)
                    else:
                        keep.append(a)
                actions_to_run = keep
            # Executed as-chosen: a failed action (e.g. "site not available") is an honest
            # policy signal returned to the agent, NOT auto-remapped or hidden. Mirrors the
            # continuous-propose stance; no site-conflict resolution here by design.
            exec_results, game_state = (
                await self.execute_resolved(
                    [{"kind": "action", "action": a} for a in actions_to_run],
                    game_state=game_state, scope_agent=agent)
                if actions_to_run else ([], game_state)
            )
            executed = 0
            lines = []
            # Per-action outcome records for the turn telemetry (mirrors the
            # _log_action ground-truth events, aggregated into the turn record).
            results: List[dict] = []
            for a in blocked:
                self._log_action(
                    self._actor_for(agent), "game_action", "execute_commands",
                    {"action": a, "success": False, "error": "blocked_already_committed",
                     "commands": commands, "note": args.get("note"),
                     **self._outcome_fields("invalid")},
                )
                results.append({"action_id": a.get("action_id"),
                                "action_type": a.get("action_type"),
                                "description": a.get("description"),
                                "cost": a.get("cost") or 0,
                                "success": False, "error": "blocked_already_committed"})
                print(f"[router]   ⛔ Blocked re-execution (already committed this "
                      f"phase): {self._action_ledger_key(a)}")
                lines.append(f"  ⛔ {a.get('description', '(action)')} — already committed "
                             f"this phase (queued; re-doing is a no-op)")
            for action, r in zip(actions_to_run, exec_results):
                success = bool(r.get("success"))
                err = r.get("error_message") or ""
                results.append({"action_id": action.get("action_id"),
                                "action_type": action.get("action_type"),
                                "description": action.get("description"),
                                "cost": action.get("cost") or 0,
                                "success": success, "error": err})
                # Deltas aren't per-action-attributable inside a batched Unity
                # commit, so log engine-truth outcome only (ok/rejected). The
                # single-action execute_game_action path carries the deltas.
                self._log_action(
                    self._actor_for(agent), "game_action", "execute_commands",
                    {"action": action, "success": success, "error": err,
                     "commands": commands, "note": args.get("note"),
                     **self._outcome_fields("ok" if success else "rejected")},
                )
                desc = action.get("description", "(action)")
                if success:
                    executed += 1
                    self._record_committed(action)
                    await self._fire_hooks(
                        "on_action_executed",
                        {"actor": agent.subagent_name, "source": "officer",
                         "is_human": False, "action": action}, agent=agent)
                    # Director-facing commit bubble: one plain-English past-tense
                    # line per committed action ("Action: Built Shelter at
                    # Riverside for $2,000") — no emojis, no command-tag syntax.
                    # This is a distinct, log-style confirmation channel; the
                    # officer still speaks to the director in its own words via
                    # send_message. Also ground-truth logged via _log_action
                    # above and surfaced to the MODEL in `lines` below.
                    await self._send_agent_response(
                        agent, "Action: " + self._humanize_committed_action(action),
                        "agent_response", origin="router_action_receipt")
                    lines.append(f"  ✅ {desc}"
                                 + (self._transfer_trip_note(action, game_state)
                                    if action.get("action_type") == "resource_transfer" else ""))
                else:
                    lines.append(f"  ❌ {desc}" + (f" — {err}" if err else ""))
            # Answer any choice tasks (<task>ID,choiceId</task>). Scope is enforced
            # via the SAME subaction_space filter as every action (_may_answer_task):
            # an officer may only answer tasks whose coarse group its config admits —
            # an out-of-scope pick is an honest policy signal, NOT silently executed.
            # Same as-chosen stance as actions: no auto-remap, failures returned.
            choice_lines = []
            answered = 0
            by_id = {t.get("taskId"): t for t in (game_state.get("allActiveTasks") or [])}
            for ch in parsed["choices"]:
                tid, cid = ch.get("taskId"), ch.get("choiceId")
                task = by_id.get(tid)
                if task is None:
                    # Model named a task that isn't active — action-space error.
                    self._log_action(
                        self._actor_for(agent), "game_action", "select_task_choice",
                        {"taskId": tid, "choiceId": cid, "success": False,
                         "error": "no_such_active_task", "commands": commands,
                         "note": args.get("note"), **self._outcome_fields("invalid")},
                    )
                    choice_lines.append(f"  ❌ task {tid}: no such active task")
                    continue
                if not self._may_answer_task(agent, task):
                    # Answered a task outside this officer's scope — action-space error.
                    self._log_action(
                        self._actor_for(agent), "game_action", "select_task_choice",
                        {"taskId": tid, "choiceId": cid, "success": False,
                         "error": "out_of_scope", "group": task_group(task),
                         "commands": commands, "note": args.get("note"),
                         **self._outcome_fields("invalid")},
                    )
                    choice_lines.append(
                        f"  ❌ task {tid}: outside your action scope "
                        f"(group {task_group(task)}) — not answered")
                    continue
                if not self._task_choice_supported:
                    choice_lines.append(
                        f"  ⏸ task {tid} choice {cid}: task-choice execution "
                        f"unavailable on this transport yet")
                    continue
                before = self._state_metrics(game_state)
                r, game_state = await self._execute_choice_via_unity(tid, cid, game_state)
                ok = bool(r.get("success"))
                err = r.get("error_message") or ""
                self._log_action(
                    self._actor_for(agent), "game_action", "select_task_choice",
                    {"taskId": tid, "choiceId": cid, "success": ok,
                     "error": err, "commands": commands, "note": args.get("note"),
                     **self._outcome_fields(
                         "ok" if ok else "rejected", before,
                         self._state_metrics(game_state), is_choice=True, tid=tid)},
                )
                results.append({"kind": "task_choice", "taskId": tid, "choiceId": cid,
                                "success": ok, "error": err})
                if ok:
                    # Fire the choice-resolved hook (e.g. a Bayesian preference elicitor). Inert
                    # when no hooks are registered. NOTE: this is an OFFICER answering a task
                    # choice; the director's package-selection is a separate future hook point.
                    await self._fire_hooks(
                        "on_choice_resolved",
                        {"kind": "task_choice", "actor": agent.subagent_name,
                         "source": "officer", "is_human": False,
                         "taskId": tid, "choiceId": cid, "choice": cid,
                         "group": task_group(task), "officer": agent.subagent_name},
                        agent=agent)
                if ok:
                    answered += 1
                    # Director-facing commit bubble for an answered task:
                    # 'Action: Chose "Send the airlift" for task "Food shortfall
                    # in Riverside"'. Resolve the human-readable choice text +
                    # task title from the task dict; fall back to ids if absent.
                    title = task.get("taskTitle") or task.get("title") or f"task {tid}"
                    choice_text = next(
                        (c.get("choiceText") or c.get("text") or "")
                        for c in (task.get("choices") or [])
                        if c.get("choiceId") == cid
                    ) if any(c.get("choiceId") == cid for c in (task.get("choices") or [])) else ""
                    choice_text = (choice_text or f"choice {cid}").strip()
                    await self._send_agent_response(
                        agent, f'Action: Chose "{choice_text}" for task "{title}"',
                        "agent_response", origin="router_action_receipt")
                    choice_lines.append(f"  ✅ answered task {tid} with choice {cid}")
                else:
                    choice_lines.append(
                        f"  ❌ task {tid} choice {cid}" + (f" — {err}" if err else ""))
            # Refresh the menu after mutating the world.
            all_actions = _enumerate_actions(game_state)
            filtered_actions = filter_actions(all_actions, agent.subaction_space)
            meta["executed"] = executed
            summary = f"Ran {len(actions_to_run)} action(s) from your commands; {executed} succeeded."
            if blocked:
                summary += (f" {len(blocked)} already-committed action(s) were skipped "
                            f"(queued from earlier this phase).")
            parts = [summary]
            if parsed["parsed"]:
                parts.append("Resolved: " + "; ".join(parsed["parsed"]))
            if lines:
                parts.append("\n".join(lines))
            if parsed["choices"]:
                parts.append(f"Answered {answered}/{len(parsed['choices'])} choice-task(s).")
            if choice_lines:
                parts.append("\n".join(choice_lines))
            if parsed["errors"]:
                # A command that didn't resolve to any real action/task is the
                # execute_commands analog of an out-of-range index: an action-space
                # error. Log each so "can't-execute" stays measurable on this path.
                for e in parsed["errors"]:
                    self._log_action(
                        self._actor_for(agent), "game_action", "execute_commands",
                        {"success": False, "error": "unresolved_command",
                         "detail": e, "commands": commands, "note": args.get("note"),
                         **self._outcome_fields("invalid")},
                    )
                    results.append({"success": False, "error": "unresolved_command",
                                    "detail": e})
                parts.append("Unresolved commands (NOT executed — fix and retry, or pick a "
                             "different move):\n  " + "\n  ".join(parsed["errors"]))
            if (not actions_to_run and not blocked
                    and not parsed["choices"] and not parsed["errors"]):
                parts.append("No command tags recognized. Use e.g. <build>Kitchen,1</build>.")
            body = "\n".join(parts)
            body += "\n\nUpdated actions:\n" + self._render_options_compact(filtered_actions, game_state)
            meta["results"] = results
            return body, game_state, all_actions, filtered_actions, meta

        if name == "propose_choices":
            result_text, game_state, all_actions, filtered_actions, executed, superseded, result_rows = \
                await self._continuous_propose(agent, args, game_state, all_actions, filtered_actions)
            meta["executed"] = executed
            # Surface the REAL per-action rows (execute_commands shape) so the logger
            # tallies genuine attempts/successes; an empty list (nothing selected /
            # superseded) correctly contributes zero attempted actions.
            meta["results"] = result_rows
            # A superseded proposal (director advanced the round or sent a new
            # instruction) ends this turn: the follow-up turn — the new round's
            # subagent or the director-message task — handles what comes next.
            # Without this the parked turn could immediately re-propose and
            # re-block the lock.
            if superseded:
                meta["finish"] = True
            else:
                # A live proposal surfaced choice cards to the director — director-facing.
                meta["spoke"] = True
            return result_text, game_state, all_actions, filtered_actions, meta

        if name == "send_message":
            message = str(args.get("message") or "").strip()
            if not message:
                return "ERROR: empty message.", game_state, all_actions, filtered_actions, meta
            to = str(args.get("to") or "Director").strip() or "Director"
            allowed = self._recipients_for(agent)
            if to not in allowed:
                # The enum should make this unreachable, but a model can still emit a name
                # outside it. Refuse with the real list rather than silently delivering to the
                # director: a misdelivered message read as a successful handoff is exactly the
                # failure this channel exists to make impossible.
                return (f"ERROR: you cannot message {to!r}. You may address: "
                        + ", ".join(allowed) + "."), \
                    game_state, all_actions, filtered_actions, meta
            await self._send_agent_response(agent, message, "agent_response", to=to)
            # `spoke` is what the post-loop fallback uses to decide whether the DIRECTOR got a
            # reply. A message to a peer is real work but it is NOT a reply to the director, so
            # it must not suppress that fallback -- otherwise an officer the director addressed
            # could answer its colleague and leave the director staring at a silent bubble.
            if to == "Director":
                meta["spoke"] = True
            return f"Message delivered to {to}.", \
                game_state, all_actions, filtered_actions, meta

        if name == "add_to_autonomy_list":
            text, shown, superseded = await self._continuous_autonomy_propose(agent, args)
            if superseded:
                meta["finish"] = True
            elif shown:
                meta["spoke"] = True      # the card is director-facing, like a proposal
            return text, game_state, all_actions, filtered_actions, meta

        if name == "remove_autonomy_rule":
            return (await self._remove_autonomy_rule(agent, args.get("rule_id"), by="officer"),
                    game_state, all_actions, filtered_actions, meta)

        if name == "finish":
            note = str(args.get("note") or "").strip()
            if note:
                await self._send_agent_response(agent, note, "agent_response")
                meta["spoke"] = True
            meta["finish"] = True
            return "Turn ended.", game_state, all_actions, filtered_actions, meta

        return f"ERROR: unknown tool {name!r}.", game_state, all_actions, filtered_actions, meta

    async def _continuous_propose(
        self,
        agent: AgentConfig,
        args: dict,
        game_state: dict,
        all_actions: List[dict],
        filtered_actions: List[dict],
    ) -> Tuple[str, dict, List[dict], List[dict], int, bool, List[dict]]:
        """Handle a propose_choices tool call: send cards, await the director's pick.

        Renders inline choice cards and awaits the choice_made reply. Blocks until the human director selects (or the
        autonomous director picks), then returns the outcome to the agent.
        """
        raw_packages = args.get("packages") or []
        reasoning = str(args.get("reasoning") or "").strip()

        # Sanitize the model-authored packages into the shape the client renders.
        # The model emits command tags (uniform vocabulary with execute_commands);
        # _tags_to_indices maps each package's tags onto positions in filtered_actions,
        # producing the same action_indices the client already consumes — so the
        # outbound payload and _await_director_choice stay byte-identical (no Unity
        # change). A package that resolves to zero actions is dropped with a reason;
        # never mis-index.
        packages: List[dict] = []
        drop_notes: List[str] = []
        for p in raw_packages:
            if not isinstance(p, dict):
                continue
            label = str(p.get("label") or f"Option {len(packages) + 1}")
            commands = str(p.get("commands") or "").strip()
            if commands:
                indices, reasons = self._tags_to_indices(commands, filtered_actions, game_state)
            else:
                indices, reasons = [], ["empty commands"]
            for r in reasons:
                print(f"[router]   ⤷ propose_choices: package '{label}': {r}")
            if not indices:
                note = f"package '{label}' dropped (no resolvable actions"
                note += f"; {'; '.join(reasons)})" if reasons else ")"
                drop_notes.append(note)
                continue
            packages.append({
                "package_index": len(packages),
                "label": label,
                "description": str(p.get("description") or ""),
                "action_indices": indices,
            })
        if not packages:
            msg = ("ERROR: no valid packages — each package's `commands` must contain "
                   "command tags that resolve to actions you can take now.")
            if drop_notes:
                msg += " " + " ".join(drop_notes)
            return (msg, game_state, all_actions, filtered_actions, 0, False, [])

        # Continuous agents render proposals INLINE in the chat timeline (a single
        # agent_message_with_choices frame) rather than as a Task Center task. This
        # keeps the cards in posted order with the surrounding narration and creates
        # Snapshot the action list the packages were built against: filtered_actions
        # is reassigned to the fresh post-execution list below, but the package's
        # action_indices point into THIS pre-execution list (used for the ledger).
        proposed_actions = list(filtered_actions)

        # Only one proposal may occupy the director's attention (single-slot
        # _pending_choice + one modal card) at a time. Hold the attention lock from
        # putting the card up through the director's pick so concurrent officers'
        # proposals queue rather than overwrite each other. This lock is distinct
        # from the commit lock, so officers can still execute_game_action while a
        # proposal is parked here awaiting the human.
        async with self._director_attention_lock:
            await self._send_inline_proposal(agent, packages, filtered_actions, reasoning)
            selected_idx, exec_results, game_state, superseded = await self._await_director_choice(
                packages, filtered_actions, game_state, reasoning)

        all_actions = _enumerate_actions(game_state)
        filtered_actions = filter_actions(all_actions, agent.subaction_space)

        executed = sum(1 for r in exec_results if (r or {}).get("success"))
        # Real per-action execution rows in the SAME shape execute_commands emits
        # (action_id/action_type/description/success/error) so the turn logger
        # counts these as genuine action attempts — not a single opaque
        # propose_choices summary that reads as 1 attempted / 0 successful.
        result_rows: List[dict] = []
        for r in exec_results:
            r = r or {}
            result_rows.append({
                "action_id": r.get("action_id"),
                "action_type": r.get("action_type"),
                "description": r.get("description"),
                "success": bool(r.get("success")),
                "error": r.get("error") or r.get("error_message") or "",
            })
        if superseded:
            body = ("The director withdrew the proposal without selecting a package "
                    "(they advanced the round or sent a new instruction). No action "
                    "taken — read the latest director message and state, then decide.")
        elif selected_idx is None:
            body = "The director did not select a package (no action taken)."
        else:
            label = packages[selected_idx]["label"] if 0 <= selected_idx < len(packages) else "?"
            total = len(exec_results)
            pkg_indices = packages[selected_idx].get("action_indices", []) \
                if 0 <= selected_idx < len(packages) else []
            # Enumerate the ENGINE's per-action outcome. The package label is the
            # agent's intent, not ground truth — some actions in a package fail
            # (e.g. a site already built on). Without this line-by-line result the
            # agent narrates the whole package as done and hallucinates successes.
            lines = []
            for pos, r in enumerate(exec_results):
                r = r or {}
                ok = r.get("success")
                aid = r.get("action_id") or r.get("action_index")
                err = (r.get("error") or "").strip()
                mark = "SUCCESS" if ok else "FAILED"
                lines.append(f"  - {aid}: {mark}" + (f" — {err}" if err and not ok else ""))
                # Ledger the succeeded actions so later turns this phase don't
                # re-propose them (order matches the package's action_indices).
                if ok and pos < len(pkg_indices):
                    ai = pkg_indices[pos]
                    if 0 <= ai < len(proposed_actions):
                        committed = proposed_actions[ai]
                        self._record_committed(committed)
                        # Per-action visibility parity with execute_game_action /
                        # execute_commands: narrate each executed action (esp. a build)
                        # to the director so a chosen package's effects show up in the
                        # chat timeline as they land — not only in the agent's summary.
                        await self._send_agent_response(
                            agent,
                            f"🔨 {committed.get('action_type', 'action')}: "
                            f"{committed.get('description', '(action)')}",
                            "agent_response", origin="router_action_receipt",
                        )
            detail = "\n".join(lines) if lines else "  (engine reported no results)"
            body = (f"The director selected package {selected_idx} ({label}). "
                    f"{executed}/{total} action(s) SUCCEEDED. Engine results:\n{detail}\n"
                    "Report ONLY the SUCCESS lines as done. Do NOT claim any FAILED "
                    "action happened — treat failures as not executed and adapt.")
        body += "\n\nUpdated actions:\n" + self._render_options_compact(filtered_actions, game_state)
        return body, game_state, all_actions, filtered_actions, executed, superseded, result_rows

    # ── Standing orders (add_to_autonomy_list) ───────────────────
    # An accepted order lets one officer use one typed action tool on its own at round
    # start, when the condition the Director approved holds. The officer judges the
    # condition; the router enforces the tool and its argument limits.
    _AUTONOMY_MAX_RULES = 5

    def _autonomy_allowed_tools(self, tools: List[dict]) -> List[str]:
        """The typed action tools in a palette, in palette order."""
        return [t["function"]["name"] for t in tools
                if t.get("function", {}).get("name") in _CORA_ACTION_TOOLS]

    def _autonomy_tool_schema(self, tools: List[dict]) -> List[dict]:
        """Narrow add_to_autonomy_list's `tool` enum to this palette's action tools, or drop
        the tool when there are none (an officer can only delegate what it can already do).
        Rewritten on a copy: TOOL_SCHEMAS is shared by every session on the server."""
        allowed = self._autonomy_allowed_tools(tools)
        out = []
        for t in tools:
            if t.get("function", {}).get("name") != "add_to_autonomy_list":
                out.append(t)
                continue
            if not allowed:
                continue
            fn = t["function"]
            props = dict(fn["parameters"]["properties"])
            props["tool"] = {**props["tool"], "enum": allowed}
            out.append({**t, "function": {**fn, "parameters": {**fn["parameters"],
                                                               "properties": props}}})
        return out

    @staticmethod
    def _autonomy_args_display(tool: str, args: dict) -> str:
        """'(any site)' or '(type kitchen)': the order's limits, as the card and prompt show them."""
        if not args:
            params = [n for n, _ in (TOOL_BY_NAME.get(tool, {}).get("params") or [])]
            what = params[0].replace("_id", "").replace("_", " ") if params else "arguments"
            return f"(any {what})"
        return "(" + ", ".join(f"{k.replace('_', ' ')} {v}" for k, v in args.items()) + ")"

    @staticmethod
    def _autonomy_args_match(limits: dict, call: dict) -> bool:
        """Every limit must hold. Numbers compare exactly; text compares case-insensitively,
        and a facility name may be a substring (a limit 'Shelter' covers 'Shelter Bravo'),
        the same looseness the staff/deconstruct site lookup already has."""
        for k, v in (limits or {}).items():
            got = call.get(k)
            if got is None:
                return False
            if isinstance(v, (int, float)) and not isinstance(v, bool):
                try:
                    if float(got) != float(v):
                        return False
                except (TypeError, ValueError):
                    return False
                continue
            want, have = str(v).strip().lower(), str(got).strip().lower()
            if want != have and not (k in ("site", "source", "dest") and want in have):
                return False
        return True

    def _autonomy_rule_for(self, agent: AgentConfig, tool: str, args: dict) -> Optional[dict]:
        for r in self._autonomy_rules.get(agent.subagent_name, []):
            if r["tool"] == tool and self._autonomy_args_match(r["args"], args or {}):
                return r
        return None

    async def _run_under_standing_order(self, agent, rule, tool_call, game_state,
                                        all_actions, filtered_actions, _skip_registry):
        """Carry out one call an order covers, through the ordinary (non-brief) path, with
        the order's id on its receipts, its result rows and the log."""
        name = agent.subagent_name
        ctx = self._turn_ctx.setdefault(name, {})
        ctx["autonomy_rule"] = rule["rule_id"]
        try:
            text, game_state, all_actions, filtered_actions, meta = \
                await self._dispatch_continuous_tool(
                    agent, tool_call, game_state, all_actions, filtered_actions,
                    brief_only=False, _skip_registry=_skip_registry)
        finally:
            ctx.pop("autonomy_rule", None)
        for row in meta.get("results") or []:
            if isinstance(row, dict):
                row["autonomy_rule"] = rule["rule_id"]
        meta["autonomy_rule"] = rule["rule_id"]
        self._log_action(self._actor_for(agent), "autonomy", "autonomy_rule_used", {
            "rule_id": rule["rule_id"], "tool": rule["tool"],
            "arguments": (tool_call.get("arguments") or {}),
            "executed": meta.get("executed", 0), "round": self.round_num})
        print(f"[router]   ⚙ {name}: standing order {rule['rule_id']} used "
              f"({meta.get('executed', 0)} action(s)).")
        return (f"[Standing order {rule['rule_id']}] " + text,
                game_state, all_actions, filtered_actions, meta)

    def _standing_orders_text(self, officer: str, brief_only: bool) -> str:
        """The officer's accepted orders, appended to its turn message ('' when none)."""
        rules = self._autonomy_rules.get(officer) or []
        if not rules:
            return ""
        lines = "\n".join(
            f"  {r['rule_id']}: {r['tool']} {self._autonomy_args_display(r['tool'], r['args'])}"
            f" — when: {r['context']}" for r in rules)
        if brief_only:
            return ("\n\nSTANDING ORDERS the Director approved — the one exception to the above:\n"
                    f"{lines}\n"
                    "For each order whose condition holds right now, carry it out with its tool, "
                    "within its limits. The chat shows the result automatically, so you do not "
                    "need to announce it. If no condition holds, leave the orders alone. Never "
                    "use these tools for anything else on this turn.")
        return ("\n\nYour standing orders (they run at the start of rounds):\n"
                f"{lines}\n"
                "If the Director tells you to stop one, cancel it with remove_autonomy_rule.")

    async def _continuous_autonomy_propose(self, agent: AgentConfig,
                                           args: dict) -> Tuple[str, bool, bool]:
        """Put a standing-order card to the Director and wait for Allow / Deny / Modify.

        Returns (result_text, card_shown, superseded)."""
        name = agent.subagent_name
        if not (self._turn_ctx.get(name) or {}).get("triggered_by_director"):
            return ("REFUSED: ask for a standing order only when the Director has asked you "
                    "for one (\"whenever...\", \"from now on...\"). You can suggest it to them "
                    "with send_message instead.", False, False)
        palette = self._autonomy_allowed_tools(build_tools(
            [t for t in (agent.tools or DEFAULT_TOOLS) if cora_ext.get_tool(t) is None]))
        tool = str(args.get("tool") or "").strip().lower()
        if tool not in palette:
            return (f"ERROR: {tool or '(none)'!r} is not one of your action tools. Choose one "
                    f"of: {', '.join(palette)}.", False, False)
        context = " ".join(str(args.get("context") or "").split())
        if not context:
            return ("ERROR: say when you would use it (`context`), in one plain sentence.",
                    False, False)
        if len(context) > 400:
            return ("ERROR: keep the condition to one or two sentences (under 400 characters).",
                    False, False)
        limits = args.get("args") or {}
        if isinstance(limits, str):
            try:
                limits = json.loads(limits) if limits.strip() else {}
            except ValueError:
                limits = None
        if not isinstance(limits, dict):
            return ("ERROR: `args` must be an object of the tool's parameters, e.g. "
                    "{\"type\": \"kitchen\"}, or left out.", False, False)
        params = [n for n, _ in TOOL_BY_NAME[tool]["params"]]
        unknown = [k for k in limits if k not in params]
        if unknown:
            return (f"ERROR: {tool} has no parameter {', '.join(map(repr, unknown))}. Its "
                    f"parameters are: {', '.join(params)}.", False, False)
        limits = {k: v for k, v in limits.items() if v not in (None, "")}
        rules = self._autonomy_rules.get(name, [])
        if len(rules) >= self._AUTONOMY_MAX_RULES:
            return (f"ERROR: you already have {len(rules)} standing orders "
                    f"({', '.join(r['rule_id'] for r in rules)}); cancel one with "
                    "remove_autonomy_rule first.", False, False)
        for r in rules:
            if r["tool"] == tool and r["args"] == limits and r["context"].lower() == context.lower():
                return (f"You already have this standing order ({r['rule_id']}).", False, False)

        reason = " ".join(str(args.get("reason") or "").split())[:300]
        proposal_id = uuid.uuid4().hex[:12]
        shown_args = self._autonomy_args_display(tool, limits)
        self._log_action(self._actor_for(agent), "autonomy", "autonomy_proposed", {
            "proposal_id": proposal_id, "tool": tool, "args": limits,
            "context": context, "reason": reason, "round": self.round_num})
        async with self._director_attention_lock:
            await self._send({
                "type": "autonomy_proposal",
                "proposal_id": proposal_id,
                "agent_name": name,
                "talkinghead_endpoint": agent.talkinghead_endpoint,
                "tool": tool,
                "args_display": shown_args,
                "context": context,
                "reason": reason,
                "round": self.round_num,
                "timestamp": _now(),
            })
            decision = await self._await_autonomy_decision(proposal_id)

        if decision.get("superseded") or decision.get("timeout"):
            await self._send({"type": "autonomy_update", "proposal_id": proposal_id,
                              "status": "withdrawn"})
            why = ("the Director moved on (new round or a new instruction)"
                   if decision.get("superseded") else "no answer within 5 minutes")
            return (f"No standing order was added: {why}. Don't act on it unprompted.",
                    True, bool(decision.get("superseded")))
        if str(decision.get("decision") or "").lower() != "accept":
            note = (" (no human Director to approve it)" if decision.get("auto") else "")
            return ("DENIED: the Director declined this standing order" + note + ". Don't do "
                    "it unprompted; carry on as before.", True, False)

        final = " ".join(str(decision.get("context") or "").split()) or context
        edited = final != context
        self._autonomy_seq += 1
        rule = {"rule_id": f"R{self._autonomy_seq}", "tool": tool, "args": limits,
                "context": final, "original_context": context, "edited": edited,
                "round": self.round_num, "proposal_id": proposal_id}
        self._autonomy_rules.setdefault(name, []).append(rule)
        self._log_action(self._actor_for(agent), "autonomy", "autonomy_rule_added", dict(rule))
        await self._send({"type": "autonomy_update", "proposal_id": proposal_id,
                          "status": "edited" if edited else "accepted",
                          "rule_id": rule["rule_id"], "context": final})
        print(f"[router]   ⚙ {name}: standing order {rule['rule_id']} added "
              f"({tool} {shown_args}{', edited' if edited else ''}).")
        text = (f"ALLOWED: standing order {rule['rule_id']} — {tool} {shown_args}, when: {final}")
        if edited:
            text += (f"\nThe Director reworded your condition (you proposed: \"{context}\"). "
                     "Follow THEIR wording.")
        return (text + "\nFrom now on, at the start of each round, you may use this tool on "
                "your own, but only when that condition holds.", True, False)

    async def _await_autonomy_decision(self, proposal_id: str) -> dict:
        """Wait for autonomy_decision on the shared pending slot (5 min). A director_policy
        cannot judge a standing order, so it is declined without a card wait."""
        if self._director_policy is not None:
            return {"decision": "deny", "auto": True}
        fut = asyncio.get_event_loop().create_future()
        self._pending_choice = fut
        self._pending_autonomy_id = proposal_id
        try:
            msg = await asyncio.wait_for(fut, timeout=300.0)
            if msg.get("superseded"):
                return {"superseded": True}
            return msg
        except asyncio.TimeoutError:
            return {"timeout": True}
        finally:
            if self._pending_choice is fut:
                self._pending_choice = None
            if self._pending_autonomy_id == proposal_id:
                self._pending_autonomy_id = None

    def _handle_autonomy_decision(self, msg: dict) -> None:
        """The Director clicked Allow, Deny, or submitted a Modify edit on a card."""
        pid = msg.get("proposal_id")
        self._log_action(HUMAN_DIRECTOR_ACTOR, "autonomy", "autonomy_decision", {
            "proposal_id": pid, "agent_name": msg.get("agent_name"),
            "decision": msg.get("decision"), "context": msg.get("context"),
            "edited": bool(msg.get("edited"))},
            click_seq=msg.get("click_seq"), client_ts=msg.get("timestamp"))
        fut = self._pending_choice
        if pid and pid == self._pending_autonomy_id and fut is not None and not fut.done():
            fut.set_result(msg)
        else:
            print(f"[router]   ⚠️  autonomy_decision for {pid!r} with no matching card pending.")

    async def _remove_autonomy_rule(self, agent: Optional[AgentConfig], rule_id,
                                    by: str) -> str:
        """Cancel a standing order. `agent` None = search every officer (developer panel)."""
        rid = str(rule_id or "").strip().upper()
        owners = ([agent.subagent_name] if agent is not None else list(self._autonomy_rules))
        for owner in owners:
            rules = self._autonomy_rules.get(owner, [])
            for r in rules:
                if r["rule_id"] == rid:
                    rules.remove(r)
                    officer = agent or self._get_agent_by_name(owner)
                    self._log_action(self._actor_for(officer) if by == "officer" else
                                     HUMAN_DIRECTOR_ACTOR if by == "director" else SYSTEM_ACTOR,
                                     "autonomy", "autonomy_rule_removed",
                                     {"rule_id": rid, "officer": owner, "by": by, "rule": r})
                    if officer is not None:
                        await self._send_agent_response(
                            officer, f"Standing order {rid} cancelled ({r['tool']} — when: "
                            f"{r['context']}).", "agent_response", origin="router_template")
                    return f"Cancelled standing order {rid}."
        have = ", ".join(r["rule_id"] for o in owners for r in self._autonomy_rules.get(o, []))
        return f"ERROR: no standing order {rid or '(none)'}. " + (
            f"Current ones: {have}." if have else "There are none.")

    def _supersede_pending_choice(self, reason: str) -> None:
        """Release a continuous turn parked at propose_choices awaiting the human.

        That turn holds _director_attention_lock while awaiting _pending_choice (up
        to 5min). If the human moves on without picking, that lock would starve
        every later proposer. Resolving the future with a 'superseded' sentinel lets
        the parked turn unwind and free the lock promptly. No-op if nothing is
        pending (a real choice_made still resolves normally via _handle_choice_made).
        """
        pc = self._pending_choice
        if pc is not None and not pc.done():
            print(f"[router]   ⏭  Superseding pending proposal ({reason}).")
            pc.set_result({"superseded": True, "reason": reason})

    async def _handle_choice_made(self, msg: dict):
        print(f"[router] 📨 choice_made received: agent={msg.get('agent_name')}, "
              f"package={msg.get('package_index')}, "
              f"results={len(msg.get('execution_results', []))} actions")
        print(f"[router]    _pending_choice state: {self._pending_choice}, "
              f"done={self._pending_choice.done() if self._pending_choice else 'N/A'}")
        # Human game action: the director picked (and Unity already executed) a
        # package. Log it independently of the pending-choice Future.
        self._log_action(
            HUMAN_DIRECTOR_ACTOR,
            "game_action",
            "choice_made",
            {
                "agent_name": msg.get("agent_name"),
                "package_index": msg.get("package_index"),
                "execution_results": msg.get("execution_results", []),
            },
            click_seq=msg.get("click_seq"),
            client_ts=msg.get("timestamp"),
        )
        # Fire hooks for the HUMAN director's choice + each action it executed. Differentiable
        # from officer/AI events via actor / source="director" / is_human=True. Session-level ctx
        # (agent=None): a human action isn't tied to one officer.
        _results = msg.get("execution_results", []) or []
        await self._fire_hooks("on_choice_resolved", {
            "kind": "package_choice", "actor": HUMAN_DIRECTOR_ACTOR, "source": "director",
            "is_human": True, "choice": msg.get("package_index"),
            "package_index": msg.get("package_index"),
            "from_officer": msg.get("agent_name"), "execution_results": _results}, agent=None)
        for _res in _results:
            await self._fire_hooks("on_action_executed", {
                "actor": HUMAN_DIRECTOR_ACTOR, "source": "director", "is_human": True,
                "action": _res}, agent=None)
        if self._pending_autonomy_id is not None:
            # The pending slot holds a standing-order card, not a package proposal; a stale
            # choice card clicked now must not be read as the Director's answer to it.
            print("[router]    ⚠️  choice_made while a standing-order card is pending — ignored.")
        elif self._pending_choice and not self._pending_choice.done():
            print(f"[router]    ✅ Setting result on pending Future")
            self._pending_choice.set_result(msg)
        else:
            print(f"[router]    ⚠️  WARNING: No pending choice to fulfill!")

    async def _handle_action_result(self, msg: dict):
        """Handle action execution result from Unity, for the officer that asked for it.

        CORRELATE BY ID, NOT BY TIMING. There is one in-flight slot, held under
        _unity_commit_lock, so under normal operation exactly one officer is waiting and
        timing is enough. It stops being enough when a send TIMES OUT: the waiter is
        dropped, the lock is released, the next officer arms a fresh future, and Unity's
        late reply to the FIRST action then lands on the SECOND officer, which reads
        another officer's tool result as its own. Construction can take >10s on the Unity
        side, so the 30s window is not unreachable.

        ActionExecutionResult already carries action_id and the enumerator already assigns
        one, so the key round-trips today -- it simply was not being checked.
        """
        if not (self._pending_action and not self._pending_action.done()):
            return                                    # nothing waiting; genuinely stray
        expected = self._pending_action_key
        if expected is not None and msg.get("action_id") not in (None, expected):
            print(f"[router]    ⚠️  Dropping stray action result "
                  f"{msg.get('action_id')!r}; the waiter expects {expected!r} "
                  f"(a previous action almost certainly timed out).")
            return
        self._pending_action.set_result(msg)

    def _emit_round_state(self, game_state: dict, phase: str) -> None:
        """One `round_state` event: the game's own score (cora.scoring), budget, efficiency, the full rewardMetrics counters, and the
        active-task count. Same fields the gym and the benchmark record per round, so a
        human session, an LLM session and a benchmark episode line up column for column."""
        try:
            sab = game_state.get("satisfactionAndBudget") or {}
            rm = game_state.get("rewardMetrics") or {}
            comps = score_components(rm)
            self._emit("round_state", {
                "phase": phase,
                "budget": sab.get("budget"),
                "satisfaction": sab.get("satisfaction"),
                "efficiency": sab.get("efficiency"),
                "active_tasks": len(game_state.get("allActiveTasks") or []),
                "score": comps["score"],
                "score_components": comps,
                "reward_metrics": rm,
            })
        except Exception as e:   # logging must never break a live session
            print(f"[router] round_state logging failed: {e}")

    def _handle_provenance(self, msg: dict) -> None:
        """Which scenario this session actually played: seed, RNG state, build, and every
        parameter in effect. Arrives once, after the client's parameters finish loading."""
        params = msg.get("parameters") or ""
        try:
            params = json.loads(params) if params else {}
        except (TypeError, ValueError):
            pass   # keep the raw string rather than drop it
        self._emit("provenance", {
            "seed": msg.get("seed"),
            "seed_source": msg.get("seed_source"),
            "rng_state": msg.get("rng_state"),
            "build_guid": msg.get("build_guid"),
            "game_version": msg.get("game_version"),
            "platform": msg.get("platform"),
            "param_source": msg.get("param_source"),
            "parameters": params,
            "map_hash": msg.get("map_hash"),
            "map_status": msg.get("map_status"),
        })

    def _handle_round_end(self, msg: dict):
        print(f"[router] Round {self.round_num} ended.")

    def _handle_client_event(self, msg: dict):
        """Human decision-support UI interaction from Unity (Tier-1 ui_interaction).

        e.g. opening an agent's conversation, switching officers, selecting/
        switching a choice package, clicking confirm, opening metrics. The raw
        click coords arrive separately via gui_event; this carries the meaning.
        """
        name = msg.get("name")
        if not name:
            return
        # category defaults to ui_interaction but the client may send "game_action"
        # for direct human actions (build/worker/deconstruct via the UI).
        self._log_action(
            HUMAN_DIRECTOR_ACTOR,
            msg.get("category", "ui_interaction"),
            name,
            msg.get("payload", {}),
            click_seq=msg.get("click_seq"),
            client_ts=msg.get("timestamp"),
        )

    def _handle_gui_event(self, msg: dict):
        """Raw human mouse click from Unity (every click) → unified log.

        Provides the GUI-control-training stream and the unproductive-click
        signal. payload carries screen/normalized coords + the UI element hit.
        """
        self._log_action(
            HUMAN_DIRECTOR_ACTOR,
            "ui_interaction",
            "click",
            msg.get("payload", {}),
            click_seq=msg.get("click_seq"),
            client_ts=msg.get("timestamp"),
        )

    def _handle_game_start(self, msg: dict):
        """Unity signals a fresh play session — wipe conversation state.

        Fires once per Unity Play session on websocket open. Clears the in-memory
        MessageQueue and resets the round counter + per-agent reproposal context so
        the new game starts with no stale conversation, no archived choice context.
        """
        self.message_queue.clear_all()
        self.round_num = 0
        self.day = 1
        self.segment = 0
        # A continuous agent's transcript spans a whole game; a fresh game must
        # start it clean (no stale trajectory bleeding across games).
        self._continuous_transcripts.clear()
        self._msg_injected_count.clear()
        # World state and the planning-phase ledger are per-GAME too. Leaving them behind let a
        # director_message arriving between game_start and the first begin_round pass the
        # `if self._latest_game_state:` gate and run a turn against the PREVIOUS game's state.
        self._latest_game_state = None
        self._latest_all_actions = []
        self._committed_this_phase = []
        self._committed_spend_this_phase = 0.0
        print("[router] 🆕 game_start received — message queue cleared, round counter reset.")

    async def _handle_director_message(self, msg: dict):
        """Handle conversational message from director to an agent."""
        to_agent_name = msg.get("to_agent")
        content = msg.get("content", "")

        if not to_agent_name or not content:
            print(f"[router] Invalid director_message: missing to_agent or content")
            return

        # Resolve the agent config FIRST and canonicalize the conversation key on
        # subagent_name. Unity may address the agent by either its subagent_name
        # or its talkinghead_endpoint (see _get_agent_by_name), and every other
        # reader/writer (proposal recording, _send_agent_response, _run_choices,
        # _repropose_choices) keys the thread by subagent_name. Keying by the raw
        # to_agent (e.g. the talkinghead endpoint "FoodMassCare") split the thread
        # so classify/clarify/chat never saw the agent's own prior turns or the
        # packages it proposed — the "I don't have a record of what I proposed" bug.
        agent = self._get_agent_by_name(to_agent_name)
        if not agent:
            print(f"[router] Agent '{to_agent_name}' not found")
            return
        convo_key = agent.subagent_name

        print(f"[router] Director → {convo_key}: {content[:50]}...")

        # Store director message in queue
        message = self.message_queue.send_message(
            from_agent="Director",
            to_agent=convo_key,
            content=content,
            msg_type="director_message",
            round_num=self.round_num
        )

        # Log director message
        self._emit("conversation_message", {
            "from": "Director",
            "to": convo_key,
            "content": content,
            "message_type": "director_message",
            "message_id": message["id"],
            "click_seq": msg.get("click_seq"),
        }, actor=HUMAN_DIRECTOR_ACTOR, client_ts=message["timestamp"])

        conversation = self.message_queue.get_conversation(convo_key, "Director")

        # Continuous agents: the director's message is a fresh trigger to ACT, not
        # just to chat. Re-enter the tool loop so the agent can actually execute /
        # propose / talk in response — otherwise it would only narrate (and, as
        # seen, hallucinate) actions it never took. The director's message is
        # already in `conversation`, so the loop sees the request in context.
        if agent.actor_type == "continuous":
            if self._latest_game_state:
                # The director redirecting mid-proposal ("I don't like these,
                # build X" / "repropose") supersedes their own pending proposal —
                # withdraw it so the parked turn releases _director_attention_lock
                # and this new instruction isn't starved behind the 5min choice wait.
                # No-op if no proposal is pending.
                self._supersede_pending_choice("director sent a new instruction")
                # Run in a background task so awaiting this officer's per-agent lock
                # never blocks the receive loop. If we awaited inline while the same
                # officer's begin_round turn held its lock (parked at propose_choices),
                # the loop couldn't process the choice_made that would release it →
                # deadlock.
                # The task re-reads _latest_game_state at run time (after the lock
                # frees), so a turn that follows a build observes the post-build
                # world instead of the stale pre-build snapshot.
                _t = asyncio.create_task(self._run_continuous_for_message(
                    agent, cause_message_id=message["id"]))
                _t.add_done_callback(self._on_round_task_done)
                return
            # No state yet (message before any begin_round): fall through to chat.

        # No game state yet (a message before the first round): a plain conversational reply.
        response_text = await asyncio.to_thread(self._generate_conversational_response, agent, conversation)
        await self._send_agent_response(agent, response_text, "agent_response")

    async def _send_agent_response(self, agent: AgentConfig, response_text: str, msg_type: str,
                                   to: str = "Director", origin: str = "llm"):
        """Persist + log + push an agent's conversational message to `to`.

        Peer messages ride the SAME path as director messages on purpose: they land in the
        transcript, the session log and the GUI exactly like anything else an officer says.
        Nothing an officer tells a colleague is hidden from the director -- if officers could
        coordinate privately, the one interaction the study measures would be unobservable."""
        # Drop a leading "<Role> Officer:" self-label badge (the avatar already shows
        # who's speaking); the prompt asks officers to introduce themselves once, not
        # on every message. See _strip_self_label.
        response_text = _strip_self_label(response_text)
        # An action carried out under a standing order: say which order, in the chat and
        # the log, so the Director (and the corpus) can tell it from an asked-for action.
        _rule = (self._turn_ctx.get(agent.subagent_name) or {}).get("autonomy_rule")
        if _rule and origin == "router_action_receipt":
            origin = "autonomy_rule"
            response_text = f"Standing order {_rule} · {response_text}"
        response_message = self.message_queue.send_message(
            from_agent=agent.subagent_name,
            to_agent=to,
            content=response_text,
            msg_type=msg_type,
            round_num=self.round_num,
        )
        # Causal links: the officer turn that produced this message, and the message that
        # woke that turn. Chained with agent_turn.caused_by_message_id these rebuild the
        # whole conversation graph (msg -> turn -> msg ...). None outside a continuous turn.
        _ctx = self._turn_ctx.get(agent.subagent_name) or {}
        self._emit("conversation_message", {
            "from": agent.subagent_name,
            "to": to,
            "content": response_text,
            "message_type": msg_type,
            "message_id": response_message["id"],
            "turn_id": _ctx.get("turn_id"),
            "caused_by_message_id": _ctx.get("caused_by_message_id"),
            # Who actually wrote the words: "llm" (the officer's model), "router_action_receipt"
            # (the router's "Action: ..." line for a committed action) or "router_template"
            # (a fixed fallback/acknowledgement). All three render identically in the chat, so
            # without this the corpus could not tell model speech from harness text.
            # Log-only: the frame sent to the client is unchanged.
            "origin": origin,
        }, agent=agent, client_ts=response_message["timestamp"])
        await self._send({
            "type": "agent_message",
            "agent_name": agent.subagent_name,
            "talkinghead_endpoint": agent.talkinghead_endpoint,
            # Recipient, so the client can render a peer message under the RECIPIENT's tab
            # with a "From: <sender>" badge instead of dropping it into the sender's own
            # thread with the director. `to_endpoint` is sent alongside the name because the
            # client routes tabs by talkinghead_endpoint, and it has no name->endpoint map
            # (OfficerRoster is endpoint->name). Resolving it here keeps the client from
            # having to invert a mapping that is not guaranteed to be one-to-one.
            "to": to,
            "to_endpoint": ("" if to == "Director" else
                            (getattr(self._get_agent_by_name(to), "talkinghead_endpoint", "") or "")),
            "content": response_text,
            "message_type": msg_type,
            "round": self.round_num,
            "timestamp": response_message["timestamp"],
        })
        print(f"[router] {agent.subagent_name} → {to}: {response_text[:60]}...")

        # A message to a COLLEAGUE wakes them, the same way a director message does. Spawned as
        # a background task and never awaited: _agent_lock is per-agent, so awaiting the
        # recipient's turn inline would deadlock the moment that officer messaged back (the
        # director path carries the same warning for the same reason).
        if to != "Director":
            recipient = self._get_agent_by_name(to)
            if recipient is not None and recipient.actor_type == "continuous":
                if getattr(self, "_peer_triggers_left", 0) > 0 and self._latest_game_state:
                    self._peer_triggers_left -= 1
                    status = "woken"
                    print(f"[router]   ✉ peer trigger: {agent.subagent_name} → {to} "
                          f"({self._peer_triggers_left} left this round)")
                    _t = asyncio.create_task(self._run_continuous_for_message(
                        recipient, by_peer=True, cause_message_id=response_message["id"]))
                    _t.add_done_callback(self._on_round_task_done)
                else:
                    # Not dropped -- _inject_unseen_messages still delivers it on the
                    # recipient's next ordinary turn. Only the immediate wake-up is skipped.
                    status = ("budget_exhausted" if self._latest_game_state
                              else "no_game_state")
                    print(f"[router]   ✉ peer message queued (no trigger budget left this "
                          f"round): {agent.subagent_name} → {to}")
                # Logged either way: when the per-round cap skips a wake-up, the cap -- not the
                # officer -- decided who got to answer, and that must be visible in the data.
                self._emit("peer_wake", {
                    "from": agent.subagent_name,
                    "to": to,
                    "message_id": response_message["id"],
                    "status": status,
                    "budget_left": getattr(self, "_peer_triggers_left", 0),
                }, agent=agent)

    def _get_agent_by_name(self, agent_name: str) -> Optional[AgentConfig]:
        """Find agent by subagent_name, with talkinghead_endpoint fallback.

        Unity's AgentConfigLoader sometimes can't resolve the subagent_name
        for the currently-selected tab (config not loaded, enum match misses)
        and falls back to sending the TaskOfficer enum string (e.g.
        ``"DisasterOfficer"``) as ``to_agent``. Accept either form so the
        message still routes to the right agent.
        """
        if not agent_name:
            return None
        for agent in self.config.agents:
            if agent.subagent_name == agent_name:
                return agent
        # Talkinghead fallback (case-insensitive).
        name_lower = agent_name.lower()
        for agent in self.config.agents:
            th = (agent.talkinghead_endpoint or "")
            if th.lower() == name_lower:
                return agent
        return None

    # ── Unity Communication ──────────────────────────────────────

    def _generate_conversational_response(self, agent: AgentConfig, conversation: list) -> str:
        """
        Generate a conversational response from an agent using their LLM.

        Args:
            agent: The agent configuration
            conversation: List of conversation messages

        Returns:
            Agent's conversational response string
        """
        import anthropic
        import openai

        provider = agent.llm_provider.lower() if agent.llm_provider else "anthropic"

        # Build conversational prompt
        messages = []
        for entry in conversation:
            if entry.get("from") == "Director":
                role = "user"
                messages.append({"role": role, "content": entry.get("content", "")})
            elif entry.get("from") == agent.subagent_name:
                role = "assistant"
                messages.append({"role": role, "content": entry.get("content", "")})

        # System prompt — tight and informational. Matches the style of the
        # auto-agent action prompt so chat replies stay short and on-task.
        system_prompt = (
            f"You are {agent.subagent_name}, an internal operator reporting to the Director on disaster response.\n\n"
            f"Reply to the Director's last message in 1–3 short sentences. Be direct and informational.\n\n"
            f"Hard rules:\n"
            f"- No greetings, sign-offs, role-play, or 'Yes, Director' style flourishes.\n"
            f"- Do not wrap your reply in quotation marks.\n"
            f"- Stick to: what you did this round, why, current constraints, what you plan next.\n"
            f"- If the Director asks for an action, briefly say whether you will do it or why you can't."
        )

        if agent.system_prompt:
            system_prompt += f"\n\nAgent role: {agent.system_prompt}"

        # Query the LLM
        try:
            if provider == "anthropic":
                api_key = os.environ.get("ANTHROPIC_API_KEY")
                if not api_key:
                    return "I'm unable to respond right now - API key not configured."

                client = anthropic.Anthropic(api_key=api_key)
                response = client.messages.create(
                    model=agent.llm_model or "claude-sonnet-4-6",
                    max_tokens=200,
                    system=system_prompt,
                    messages=messages
                )
                return response.content[0].text

            elif provider == "openai":
                api_key = os.environ.get("OPENAI_API_KEY")
                if not api_key:
                    return "I'm unable to respond right now - API key not configured."

                # Support custom base_url for third-party providers
                base_url = agent.llm_endpoint if hasattr(agent, 'llm_endpoint') else None
                if base_url:
                    client = openai.OpenAI(api_key=api_key, base_url=base_url)
                else:
                    client = openai.OpenAI(api_key=api_key)

                msgs = [{"role": "system", "content": system_prompt}] + messages
                response = client.chat.completions.create(
                    model=agent.llm_model or "gpt-4",
                    max_tokens=200,
                    messages=msgs
                )
                return response.choices[0].message.content

            else:
                return "I'm unable to respond - unsupported LLM provider."

        except Exception as e:
            print(f"[router] Error generating conversational response for {agent.subagent_name}: {e}")
            return "I'm having trouble responding right now."

    async def _send(self, payload: dict):
        ws = self._websocket
        if ws is None:
            return
        # A user closing/reloading the tab mid-round drops the socket while a
        # background begin_round task is still emitting frames. Sending on a
        # closed socket makes Starlette raise RuntimeError ("websocket.send
        # after websocket.close"), which surfaced as a flood of unhandled task
        # exceptions. Skip the send once the socket is no longer connected, and
        # swallow the close-race so a mid-round disconnect can't crash the task.
        if (ws.client_state != WebSocketState.CONNECTED
                or ws.application_state != WebSocketState.CONNECTED):
            return
        try:
            await ws.send_text(json.dumps(payload))
        except (WebSocketDisconnect, RuntimeError) as e:
            # Socket closed between the state check and the send — benign for
            # the demo (client disconnected); drop the frame instead of raising.
            print(f"[router][{self.api_key_label}] dropped send (socket closed): {e}")

    # ── Helpers ──────────────────────────────────────────────────

    def _validate_game_state(self, game_state: dict):
        """Validate game state has required fields with valid values."""
        # Check for satisfactionAndBudget field
        if "satisfactionAndBudget" not in game_state:
            raise ValueError(
                "Missing 'satisfactionAndBudget' in game state. "
                "Unity may not be sending budget/satisfaction data correctly."
            )

        sat_budget = game_state["satisfactionAndBudget"]

        # Validate budget is present and reasonable
        budget = sat_budget.get("budget", None)
        if budget is None:
            raise ValueError("Missing 'budget' field in satisfactionAndBudget")

        if budget < 0:
            print(f"[router] ⚠️  Warning: Negative budget detected: {budget}")

        # Validate satisfaction is present
        satisfaction = sat_budget.get("satisfaction", None)
        if satisfaction is None:
            raise ValueError("Missing 'satisfaction' field in satisfactionAndBudget")

        # Log validation info on round 1
        if self.round_num == 1:
            print(f"[router] ✓ Game state validated: Budget=${budget}, Satisfaction={satisfaction}")

    def _filter_state(self, game_state: dict, agent: AgentConfig) -> dict:
        filtered = filter_observation(game_state, agent.subobservation_space)
        # Tasks obs bug + jurisdiction routing. filter_observation copies keys by
        # name, but the raw game_state key is `allActiveTasks` while configs list the
        # ENCODED name `tasks` — so tasks were silently dropped and the observation (which
        # reads `allActiveTasks`) rendered none. Re-inject under the raw key, narrowed
        # to this agent's jurisdiction so each officer sees only the tasks Unity would
        # route to it (task_officer mirrors the hardcoded Unity assignment). An agent
        # with no talkinghead_endpoint (the director) sees all active tasks.
        obs_space = agent.subobservation_space or []
        # Per-group obs gating (config-driven, matches the action-space scheme):
        # "tasks:<group>" entries narrow the visible tasks to those groups. Bare
        # "tasks" keeps the back-compat behavior — all tasks Unity would route to
        # this officer (jurisdiction via task_officer). The director ("all") already
        # gets allActiveTasks through filter_observation and skips this block.
        task_groups = {e.split(":", 1)[1] for e in obs_space
                       if isinstance(e, str) and e.startswith("tasks:")}
        if task_groups:
            active = game_state.get("allActiveTasks") or []
            filtered["allActiveTasks"] = [t for t in active if task_group(t) in task_groups]
        elif "tasks" in obs_space:
            active = game_state.get("allActiveTasks") or []
            ep = agent.talkinghead_endpoint
            if ep:
                active = [t for t in active if task_officer(t) == ep]
            filtered["allActiveTasks"] = list(active)
        return filtered

    async def _send_proposal(self, agent: AgentConfig, packages: List[dict], frame: dict):
        """Send a proposal frame to Unity and record it into conversation memory.

        `frame` is the fully-built outgoing payload — choices_proposal for the Task
        Center path, agent_message_with_choices for the inline path. The memory
        record is identical either way: without it get_conversation() holds only
        chat text, so the classify/clarify/repropose LLM calls see no record of the
        packages and the agent says things like "I don't have a record of the
        strategy packages I previously proposed."
        """
        await self._send(frame)
        memory = self._format_proposal_for_memory(packages)
        if memory:
            self.message_queue.send_message(
                from_agent=agent.subagent_name,
                to_agent="Director",
                content=memory,
                msg_type="choices_proposal",
                round_num=self.round_num,
            )

    async def _send_inline_proposal(
        self,
        agent: AgentConfig,
        packages: List[dict],
        filtered_actions: List[dict],
        reasoning: str,
    ):
        """Push an inline agent_message_with_choices frame to Unity (inline render path).

        Used by continuous agents: the client renders the proposal as choice cards
        inline in the chat timeline (AddAgentMessageWithChoices) — in posted order,
        with NO Task Center task. The choice_made round-trip is identical to the
        task-backed path, so _pending_choice resolution is unchanged.
        """
        content = reasoning or "Here are a few options — pick one."
        await self._send_proposal(agent, packages, {
            "type": "agent_message_with_choices",
            "agent_name": agent.subagent_name,
            "talkinghead_endpoint": agent.talkinghead_endpoint,
            "content": content,
            "message_type": "agent_response",
            "reasoning": reasoning,
            "packages": packages,
            "available_actions": filtered_actions,
            "round": self.round_num,
            "timestamp": _now(),
        })

    @staticmethod
    def _format_proposal_for_memory(packages: List[dict]) -> str:
        """Render a faithful record of a choices proposal for conversation memory.

        Recorded as one of the agent's own turns so it can later quote exactly
        what it offered when the Director asks it to explain, clarify, or repropose.
        Deliberately NOT the compose_summary blob (that carries the repropose hint
        and a day/budget preamble that read as meta-noise); just the packages, with
        a generous description budget so the record never looks truncated — a
        truncated-looking record made the model distrust and disown it.
        """
        lines = ["Here are the exact options I proposed to the Director this round:"]
        for i, p in enumerate(packages):
            label = (p.get("label") or f"Option {i + 1}").strip()
            desc = " ".join((p.get("description") or "").split())
            if len(desc) > 500:
                desc = desc[:500].rstrip() + "…"
            lines.append(f"{i + 1}) {label} — {desc}" if desc else f"{i + 1}) {label}")
        return "\n".join(lines)

    def _log_turn(
        self,
        agent: AgentConfig,
        filtered_state: dict,
        filtered_actions: List[dict],
        packages: list,
        selected_idx,
        results: list,
        sat_before: float,
        game_state_after: dict,
        budget_before: float,
        raw: str,
        tokens: int,
        trigger: Optional[str] = None,
        tools_called: Optional[list] = None,
        extra: Optional[dict] = None,
    ):
        # Continuous officers keep their history in _continuous_transcripts, not
        # agent.conversation_history (which only the auto/choices path appends to), so the
        # old count was structurally 0 for every officer turn.
        if agent.actor_type == "continuous":
            history_len = len(self._continuous_transcripts.get(agent.subagent_name, []))
        else:
            history_len = len(agent.conversation_history)
        self.logger.log_turn(
            episode_id=self.episode_id,
            round_num=self.round_num,
            day=game_state_after.get("sessionInfo", {}).get("currentDay", 0),
            segment=game_state_after.get("sessionInfo", {}).get("currentTimeSegment", 0),
            agent_name=agent.subagent_name,
            role=agent.role,
            actor_type=agent.actor_type,
            subobservation=filtered_state,
            subactions_available=len(filtered_actions),
            proposed_packages=packages,
            selected_package_index=selected_idx,
            execution_results=results,
            satisfaction_before=sat_before,
            satisfaction_after=_get_satisfaction(game_state_after),
            budget_before=budget_before,
            budget_after=_get_budget(game_state_after),
            llm_raw_response=raw,
            conv_history_length=history_len,
            tokens_used=tokens,
            # Pass the post-execution state so the logger can route reward through
            # the shared gym scorer (game_state_after["rewardMetrics"]).
            game_state_after=game_state_after,
            # Keeps the turn record attributable in a MERGED corpus (bulk export → SFT).
            session_id=self.session_id,
            # WHY this turn ran (director addressed it / a peer officer messaged it / the
            # round started) and WHICH tools it called, in order. This is the hook for
            # logging sub-agent use: a delegated turn is one more trigger value.
            trigger=trigger,
            tools_called=tools_called,
            extra=extra,
        )


# ── Utilities ────────────────────────────────────────────────────

def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


def _get_satisfaction(state: dict) -> float:
    return state.get("satisfactionAndBudget", {}).get("satisfaction", 0)


def _get_budget(state: dict) -> float:
    return state.get("satisfactionAndBudget", {}).get("budget", 0)


# ── Multi-tenant Service ─────────────────────────────────────────

# Interactive API docs enumerate EVERY route and its schema — a free map of the attack
# surface (including which capabilities exist). Off unless explicitly opted in for local
# dev: safe-by-default, since production is the case you can forget to harden.
_DEV_DOCS = os.environ.get("CORA_DEV_DOCS", "").strip().lower() in ("1", "true", "yes")
_DOCS_KW = {} if _DEV_DOCS else {"docs_url": None, "redoc_url": None, "openapi_url": None}

# CONTROL PLANE / DATA PLANE SPLIT.
# `app` (data plane) is the public surface: gameplay WS, config catalog, bundle upload,
# a caller's own session data. It binds 0.0.0.0 and sits behind the Apache proxy.
# `admin_app` (control plane) carries the privileged surface — minting keys and ACTIVATING
# uploaded code — and binds 127.0.0.1 only, so it is not reachable from the internet at all.
# This is enforced at the socket, not by proxy rules: a missing/incorrect Apache rule can
# no longer expose admin (fail-closed instead of fail-open), and admin routes never appear
# in the public app's schema. Reach it with an SSH tunnel:
#     ssh -L 9877:127.0.0.1:9877 <host>   then hit http://localhost:9877/admin/...
# Paths are unchanged (/admin/...) so existing scripts only need a different base URL.
app = FastAPI(**_DOCS_KW)
admin_app = FastAPI(**_DOCS_KW)


class AgentService:
    """Process-wide service state: API keys, config catalog, live sessions."""

    def __init__(self, keys: Dict[str, dict], config_dir: Path, log_dir: Path):
        self.keys = keys                      # api_key -> {label, ...}
        self.config_dir = config_dir
        self.log_dir = log_dir
        # Contributor bundle uploads land here, namespaced per key-label. A SUBdir of
        # config_dir, so the top-level ``*.json`` catalog glob never picks them up as
        # maintained configs; list_configs/resolve_config include them explicitly.
        self.uploads_dir = config_dir / "_uploads"
        self.uploads_dir.mkdir(parents=True, exist_ok=True)
        # Hashed, mintable cohort/participant keys (additive to the static `keys` map above).
        self.key_store = key_store.default_store()
        self.sessions: Dict[str, Session] = {}  # session_id -> Session
        self.started_at = datetime.now(timezone.utc)

    def sessions_by_label(self) -> Dict[str, int]:
        """Live session count grouped by API-key label (for monitoring)."""
        counts: Dict[str, int] = {}
        for s in self.sessions.values():
            counts[s.api_key_label] = counts.get(s.api_key_label, 0) + 1
        return counts

    def resolve_key(self, presented: Optional[str]) -> Optional[dict]:
        """Normalize a presented key to {label, configs(set|None), caps(frozenset), role, source}.
        Checks the static keys.json map first (trusted maintainer/lab keys), then the hashed
        mintable store. Returns None if unknown/revoked/expired."""
        if not presented:
            return None
        meta = self.keys.get(presented)
        if meta is not None:
            cfgs = meta.get("configs")
            return {"label": meta.get("label"),
                    "configs": set(cfgs) if cfgs else None,
                    "caps": frozenset(meta.get("caps") or []),
                    "role": meta.get("role", "static"), "source": "static"}
        info = self.key_store.verify(presented)
        if info is not None:
            info = dict(info)
            info["configs"] = set(info["configs"]) if info["configs"] else None
            return info
        return None

    def key_known(self, presented: Optional[str]) -> bool:
        return self.resolve_key(presented) is not None

    def caps_for(self, presented: Optional[str]) -> frozenset:
        info = self.resolve_key(presented)
        return info["caps"] if info else frozenset()

    def label_for(self, api_key: str) -> Optional[str]:
        info = self.resolve_key(api_key)
        return info["label"] if info else None

    def allowed_configs_for(self, api_key: str) -> Optional[set]:
        """Configs this key may use. None = unrestricted (all configs)."""
        info = self.resolve_key(api_key)
        return info["configs"] if info else None

    def list_configs(self, include_uploads_for: Optional[str] = None,
                     include_names: Optional[List[str]] = None) -> List[dict]:
        """Return public-facing config descriptors derived from filesystem.

        Maintained configs (top-level config_dir) are shown to everyone. Uploaded configs
        are private to their owner: pass ``include_uploads_for=<key label>`` to also list
        that label's own uploads (files named ``<safe_label>__*.json``).

        ``include_names`` additionally lists uploads GRANTED to a key that live in someone
        ELSE's namespace — the study case: a collaborator uploads ``lab__cfg`` and mints
        participant keys scoped to it. Those participants have a different label, so the
        glob above never finds the file, and without this they could hello into a config the
        launcher never listed — access and discovery would disagree."""
        out: List[dict] = []
        paths = sorted(self.config_dir.glob("*.json"))
        if include_uploads_for:
            safe = re.sub(r"[^A-Za-z0-9_-]+", "_", include_uploads_for)
            paths = paths + sorted(self.uploads_dir.glob(f"{safe}__*.json"))
        if include_names:
            seen = set(paths)
            for name in include_names:
                # Sanitize: a granted name is only ever a bare stem in uploads_dir.
                stem = re.sub(r"[^A-Za-z0-9_.-]+", "_", str(name))
                p = (self.uploads_dir / f"{stem}.json").resolve()
                if p.parent != self.uploads_dir.resolve() or not p.exists() or p in seen:
                    continue
                paths.append(p)
                seen.add(p)
        for path in paths:
            if path.name.startswith("keys"):
                continue  # skip the keys file even if it lives in config_dir
            try:
                cfg = load_config(str(path))
            except Exception as e:
                print(f"[router] Skipping unloadable config {path.name}: {e}")
                continue
            agents = [
                {"name": a.subagent_name, "role": a.role, "actor_type": a.actor_type}
                for a in cfg.agents
            ]
            # Optional human-facing title for the client dropdown; falls back
            # to the filename stem. Read raw so configs need no schema change.
            title = path.stem
            try:
                with open(path) as f:
                    raw = json.load(f)
                title = raw.get("title") or raw.get("display_name") or path.stem
            except Exception:
                pass
            out.append({"name": path.stem, "title": title,
                        "path": path.name, "agents": agents,
                        "uploaded": path.parent == self.uploads_dir})
        return out

    def resolve_config(self, name: str) -> Optional[Path]:
        """Map a config name (without .json) to its path under config_dir or uploads_dir."""
        candidate = self.config_dir / f"{name}.json"
        if candidate.exists():
            return candidate
        # Uploaded configs (namespaced <label>__<slug>).
        up = self.uploads_dir / f"{name}.json"
        if up.exists() and up.parent.resolve() == self.uploads_dir.resolve():
            return up
        # Allow callers to pass an explicit relative or absolute path too.
        as_path = Path(name)
        if as_path.is_absolute() and as_path.exists():
            return as_path
        return None

    def store_upload(self, key_label: str, bundle_name: str, cfg: dict) -> str:
        """Persist a validated uploaded config under the uploader's namespace and return its
        public config name (``<safe_label>__<slug>``). Path-containment checked."""
        safe_label = re.sub(r"[^A-Za-z0-9_-]+", "_", key_label or "anon")
        slug = re.sub(r"[^A-Za-z0-9_.-]+", "_", bundle_name.split("/")[-1]) or "bundle"
        public = f"{safe_label}__{slug}"
        path = (self.uploads_dir / f"{public}.json").resolve()
        if path.parent != self.uploads_dir.resolve():
            raise ValueError("upload path escaped uploads_dir")
        path.write_text(json.dumps(cfg, indent=2, ensure_ascii=False) + "\n")
        return public

    def grant_config(self, api_key: str, name: str) -> None:
        """Add ``name`` to a key's config allowlist if it is restricted (unrestricted keys
        already see everything). Keeps an uploader able to select what they just uploaded."""
        meta = self.keys.get(api_key)
        if meta is None:
            return
        cfgs = meta.get("configs")
        if cfgs and name not in cfgs:
            cfgs.append(name)

    def user_dir(self, key_label: str) -> Path:
        """Per-user log directory: logs are grouped by API-key label so each
        user's games live together (logs/sessions/<label>/)."""
        safe_label = re.sub(r"[^A-Za-z0-9_-]+", "_", key_label or "anon")
        d = self.log_dir / safe_label
        d.mkdir(parents=True, exist_ok=True)
        return d

    def log_path_for(self, session_id: str, key_label: str) -> str:
        ts = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        return str(self.user_dir(key_label) / f"session_{ts}_{session_id[:8]}.jsonl")

    def record_session(self, key_label: str, key_fp: str, session_id: str,
                       config_name: str, log_file: str,
                       player_id: Optional[str] = None) -> None:
        """Append a one-line manifest entry to the user's session index so
        every game a user plays is catalogued (id, config, log file, time).
        player_id is the client's persistent localStorage UUID (may be None)."""
        entry = {
            "session_id": session_id,
            "label": key_label,
            "key_fingerprint": key_fp,
            "player_id": player_id,
            "config": config_name,
            "log_file": Path(log_file).name,
            "started_at": _now(),
        }
        index = self.user_dir(key_label) / "_sessions_index.jsonl"
        with open(index, "a") as f:
            f.write(json.dumps(entry) + "\n")


service: Optional[AgentService] = None


# JSON has no comment syntax, so `_comment` / `_note` keys are the near-universal convention
# for annotating a config file. In a KEY map that convention is dangerous: every top-level
# entry is a credential, so a comment silently becomes a working API key whose label is the
# comment text. Found live on the Talos deployment 2026-08-13 — `Authorization: Bearer _comment`
# returned 200 with config_scope "all" and upload rights, and `_comment` is guessable by anyone
# who has seen a JSON config. Underscore-prefixed entries are therefore ignored, never
# credentials. Also drops non-string/empty keys, which cannot be presented as a bearer token
# anyway but would otherwise sit in the map looking valid.
def _keys_from_mapping(data: dict) -> dict:
    """Normalize a raw key map, dropping annotation entries (see comment above)."""
    out = {}
    for k, v in (data or {}).items():
        if not isinstance(k, str) or not k.strip():
            continue
        if k.startswith("_"):
            print(f"[router] keys file: ignoring annotation entry {k!r} (not a credential)")
            continue
        out[k] = v if isinstance(v, dict) else {"label": str(v)}
    return out


def _load_keys(path: Optional[Path]) -> Dict[str, dict]:
    """Load API keys from a JSON file, env var, or fall back to a dev key.

    JSON file format::

        { "ck_abc...": {"label": "Conner"}, "ck_xyz...": {"label": "Erin"} }

    Env var ``ARC_API_KEYS`` accepts either a JSON object of the same shape,
    or a comma-separated list (each key gets a generic label).
    """
    if path is not None:
        with open(path, "r") as f:
            data = json.load(f)
        return _keys_from_mapping(data)

    env = os.environ.get("ARC_API_KEYS")
    if env:
        try:
            data = json.loads(env)
            if isinstance(data, dict):
                # Same sanitizer as the file path — an annotation entry in ARC_API_KEYS
                # would otherwise become a credential exactly as it did in keys.json.
                return _keys_from_mapping(data)
        except json.JSONDecodeError:
            pass
        out: Dict[str, dict] = {}
        for i, key in enumerate(s.strip() for s in env.split(",") if s.strip()):
            out[key] = {"label": f"user{i+1}"}
        return out

    # Dev fallback for local testing.
    dev_key = "dev-local-key"
    print(f"[router] No --keys-file or ARC_API_KEYS env; accepting dev key '{dev_key}'")
    # Local dev key is an admin: it can mint cohort keys and upload plugin code.
    # `play_tester` is included so the unrestricted dev key exercises the play-tester
    # controls locally; a real cohort key only gets it if minted with it.
    return {dev_key: {"label": "dev",
                      "caps": ["mint", "upload_code", "play_tester", "dev_panel"]}}


# Configs of the retired auto / choices / coach actors (deleted 2026-10) and what a client asking
# for one gets instead: the public demo config Talos serves.
RETIRED_CONFIGS = {"openai_multi_agent_config_local", "openai_multi_agent_config", "single_agent_config",
                   "claude_multi_agent_config", "openai_choices_only_local", "agents_config.example"}
DEFAULT_CONFIG = "continuous_all_officers_anthropic"


def _bearer_to_key(auth: Optional[str]) -> Optional[str]:
    """Pull the key out of an ``Authorization: Bearer <key>`` header."""
    if not auth:
        return None
    parts = auth.strip().split(None, 1)
    if len(parts) == 2 and parts[0].lower() == "bearer":
        return parts[1].strip()
    return None


def _tool_schema_name(s: dict) -> Optional[str]:
    return (s.get("function") or {}).get("name") or s.get("name")


def _plugin_tool_schemas_for(agent: AgentConfig, brief_only: bool,
                             existing: List[dict]) -> List[dict]:
    """Registered plugin tool schemas this agent may use: filtered by the per-agent allowlist
    (if any), the reactive acting-strip, and de-duped against built-ins already offered."""
    allow = set(agent.tools) if getattr(agent, "tools", None) else None
    have = {_tool_schema_name(t) for t in existing}
    out: List[dict] = []
    for name, spec in cora_ext.all_tools().items():
        if allow is not None and name not in allow:
            continue
        if brief_only and spec.acting:
            continue
        if name in have:
            continue
        out.append(spec.schema)
    return out


class _SessionToolContext(cora_ext.ToolContext):
    """Live ToolContext backed by a Session — the concrete `ctx` handed to plugin tools/hooks.

    Reads route to the session's filtered latest snapshot; the three store scopes and the
    session lock live on the Session. Acting (`emit_commands`/`propose_choices`) is wired in a
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

    async def emit_commands(self, tags: str) -> cora_ext.ToolResult:
        """Execute command tags through the SAME built-in path as the execute_commands tool
        (parse → validate → Unity), reusing all its ledger/scope logic. `_skip_registry` avoids
        re-entering the plugin registry (no recursion if a plugin overrides execute_commands)."""
        if self.agent is None:
            raise RuntimeError("emit_commands requires an officer context (not a session hook)")
        tc = {"name": "execute_commands", "arguments": {"commands": tags}}
        text, gs, all_a, filt, meta = await self._s._dispatch_continuous_tool(
            self.agent, tc, self._game_state, self.all_actions, self.filtered_actions,
            _skip_registry=True)
        self._game_state, self.all_actions, self.filtered_actions = gs, all_a, filt
        return cora_ext.ToolResult(text=text, executed=meta.get("executed", 0),
                                   finish=meta.get("finish", False))

    async def propose_choices(self, packages: list) -> cora_ext.ToolResult:
        """Send the director a choice set via the SAME path as the propose_choices tool."""
        if self.agent is None:
            raise RuntimeError("propose_choices requires an officer context")
        text, gs, all_a, filt, executed, superseded, _rows = await self._s._continuous_propose(
            self.agent, {"packages": packages}, self._game_state, self.all_actions,
            self.filtered_actions)
        self._game_state, self.all_actions, self.filtered_actions = gs, all_a, filt
        return cora_ext.ToolResult(text=text, executed=executed, finish=bool(superseded))

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


@app.get("/health")
async def health():
    if service is None:
        return {"status": "starting", "live_sessions": 0, "version": "2.0"}
    uptime = (datetime.now(timezone.utc) - service.started_at).total_seconds()
    return {
        "status": "healthy",
        "live_sessions": len(service.sessions),
        "sessions_by_label": service.sessions_by_label(),
        "uptime_seconds": round(uptime, 1),
        "configs_available": len(service.list_configs()),
        "version": "2.0",
    }


@app.get("/whoami")
async def whoami(authorization: Optional[str] = Header(default=None)):
    """What this key is and what it may do — the self-diagnosis endpoint.

    Without it a collaborator whose key is wrong, expired, or missing a capability only finds
    out as a 401/403 partway through some other call, and cannot tell "bad key" from "valid key,
    wrong capability". `cora.py doctor` reads this to answer both in one shot. Returns no secret:
    the key itself is never echoed, only its label, capabilities and config scope.
    """
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    info = service.resolve_key(_bearer_to_key(authorization))
    if info is None:
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    scoped = info.get("configs")
    return {
        "label": info.get("label"),
        "role": info.get("role"),
        "source": info.get("source"),          # static (keys.json) vs minted (hashed store)
        "capabilities": sorted(info.get("caps") or []),
        "config_scope": sorted(scoped) if scoped else "all",
        # Mirrors the ACTUAL gates: POST /bundles accepts any valid key (key_known), while
        # POST /plugins and the admin mint route are capability-gated. Reported rather than
        # inferred so `doctor` never tells a collaborator they can do something they can't.
        "can_upload_configs": True,
        "can_upload_code": "upload_code" in (info.get("caps") or ()),
        "can_mint_keys": "mint" in (info.get("caps") or ()),
        "can_use_dev_panel": "dev_panel" in (info.get("caps") or ()),
    }


@app.get("/configs")
async def list_configs(authorization: Optional[str] = Header(default=None)):
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    key = _bearer_to_key(authorization)
    if key is None or not service.key_known(key):
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    allowed = service.allowed_configs_for(key)
    # Pass `allowed` through so a config GRANTED to this key is listed even when it was
    # uploaded under another label (collaborator uploads → participant keys scoped to it).
    configs = service.list_configs(include_uploads_for=service.label_for(key),
                                   include_names=allowed)
    if allowed is not None:
        configs = [c for c in configs if c["name"] in allowed]
    return {"configs": configs}


# Cap uploaded bundle size (a config bundle is small JSON; this blocks JSON-bomb DoS).
_MAX_BUNDLE_BYTES = 256 * 1024


@app.post("/bundles")
async def upload_bundle(request: Request,
                        authorization: Optional[str] = Header(default=None),
                        base: Optional[str] = None):
    """Upload a contributor config bundle. Auth via ``Authorization: Bearer <key>``.

    Security posture (see docs/contributor-platform-design.md): namespace is derived from the
    TOKEN's label (never the body); body size is capped; the bundle is validated by cora_schema
    (``extra='forbid'`` + provider-enum, so no endpoint/secret can be smuggled in); it is stored
    in the uploader's private namespace and granted only to the uploading key. No code executes.

    For a *delta* bundle, pass ``?base=<config name>`` naming a config to layer onto.
    """
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    key = _bearer_to_key(authorization)
    if key is None or not service.key_known(key):
        raise HTTPException(status_code=401, detail="Invalid or missing API key")

    raw = await request.body()
    if len(raw) > _MAX_BUNDLE_BYTES:
        raise HTTPException(status_code=413, detail="bundle too large")
    try:
        payload = json.loads(raw)
    except (json.JSONDecodeError, UnicodeDecodeError):
        raise HTTPException(status_code=400, detail="body is not valid JSON")

    base_config = None
    if base is not None:
        bp = service.resolve_config(base)
        if bp is None:
            raise HTTPException(status_code=400, detail=f"unknown base config '{base}'")
        base_config = str(bp)

    try:
        cfg = load_bundle(payload, base_config=base_config)
    except BundleError as e:
        raise HTTPException(status_code=422, detail=str(e))

    # The Pydantic gate and the RUNTIME invariants check different things, and a config can
    # pass the first while being unusable under the second — two officers sharing a
    # talkinghead slot is the canonical case: CoraConfig accepts it, RouterConfig raises. That
    # combination meant such a bundle uploaded 200 and then broke the session of whoever
    # selected it. Enforce both here so an unusable config is refused at upload, not at play.
    try:
        agent_config.config_from_dict(cfg)
    except Exception as e:
        raise HTTPException(status_code=422,
                            detail=f"config is schema-valid but not runnable: {e}")

    try:
        manifest_name = payload["manifest"]["name"]
    except (KeyError, TypeError):
        raise HTTPException(status_code=422, detail="bundle missing manifest.name")

    label = service.label_for(key) or "anon"
    name = service.store_upload(label, manifest_name, cfg)
    service.grant_config(key, name)
    warnings = _bundle_warnings(cfg)
    if warnings:
        print(f"[router] upload '{name}' stored with {len(warnings)} warning(s): {warnings}")
    return {"status": "ok", "name": name, "warnings": warnings,
            "message": f"stored as config '{name}'; select it in the hello frame to play"}


def _bundle_warnings(cfg: dict) -> List[str]:
    """Authoring warnings for an uploaded bundle — delegates to the shared implementation in
    bundle.py so the CLI (`cora-bundle validate`) and this endpoint report the SAME problems.
    Previously this logic lived only here, so validating locally gave a clean "OK" for a config
    that could not work in the UI."""
    return bundle_mod.config_warnings(cfg)


def _require_cap(authorization: Optional[str], cap: str) -> dict:
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    info = service.resolve_key(_bearer_to_key(authorization))
    if info is None:
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    if cap not in info["caps"]:
        raise HTTPException(status_code=403, detail=f"requires the '{cap}' capability")
    return info


@admin_app.post("/admin/keys")
async def mint_keys(request: Request, authorization: Optional[str] = Header(default=None)):
    """Mint scoped cohort/participant keys (requires the 'mint' capability). Raw keys are returned
    ONCE — only the prefix + SHA-256 hash persist."""
    admin = _require_cap(authorization, "mint")
    body = await request.json()
    count = int(body.get("count") or 1)
    if not (1 <= count <= 500):
        raise HTTPException(status_code=400, detail="count must be 1..500")
    role = body.get("role") or "cohort"
    if role not in ("cohort", "admin"):
        raise HTTPException(status_code=400, detail="role must be 'cohort' or 'admin'")
    tokens = [service.key_store.mint(
        role=role, cohort=body.get("cohort"), configs=body.get("configs"),
        caps=body.get("caps") or [], quota=body.get("quota"),
        expires_days=body.get("expires_days"),
        created_by=admin.get("label") or "admin") for _ in range(count)]
    return {"cohort": body.get("cohort"), "count": len(tokens), "keys": tokens,
            "note": "store these now — they are not retrievable later"}


@admin_app.get("/admin/keys")
async def list_keys_admin(cohort: Optional[str] = None,
                          authorization: Optional[str] = Header(default=None)):
    """List keys + usage for auditing (requires 'mint'). Never returns secrets, only prefixes."""
    _require_cap(authorization, "mint")
    return {"keys": service.key_store.list_keys(cohort),
            "usage": service.key_store.usage_summary(cohort)}


@admin_app.post("/admin/keys/revoke")
async def revoke_key_admin(request: Request, authorization: Optional[str] = Header(default=None)):
    """Immediately revoke a key by prefix (requires 'mint')."""
    _require_cap(authorization, "mint")
    body = await request.json()
    prefix = str(body.get("prefix") or "")
    if not service.key_store.revoke(prefix):
        raise HTTPException(status_code=404, detail="unknown or already-revoked prefix")
    return {"status": "revoked", "prefix": prefix}


_MAX_PLUGIN_BYTES = 128 * 1024
# Staged uploads land here and are NOT imported until an admin activates them via
# /admin/plugins/reload. Kept beside plugins/ (not inside it) so a stage can never be
# picked up by the startup load.
_PLUGINS_STAGED_DIR = Path(__file__).parent / "plugins_staged"
# Constructs worth a human's eyes before activation. This is NOT a sandbox and does not
# make untrusted code safe — once activated, a plugin runs in-process with full router
# privileges. The real controls are the upload_code capability + the manual activation
# gate; this scan just tells the reviewer where to look.
_PLUGIN_AUDIT_IMPORTS = {"subprocess", "socket", "shutil", "ctypes", "pickle", "marshal",
                         "importlib", "pty", "multiprocessing", "urllib", "requests", "httpx"}
_PLUGIN_AUDIT_CALLS = {"eval", "exec", "compile", "__import__", "open"}


@app.post("/plugins")
async def upload_plugin(request: Request,
                        name: Optional[str] = None,
                        authorization: Optional[str] = Header(default=None)):
    """Stage a contributor plugin (a cora_ext tool/hook module) for review. Body is the raw
    UTF-8 Python source; name it with ``?name=<slug>``. Requires the 'upload_code' capability.

    SECURITY POSTURE (deliberate, staged-manual): the module is validated and written to
    plugins_staged/<label>__<slug>.py but is **NOT imported and NOT activated**. Uploaded
    Python executes with full router privileges once loaded, so activation is a separate,
    explicit admin action (POST /admin/plugins/reload). The AST findings returned here are
    advisory input to that human review — not a sandbox.
    """
    import ast
    info = _require_cap(authorization, "upload_code")
    raw = await request.body()
    if not raw:
        raise HTTPException(status_code=400, detail="empty body: POST the plugin source as the body")
    if len(raw) > _MAX_PLUGIN_BYTES:
        raise HTTPException(status_code=413, detail=f"plugin too large (max {_MAX_PLUGIN_BYTES} bytes)")
    try:
        source = raw.decode("utf-8")
    except UnicodeDecodeError:
        raise HTTPException(status_code=400, detail="plugin must be UTF-8 text")

    # Namespace by the TOKEN's label (never the body/query), same rule as bundle uploads.
    label = info.get("label") or "anon"
    safe_label = re.sub(r"[^A-Za-z0-9_-]+", "_", label) or "anon"
    slug = re.sub(r"[^A-Za-z0-9_]+", "_", (name or "plugin").strip()).strip("_") or "plugin"
    stem = f"{safe_label}__{slug}"

    # 1) Must parse. A syntax error is a hard reject — it could never import anyway.
    try:
        tree = ast.parse(source, filename=f"{stem}.py")
    except SyntaxError as e:
        raise HTTPException(status_code=422, detail=f"syntax error line {e.lineno}: {e.msg}")

    # 2) Advisory scan: risky constructs + whether it registers anything at all.
    findings: list = []
    registers = False
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for a in node.names:
                root = a.name.split(".")[0]
                if root in _PLUGIN_AUDIT_IMPORTS:
                    findings.append(f"line {node.lineno}: imports '{a.name}'")
        elif isinstance(node, ast.ImportFrom):
            root = (node.module or "").split(".")[0]
            if root in _PLUGIN_AUDIT_IMPORTS:
                findings.append(f"line {node.lineno}: from '{node.module}' import ...")
        elif isinstance(node, ast.Call):
            fn = node.func
            fname = getattr(fn, "id", None) or getattr(fn, "attr", None)
            if fname in _PLUGIN_AUDIT_CALLS:
                findings.append(f"line {node.lineno}: calls {fname}()")
            if fname in ("register_tool", "register_hook", "register_loop"):
                registers = True
    if not registers:
        findings.append("no register_tool/register_hook call found — this may not be a CORA plugin")

    _PLUGINS_STAGED_DIR.mkdir(parents=True, exist_ok=True)
    path = (_PLUGINS_STAGED_DIR / f"{stem}.py").resolve()
    if path.parent != _PLUGINS_STAGED_DIR.resolve():
        raise HTTPException(status_code=400, detail="staged path escaped plugins_staged/")
    path.write_text(source)
    print(f"[router] plugin STAGED (not active): {path.name} by '{label}' "
          f"({len(raw)} bytes, {len(findings)} review note(s))")
    return {
        "status": "staged",
        "name": path.name,
        "bytes": len(raw),
        "active": False,
        "review_notes": findings,
        "message": ("Staged for review — NOT active. An admin must activate it with "
                    "POST /admin/plugins/reload (this imports and runs the module)."),
    }


@app.get("/plugins")
async def list_plugins(authorization: Optional[str] = Header(default=None)):
    """List staged (inactive) and active plugin modules. Requires 'upload_code'."""
    _require_cap(authorization, "upload_code")
    staged = sorted(p.name for p in _PLUGINS_STAGED_DIR.glob("*.py")) \
        if _PLUGINS_STAGED_DIR.exists() else []
    return {"staged_inactive": staged,
            "active_tools": list(cora_ext.all_tools()),
            "active_hooks": {e: len(cora_ext.get_hooks(e)) for e in cora_ext.HOOK_EVENTS
                             if cora_ext.get_hooks(e)},
            "load_errors": cora_ext.load_errors()}


@admin_app.post("/admin/plugins/reload")
async def reload_plugins(authorization: Optional[str] = Header(default=None)):
    """Hot-reload plugins WITHOUT a router restart: clear the plugin registry and re-import every
    module under plugins/ AND plugins_staged/ (edits + new files picked up; deleted files dropped).
    Requires the 'upload_code' capability. This is the ACTIVATION step for anything uploaded via
    POST /plugins — it imports and executes that code, so review the staged file first. Prefer to
    run between games — a tool/hook call arriving during the brief reload window degrades to a tool
    error (exception isolation), never a crash. A plugin with a syntax/import error is skipped and
    reported, not fatal."""
    _require_cap(authorization, "upload_code")
    cora_ext.clear_registry()
    loaded = cora_ext.load_plugins(["plugins", str(_PLUGINS_STAGED_DIR)])
    return {"reloaded_modules": loaded,
            "load_errors": cora_ext.load_errors(),          # files that failed to import
            "tools": list(cora_ext.all_tools()),
            "hooks": {e: len(cora_ext.get_hooks(e)) for e in cora_ext.HOOK_EVENTS
                      if cora_ext.get_hooks(e)}}


@admin_app.get("/admin/plugins/errors")
async def plugin_errors(limit: int = 50, authorization: Optional[str] = Header(default=None)):
    """Recent plugin diagnostics (requires 'upload_code'): load-time import failures + a ring
    buffer of the most recent tool/hook runtime exceptions, each with a full traceback."""
    _require_cap(authorization, "upload_code")
    return {"load_errors": cora_ext.load_errors(),
            "runtime_errors": cora_ext.recent_errors(limit)}


@app.get("/my/sessions")
async def my_sessions(authorization: Optional[str] = Header(default=None)):
    """List the caller's OWN sessions (from their per-label session index). The label — and thus
    the namespace — is derived from the token, never from a parameter, so one key can only ever
    see its own cohort's data."""
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    key = _bearer_to_key(authorization)
    if key is None or not service.key_known(key):
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    label = service.label_for(key) or "anon"
    index = service.user_dir(label) / "_sessions_index.jsonl"
    sessions = []
    if index.exists():
        for line in index.read_text().splitlines():
            line = line.strip()
            if line:
                try:
                    sessions.append(json.loads(line))
                except json.JSONDecodeError:
                    pass
    return {"label": label, "count": len(sessions), "sessions": sessions}


@app.get("/my/sessions/export")
async def my_sessions_export(format: str = "ndjson", config: Optional[str] = None,
                             limit: int = 0,
                             authorization: Optional[str] = Header(default=None)):
    """Bulk-download ALL of the caller's own session logs in one request — the corpus step for
    fine-tuning. Scoped to the token's label exactly like /my/sessions, so a key can only ever
    export its own cohort's data.

    NOTE: this route MUST stay declared above /my/sessions/{session_id}; otherwise FastAPI
    matches "export" as a session_id and this endpoint becomes unreachable.

    format=ndjson (default): every event of every session concatenated, newline-delimited. Each
      line already carries session_id/episode_id, so the stream stays attributable after merging.
    format=tar: a .tar.gz of the individual session .jsonl files (one member per session).
    config=<name>: only sessions played on that config. limit=N: most recent N sessions.
    """
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    key = _bearer_to_key(authorization)
    if key is None or not service.key_known(key):
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    if format not in ("ndjson", "tar"):
        raise HTTPException(status_code=400, detail="format must be 'ndjson' or 'tar'")

    label = service.label_for(key) or "anon"
    udir = service.user_dir(label).resolve()
    index = udir / "_sessions_index.jsonl"
    entries: list = []
    if index.exists():
        for line in index.read_text().splitlines():
            line = line.strip()
            if not line:
                continue
            try:
                e = json.loads(line)
            except json.JSONDecodeError:
                continue
            if config and e.get("config") != config:
                continue
            entries.append(e)
    if limit and limit > 0:
        entries = entries[-limit:]

    # Resolve to real, containment-checked files (skip index rows whose log is gone).
    files: list = []
    for e in entries:
        name = e.get("log_file")
        if not name:
            continue
        p = (udir / name).resolve()
        if p.parent == udir and p.exists():
            files.append(p)

    stamp = _now().replace(":", "").replace("-", "")[:15]
    if format == "tar":
        import io, tarfile
        buf = io.BytesIO()
        with tarfile.open(fileobj=buf, mode="w:gz") as tf:
            for p in files:
                tf.add(str(p), arcname=f"{label}/{p.name}")
        data = buf.getvalue()
        fn = f"cora_sessions_{label}_{stamp}.tar.gz"
        print(f"[router] export: {len(files)} session(s) for '{label}' as tar ({len(data)} bytes)")
        return Response(content=data, media_type="application/gzip",
                        headers={"Content-Disposition": f'attachment; filename="{fn}"',
                                 "X-CORA-Sessions": str(len(files))})

    chunks = []
    for p in files:
        text = p.read_text()
        if text and not text.endswith("\n"):
            text += "\n"
        chunks.append(text)
    body = "".join(chunks)
    fn = f"cora_sessions_{label}_{stamp}.jsonl"
    print(f"[router] export: {len(files)} session(s) for '{label}' as ndjson ({len(body)} bytes)")
    return Response(content=body, media_type="application/x-ndjson",
                    headers={"Content-Disposition": f'attachment; filename="{fn}"',
                             "X-CORA-Sessions": str(len(files))})


@app.get("/my/sessions/{session_id}")
async def my_session_log(session_id: str, authorization: Optional[str] = Header(default=None)):
    """Download one of the caller's own session logs (NDJSON event stream). Scoped to the caller's
    label directory with a path-containment check."""
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    key = _bearer_to_key(authorization)
    if key is None or not service.key_known(key):
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    label = service.label_for(key) or "anon"
    udir = service.user_dir(label).resolve()
    index = udir / "_sessions_index.jsonl"
    log_name = None
    if index.exists():
        for line in index.read_text().splitlines():
            try:
                e = json.loads(line)
            except json.JSONDecodeError:
                continue
            if e.get("session_id") == session_id:
                log_name = e.get("log_file")
                break
    if not log_name:
        raise HTTPException(status_code=404, detail="session not found for your key")
    path = (udir / log_name).resolve()
    if path.parent != udir or not path.exists():
        raise HTTPException(status_code=404, detail="log file not found")
    return Response(content=path.read_text(), media_type="application/x-ndjson",
                    headers={"Content-Disposition": f'attachment; filename="{log_name}"'})


async def _handshake(websocket: WebSocket) -> Optional[Session]:
    """Accept a WebSocket, perform the hello handshake, return a Session.

    On any handshake failure the WebSocket is closed and ``None`` is returned.
    """
    await websocket.accept()
    if service is None:
        await websocket.send_text(json.dumps({"type": "hello_error", "error": "service_not_ready"}))
        await websocket.close(code=1011, reason="Service not initialized")
        return None

    try:
        raw = await asyncio.wait_for(websocket.receive_text(), timeout=15.0)
    except asyncio.TimeoutError:
        await websocket.close(code=1008, reason="hello timeout")
        return None

    try:
        msg = json.loads(raw)
    except json.JSONDecodeError:
        await websocket.send_text(json.dumps({"type": "hello_error", "error": "bad_json"}))
        await websocket.close(code=1008, reason="hello must be JSON")
        return None

    if msg.get("type") != "hello":
        await websocket.send_text(json.dumps({
            "type": "hello_error",
            "error": "expected_hello",
            "got": msg.get("type"),
        }))
        await websocket.close(code=1008, reason="expected hello frame")
        return None

    api_key = msg.get("api_key")
    config_name = msg.get("config")
    if config_name in RETIRED_CONFIGS:
        # A client built before the legacy actors were retired (its scene default, or a saved
        # arc_config_name) still asks for one of their configs.
        print(f"[router] config {config_name!r} is retired; serving {DEFAULT_CONFIG!r}")
        config_name = DEFAULT_CONFIG
    # Optional client-supplied persistent player id (localStorage UUID). It is
    # UNTRUSTED input: sanitize to a bounded safe charset and only ever store it
    # as a log VALUE, never as a path component. Absent/blank -> None (anonymous).
    raw_pid = msg.get("player_id")
    player_id = None
    if isinstance(raw_pid, str):
        player_id = re.sub(r"[^A-Za-z0-9_-]", "", raw_pid)[:64] or None
    # Map provenance the client reports (see the session_start emit below). UNTRUSTED like
    # player_id: bounded and only ever stored as a log VALUE, never used as a path or to
    # fetch anything. "" when an older client doesn't send it.
    def _clip(v, n=300):
        return v[:n] if isinstance(v, str) else ""
    map_url = _clip(msg.get("map_url"))
    map_hash = _clip(msg.get("map_hash"), 32)
    map_status = _clip(msg.get("map_status"), 24)
    if not api_key or not service.key_known(api_key):
        await websocket.send_text(json.dumps({"type": "hello_error", "error": "invalid_api_key"}))
        await websocket.close(code=1008, reason="invalid api key")
        return None
    if not config_name:
        await websocket.send_text(json.dumps({"type": "hello_error", "error": "missing_config"}))
        await websocket.close(code=1008, reason="missing config")
        return None

    allowed = service.allowed_configs_for(api_key)
    if allowed is not None and config_name not in allowed:
        await websocket.send_text(json.dumps({
            "type": "hello_error",
            "error": "config_not_allowed",
            "config": config_name,
        }))
        await websocket.close(code=1008, reason="config not allowed for this key")
        return None

    config_path = service.resolve_config(config_name)
    if config_path is None:
        await websocket.send_text(json.dumps({
            "type": "hello_error",
            "error": "unknown_config",
            "config": config_name,
        }))
        await websocket.close(code=1008, reason="unknown config")
        return None

    try:
        cfg = load_config(str(config_path))
    except Exception as e:
        await websocket.send_text(json.dumps({
            "type": "hello_error",
            "error": "config_load_failed",
            "detail": str(e),
        }))
        await websocket.close(code=1011, reason="config load failed")
        return None

    session_id = str(uuid.uuid4())
    key_label = service.label_for(api_key) or "anon"
    log_path = service.log_path_for(session_id, key_label)
    session = Session(
        config=cfg,
        session_id=session_id,
        api_key_label=key_label,
        log_path=log_path,
        websocket=websocket,
    )
    session.player_id = player_id
    session.config_name = config_name      # for the developer panel's session list
    session.started_at = _now()
    service.sessions[session_id] = session

    # Catalogue this game under the user (per-key index) and stamp a
    # session_start header at the top of the session's own log.
    key_fp = hashlib.sha256(api_key.encode("utf-8")).hexdigest()[:12]
    service.record_session(key_label, key_fp, session_id, config_name, log_path,
                           player_id=player_id)
    session._emit("session_start", {
        "label": key_label,
        "key_fingerprint": key_fp,
        "player_id": player_id,
        "config": config_name,
        "agents": [a.subagent_name for a in cfg.agents],
        # Per-agent specs: which model and messaging rules each seat ran under. The name
        # list above is kept as-is for existing readers; this is the self-describing form.
        "agent_specs": [{
            "name": a.subagent_name,
            "role": a.role,
            "actor_type": a.actor_type,
            "provider": getattr(a, "provider", None),
            "llm_model": getattr(a, "llm_model", None),
            "opening_mode": getattr(a, "opening_mode", None),
            "can_address": list(getattr(a, "can_address", None) or []),
            "max_steps": getattr(a, "max_steps", None),
        } for a in cfg.agents],
        "peer_trigger_budget": int(getattr(cfg, "peer_trigger_budget", None)
                                   or PEER_TRIGGER_BUDGET_PER_ROUND),
        # Map PROVENANCE, reported by the client. Maps are deliberately served outside the
        # router (a partner can expose a map derived from private data), so this is the only
        # record of which map a session actually ran on. Stamping it here keeps a merged
        # corpus self-describing — two map conditions stay separable at training time, and a
        # silent fallback to the default layout (map_status != "loaded") is visible after the
        # fact. The router RECORDS these; it never serves or validates map content.
        "map": {"url": map_url, "hash": map_hash, "status": map_status},
        # The weights that turn Unity's rewardMetrics into `score`. Every front end (live, RL,
        # benchmark) uses cora.scoring, so scores ARE directly
        # comparable — but only under the SAME weights. Without this stamp, retuning a weight
        # silently makes old and new runs incomparable: the numbers still merge and parse,
        # they just quietly mean something different. Raw rewardMetrics are preserved per
        # turn regardless, so a corpus can always be re-scored under new weights.
        "reward_weights": REWARD_WEIGHTS,
    })

    await websocket.send_text(json.dumps({
        "type": "hello_ack",
        "session_id": session_id,
        "config": config_name,
        "agents": [a.subagent_name for a in cfg.agents],
        # Roster the client uses to label the (fixed 5) sidebar talking-head slots by
        # the config's real officer names, keyed by talkinghead_endpoint. Without this
        # the WebGL client has no local config and falls back to the enum slot names,
        # so e.g. a "Logistics Officer" (endpoint=WorkforceService) renders under the
        # "Workforce Service" tab and looks like it never landed.
        "officers": [{"name": a.subagent_name, "endpoint": a.talkinghead_endpoint}
                     for a in cfg.agents if a.talkinghead_endpoint],
        "label": key_label,
        # Capabilities of the presented key, so the CLIENT can gate its own controls.
        # This is a convenience for the UI, NOT a security boundary: anything that must
        # actually be enforced is enforced server-side on the request that does the work.
        # Used today by `play_tester`, which reveals Load .cora and Flag Interaction —
        # both are local-only actions (a file picker, a log line), so a client that lies
        # about its caps gains nothing it could not already do by editing its own save.
        "capabilities": sorted(service.caps_for(api_key)),
        "player_id": player_id,
    }))
    print(f"[router] hello_ack -> {key_label} (session {session_id[:8]}, "
          f"config={config_name}, agents={len(cfg.agents)})")
    return session


# ── Developer panel ──────────────────────────────────────────────
# A plain web page (devpanel/index.html) plus a small JSON API for editing officers' prompts
# in a LIVE game. Every API call needs a key with the `dev_panel` capability; study
# participants' keys never have it. The page itself is static and holds no data.
_DEVPANEL_DIR = Path(__file__).parent / "devpanel"


def _require_dev_panel(authorization: Optional[str]) -> dict:
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    info = service.resolve_key(_bearer_to_key(authorization))
    if info is None:
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    if "dev_panel" not in (info.get("caps") or ()):
        raise HTTPException(status_code=403,
                            detail="This key lacks the 'dev_panel' capability")
    return info


def _dev_session(session_id: str) -> "Session":
    s = service.sessions.get(session_id)
    if s is None:
        raise HTTPException(status_code=404, detail="No live session with that id")
    return s


@app.get("/dev")
async def dev_panel_page():
    page = _DEVPANEL_DIR / "index.html"
    if not page.is_file():
        raise HTTPException(status_code=404, detail="devpanel/index.html not found")
    return Response(content=page.read_text(encoding="utf-8"), media_type="text/html")


@app.get("/dev/api/sessions")
async def dev_list_sessions(authorization: Optional[str] = Header(default=None)):
    """Live games on this router, newest first."""
    _require_dev_panel(authorization)
    rows = [{
        "session_id": s.session_id,
        "config": getattr(s, "config_name", None),
        "key_label": s.api_key_label,
        "started_at": getattr(s, "started_at", None),
        "round": s.round_num, "day": s.day,
        "officers": [a.subagent_name for a in s.config.agents
                     if a.actor_type == "continuous"],
    } for s in service.sessions.values()]
    rows.sort(key=lambda r: r["started_at"] or "", reverse=True)
    return {"sessions": rows}


@app.get("/dev/api/sessions/{session_id}")
async def dev_session_view(session_id: str,
                           authorization: Optional[str] = Header(default=None)):
    """Prompt layers, each officer's assembled prompt, and live officer status."""
    _require_dev_panel(authorization)
    return _dev_session(session_id).dev_prompt_view()


@app.post("/dev/api/sessions/{session_id}/prompt")
async def dev_set_prompt(session_id: str, request: Request,
                         authorization: Optional[str] = Header(default=None)):
    """Body: {"scope": "agent"|"global_behavior"|"global_manual"|"tool_policy",
              "agent": "<officer name, for scope=agent>", "text": "<new prompt>"}.
    Omit "text" (or send null) to reset that layer to the config's text."""
    info = _require_dev_panel(authorization)
    try:
        body = await request.json()
    except Exception:
        raise HTTPException(status_code=400, detail="Body must be JSON")
    text = body.get("text")
    if text is not None and not isinstance(text, str):
        raise HTTPException(status_code=400, detail="'text' must be a string or null")
    if text is not None and len(text) > 200_000:
        raise HTTPException(status_code=413, detail="Prompt text over 200,000 characters")
    try:
        change = _dev_session(session_id).dev_set_prompt(
            body.get("scope"), text, editor=info.get("label") or "unknown",
            agent_name=body.get("agent"))
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    return {"ok": True, "change": change}


@app.delete("/dev/api/sessions/{session_id}/autonomy/{rule_id}")
async def dev_remove_autonomy_rule(session_id: str, rule_id: str,
                                   authorization: Optional[str] = Header(default=None)):
    """Cancel a standing order from the developer panel. The officer's chat shows it."""
    _require_dev_panel(authorization)
    text = await _dev_session(session_id)._remove_autonomy_rule(None, rule_id, by="developer")
    if text.startswith("ERROR"):
        raise HTTPException(status_code=404, detail=text)
    return {"ok": True, "detail": text}


_MAX_CHECKPOINT_BYTES = 8 * 1024 * 1024


@app.post("/dev/api/sessions/{session_id}/load_checkpoint")
async def dev_load_checkpoint(session_id: str, request: Request,
                              authorization: Optional[str] = Header(default=None)):
    """Body: {"checkpoint": <the .cora JSON, as an object or a string>,
              "officer_memory": "fresh" (default) | "keep"}.
    Loads the checkpoint into the live game; "fresh" also clears the officers' transcripts."""
    info = _require_dev_panel(authorization)
    raw = await request.body()
    if len(raw) > _MAX_CHECKPOINT_BYTES:
        raise HTTPException(status_code=413, detail="Checkpoint over 8 MB")
    try:
        body = json.loads(raw)
    except ValueError:
        raise HTTPException(status_code=400, detail="Body must be JSON")
    cp = body.get("checkpoint")
    if isinstance(cp, dict):
        cp = json.dumps(cp)
    if not isinstance(cp, str) or not cp.strip():
        raise HTTPException(status_code=400, detail="'checkpoint' is required")
    session = _dev_session(session_id)
    try:
        result = await session.dev_load_checkpoint(
            cp, body.get("officer_memory") or "fresh", editor=info.get("label") or "unknown")
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    return {"ok": result["accepted"], "load": result}


@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    session = await _handshake(websocket)
    if session is None:
        return
    try:
        await session.run()
    finally:
        service.sessions.pop(session.session_id, None)


@app.websocket("/")
async def websocket_root_endpoint(websocket: WebSocket):
    """Alias path for clients that connect at the root."""
    session = await _handshake(websocket)
    if session is None:
        return
    try:
        await session.run()
    finally:
        service.sessions.pop(session.session_id, None)


def main():
    global service

    parser = argparse.ArgumentParser(description="ARC Game Multi-Agent Router (multi-tenant)")
    parser.add_argument("--config-dir", default="config",
                        help="Directory containing config JSON files to expose to clients")
    parser.add_argument("--keys-file", default=None,
                        help="JSON file mapping api_key -> {label}. "
                             "If omitted, reads ARC_API_KEYS env var; if that's also "
                             "absent, falls back to a single 'dev-local-key' for testing.")
    parser.add_argument("--log-dir", default="logs/sessions",
                        help="Directory for per-session episode log files")
    parser.add_argument("--port", type=int, default=9876,
                        help="Port to listen on for Unity connections")
    parser.add_argument("--admin-port", type=int, default=9877,
                        help="Port for the ADMIN (control-plane) app: key minting, plugin "
                             "activation, diagnostics. Bound to loopback by default — reach "
                             "it with: ssh -L 9877:127.0.0.1:9877 <host>")
    parser.add_argument("--admin-host", default="127.0.0.1",
                        help="Bind address for the admin app. Keep 127.0.0.1 in production; "
                             "binding it to 0.0.0.0 puts key minting and code activation on "
                             "the network and is almost never what you want.")
    parser.add_argument("--cors-origins", default="*",
                        help="Comma-separated origins allowed for browser (WebGL) "
                             "clients, or '*' for any. Only needed when the WebGL "
                             "page is served from a different origin than this "
                             "router; harmless behind a same-origin reverse proxy.")
    # Legacy single-config flag is no longer used; configs are chosen per-session
    # via the hello frame. Kept here only so old launch scripts don't fail hard.
    parser.add_argument("--config", default=None,
                        help=argparse.SUPPRESS)
    parser.add_argument("--log", default=None,
                        help=argparse.SUPPRESS)
    args = parser.parse_args()

    if args.config is not None:
        print(f"[router] NOTE: --config is ignored in multi-tenant mode "
              f"(clients pick a config via the hello frame).")
    if args.log is not None:
        print(f"[router] NOTE: --log is ignored; logs go to --log-dir as one file per session.")

    keys = _load_keys(Path(args.keys_file) if args.keys_file else None)
    service = AgentService(
        keys=keys,
        config_dir=Path(args.config_dir),
        log_dir=Path(args.log_dir),
    )

    print(f"[router] Starting service on port {args.port}")
    print(f"[router] Config catalog: {service.config_dir} "
          f"({len(service.list_configs())} configs visible)")
    _store = plugin_store.default_store()
    key_store.default_store()
    print(f"[router] Plugin persist store: {plugin_store._DEFAULT_PATH} | "
          f"key store: {key_store._DEFAULT_PATH}")
    _loaded_plugins = cora_ext.load_plugins(["plugins"])
    if _loaded_plugins:
        print(f"[router] Loaded {len(_loaded_plugins)} plugin module(s): {_loaded_plugins} "
              f"| tools={list(cora_ext.all_tools())}")
    print(f"[router] Authorized keys: {[m.get('label') for m in keys.values()]}")
    print(f"[router] Session logs: {service.log_dir}")
    print(f"[router] Clients connect to ws://localhost:{args.port}/ws "
          f"and send a hello frame.")

    # CORS lets a browser-based (WebGL) client call /configs from another
    # origin. WebSockets aren't subject to CORS, so this mainly covers the
    # /configs + /health fetches. Auth is via Bearer header (not cookies),
    # so wildcard origins without credentials is safe.
    origins = (["*"] if args.cors_origins.strip() == "*"
               else [o.strip() for o in args.cors_origins.split(",") if o.strip()])
    app.add_middleware(
        CORSMiddleware,
        allow_origins=origins,
        allow_methods=["*"],
        allow_headers=["*"],
    )
    print(f"[router] CORS allow_origins = {origins}")

    # Serve BOTH planes: the public app on all interfaces (behind the proxy) and the admin
    # app on loopback only. Binding admin to 127.0.0.1 is the actual control — it cannot be
    # reached from off-box regardless of what the reverse proxy is configured to forward.
    async def _serve_both():
        public = uvicorn.Server(uvicorn.Config(
            app, host="0.0.0.0", port=args.port, log_level="info"))
        admin = uvicorn.Server(uvicorn.Config(
            admin_app, host=args.admin_host, port=args.admin_port, log_level="warning"))
        print(f"[router] data plane  : http://0.0.0.0:{args.port} (public, proxied)")
        print(f"[router] control plane: http://{args.admin_host}:{args.admin_port} "
              f"(admin — loopback only; reach via: ssh -L {args.admin_port}:127.0.0.1:{args.admin_port} <host>)")
        print(f"[router] API docs: {'ENABLED (CORA_DEV_DOCS)' if _DEV_DOCS else 'disabled'}")
        await asyncio.gather(public.serve(), admin.serve())

    asyncio.run(_serve_both())


if __name__ == "__main__":
    main()
