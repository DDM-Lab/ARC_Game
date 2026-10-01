"""LLM access shared by every front end: providers, keys, one client factory, response helpers.

    providers.py        the provider enum: the security boundary for endpoints and secrets
                        (configs and bundles name a provider, never a URL or an env var)
    anthropic_native.py the native Anthropic SDK behind the OpenAI-shaped client surface
                        (prompt caching, which Anthropic's OpenAI-compat layer does not support)

client_for() returns an OpenAI-shaped client (`.chat.completions.create`) for any provider, so the
caller's request code is the same for the gateway, OpenAI, Anthropic and local servers.
"""
from __future__ import annotations

import os
from pathlib import Path
from typing import Optional

from cora.llm.providers import GATEWAY_BASE, Provider, ProviderSpec, resolve

REPO_ROOT = Path(__file__).resolve().parents[2]
_LOOPBACK = ("127.0.0.1", "localhost")


def _dotenv(name: str) -> Optional[str]:
    path = REPO_ROOT / ".env"
    if path.exists():
        for line in path.read_text().splitlines():
            if line.startswith(name + "="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    return None


def resolve_api_key(key_env: Optional[str], base_url: Optional[str] = None,
                    default_env: Optional[str] = None) -> str:
    """The API key for an endpoint: the named env var (or the repo's .env), else a placeholder
    for a keyless loopback server, else `default_env`. "" when nothing is found.

    Two traps this handles:
      * a keyless provider stores key_env = null, so `cfg.get("api_key_env", DEFAULT)` returns
        None, not the default; callers pass the stored value and this treats None as "unnamed";
      * a loopback server never gets a real hosted key (OPENAI_API_KEY would otherwise be sent
        to a local process); it gets a placeholder, since the OpenAI client needs a non-empty one.
    """
    if key_env:
        return os.environ.get(key_env) or _dotenv(key_env) or ""
    if any(h in (base_url or "") for h in _LOOPBACK):
        return "local"
    if default_env:
        return os.environ.get(default_env) or _dotenv(default_env) or ""
    return ""


def client_for(provider: "Provider | str | ProviderSpec", api_key: Optional[str] = None):
    """An OpenAI-shaped client for a provider (or an explicit ProviderSpec, for operator-given
    endpoints such as the benchmark's --base-url). Anthropic uses the native SDK adapter."""
    spec = provider if isinstance(provider, ProviderSpec) else resolve(provider)
    key = api_key or resolve_api_key(spec.key_env, spec.base_url)
    if not key:
        raise RuntimeError(f"no API key: set {spec.key_env or 'one'} in the environment or the repo's .env")
    if spec.backend == "anthropic":
        from cora.llm.anthropic_native import Client
        return Client(api_key=key)
    import openai
    return openai.OpenAI(api_key=key, base_url=spec.base_url)


def reasoning_of(response) -> tuple:
    """(reasoning_trace, reasoning_tokens) from a chat completion. Providers put hidden thinking in
    non-standard fields (vLLM's reasoning parser: reasoning_content; some gateways: reasoning);
    the token count comes from usage. Both None when the provider exposes neither."""
    m = response.choices[0].message
    extra = getattr(m, "model_extra", None) or {}
    trace = (getattr(m, "reasoning_content", None) or getattr(m, "reasoning", None)
             or extra.get("reasoning_content") or extra.get("reasoning"))
    det = getattr(getattr(response, "usage", None), "completion_tokens_details", None)
    tokens = getattr(det, "reasoning_tokens", None) if det else None
    return (trace if isinstance(trace, str) else None), tokens


def accepts_temperature(model: str) -> bool:
    """Next-generation Claude models reject `temperature` (400: "`temperature` is deprecated for
    this model"); older models accept it."""
    m = (model or "").lower()
    return not any(tok in m for tok in ("sonnet-5", "opus-5", "haiku-5", "fable-5", "mythos-5"))


__all__ = ["GATEWAY_BASE", "Provider", "ProviderSpec", "REPO_ROOT", "accepts_temperature",
           "client_for", "reasoning_of", "resolve", "resolve_api_key"]
