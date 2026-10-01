"""The officers' tools: dispatching each call, running action calls through cora.executor, the action menu and the commit ledger.

A mixin of router.session.Session; its methods use the Session's state."""
from __future__ import annotations

from typing import List, Tuple, Optional


from router.config import AgentConfig
from router.scope import filter_actions
from cora.tools import TOOLS
# The typed action tools (build/hire/train/staff/deconstruct/task/transfer) the officer emits;
# Session._execute_calls runs them through cora.executor.
_CORA_ACTION_TOOLS = {t["name"] for t in TOOLS}
from router import plugin_api
from cora import executor
from cora.observation import officer_text, task_group, task_token, vehicle_capacity


def _num(v, default=0):
    """A number for $-formatting; anything else formats as `default`."""
    return v if isinstance(v, (int, float)) else default
from router.common import _CORA_ACTION_TOOLS, _num, _NON_REPEATABLE_TYPES, _enumerate_actions, _num_free_vehicles
from router.plugin_context import _SessionToolContext


class OfficerToolsMixin:

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


    @staticmethod
    def _transfer_trip_note(action: dict, game_state: dict) -> str:
        """For a committed transfer, say how many vehicle trips it actually needs.

        WHY THIS IS A TOOL RESULT AND NOT A PROMPT RULE. DeliverySystem.CreateDeliveryTask
        loops `Mathf.Min(remaining, maxCapacity)` and returns a LIST, so a request larger
        than one vehicle load is split into that many trips, each needing its own vehicle.
        Measured: an officer told to move 400 food with 3 vehicles free committed it and
        reported "Sent 400 food" with no caveat -- yet asked directly, the SAME officer
        computed "4 vehicles (400 / 100 load each), we've got 3 free" correctly off the
        same observation. It could do the arithmetic; it just didn't think to, under a
        direct instruction. Telling it harder in the prompt is the lever that already
        failed, so the count is returned as engine fact alongside the action instead.

        Returns "" when the transfer fits in one trip, when the fleet covers it, or when
        the load/quantity is unknown -- a note that fires on every transfer is noise.
        """
        t = action.get("transfer") or action.get("resource_transfer") or {}
        try:
            qty = int(t.get("quantity") or 0)
        except (TypeError, ValueError):
            return ""
        load = vehicle_capacity(game_state)
        if not qty or not load:
            return ""
        trips = -(-qty // int(load))          # ceil
        if trips <= 1:
            return ""
        free = _num_free_vehicles(game_state)
        note = f" — needs {trips} vehicle trips at {load}/load"
        if free is not None:
            note += (f"; {free} free, so {trips - free} trip(s) wait for a vehicle"
                     if trips > free else f"; {free} free, covered")
        return note

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
        # State the committed spend as a NUMBER. The prose below already warns that the
        # frozen budget excludes these commits, but without the figure the officer has to
        # infer it — and a wrong inference means overcommitting against money it no longer
        # has. (Observed live: an officer built a $1,000 kitchen, was still shown $5,000,
        # and reported "~$4,000" as arithmetic rather than fact.)
        spend = ""
        if self._committed_spend_this_phase:
            spend = (f"Committed spend this phase: ${self._committed_spend_this_phase:,.0f} "
                     f"— subtract it from the budget shown above before deciding what you "
                     f"can still afford.\n")
        return (
            "YOU HAVE ALREADY COMMITTED these actions this planning phase — they are "
            "locked in and real, and take effect when the phase resolves (next "
            "round):\n"
            f"{items}\n"
            f"{spend}"
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

    def _record_committed(self, action: dict) -> None:
        """Append a succeeded action to the planning-phase ledger (deduped), and add its
        cost to the phase's committed spend so the officer can reconcile a frozen budget."""
        # Spend accumulates OUTSIDE the dedupe. The ledger key is "[type] description", so two
        # legitimate repeats of a repeatable action (worker, resource_transfer — both
        # deliberately excluded from _NON_REPEATABLE_TYPES) collapse onto one line. Counting
        # their cost once understated the very figure this ledger exists to make truthful.
        try:
            self._committed_spend_this_phase += float(action.get("cost") or 0)
        except (TypeError, ValueError):
            pass
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
        """Compact affordance view, grouped by action tool: what the officer can do now and the
        arguments to call each tool with. Nothing references a volatile menu index.

        (a) Task rows carry the stable task token (cora.observation.task_token, from the RAW
            task, so it matches what the task tool accepts), not a turn-to-turn taskId.
        (b) Committed non-repeatable affordances are pulled OUT of the available set and listed
            under an ALREADY-COMMITTED footer, using `_action_ledger_key` so the identity
            matches the ledger block.
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
                # cora.actions nests these fields under "assignment" (see
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
                transfer.append(f"{res}: {tr.get('source_facility')} -> "
                                f"{tr.get('destination_facility')} (up to {tr.get('quantity')})")
            elif t == "task_choice":
                tc = a.get("task_choice", {})
                tid, cid = tc.get("taskId"), tc.get("choiceId")
                if tid not in tasks:
                    raw = next((x for x in (game_state.get("allActiveTasks") or [])
                                if x.get("taskId") == tid), None)
                    tok = (task_token(self._norm_task_for_token(raw))
                           if raw else f"TASK_{tid}")
                    tasks[tid] = {"token": tok, "title": tc.get("taskTitle") or "", "choices": []}
                desc = a.get("description", "")
                marker = f"choice {cid}: "
                text = desc.split(marker, 1)[-1] if marker in desc else ""
                tasks[tid]["choices"].append((cid, text))

        lines = ["What you can do now (call the action tools with these arguments):"]
        if sites:
            lines.append("  build(type, site_id):")
            # Collapse consecutive sites that offer the SAME building types into one range.
            # A kitchen-scoped officer was shown fifteen lines that differed only by an id —
            # "site 0 (AbandonedSite (1)): Kitchen" repeated down the page — which was the
            # bulk of its per-turn message and buried the parts that actually varied. The
            # site ids are unchanged and still individually addressable; only the rendering
            # is folded.
            ordered = sorted(sites, key=lambda s: (s is None, s))
            run: list = []

            def _flush(run_ids):
                if not run_ids:
                    return
                types = ", ".join(sorted(sites[run_ids[0]][1]))
                if len(run_ids) == 1:
                    sid = run_ids[0]
                    lines.append(f"    site {sid} ({sites[sid][0]}): {types}")
                else:
                    names = {sites[s][0].rstrip("0123456789() ") for s in run_ids}
                    kind = names.pop() if len(names) == 1 else "site"
                    lines.append(f"    sites {run_ids[0]}-{run_ids[-1]} "
                                 f"({len(run_ids)}x {kind}): {types}")

            for sid in ordered:
                same = run and sorted(sites[sid][1]) == sorted(sites[run[-1]][1])
                contiguous = same and isinstance(sid, int) and isinstance(run[-1], int) \
                    and sid == run[-1] + 1
                if contiguous:
                    run.append(sid)
                else:
                    _flush(run)
                    run = [sid]
            _flush(run)
        hires = []
        if hire["untrained"]:
            hires.append(f"untrained up to {hire['untrained']}")
        if hire["trained"]:
            hires.append(f"trained up to {hire['trained']}")
        if hires:
            lines.append("  hire(kind, count): " + "  |  ".join(hires))
        if train:
            lines.append(f"  train(count): up to {train} untrained")
        if staff:
            lines.append("  staff(site): " + ", ".join(staff))
        if decon:
            lines.append("  deconstruct(site): " + ", ".join(decon))
        if transfer:
            lines.append("  transfer(resource, source, dest, qty): " + "  ".join(transfer))
        for tid, tk in tasks.items():
            opts = "  ".join(f"[{cid}] {txt}" for cid, txt in tk["choices"])
            lines.append(f'  task(task_id="{tk["token"]}", choice_id)  "{tk["title"]}": {opts}')
        if done:
            lines.append("")
            lines.append("⚠️ ALREADY COMMITTED THIS PHASE — do NOT pick these again:")
            for a in done:
                lines.append(f"  - [{a.get('action_type', '?')}] {a.get('description', '?')}")
        return "\n".join(lines)

    @staticmethod
    def _norm_task_for_token(t: dict) -> dict:
        """Raw allActiveTasks row → the {title, affects} shape task_token
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
            if task_token(self._norm_task_for_token(t)).lower() == ql:
                return t
        for t in tasks:            # 3. title substring
            title = (t.get("taskTitle") or t.get("title") or "").lower()
            if title and ql in title:
                return t
        return None

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
            token = task_token(self._norm_task_for_token(t))
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
        _skip_registry: bool = False,
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
        meta = {"executed": 0, "finish": False}
        args = tool_call.get("arguments") or {}

        if brief_only and name in self._ACTING_TOOLS:
            # A standing order the Director approved is the one thing an officer may do on
            # a turn nobody asked it to take: that typed tool, within the order's argument
            # limits. It then runs through the normal (non-brief) path, tagged with the rule.
            rule = (self._autonomy_rule_for(agent, name, args)
                    if name in _CORA_ACTION_TOOLS else None)
            if rule is not None:
                return await self._run_under_standing_order(
                    agent, rule, tool_call, game_state, all_actions, filtered_actions,
                    _skip_registry)
            refusal = ("REFUSED: you have not been directly addressed this turn, so you "
                       "cannot take actions or send proposals. Brief the director via "
                       "send_message to the Director (or call finish); they will tell you "
                       "what to do.")
            if self._autonomy_rules.get(agent.subagent_name):
                refusal = ("REFUSED: this call is not covered by any of your standing orders "
                           "(same tool, within its limits). Unprompted, you may only carry out "
                           "a standing order; for anything else, ask the Director.")
            return (refusal, game_state, all_actions, filtered_actions, meta)

        # The typed action tools (cora.tools) run through the shared executor.
        if name in _CORA_ACTION_TOOLS:
            return await self._execute_calls(agent, [(name, args)], game_state, all_actions,
                                             filtered_actions, meta)

        # Plugin tools (cora_ext registry) take precedence — a contributor tool, or one that
        # overrides a built-in by name, dispatches here. Inert when no plugins are loaded.
        # Acting plugin tools obey the same reactive brief-only gate as built-in acting tools.
        _plugin_spec = None if _skip_registry else plugin_api.get_tool(name)
        if _plugin_spec is not None:
            if brief_only and _plugin_spec.acting:
                return (
                    "REFUSED: you have not been directly addressed this turn, so you cannot "
                    "take actions. Brief the director via send_message (or call finish).",
                    game_state, all_actions, filtered_actions, meta,
                )
            _ctx = _SessionToolContext(self, agent, game_state, all_actions, filtered_actions)
            _res = await plugin_api.run_tool(_plugin_spec, _ctx, args)
            meta["executed"] = _res.executed
            meta["finish"] = _res.finish
            return (_res.text, self._latest_game_state or game_state,
                    _ctx.all_actions, _ctx.filtered_actions, meta)

        if name == "read_state":
            # Read the freshest state the router holds (kept current by every execute
            # commit + the per-turn get_game_state pull), so a look-up reflects reality
            # — including the officer's own just-executed actions — not a stale snapshot.
            fresh = self._latest_game_state or game_state
            return officer_text(self._filter_state(fresh, agent)), \
                game_state, all_actions, filtered_actions, meta

        # Granular getters — one slice of the same filtered observation each, so an
        # officer can pull just the detail it needs without re-dumping read_state.
        # get_logistics needs the officer's enumerated actions (the affordance block
        # is derived from them); the others are pure state slices.
        if name in ("get_facilities", "get_workforce", "get_tasks", "get_logistics"):
            fs = self._filter_state(self._latest_game_state or game_state, agent)
            section = name[len("get_"):]
            text = officer_text(fs, section, filtered_actions if section == "logistics" else None)
            return text, game_state, all_actions, filtered_actions, meta

        if name == "list_actions":
            filtered_actions = filter_actions(all_actions, agent.subaction_space)
            return ("Actions available to you now:\n"
                    + self._render_options_compact(filtered_actions, game_state)), \
                game_state, all_actions, filtered_actions, meta

        if name == "responsibility_lookup":
            return self._responsibility_lookup_text(agent, args, game_state), \
                game_state, all_actions, filtered_actions, meta

        if name == "propose_choices":
            result_text, game_state, all_actions, filtered_actions, executed, superseded, result_rows = \
                await self._continuous_propose(agent, args, game_state, all_actions, filtered_actions)
            meta["executed"] = executed
            # Surface the REAL per-action rows (the action tools' shape) so the logger
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
            else:
                # A live proposal surfaced choice cards to the director — director-facing.
                meta["spoke"] = True
            return result_text, game_state, all_actions, filtered_actions, meta

        if name == "send_message":
            message = str(args.get("message") or "").strip()
            if not message:
                return "ERROR: empty message.", game_state, all_actions, filtered_actions, meta
            to = str(args.get("to") or "Director").strip() or "Director"
            allowed = self._recipients_for(agent)
            if to not in allowed:
                # The enum should make this unreachable, but a model can still emit a name
                # outside it. Refuse with the real list rather than silently delivering to the
                # director: a misdelivered message read as a successful handoff is exactly the
                # failure this channel exists to make impossible.
                return (f"ERROR: you cannot message {to!r}. You may address: "
                        + ", ".join(allowed) + "."), \
                    game_state, all_actions, filtered_actions, meta
            await self._send_agent_response(agent, message, "agent_response", to=to)
            # `spoke` is what the post-loop fallback uses to decide whether the DIRECTOR got a
            # reply. A message to a peer is real work but it is NOT a reply to the director, so
            # it must not suppress that fallback -- otherwise an officer the director addressed
            # could answer its colleague and leave the director staring at a silent bubble.
            if to == "Director":
                meta["spoke"] = True
            return f"Message delivered to {to}.", \
                game_state, all_actions, filtered_actions, meta

        if name == "add_to_autonomy_list":
            text, shown, superseded = await self._continuous_autonomy_propose(agent, args)
            if superseded:
                meta["finish"] = True
            elif shown:
                meta["spoke"] = True      # the card is director-facing, like a proposal
            return text, game_state, all_actions, filtered_actions, meta

        if name == "remove_autonomy_rule":
            return (await self._remove_autonomy_rule(agent, args.get("rule_id"), by="officer"),
                    game_state, all_actions, filtered_actions, meta)

        if name == "finish":
            note = str(args.get("note") or "").strip()
            if note:
                await self._send_agent_response(agent, note, "agent_response")
                meta["spoke"] = True
            meta["finish"] = True
            return "Turn ended.", game_state, all_actions, filtered_actions, meta

        return f"ERROR: unknown tool {name!r}.", game_state, all_actions, filtered_actions, meta

    async def _execute_calls(self, agent: AgentConfig, calls: list, game_state: dict,
                             all_actions: List[dict], filtered_actions: List[dict], meta: dict):
        """Run an officer's typed action calls through cora.executor, the same resolver the
        benchmark and RL use: calls resolve against the officer's scoped menu (so out-of-scope
        targets are invalid), resolved game actions are committed to Unity in the executor's
        ORDER, and task answers are sent one by one. Every outcome is logged and reported back to
        the officer honestly; nothing is remapped.

        Returns (result_text, game_state, all_actions, filtered_actions, meta)."""
        resolved, tr = executor.plan_turn(calls, executor.Menu(filtered_actions, game_state))
        ordered = sorted(resolved, key=lambda r: executor.ORDER.get(r.tool, 99))
        to_run, choices, errors, summaries = [], [], [], []       # to_run: (action, CallResult)
        for r in ordered:
            if r.status != "resolved":
                errors.append(r)
            elif r.choice is not None:
                choices.append(r)
                summaries.append(r.summary)
            else:
                to_run.extend((tr.actions[i], r) for i in r.action_indices)
                summaries.append(r.summary)

        def call_of(r):
            return {"tool": r.tool, "args": r.args}

        # ledger_mode="block": a NON-repeatable action already committed this phase is not re-sent
        # (the paused-phase state cannot show the queued action yet, so redoing it would just fail
        # engine-side). Grounding, not style-gating: hire/train/transfer are never blocked, and the
        # rest of the batch still runs.
        blocked = []
        if getattr(agent, "ledger_mode", "annotate") == "block":
            committed = set(self._committed_this_phase)
            keep = []
            for a, r in to_run:
                if a.get("action_type") in _NON_REPEATABLE_TYPES and self._action_ledger_key(a) in committed:
                    blocked.append((a, r))
                else:
                    keep.append((a, r))
            to_run = keep
        exec_results, game_state = (
            await self.execute_resolved([{"kind": "action", "action": a} for a, _ in to_run],
                                        game_state=game_state, scope_agent=agent)
            if to_run else ([], game_state))
        executed, lines, results = 0, [], []
        for a, r in blocked:
            self._log_action(self._actor_for(agent), "game_action", r.tool,
                             {"action": a, "success": False, "error": "blocked_already_committed",
                              "call": call_of(r), **self._outcome_fields("invalid")})
            results.append({"action_id": a.get("action_id"), "action_type": a.get("action_type"),
                            "description": a.get("description"), "cost": a.get("cost") or 0,
                            "success": False, "error": "blocked_already_committed"})
            print(f"[router]   ⛔ Blocked re-execution (already committed this phase): "
                  f"{self._action_ledger_key(a)}")
            lines.append(f"  ⛔ {a.get('description', '(action)')} — already committed "
                         f"this phase (queued; re-doing is a no-op)")
        for (action, r), res in zip(to_run, exec_results):
            success = bool(res.get("success"))
            err = res.get("error_message") or ""
            results.append({"action_id": action.get("action_id"), "action_type": action.get("action_type"),
                            "description": action.get("description"), "cost": action.get("cost") or 0,
                            "success": success, "error": err})
            # Deltas aren't per-action-attributable inside a batched Unity commit, so log the
            # engine-truth outcome only (ok/rejected).
            self._log_action(self._actor_for(agent), "game_action", r.tool,
                             {"action": action, "success": success, "error": err, "call": call_of(r),
                              **self._outcome_fields("ok" if success else "rejected")})
            desc = action.get("description", "(action)")
            if success:
                executed += 1
                self._record_committed(action)
                await self._fire_hooks(
                    "on_action_executed",
                    {"actor": agent.subagent_name, "source": "officer",
                     "is_human": False, "action": action}, agent=agent)
                # Director-facing commit bubble: one plain-English past-tense line per committed
                # action ("Action: Built Shelter at Riverside for $2,000"); the officer still
                # speaks to the director in its own words via send_message.
                await self._send_agent_response(
                    agent, "Action: " + self._humanize_committed_action(action),
                    "agent_response", origin="router_action_receipt")
                lines.append(f"  ✅ {desc}"
                             + (self._transfer_trip_note(action, game_state)
                                if action.get("action_type") == "resource_transfer" else ""))
            else:
                lines.append(f"  ❌ {desc}" + (f" — {err}" if err else ""))
        # Task answers. Scope is enforced through the SAME subaction_space filter as every action
        # (_may_answer_task): an out-of-scope pick is an honest policy signal, not executed.
        choice_lines, answered = [], 0
        by_id = {t.get("taskId"): t for t in (game_state.get("allActiveTasks") or [])}
        for r in choices:
            tid, cid = r.choice["taskId"], r.choice["choiceId"]
            task = by_id.get(tid)
            if task is None:
                self._log_action(self._actor_for(agent), "game_action", "select_task_choice",
                                 {"taskId": tid, "choiceId": cid, "success": False,
                                  "error": "no_such_active_task", "call": call_of(r),
                                  **self._outcome_fields("invalid")})
                choice_lines.append(f"  ❌ task {tid}: no such active task")
                continue
            if not self._may_answer_task(agent, task):
                self._log_action(self._actor_for(agent), "game_action", "select_task_choice",
                                 {"taskId": tid, "choiceId": cid, "success": False,
                                  "error": "out_of_scope", "group": task_group(task), "call": call_of(r),
                                  **self._outcome_fields("invalid")})
                choice_lines.append(f"  ❌ task {tid}: outside your action scope "
                                    f"(group {task_group(task)}) — not answered")
                continue
            if not self._task_choice_supported:
                choice_lines.append(f"  ⏸ task {tid} choice {cid}: task-choice execution "
                                    f"unavailable on this transport yet")
                continue
            before = self._state_metrics(game_state)
            res, game_state = await self._execute_choice_via_unity(tid, cid, game_state)
            ok = bool(res.get("success"))
            err = res.get("error_message") or ""
            self._log_action(self._actor_for(agent), "game_action", "select_task_choice",
                             {"taskId": tid, "choiceId": cid, "success": ok, "error": err,
                              "call": call_of(r),
                              **self._outcome_fields("ok" if ok else "rejected", before,
                                                     self._state_metrics(game_state),
                                                     is_choice=True, tid=tid)})
            results.append({"kind": "task_choice", "taskId": tid, "choiceId": cid,
                            "success": ok, "error": err})
            if ok:
                answered += 1
                await self._fire_hooks(
                    "on_choice_resolved",
                    {"kind": "task_choice", "actor": agent.subagent_name, "source": "officer",
                     "is_human": False, "taskId": tid, "choiceId": cid, "choice": cid,
                     "group": task_group(task), "officer": agent.subagent_name},
                    agent=agent)
                # Director-facing commit bubble: 'Action: Chose "<choice>" for task "<title>"'.
                title = task.get("taskTitle") or task.get("title") or f"task {tid}"
                choice_text = next(((c.get("choiceText") or c.get("text") or "")
                                    for c in (task.get("choices") or []) if c.get("choiceId") == cid), "")
                choice_text = (choice_text or f"choice {cid}").strip()
                await self._send_agent_response(
                    agent, f'Action: Chose "{choice_text}" for task "{title}"',
                    "agent_response", origin="router_action_receipt")
                choice_lines.append(f"  ✅ answered task {tid} with choice {cid}")
            else:
                choice_lines.append(f"  ❌ task {tid} choice {cid}" + (f" — {err}" if err else ""))
        for r in errors:
            # A call that resolves to nothing the officer can do now is an action-space error;
            # logged so "can't-execute" stays measurable.
            self._log_action(self._actor_for(agent), "game_action", r.tool,
                             {"success": False, "error": "invalid_call", "detail": r.reason,
                              "call": call_of(r), **self._outcome_fields("invalid")})
            results.append({"success": False, "error": "invalid_call", "detail": f"{r.tool}: {r.reason}"})
        # Refresh the menu after mutating the world.
        all_actions = _enumerate_actions(game_state)
        filtered_actions = filter_actions(all_actions, agent.subaction_space)
        meta["executed"] = executed
        meta["results"] = results
        parts = [f"Ran {len(to_run)} action(s); {executed} succeeded."]
        if blocked:
            parts[0] += (f" {len(blocked)} already-committed action(s) were skipped "
                         f"(queued from earlier this phase).")
        if summaries:
            parts.append("Resolved: " + "; ".join(summaries))
        if lines:
            parts.append("\n".join(lines))
        if choices:
            parts.append(f"Answered {answered}/{len(choices)} choice-task(s).")
        if choice_lines:
            parts.append("\n".join(choice_lines))
        if errors:
            parts.append("Not executed (fix and retry, or pick a different move):\n  "
                         + "\n  ".join(f"{r.tool}: {r.reason}" for r in errors))
        body = "\n".join(parts)
        body += "\n\nUpdated actions:\n" + self._render_options_compact(filtered_actions, game_state)
        return body, game_state, all_actions, filtered_actions, meta
