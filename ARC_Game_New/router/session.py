"""One game session of the officer router: the Session drives a connected client's game.

A Session holds the game state the client reports, runs each LLM officer's tool loop
(router.officer_llm), resolves and commits their actions (cora.executor -> Unity over the
websocket), renders proposals and standing-order cards for the human Director, and logs every
turn and action. router.service creates one per websocket connection.
"""
from __future__ import annotations

import asyncio
import json
from typing import Dict, List, Optional

from fastapi import WebSocket, WebSocketDisconnect
from starlette.websockets import WebSocketState

from router.config import AgentConfig, RouterConfig
from router.scope import filter_observation
from router.ordering import get_agent_order
from router.episode_log import EpisodeLogger
from cora.tools import TOOLS
# The typed action tools (build/hire/train/staff/deconstruct/task/transfer) the officer emits;
# Session._execute_calls runs them through cora.executor.
_CORA_ACTION_TOOLS = {t["name"] for t in TOOLS}
from router import plugin_store
from cora.scoring import score_components
from cora.observation import task_officer, task_group


def _num(v, default=0):
    """A number for $-formatting; anything else formats as `default`."""
    return v if isinstance(v, (int, float)) else default
from router.message_queue import MessageQueue
from router.common import _enumerate_actions, HUMAN_DIRECTOR_ACTOR, _UNSET, _wrap_actor, PEER_TRIGGER_BUDGET_PER_ROUND, _now, _get_satisfaction, _get_budget
from router.unity_io import UnityIOMixin
from router.officer_loop import OfficerLoopMixin
from router.officer_tools import OfficerToolsMixin
from router.proposals import ProposalsMixin
from router.standing_orders import StandingOrdersMixin
from router.messaging import DirectorMessagingMixin
from router.devpanel import DevPanelMixin


class Session(UnityIOMixin, OfficerLoopMixin, OfficerToolsMixin, ProposalsMixin, StandingOrdersMixin, DirectorMessagingMixin, DevPanelMixin):
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
        # --- Plugin per-session state: three store scopes + a lock for shared
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

        # Continuous officers run their tool-loops CONCURRENTLY: each reads the freshest shared
        # snapshot and publishes its result, while the Unity socket is arbitrated by the
        # commit/attention locks. Manual actors are played by the human; nothing runs for them.
        continuous = [a for a in ordered if a.actor_type == "continuous"]

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
