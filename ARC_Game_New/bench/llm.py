"""The LLM policy: one model call per decision — system prompt, tool schema, observation.

The model may reason, then emits tool calls (game actions only); cora.executor runs them and the
tools return nothing to the model. Reasoning traces are captured for the record.
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Optional

from cora.llm import reasoning_of


@dataclass(frozen=True)
class LocalOptions:
    """Request knobs for a local OpenAI-compatible server (Ollama, vLLM); unused on hosted APIs.

    reasoning_effort      forwarded as-is. Ollama AUTO-ENABLES thinking on reasoning-capable models
                          (qwen3, qwen3.5, gpt-oss) unless the request carries it; "none" turns it
                          off, low/medium/high cap it. Hosted APIs never get it (Claude rejects it).
    max_tokens            total generation budget (reasoning + answer); None = 2000.
    chat_template_kwargs  e.g. {"enable_thinking": False}. Measured on qwen3:4b (real tools
                          prompt): completion tokens are ~3-5k either way; enable_thinking=false
                          only moves the chain-of-thought out of `content` (16k chars -> ~94). It
                          buys readable transcripts, not speed.
    tool_rounds           model replies per decision (default 1). A sequential tool caller makes one
                          call and waits for its result before the next: gpt-oss sent exactly one
                          call on all 898 turns of a 32-game run while its reasoning planned several.
                          With N > 1 each call is answered "queued" and the model may keep calling,
                          up to N replies; every call still runs together when the turn ends.
    """
    reasoning_effort: Optional[str] = None
    max_tokens: Optional[int] = None
    chat_template_kwargs: Optional[dict] = None
    tool_rounds: int = 1


def is_anthropic(model):
    m = model.lower()
    return "anthropic" in m or "claude" in m


ANTHROPIC_TEMP_MAX = 1.0   # Bedrock/Anthropic reject temperature > 1.0 (gpt-5* ignore temp; gemini allows >1)


def _user_msg(text, image_b64=None):
    """Build a user message, multimodal when an image is supplied. The image is a
    decision-time view of the same state (synthetic dashboard or real game frame)."""
    if not image_b64:
        return {"role": "user", "content": text}
    return {"role": "user", "content": [
        {"type": "text", "text": text},
        {"type": "image_url", "image_url": {"url": f"data:image/png;base64,{image_b64}"}}]}


def ask_tools(client, model, system_text, tools, user_text, image_b64=None, temperature=None,
              history=None, local: Optional[LocalOptions] = None):
    """One model call: system prompt + tool schema + the turn's user message (rl.CoraEnv builds
    all three); the model may reason, then emits tool calls. Returns (decision, raw_content,
    reasoning_trace, reasoning_tokens, None): the calls go to cora.executor in the round loop,
    which also decides parsed_ok (whether every call was well-formed)."""
    msgs = [{"role": "system", "content": system_text}]
    if history:
        msgs.extend(history)
    msgs.append(_user_msg(user_text, image_b64))
    kw = dict(model=model, messages=msgs, tools=tools, max_tokens=2000)
    # LOCAL path: a reasoning-capable local model auto-enables thinking unless reasoning_effort
    # is sent, and several emit a long prose preamble BEFORE the tool call — a hard 2000 cap
    # truncates them before any tool_call is produced. `local` is None on hosted APIs.
    local = local or LocalOptions()
    # MEASURED CONFLICT: sending reasoning_effort ALONGSIDE enable_thinking=false defeats it
    # -- Ollama then leaks the whole chain-of-thought back into `content` (median 16.6k chars
    # over 3 samples, vs 0 with the template kwarg alone). Same token count either way, so
    # this is purely about which channel it lands in. enable_thinking wins when both are set.
    if local.reasoning_effort is not None and not local.chat_template_kwargs:
        kw["reasoning_effort"] = local.reasoning_effort
    if local.max_tokens is not None:
        kw["max_tokens"] = local.max_tokens
    if local.chat_template_kwargs:
        # MUST go through extra_body: the OpenAI SDK rejects unknown top-level params, and
        # passing it directly got silently swallowed by the retry handler -- the run looked
        # fine and thinking stayed ON (16k chars of content instead of ~0).
        kw["extra_body"] = {**kw.get("extra_body", {}),
                            "chat_template_kwargs": local.chat_template_kwargs}
    if temperature is not None:
        kw["temperature"] = temperature
    def create():
        try:
            return client.chat.completions.create(**kw)
        except Exception as e:
            emsg = str(e).lower()
            if "temperature" in emsg:
                kw.pop("temperature", None)
            if "max_tokens" in emsg or "max_completion_tokens" in emsg:
                kw.pop("max_tokens", None)
                kw["max_completion_tokens"] = local.max_tokens or 2000
            if ("reasoning" in emsg or "think" in emsg) and "reasoning_effort" in kw:
                kw.pop("reasoning_effort", None)   # non-thinking model rejects the knob
            if "chat_template" in emsg or "template" in emsg:
                kw.pop("extra_body", None)
            return client.chat.completions.create(**kw)

    # One reply per decision, or up to local.tool_rounds for a sequential tool caller: each
    # call is acknowledged as queued and the model may add more; all of them run at turn end.
    turn, contents, raw_tcs, traces, rtok = [], [], [], [], None
    for k in range(max(1, local.tool_rounds)):
        r = create()
        m = r.choices[0].message
        tcs = getattr(m, "tool_calls", None) or []
        for tc in tcs:
            # vLLM's harmony parser (gpt-oss) sometimes keeps the channel header in the name:
            # "task<|channel|>commentary" -- 192 of 2,095 gpt-oss calls rejected as unknown tools.
            if tc.function.name and "<|" in tc.function.name:
                tc.function.name = tc.function.name.split("<|", 1)[0].strip()
        contents.append(m.content or "")
        raw_tcs.extend(tcs)
        tr, tk = reasoning_of(r)
        if tr:
            traces.append(tr)
        if tk is not None:
            rtok = (rtok or 0) + tk
        # HISTORY SERIALIZATION BUG (fixed): a pure tool-call reply used to be recorded as if
        # the assistant had SPOKEN command-tag text. The model then few-shot-imitated its own
        # apparent output format and emitted "<staff>Kitchen_0,4</staff>" as plain text -- which
        # the executor never reads (it only looks at message.tool_calls), so the action was
        # silently dropped. Self-reinforcing: once it emits text, content is non-empty, so
        # history keeps teaching text-mode. Measured on 32-episode runs: Qwen3-4B h=4 937/1024
        # rounds (91.5%) corrupted, Qwen3-14B 397 (38.8%), Qwen3.5-27B 153 (14.9%), and 0/1024
        # at history=1 -- median onset round 2. Record the real tool_calls instead, so the
        # replayed history shows the model the channel it must actually use.
        if not tcs:
            turn.append({"role": "assistant", "content": m.content or ""})
            break
        turn.append({"role": "assistant", "content": m.content or None,
                     "tool_calls": [{"id": tc.id, "type": "function",
                                     "function": {"name": tc.function.name,
                                                  "arguments": tc.function.arguments}}
                                    for tc in tcs]})
        last = k + 1 >= max(1, local.tool_rounds)
        ack = "ok" if last else ("queued; it runs when the turn ends. Call any further actions now, "
                                  "or reply without a tool call to end the turn.")
        turn.extend({"role": "tool", "tool_call_id": tc.id, "content": ack} for tc in tcs)
        if last:
            break
        kw["messages"] = msgs + turn
    content = "\n".join(c for c in contents if c)

    if history is not None:
        history.append(_user_msg(user_text, None))
        history.extend(turn)
    reason = ""
    mr = re.search(r"REASONING:\s*(.+)", content or "")
    if mr:
        reason = mr.group(1).splitlines()[0].strip()
    # Run by executor.execute_turn in the round loop; nothing is returned to the model.
    # Zero calls is a deliberate no-op, not a failure.
    dec = {"tool_calls": list(raw_tcs), "reasoning": reason}
    rtrace = "\n".join(traces) if traces else None
    return dec, content, rtrace, rtok, None
