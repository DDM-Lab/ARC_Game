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
import sys
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Tuple, Optional

from fastapi import FastAPI, WebSocket, Header, HTTPException
from fastapi.responses import JSONResponse
from fastapi.middleware.cors import CORSMiddleware
import uvicorn

from agent_config import AgentConfig, RouterConfig, load_config
from agent_filters import filter_observation, filter_actions
from agent_ordering import get_agent_order
from episode_logger import EpisodeLogger
from llm_query import query_llm
from choices_reliability import (
    dedupe_packages,
    enforce_diversity,
    apply_grounded_explanations,
    build_fallback_packages,
    compose_summary,
)
from message_queue import MessageQueue
import re


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
        return enumerator.enumerate_all_actions()
    except ImportError:
        print("[router] action_enumerator not available — action list empty.")
        return []
    except Exception as e:
        print(f"[router] action_enumerator error: {e}")
        return []


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
        self._choice_context: dict = {}
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

        # Run each subagent
        for agent in ordered:
            game_state, all_actions = await self._run_subagent(
                agent, game_state, all_actions
            )

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
        results = await self._execute_validated_actions(
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
                self._pending_action = None

                exec_results.append({
                    "action_id": action.get("action_id", "unknown"),
                    "success": result_msg.get("success", False),
                    "error_message": result_msg.get("error_message", "")
                })

                # Update game state from result
                if "game_state" in result_msg:
                    game_state = result_msg["game_state"]

            except asyncio.TimeoutError:
                print(f"[router]   ⚠️  Timeout executing action {action.get('action_id', 'unknown')}")
                exec_results.append({
                    "action_id": action.get("action_id", "unknown"),
                    "success": False,
                    "error_message": "Timeout waiting for Unity execution"
                })

        return exec_results, game_state

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

        # Check if director is autonomous or manual
        if self._director_agent and self._director_agent.actor_type == "auto":
            # Autonomous director: Query LLM to select package
            print(f"[router]   🤖 Autonomous director selecting package...")
            selected_idx = await self._autonomous_director_select(packages, game_state, reasoning)
            print(f"[router]   ✅ Director selected package {selected_idx}")

            # Execute selected package via Unity
            if selected_idx is not None and 0 <= selected_idx < len(packages):
                selected_package = packages[selected_idx]
                action_indices = selected_package["action_indices"]
                actions_to_execute = [filtered_actions[i] for i in action_indices if i < len(filtered_actions)]

                exec_results, game_state = await self._execute_actions_via_unity(actions_to_execute, game_state)
            else:
                print(f"[router]   ⚠️  Invalid package index {selected_idx}, skipping execution")
                selected_idx = None
                exec_results = []
        else:
            # Manual director: Wait for choice_made from Unity
            # Timeout set to 5 minutes for human director decision time
            loop = asyncio.get_event_loop()
            self._pending_choice = loop.create_future()
            print(f"[router]   ⏳ Waiting for director to select a package (5min timeout)...")

            try:
                choice_msg = await asyncio.wait_for(self._pending_choice, timeout=300.0)
                self._pending_choice = None
                print(f"[router]   ✅ Received director choice!")

                selected_idx = choice_msg.get("package_index", 0)
                exec_results = choice_msg.get("execution_results", [])
                game_state = choice_msg.get("game_state", game_state)
            except asyncio.TimeoutError:
                print(f"[router]   ⚠️  Timeout (5min) waiting for choice_made from {agent.subagent_name}")
                print(f"[router]   Skipping - no action taken.")
                self._pending_choice = None
                selected_idx = None
                exec_results = []

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
        print("[router] 🆕 game_start received — message queue cleared, round counter reset.")

    async def _handle_director_message(self, msg: dict):
        """Handle conversational message from director to an agent."""
        to_agent_name = msg.get("to_agent")
        content = msg.get("content", "")

        if not to_agent_name or not content:
            print(f"[router] Invalid director_message: missing to_agent or content")
            return

        print(f"[router] Director → {to_agent_name}: {content[:50]}...")

        # Store director message in queue
        message = self.message_queue.send_message(
            from_agent="Director",
            to_agent=to_agent_name,
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
            "to": to_agent_name,
            "content": content,
            "message_type": "director_message",
            "message_id": message["id"],
            "click_seq": msg.get("click_seq"),
            "timestamp": message["timestamp"]
        })

        # Find the agent config
        agent = self._get_agent_by_name(to_agent_name)
        if not agent:
            print(f"[router] Agent '{to_agent_name}' not found")
            return

        conversation = self.message_queue.get_conversation(to_agent_name, "Director")

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
            "messaged you. Decide EXACTLY ONE response path:\n\n"
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
        if self._websocket:
            await self._websocket.send_text(json.dumps(payload))

    async def _execute_action(self, agent_name: str, action: dict) -> Tuple[dict, dict]:
        """Send execute_action to Unity, wait for result via Future, return (result, updated_state)."""
        await self._send({
            "type": "execute_action",
            "agent_name": agent_name,
            "action": action,
            "timestamp": _now(),
        })

        # Wait for action result via Future (delivered by message handler)
        loop = asyncio.get_event_loop()
        self._pending_action = loop.create_future()

        try:
            result = await asyncio.wait_for(self._pending_action, timeout=10.0)
            self._pending_action = None
        except asyncio.TimeoutError:
            print(f"[router]   ⚠️  Timeout waiting for action result")
            self._pending_action = None
            return {"success": False, "error_message": "Timeout"}, {}

        game_state = result.get("game_state", {})
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

        return results

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
        return filter_observation(game_state, agent.subobservation_space)

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

    async def _send_choices_proposal(
        self,
        agent: AgentConfig,
        packages: List[dict],
        filtered_actions: List[dict],
        reasoning: str,
    ):
        """Push a choices_proposal payload to Unity.

        Used by both the initial proposal in _run_choices and the reproposal
        path so the UI always renders cards through the same select-then-confirm
        machinery (HandleChoicesProposal → multi-agent task → DisplayInteractiveChoice).
        """
        await self._send({
            "type": "choices_proposal",
            "agent_name": agent.subagent_name,
            "talkinghead": agent.talkinghead_endpoint,
            "reasoning": reasoning,
            "packages": packages,
            "available_actions": filtered_actions,
            "timestamp": _now(),
        })

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
                       config_name: str, log_file: str) -> None:
        """Append a one-line manifest entry to the user's session index so
        every game a user plays is catalogued (id, config, log file, time)."""
        entry = {
            "session_id": session_id,
            "label": key_label,
            "key_fingerprint": key_fp,
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
    service.sessions[session_id] = session

    # Catalogue this game under the user (per-key index) and stamp a
    # session_start header at the top of the session's own log.
    key_fp = hashlib.sha256(api_key.encode("utf-8")).hexdigest()[:12]
    service.record_session(key_label, key_fp, session_id, config_name, log_path)
    session.logger.log_event({
        "event_type": "session_start",
        "session_id": session_id,
        "label": key_label,
        "key_fingerprint": key_fp,
        "config": config_name,
        "agents": [a.subagent_name for a in cfg.agents],
    })

    await websocket.send_text(json.dumps({
        "type": "hello_ack",
        "session_id": session_id,
        "config": config_name,
        "agents": [a.subagent_name for a in cfg.agents],
        "label": key_label,
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
