"""
Provider-agnostic tool-calling step for the Continuous Agent.

The Continuous Agent holds the FULL tool palette every step and decides which
tool to invoke. Interaction *style* — instruct/act, recommend, ask, coach — is an
EMERGENT property of which tool it reaches for under a given game state, not
something the router imposes via config gating or allowlists. See
CONTINUOUS_AGENT.md for the design rationale and the 2026 literature it draws on.

This module owns only two things:

  1. The canonical tool *schemas* (OpenAI function-calling format, the neutral
     representation) and an allowlist filter.
  2. A single provider-agnostic tool-call *step*: one LLM request that returns
     either tool calls or a final message, normalized across providers.

The *loop* — and the binding of each tool name to a real game backend
(execute action, propose choices, talk to the director) — lives in the router
(`agent_router.py::_run_continuous`). The router speaks the OpenAI message shape
throughout; this module translates to/from Anthropic and the text fallback so
the loop never has to care which provider is behind it.
"""
import json
import os
from pathlib import Path
from typing import Optional, List, Dict, Any

from dotenv import load_dotenv

from llm_query import load_global_prompt

load_dotenv(Path(__file__).parent / ".env")

try:
    import openai
except ImportError:
    openai = None

try:
    import anthropic
except ImportError:
    anthropic = None


# ---------------------------------------------------------------------------
# Canonical tool palette (Phase 1)
# ---------------------------------------------------------------------------
# OpenAI function-calling format is the neutral representation; the Anthropic
# adapter rewrites `parameters` -> `input_schema` on the fly. Every tool is
# available to the agent every step (subject only to an optional per-config
# allowlist); nothing here encodes when a tool "should" be used — that judgment
# is the model's, guided by the system prompt.
#
# Phase 1 needs NO Unity client changes: execute_game_action and propose_choices
# ride the already-built execute / choices backends. `open_interaction` (handing
# a native dialog panel to the human) is Phase 2 and deliberately absent here.

TOOL_SCHEMAS: Dict[str, dict] = {
    "read_state": {
        "type": "function",
        "function": {
            "name": "read_state",
            "description": (
                "Return the current game-state snapshot (your filtered observation) "
                "as text. Use it to refresh your view after actions have changed the "
                "world, before deciding what to do next."
            ),
            "parameters": {"type": "object", "properties": {}, "required": []},
        },
    },
    "get_facilities": {
        "type": "function",
        "function": {
            "name": "get_facilities",
            "description": (
                "Return ONLY the facilities table (each facility's name, type, "
                "status, staffing vs. need, food, and population). A focused slice "
                "of read_state — use it when you just need the buildings, without "
                "the rest of the state."
            ),
            "parameters": {"type": "object", "properties": {}, "required": []},
        },
    },
    "get_workforce": {
        "type": "function",
        "function": {
            "name": "get_workforce",
            "description": (
                "Return ONLY the headline scalars: day, budget, satisfaction, the "
                "shared worker pool (free trained / free untrained / working / in "
                "training), and current spend/costs. Use it to check money and labor "
                "before hiring, training, or staffing."
            ),
            "parameters": {"type": "object", "properties": {}, "required": []},
        },
    },
    "get_tasks": {
        "type": "function",
        "function": {
            "name": "get_tasks",
            "description": (
                "Return ONLY the active tasks in YOUR jurisdiction, each with its "
                "stable token (e.g. FOOD_C01), title, rounds left, and answer "
                "choices. Use it to see what needs answering without the rest of "
                "the state."
            ),
            "parameters": {"type": "object", "properties": {}, "required": []},
        },
    },
    "get_logistics": {
        "type": "function",
        "function": {
            "name": "get_logistics",
            "description": (
                "Return ONLY the logistics/affordance block: open build sites, who "
                "needs staffing, staff-now options, hire/train capacity, and valid "
                "resource-transfer endpoints. Use it to see what you can act on right "
                "now before composing command tags."
            ),
            "parameters": {"type": "object", "properties": {}, "required": []},
        },
    },
    "list_actions": {
        "type": "function",
        "function": {
            "name": "list_actions",
            "description": (
                "List the game actions currently available to you — each with an "
                "index, type, human-readable description, and dollar cost. Indices are "
                "only valid until you execute something; re-list after any change."
            ),
            "parameters": {"type": "object", "properties": {}, "required": []},
        },
    },
    "responsibility_lookup": {
        "type": "function",
        "function": {
            "name": "responsibility_lookup",
            "description": (
                "Check WHO is responsible for an action OR a task before you do it, "
                "answer it, or name a colleague. Read-only — it does not change the "
                "game. Three ways to use it:\n"
                "- Give a `task` (its token like FOOD_C01, its numeric id, or a bit of "
                "its title) to find which officer answers that task.\n"
                "- Give a `category` (and a `building_type` where it applies) to find "
                "which officer owns that kind of game action.\n"
                "- Give nothing to see the whole roster of officers and what each owns.\n"
                "It always returns the responsible officer, whether it is in YOUR "
                "scope, and the full roster. Use it whenever you are about to act or "
                "answer near the edge of your role, or before telling the director "
                "something is someone else's job, so you name the RIGHT officer instead "
                "of guessing. If the owner is not you, do NOT do it or claim it — tell "
                "the director it belongs to the named officer."
            ),
            "parameters": {
                "type": "object",
                "properties": {
                    "task": {
                        "type": "string",
                        "description": (
                            "A task to look up ownership of: its stable token (e.g. "
                            "FOOD_C01, RELOC_C02, BUDGET_DAILY), its numeric task id, or "
                            "a distinctive part of its title. Resolved against the tasks "
                            "currently active in the game."
                        ),
                    },
                    "category": {
                        "type": "string",
                        "enum": ["construction", "worker_assignment",
                                 "deconstruction", "worker", "resource_transfer"],
                        "description": (
                            "The kind of action to look up: construction (build a "
                            "facility), worker_assignment (staff a built facility), "
                            "deconstruction, worker (hire/train the shared labor pool), "
                            "or resource_transfer (move food/people). Omit to see the "
                            "whole roster."
                        ),
                    },
                    "building_type": {
                        "type": "string",
                        "enum": ["Kitchen", "Shelter", "CaseworkSite"],
                        "description": (
                            "For construction / worker_assignment / deconstruction, "
                            "which building type. Ignored for worker and "
                            "resource_transfer."
                        ),
                    },
                },
                "required": [],
            },
        },
    },
    "execute_commands": {
        "type": "function",
        "function": {
            "name": "execute_commands",
            "description": (
                "Execute a batch of game actions by INTENT using command tags, instead "
                "of raw indices. Each tag is resolved against the current game state, so "
                "you describe WHAT you want, not menu positions. Actions run in a "
                "commonsense order (deconstruct → build → hire → train → staff → "
                "transfer) regardless of the order you write them, so \"hire, then staff "
                "the workers you just hired\" works in one call. Returns the engine's real "
                "per-action success/failure plus any commands that couldn't be resolved, "
                "and a refreshed action list.\n"
                "Grammar (one tag per action, newline-separated):\n"
                "  <build>TYPE,SITE_ID</build>        TYPE=Kitchen|Shelter|CaseworkSite\n"
                "  <hire>KIND,N</hire>                 KIND=trained|untrained, N=count\n"
                "  <train>N</train>                    train N untrained workers\n"
                "  <staff>BUILDING,N</staff>           assign N workforce to a built building\n"
                "  <deconstruct>BUILDING</deconstruct> BUILDING=name substring\n"
                "  <transfer>RESOURCE,SRC,DEST,QTY</transfer>  RESOURCE=food|people\n"
                "  <task>TASK,CHOICE_ID</task>        answer a choice-task in your scope;\n"
                "                                     TASK is the stable token shown in the\n"
                "                                     options (e.g. FOOD_C01, BUDGET_DAILY)\n"
                "Example: \"<build>Kitchen,1</build>\\n<hire>untrained,4</hire>\\n<staff>Kitchen,4</staff>\""
            ),
            "parameters": {
                "type": "object",
                "properties": {
                    "commands": {
                        "type": "string",
                        "description": "The command tags to execute, one per line.",
                    },
                    "note": {
                        "type": "string",
                        "description": "Optional one-line rationale for the record.",
                    },
                },
                "required": ["commands"],
            },
        },
    },
    "propose_choices": {
        "type": "function",
        "function": {
            "name": "propose_choices",
            "description": (
                "Offer the human director a small set of selectable action packages "
                "and hand THEM the decision. Blocks until the director picks one, then "
                "returns which package they chose and the result of executing it. Use "
                "when the call is genuinely theirs to make, when you want a steer, or "
                "when you'd rather recommend than act."
            ),
            "parameters": {
                "type": "object",
                "properties": {
                    "reasoning": {
                        "type": "string",
                        "description": "1-2 sentences framing the trade-off across the packages.",
                    },
                    "packages": {
                        "type": "array",
                        "description": "2-4 genuinely distinct strategy packages.",
                        "items": {
                            "type": "object",
                            # Property order is emission order: label + the
                            # structured commands come BEFORE the prose description,
                            # so if a completion is ever truncated it loses (optional)
                            # description text, never the command tags the package is
                            # worthless without.
                            "properties": {
                                "label": {"type": "string", "description": "Short name, 2-4 words."},
                                "commands": {
                                    "type": "string",
                                    "description": (
                                        "The actions this package bundles, as command tags — "
                                        "SAME grammar as execute_commands (e.g. "
                                        "<build>Kitchen,3</build>, <hire>untrained,4</hire>), "
                                        "one tag per line. The director executes exactly these "
                                        "if they pick this package. Emit this first."
                                    ),
                                },
                                "description": {
                                    "type": "string",
                                    "description": "1-2 sentences: what this package does and why pick it.",
                                },
                            },
                            "required": ["label", "commands"],
                        },
                    },
                },
                "required": ["packages"],
            },
        },
    },
    "talk_to_director": {
        "type": "function",
        "function": {
            "name": "talk_to_director",
            "description": (
                "Send a message to the human director: an explanation grounded in the "
                "state, a clarifying question, a heads-up, or plain conversation. This "
                "does NOT change the game. (Later this same channel will also reach the "
                "other officers — for now it goes to the director.)"
            ),
            "parameters": {
                "type": "object",
                "properties": {
                    "message": {"type": "string", "description": "The message text."},
                },
                "required": ["message"],
            },
        },
    },
    "finish": {
        "type": "function",
        "function": {
            "name": "finish",
            "description": (
                "End your turn for this round. Use it when you have nothing further "
                "worth doing right now. You may include a brief closing note."
            ),
            "parameters": {
                "type": "object",
                "properties": {
                    "note": {"type": "string", "description": "Optional closing note to the director."},
                },
                "required": [],
            },
        },
    },
}

# Default palette when a config sets no `tools` allowlist: everything.
DEFAULT_TOOLS: List[str] = list(TOOL_SCHEMAS.keys())


def build_tools(allowlist: Optional[List[str]] = None) -> List[dict]:
    """Return the OpenAI-format tool schemas for the given allowlist.

    `allowlist=None` (the default) exposes the full palette — the no-gating
    default. A config MAY restrict the palette, but that is an explicit,
    documented narrowing, never the router second-guessing the model per turn.
    """
    names = allowlist if allowlist else DEFAULT_TOOLS
    tools = []
    for name in names:
        schema = TOOL_SCHEMAS.get(name)
        if schema is None:
            print(f"[continuous_agent] unknown tool in allowlist: {name!r} (skipped)")
            continue
        tools.append(schema)
    return tools


# ---------------------------------------------------------------------------
# Provider-agnostic tool-call step
# ---------------------------------------------------------------------------


def _resolve_mode(agent_cfg: dict, tool_mode: str) -> str:
    """Resolve tool_mode='auto' to a concrete adapter for this provider."""
    if tool_mode in ("native", "text"):
        return tool_mode
    provider = (agent_cfg.get("llm_provider") or "openai").lower()
    # OpenAI-compatible endpoints (incl. the CMU gateway) and Anthropic support
    # native tool calls. Ollama's tool support is uneven across models, so fall
    # back to the text/ReAct adapter there for robustness.
    return "text" if provider == "ollama" else "native"


def run_tool_step(
    messages: List[dict],
    tools: List[dict],
    agent_cfg: dict,
    tool_mode: str = "auto",
) -> Dict[str, Any]:
    """Run one tool-calling step against the configured provider.

    Args:
        messages: conversation in OpenAI shape (system / user / assistant /
            {role: "tool", tool_call_id, content}). The caller owns this list.
        tools: OpenAI-format tool schemas (from `build_tools`).
        agent_cfg: the agent config dict (provider, model, endpoint, key env,
            token budget).
        tool_mode: "auto" | "native" | "text".

    Returns a normalized dict:
        {
          "content": str | None,          # assistant free text, if any
          "tool_calls": [                  # zero or more
              {"id": str, "name": str, "arguments": dict}, ...
          ],
          "raw": str,                      # best-effort raw text (debug/log)
          "error": str | None,             # set if the call failed
        }
    """
    mode = _resolve_mode(agent_cfg, tool_mode)
    provider = (agent_cfg.get("llm_provider") or "openai").lower()

    try:
        if mode == "text":
            return _text_tool_step(messages, tools, agent_cfg)
        if provider == "anthropic":
            return _anthropic_tool_step(messages, tools, agent_cfg)
        # openai + any OpenAI-compatible gateway
        return _openai_tool_step(messages, tools, agent_cfg)
    except Exception as e:  # noqa: BLE001 — surface as a tool-step error, don't crash the loop
        name = agent_cfg.get("subagent_name", "Unknown")
        print(f"[continuous_agent] [{name}] tool step failed ({mode}/{provider}): {e}")
        return {"content": None, "tool_calls": [], "raw": "", "error": str(e)}


# Hard ceiling for the truncation self-heal: a step that runs out of completion
# tokens (finish_reason == "length" / stop_reason == "max_tokens") is retried ONCE
# with a doubled budget, capped here so a runaway generation can't balloon cost.
_TOKEN_RETRY_CEILING = 16384


def _max_tokens(agent_cfg: dict) -> int:
    return int(agent_cfg.get("turn_token_budget") or 1024)


def _usage_dict(input_tokens=0, output_tokens=0) -> Dict[str, int]:
    """Normalized token-usage record returned by every step (zeros when the
    provider doesn't report usage, e.g. the text/Ollama path). The router sums
    `total_tokens` across a turn's steps for per-turn cost accounting."""
    it, ot = int(input_tokens or 0), int(output_tokens or 0)
    return {"input_tokens": it, "output_tokens": ot, "total_tokens": it + ot}


def _openai_tool_step(messages, tools, agent_cfg) -> Dict[str, Any]:
    name = agent_cfg.get("subagent_name", "Unknown")
    model = agent_cfg.get("llm_model", "gpt-4o-mini")
    if openai is None:
        return {"content": None, "tool_calls": [], "raw": "", "error": "openai lib not installed"}

    api_key = os.environ.get(agent_cfg.get("api_key_env", "OPENAI_API_KEY"))
    if not api_key:
        return {"content": None, "tool_calls": [], "raw": "",
                "error": f"missing API key ({agent_cfg.get('api_key_env', 'OPENAI_API_KEY')})"}

    base_url = agent_cfg.get("llm_endpoint")
    client = openai.OpenAI(api_key=api_key, base_url=base_url) if base_url else openai.OpenAI(api_key=api_key)

    # Self-heal truncation: if the model runs out of completion tokens mid tool
    # call (finish_reason == "length"), its arguments JSON is cut off and would
    # silently _parse_args to {} — the "no valid packages" failure. Retry ONCE
    # with a doubled budget (capped) so the call can finish instead of degrading.
    budget = _max_tokens(agent_cfg)
    choice = None
    for attempt in range(2):
        resp = client.chat.completions.create(
            model=model,
            messages=messages,
            tools=tools,
            tool_choice="auto",
            temperature=0.3,
            max_tokens=budget,
        )
        choice = resp.choices[0]
        if choice.finish_reason != "length":
            break
        grown = min(budget * 2, _TOKEN_RETRY_CEILING)
        if attempt == 0 and grown > budget:
            print(f"[continuous_agent] [{name}] completion truncated "
                  f"(finish_reason=length); retrying with max_tokens {budget}->{grown}")
            budget = grown
            continue
        print(f"[continuous_agent] [{name}] completion STILL truncated at "
              f"max_tokens={budget} — proceeding with the partial result.")
        break
    msg = choice.message
    tool_calls = []
    for tc in (msg.tool_calls or []):
        tool_calls.append({
            "id": tc.id,
            "name": tc.function.name,
            "arguments": _parse_args(tc.function.arguments),
        })
    u = getattr(resp, "usage", None)
    return {
        "content": (msg.content or None),
        "tool_calls": tool_calls,
        "raw": msg.content or "",
        "error": None,
        "usage": _usage_dict(getattr(u, "prompt_tokens", 0),
                             getattr(u, "completion_tokens", 0)) if u else _usage_dict(),
    }


def _anthropic_tool_step(messages, tools, agent_cfg) -> Dict[str, Any]:
    name = agent_cfg.get("subagent_name", "Unknown")
    model = agent_cfg.get("llm_model", "claude-sonnet-4-6")
    if anthropic is None:
        return {"content": None, "tool_calls": [], "raw": "", "error": "anthropic lib not installed"}

    api_key = os.environ.get(agent_cfg.get("api_key_env", "ANTHROPIC_API_KEY"))
    if not api_key:
        return {"content": None, "tool_calls": [], "raw": "",
                "error": f"missing API key ({agent_cfg.get('api_key_env', 'ANTHROPIC_API_KEY')})"}

    base_url = agent_cfg.get("llm_endpoint")
    client = anthropic.Anthropic(api_key=api_key, base_url=base_url) if base_url else anthropic.Anthropic(api_key=api_key)

    system_prompt, conversation = _to_anthropic_messages(messages)
    anth_tools = [
        {
            "name": t["function"]["name"],
            "description": t["function"]["description"],
            "input_schema": t["function"]["parameters"],
        }
        for t in tools
    ]

    # Prompt caching (first-party Anthropic client, GA — no beta header):
    # cache tools + system together by marking the last system block ephemeral.
    # Render order is tools -> system -> messages, so this breakpoint caches the
    # frozen tool list and the (stable) system prompt as one prefix.
    system_param = (
        [{"type": "text", "text": system_prompt, "cache_control": {"type": "ephemeral"}}]
        if system_prompt else anthropic.NOT_GIVEN
    )
    # Second breakpoint on the growing transcript: mark the last content block of
    # the last message ephemeral so each of the up-to-8 steps reads the prior turns
    # from cache. Wrap a plain-string content as a single text block first.
    if conversation:
        last = conversation[-1]
        content = last.get("content")
        if isinstance(content, str):
            last["content"] = [{"type": "text", "text": content,
                                "cache_control": {"type": "ephemeral"}}]
        elif isinstance(content, list) and content and isinstance(content[-1], dict):
            content[-1]["cache_control"] = {"type": "ephemeral"}

    # Self-heal truncation (mirrors the OpenAI path): a call cut off at
    # stop_reason == "max_tokens" loses its tool-input tail. Retry ONCE with a
    # doubled, capped budget before accepting the partial result.
    budget = _max_tokens(agent_cfg)
    resp = None
    for attempt in range(2):
        resp = client.messages.create(
            model=model,
            system=system_param,
            messages=conversation,
            tools=anth_tools,
            temperature=0.3,
            max_tokens=budget,
        )
        if resp.stop_reason != "max_tokens":
            break
        grown = min(budget * 2, _TOKEN_RETRY_CEILING)
        if attempt == 0 and grown > budget:
            print(f"[continuous_agent] [{name}] completion truncated "
                  f"(stop_reason=max_tokens); retrying with max_tokens {budget}->{grown}")
            budget = grown
            continue
        print(f"[continuous_agent] [{name}] completion STILL truncated at "
              f"max_tokens={budget} — proceeding with the partial result.")
        break
    text_parts, tool_calls = [], []
    for block in resp.content:
        if block.type == "text":
            text_parts.append(block.text)
        elif block.type == "tool_use":
            tool_calls.append({
                "id": block.id,
                "name": block.name,
                "arguments": block.input if isinstance(block.input, dict) else {},
            })
    content = "".join(text_parts).strip() or None
    u = getattr(resp, "usage", None)
    usage = (_usage_dict(getattr(u, "input_tokens", 0), getattr(u, "output_tokens", 0))
             if u else _usage_dict())
    # Cache-hit visibility: nonzero across repeated steps confirms the two
    # breakpoints above are serving prior turns from cache.
    usage["cache_read_input_tokens"] = int(getattr(u, "cache_read_input_tokens", 0) or 0) if u else 0
    return {"content": content, "tool_calls": tool_calls, "raw": content or "", "error": None,
            "usage": usage}


def _text_tool_step(messages, tools, agent_cfg) -> Dict[str, Any]:
    """ReAct-style fallback: render tools into the prompt, parse a JSON reply.

    For providers/models without reliable native tool calling (e.g. Ollama). The
    model returns ONE JSON object: either {"tool": name, "arguments": {...}} or
    {"final": "message"}. We normalize that into the same shape as native calls.
    """
    provider = (agent_cfg.get("llm_provider") or "ollama").lower()
    tool_doc = _render_tools_for_text(tools)
    transcript = _render_messages_for_text(messages)

    instruction = (
        "You are an agent that acts by emitting exactly ONE JSON object and nothing else.\n"
        "Choose one of the available tools, or finish.\n\n"
        f"AVAILABLE TOOLS:\n{tool_doc}\n\n"
        "Respond with a single JSON object, one of:\n"
        '  {\"tool\": \"<tool_name>\", \"arguments\": { ... }}\n'
        '  {\"final\": \"<message to the director>\"}\n'
        "Do not wrap it in markdown fences. Do not add commentary.\n\n"
        f"CONVERSATION SO FAR:\n{transcript}\n\n"
        "Your JSON response:"
    )

    raw = _plain_completion(instruction, agent_cfg, provider)
    parsed = _extract_json_object(raw)
    if parsed is None:
        # Couldn't parse — treat the whole text as a final message so the loop ends gracefully.
        return {"content": raw.strip() or None, "tool_calls": [], "raw": raw, "error": None}

    if "final" in parsed:
        return {"content": str(parsed.get("final") or "").strip() or None,
                "tool_calls": [], "raw": raw, "error": None}

    tool_name = parsed.get("tool")
    args = parsed.get("arguments")
    if not isinstance(args, dict):
        args = {}
    if not tool_name:
        return {"content": raw.strip() or None, "tool_calls": [], "raw": raw, "error": None}
    # Synthesize a stable call id (loop appends this to the OpenAI-shaped history).
    call_id = f"text_call_{abs(hash(raw)) % 10_000_000}"
    return {
        "content": None,
        "tool_calls": [{"id": call_id, "name": tool_name, "arguments": args}],
        "raw": raw,
        "error": None,
    }


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _parse_args(raw_args) -> dict:
    if isinstance(raw_args, dict):
        return raw_args
    if not raw_args:
        return {}
    try:
        val = json.loads(raw_args)
        return val if isinstance(val, dict) else {}
    except (json.JSONDecodeError, TypeError):
        return {}


def _to_anthropic_messages(messages: List[dict]):
    """Translate OpenAI-shaped messages to (system_prompt, anthropic_messages).

    - system message -> returned separately
    - assistant with tool_calls -> content blocks incl. tool_use
    - role "tool" -> a user turn carrying tool_result block(s); consecutive tool
      results are merged into one user turn (Anthropic groups them).
    """
    system_prompt = ""
    out: List[dict] = []
    for m in messages:
        role = m.get("role")
        if role == "system":
            system_prompt = m.get("content") or ""
            continue
        if role == "tool":
            block = {
                "type": "tool_result",
                "tool_use_id": m.get("tool_call_id"),
                "content": m.get("content") or "",
            }
            if out and out[-1]["role"] == "user" and isinstance(out[-1]["content"], list):
                out[-1]["content"].append(block)
            else:
                out.append({"role": "user", "content": [block]})
            continue
        if role == "assistant" and m.get("tool_calls"):
            blocks = []
            if m.get("content"):
                blocks.append({"type": "text", "text": m["content"]})
            for tc in m["tool_calls"]:
                blocks.append({
                    "type": "tool_use",
                    "id": tc["id"],
                    "name": tc["function"]["name"],
                    "input": _parse_args(tc["function"]["arguments"]),
                })
            out.append({"role": "assistant", "content": blocks})
            continue
        # plain user / assistant text
        out.append({"role": role, "content": m.get("content") or ""})
    return system_prompt, out


def _render_tools_for_text(tools: List[dict]) -> str:
    lines = []
    for t in tools:
        fn = t["function"]
        params = fn.get("parameters", {}).get("properties", {})
        param_str = ", ".join(params.keys()) if params else "(none)"
        lines.append(f"- {fn['name']}({param_str}): {fn['description']}")
    return "\n".join(lines)


def _render_messages_for_text(messages: List[dict]) -> str:
    lines = []
    for m in messages:
        role = m.get("role")
        if role == "system":
            lines.append(f"[SYSTEM]\n{m.get('content','')}")
        elif role == "tool":
            lines.append(f"[TOOL RESULT]\n{m.get('content','')}")
        elif role == "assistant" and m.get("tool_calls"):
            calls = ", ".join(
                f"{tc['function']['name']}({tc['function']['arguments']})" for tc in m["tool_calls"]
            )
            if m.get("content"):
                lines.append(f"[YOU] {m['content']}")
            lines.append(f"[YOU called] {calls}")
        else:
            tag = "YOU" if role == "assistant" else "DIRECTOR" if role == "user" else role
            lines.append(f"[{tag}] {m.get('content','')}")
    return "\n\n".join(lines)


def _plain_completion(prompt: str, agent_cfg: dict, provider: str) -> str:
    """One plain-text completion, provider-appropriate (text fallback path)."""
    name = agent_cfg.get("subagent_name", "Unknown")
    model = agent_cfg.get("llm_model", "llama3.1")
    max_tokens = _max_tokens(agent_cfg)
    try:
        if provider == "ollama":
            import ollama as _ollama
            resp = _ollama.chat(
                model=model,
                messages=[{"role": "user", "content": prompt}],
                options={"temperature": 0.3, "num_predict": max_tokens},
            )
            return resp["message"]["content"]
        # OpenAI-compatible fallback (also lets us force text mode on a gateway).
        api_key = os.environ.get(agent_cfg.get("api_key_env", "OPENAI_API_KEY"))
        base_url = agent_cfg.get("llm_endpoint")
        client = openai.OpenAI(api_key=api_key, base_url=base_url) if base_url else openai.OpenAI(api_key=api_key)
        resp = client.chat.completions.create(
            model=model,
            messages=[{"role": "user", "content": prompt}],
            temperature=0.3,
            max_tokens=max_tokens,
        )
        return resp.choices[0].message.content or ""
    except Exception as e:  # noqa: BLE001
        print(f"[continuous_agent] [{name}] text completion failed: {e}")
        return ""


def _extract_json_object(text: str) -> Optional[dict]:
    """Pull the first balanced top-level JSON object out of a text blob."""
    if not text:
        return None
    # Fast path: whole string is JSON.
    stripped = text.strip()
    # Strip common markdown fences.
    if stripped.startswith("```"):
        stripped = stripped.strip("`")
        nl = stripped.find("\n")
        if nl != -1:
            stripped = stripped[nl + 1:]
    try:
        val = json.loads(stripped)
        if isinstance(val, dict):
            return val
    except json.JSONDecodeError:
        pass
    # Scan for the first balanced {...}.
    depth = 0
    start = -1
    in_str = False
    esc = False
    for i, ch in enumerate(text):
        if in_str:
            if esc:
                esc = False
            elif ch == "\\":
                esc = True
            elif ch == '"':
                in_str = False
            continue
        if ch == '"':
            in_str = True
        elif ch == "{":
            if depth == 0:
                start = i
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0 and start != -1:
                candidate = text[start:i + 1]
                try:
                    val = json.loads(candidate)
                    if isinstance(val, dict):
                        return val
                except json.JSONDecodeError:
                    start = -1
    return None
