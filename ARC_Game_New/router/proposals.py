"""propose_choices: choice cards for the Director, resolved to the client's action indices, and the Director's pick.

A mixin of router.session.Session; its methods use the Session's state."""
from __future__ import annotations

import asyncio
from typing import List, Tuple, Optional


from router.config import AgentConfig
from router.scope import filter_actions
from cora.tools import TOOLS
# The typed action tools (build/hire/train/staff/deconstruct/task/transfer) the officer emits;
# Session._execute_calls runs them through cora.executor.
_CORA_ACTION_TOOLS = {t["name"] for t in TOOLS}
from cora import executor


def _num(v, default=0):
    """A number for $-formatting; anything else formats as `default`."""
    return v if isinstance(v, (int, float)) else default
from router.common import _enumerate_actions, HUMAN_DIRECTOR_ACTOR, _now


class ProposalsMixin:

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

    def _calls_to_indices(
        self, calls: list, filtered_actions: List[dict], game_state: dict
    ) -> Tuple[List[int], List[str]]:
        """Resolve a proposal package's typed calls to indices INTO filtered_actions.

        A package's `action_indices` index the exact list the client renders and executes
        (available_actions = filtered_actions), so every kept action must land back inside it:

        - Most calls resolve (cora.executor) to an index < len(filtered_actions): keep it.
          A repeated index encodes quantity (a hire covered by several bundles) and is kept.
        - staff() makes the executor SYNTHESIZE a worker_assignment appended past the menu; it
          is matched back structurally on (building_name, quantity) to the enumerated
          assignment the client can execute, or dropped with a reason if none is offered.
        - task() answers have no home in the index contract: dropped with a reason (officers
          answer tasks with the task tool, not inside a proposal).
        - Calls that do not resolve are dropped with their reason; nothing is guessed.

        Returns (indices, reasons)."""
        resolved, tr = executor.plan_turn(calls, executor.Menu(filtered_actions, game_state))
        n = len(filtered_actions)
        assign_to_idx: dict = {}
        for i, a in enumerate(filtered_actions):
            if a.get("action_type") == "worker_assignment":
                asg = a.get("assignment", {})
                k = (asg.get("building_name"), asg.get("quantity"))
                if k not in assign_to_idx or asg.get("worker_type") == "untrained":
                    assign_to_idx[k] = i
        indices: List[int] = []
        reasons: List[str] = []
        for r in sorted(resolved, key=lambda r: executor.ORDER.get(r.tool, 99)):
            if r.status != "resolved":
                reasons.append(f"{r.tool}: {r.reason}")
            elif r.choice is not None:
                reasons.append(f"task {r.choice['taskId']} choice {r.choice['choiceId']} — answer "
                               "tasks with the task tool, not inside a proposal package")
            for i in (r.action_indices if r.status == "resolved" else []):
                if i < n:
                    indices.append(i)
                    continue
                asg = tr.actions[i].get("assignment", {})
                idx = assign_to_idx.get((asg.get("building_name"), asg.get("quantity")))
                if idx is None:
                    reasons.append(f"staffing {asg.get('quantity')} to '{asg.get('building_name')}' — "
                                   "not offered at that quantity this turn (check the free-worker pool)")
                else:
                    indices.append(idx)
        return indices, reasons

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

        # Sanitize the model-authored packages into the shape the client renders: each
        # package's typed calls resolve (_calls_to_indices) to positions in filtered_actions,
        # the action_indices the client consumes. A package that resolves to zero actions is
        # dropped with a reason; never mis-index.
        packages: List[dict] = []
        drop_notes: List[str] = []
        for p in raw_packages:
            if not isinstance(p, dict):
                continue
            label = str(p.get("label") or f"Option {len(packages) + 1}")
            calls = [(c.get("tool"), c.get("args") or {}) for c in (p.get("calls") or [])
                     if isinstance(c, dict)]
            if calls:
                indices, reasons = self._calls_to_indices(calls, filtered_actions, game_state)
            else:
                indices, reasons = [], ["no calls"]
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
            msg = ("ERROR: no valid packages — each package's `calls` must contain action tool "
                   "calls that resolve to actions you can take now.")
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
        # Real per-action execution rows in the SAME shape the action tools emit
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
                        # the action tools: narrate each executed action (esp. a build)
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
