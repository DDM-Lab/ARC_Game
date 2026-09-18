"""
Agent configuration loader for the ARC Game multi-agent framework.
Reads agents_config.json shared between Python router and Unity.
"""
from __future__ import annotations
import json
from dataclasses import dataclass, field
from typing import Optional

from provider_registry import (
    resolve as _resolve_provider,
    is_valid as _provider_ok,
    valid_names as _provider_names,
)


VALID_ROLES = {"subagent", "director"}
VALID_ACTOR_TYPES = {"auto", "choices", "manual", "llm", "coach", "continuous"}
VALID_CATEGORIES = {"construction", "deconstruction", "worker",
                    "worker_assignment", "resource_transfer", "task_choice", "all"}
# Coarse task-group slugs (obs_encoder.task_group) — one per officer domain. Used
# both as the {"category":"task_choice","group":<slug>} sub-scope in subaction_space
# and as the "tasks:<slug>" narrowing in subobservation_space.
VALID_TASK_GROUPS = {"budget", "workforce", "food", "lodging", "disaster"}
VALID_OBS_KEYS = {"sessionInfo", "satisfactionAndBudget", "workers",
                  "buildings", "tasks", "constructionState",
                  "mapState", "logistics", "all",
                  # Raw game_state keys are also accepted (aliases map workers->
                  # workforceState, buildings->mapState; these let a config name the
                  # raw key directly). See agent_filters._OBS_KEY_ALIASES.
                  "workforceState"}
VALID_ORDER_RULES = {"sequential", "random", "priority"}
VALID_TALKINGHEADS = {
    "DisasterOfficer", "WorkforceService", "LodgingMassCare",
    "ExternalRelationship", "FoodMassCare", None
}


@dataclass
class AgentConfig:
    subagent_name: str
    role: str                              # "subagent" | "director"
    actor_type: str                        # "auto" | "choices" | "manual" | "llm" | "coach"
    num_choices: Optional[int]             # For choices agents: number of packages
    max_actions_per_package: Optional[int] # For choices/auto: actions per package/turn
    num_turns: Optional[int]               # For coach agents: number of turn recommendations
    max_actions_per_turn: Optional[int]    # For coach agents: actions per turn recommendation
    talkinghead_endpoint: Optional[str]
    subaction_space: list[dict]
    subobservation_space: list[str]
    llm_provider: Optional[str]            # "ollama" | "openai" | "anthropic"
    llm_model: Optional[str]
    llm_endpoint: Optional[str]
    llm_port: Optional[int]
    api_key_env: Optional[str]             # Environment variable name for API key
    turn_token_budget: Optional[int]
    system_prompt: Optional[str]
    use_global_prompt: bool = True         # Prepend global prompt before system_prompt
    can_address: list[str] = field(default_factory=list)
    # Provider enum (provider_registry). Preferred over the raw llm_provider/llm_endpoint/
    # api_key_env trio: it names a server-side provider that resolves to those fields in
    # __post_init__, so an uploaded config can never carry an endpoint or a secret env var.
    # Mutually exclusive with the raw fields. (Kept in the defaults section for dataclass
    # field-ordering; it belongs conceptually with the llm_* fields above.)
    provider: Optional[str] = None
    # --- Choices-agent reliability + explainability (opt-in; safe defaults) ---
    choices_max_retries: int = 1           # extra LLM re-queries if a parse underdelivers
    choices_min_packages: int = 1          # floor below which we retry / fall back
    choices_fallback: bool = True          # synthesize deterministic packages to fill the set
    explain_grounded: bool = True          # prepend engine-computed $cost to each package desc
    explain_summary: bool = True           # prepend grounded context to the pre-choices summary
    choices_repropose_hint: bool = True    # append "you can ask me to repropose" nudge to the summary
    # --- Continuous-agent (tool-using loop) knobs (opt-in; safe defaults) ---
    # The continuous agent holds the full tool palette every step and picks which
    # tool to use — interaction style is emergent, not imposed. `tools` MAY narrow
    # the palette but None means "all"; it is never the router gating per-turn.
    tools: Optional[list[str]] = None      # tool-name allowlist; None = full palette
    max_steps: int = 8                     # max tool-call steps per turn (loop guard)
    tool_mode: str = "auto"                # "auto" | "native" | "text" (ReAct fallback)
    # Opening posture / autonomy dial (à la Claude Code permission modes), kept OUT
    # of the agent's prompt so the agent itself stays un-handheld:
    #   "emergent"    — no imposed style; pure tool-user, may act from step 1.
    #   "brief_first" — open with a situation briefing + ask instead of acting,
    #                   but only until the director first speaks; emergent after.
    #   "reactive"    — act ONLY on a turn the director directly triggered. On an
    #                   unprompted (begin_round) turn the officer may brief the
    #                   director but has NO action tools, so it cannot commit
    #                   anything; on a director-message turn it gets the full
    #                   palette and does exactly what was asked. This is the
    #                   "activate when spoken to" posture.
    opening_mode: str = "emergent"         # "emergent" | "brief_first" | "reactive"
    # Planning-phase ledger enforcement. The paused-phase observation is frozen, so
    # the agent can't see its own queued actions and may repeat them. "annotate" =
    # mark already-committed actions inline (advisory; the model may still pick
    # them). "block" = the harness no-ops a re-execution of a non-repeatable action
    # already committed this phase (staleness-style, à la Claude Code read-before-
    # edit) — grounding, not style-gating.
    ledger_mode: str = "block"             # "block" | "annotate"
    # Runtime state — not from config
    conversation_history: list[dict] = field(default_factory=list, init=False)

    def __post_init__(self):
        # Resolve the provider enum FIRST so the derived llm_provider/llm_endpoint/api_key_env are
        # in place for every downstream client-construction site (which read them via vars(agent)).
        if self.provider is not None:
            if not _provider_ok(self.provider):
                raise ValueError(
                    f"Invalid provider '{self.provider}' for agent '{self.subagent_name}'. "
                    f"Must be one of {_provider_names()}.")
            if self.llm_provider or self.llm_endpoint or self.api_key_env:
                raise ValueError(
                    f"Agent '{self.subagent_name}': set `provider` OR raw "
                    f"llm_provider/llm_endpoint/api_key_env, not both.")
            spec = _resolve_provider(self.provider)
            self.llm_provider = spec.backend
            self.llm_endpoint = spec.base_url
            self.api_key_env = spec.key_env
        if self.role not in VALID_ROLES:
            raise ValueError(f"Invalid role '{self.role}' for agent '{self.subagent_name}'. "
                             f"Must be one of {VALID_ROLES}")
        if self.actor_type not in VALID_ACTOR_TYPES:
            raise ValueError(f"Invalid actor_type '{self.actor_type}' for agent '{self.subagent_name}'.")
        for entry in self.subaction_space:
            if entry.get("category") not in VALID_CATEGORIES:
                raise ValueError(f"Invalid action category '{entry}' for agent '{self.subagent_name}'.")
            # task_choice takes an optional {"group": <slug>} sub-scope (one coarse
            # group per officer domain). A missing group means "any task_choice".
            if entry.get("category") == "task_choice":
                grp = entry.get("group")
                if grp is not None and grp not in VALID_TASK_GROUPS:
                    raise ValueError(
                        f"Invalid task_choice group '{grp}' for agent "
                        f"'{self.subagent_name}'. Must be one of {VALID_TASK_GROUPS}.")
            # Optional building-type sub-scope (see agent_filters.filter_actions).
            # Case-insensitive substring match against a construction building_type
            # ("Kitchen"/"Shelter"/"CaseworkSite") or an assignment/deconstruction
            # building_name ("Kitchen Alpha"). Must be a list of strings if present.
            btypes = entry.get("building_types")
            if btypes is not None and (
                not isinstance(btypes, list)
                or not all(isinstance(b, str) for b in btypes)
            ):
                raise ValueError(
                    f"building_types must be a list of strings, got {btypes!r} "
                    f"for agent '{self.subagent_name}'."
                )
        for key in self.subobservation_space:
            # "tasks:<group>" narrows visible tasks to a coarse group (parallel to
            # the task_choice action sub-scope). Bare "tasks" keeps its jurisdiction
            # narrowing. Validate the suffix against the known groups.
            if isinstance(key, str) and key.startswith("tasks:"):
                grp = key.split(":", 1)[1]
                if grp not in VALID_TASK_GROUPS:
                    raise ValueError(
                        f"Invalid task obs group '{key}' for agent "
                        f"'{self.subagent_name}'. Must be tasks:<{'|'.join(sorted(VALID_TASK_GROUPS))}>.")
                continue
            if key not in VALID_OBS_KEYS:
                raise ValueError(f"Invalid observation key '{key}' for agent '{self.subagent_name}'.")
        if self.talkinghead_endpoint not in VALID_TALKINGHEADS:
            raise ValueError(
                f"Invalid talkinghead_endpoint '{self.talkinghead_endpoint}' "
                f"for agent '{self.subagent_name}'. Must be one of {VALID_TALKINGHEADS}"
            )

    @property
    def is_llm_driven(self) -> bool:
        return self.actor_type in {"auto", "choices", "llm", "coach", "continuous"}

    @property
    def action_categories(self) -> set[str]:
        cats = {entry["category"] for entry in self.subaction_space}
        return cats  # "all" means no filter applied


@dataclass
class RouterConfig:
    agent_order_rule: str
    agents: list[AgentConfig]
    # Per-config prompt overrides (from an uploaded bundle). None → inherit the server-wide
    # defaults. These are what let two collaborators run DIFFERENT prompt conditions on one
    # server instead of sharing one process-wide prompt.
    #   global_prompt_behavior : communication/persona rules half
    #   global_prompt_manual   : game-rules half (mechanics, costs, strategy)
    #   global_prompt          : legacy whole-blob replace (both halves at once)
    #   tool_policy            : full replace of the tool-use contract
    global_prompt: Optional[str] = None
    global_prompt_behavior: Optional[str] = None
    global_prompt_manual: Optional[str] = None
    tool_policy: Optional[str] = None
    # Authored strings of the per-turn message (see cora_schema.Bundle.turn_instructions).
    turn_instructions: Optional[dict] = None
    #   tool_descriptions      : reword built-in tool descriptions, {tool_name: text}. These
    #                            ride the API `tools` argument rather than the prompt, so this
    #                            is the one model-visible surface no prompt override reaches.
    #                            Parameters/enums stay harness-owned (they feed cora_tools).
    tool_descriptions: Optional[dict] = None

    def __post_init__(self):
        if self.agent_order_rule not in VALID_ORDER_RULES:
            raise ValueError(f"Invalid agent_order_rule '{self.agent_order_rule}'. "
                             f"Must be one of {VALID_ORDER_RULES}")
        directors = [a for a in self.agents if a.role == "director"]
        if len(directors) != 1:
            raise ValueError(f"Config must have exactly one director, found {len(directors)}.")
        # The GUI has exactly five FIXED talking-head slots (Unity's TaskOfficer enum), and
        # the client buckets every inbound message by talkinghead_endpoint. Two TAB-RENDERING
        # officers on one endpoint therefore collapse into a single tab: only one name
        # survives in the hello_ack roster and their messages interleave under it, with no
        # error anywhere. Per-agent validation above only checks each endpoint is a VALID
        # name — it cannot see the collision — so reject it here, loudly, at config load.
        # Scope: only actors that actually emit agent_message/choices frames to a tab.
        # "coach" is excluded — it emits a `coach_report` frame instead, so it never
        # occupies a tab and can legitimately share an endpoint (see coach_agent_example).
        seen: dict[str, str] = {}
        for a in self.agents:
            ep = a.talkinghead_endpoint
            if not ep or a.actor_type == "coach":
                continue          # no slot: the director, headless agents, coach reports
            if ep in seen:
                raise ValueError(
                    f"Duplicate talkinghead_endpoint '{ep}': used by both "
                    f"'{seen[ep]}' and '{a.subagent_name}'. Each officer needs its own "
                    f"slot — the GUI has one tab per endpoint, so they would collide."
                )
            seen[ep] = a.subagent_name

    def get_subagents(self) -> list[AgentConfig]:
        return [a for a in self.agents if a.role == "subagent"]

    def get_director(self) -> AgentConfig:
        return next(a for a in self.agents if a.role == "director")


def load_config(path: str) -> RouterConfig:
    """Load and validate agents_config.json from the given path."""
    try:
        with open(path, "r") as f:
            data = json.load(f)
    except FileNotFoundError:
        raise FileNotFoundError(f"agents_config.json not found at: {path}")
    return config_from_dict(data)


def config_from_dict(data: dict) -> RouterConfig:
    """Build + validate a RouterConfig from an already-parsed config dict.

    Split out of load_config so the UPLOAD path can enforce the same RUNTIME invariants
    without writing a file first. The Pydantic gate (cora_schema.CoraConfig) and these
    invariants check different things: a config can pass the schema and still be unusable —
    e.g. two officers sharing one talkinghead slot passes CoraConfig but raises here, which
    meant such a bundle uploaded with HTTP 200 and then broke the session of whoever selected
    it. Running both at upload turns that into an honest rejection at the point of upload.
    """
    agents = []
    for entry in data["agents"]:
        agents.append(AgentConfig(
            subagent_name=entry["subagent_name"],
            role=entry["role"],
            actor_type=entry["actor_type"],
            num_choices=entry.get("num_choices"),
            max_actions_per_package=entry.get("max_actions_per_package"),
            num_turns=entry.get("num_turns"),
            max_actions_per_turn=entry.get("max_actions_per_turn"),
            talkinghead_endpoint=entry.get("talkinghead_endpoint"),
            subaction_space=entry.get("subaction_space", []),
            subobservation_space=entry.get("subobservation_space", ["all"]),
            llm_provider=entry.get("llm_provider"),
            llm_model=entry.get("llm_model"),
            llm_endpoint=entry.get("llm_endpoint"),
            llm_port=entry.get("llm_port"),
            api_key_env=entry.get("api_key_env"),
            provider=entry.get("provider"),
            turn_token_budget=entry.get("turn_token_budget"),
            system_prompt=entry.get("system_prompt"),
            use_global_prompt=entry.get("use_global_prompt", True),
            can_address=entry.get("can_address", []),
            choices_max_retries=entry.get("choices_max_retries", 1),
            choices_min_packages=entry.get("choices_min_packages", 1),
            choices_fallback=entry.get("choices_fallback", True),
            explain_grounded=entry.get("explain_grounded", True),
            explain_summary=entry.get("explain_summary", True),
            choices_repropose_hint=entry.get("choices_repropose_hint", True),
            tools=entry.get("tools"),
            max_steps=entry.get("max_steps", 8),
            tool_mode=entry.get("tool_mode", "auto"),
            opening_mode=entry.get("opening_mode", "emergent"),
            ledger_mode=entry.get("ledger_mode", "block"),
        ))

    # Optional per-config global prompt, carried in by an uploaded bundle (bundle.py's
    # _attach_global_prompt). Accepts the global_prompt_config.json dict shape or a bare
    # string; `enabled: false` means "use the server default", same as the file's flag.
    # Prompt overrides. A bare string = BEHAVIOR only (the game manual is inherited) —
    # replacing the whole blob by accident is how a personality tweak silently deletes the
    # game rules. `global_system_prompt` remains a whole-blob replace for the config-file
    # shape, which is what config/global_prompt_config.json itself uses.
    gp = data.get("global_prompt")
    gp_text = gp_behavior = gp_manual = None
    if isinstance(gp, str):
        gp_behavior = gp or None
    elif isinstance(gp, dict) and gp.get("enabled", True):
        gp_text = gp.get("global_system_prompt") or None
        gp_behavior = gp.get("behavior") or None
        gp_manual = gp.get("manual") or None

    return RouterConfig(
        agent_order_rule=data["agent_order_rule"],
        agents=agents,
        global_prompt=gp_text,
        global_prompt_behavior=gp_behavior,
        global_prompt_manual=gp_manual,
        tool_policy=(data.get("tool_policy") or None),
        turn_instructions=(data.get("turn_instructions") or None),
        tool_descriptions=(data.get("tool_descriptions") or None),
    )
