"""
Agent configuration loader for the ARC Game multi-agent framework.
Reads agents_config.json shared between Python router and Unity.
"""
from __future__ import annotations
import json
from dataclasses import dataclass, field
from typing import Optional


VALID_ROLES = {"subagent", "director"}
VALID_ACTOR_TYPES = {"auto", "choices", "manual", "llm", "coach"}
VALID_CATEGORIES = {"construction", "deconstruction", "worker",
                    "worker_assignment", "resource_transfer", "all"}
VALID_OBS_KEYS = {"sessionInfo", "satisfactionAndBudget", "workers",
                  "buildings", "tasks", "constructionState",
                  "mapState", "logistics", "all"}
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
    # Runtime state — not from config
    conversation_history: list[dict] = field(default_factory=list, init=False)

    def __post_init__(self):
        if self.role not in VALID_ROLES:
            raise ValueError(f"Invalid role '{self.role}' for agent '{self.subagent_name}'. "
                             f"Must be one of {VALID_ROLES}")
        if self.actor_type not in VALID_ACTOR_TYPES:
            raise ValueError(f"Invalid actor_type '{self.actor_type}' for agent '{self.subagent_name}'.")
        for entry in self.subaction_space:
            if entry.get("category") not in VALID_CATEGORIES:
                raise ValueError(f"Invalid action category '{entry}' for agent '{self.subagent_name}'.")
        for key in self.subobservation_space:
            if key not in VALID_OBS_KEYS:
                raise ValueError(f"Invalid observation key '{key}' for agent '{self.subagent_name}'.")
        if self.talkinghead_endpoint not in VALID_TALKINGHEADS:
            raise ValueError(
                f"Invalid talkinghead_endpoint '{self.talkinghead_endpoint}' "
                f"for agent '{self.subagent_name}'. Must be one of {VALID_TALKINGHEADS}"
            )

    @property
    def is_llm_driven(self) -> bool:
        return self.actor_type in {"auto", "choices", "llm", "coach"}

    @property
    def action_categories(self) -> set[str]:
        cats = {entry["category"] for entry in self.subaction_space}
        return cats  # "all" means no filter applied


@dataclass
class RouterConfig:
    agent_order_rule: str
    agents: list[AgentConfig]

    def __post_init__(self):
        if self.agent_order_rule not in VALID_ORDER_RULES:
            raise ValueError(f"Invalid agent_order_rule '{self.agent_order_rule}'. "
                             f"Must be one of {VALID_ORDER_RULES}")
        directors = [a for a in self.agents if a.role == "director"]
        if len(directors) != 1:
            raise ValueError(f"Config must have exactly one director, found {len(directors)}.")

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
            turn_token_budget=entry.get("turn_token_budget"),
            system_prompt=entry.get("system_prompt"),
            use_global_prompt=entry.get("use_global_prompt", True),
            can_address=entry.get("can_address", []),
        ))

    return RouterConfig(
        agent_order_rule=data["agent_order_rule"],
        agents=agents,
    )
