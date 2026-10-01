"""Command-tag adapter for the officer router (temporary).

The router still turns officer tool calls into command tags (<build>Kitchen,3</build>) and
proposal packages carry tag strings. parse_commands turns tags back into typed tool calls and
resolves them with tool_executor's resolver, so resolution is shared. It is deleted when the
router calls the executor directly (docs/ARCHITECTURE.md, migration status).

ParserEnv is the minimal env shape the resolver reads: get_valid_actions(), game_state, and
valid_actions (a private copy that resolved staff actions are appended to).
"""
import re


class ParserEnv:
    """Minimal env adapter satisfying parse_commands' contract, shared by every
    non-router caller (gym, benchmark) so they don't each hand-roll a stub.

    Holds the round's enumerated menu + raw game_state and exposes the three
    things parse_commands reads: get_valid_actions(), game_state, valid_actions.
    valid_actions is a private COPY of the menu, so the parser's <staff>
    synth-append (it appends a worker_assignment action so <staff> is executable
    this turn) mutates only this object — the caller then executes the resolved
    indices against THIS object's valid_actions, not its own list. The live
    router aliases its _CmdParseShim to this class (one shim, every arm).
    """

    def __init__(self, valid_actions, game_state):
        self.valid_actions = list(valid_actions)
        self.game_state = game_state

    def get_valid_actions(self):
        return self.valid_actions


_CMD_RE = re.compile(r"<\s*(build|hire|train|staff|task|deconstruct|transfer)\s*>(.*?)<\s*[\\/]\s*\1\s*>",
                     re.I | re.S)


def parse_commands(text, env):
    """Map command tags in `text` to (action_indices, choices) against the round's enumeration.

    A thin front end over tool_executor: each tag becomes the equivalent typed tool call
    (<staff>Shelter Alpha,</staff> -> staff(site="Shelter Alpha")) and the shared TurnResolver
    resolves it, so the tag grammar, the typed-tool benchmark, the RL env and the officer router
    all resolve actions with ONE implementation. Synthesized staff actions are appended to
    env.valid_actions, as before, so callers keep executing the returned indices against it.

    Returns dict: {actions:[idx...], choices:[{taskId,choiceId}...], parsed:[...], errors:[...]}.
    """
    import tool_executor
    from cora_tools import _TOOL_BY_NAME, _param_names
    calls = []
    for m in _CMD_RE.finditer(text or ""):
        cmd, body = m.group(1).lower(), m.group(2).strip()
        tool = _TOOL_BY_NAME.get(cmd)
        names = _param_names(tool) if tool else []
        parts = [p.strip() for p in body.replace("\n", " ").split(",")]
        args = {n: parts[i] for i, n in enumerate(names) if i < len(parts) and parts[i] != ""}
        calls.append((cmd, args))
    results, tr = tool_executor.plan_turn(calls, env)
    if hasattr(env, "valid_actions"):
        env.valid_actions = tr.actions
    actions, choices, parsed, errors = [], [], [], []
    for r in sorted(results, key=lambda r: tool_executor.ORDER.get(r.tool, 99)):
        if r.status != "resolved":
            errors.append(f"{r.tool}: {r.reason}")
        elif r.choice is not None:
            choices.append(r.choice); parsed.append(r.summary)
        else:
            actions.extend(r.action_indices); parsed.append(r.summary)
    return {"actions": actions, "choices": choices, "parsed": parsed, "errors": errors}
