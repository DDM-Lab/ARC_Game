"""Messages between the Director and the officers: delivering them, and replying when no game is running yet.

A mixin of router.session.Session; its methods use the Session's state."""
from __future__ import annotations

import asyncio
import os


from router.config import AgentConfig
from cora.tools import TOOLS
# The typed action tools (build/hire/train/staff/deconstruct/task/transfer) the officer emits;
# Session._execute_calls runs them through cora.executor.
_CORA_ACTION_TOOLS = {t["name"] for t in TOOLS}


def _num(v, default=0):
    """A number for $-formatting; anything else formats as `default`."""
    return v if isinstance(v, (int, float)) else default
from router.common import _strip_self_label, HUMAN_DIRECTOR_ACTOR


class DirectorMessagingMixin:

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
