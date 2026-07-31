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
import hashlib
import os
import re
import sys
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Tuple, Optional

from fastapi import FastAPI, WebSocket, WebSocketDisconnect, Header, HTTPException
from fastapi.responses import JSONResponse
from fastapi.middleware.cors import CORSMiddleware
from starlette.websockets import WebSocketState
import uvicorn

from agent_config import AgentConfig, RouterConfig, load_config
from agent_filters import filter_observation, filter_actions
from agent_ordering import get_agent_order
from episode_logger import EpisodeLogger
from llm_query import query_llm, load_global_prompt
from continuous_agent import build_tools, run_tool_step, DEFAULT_TOOLS
from cmd_parser import parse_commands, ParserEnv  # SHARED parser + env shim (benchmark + router + gym)
from obs_encoder import (
    render_state_text, _num, task_officer, task_group, stable_task_token,
    render_facilities_text, render_workforce_text, render_tasks_text,
    render_logistics_text,
)
from choices_reliability import (
    dedupe_packages,
    enforce_diversity,
    apply_grounded_explanations,
    build_fallback_packages,
    compose_summary,
    append_repropose_hint,
)
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
    """
    Enumerate available actions from game state.
    If action_enumerator.py is available uses it; otherwise returns empty list.
    The router can still be tested without an enumerator.
    """
    try:
        from action_enumerator import ActionEnumerator
        enumerator = ActionEnumerator(game_state)
        # enumerate_all_actions() already returns list of dicts
        actions = enumerator.enumerate_all_actions()
    except ImportError:
        print("[router] action_enumerator not available — action list empty.")
        actions = []
    except Exception as e:
        print(f"[router] action_enumerator error: {e}")
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
    ):
        self.config = config
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
        self._choice_context: dict = {}
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
        self._director_injected_count: Dict[str, int] = {}
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

        Mapping: subagent → llm_agent; director + manual → human;
        director + LLM-driven (auto/llm/choices/coach) → auto_director.
        Falls back to the human director if the agent can't be resolved.
        """
        if agent is None:
            return dict(HUMAN_DIRECTOR_ACTOR)
        if agent.role == "director":
            kind = "human" if agent.actor_type == "manual" else "auto_director"
        else:
            kind = "llm_agent"
        return {
            "kind": kind,
            "name": agent.subagent_name,
            "role": agent.role,
            "actor_type": agent.actor_type,
        }

    def _log_action(self, actor: dict, category: str, name: str,
                    payload: dict, click_seq=None, client_ts=None) -> None:
        """Append one unified, actor-tagged `action` event to the session JSONL.

        `timestamp` (added by log_event) is server-receive time — authoritative for
        ordering. `client_ts` is the Unity-side UTC stamp from the originating
        frame, for precise human-action timing (e.g. time-to-complete-task).
        """
        self.logger.log_event({
            "event_type": "action",
            "schema_version": 1,
            "session_id": self.session_id,
            "episode_id": self.episode_id,
            "round": self.round_num,
            "day": self.day,
            "segment": self.segment,
            "actor": actor,
            "category": category,
            "name": name,
            "payload": payload,
            "click_seq": click_seq,
            "client_ts": client_ts,
        })

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
                self.logger.log_event({
                    "event_type": "session_end",
                    "session_id": self.session_id,
                    "label": self.api_key_label,
                    "rounds_played": self.round_num,
                })
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
        print(f"[router] Received message type: {msg_type}")

        if msg_type == "begin_round":
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
        elif msg_type == "director_message":
            await self._handle_director_message(msg)
        elif msg_type == "request_reproposal":
            await self._handle_request_reproposal(msg)
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

        # The round advanced: the world simulates and the fresh game_state now
        # reflects everything queued last phase. The committed-this-phase ledger is
        # stale — drop it so it doesn't double-count into the new phase.
        if self._committed_this_phase:
            print(f"[router]   🧾 Clearing planning-phase ledger "
                  f"({len(self._committed_this_phase)} committed action(s)).")
            self._committed_this_phase = []

        # Validate game state has required fields
        self._validate_game_state(game_state)

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

        if agent.actor_type == "auto":
            game_state, all_actions = await self._run_auto(
                agent, filtered_state, filtered_actions, game_state, all_actions
            )
        elif agent.actor_type == "choices":
            game_state, all_actions = await self._run_choices(
                agent, filtered_state, filtered_actions, game_state, all_actions
            )
        elif agent.actor_type == "coach":
            game_state, all_actions = await self._run_coach(
                agent, filtered_state, filtered_actions, game_state, all_actions
            )
        elif agent.actor_type == "continuous":
            game_state, all_actions = await self._run_continuous(
                agent, filtered_state, filtered_actions, game_state, all_actions
            )

        return game_state, all_actions

    async def _run_auto(
        self,
        agent: AgentConfig,
        filtered_state: dict,
        filtered_actions: List[dict],
        game_state: dict,
        all_actions: List[dict],
    ) -> Tuple[dict, List[dict]]:
        # Get conversation history from message queue
        conversation = self.message_queue.get_conversation(agent.subagent_name, "Director")
        raw = await asyncio.to_thread(query_llm, filtered_state, filtered_actions, agent, conversation)

        # Parse structured response (ACTIONS + REASONING + EXPECTED_IMPACT + NEXT_STEPS)
        parsed = self._parse_auto_response(raw)
        actions_str = parsed["actions_str"]

        # Validate LLM response
        indices, validation_errors = self._validate_action_indices(
            actions_str, filtered_actions,
            max_actions=agent.max_actions_per_package or len(filtered_actions),
            agent_name=agent.subagent_name
        )
        print(f"[{agent.subagent_name}] LLM chose indices: {indices}")
        print(f"[{agent.subagent_name}] Reasoning: {parsed['reasoning'][:100]}...")

        results = []
        sat_before = _get_satisfaction(game_state)
        budget_before = _get_budget(game_state)

        # Execute actions with runtime validation
        results, game_state = await self._execute_validated_actions(
            agent.subagent_name, indices, filtered_actions, game_state
        )

        # Update game state after all executions
        if results.get("executed"):
            # Get latest state from last executed action
            all_actions = _enumerate_actions(game_state)

        # Convert to old format for logging
        log_results = []
        for item in results.get("executed", []):
            log_results.append({
                "action_index": item["index"],
                "action_id": item.get("action_id"),
                "success": True,
            })
        for item in results.get("skipped", []):
            log_results.append({
                "action_index": item["index"],
                "success": False,
                "error": item["reason"],
            })

        self._update_conv_history(agent, filtered_state, filtered_actions, raw)
        self._log_turn(agent, filtered_state, filtered_actions, [], None, log_results,
                       sat_before, game_state, budget_before, raw, 0)

        # Post action summary with sectioned rationale to director
        await self._post_auto_summary(agent, results, parsed)

        return game_state, all_actions

    async def _autonomous_director_select(
        self,
        packages: List[dict],
        game_state: dict,
        reasoning: str
    ) -> Optional[int]:
        """Query autonomous director LLM to select a package index."""
        if not self._director_agent or not self._director_agent.llm_model:
            print("[router]   ⚠️  Director agent has no LLM configured")
            return 0  # Default to first package

        # Build prompt for director
        sat = _get_satisfaction(game_state)
        budget = _get_budget(game_state)
        day = game_state.get("sessionInfo", {}).get("currentDay", 0)

        # Format packages for the director
        packages_text = "\n".join([
            f"Package {i}: {pkg.get('label', 'Unnamed')} - {pkg.get('description', 'No description')}"
            for i, pkg in enumerate(packages)
        ])

        prompt = f"""You are the director of disaster response operations.

Current Situation:
- Day: {day}
- Satisfaction: {sat:.1f}%
- Budget: ${budget:,.2f}

Your team has proposed the following action packages:

{packages_text}

Team Reasoning: {reasoning}

Select the package that best balances immediate needs with long-term sustainability.
Respond with ONLY the package index number (0, 1, or 2).
"""

        # Query director LLM
        director_state = {"situation": prompt}
        director_actions = []  # Director doesn't need action list
        conversation = []  # No conversation history for director (for now)

        raw_response = await asyncio.to_thread(query_llm, director_state, director_actions, self._director_agent, conversation)

        # Parse index from response
        selected_idx = self._parse_director_choice(raw_response, len(packages))
        return selected_idx

    def _parse_director_choice(self, raw_response: str, num_packages: int) -> int:
        """Extract package index from director LLM response."""
        # Look for first number in response
        import re
        match = re.search(r'\b([0-9])\b', raw_response)
        if match:
            idx = int(match.group(1))
            if 0 <= idx < num_packages:
                return idx

        print(f"[router]   ⚠️  Could not parse valid index from director response: {raw_response[:100]}")
        return 0  # Default to first package

    async def _execute_actions_via_unity(
        self,
        actions: List[dict],
        game_state: dict
    ) -> Tuple[List[dict], dict]:
        """Execute actions via Unity and wait for results."""
        exec_results = []

        for action in actions:
            # Same single-in-flight discipline as _execute_action: hold the commit
            # lock across arm-future → send → await so concurrent officers can't
            # clobber the single-slot _pending_action. Per-action (not whole-batch)
            # so a long package doesn't block other officers longer than necessary.
            async with self._unity_commit_lock:
                loop = asyncio.get_event_loop()
                self._pending_action = loop.create_future()

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

            if result_msg is not None:
                exec_results.append({
                    "action_id": action.get("action_id", "unknown"),
                    "success": result_msg.get("success", False),
                    "error_message": result_msg.get("error_message", "")
                })
                # Update game state from result
                if "game_state" in result_msg:
                    game_state = result_msg["game_state"]
            else:
                print(f"[router]   ⚠️  Timeout executing action {action.get('action_id', 'unknown')}")
                exec_results.append({
                    "action_id": action.get("action_id", "unknown"),
                    "success": False,
                    "error_message": "Timeout waiting for Unity execution"
                })

        # Publish freshest global state (same authority as _execute_action).
        self._publish_state(game_state)
        return exec_results, game_state

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
        # Autonomous director: pick + execute immediately.
        if self._director_agent and self._director_agent.actor_type == "auto":
            print("[router]   🤖 Autonomous director selecting package...")
            selected_idx = await self._autonomous_director_select(packages, game_state, reasoning)
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
        self._pending_choice = loop.create_future()
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
            self._pending_choice = None

    async def _run_choices(
        self,
        agent: AgentConfig,
        filtered_state: dict,
        filtered_actions: List[dict],
        game_state: dict,
        all_actions: List[dict],
    ) -> Tuple[dict, List[dict]]:
        # Get conversation history from message queue
        conversation = self.message_queue.get_conversation(agent.subagent_name, "Director")
        raw, packages = await self._query_and_parse_choices(
            agent, filtered_state, filtered_actions, conversation
        )
        print(f"[router]   Proposing {len(packages)} packages to director.")

        # Store context for potential reproposal
        self._choice_context[agent.subagent_name] = (filtered_state, filtered_actions, game_state, all_actions)

        # Reliability + explainability layer (dedupe, grounded cost, fallback, summary).
        packages, reasoning = self._finalize_choice_packages(
            agent, packages, filtered_actions, game_state, raw
        )

        sat_before = _get_satisfaction(game_state)
        budget_before = _get_budget(game_state)

        await self._send_choices_proposal(agent, packages, filtered_actions, reasoning)

        selected_idx, exec_results, game_state, _superseded = await self._await_director_choice(
            packages, filtered_actions, game_state, reasoning)

        # Re-enumerate after director selected and Unity executed
        all_actions = _enumerate_actions(game_state)

        self._update_conv_history(agent, filtered_state, filtered_actions, raw)
        self._log_turn(agent, filtered_state, filtered_actions, packages, selected_idx,
                       exec_results, sat_before, game_state, budget_before, raw, 0)
        return game_state, all_actions

    async def _run_coach(
        self,
        agent: AgentConfig,
        filtered_state: dict,
        filtered_actions: List[dict],
        game_state: dict,
        all_actions: List[dict],
    ) -> Tuple[dict, List[dict]]:
        """Run coach agent - provides strategic analysis and recommendations without execution."""
        # Get conversation history from message queue
        conversation = self.message_queue.get_conversation(agent.subagent_name, "Director")
        raw = await asyncio.to_thread(query_llm, filtered_state, filtered_actions, agent, conversation)

        # Parse coach response
        recommendations = self._parse_coach_response(
            raw, filtered_actions,
            num_turns=agent.num_turns or 3,
            max_per_turn=agent.max_actions_per_turn or 3,
        )

        # Extract situation and analysis
        situation = self._extract_coach_situation(raw)
        analysis = self._extract_coach_analysis(raw)

        print(f"[router]   Coach provided {len(recommendations)} turn recommendations.")
        print(f"[router]   SITUATION: {situation[:100]}...")
        print(f"[router]   ANALYSIS: {analysis[:100]}...")

        sat_before = _get_satisfaction(game_state)
        budget_before = _get_budget(game_state)

        # Send coach report to Unity (informational only, no execution)
        await self._send({
            "type": "coach_report",
            "agent_name": agent.subagent_name,
            "talkinghead": agent.talkinghead_endpoint,
            "situation": situation,
            "analysis": analysis,
            "recommendations": recommendations,
            "timestamp": _now(),
        })

        # No execution, no waiting - coach just provides advice
        print(f"[router]   📋 Coach report sent to Unity.")

        self._update_conv_history(agent, filtered_state, filtered_actions, raw)
        self._log_turn(agent, filtered_state, filtered_actions, recommendations, None,
                       [], sat_before, game_state, budget_before, raw, 0)
        return game_state, all_actions

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
        "- execute_commands: act directly and immediately. Write what you want as "
        "command tags (e.g. <build>Kitchen,3</build>, <hire>untrained,4</hire>, "
        "<task>FOOD_C01,1</task>) — one tag per action, composed from the OPTIONS list "
        "you were shown. You describe WHAT you want, never a menu index; the tags are "
        "resolved against the live state and committed on your own judgment. Use it when "
        "you are confident and the action is within your remit.\n"
        "- propose_choices: hand the decision to the human director as selectable "
        "packages. Use it when the call is genuinely theirs, the stakes or ambiguity "
        "are high, or you want their steer. The director's review time is scarce — "
        "propose only when it adds real value, and keep packages genuinely distinct.\n"
        "- talk_to_director: explain, ask a clarifying question, or flag something. "
        "Keep explanations grounded in the real state numbers; they build calibrated "
        "trust, not blind acceptance. Ask only when the answer would change what you do.\n"
        "- read_state / list_actions: refresh your view of the state and the OPTIONS "
        "you can act on. get_facilities / get_workforce / get_tasks / get_logistics pull "
        "one focused slice when you don't need the whole picture.\n"
        "- responsibility_lookup: check who owns an action OR who answers a task, and "
        "whether it is yours, before acting/answering near your role's edge or naming a "
        "colleague. Use it so you name the RIGHT officer instead of guessing.\n"
        "- finish: end your turn when nothing further is worth doing.\n"
        "Advise and propose by default; act only on a clear, specific instruction. "
        "A request to recommend, diagnose, explain, or ask 'why doesn't X work' is "
        "NOT authorization to execute — answer it, and (where useful) offer the "
        "action as a proposal rather than committing it. Reserve execute_commands "
        "for when the director has clearly told you to do the specific thing, or it "
        "is unambiguously routine within your remit and they expect it done. When in "
        "doubt, propose or ask rather than act. Ground every number you cite in the "
        "state you were given.\n"
        "CRITICAL: never claim to have built, hired, staffed, moved, or changed "
        "anything unless you actually called execute_commands (or the director "
        "selected a package you proposed) THIS turn and saw a success result. If an "
        "action you want is not in your available action list, say so plainly and "
        "explain what is blocking it — do not pretend it happened.\n"
        "STAY IN YOUR LANE: you may only act on, and answer tasks within, your own "
        "remit. If something the situation needs — an action or a task — is not yours, "
        "do NOT do it or claim it — call responsibility_lookup to find the officer who "
        "owns it, then tell the director it belongs to that officer by their correct "
        "name. Never invent a colleague's name or role from memory; look it up."
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
        async with self._agent_lock(agent.subagent_name):
            return await self._run_continuous_inner(
                agent, filtered_state, filtered_actions, game_state, all_actions,
                triggered_by_director,
            )

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

    async def _run_continuous_for_message(self, agent: AgentConfig) -> None:
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
            triggered_by_director=True,
        )

    # Keep this many most-recent activation turns (each: one user re-grounding +
    # its assistant/tool steps) when compacting a continuous transcript. The system
    # message and the current turn are always retained; only OLD tool-spam is shed.
    _CONTINUOUS_KEEP_TURNS = 8

    @staticmethod
    def _compact_transcript(messages: List[dict], keep_turns: int) -> List[dict]:
        """Bound a continuous transcript to system + the last `keep_turns` turns.

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
        if len(starts) <= keep_turns:
            return messages
        return [system] + body[starts[-keep_turns]:]

    # Tools that COMMIT to the world (spend, build, hire, transfer) or seize the
    # director's attention with a proposal. Stripped from the palette on an
    # unprompted "reactive" turn so an officer physically cannot act unbidden.
    _ACTING_TOOLS = frozenset({"execute_commands", "propose_choices"})

    async def _run_continuous_inner(
        self,
        agent: AgentConfig,
        filtered_state: dict,
        filtered_actions: List[dict],
        game_state: dict,
        all_actions: List[dict],
        triggered_by_director: bool = False,
    ) -> Tuple[dict, List[dict]]:
        """Drive one turn of the continuous (tool-using) agent."""
        # Reactive autonomy ("activate when spoken to"): on an UNPROMPTED turn a
        # reactive officer may brief the director but must not act — so we hand it a
        # palette with the acting tools removed. It is then structurally impossible
        # to commit anything unbidden; no reliance on prompt adherence. Any other
        # opening_mode (emergent/brief_first) keeps the full configured palette.
        opening_mode = getattr(agent, "opening_mode", "emergent")
        brief_only = (opening_mode == "reactive") and not triggered_by_director
        if brief_only:
            base = list(agent.tools) if agent.tools else list(DEFAULT_TOOLS)
            tools = build_tools([t for t in base if t not in self._ACTING_TOOLS])
        else:
            tools = build_tools(agent.tools)
        agent_cfg = vars(agent)  # run_tool_step reads provider/model/endpoint/key/budget
        max_steps = agent.max_steps or 8

        # The continuous agent carries ONE growing transcript for the whole game.
        # Seed the system message once, fold in any director input that arrived
        # since our last activation, then append this activation's live-state turn.
        # The tool loop below appends its assistant/tool turns to this same list,
        # so every prior step stays visible across activations and rounds.
        name = agent.subagent_name
        messages = self._continuous_transcripts.setdefault(name, [])
        if not messages:
            messages.append(self._continuous_system_message(agent))
        director_entries = [
            e for e in self.message_queue.get_conversation(name, "Director")
            if e.get("from") == "Director"
        ]
        already = self._director_injected_count.get(name, 0)
        for e in director_entries[already:]:
            messages.append({"role": "user", "content": f"[Director] {e.get('content', '')}"})
        self._director_injected_count[name] = len(director_entries)
        director_has_spoken = len(director_entries) > 0
        messages.append(self._continuous_turn_message(
            agent, filtered_state, filtered_actions, director_has_spoken,
            brief_only=brief_only, triggered_by_director=triggered_by_director))

        # Bound the ever-growing per-officer transcript: keep the system message and
        # the last N activation turns (the current one always included), shedding old
        # tool-spam. The committed ledger rides inside each turn message, so it is
        # never dropped. Reassign both the persistent store and the local handle so
        # the loop below appends onto the compacted list.
        messages = self._compact_transcript(messages, self._CONTINUOUS_KEEP_TURNS)
        self._continuous_transcripts[name] = messages

        sat_before = _get_satisfaction(game_state)
        budget_before = _get_budget(game_state)
        executed_total = 0
        # Whether the officer sent the director a message this turn. Answering a
        # question via talk_to_director is real work even though it executes no game
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

        print(f"[router]   ▶ Continuous agent {agent.subagent_name}: "
              f"{len(filtered_actions)} actions, up to {max_steps} steps, "
              f"tools={[t['function']['name'] for t in tools]}")

        for step in range(max_steps):
            resp = await asyncio.to_thread(
                run_tool_step, messages, tools, agent_cfg, agent.tool_mode
            )
            if resp.get("error"):
                print(f"[router]   ⚠️  Continuous step {step} error: {resp['error']}")
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
                if tc["name"] == "talk_to_director":
                    talked = True
                result_str, game_state, all_actions, filtered_actions, meta = \
                    await self._dispatch_continuous_tool(
                        agent, tc, game_state, all_actions, filtered_actions,
                        brief_only=brief_only,
                    )
                # Every tool_call id MUST get a matching tool result before the
                # next assistant turn (OpenAI/Anthropic protocol requirement).
                messages.append({"role": "tool", "tool_call_id": tc["id"], "content": result_str})
                executed_total += meta.get("executed", 0)
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
                if brief_only and tc["name"] == "talk_to_director":
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

        raw = last_text or f"[continuous] {executed_total} action(s) executed"
        self._log_turn(agent, filtered_state, filtered_actions, turn_attempts, None,
                       turn_results, sat_before, game_state, budget_before,
                       raw, tokens_total)
        print(f"[router]   ✓ Continuous agent {agent.subagent_name}: "
              f"{executed_total} action(s) executed this turn.")
        return game_state, all_actions

    def _continuous_system_message(self, agent: AgentConfig) -> dict:
        """The system message (role + global prompt + tool policy). Built ONCE per
        game — it seeds the persistent transcript and never changes mid-game."""
        use_global = agent.use_global_prompt
        global_prompt = load_global_prompt() if use_global else ""
        agent_prompt = agent.system_prompt or (
            "You are an officer in a disaster-relief operation."
        )
        if global_prompt:
            system = f"{global_prompt}\n\n---\n\nAGENT ROLE: {agent_prompt}"
        else:
            system = agent_prompt
        system = f"{system}\n\n---\n\n{self._CONTINUOUS_TOOL_POLICY}"
        return {"role": "system", "content": system}

    def _continuous_turn_message(
        self,
        agent: AgentConfig,
        filtered_state: dict,
        filtered_actions: List[dict],
        director_has_spoken: bool,
        brief_only: bool = False,
        triggered_by_director: bool = False,
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
            closing = (
                "You have NOT been directly addressed this turn, and you act only "
                "when the director speaks to you. You have NO action tools right now, "
                "so do not attempt to build, hire, transfer, or propose. Send AT MOST "
                f"ONE short talk_to_director message that (1) opens by naming your "
                f"office (you are the {title}), states your responsibility "
                "in one line and what you can do for the director "
                f"(you can {capabilities}), and (2) in at most 2 more sentences gives "
                "the single biggest need in your domain, the budget remaining, and one "
                "recommendation — then call finish. Ground any factual claim (building "
                "counts, worker counts, shortfalls) in the situation above, the "
                "'already committed' ledger, or a read tool (read_state, "
                "get_facilities, get_workforce) — count anything you committed this "
                "phase as pending, and never state a count from memory. If nothing "
                "material has changed since your last brief, "
                "just restate your remit in one line and call finish."
            )
        elif opening_mode == "reactive" and triggered_by_director:
            closing = (
                f"Identify yourself by your office (you are the {title}) when you "
                "reply to the director. "
                "The director has addressed you. Do EXACTLY what they asked — nothing "
                "more. Do not scale the quantity up or down, and do not add sites, "
                "targets, or extra actions they did not name. If the director asks a "
                "factual question (how many buildings exist, worker counts, what is "
                "built where), answer from the situation above, the 'already "
                "committed' ledger, AND a read tool (read_state, get_facilities, "
                "get_workforce, get_tasks) — reconcile all three: a worker you hired "
                "or a facility you queued THIS phase counts even if the frozen "
                "situation still shows it absent, so report it as queued/pending "
                "rather than saying it doesn't exist. Do NOT invent counts from "
                "memory. If the instruction is "
                "ambiguous or would exceed budget, ask ONE brief clarifying question "
                "via talk_to_director instead of guessing. When done, send ONE short "
                "talk_to_director message confirming what you did (or answering their "
                "question), then call finish."
            )
        elif opening_mode == "brief_first" and not director_has_spoken:
            # FIRST message (opening brief): introduce the office by name.
            closing = (
                "The director has not given you any direction yet. Do NOT commit any "
                "builds, hires, or transfers. Send EXACTLY ONE short talk_to_director "
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
                "but drop the formal self-introduction. If they ask a factual or "
                "quantitative question, LEAD your reply with the concrete figure or a "
                "direct yes/no from the state or a read tool (e.g. \"You have 12 free "
                "spaces — 30 capacity, 18 filled.\"), then stop. Do NOT reply with a "
                "description of what you answered or did (\"Answered the capacity "
                "question; no action taken\") — a summary of the speech act is not an "
                "answer."
            )
        state_text = render_state_text(filtered_state)
        action_text = self._render_options_compact(filtered_actions, filtered_state)
        return {
            "role": "user",
            "content": (
                f"It is your turn. Current situation:\n{state_text}\n\n"
                f"{self._committed_ledger_text()}"
                f"Actions available to you now:\n{action_text}\n\n"
                f"{closing}"
            ),
        }

    def _build_continuous_messages(
        self,
        agent: AgentConfig,
        filtered_state: dict,
        filtered_actions: List[dict],
    ) -> List[dict]:
        """Assemble a full COLD-START message list (system + prior director
        conversation + current turn).

        This is what a continuous turn looks like with an empty transcript. The
        live loop does NOT call this per turn — it appends to the persistent
        transcript (see _run_continuous_inner) so the whole game's trajectory
        accumulates. Kept for cold-start equivalence and out-of-band inspection."""
        messages: List[dict] = [self._continuous_system_message(agent)]
        director_has_spoken = False
        for entry in self.message_queue.get_conversation(agent.subagent_name, "Director"):
            content = entry.get("content", "")
            if entry.get("from") == "Director":
                director_has_spoken = True
                messages.append({"role": "user", "content": f"[Director] {content}"})
            else:
                messages.append({"role": "assistant", "content": content})
        messages.append(self._continuous_turn_message(
            agent, filtered_state, filtered_actions, director_has_spoken))
        return messages

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
        return (
            "YOU HAVE ALREADY COMMITTED these actions this planning phase — they are "
            "locked in and real, and take effect when the phase resolves (next "
            "round):\n"
            f"{items}\n"
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
        """Append a succeeded action to the planning-phase ledger (deduped)."""
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

        (a) TASK rows carry the stable task token (obs_encoder.stable_task_token,
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
                # action_enumerator nests these fields under "assignment" (see
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
                    tok = (stable_task_token(self._norm_task_for_token(raw))
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
            for sid in sorted(sites, key=lambda s: (s is None, s)):
                nm, types = sites[sid]
                lines.append(f"    site {sid} ({nm}): {', '.join(sorted(types))}")
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
            if idx not in seen:
                seen.add(idx)
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
        """Raw allActiveTasks row → the {title, affects} shape stable_task_token
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
            if stable_task_token(self._norm_task_for_token(t)).lower() == ql:
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
            token = stable_task_token(self._norm_task_for_token(t))
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

    async def _dispatch_continuous_tool(
        self,
        agent: AgentConfig,
        tool_call: dict,
        game_state: dict,
        all_actions: List[dict],
        filtered_actions: List[dict],
        brief_only: bool = False,
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
        args = tool_call.get("arguments") or {}
        meta = {"executed": 0, "finish": False}

        if brief_only and name in self._ACTING_TOOLS:
            return (
                "REFUSED: you have not been directly addressed this turn, so you "
                "cannot take actions or send proposals. Brief the director via "
                "talk_to_director (or call finish); they will tell you what to do.",
                game_state, all_actions, filtered_actions, meta,
            )

        if name == "read_state":
            # Read the freshest state the router holds (kept current by every execute
            # commit + the per-turn get_game_state pull), so a look-up reflects reality
            # — including the officer's own just-executed actions — not a stale snapshot.
            fresh = self._latest_game_state or game_state
            return render_state_text(self._filter_state(fresh, agent)), \
                game_state, all_actions, filtered_actions, meta

        # Granular getters — one slice of the same filtered observation each, so an
        # officer can pull just the detail it needs without re-dumping read_state.
        # get_logistics needs the officer's enumerated actions (the affordance block
        # is derived from them); the others are pure state slices.
        if name in ("get_facilities", "get_workforce", "get_tasks", "get_logistics"):
            fs = self._filter_state(self._latest_game_state or game_state, agent)
            if name == "get_logistics":
                text = render_logistics_text(fs, filtered_actions)
            else:
                text = {
                    "get_facilities": render_facilities_text,
                    "get_workforce": render_workforce_text,
                    "get_tasks": render_tasks_text,
                }[name](fs)
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
                await self._execute_actions_via_unity(actions_to_run, game_state)
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
                    # Director-facing commit bubble: one plain-English past-tense
                    # line per committed action ("Action: Built Shelter at
                    # Riverside for $2,000") — no emojis, no command-tag syntax.
                    # This is a distinct, log-style confirmation channel; the
                    # officer still speaks to the director in its own words via
                    # talk_to_director. Also ground-truth logged via _log_action
                    # above and surfaced to the MODEL in `lines` below.
                    await self._send_agent_response(
                        agent, "Action: " + self._humanize_committed_action(action),
                        "agent_response")
                    lines.append(f"  ✅ {desc}")
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
                        "agent_response")
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
            return result_text, game_state, all_actions, filtered_actions, meta

        if name == "talk_to_director":
            message = str(args.get("message") or "").strip()
            if not message:
                return "ERROR: empty message.", game_state, all_actions, filtered_actions, meta
            await self._send_agent_response(agent, message, "agent_response")
            return "Message delivered to the director.", \
                game_state, all_actions, filtered_actions, meta

        if name == "finish":
            note = str(args.get("note") or "").strip()
            if note:
                await self._send_agent_response(agent, note, "agent_response")
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

        Reuses the existing choices machinery (_send_choices_proposal + the
        choice_made Future). Blocks until the human director selects (or the
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
        # no GameTask. The classic workflow agents still use _send_choices_proposal.
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
                            "agent_response",
                        )
            detail = "\n".join(lines) if lines else "  (engine reported no results)"
            body = (f"The director selected package {selected_idx} ({label}). "
                    f"{executed}/{total} action(s) SUCCEEDED. Engine results:\n{detail}\n"
                    "Report ONLY the SUCCESS lines as done. Do NOT claim any FAILED "
                    "action happened — treat failures as not executed and adapt.")
        body += "\n\nUpdated actions:\n" + self._render_options_compact(filtered_actions, game_state)
        return body, game_state, all_actions, filtered_actions, executed, superseded, result_rows

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
        if self._pending_choice and not self._pending_choice.done():
            print(f"[router]    ✅ Setting result on pending Future")
            self._pending_choice.set_result(msg)
        else:
            print(f"[router]    ⚠️  WARNING: No pending choice to fulfill!")

    async def _handle_action_result(self, msg: dict):
        """Handle action execution result from Unity."""
        if self._pending_action and not self._pending_action.done():
            self._pending_action.set_result(msg)
        # else: silently ignore - may be stray message

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
        self._choice_context.clear()
        # A continuous agent's transcript spans a whole game; a fresh game must
        # start it clean (no stale trajectory bleeding across games).
        self._continuous_transcripts.clear()
        self._director_injected_count.clear()
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
        self.logger.log_event({
            "event_type": "conversation_message",
            "round": self.round_num,
            "actor": HUMAN_DIRECTOR_ACTOR,
            "from": "Director",
            "to": convo_key,
            "content": content,
            "message_type": "director_message",
            "message_id": message["id"],
            "click_seq": msg.get("click_seq"),
            "timestamp": message["timestamp"]
        })

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
                _t = asyncio.create_task(self._run_continuous_for_message(agent))
                _t.add_done_callback(self._on_round_task_done)
                return
            # No state yet (message before any begin_round): fall through to chat.

        # For choices agents with an active proposal context, force a single-path
        # decision: CLARIFY, REPROPOSE, or CHAT. This avoids the previous bug where
        # the agent both asked clarifying questions AND auto-reproposed.
        if agent.actor_type == "choices" and agent.subagent_name in self._choice_context:
            decision = await asyncio.to_thread(self._classify_director_intent, agent, conversation)
            intent = decision.get("intent", "CHAT")
            payload = (decision.get("payload") or "").strip()

            print(f"[router]   Intent={intent} for {to_agent_name}")

            if intent == "REPROPOSE":
                ack = payload or "Generating new options based on your feedback."
                await self._send_agent_response(agent, ack, "agent_response")
                await self._repropose_choices(agent)
                return

            if intent == "CLARIFY":
                question = payload or "Could you clarify what you'd like me to change about the options?"
                await self._send_agent_response(agent, question, "agent_response")
                return

            # CHAT: payload IS the reply when present.
            if payload:
                await self._send_agent_response(agent, payload, "agent_response")
                return
            # Otherwise fall through to the legacy free-form generator below.

        # Default path: free-form conversational response.
        response_text = await asyncio.to_thread(self._generate_conversational_response, agent, conversation)
        await self._send_agent_response(agent, response_text, "agent_response")

    async def _send_agent_response(self, agent: AgentConfig, response_text: str, msg_type: str):
        """Persist + log + push an agent's conversational reply to the Director."""
        # Drop a leading "<Role> Officer:" self-label badge (the avatar already shows
        # who's speaking); the prompt asks officers to introduce themselves once, not
        # on every message. See _strip_self_label.
        response_text = _strip_self_label(response_text)
        response_message = self.message_queue.send_message(
            from_agent=agent.subagent_name,
            to_agent="Director",
            content=response_text,
            msg_type=msg_type,
            round_num=self.round_num,
        )
        self.logger.log_event({
            "event_type": "conversation_message",
            "round": self.round_num,
            "actor": self._actor_for(agent),
            "from": agent.subagent_name,
            "to": "Director",
            "content": response_text,
            "message_type": msg_type,
            "message_id": response_message["id"],
            "timestamp": response_message["timestamp"],
        })
        await self._send({
            "type": "agent_message",
            "agent_name": agent.subagent_name,
            "talkinghead_endpoint": agent.talkinghead_endpoint,
            "content": response_text,
            "message_type": msg_type,
            "round": self.round_num,
            "timestamp": response_message["timestamp"],
        })
        print(f"[router] {agent.subagent_name} → Director: {response_text[:60]}...")

    def _build_observation_snapshot(self, agent: AgentConfig) -> str:
        """Compact factual ground-truth snapshot for the CLARIFY branch.

        Dumps the demand/capacity-relevant subtrees of the most recent
        filtered observation as JSON so the LLM can quote real numbers
        (clients waiting, building capacities, worker counts, open tasks)
        rather than restating what's already in chat memory (day/budget).
        Returns "" if no context is stashed.
        """
        ctx = self._choice_context.get(agent.subagent_name)
        if ctx is None:
            return ""
        filtered_state, _filtered_actions, _gs, _all = ctx

        # Keep only the subtrees that actually inform shelter-vs-kitchen-style
        # trade-offs. Skip session/budget — the agent already cited those when
        # generating its prior REASONING and they're in the chat history.
        relevant_keys = (
            "constructionState",
            "mapState",
            "workers",
            "workforceState",
            "logistics",
            "tasks",
        )
        slice_ = {
            k: filtered_state[k]
            for k in relevant_keys
            if k in filtered_state and filtered_state[k] is not None
        }
        if not slice_:
            return ""

        try:
            payload = json.dumps(slice_, default=str)
        except Exception:
            return ""

        # Soft cap to avoid burning the whole context on the snapshot.
        MAX_CHARS = 4000
        if len(payload) > MAX_CHARS:
            payload = payload[:MAX_CHARS] + "…(truncated)"

        return (
            "Observation facts (use these as ground truth when citing capacity, demand, worker counts, "
            "open tasks, or building inventory — do NOT invent numbers):\n"
            + payload
        )

    def _classify_director_intent(self, agent: AgentConfig, conversation: list) -> dict:
        """Single-call classification: does the Director want CLARIFY, REPROPOSE, or CHAT?

        Returns ``{"intent": <str>, "payload": <str>}``. The payload doubles as
        the message to send back: a clarifying question, a one-sentence
        acknowledgement of reproposal, or a free-form reply.
        """
        import anthropic
        import openai

        provider = (agent.llm_provider or "anthropic").lower()

        messages = []
        for entry in conversation:
            sender = entry.get("from")
            if sender == "Director":
                messages.append({"role": "user", "content": entry.get("content", "")})
            elif sender == agent.subagent_name:
                messages.append({"role": "assistant", "content": entry.get("content", "")})
        if not messages:
            messages = [{"role": "user", "content": "Decide and respond."}]

        system_prompt = (
            f"You are {agent.subagent_name}, a choices agent in the ARC disaster-response game. "
            "You previously proposed strategy packages to the Director, and the Director has now "
            "messaged you. The EXACT options you proposed are recorded earlier in this "
            "conversation as your own turn beginning 'Here are the exact options I proposed'. "
            "Treat that as your reliable memory: when asked what you proposed or why, quote those "
            "options accurately. NEVER say you lack a record of your proposals — you have it above.\n\n"
            "Decide EXACTLY ONE response path:\n\n"
            "  REPROPOSE — they want a fresh set of packages and you have enough information to commit.\n"
            "  CLARIFY   — you genuinely need more information before you could repropose.\n"
            "  CHAT      — they are not asking for new packages (a question, thanks, small talk).\n\n"
            "Reply with exactly two lines:\n"
            "DECISION: <REPROPOSE | CLARIFY | CHAT>\n"
            "PAYLOAD:\n"
            "  - If REPROPOSE: a one-sentence acknowledgement.\n"
            "  - If CLARIFY: 1-3 short sentences that *reduce* the Director's cognitive load. State the "
            "relevant facts first — situation + concrete trade-off (costs, capacity gains, impacts), grounded in "
            "real numbers from the observation. Then EITHER:\n"
            "      • offer a light recommendation when one option clearly fits the Director's stated priority "
            "(e.g. 'If budget matters most, the kitchen is the better fit at $1k vs $1.5k.'), OR\n"
            "      • ask ONE short clarifying question only when the choice is genuinely ambiguous given what "
            "they've said.\n"
            "    Do not do both. Do not trail off with a vague question when the answer is obvious from facts "
            "you already have. Never invent numbers; only quote facts from the observation or chat.\n"
            "  - If CHAT: a brief conversational reply.\n\n"
            "Do not ask a clarifying question AND repropose. Be decisive."
        )
        if agent.system_prompt:
            system_prompt += f"\n\nAgent role: {agent.system_prompt}"

        # Inject a brief observation snapshot (budget, satisfaction, day/segment)
        # so the CLARIFY branch can ground its facts in the actual game state,
        # not just whatever has been mentioned in chat so far.
        obs_summary = self._build_observation_snapshot(agent)
        if obs_summary:
            system_prompt += f"\n\n{obs_summary}"

        raw = ""
        try:
            if provider == "anthropic":
                api_key = os.environ.get(agent.api_key_env or "ANTHROPIC_API_KEY")
                if not api_key:
                    return {"intent": "CHAT", "payload": ""}
                client = anthropic.Anthropic(api_key=api_key)
                resp = client.messages.create(
                    model=agent.llm_model or "claude-sonnet-4-6",
                    max_tokens=400,
                    system=system_prompt,
                    messages=messages,
                )
                raw = resp.content[0].text
            elif provider == "openai":
                api_key = os.environ.get(agent.api_key_env or "OPENAI_API_KEY")
                if not api_key:
                    return {"intent": "CHAT", "payload": ""}
                base_url = getattr(agent, "llm_endpoint", None)
                client = (
                    openai.OpenAI(api_key=api_key, base_url=base_url) if base_url
                    else openai.OpenAI(api_key=api_key)
                )
                resp = client.chat.completions.create(
                    model=agent.llm_model or "gpt-4o-mini",
                    max_tokens=400,
                    messages=[{"role": "system", "content": system_prompt}] + messages,
                )
                raw = resp.choices[0].message.content or ""
            else:
                return {"intent": "CHAT", "payload": ""}
        except Exception as e:
            print(f"[router] Intent classification failed for {agent.subagent_name}: {e}")
            return {"intent": "CHAT", "payload": ""}

        intent = "CHAT"
        payload = ""
        for raw_line in raw.split("\n"):
            line = raw_line.strip()
            if line.upper().startswith("DECISION:"):
                val = line.split(":", 1)[1].strip().upper()
                # Strip trailing punctuation/markdown the model occasionally adds.
                val = val.strip("*` _.")
                if val in ("REPROPOSE", "CLARIFY", "CHAT"):
                    intent = val
            elif line.upper().startswith("PAYLOAD:"):
                idx = raw.upper().find("PAYLOAD:")
                payload = raw[idx + len("PAYLOAD:"):].strip()
                break

        return {"intent": intent, "payload": payload}

    async def _handle_request_reproposal(self, msg: dict):
        """Handle director requesting an agent to repropose choices."""
        agent_name = msg.get("agent_name")
        feedback = msg.get("feedback", "")

        if not agent_name:
            print(f"[router] Invalid request_reproposal: missing agent_name")
            return

        print(f"[router] Director requests reproposal from {agent_name}")

        # Store feedback message
        if feedback:
            message = self.message_queue.send_message(
                from_agent="Director",
                to_agent=agent_name,
                content=feedback,
                msg_type="feedback",
                round_num=self.round_num
            )

            # Log feedback
            self.logger.log_event({
                "event_type": "conversation_message",
                "round": self.round_num,
                "actor": HUMAN_DIRECTOR_ACTOR,
                "from": "Director",
                "to": agent_name,
                "content": feedback,
                "message_type": "feedback",
                "message_id": message["id"],
                "timestamp": message["timestamp"]
            })

        # Find the agent
        agent = self._get_agent_by_name(agent_name)
        if not agent:
            print(f"[router] Agent '{agent_name}' not found for reproposal")
            return

        # Repropose choices
        await self._repropose_choices(agent)

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

    async def _execute_action(self, agent_name: str, action: dict) -> Tuple[dict, dict]:
        """Send execute_action to Unity, wait for result via Future, return (result, updated_state).

        The whole create-future → send → await critical section runs under
        _unity_commit_lock so concurrent officers never have two requests in flight
        against the single-slot _pending_action (results correlate by timing only).
        The future is armed BEFORE the send so a fast Unity reply can't land in an
        empty slot and get dropped as a stray.
        """
        async with self._unity_commit_lock:
            loop = asyncio.get_event_loop()
            self._pending_action = loop.create_future()
            await self._send({
                "type": "execute_action",
                "agent_name": agent_name,
                "action": action,
                "timestamp": _now(),
            })
            try:
                # 30s to match the batch path (_execute_actions_via_unity). Construction
                # and other non-instant actions can take >10s on the Unity side; the old
                # 10s window returned spurious "Timeout" false-failures (and dropped the
                # late result as a stray), so the agent never saw the action land.
                result = await asyncio.wait_for(self._pending_action, timeout=30.0)
            except asyncio.TimeoutError:
                print(f"[router]   ⚠️  Timeout waiting for action result")
                return {"success": False, "error_message": "Timeout"}, {}
            finally:
                self._pending_action = None

        game_state = result.get("game_state", {})
        # Freshest authoritative global state — publish so concurrent officers and
        # the post-gather director_turn always read the latest world.
        self._publish_state(game_state)
        return result, game_state

    async def _execute_validated_actions(
        self,
        agent_name: str,
        action_indices: List[int],
        valid_actions: List[dict],
        initial_state: dict
    ) -> dict:
        """
        Execute actions with runtime validation (Layer 3).
        Tracks budget/resources and skips actions that became invalid.

        Returns:
            {
                'executed': [{'index': idx, 'action': action, 'action_id': id}, ...],
                'skipped': [{'index': idx, 'reason': str}, ...],
                'errors': [{'index': idx, 'error': str}, ...]
            }
        """
        # Track running state
        running_budget = _get_budget(initial_state)
        free_workers = self._count_free_workers(initial_state)
        # Sites already targeted by a construction action this turn. Prevents
        # an agent from queueing e.g. Shelter@site9 + Kitchen@site9 in the
        # same batch (only one building fits per site).
        used_construction_sites: set = set()

        results = {
            'executed': [],
            'skipped': [],
            'errors': []
        }

        # Actor for the unified action log: server-side execution is agent-driven
        # (llm_agent, or auto_director when the director runs autonomously).
        actor = self._actor_for(self._get_agent_by_name(agent_name))

        current_state = initial_state

        for idx in action_indices:
            action = valid_actions[idx]
            action_cost = action.get('cost', 0)
            # Note: enumerated actions use snake_case `action_type`; the older
            # `actionType` lookup elsewhere in this file is a stale leftover.
            action_type = action.get('action_type') or action.get('actionType', 'unknown')
            action_desc = action.get('description', '?')

            # Check budget
            if action_cost > running_budget:
                msg = (f"Insufficient budget: need ${action_cost:,}, have ${running_budget:,}")
                results['skipped'].append({'index': idx, 'reason': msg})
                print(f"[{agent_name}]   ⚠️  Skipping action {idx} ({action_type}): {msg}")
                continue

            # Check workers (for assignment actions)
            if action_type == 'AssignWorker' and free_workers <= 0:
                msg = f"No free workers available"
                results['skipped'].append({'index': idx, 'reason': msg})
                print(f"[{agent_name}]   ⚠️  Skipping action {idx} ({action_type}): {msg}")
                continue

            # Reject duplicate construction at the same site this turn
            if action_type == 'construction':
                site_id = (action.get('construction') or {}).get('site_id')
                if site_id is not None:
                    if site_id in used_construction_sites:
                        msg = f"Site {site_id} already targeted by an earlier construction action this turn"
                        results['skipped'].append({'index': idx, 'reason': msg})
                        print(f"[{agent_name}]   ⚠️  Skipping action {idx} ({action_type}): {msg}")
                        continue
                    used_construction_sites.add(site_id)

            # Execute action
            try:
                print(f"[{agent_name}]   ✓ Executing action {idx}: {action_desc} (cost: ${action_cost:,})")
                result, new_state = await self._execute_action(agent_name, action)

                if result.get("success", False):
                    results['executed'].append({
                        'index': idx,
                        'action': action,
                        'action_id': action.get('action_id')
                    })

                    # Update running state
                    current_state = new_state
                    running_budget = _get_budget(new_state)
                    free_workers = self._count_free_workers(new_state)

                    print(f"[{agent_name}]      Budget: ${running_budget:,}, Free workers: {free_workers}")
                    self._log_action(actor, "game_action", "execute_action", {
                        "index": idx,
                        "action_id": action.get('action_id'),
                        "action_type": action_type,
                        "description": action_desc,
                        "cost": action_cost,
                        "success": True,
                        "error_message": None,
                    })
                else:
                    error_msg = result.get('error_message', 'Unknown error')
                    results['errors'].append({'index': idx, 'error': error_msg})
                    print(f"[{agent_name}]   ✗ Action {idx} failed: {error_msg}")
                    self._log_action(actor, "game_action", "execute_action", {
                        "index": idx,
                        "action_id": action.get('action_id'),
                        "action_type": action_type,
                        "description": action_desc,
                        "cost": action_cost,
                        "success": False,
                        "error_message": error_msg,
                    })

            except Exception as e:
                msg = f"Exception during execution: {e}"
                results['errors'].append({'index': idx, 'error': str(e)})
                print(f"[{agent_name}]   ✗ Action {idx} exception: {e}")
                self._log_action(actor, "game_action", "execute_action", {
                    "index": idx,
                    "action_id": action.get('action_id'),
                    "action_type": action_type,
                    "description": action_desc,
                    "cost": action_cost,
                    "success": False,
                    "error_message": f"exception: {e}",
                })

        # Summary
        print(f"[{agent_name}] Execution summary: "
              f"{len(results['executed'])} executed, "
              f"{len(results['skipped'])} skipped, "
              f"{len(results['errors'])} errors")

        # Also return the final refreshed state (post last successful commit) so the
        # caller can log deltas/rewardMetrics against real post-execution state
        # instead of the frozen pre-turn snapshot.
        return results, current_state

    def _count_free_workers(self, game_state: dict) -> int:
        """Count number of free (unassigned) workers."""
        try:
            workers = game_state.get('workers', {}).get('workers', [])
            return sum(1 for w in workers if w.get('currentAssignment') is None)
        except Exception:
            return 0  # Safe default if workers data unavailable

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
        # ENCODED name `tasks` — so tasks were silently dropped and obs_encoder (which
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

    async def _post_auto_summary(self, agent: AgentConfig, results: dict, parsed: dict):
        """
        Post a sectioned summary to the director after an auto agent acts.

        The "Actions Executed" section is filled deterministically from
        ``results`` (router-authoritative); the remaining sections come from
        the LLM's parsed response.

        Args:
            agent: Agent configuration
            results: Execution results dict with "executed" / "skipped" / "errors" lists
            parsed: Dict from _parse_auto_response with keys
                actions_str, reasoning, expected_impact, next_steps
        """
        executed = results.get("executed", [])
        skipped = results.get("skipped", [])
        errors = results.get("errors", [])

        # Section 1: Actions Executed — router-authoritative (no LLM hallucination).
        action_lines = ["**Actions Executed**"]
        if executed:
            for item in executed:
                action = item.get("action") or {}
                desc = action.get("description", "?")
                cost = action.get("cost", 0)
                try:
                    cost_str = f"${cost:,}"
                except (TypeError, ValueError):
                    cost_str = f"${cost}"
                action_lines.append(f"• {desc} ({cost_str})")
        else:
            action_lines.append("• (no actions taken)")
        for item in skipped:
            action_lines.append(f"• [skipped] {item.get('reason', 'invalid')}")
        for item in errors:
            action_lines.append(f"• [failed] {item.get('error', 'unknown error')}")

        sections = ["\n".join(action_lines)]

        # Sections 2–4: from the LLM
        if parsed.get("reasoning"):
            sections.append(f"**Reasoning**\n{parsed['reasoning']}")
        if parsed.get("expected_impact"):
            sections.append(f"**Expected Impact**\n{parsed['expected_impact']}")
        if parsed.get("next_steps"):
            sections.append(f"**Planned Next Steps**\n{parsed['next_steps']}")

        summary = "\n\n".join(sections)

        # Send message to message queue
        message = self.message_queue.send_message(
            from_agent=agent.subagent_name,
            to_agent="Director",
            content=summary,
            msg_type="action_summary",
            round_num=self.round_num
        )

        # Log conversation message
        self.logger.log_event({
            "event_type": "conversation_message",
            "round": self.round_num,
            "actor": self._actor_for(agent),
            "from": agent.subagent_name,
            "to": "Director",
            "content": summary,
            "message_type": "action_summary",
            "message_id": message["id"],
            "timestamp": message["timestamp"]
        })

        # Send to Unity for display
        await self._send({
            "type": "agent_message",
            "agent_name": agent.subagent_name,
            "talkinghead_endpoint": agent.talkinghead_endpoint,
            "content": summary,
            "message_type": "action_summary",
            "round": self.round_num,
            "timestamp": message["timestamp"]
        })

        print(f"[router] {agent.subagent_name} → Director: {summary[:60]}...")

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

    async def _send_choices_proposal(
        self,
        agent: AgentConfig,
        packages: List[dict],
        filtered_actions: List[dict],
        reasoning: str,
    ):
        """Push a choices_proposal payload to Unity (Task Center render path).

        Used by both the initial proposal in _run_choices and the reproposal
        path so the UI always renders cards through the same select-then-confirm
        machinery (HandleChoicesProposal → multi-agent task → DisplayInteractiveChoice).
        """
        await self._send_proposal(agent, packages, {
            "type": "choices_proposal",
            "agent_name": agent.subagent_name,
            "talkinghead": agent.talkinghead_endpoint,
            "reasoning": reasoning,
            "packages": packages,
            "available_actions": filtered_actions,
            "timestamp": _now(),
        })

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

    async def _repropose_choices(self, agent: AgentConfig):
        """Agent generates new choices based on director feedback.

        Uses the same Unity rendering path as the original proposal: a
        choices_proposal payload routed through HandleChoicesProposal. This
        keeps the select-then-confirm UX and lets _run_choices, which is still
        awaiting _pending_choice, receive the choice_made via the existing flow.
        """
        print(f"[router] {agent.subagent_name} reproposing choices...")

        context = self._choice_context.get(agent.subagent_name)
        if not context:
            print(f"[router] Warning: No stored context for {agent.subagent_name} - cannot repropose")
            return

        filtered_state, filtered_actions, game_state, all_actions = context
        conversation = self.message_queue.get_conversation(agent.subagent_name, "Director")

        raw, packages = await self._query_and_parse_choices(
            agent, filtered_state, filtered_actions, conversation
        )
        packages, reasoning = self._finalize_choice_packages(
            agent, packages, filtered_actions, game_state, raw
        )
        print(f"[router]   Reproposed {len(packages)} packages to director.")

        self.logger.log_event({
            "event_type": "choices_reproposed",
            "round": self.round_num,
            "agent_name": agent.subagent_name,
            "num_packages": len(packages),
            "timestamp": _now(),
        })

        await self._send_choices_proposal(agent, packages, filtered_actions, reasoning)

    def _resolve_construction_site_conflicts(
        self,
        indices: List[int],
        actions: List[dict],
        package_label: str,
    ) -> Tuple[List[int], int]:
        """Resolve same-site construction conflicts in a package.

        Two buildings can't share a site (Unity will fail the second build).
        For each construction action that targets a site already used by an
        earlier action in the same package, try to substitute a sibling action
        of the same ``building_type`` at an unused site. If no alternative
        exists (every site is already taken or no sibling action), the
        offending index is dropped.

        Returns ``(resolved_indices, dropped_count)``. ``dropped_count``
        counts only the actions we *couldn't* salvage by remapping — caller
        uses it to decide whether to mark the package label as partial.
        """
        # Index lookup: (building_type, site_id) -> action index.
        by_building_site: Dict[Tuple[str, int], int] = {}
        for i, a in enumerate(actions):
            atype = a.get('action_type') or a.get('actionType')
            if atype != 'construction':
                continue
            cons = a.get('construction') or {}
            bt = cons.get('building_type')
            sid = cons.get('site_id')
            if bt is None or sid is None:
                continue
            by_building_site[(bt, sid)] = i

        used_sites: set = set()
        kept: List[int] = []
        dropped: int = 0

        for idx in indices:
            action = actions[idx] if 0 <= idx < len(actions) else None
            if action is None:
                continue

            atype = action.get('action_type') or action.get('actionType')
            if atype != 'construction':
                kept.append(idx)
                continue

            cons = action.get('construction') or {}
            building_type = cons.get('building_type')
            site_id = cons.get('site_id')

            # Non-construction or missing site info — pass through.
            if site_id is None:
                kept.append(idx)
                continue

            # No conflict — claim the site.
            if site_id not in used_sites:
                used_sites.add(site_id)
                kept.append(idx)
                continue

            # Conflict — look for the same building type at an unused site.
            remapped = False
            for (alt_bt, alt_sid), alt_idx in by_building_site.items():
                if alt_bt != building_type:
                    continue
                if alt_sid in used_sites:
                    continue
                if alt_idx in kept:
                    continue
                print(f"[{package_label}] ↪️  Remapped {building_type} site {site_id} → {alt_sid} (action {idx} → {alt_idx})")
                kept.append(alt_idx)
                used_sites.add(alt_sid)
                remapped = True
                break

            if not remapped:
                print(f"[{package_label}] ⚠️  Dropped {building_type} at site {site_id}: no alternative site available")
                dropped += 1

        return kept, dropped

    def _validate_action_indices(
        self,
        raw: str,
        actions: List[dict],
        max_actions: int,
        agent_name: str,
    ) -> Tuple[list, list]:
        """
        Parse and validate LLM response for action indices.

        Returns:
            (valid_indices, error_messages)
        """
        errors = []

        # Handle empty/pass response
        if not raw or not raw.strip():
            return [], []

        # Parse comma-separated indices
        indices = []
        raw_tokens = raw.split(",")

        for token in raw_tokens:
            token = token.strip()
            if not token:
                continue

            # Extract first integer from token (handles "0", "Action 0", etc.)
            match = re.search(r'\d+', token)
            if match:
                try:
                    idx = int(match.group())
                    indices.append((idx, token))
                except ValueError:
                    errors.append(f"Could not parse integer from: '{token}'")
            else:
                errors.append(f"No integer found in token: '{token}'")

        # Validate bounds and remove duplicates
        valid_indices = []
        seen = set()

        for idx, original_token in indices:
            if idx < 0 or idx >= len(actions):
                errors.append(
                    f"Index {idx} out of bounds (valid: 0-{len(actions)-1})"
                )
            elif idx in seen:
                errors.append(f"Duplicate index {idx} removed")
            else:
                valid_indices.append(idx)
                seen.add(idx)

        # Enforce max_actions limit
        if len(valid_indices) > max_actions:
            truncated = valid_indices[max_actions:]
            valid_indices = valid_indices[:max_actions]
            errors.append(
                f"Truncated to {max_actions} actions (removed indices: {truncated})"
            )

        # Log validation results
        if errors:
            print(f"[{agent_name}] ⚠️  Validation warnings:")
            for error in errors:
                print(f"[{agent_name}]     - {error}")

        return valid_indices, errors

    def _parse_csv_response(
        self,
        raw: str,
        actions: List[dict],
        max_actions: int,
    ) -> list:
        """Parse LLM CSV response into valid action indices (legacy wrapper)."""
        # Call new validation function (agent_name not available in this context)
        indices, _ = self._validate_action_indices(raw, actions, max_actions, "?")
        return indices

    def _extract_reasoning(self, raw: str) -> str:
        """Extract REASONING line from structured LLM response."""
        lines = raw.strip().split("\n")
        for line in lines:
            if line.strip().startswith("REASONING:"):
                return line.split(":", 1)[1].strip()
        # Fallback: return first non-empty line or truncated raw response
        for line in lines:
            if line.strip():
                return line.strip()[:200]
        return raw[:200]

    def _parse_auto_response(self, raw: str) -> dict:
        """
        Parse auto agent response into sectioned rationale.

        Expected format (each header on its own line, sections may span lines):
            ACTIONS: 0,3,5
            REASONING: ...
            EXPECTED_IMPACT: ...
            NEXT_STEPS: ...

        Returns dict: {actions_str, reasoning, expected_impact, next_steps}.
        Missing sections fall back to sensible defaults.
        """
        section_headers = ["ACTIONS", "REASONING", "EXPECTED_IMPACT", "NEXT_STEPS"]
        sections = {h: "" for h in section_headers}
        current = None

        for raw_line in raw.split("\n"):
            line = raw_line.strip()
            matched = False
            for h in section_headers:
                prefix = f"{h}:"
                if line.startswith(prefix):
                    current = h
                    sections[h] = line[len(prefix):].strip()
                    matched = True
                    break
            if not matched and current and line:
                sections[current] = (sections[current] + " " + line).strip()

        # Treat the whole response as a comma-list if the LLM skipped the ACTIONS header.
        if not sections["ACTIONS"]:
            sections["ACTIONS"] = raw.strip()

        if not sections["REASONING"]:
            sections["REASONING"] = "Executed selected actions based on current priorities."

        return {
            "actions_str": sections["ACTIONS"],
            "reasoning": sections["REASONING"],
            "expected_impact": sections["EXPECTED_IMPACT"],
            "next_steps": sections["NEXT_STEPS"],
        }

    def _extract_coach_situation(self, raw: str) -> str:
        """Extract SITUATION line from coach response."""
        lines = raw.strip().split("\n")
        for line in lines:
            if line.strip().startswith("SITUATION:"):
                return line.split(":", 1)[1].strip()
        return "No situation analysis provided."

    def _extract_coach_analysis(self, raw: str) -> str:
        """Extract ANALYSIS line from coach response."""
        lines = raw.strip().split("\n")
        for line in lines:
            if line.strip().startswith("ANALYSIS:"):
                return line.split(":", 1)[1].strip()
        return "No analysis provided."

    def _parse_coach_response(
        self,
        raw: str,
        actions: List[dict],
        num_turns: int,
        max_per_turn: int,
    ) -> list:
        """
        Parse coach LLM response into turn recommendations.
        Expected format:
            SITUATION: [analysis]
            ANALYSIS: [problems/opportunities]
            RECOMMENDATION:
            TURN1: [indices] | [rationale]
            TURN2: [indices] | [rationale]
            TURN3: [indices] | [rationale]
        """
        if not raw or not raw.strip():
            return []

        recommendations = []
        lines = raw.strip().split("\n")

        # Find TURN lines
        turn_lines = [line for line in lines if line.strip().startswith("TURN")]

        for turn_idx, line in enumerate(turn_lines[:num_turns]):
            # Parse: "TURN1: 0,2,5 | Build shelters for housing shortage"
            parts = line.split(":", 1)
            if len(parts) < 2:
                continue

            content = parts[1].strip()
            segments = content.split("|")

            if len(segments) < 2:
                # No rationale, just indices
                indices_str = content
                rationale = ""
            else:
                indices_str = segments[0].strip()
                rationale = segments[1].strip()

            # Parse and validate action indices
            indices, errors = self._validate_action_indices(
                indices_str, actions, max_per_turn, f"TURN{turn_idx+1}"
            )

            if not indices:
                print(f"[coach] ⚠️  Turn {turn_idx+1} has no valid indices, skipping")
                continue

            # Build action descriptions
            action_list = [actions[i].get("description", "?") for i in indices]

            recommendations.append({
                "turn_index": turn_idx + 1,
                "turn_label": f"Turn {turn_idx + 1}",
                "rationale": rationale,
                "action_indices": indices,
                "action_descriptions": action_list,
            })

        return recommendations

    def _parse_packages_response(
        self,
        raw: str,
        actions: List[dict],
        num_choices: int,
        max_per_package: int,
    ) -> list:
        """
        Parse LLM response into choice packages.
        Expected format (v2 - structured):
            REASONING: [explanation]
            PACKAGE1: [name] | [indices] | [outcome]
            PACKAGE2: [name] | [indices] | [outcome]

        Fallback format (v1 - semicolon-separated):
            0,2,5;1,3,7;4,6,8
        """
        if not raw or not raw.strip():
            return []

        packages = []
        lines = raw.strip().split("\n")

        # Try to parse structured format (v2)
        package_lines = [line for line in lines if line.strip().startswith("PACKAGE")]

        if package_lines:
            # Structured format detected
            for pkg_idx, line in enumerate(package_lines[:num_choices]):
                # Parse: "PACKAGE1: Strategy Name | 0,2,5 | Outcome description"
                parts = line.split(":", 1)
                if len(parts) < 2:
                    continue

                content = parts[1].strip()
                segments = content.split("|")

                if len(segments) < 2:
                    continue

                strategy_name = segments[0].strip()
                indices_str = segments[1].strip()
                outcome = segments[2].strip() if len(segments) > 2 else ""
                rationale = segments[3].strip() if len(segments) > 3 else ""

                # Parse and validate action indices
                indices, errors = self._validate_action_indices(
                    indices_str, actions, max_per_package, f"PKG{pkg_idx+1}"
                )

                if not indices:
                    print(f"[choices] ⚠️  Package {pkg_idx+1} has no valid indices, skipping")
                    continue

                indices, dropped = self._resolve_construction_site_conflicts(
                    indices, actions, f"PKG{pkg_idx+1}"
                )

                # Build description: "Outcome | Action1, Action2, ..."
                action_list = ", ".join([actions[i].get("description", "?") for i in indices])
                if outcome:
                    description = f"{outcome}\n{action_list}"
                else:
                    description = action_list

                # Mark the label as partial only when we had to drop actions
                # (remapping preserved the strategy intent, dropping did not).
                label = strategy_name or f"Option {pkg_idx + 1}"
                if dropped > 0:
                    label = f"{label} [partial]"

                packages.append({
                    "package_index": pkg_idx,
                    "label": label,
                    "description": description,
                    "rationale": rationale,
                    "confidence": 0.8,
                    "action_indices": indices,
                })
        else:
            # Try semicolon-separated format (v1 fallback)
            package_texts = raw.split(";")

            for pkg_idx, pkg_text in enumerate(package_texts[:num_choices]):
                # Parse and validate action indices
                indices, errors = self._validate_action_indices(
                    pkg_text, actions, max_per_package, f"PKG{pkg_idx+1}"
                )

                if not indices:
                    print(f"[choices] ⚠️  Package {pkg_idx+1} has no valid indices, skipping")
                    continue

                indices, dropped = self._resolve_construction_site_conflicts(
                    indices, actions, f"PKG{pkg_idx+1}"
                )

                # Generate package description from action descriptions
                descriptions = [actions[i].get("description", "?") for i in indices]
                description = ", ".join(descriptions)

                label = f"Option {pkg_idx + 1}"
                if dropped > 0:
                    label = f"{label} [partial]"

                packages.append({
                    "package_index": pkg_idx,
                    "label": label,
                    "description": description,
                    "confidence": 0.8,
                    "action_indices": indices,
                })

        return packages

    async def _query_and_parse_choices(
        self,
        agent: AgentConfig,
        filtered_state: dict,
        filtered_actions: List[dict],
        conversation: list,
    ) -> Tuple[str, list]:
        """Query the LLM for choice packages, re-querying up to choices_max_retries
        times if the parse yields fewer than choices_min_packages VALID packages
        (the common empty/malformed-response failure). Returns (raw, packages) from
        the best attempt so far. The deterministic fallback in _finalize_choice_packages
        is the hard guarantee; this just gives the model another shot first."""
        num_choices = agent.num_choices or 3
        max_per_package = agent.max_actions_per_package or 4
        min_pkgs = max(1, agent.choices_min_packages)

        raw = await asyncio.to_thread(query_llm, filtered_state, filtered_actions, agent, conversation)
        packages = self._parse_packages_response(raw, filtered_actions, num_choices, max_per_package)

        attempts = 0
        while len(dedupe_packages(packages)) < min_pkgs and attempts < agent.choices_max_retries:
            attempts += 1
            print(f"[choices] ⚠️  only {len(packages)} valid package(s) (< {min_pkgs}); "
                  f"retry {attempts}/{agent.choices_max_retries}")
            retry_raw = await asyncio.to_thread(query_llm, filtered_state, filtered_actions, agent, conversation)
            retry_pkgs = self._parse_packages_response(retry_raw, filtered_actions, num_choices, max_per_package)
            # Keep whichever attempt produced more valid packages.
            if len(retry_pkgs) > len(packages):
                raw, packages = retry_raw, retry_pkgs
            if len(dedupe_packages(packages)) >= min_pkgs:
                break

        return raw, packages

    def _finalize_choice_packages(
        self,
        agent: AgentConfig,
        packages: list,
        filtered_actions: List[dict],
        game_state: dict,
        raw: str,
    ) -> Tuple[list, str]:
        """Apply the reliability + explainability layer to parsed packages.

        Order: dedupe -> grounded per-package explanations -> deterministic
        fallback fill -> contiguous reindex -> grounded pre-choices summary.
        Each step is gated on the agent's opt-in flags (see agent_config.py), so
        with all flags off this is just a dedupe + reindex passthrough.
        Returns (packages, reasoning) ready for _send_choices_proposal.
        """
        reasoning = self._extract_reasoning(raw)

        packages = dedupe_packages(packages)
        # Drop near-duplicate strategies (same plan at a different spend level) so the
        # human sees genuinely different bets; the fallback below refills distinct
        # archetypes for any slot this frees up.
        before_div = len(packages)
        packages = enforce_diversity(packages, filtered_actions)
        if len(packages) < before_div:
            print(f"[choices] diversity guard dropped {before_div - len(packages)} "
                  f"near-duplicate package(s)")
        if agent.explain_grounded:
            packages = apply_grounded_explanations(packages, filtered_actions, game_state)

        num_choices = agent.num_choices or 3
        if agent.choices_fallback and len(packages) < num_choices:
            before = len(packages)
            packages = build_fallback_packages(
                packages, filtered_actions, game_state,
                num_choices=num_choices,
                max_per_package=agent.max_actions_per_package or 4,
            )
            if len(packages) > before:
                print(f"[choices] fallback filled {len(packages) - before} package(s) "
                      f"({before} from LLM, {len(packages)} total)")

        for n, p in enumerate(packages):
            p["package_index"] = n

        if agent.explain_summary:
            reasoning = compose_summary(reasoning, packages, filtered_actions, game_state)

        # Discoverability: tell the director they can chat to request a fresh set.
        # Independent of explain_summary so the nudge rides with the proposal either way.
        if agent.choices_repropose_hint:
            reasoning = append_repropose_hint(reasoning)

        return packages, reasoning

    def _update_conv_history(
        self,
        agent: AgentConfig,
        state: dict,
        actions: List[dict],
        raw_response: str,
    ):
        agent.conversation_history.append({
            "user": f"State: {json.dumps(state)[:200]}... Actions: {len(actions)} available.",
            "assistant": raw_response,
        })

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
    ):
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
            conv_history_length=len(agent.conversation_history),
            tokens_used=tokens,
            # Pass the post-execution state so the logger can route reward through
            # the shared gym scorer (game_state_after["rewardMetrics"]).
            game_state_after=game_state_after,
        )


# ── Utilities ────────────────────────────────────────────────────

def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


def _get_satisfaction(state: dict) -> float:
    return state.get("satisfactionAndBudget", {}).get("satisfaction", 0)


def _get_budget(state: dict) -> float:
    return state.get("satisfactionAndBudget", {}).get("budget", 0)


# ── Multi-tenant Service ─────────────────────────────────────────

app = FastAPI()


class AgentService:
    """Process-wide service state: API keys, config catalog, live sessions."""

    def __init__(self, keys: Dict[str, dict], config_dir: Path, log_dir: Path):
        self.keys = keys                      # api_key -> {label, ...}
        self.config_dir = config_dir
        self.log_dir = log_dir
        self.sessions: Dict[str, Session] = {}  # session_id -> Session
        self.started_at = datetime.now(timezone.utc)

    def sessions_by_label(self) -> Dict[str, int]:
        """Live session count grouped by API-key label (for monitoring)."""
        counts: Dict[str, int] = {}
        for s in self.sessions.values():
            counts[s.api_key_label] = counts.get(s.api_key_label, 0) + 1
        return counts

    def label_for(self, api_key: str) -> Optional[str]:
        meta = self.keys.get(api_key)
        return meta.get("label") if meta else None

    def allowed_configs_for(self, api_key: str) -> Optional[set]:
        """Configs this key may use. Returns None when unrestricted (no
        ``configs`` list on the key → all configs allowed)."""
        meta = self.keys.get(api_key) or {}
        cfgs = meta.get("configs")
        if not cfgs:
            return None
        return set(cfgs)

    def list_configs(self) -> List[dict]:
        """Return public-facing config descriptors derived from filesystem."""
        out: List[dict] = []
        for path in sorted(self.config_dir.glob("*.json")):
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
                        "path": path.name, "agents": agents})
        return out

    def resolve_config(self, name: str) -> Optional[Path]:
        """Map a config name (without .json) to its path under config_dir."""
        candidate = self.config_dir / f"{name}.json"
        if candidate.exists():
            return candidate
        # Allow callers to pass an explicit relative or absolute path too.
        as_path = Path(name)
        if as_path.is_absolute() and as_path.exists():
            return as_path
        return None

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
        return {k: (v if isinstance(v, dict) else {"label": str(v)}) for k, v in data.items()}

    env = os.environ.get("ARC_API_KEYS")
    if env:
        try:
            data = json.loads(env)
            if isinstance(data, dict):
                return {k: (v if isinstance(v, dict) else {"label": str(v)})
                        for k, v in data.items()}
        except json.JSONDecodeError:
            pass
        out: Dict[str, dict] = {}
        for i, key in enumerate(s.strip() for s in env.split(",") if s.strip()):
            out[key] = {"label": f"user{i+1}"}
        return out

    # Dev fallback for local testing.
    dev_key = "dev-local-key"
    print(f"[router] No --keys-file or ARC_API_KEYS env; accepting dev key '{dev_key}'")
    return {dev_key: {"label": "dev"}}


def _bearer_to_key(auth: Optional[str]) -> Optional[str]:
    """Pull the key out of an ``Authorization: Bearer <key>`` header."""
    if not auth:
        return None
    parts = auth.strip().split(None, 1)
    if len(parts) == 2 and parts[0].lower() == "bearer":
        return parts[1].strip()
    return None


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


@app.get("/configs")
async def list_configs(authorization: Optional[str] = Header(default=None)):
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    key = _bearer_to_key(authorization)
    if key is None or key not in service.keys:
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    configs = service.list_configs()
    allowed = service.allowed_configs_for(key)
    if allowed is not None:
        configs = [c for c in configs if c["name"] in allowed]
    return {"configs": configs}


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
    # Optional client-supplied persistent player id (localStorage UUID). It is
    # UNTRUSTED input: sanitize to a bounded safe charset and only ever store it
    # as a log VALUE, never as a path component. Absent/blank -> None (anonymous).
    raw_pid = msg.get("player_id")
    player_id = None
    if isinstance(raw_pid, str):
        player_id = re.sub(r"[^A-Za-z0-9_-]", "", raw_pid)[:64] or None
    if not api_key or api_key not in service.keys:
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
    service.sessions[session_id] = session

    # Catalogue this game under the user (per-key index) and stamp a
    # session_start header at the top of the session's own log.
    key_fp = hashlib.sha256(api_key.encode("utf-8")).hexdigest()[:12]
    service.record_session(key_label, key_fp, session_id, config_name, log_path,
                           player_id=player_id)
    session.logger.log_event({
        "event_type": "session_start",
        "session_id": session_id,
        "label": key_label,
        "key_fingerprint": key_fp,
        "player_id": player_id,
        "config": config_name,
        "agents": [a.subagent_name for a in cfg.agents],
    })

    await websocket.send_text(json.dumps({
        "type": "hello_ack",
        "session_id": session_id,
        "config": config_name,
        "agents": [a.subagent_name for a in cfg.agents],
        "label": key_label,
        "player_id": player_id,
    }))
    print(f"[router] hello_ack -> {key_label} (session {session_id[:8]}, "
          f"config={config_name}, agents={len(cfg.agents)})")
    return session


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

    uvicorn.run(app, host="0.0.0.0", port=args.port, log_level="info")


if __name__ == "__main__":
    main()
