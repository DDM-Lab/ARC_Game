"""Pydantic v2 schema for CORA contributor bundles — the untrusted-input gate.

This is the validation boundary for anything a contributor uploads (config + prompts). It is
DELIBERATELY separate from `agent_config.AgentConfig`: Pydantic validates and coerces the
uploaded envelope against a schema we own (rejecting unknown keys, wrong types, and — critically —
any raw endpoint/secret field), then emits a plain dict that flows into the *existing*
`agent_config.load_config` / `AgentConfig.__post_init__`. Two layers, each with a job:

  * `cora_schema`  — gate untrusted input (extra='forbid', strict, provider-by-enum-only).
  * `AgentConfig`  — runtime invariants the engine relies on.

See docs/CORA_API_v1.md and docs/contributor-platform-design.md.

The officer field set mirrors `AgentConfig`'s accepted keys, EXCEPT the three raw provider fields
`llm_provider` / `llm_endpoint` / `api_key_env` (and legacy `llm_port`) are REPLACED by a single
`provider: Provider` enum resolved server-side by provider_registry. An uploaded config therefore
cannot name an endpoint or a secret env var.
"""
from __future__ import annotations

import re
from typing import Literal, Optional

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

from provider_registry import Provider

CORA_API_VERSION = "1.0"

# Vocabularies mirrored from agent_config (kept in sync deliberately; see docs/CORA_API_v1.md).
Role = Literal["subagent", "director"]
ActorType = Literal["auto", "choices", "manual", "llm", "coach", "continuous"]
Category = Literal["construction", "deconstruction", "worker",
                   "worker_assignment", "resource_transfer", "task_choice", "all"]
TaskGroup = Literal["budget", "workforce", "food", "lodging", "disaster"]
OrderRule = Literal["sequential", "random", "priority"]
ToolMode = Literal["auto", "native", "text"]
OpeningMode = Literal["emergent", "brief_first", "reactive"]
LedgerMode = Literal["block", "annotate"]
Talkinghead = Literal["DisasterOfficer", "WorkforceService", "LodgingMassCare",
                      "ExternalRelationship", "FoodMassCare"]

_OBS_SECTION_KEYS = {"sessionInfo", "satisfactionAndBudget", "constructionState", "logistics",
                     "tasks", "workers", "buildings", "all", "workforceState", "mapState"}
_SEMVER_RE = re.compile(r"^\d+\.\d+\.\d+$")
_BUNDLE_NAME_RE = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")  # owner/slug


def _validate_obs_key(key: str) -> str:
    if key.startswith("tasks:"):
        grp = key.split(":", 1)[1]
        if grp not in {"budget", "workforce", "food", "lodging", "disaster"}:
            raise ValueError(f"invalid task obs group in {key!r}")
        return key
    if key not in _OBS_SECTION_KEYS:
        raise ValueError(f"invalid observation key {key!r}; "
                         f"allowed: {sorted(_OBS_SECTION_KEYS)} or 'tasks:<group>'")
    return key


class SubActionEntry(BaseModel):
    model_config = ConfigDict(extra="forbid")
    category: Category
    group: Optional[TaskGroup] = None
    building_types: Optional[list[str]] = None


class OfficerConfig(BaseModel):
    """Full, validated officer entry. `extra='forbid'` so a typo'd key is a hard error, not a
    silently-ignored no-op. `provider` (enum) replaces raw llm_provider/llm_endpoint/api_key_env."""
    model_config = ConfigDict(extra="forbid", strict=True, populate_by_name=True)

    comment: Optional[str] = Field(None, alias="_comment")

    subagent_name: str
    role: Role
    actor_type: ActorType
    # strict=False so a JSON string ("cmu-gateway") coerces to the enum; the value is still
    # validated against Provider — an unregistered name is rejected.
    provider: Optional[Provider] = Field(None, strict=False)  # required for LLM actors (below)
    llm_model: Optional[str] = None

    talkinghead_endpoint: Optional[Talkinghead] = None
    subaction_space: list[SubActionEntry] = Field(default_factory=list)
    subobservation_space: list[str] = Field(default_factory=lambda: ["all"])

    num_choices: Optional[int] = None
    max_actions_per_package: Optional[int] = None
    num_turns: Optional[int] = None
    max_actions_per_turn: Optional[int] = None
    turn_token_budget: Optional[int] = None

    system_prompt: Optional[str] = None
    use_global_prompt: bool = True
    can_address: list[str] = Field(default_factory=list)

    choices_max_retries: int = 1
    choices_min_packages: int = 1
    choices_fallback: bool = True
    explain_grounded: bool = True
    explain_summary: bool = True
    choices_repropose_hint: bool = True

    tools: Optional[list[str]] = None
    max_steps: int = 8
    tool_mode: ToolMode = "auto"
    opening_mode: OpeningMode = "emergent"
    ledger_mode: LedgerMode = "block"

    @field_validator("subobservation_space")
    @classmethod
    def _check_obs(cls, v: list[str]) -> list[str]:
        for k in v:
            _validate_obs_key(k)
        return v

    @model_validator(mode="after")
    def _llm_actor_needs_provider(self):
        if self.actor_type in {"auto", "choices", "llm", "coach", "continuous"} and self.provider is None:
            raise ValueError(
                f"officer {self.subagent_name!r} has actor_type={self.actor_type!r} but no "
                f"`provider` (one of {[p.value for p in Provider]})")
        return self


class CoraConfig(BaseModel):
    """A complete, validated roster — the shape `load_config` ultimately consumes."""
    model_config = ConfigDict(extra="forbid", populate_by_name=True)

    comment: Optional[str] = Field(None, alias="_comment")
    agent_order_rule: OrderRule
    agents: list[OfficerConfig]

    @model_validator(mode="after")
    def _exactly_one_director(self):
        directors = [a for a in self.agents if a.role == "director"]
        if len(directors) != 1:
            raise ValueError(f"config must have exactly one director, found {len(directors)}")
        return self


# --- Delta (contributor override that layers on a maintained base) -----------------------------
class OfficerOverride(BaseModel):
    """Partial officer override, matched to a base officer by `subagent_name`. Every field except
    the match key is optional; `extra='forbid'` still rejects typos."""
    model_config = ConfigDict(extra="forbid", populate_by_name=True)

    subagent_name: str  # match key (required)
    provider: Optional[Provider] = Field(None, strict=False)
    llm_model: Optional[str] = None
    system_prompt: Optional[str] = None
    subaction_space: Optional[list[SubActionEntry]] = None
    subobservation_space: Optional[list[str]] = None
    turn_token_budget: Optional[int] = None
    max_steps: Optional[int] = None
    tool_mode: Optional[ToolMode] = None
    opening_mode: Optional[OpeningMode] = None
    ledger_mode: Optional[LedgerMode] = None
    use_global_prompt: Optional[bool] = None
    can_address: Optional[list[str]] = None
    tools: Optional[list[str]] = None

    @field_validator("subobservation_space")
    @classmethod
    def _check_obs(cls, v: Optional[list[str]]) -> Optional[list[str]]:
        if v is not None:
            for k in v:
                _validate_obs_key(k)
        return v


class CoraConfigDelta(BaseModel):
    """What a contributor uploads to tweak a base config without forking it."""
    model_config = ConfigDict(extra="forbid", populate_by_name=True)

    comment: Optional[str] = Field(None, alias="_comment")
    agent_order_rule: Optional[OrderRule] = None
    agents: Optional[list[OfficerOverride]] = None


# --- Manifest + bundle envelope ----------------------------------------------------------------
class BundleManifest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    name: str                                   # namespaced: owner/slug
    author: str
    version: str                                # immutable SemVer once published
    cora_api_version: str = CORA_API_VERSION
    description: str = ""
    dependencies: list[str] = Field(default_factory=list)

    @field_validator("name")
    @classmethod
    def _check_name(cls, v: str) -> str:
        if not _BUNDLE_NAME_RE.match(v):
            raise ValueError(f"bundle name must be 'owner/slug', got {v!r}")
        return v

    @field_validator("version")
    @classmethod
    def _check_version(cls, v: str) -> str:
        if not _SEMVER_RE.match(v):
            raise ValueError(f"version must be MAJOR.MINOR.PATCH, got {v!r}")
        return v


class Bundle(BaseModel):
    """The single uploadable artifact. Exactly one of `config` (full) or `delta` (override) is
    provided; bundle.load_bundle enforces base+delta composition. `tools` is reserved for a later
    phase and must be empty today."""
    model_config = ConfigDict(extra="forbid")

    manifest: BundleManifest
    config: Optional[CoraConfig] = None
    delta: Optional[CoraConfigDelta] = None
    global_prompt: Optional[dict] = None        # optional override; shape validated elsewhere
    tools: list = Field(default_factory=list)

    @model_validator(mode="after")
    def _config_xor_delta(self):
        if (self.config is None) == (self.delta is None):
            raise ValueError("provide exactly one of `config` (full) or `delta` (override)")
        if self.tools:
            raise ValueError("`tools` is reserved for a later phase and must be empty in v1.0")
        return self
