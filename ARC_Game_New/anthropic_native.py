"""Native Anthropic client presented as an OpenAI-shaped `.chat.completions.create()`.

WHY THIS EXISTS
The benchmark harness speaks OpenAI-compatible HTTP so one code path covers local vLLM and
API models. Anthropic's OpenAI compatibility layer, however, documents "Prompt caching is not
supported" — and this workload re-sends ~1,708 tokens of identical system prompt + tool schema
on every one of 1,024 rounds (~70% of all input tokens). That is the single biggest cost item,
so the Claude path needs the native SDK.

Rather than fork ask_tools()/chat(), this exposes the same duck-typed surface the harness already
calls, so the prompt text, tool schema, observation encoder and episode loop are byte-identical
to every other run — only the transport changes.

Caching: render order is tools -> system -> messages, so a single cache_control breakpoint on the
last system block caches the tools schema AND the system prompt together. Verify with
usage.cache_read_input_tokens (the compat layer's prompt_tokens_details is always empty, which is
why cache hits could not be confirmed there at all).
"""
import json
import os
import json as _json

import anthropic


def _to_anthropic_tools(oai_tools):
    """OpenAI {'type':'function','function':{name,description,parameters}} -> Anthropic tool."""
    out = []
    for t in oai_tools or []:
        f = t.get("function", t)
        out.append({"name": f["name"], "description": f.get("description", ""),
                    "input_schema": f.get("parameters") or {"type": "object", "properties": {}}})
    return out


def _text_of(content):
    """Harness content is a plain string (image_mode=none) or a list of OpenAI blocks."""
    if isinstance(content, str):
        return content
    parts = []
    for b in content or []:
        if isinstance(b, dict) and b.get("type") == "text":
            parts.append(b.get("text", ""))
    return "\n".join(parts)


class _Fn:
    def __init__(self, name, arguments):
        self.name = name
        self.arguments = arguments


class _ToolCall:
    def __init__(self, id, name, arguments):
        self.id = id
        self.type = "function"
        self.function = _Fn(name, arguments)


class _Msg:
    def __init__(self, content, tool_calls):
        self.role = "assistant"
        self.content = content
        self.tool_calls = tool_calls or None


class _Choice:
    def __init__(self, msg, finish_reason):
        self.index = 0
        self.message = msg
        self.finish_reason = finish_reason


class _Usage:
    def __init__(self, u):
        self.prompt_tokens = getattr(u, "input_tokens", 0)
        self.completion_tokens = getattr(u, "output_tokens", 0)
        self.total_tokens = self.prompt_tokens + self.completion_tokens
        # Anthropic-only, surfaced so runs can prove caching actually engaged.
        self.cache_read_input_tokens = getattr(u, "cache_read_input_tokens", 0) or 0
        self.cache_creation_input_tokens = getattr(u, "cache_creation_input_tokens", 0) or 0


class _Response:
    def __init__(self, resp):
        blocks = resp.content or []
        text = "".join(b.text for b in blocks if getattr(b, "type", None) == "text")
        calls = [_ToolCall(b.id, b.name, json.dumps(b.input))
                 for b in blocks if getattr(b, "type", None) == "tool_use"]
        fr = "tool_calls" if calls else ("length" if resp.stop_reason == "max_tokens" else "stop")
        self.id = resp.id
        self.model = resp.model
        self.choices = [_Choice(_Msg(text, calls), fr)]
        self.usage = _Usage(resp.usage)
        self.stop_reason = resp.stop_reason


class _Completions:
    def __init__(self, client):
        self._c = client

    def create(self, *, model, messages, max_tokens=2000, tools=None, temperature=None, **kw):
        # Params the compat layer silently ignored and the native API rejects outright.
        kw.pop("reasoning_effort", None)
        kw.pop("extra_body", None)          # chat_template_kwargs is a vLLM concept
        kw.pop("max_completion_tokens", None)

        # Preserve tool_calls / tool results instead of flattening every turn to plain text.
        # The OpenAI shape (assistant.tool_calls + role="tool" replies) has no Anthropic
        # equivalent by that name: assistant tool calls become `tool_use` content blocks and
        # tool replies become a USER message of `tool_result` blocks. Flattening them dropped the
        # calls entirely, which is the same defect the benchmark harness had -- a model replaying
        # its own history saw prose where its actions should be and started imitating the prose.
        system_txt, conv = [], []
        for m in messages:
            role = m.get("role")
            if role in ("system", "developer"):
                system_txt.append(_text_of(m.get("content")))
            elif role == "tool":
                block = {"type": "tool_result", "tool_use_id": m.get("tool_call_id"),
                         "content": _text_of(m.get("content")) or "ok"}
                if conv and conv[-1]["role"] == "user" and isinstance(conv[-1]["content"], list):
                    conv[-1]["content"].append(block)          # merge consecutive tool results
                else:
                    conv.append({"role": "user", "content": [block]})
            elif role == "assistant" and m.get("tool_calls"):
                blocks = []
                txt = _text_of(m.get("content"))
                if txt:
                    blocks.append({"type": "text", "text": txt})
                for tc in m["tool_calls"]:
                    fn = tc["function"] if isinstance(tc, dict) else tc.function
                    name = fn["name"] if isinstance(fn, dict) else fn.name
                    raw = fn["arguments"] if isinstance(fn, dict) else fn.arguments
                    try:
                        args = _json.loads(raw) if isinstance(raw, str) else (raw or {})
                    except Exception:
                        args = {}
                    blocks.append({"type": "tool_use",
                                   "id": (tc["id"] if isinstance(tc, dict) else tc.id),
                                   "name": name, "input": args})
                conv.append({"role": "assistant", "content": blocks})
            else:
                conv.append({"role": role, "content": _text_of(m.get("content"))})

        system = None
        if system_txt:
            system = [{"type": "text", "text": "\n".join(system_txt),
                       "cache_control": {"type": "ephemeral"}}]  # caches tools + system

        req = dict(model=model, max_tokens=max_tokens, messages=conv)
        if system:
            req["system"] = system
        if tools:
            req["tools"] = _to_anthropic_tools(tools)
        # Claude 5 rejects temperature; only forward when explicitly asked for.
        if temperature is not None and not model.startswith(("claude-sonnet-5", "claude-opus-5", "claude-fable-5")):
            req["temperature"] = temperature
        # ARC_ANTHROPIC_THINKING: an INTEGER token budget (the only form Claude 4.5 accepts --
        # `adaptive` and `output_config.effort` both 400 on claude-haiku-4-5), or adaptive/off for
        # the Claude 5 models that do support them. max_tokens must exceed budget_tokens, so raise
        # the ceiling rather than let the API reject the pair.
        think = os.environ.get("ARC_ANTHROPIC_THINKING", "").strip().lower()
        if think.isdigit():
            budget = int(think)
            req["thinking"] = {"type": "enabled", "budget_tokens": budget}
            if req["max_tokens"] <= budget:
                req["max_tokens"] = budget + 2048
            req.pop("temperature", None)      # thinking requires the default temperature
        elif think in ("adaptive", "on"):
            req["thinking"] = {"type": "adaptive"}
        elif think in ("off", "disabled"):
            req["thinking"] = {"type": "disabled"}
        eff = os.environ.get("ARC_ANTHROPIC_EFFORT", "").strip().lower()
        if eff:
            req["output_config"] = {"effort": eff}
        return _Response(self._c.messages.create(**req))


class _Chat:
    def __init__(self, client):
        self.completions = _Completions(client)


class Client:
    """Duck-typed stand-in for openai.OpenAI."""

    def __init__(self, api_key=None):
        self._c = anthropic.Anthropic(api_key=api_key)
        self.chat = _Chat(self._c)
