"""Server-side provider registry — the security boundary for LLM endpoints/secrets.

Bundles (and, after migration, all configs) name a provider by a fixed **enum**, never a
raw endpoint or secret. The router resolves the enum here to a concrete
``(backend, base_url, key_env)`` triple. Two properties this buys us:

* **No SSRF / credential exfil from uploaded content.** A contributor can only pick a name
  from `Provider`; they cannot point `base_url` at an arbitrary host, and they can never name
  an env var whose secret we would read and forward. The actual secret stays in the server's
  environment — this registry only ever names the *env var*, never its value.
* **One place to add/audit a provider.** Adding an endpoint is a reviewed edit to this file,
  not a field an uploader controls (the Hydra ``_target_`` / ``trust_remote_code`` anti-pattern).

See docs/CORA_API_v1.md §4. Enum values below are derived from the endpoint/key combos that
existed across config/*.json at migration time.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Optional


class Provider(str, Enum):
    """Stable provider names a bundle/config may reference. `str`-Enum so it serializes to
    plain JSON and compares equal to its string value."""
    anthropic = "anthropic"
    anthropic_ddmlab = "anthropic-ddmlab"
    cmu_gateway = "cmu-gateway"
    openai = "openai"
    ollama_local = "ollama-local"


@dataclass(frozen=True)
class ProviderSpec:
    """How to construct a client for a provider. `backend` is the value the router's
    client-construction switch keys on today ("anthropic" | "openai" | "ollama"); `base_url`
    and `key_env` replace the old per-config ``llm_endpoint`` / ``api_key_env`` fields.

    `key_env` names an environment variable — never the secret itself.
    """
    backend: str
    base_url: Optional[str]
    key_env: Optional[str]


PROVIDER_REGISTRY: dict[Provider, ProviderSpec] = {
    Provider.anthropic:        ProviderSpec("anthropic", None, "ANTHROPIC_API_KEY"),
    Provider.anthropic_ddmlab: ProviderSpec("anthropic", None, "DDMLAB_ANTHROPIC_API_KEY"),
    Provider.cmu_gateway:      ProviderSpec("openai", "https://ai-gateway.andrew.cmu.edu/v1", "OPENAI_API_KEY"),
    Provider.openai:           ProviderSpec("openai", None, "OPENAI_API_KEY"),
    # ollama runs OpenAI-compatible on localhost; no key. base_url explicit so the resolved
    # triple is self-contained regardless of any caller-side default.
    Provider.ollama_local:     ProviderSpec("ollama", "http://localhost:11434/v1", None),
}


def resolve(provider: "Provider | str") -> ProviderSpec:
    """Resolve a provider enum (or its string value) to a ProviderSpec.

    Raises KeyError with the valid set on an unknown name — an uploaded config that names an
    unregistered provider is rejected loudly, never silently defaulted.
    """
    if isinstance(provider, str) and not isinstance(provider, Provider):
        try:
            provider = Provider(provider)
        except ValueError:
            raise KeyError(
                f"unknown provider {provider!r}; registered: {[p.value for p in Provider]}"
            )
    return PROVIDER_REGISTRY[provider]


def is_valid(name: str) -> bool:
    return name in (p.value for p in Provider)


def valid_names() -> list[str]:
    return [p.value for p in Provider]
