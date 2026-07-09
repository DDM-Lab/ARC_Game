"""
Standalone LLM query function for the ARC Game agent router.
Extracted from ollama_websocket_server.py so the router can call
LLMs directly without going through a WebSocket server.

Supports multiple LLM providers: Ollama, OpenAI, and Anthropic.
"""
import json
import os
from pathlib import Path
from typing import Optional, Dict, Any
from dotenv import load_dotenv
import ollama

from obs_encoder import render_state_text, _num

load_dotenv(Path(__file__).parent / ".env")

# Import optional providers
try:
    import openai
except ImportError:
    openai = None

try:
    import anthropic
except ImportError:
    anthropic = None


# Global prompt cache
_GLOBAL_PROMPT_CACHE = None
_GLOBAL_PROMPT_CONFIG_PATH = Path(__file__).parent / "config" / "global_prompt_config.json"


def load_global_prompt(config_path: Optional[str] = None) -> str:
    """
    Load the global system prompt from config file.

    Args:
        config_path: Path to global_prompt_config.json. If None, uses default location.

    Returns:
        Global system prompt string, or empty string if disabled or not found.
    """
    global _GLOBAL_PROMPT_CACHE

    # Return cached prompt if available
    if _GLOBAL_PROMPT_CACHE is not None:
        return _GLOBAL_PROMPT_CACHE

    # Determine config path
    if config_path is None:
        config_path = _GLOBAL_PROMPT_CONFIG_PATH
    else:
        config_path = Path(config_path)

    # Load config file
    try:
        with open(config_path, "r") as f:
            config = json.load(f)

        # Check if global prompt is enabled
        if not config.get("enabled", True):
            print(f"[llm_query] Global prompt disabled in config")
            _GLOBAL_PROMPT_CACHE = ""
            return ""

        # Get prompt text
        global_prompt = config.get("global_system_prompt", "")
        _GLOBAL_PROMPT_CACHE = global_prompt

        print(f"[llm_query] Loaded global prompt (v{config.get('version', '?')}, "
              f"{len(global_prompt)} chars)")
        return global_prompt

    except FileNotFoundError:
        print(f"[llm_query] Global prompt config not found at {config_path}, using empty prompt")
        _GLOBAL_PROMPT_CACHE = ""
        return ""
    except Exception as e:
        print(f"[llm_query] Error loading global prompt config: {e}")
        _GLOBAL_PROMPT_CACHE = ""
        return ""


def _build_prompt(
    game_state: dict,
    actions: list,
    agent_cfg: dict,
    history: list,
) -> list:
    """Build the message list for the Ollama chat call."""
    messages = []

    # Build system prompt with optional global prompt prepended
    use_global = agent_cfg.get("use_global_prompt", True)
    global_prompt = load_global_prompt() if use_global else ""

    # Agent-specific system prompt
    agent_prompt = agent_cfg.get("system_prompt") or (
        "You are an expert advisor in a disaster relief operation. "
        "Select actions by their index numbers only."
    )

    # Combine prompts: global first (if enabled), then agent-specific
    if global_prompt:
        system_prompt = f"{global_prompt}\n\n---\n\nAGENT ROLE: {agent_prompt}"
    else:
        system_prompt = agent_prompt

    messages.append({"role": "system", "content": system_prompt})

    # Inject conversation history (prior rounds)
    # History can be in two formats:
    # 1. Old format: [{"user": "...", "assistant": "..."}]
    # 2. New format (from message_queue): [{"from": "agent", "to": "Director", "content": "...", "round": N}]
    for entry in history:
        if "user" in entry and "assistant" in entry:
            # Old format
            messages.append({"role": "user",    "content": entry.get("user", "")})
            messages.append({"role": "assistant","content": entry.get("assistant", "")})
        elif "from" in entry and "to" in entry:
            # New format - conversation message
            from_agent = entry.get("from")
            to_agent = entry.get("to")
            content = entry.get("content", "")
            round_num = entry.get("round", "?")

            # Determine role based on who sent the message
            # If agent sent it, it's "assistant". If Director sent it, it's "user"
            if from_agent == "Director":
                role = "user"
                label = f"Director (Round {round_num})"
            else:
                role = "assistant"
                label = f"You (Round {round_num})"

            formatted_content = f"[{label}] {content}"
            messages.append({"role": role, "content": formatted_content})

    # Current state summary — grounded, engine-computed facts (facilities, worker
    # pools, open tasks with per-choice impacts, spend). See obs_encoder.py.
    state_text = render_state_text(game_state)

    # Action list. NOTE: action dicts use snake_case `action_type` (from
    # ActionEnumerator.to_dict), not `actionType`; the old camelCase read always
    # rendered "[?]". Cost is real engine data — surface it as ground truth.
    action_lines = [
        f"{i}. [{a.get('action_type','?')}] {a.get('description','?')} (cost: ${_num(a.get('cost')):,})"
        for i, a in enumerate(actions)
    ]
    action_text = "\n".join(action_lines) if action_lines else "(no valid actions)"

    # Different prompts for auto vs choices vs coach agents
    actor_type = agent_cfg.get("actor_type", "auto")

    if actor_type == "coach":
        num_turns = agent_cfg.get("num_turns", 3)
        max_per_turn = agent_cfg.get("max_actions_per_turn", 3)
        user_content = (
            f"Current situation:\n{state_text}\n\n"
            f"Available actions:\n{action_text}\n\n"
            f"As a strategic coach, analyze the current situation and provide a multi-turn action plan.\n"
            f"Recommend {num_turns} turns of actions, with up to {max_per_turn} actions per turn.\n\n"
            f"Response format:\n"
            f"SITUATION: [2-3 sentences analyzing the current game state and key metrics]\n"
            f"ANALYSIS: [Key problems, opportunities, or risks you've identified]\n"
            f"RECOMMENDATION:\n"
            f"TURN1: [action indices] | [rationale for this turn]\n"
            f"TURN2: [action indices] | [rationale for this turn]\n"
            f"TURN3: [action indices] | [rationale for this turn]\n\n"
            f"Example:\n"
            f"SITUATION: Day 3 with 75% satisfaction and $15,000 budget. We have infrastructure gaps and growing population.\n"
            f"ANALYSIS: Shelter capacity is critical. Food production stable but vulnerable. Workforce stretched thin.\n"
            f"RECOMMENDATION:\n"
            f"TURN1: 0,1 | Build two shelters immediately to address housing shortage\n"
            f"TURN2: 3,4 | Hire workers to staff new buildings and increase capacity\n"
            f"TURN3: 5 | Build kitchen as backup food source for resilience\n\n"
            f"Keep rationales concise (under 80 characters). Focus on strategic reasoning, not just action descriptions."
        )
    elif actor_type == "choices":
        num_choices = agent_cfg.get("num_choices", 3)
        max_per_package = agent_cfg.get("max_actions_per_package", 4)
        user_content = (
            f"Current situation:\n{state_text}\n\n"
            f"Available actions:\n{action_text}\n\n"
            f"Propose {num_choices} strategy packages, up to {max_per_package} action indices each.\n\n"
            f"GROUNDING: the situation above lists real, engine-computed numbers — each action's cost, "
            f"cumulative spend, worker pools, facility state, and each open task choice's exact impacts "
            f"(e.g. [Budget +5000] [Satisfaction +10]). A package's cost is the SUM of its actions' listed "
            f"costs — compute it, do not estimate. Do NOT invent satisfaction/budget numbers that are not "
            f"shown: if an outcome isn't given, describe it qualitatively (e.g. 'adds shelter capacity').\n\n"
            f"DIVERSITY (critical): the {num_choices} packages MUST be genuinely DIFFERENT strategies — NOT "
            f"the same plan at different spend levels. Each must differ in FOCUS. Pick distinct focuses from: "
            f"food, shelter, workforce, respond to an open task, or conserve budget. Vary WHAT the option "
            f"prioritizes, not just how much it spends. Include at least one low-cost / conserve option; if an "
            f"open task offers a reward, make one package take it. If two of your packages would touch the same "
            f"kinds of actions, replace one with a different focus.\n\n"
            f"Response format — each PACKAGE has FOUR fields separated by '|':\n"
            f"  name | indices | what it does | why pick it\n"
            f"REASONING: 2 short sentences. First names the key constraint (budget, gap, pressing task). Second says why these options.\n"
            f"PACKAGE1: <name, 2-4 words> | <indices> | <what it does, no $ amount, ~55 chars> | <why choose it — the trade-off, ~70 chars>\n"
            f"PACKAGE2: <name, 2-4 words> | <indices> | <what it does, no $ amount, ~55 chars> | <why choose it — the trade-off, ~70 chars>\n"
            f"PACKAGE3: <name, 2-4 words> | <indices> | <what it does, no $ amount, ~55 chars> | <why choose it — the trade-off, ~70 chars>\n\n"
            f"Example (each option has a DIFFERENT focus; the 'why' names the trade-off with NO numbers):\n"
            f"REASONING: Budget is tight and shelter capacity trails population. These options trade shelter expansion against food and keeping a reserve for next-round flood costs.\n"
            f"PACKAGE1: Cheap Food | 0,5,7 | restocks kitchen food | cheapest fix; keeps a reserve but ignores housing\n"
            f"PACKAGE2: Shelter First | 1,2,8 | adds shelter capacity | houses people now, but little left for floods\n"
            f"PACKAGE3: Fund + Balance | 0,1,5 | build + task choice 3 [Budget +5000] | unlocks budget for next round; slower now\n\n"
            f"Hard limits: name ≤ 4 words, 'what it does' ≤ 60 chars, 'why' ≤ 70 chars. In 'why' give the QUALITATIVE "
            f"trade-off (what it gains vs gives up) — do NOT put ANY $ amounts in 'what it does' OR 'why'; the $cost "
            f"and remaining budget ('leaves $X') are computed and shown automatically. REASONING ≤ 2 sentences.\n"
            f"Constraint: do not include two construction actions targeting the same site in one package."
        )
    else:
        # Auto agent: execute immediately with structured rationale
        max_actions = agent_cfg.get("max_actions_per_turn", 5)
        user_content = (
            f"Current situation:\n{state_text}\n\n"
            f"Available actions:\n{action_text}\n\n"
            f"GROUNDING: the situation above lists real, engine-computed numbers — each action's cost, "
            f"cumulative spend, worker pools, facility state, and each open task choice's exact impacts. "
            f"Total cost is the SUM of the chosen actions' listed costs — compute it, do not estimate. Do "
            f"NOT invent satisfaction/budget numbers that are not shown; describe unquantified effects "
            f"qualitatively.\n\n"
            f"Pick up to {max_actions} actions. Reply with EXACTLY these four lines:\n"
            f"ACTIONS: <comma-separated indices, or empty>\n"
            f"REASONING: <one short sentence: why these actions, now>\n"
            f"EXPECTED_IMPACT: <one short sentence: summed cost spent, and capacity/task effects>\n"
            f"NEXT_STEPS: <one short clause: what you'd do next, or what would change your plan>\n\n"
            f"Example:\n"
            f"ACTIONS: 0,1,5\n"
            f"REASONING: Closing the housing/food gap before satisfaction drops further.\n"
            f"EXPECTED_IMPACT: Spends $2,400; adds shelter capacity and staffs the kitchen.\n"
            f"NEXT_STEPS: Watch flood tasks; pause new builds if budget < $5k.\n\n"
            f"One sentence per section. Write 'None.' if a section truly does not apply.\n"
            f"Constraint: do not include two construction actions targeting the same site this turn."
        )

    messages.append({"role": "user", "content": user_content})
    return messages


def query_llm(
    game_state: dict,
    actions: list,
    agent_cfg: dict,
    history: list,
) -> str:
    """
    Query the LLM for this agent's turn and return the raw response string.

    Args:
        game_state: Filtered observation (already subobservation_space-filtered).
        actions:    Filtered valid actions (already subaction_space-filtered).
        agent_cfg:  Agent config dict (or AgentConfig dataclass as dict).
        history:    Agent's conversation history list for this episode.

    Returns:
        Raw LLM response string, e.g. "0,3,5" or "". Empty string on failure.
    """
    # Support both dict and AgentConfig dataclass
    if hasattr(agent_cfg, '__dict__'):
        agent_cfg = agent_cfg.__dict__

    # Dispatch by provider
    provider = agent_cfg.get("llm_provider", "ollama").lower()
    agent_name = agent_cfg.get("subagent_name", "Unknown")

    if provider == "ollama":
        return _query_ollama(game_state, actions, agent_cfg, history)
    elif provider == "openai":
        return _query_openai(game_state, actions, agent_cfg, history)
    elif provider == "anthropic":
        return _query_anthropic(game_state, actions, agent_cfg, history)
    else:
        print(f"[llm_query] Unknown provider '{provider}' for agent '{agent_name}', "
              f"falling back to Ollama")
        return _query_ollama(game_state, actions, agent_cfg, history)


def _query_ollama(
    game_state: dict,
    actions: list,
    agent_cfg: dict,
    history: list,
) -> str:
    """Query Ollama LLM (local inference)."""
    agent_name = agent_cfg.get("subagent_name", "Unknown")
    model = agent_cfg.get("llm_model", "qwen2.5:0.5b")

    try:
        messages = _build_prompt(game_state, actions, agent_cfg, history)
        print(f"[llm_query] [{agent_name}] Querying OLLAMA model: {model}")

        response = ollama.chat(
            model=model,
            messages=messages,
            options={
                "temperature": 0.3,
                "num_predict": agent_cfg.get("turn_token_budget") or 64,
            }
        )
        return response["message"]["content"].strip()
    except Exception as e:
        print(f"[llm_query] [{agent_name}] Ollama query failed: {e}")
        return ""


def _query_openai(
    game_state: dict,
    actions: list,
    agent_cfg: dict,
    history: list,
) -> str:
    """Query OpenAI GPT models."""
    agent_name = agent_cfg.get("subagent_name", "Unknown")
    model = agent_cfg.get("llm_model", "gpt-3.5-turbo")

    if openai is None:
        print(f"[llm_query] [{agent_name}] OpenAI library not installed. "
              f"Run: pip install openai")
        return ""

    try:
        # Get API key from environment
        api_key_env = agent_cfg.get("api_key_env", "OPENAI_API_KEY")
        api_key = os.environ.get(api_key_env)
        if not api_key:
            print(f"[llm_query] [{agent_name}] OpenAI API key not found "
                  f"in environment variable '{api_key_env}'")
            return ""

        # Support custom base_url for third-party providers
        base_url = agent_cfg.get("llm_endpoint")
        if base_url:
            client = openai.OpenAI(api_key=api_key, base_url=base_url)
            print(f"[llm_query] [{agent_name}] Querying OPENAI (custom endpoint) model: {model}")
        else:
            client = openai.OpenAI(api_key=api_key)
            print(f"[llm_query] [{agent_name}] Querying OPENAI model: {model}")

        messages = _build_prompt(game_state, actions, agent_cfg, history)

        response = client.chat.completions.create(
            model=model,
            messages=messages,
            temperature=0.3,
            max_tokens=agent_cfg.get("turn_token_budget") or 64,
        )
        return response.choices[0].message.content.strip()
    except Exception as e:
        print(f"[llm_query] [{agent_name}] OpenAI query failed: {e}")
        return ""


def _query_anthropic(
    game_state: dict,
    actions: list,
    agent_cfg: dict,
    history: list,
) -> str:
    """Query Anthropic Claude models."""
    agent_name = agent_cfg.get("subagent_name", "Unknown")
    model = agent_cfg.get("llm_model", "claude-sonnet-4-6")

    if anthropic is None:
        print(f"[llm_query] [{agent_name}] Anthropic library not installed. "
              f"Run: pip install anthropic")
        return ""

    try:
        # Get API key from environment
        api_key_env = agent_cfg.get("api_key_env", "ANTHROPIC_API_KEY")
        api_key = os.environ.get(api_key_env)
        if not api_key:
            print(f"[llm_query] [{agent_name}] Anthropic API key not found "
                  f"in environment variable '{api_key_env}'")
            return ""

        client = anthropic.Anthropic(api_key=api_key)
        messages = _build_prompt(game_state, actions, agent_cfg, history)

        print(f"[llm_query] [{agent_name}] Querying ANTHROPIC model: {model}")

        # Anthropic requires system prompt separate from messages
        system_prompt = ""
        conversation = messages
        if messages and messages[0]["role"] == "system":
            system_prompt = messages[0]["content"]
            conversation = messages[1:]

        response = client.messages.create(
            model=model,
            system=system_prompt,
            messages=conversation,
            temperature=0.3,
            max_tokens=agent_cfg.get("turn_token_budget") or 256,
        )
        return response.content[0].text.strip()
    except Exception as e:
        print(f"[llm_query] [{agent_name}] Anthropic query failed: {e}")
        return ""
