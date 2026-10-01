"""Unity I/O: committing actions and task answers over the websocket and keeping the latest game state.

A mixin of router.session.Session; its methods use the Session's state."""
from __future__ import annotations

import asyncio
from typing import List, Tuple, Optional


from router.config import AgentConfig
from cora.tools import TOOLS
# The typed action tools (build/hire/train/staff/deconstruct/task/transfer) the officer emits;
# Session._execute_calls runs them through cora.executor.
_CORA_ACTION_TOOLS = {t["name"] for t in TOOLS}


def _num(v, default=0):
    """A number for $-formatting; anything else formats as `default`."""
    return v if isinstance(v, (int, float)) else default
from router.common import _enumerate_actions, _now


class UnityIOMixin:

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

        Callers resolve calls to this ordered stream with cora.executor (which orders them);
        this method does NOT reorder.
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
