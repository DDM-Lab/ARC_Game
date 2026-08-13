"""Load + compose contributor bundles. ONE code path shared by the git-native loader, the
(later) live upload endpoint, and the CLI — validate → compose base+delta → re-validate.

A bundle is either:
  * **full**  — carries a complete `config` (CoraConfig), used as-is; or
  * **delta** — carries a `delta` (overrides) that layers onto a maintained base config (override,
    don't fork — RimWorld PatchOperation / Factorio data.raw).

`load_bundle` returns a plain dict that is a valid `CoraConfig` (provider named by enum). Handing
that dict to the runtime (`agent_config.load_config`) happens after the provider-enum migration
(see docs/contributor-platform-design.md, Phase 1 wiring); bundle.py itself stays runtime-agnostic
and fully unit-testable.
"""
from __future__ import annotations

import json
import warnings
from pathlib import Path
from typing import Optional, Union

from pydantic import ValidationError

from cora_schema import CORA_API_VERSION, Bundle, CoraConfig

Src = Union[str, Path, dict]


class BundleError(ValueError):
    """Raised when a bundle is malformed, incompatible, or fails composition."""


def _load_json(src: Src) -> dict:
    if isinstance(src, dict):
        return src
    text = Path(src).read_text()
    try:
        return json.loads(text)
    except json.JSONDecodeError as e:
        raise BundleError(f"bundle is not valid JSON: {e}") from e


def _check_api_version(bundle_version: str) -> None:
    """Refuse on MAJOR mismatch, warn if the bundle targets a newer MINOR than the server."""
    try:
        b_major, b_minor = (int(x) for x in bundle_version.split(".")[:2])
        s_major, s_minor = (int(x) for x in CORA_API_VERSION.split(".")[:2])
    except (ValueError, IndexError):
        raise BundleError(f"unparseable cora_api_version {bundle_version!r}")
    if b_major != s_major:
        raise BundleError(
            f"bundle targets CORA API {bundle_version} but server is {CORA_API_VERSION} "
            f"(major mismatch — refusing)")
    if b_minor > s_minor:
        warnings.warn(
            f"bundle targets CORA API {bundle_version}, newer than server {CORA_API_VERSION}; "
            f"features it relies on may be absent", stacklevel=2)


def _merge_agents(base_agents: list[dict], overrides: list[dict]) -> list[dict]:
    """Apply each override to the base officer with the same `subagent_name`. Overriding an
    officer that isn't in the base is an error (typo/identity protection)."""
    by_name = {a["subagent_name"]: dict(a) for a in base_agents}
    for ov in overrides:
        name = ov["subagent_name"]
        if name not in by_name:
            raise BundleError(
                f"delta overrides officer {name!r} which is not in the base config "
                f"(base has: {sorted(by_name)})")
        target = by_name[name]
        for k, v in ov.items():
            if k == "subagent_name" or v is None:
                continue
            target[k] = v          # list/scalar fields REPLACE (documented semantics)
    return list(by_name.values())


# Runtime-only officer fields that the UPLOAD schema deliberately does not know about
# (they were replaced by the `provider` enum). See cora_schema's module docstring.
_LEGACY_PROVIDER_FIELDS = ("llm_provider", "llm_endpoint", "api_key_env", "llm_port")


def _sanitize_base(base_raw: dict) -> dict:
    """Drop NULL legacy provider fields from a base config before schema validation.

    A base config is a maintained RUNTIME file — the same one agent_config loads — so it can
    carry runtime-shaped keys. `CoraConfig` is the untrusted-UPLOAD gate with extra='forbid',
    so a leftover `"llm_provider": null` on the director made the whole base fail validation
    and every delta bundle against the shipped configs 422'd. The null value carries no
    information, so dropping it is safe and changes nothing about the composed result.

    Only NULL values are dropped. A base that names a real endpoint or secret env var still
    fails loudly — that is a base worth migrating, not one to silently paper over.
    """
    out = dict(base_raw)
    agents = []
    for a in out.get("agents") or []:
        if isinstance(a, dict):
            a = {k: v for k, v in a.items()
                 if not (k in _LEGACY_PROVIDER_FIELDS and v is None)}
        agents.append(a)
    if agents:
        out["agents"] = agents
    return out


def _compose(base: dict, delta: dict) -> dict:
    merged = dict(base)
    if delta.get("agent_order_rule") is not None:
        merged["agent_order_rule"] = delta["agent_order_rule"]
    if delta.get("agents"):
        merged["agents"] = _merge_agents(base["agents"], delta["agents"])
    return merged


def _attach_global_prompt(cfg: dict, bundle) -> None:
    """Carry the bundle's prompt overrides into the RUNTIME config dict.

    Without this the composed config drops them, so an uploaded bundle's prompts were
    silently ignored and every session on the server shared the one process-wide
    global_prompt_config.json — meaning two collaborators could not run different prompt
    conditions, which is the core prompt-experiment axis. The router reads these back out
    per session (agent_config.RouterConfig.global_prompt / .tool_policy)."""
    gp = getattr(bundle, "global_prompt", None)
    if gp and gp.get("enabled", True):
        # Keep whichever halves were supplied; omitted halves inherit the server default.
        keep = {k: gp[k] for k in ("behavior", "manual", "global_system_prompt")
                if gp.get(k)}
        if keep:
            cfg["global_prompt"] = keep
    tp = getattr(bundle, "tool_policy", None)
    if tp and tp.strip():
        cfg["tool_policy"] = tp
    ti = getattr(bundle, "turn_instructions", None)
    if ti:
        keep = {k: v for k, v in ti.items() if isinstance(v, str) and v.strip()}
        if keep:
            cfg["turn_instructions"] = keep
    td = getattr(bundle, "tool_descriptions", None)
    if td:
        keep = {k: v for k, v in td.items() if isinstance(v, str) and v.strip()}
        if keep:
            cfg["tool_descriptions"] = keep


def _known_tool_names() -> Optional[set]:
    """Built-in tool names, or None if the runtime isn't importable here.

    Lazy + guarded on purpose: bundle.py is deliberately runtime-agnostic and unit-testable
    without the provider SDKs continuous_agent pulls in. When the import isn't available we
    skip the tool-name check rather than fail the whole warning pass; the router, which always
    has the runtime, still performs it.
    """
    try:
        from continuous_agent import TOOL_SCHEMAS
        return set(TOOL_SCHEMAS)
    except Exception:
        return None


def config_warnings(cfg: dict) -> list[str]:
    """Non-fatal authoring problems that would otherwise show up as an officer that simply
    does nothing — with the reason visible only in a server log the uploader can't read.

    These are the exact traps a first-time contributor hits with the scaffold: an officer with
    no talkinghead_endpoint (its messages can never render in the 5-slot sidebar), and an
    officer whose action scope is so narrow it gets skipped outright at
    `no in-scope actions — skipping` before any LLM call. Warn, don't reject: a narrow scope
    can be deliberate, and only the uploader knows their intent.

    Lives here, next to load_bundle, so the CLI (`cora-bundle validate`) and the upload
    endpoint report the SAME problems. Previously only the upload path ran these, so a
    contributor validating locally got a clean "OK" for a config that could not work.
    """
    out: list[str] = []
    for a in cfg.get("agents") or []:
        if not isinstance(a, dict) or a.get("role") != "subagent":
            continue
        nm = a.get("subagent_name", "?")
        if not a.get("talkinghead_endpoint"):
            out.append(
                f"'{nm}' has no talkinghead_endpoint — it will run but its messages cannot "
                f"appear in the UI. Set one of: DisasterOfficer, FoodMassCare, "
                f"LodgingMassCare, WorkforceService, ExternalRelationship.")
        scope = a.get("subaction_space") or []
        cats = {e.get("category") for e in scope if isinstance(e, dict)}
        # task_choice-only officers idle until a matching task spawns; at day 1 round 0 there
        # are no tasks at all, so such an officer is skipped and looks broken.
        if scope and cats <= {"task_choice"}:
            out.append(
                f"'{nm}' can only answer task_choice actions, so it is SKIPPED in any round "
                f"with no matching task (including the start of a game) and will look inert. "
                f"Add a category like construction / worker_assignment / resource_transfer "
                f"if you want it active from round 1.")
        if not scope:
            out.append(f"'{nm}' has an empty subaction_space — it can never act.")
        if not a.get("provider"):
            out.append(f"'{nm}' has no provider — it will not be LLM-driven.")

    # A replaced tool policy is the mechanical contract. We do NOT reject it (informed
    # collaborators may legitimately rewrite the whole surface), but a rewrite that drops
    # these clauses reliably produces officers that hallucinate actions or act outside their
    # remit — and that quietly breaks cross-arm comparability, which is expensive to notice
    # later. Flag the specific omissions so the author can decide deliberately.
    tp = cfg.get("tool_policy")
    if tp:
        low = tp.lower()
        if "finish" not in low:
            out.append("tool_policy override never mentions `finish` — officers may run to "
                       "max_steps every turn instead of ending cleanly.")
        if "talk_to_director" not in low:
            out.append("tool_policy override never mentions `talk_to_director` — officers "
                       "may stop replying to the human.")
        if not any(k in low for k in ("never claim", "unless you actually", "did not happen",
                                      "do not pretend")):
            out.append("tool_policy override drops the anti-hallucination clause (\"never "
                       "claim to have built/hired/... unless a tool actually succeeded\") — "
                       "officers tend to narrate actions they never took.")
        if "remit" not in low and "lane" not in low:
            out.append("tool_policy override drops the stay-in-your-lane clause — officers "
                       "may act outside their configured remit.")
    # A tool_descriptions key that names no real tool is INERT — the reword silently does
    # nothing and the author only finds out by reading transcripts. Same failure shape as a
    # typo'd `tools` allowlist entry, so warn rather than reject.
    td = cfg.get("tool_descriptions") or {}
    known = _known_tool_names() if td else None
    if td and known is not None:
        unknown = sorted(k for k in td if k not in known)
        if unknown:
            out.append(f"tool_descriptions names unknown tool(s) {unknown} — those overrides "
                       f"do nothing. Valid names: {sorted(known)}")
    return out


def load_bundle(src: Src, base_config: Optional[Src] = None) -> dict:
    """Validate a bundle and return a runtime config dict (valid CoraConfig, provider-by-enum).

    * full bundle  → its `config`, validated.
    * delta bundle → requires `base_config` (path or dict of a valid CoraConfig); composes then
      RE-validates the merged result (an override that produces an invalid whole is rejected).
    """
    raw = _load_json(src)
    try:
        bundle = Bundle.model_validate(raw)
    except ValidationError as e:
        raise BundleError(f"bundle failed validation:\n{e}") from e

    _check_api_version(bundle.manifest.cora_api_version)

    if bundle.config is not None:
        out = bundle.config.model_dump(mode="json", by_alias=True, exclude_none=True)
        _attach_global_prompt(out, bundle)
        return out

    # delta path
    if base_config is None:
        raise BundleError(
            f"bundle {bundle.manifest.name!r} is a delta and needs a base config "
            f"(pass base_config=)")
    base_raw = _sanitize_base(_load_json(base_config))
    try:
        base = CoraConfig.model_validate(base_raw).model_dump(mode="json", by_alias=True,
                                                              exclude_none=True)
    except ValidationError as e:
        raise BundleError(f"base config failed validation:\n{e}") from e

    delta = bundle.delta.model_dump(mode="json", by_alias=True)
    merged = _compose(base, delta)
    try:
        out = CoraConfig.model_validate(merged).model_dump(mode="json", by_alias=True,
                                                          exclude_none=True)
    except ValidationError as e:
        raise BundleError(f"composed config (base + delta) is invalid:\n{e}") from e
    # A delta bundle may also override the global prompt (same reasoning as the full path).
    _attach_global_prompt(out, bundle)
    return out
