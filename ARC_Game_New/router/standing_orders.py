"""Standing orders (add_to_autonomy_list): an officer may use one action tool on its own, within limits the Director approved.

A mixin of router.session.Session; its methods use the Session's state."""
from __future__ import annotations

import asyncio
import json
import uuid
from typing import List, Tuple, Optional


from router.config import AgentConfig
from router.officer_llm import build_tools, DEFAULT_TOOLS
from cora.tools import TOOLS, TOOL_BY_NAME
# The typed action tools (build/hire/train/staff/deconstruct/task/transfer) the officer emits;
# Session._execute_calls runs them through cora.executor.
_CORA_ACTION_TOOLS = {t["name"] for t in TOOLS}
from router import plugin_api


def _num(v, default=0):
    """A number for $-formatting; anything else formats as `default`."""
    return v if isinstance(v, (int, float)) else default
from router.common import _CORA_ACTION_TOOLS, HUMAN_DIRECTOR_ACTOR, SYSTEM_ACTOR, _now


class StandingOrdersMixin:

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
            [t for t in (agent.tools or DEFAULT_TOOLS) if plugin_api.get_tool(t) is None]))
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
