"""
ARC Game Multi-Agent Router.

Orchestrates subagents and director each round:
  1. Receives begin_round from Unity
  2. Queries each subagent LLM in turn-order with filtered state/actions
  3. Executes auto agent actions via Unity WebSocket
  4. Sends choices_proposal for choices agents, waits for choice_made
  5. Signals director_turn to Unity
  6. Logs everything to episode_log.jsonl

Usage:
    python agent_router.py --config config/agents_config.json \
                           --port 8000 \
                           --log episode_log.jsonl
"""
from __future__ import annotations

import asyncio
import json
import argparse
import os
import sys
from datetime import datetime, timezone
from typing import List, Tuple, Optional

from fastapi import FastAPI, WebSocket
from fastapi.responses import JSONResponse
import uvicorn

from agent_config import AgentConfig, RouterConfig, load_config
from agent_filters import filter_observation, filter_actions
from agent_ordering import get_agent_order
from episode_logger import EpisodeLogger
from llm_query import query_llm
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


class AgentRouter:
    def __init__(self, config: RouterConfig, log_path: str = "episode_log.jsonl"):
        self.config = config
        self.logger = EpisodeLogger(log_path)
        self.message_queue = MessageQueue()  # Conversation management
        self.episode_id: str = self.logger.new_episode()
        self.round_num: int = 0
        self._websocket: Optional[WebSocket] = None  # set when Unity connects
        self._pending_choice: Optional[asyncio.Future] = None  # asyncio.Future when waiting for choice_made
        self._pending_action: Optional[asyncio.Future] = None  # asyncio.Future when waiting for action result

    # ── WebSocket Handler ────────────────────────────────────────

    async def handle_websocket(self, websocket: WebSocket):
        """Handle WebSocket connection from Unity."""
        await websocket.accept()
        self._websocket = websocket
        print("[router] Unity connected. Waiting for begin_round...")

        try:
            while True:
                raw_msg = await websocket.receive_text()
                await self._handle_message(raw_msg)
        except Exception as e:
            print(f"[router] WebSocket error: {e}")
        finally:
            self._websocket = None
            print("[router] Unity disconnected.")

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

        if msg_type == "begin_round" or msg_type == "request_agent_decision":
            # request_agent_decision is the old message type from original LLM integration
            # Run as background task so receive loop stays active for choice_made messages
            asyncio.create_task(self._handle_begin_round(msg))
        elif msg_type == "choice_made":
            await self._handle_choice_made(msg)
        elif msg_type == "director_message":
            await self._handle_director_message(msg)
        elif msg_type == "request_reproposal":
            await self._handle_request_reproposal(msg)
        elif msg_type == "round_end":
            self._handle_round_end(msg)
        else:
            print(f"[router] Unknown message type: {msg_type}")

    # ── Round Orchestration ──────────────────────────────────────

    async def _handle_begin_round(self, msg: dict):
        self.round_num += 1
        game_state = msg.get("game_state", {})
        print(f"\n[router] === Round {self.round_num} | "
              f"Day {msg.get('day', 1)} Seg {msg.get('segment', 0)} ===")

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
        raw = query_llm(filtered_state, filtered_actions, agent, conversation)

        # Validate LLM response
        indices, validation_errors = self._validate_action_indices(
            raw, filtered_actions,
            max_actions=agent.max_actions_per_package or len(filtered_actions),
            agent_name=agent.subagent_name
        )
        print(f"[{agent.subagent_name}] LLM chose indices: {indices} (raw: '{raw}')")

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

        # Post action summary to director
        await self._post_auto_summary(agent, results)

        return game_state, all_actions

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
        raw = query_llm(filtered_state, filtered_actions, agent, conversation)
        packages = self._parse_packages_response(
            raw, filtered_actions,
            num_choices=agent.num_choices or 3,
            max_per_package=agent.max_actions_per_package or 4,
        )
        print(f"[router]   Proposing {len(packages)} packages to director.")

        # Extract reasoning from structured response
        reasoning = self._extract_reasoning(raw)

        sat_before = _get_satisfaction(game_state)
        budget_before = _get_budget(game_state)

        await self._send({
            "type": "choices_proposal",
            "agent_name": agent.subagent_name,
            "talkinghead": agent.talkinghead_endpoint,
            "reasoning": reasoning,
            "packages": packages,
            "available_actions": filtered_actions,  # Full action objects for Unity to execute
            "timestamp": _now(),
        })

        # Wait for choice_made from Unity
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
        raw = query_llm(filtered_state, filtered_actions, agent, conversation)

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
            "from": "Director",
            "to": to_agent_name,
            "content": content,
            "message_type": "director_message",
            "message_id": message["id"],
            "timestamp": message["timestamp"]
        })

        # Find the agent config
        agent = self._get_agent_by_name(to_agent_name)
        if not agent:
            print(f"[router] Agent '{to_agent_name}' not found")
            return

        # Generate immediate response from the agent
        conversation = self.message_queue.get_conversation(to_agent_name, "Director")
        response_text = self._generate_conversational_response(agent, conversation)

        print(f"[router] {to_agent_name} → Director: {response_text[:50]}...")

        # Store agent response in queue
        response_message = self.message_queue.send_message(
            from_agent=to_agent_name,
            to_agent="Director",
            content=response_text,
            msg_type="agent_response",
            round_num=self.round_num
        )

        # Log agent response
        self.logger.log_event({
            "event_type": "conversation_message",
            "round": self.round_num,
            "from": to_agent_name,
            "to": "Director",
            "content": response_text,
            "message_type": "agent_response",
            "message_id": response_message["id"],
            "timestamp": response_message["timestamp"]
        })

        # Send agent response to Unity for display
        await self._send({
            "type": "agent_message",
            "agent_name": to_agent_name,
            "talkinghead_endpoint": agent.talkinghead_endpoint,
            "content": response_text,
            "message_type": "agent_response",
            "round": self.round_num,
            "timestamp": response_message["timestamp"]
        })

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
        """Find agent by subagent_name."""
        for agent in self.config.agents:
            if agent.subagent_name == agent_name:
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

        # Create system prompt
        system_prompt = f"""You are {agent.subagent_name}, an AI agent helping manage disaster response in the ARC Game.

You are having a conversation with the Director (the human player). Respond naturally and helpfully to their messages.

Keep your responses concise and focused. You can discuss:
- Your recent actions and decisions
- Your understanding of the current situation
- Questions or clarifications about the disaster response
- Coordination with other agents"""

        if agent.system_prompt:
            system_prompt += f"\n\nAdditional context: {agent.system_prompt}"

        # Query the LLM
        try:
            if provider == "anthropic":
                api_key = os.environ.get("ANTHROPIC_API_KEY")
                if not api_key:
                    return "I'm unable to respond right now - API key not configured."

                client = anthropic.Anthropic(api_key=api_key)
                response = client.messages.create(
                    model=agent.llm_model or "claude-sonnet-4-6",
                    max_tokens=500,
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
                    max_tokens=500,
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

        results = {
            'executed': [],
            'skipped': [],
            'errors': []
        }

        current_state = initial_state

        for idx in action_indices:
            action = valid_actions[idx]
            action_cost = action.get('cost', 0)
            action_type = action.get('actionType', 'unknown')
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
                else:
                    error_msg = result.get('error_message', 'Unknown error')
                    results['errors'].append({'index': idx, 'error': error_msg})
                    print(f"[{agent_name}]   ✗ Action {idx} failed: {error_msg}")

            except Exception as e:
                msg = f"Exception during execution: {e}"
                results['errors'].append({'index': idx, 'error': str(e)})
                print(f"[{agent_name}]   ✗ Action {idx} exception: {e}")

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

    def _filter_state(self, game_state: dict, agent: AgentConfig) -> dict:
        return filter_observation(game_state, agent.subobservation_space)

    async def _post_auto_summary(self, agent: AgentConfig, results: dict):
        """Post conversational summary after auto agent executes actions."""
        executed_count = len(results.get("executed", []))
        skipped_count = len(results.get("skipped", []))

        # Generate summary message
        if executed_count > 0:
            executed_actions = [item["action"]["description"] for item in results.get("executed", [])]
            summary = f"I executed {executed_count} action(s): {', '.join(executed_actions[:3])}"
            if executed_count > 3:
                summary += f" and {executed_count - 3} more"
        else:
            summary = "I didn't execute any actions this turn"

        if skipped_count > 0:
            summary += f" ({skipped_count} action(s) were invalid/skipped)"

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

    async def _repropose_choices(self, agent: AgentConfig):
        """Agent generates new choices based on director feedback."""
        print(f"[router] {agent.subagent_name} reproposing choices...")

        # Get conversation including director's feedback
        conversation = self.message_queue.get_conversation(agent.subagent_name, "Director")

        # Need current game state and actions - store these when choices are first proposed
        # For now, re-query with conversation context
        # TODO: Store filtered_state and filtered_actions when first proposing choices

        # This is a simplified version - in production, we'd need to store the game state
        # when choices were first proposed and reuse it here
        print(f"[router] Note: Reproposal needs game state context - not yet fully implemented")

        # Post message about reproposal
        message = self.message_queue.send_message(
            from_agent=agent.subagent_name,
            to_agent="Director",
            content="Based on your feedback, let me generate revised choices...",
            msg_type="choice_revision",
            round_num=self.round_num
        )

        # Log message
        self.logger.log_event({
            "event_type": "conversation_message",
            "round": self.round_num,
            "from": agent.subagent_name,
            "to": "Director",
            "content": message["content"],
            "message_type": "choice_revision",
            "message_id": message["id"],
            "timestamp": message["timestamp"]
        })

        # Send to Unity
        await self._send({
            "type": "agent_message",
            "agent_name": agent.subagent_name,
            "talkinghead_endpoint": agent.talkinghead_endpoint,
            "content": message["content"],
            "message_type": "choice_revision",
            "round": self.round_num,
            "timestamp": message["timestamp"]
        })

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

                # Parse and validate action indices
                indices, errors = self._validate_action_indices(
                    indices_str, actions, max_per_package, f"PKG{pkg_idx+1}"
                )

                if not indices:
                    print(f"[choices] ⚠️  Package {pkg_idx+1} has no valid indices, skipping")
                    continue

                # Build description: "Outcome | Action1, Action2, ..."
                action_list = ", ".join([actions[i].get("description", "?") for i in indices])
                if outcome:
                    description = f"{outcome}\n{action_list}"
                else:
                    description = action_list

                packages.append({
                    "package_index": pkg_idx,
                    "label": strategy_name or f"Option {pkg_idx + 1}",
                    "description": description,
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

                # Generate package description from action descriptions
                descriptions = [actions[i].get("description", "?") for i in indices]
                description = ", ".join(descriptions)

                packages.append({
                    "package_index": pkg_idx,
                    "label": f"Option {pkg_idx + 1}",
                    "description": description,
                    "confidence": 0.8,
                    "action_indices": indices,
                })

        return packages

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


# ── FastAPI Server Setup ─────────────────────────────────────────

app = FastAPI()
router_instance: Optional[AgentRouter] = None


@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    """WebSocket endpoint for Unity to connect to."""
    global router_instance
    if router_instance:
        await router_instance.handle_websocket(websocket)
    else:
        await websocket.close(code=1011, reason="Router not initialized")


@app.get("/")
async def root():
    return {"status": "ARC Game Multi-Agent Router", "version": "1.0"}


@app.get("/health")
async def health():
    return {"status": "healthy", "router_active": router_instance is not None}


def main():
    global router_instance

    parser = argparse.ArgumentParser(description="ARC Game Multi-Agent Router")
    parser.add_argument("--config", default="config/agents_config.json",
                        help="Path to agents_config.json")
    parser.add_argument("--port", type=int, default=8000,
                        help="Port to listen on for Unity connections")
    parser.add_argument("--log", default="episode_log.jsonl",
                        help="Episode log output path")
    args = parser.parse_args()

    # Load config and create router instance
    config = load_config(args.config)
    router_instance = AgentRouter(config=config, log_path=args.log)

    print(f"[router] Starting server on port {args.port}...")
    print(f"[router] Loaded {len(config.agents)} agents from {args.config}")
    print(f"[router] Waiting for Unity to connect at ws://localhost:{args.port}/ws")

    # Start FastAPI server
    uvicorn.run(app, host="0.0.0.0", port=args.port, log_level="info")


if __name__ == "__main__":
    main()
