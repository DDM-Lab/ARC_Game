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


def _compose(base: dict, delta: dict) -> dict:
    merged = dict(base)
    if delta.get("agent_order_rule") is not None:
        merged["agent_order_rule"] = delta["agent_order_rule"]
    if delta.get("agents"):
        merged["agents"] = _merge_agents(base["agents"], delta["agents"])
    return merged


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
        return bundle.config.model_dump(mode="json", by_alias=True, exclude_none=True)

    # delta path
    if base_config is None:
        raise BundleError(
            f"bundle {bundle.manifest.name!r} is a delta and needs a base config "
            f"(pass base_config=)")
    base_raw = _load_json(base_config)
    try:
        base = CoraConfig.model_validate(base_raw).model_dump(mode="json", by_alias=True,
                                                              exclude_none=True)
    except ValidationError as e:
        raise BundleError(f"base config failed validation:\n{e}") from e

    delta = bundle.delta.model_dump(mode="json", by_alias=True)
    merged = _compose(base, delta)
    try:
        return CoraConfig.model_validate(merged).model_dump(mode="json", by_alias=True,
                                                            exclude_none=True)
    except ValidationError as e:
        raise BundleError(f"composed config (base + delta) is invalid:\n{e}") from e
