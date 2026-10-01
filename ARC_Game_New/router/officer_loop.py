"""The officers' turn loop: when each officer runs, the messages it is shown, its transcript, and the hooks around it.

A mixin of router.session.Session; its methods use the Session's state."""
from __future__ import annotations

import asyncio
import json
import hashlib
import uuid
from typing import List, Tuple, Optional


from router.config import AgentConfig
from router.scope import filter_actions
from router.config import load_global_prompt
from router.officer_llm import (build_tools, run_tool_step, DEFAULT_TOOLS,
                              known_ctx_limit, _est_prompt_tokens)
from cora.tools import TOOLS
# The typed action tools (build/hire/train/staff/deconstruct/task/transfer) the officer emits;
# Session._execute_calls runs them through cora.executor.
_CORA_ACTION_TOOLS = {t["name"] for t in TOOLS}
from router import plugin_api
from cora.observation import officer_text


def _num(v, default=0):
    """A number for $-formatting; anything else formats as `default`."""
    return v if isinstance(v, (int, float)) else default
from router.common import _CORA_ACTION_TOOLS, _enumerate_actions, _now, _get_satisfaction, _get_budget
from router.plugin_context import _SessionToolContext, _plugin_tool_schemas_for


class OfficerLoopMixin:

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
    # The typed action tools count as "acting" (build/hire/…) alongside propose_choices, so the
    # reactive-autonomy guard strips ALL of them on an unprompted turn.
    # add_to_autonomy_list counts too: an officer asks for a standing order only when the
    # Director has spoken to it, never on an unprompted turn.
    _ACTING_TOOLS = frozenset({"propose_choices", "add_to_autonomy_list"}
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
                                 and plugin_api.get_tool(t) is None],
                                descriptions=_tdesc,
                                recipients=self._recipients_for(agent))
        else:
            _builtins = ([t for t in agent.tools if plugin_api.get_tool(t) is None]
                         if agent.tools else None)
            tools = build_tools(_builtins, descriptions=_tdesc,
                                recipients=self._recipients_for(agent))
        # Append registered plugin tool schemas this agent may use. Inert when no
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

    async def _fire_hooks(self, event: str, event_obj: dict,
                          agent: Optional[AgentConfig] = None) -> None:
        """Fire plugin hooks for a game event. Inert (no ctx built) when nothing is registered.
        `agent` is None for session-level events (round start, human choice); the hook ctx then
        uses the shared session store and unfiltered state."""
        if not plugin_api.get_hooks(event):
            return
        ctx = _SessionToolContext(self, agent, self._latest_game_state or {}, [], [])
        await plugin_api.run_hooks(event, ctx, event_obj)

    async def _fire_hooks_collect(self, event: str, event_obj: dict,
                                  agent: Optional[AgentConfig] = None) -> list:
        """Fire loop-shaping hooks and RETURN their values. Inert (and free) when none are
        registered, so the built-in loop pays nothing for the extension point."""
        if not plugin_api.get_hooks(event):
            return []
        ctx = _SessionToolContext(self, agent, self._latest_game_state or {},
                                  self._latest_all_actions or [], [])
        return await plugin_api.run_hooks_collect(event, ctx, event_obj)

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
