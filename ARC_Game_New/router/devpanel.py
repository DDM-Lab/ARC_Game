"""The developer panel's view of a session: inspect and edit prompts live, reset officers, load a checkpoint.

A mixin of router.session.Session; its methods use the Session's state."""
from __future__ import annotations

import asyncio
import json
import difflib
import hashlib
import uuid
from typing import Optional


from router.config import load_global_prompt
from cora.tools import TOOLS
# The typed action tools (build/hire/train/staff/deconstruct/task/transfer) the officer emits;
# Session._execute_calls runs them through cora.executor.
_CORA_ACTION_TOOLS = {t["name"] for t in TOOLS}


def _num(v, default=0):
    """A number for $-formatting; anything else formats as `default`."""
    return v if isinstance(v, (int, float)) else default
from router.common import PEER_TRIGGER_BUDGET_PER_ROUND


class DevPanelMixin:

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
