# Agent Configuration Guide

This guide explains how to configure the multi-agent system for the ARC disaster relief game. You can create custom agent configurations to experiment with different team compositions, LLM models, and agent behaviors.

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Configuration File Structure](#configuration-file-structure)
3. [Agent Order Rule](#agent-order-rule)
4. [Agent Settings](#agent-settings)
5. [Action and Observation Spaces](#action-and-observation-spaces)
6. [LLM Configuration](#llm-configuration)
7. [Prompt Configuration](#prompt-configuration)
8. [Common Configuration Patterns](#common-configuration-patterns)
9. [Troubleshooting](#troubleshooting)

---

## Quick Start

1. **Copy an example config** from the `config/` directory
2. **Edit the JSON file** with your desired settings
3. **Run the game** with your custom config:
   ```bash
   python agent_router.py --config path/to/your_config.json
   ```

**Recommended starting points:**
- `single_agent_config.json` - Simplest setup, one AI agent
- `claude_multi_agent_config.json` - Multiple specialized agents
- `coach_agent_example.json` - Coach-style multi-turn planning

---

## Configuration File Structure

Every configuration file has two main parts:

```json
{
  "agent_order_rule": "sequential",
  "agents": [
    { /* agent 1 config */ },
    { /* agent 2 config */ },
    { /* director config */ }
  ]
}
```

### Required Elements

1. **`agent_order_rule`** - How agents are ordered each turn
2. **`agents`** - Array of agent configurations
3. **Exactly one director** - Must have `"role": "director"`

---

## Agent Order Rule

Controls the sequence in which agents take their turns.

### Available Options

| Value | Behavior | Use Case |
|-------|----------|----------|
| `"sequential"` | Agents act in the order listed in config | **Most common.** Predictable, allows for deliberate agent ordering |
| `"random"` | Agents act in random order each turn | Testing robustness to ordering changes |
| `"priority"` | Agents with higher priority act first | Future feature (not yet implemented) |

**Example:**
```json
{
  "agent_order_rule": "sequential",
  "agents": [...]
}
```

**Recommendation:** Use `"sequential"` unless you have a specific reason to randomize.

---

## Agent Settings

Each agent in the `agents` array has the following settings:

### Core Identity Settings

#### `subagent_name` (string, required)
The display name for this agent.

**Examples:**
```json
"subagent_name": "Construction Officer"
"subagent_name": "Strategic Coach"
"subagent_name": "Player"
```

**Tips:**
- Use descriptive names that reflect the agent's role
- Names appear in Unity UI and conversation logs
- No special characters required, spaces are fine

---

#### `role` (string, required)
Defines whether this agent is a subagent or the director.

**Valid values:**
- `"subagent"` - Recommends or executes actions
- `"director"` - Makes final decisions (usually the human player or final AI)

**Rules:**
- ✅ Exactly **one** agent must have `"role": "director"`
- ❌ Cannot have zero or multiple directors

**Example:**
```json
{
  "subagent_name": "Construction Officer",
  "role": "subagent",
  ...
},
{
  "subagent_name": "Player",
  "role": "director",
  ...
}
```

---

#### `actor_type` (string, required)
Controls how the agent behaves and what kind of output it produces.

**Valid values:**

| Type | Behavior | Output Format | When to Use |
|------|----------|---------------|-------------|
| `"auto"` | Directly executes actions | Comma-separated action indices: `"0,3,5"` | Autonomous agents that act independently |
| `"choices"` | Proposes multiple strategy packages | Named packages with descriptions | Advisors presenting options to director |
| `"manual"` | Human player input | N/A (waits for human input) | Director role for human players |
| `"coach"` | Multi-turn strategic planning | Turn-by-turn recommendations | Strategic advisors giving long-term plans |

**Example configurations:**

**Auto agent** (executes immediately):
```json
{
  "subagent_name": "Logistics Officer",
  "actor_type": "auto",
  "max_actions_per_turn": 3
}
```

**Choices agent** (presents options):
```json
{
  "subagent_name": "Resource Manager",
  "actor_type": "choices",
  "num_choices": 3,
  "max_actions_per_package": 4
}
```

**Coach agent** (multi-turn planning):
```json
{
  "subagent_name": "Strategic Coach",
  "actor_type": "coach",
  "num_turns": 3,
  "max_actions_per_turn": 3
}
```

**Manual agent** (human player):
```json
{
  "subagent_name": "Player",
  "actor_type": "manual"
}
```

---

#### `num_choices` (integer, optional)
**Only for `"choices"` agents.** Number of strategy packages to propose.

**Valid range:** 1-10 (typically 2-4)

**Example:**
```json
{
  "actor_type": "choices",
  "num_choices": 3  // Proposes 3 different strategies
}
```

**Tip:** More choices = more flexibility for director, but longer prompts and slower response.

---

#### `max_actions_per_package` (integer, optional)
**For `"choices"` agents:** Maximum actions in each package.  
**For `"auto"` agents:** Can also limit actions per turn (use `max_actions_per_turn` instead).

**Valid range:** 1-20 (typically 2-5)

**Example:**
```json
{
  "actor_type": "choices",
  "num_choices": 3,
  "max_actions_per_package": 4  // Each package has up to 4 actions
}
```

---

#### `num_turns` (integer, optional)
**Only for `"coach"` agents.** Number of future turns to recommend.

**Valid range:** 1-10 (typically 2-4)

**Example:**
```json
{
  "actor_type": "coach",
  "num_turns": 3,  // Recommends actions for next 3 turns
  "max_actions_per_turn": 3
}
```

---

#### `max_actions_per_turn` (integer, optional)
**For `"coach"` and `"auto"` agents.** Maximum actions per turn.

**Valid range:** 1-20 (typically 2-5)

**Example:**
```json
{
  "actor_type": "auto",
  "max_actions_per_turn": 5  // Can execute up to 5 actions per turn
}
```

**Tip:** Higher limits give agents more freedom but can lead to expensive/risky turns.

---

#### `talkinghead_endpoint` (string or null, optional)
Visual character displayed in Unity UI when this agent speaks.

**Valid values:**
- `"DisasterOfficer"` - Emergency response coordinator
- `"WorkforceService"` - Worker management specialist  
- `"LodgingMassCare"` - Shelter and housing expert
- `"ExternalRelationship"` - External communications officer
- `"FoodMassCare"` - Food and nutrition coordinator
- `null` - No visual character (for directors or background agents)

**Example:**
```json
{
  "subagent_name": "Construction Officer",
  "talkinghead_endpoint": "DisasterOfficer"
}
```

**Tip:** Match the visual character to the agent's domain for better player experience.

---

### Action and Observation Spaces

These settings control what actions an agent can take and what information it sees.

#### `subaction_space` (array, required)
Defines which action categories this agent can access.

**Action Categories:**

| Category | Actions Included | Use For |
|----------|------------------|---------|
| `"construction"` | Building new facilities | Construction specialists |
| `"deconstruction"` | Demolishing buildings | Emergency demolition, resource reallocation |
| `"worker"` | Hiring and training workers | Workforce management |
| `"worker_assignment"` | Assigning workers to buildings | Operations management |
| `"resource_transfer"` | Moving food/supplies between buildings | Logistics and supply chain |
| `"all"` | All action types | Generalist agents, directors |

**Format:**
```json
"subaction_space": [
  { "category": "construction" },
  { "category": "deconstruction" }
]
```

**Common patterns:**

**Specialist (limited domain):**
```json
"subaction_space": [
  { "category": "worker" },
  { "category": "worker_assignment" }
]
```

**Generalist (full access):**
```json
"subaction_space": [
  { "category": "all" }
]
```

---

#### `subobservation_space` (array, required)
Defines what game state information this agent sees.

**Observation Keys:**

| Key | Information Included | Use For |
|-----|---------------------|---------|
| `"sessionInfo"` | Day, time segment, current round | All agents (temporal context) |
| `"satisfactionAndBudget"` | Satisfaction %, budget $ | All agents (critical metrics) |
| `"workers"` | Hired workers, assignments, training status | Workforce agents |
| `"buildings"` | Facilities, status, workforce needs | Construction/operations agents |
| `"tasks"` | Active tasks, deadlines, priorities | Task-focused agents |
| `"constructionState"` | Available sites, construction progress | Construction specialists |
| `"mapState"` | Spatial layout, distances, roads | Logistics/delivery agents |
| `"logistics"` | Vehicles, deliveries, inventory | Supply chain agents |
| `"all"` | Complete game state | Generalist agents, directors |

**Format:**
```json
"subobservation_space": [
  "sessionInfo",
  "satisfactionAndBudget",
  "constructionState",
  "mapState"
]
```

**Common patterns:**

**Construction specialist:**
```json
"subobservation_space": [
  "sessionInfo",
  "satisfactionAndBudget",
  "constructionState",
  "mapState"
]
```

**Full visibility:**
```json
"subobservation_space": ["all"]
```

**Tip:** Give agents only the information they need. Less information = faster prompts, clearer focus.

---

## LLM Configuration

Settings for connecting to language models.

#### `llm_provider` (string, optional)
Which LLM service to use.

**Valid values:**
- `"anthropic"` - Claude models (Sonnet, Opus, Haiku)
- `"openai"` - GPT models or OpenAI-compatible APIs
- `"ollama"` - Local Ollama server
- `null` - For manual agents (no LLM needed)

**Example:**
```json
{
  "llm_provider": "anthropic",
  "llm_model": "claude-sonnet-4-6",
  "api_key_env": "ANTHROPIC_API_KEY"
}
```

---

#### `llm_model` (string, optional)
Model name to use.

**Anthropic models:**
```json
"llm_model": "claude-sonnet-4-6"       // Latest Sonnet
"llm_model": "claude-opus-4-6"         // Highest capability
"llm_model": "claude-haiku-4-5"        // Fastest, cheapest
```

**OpenAI models:**
```json
"llm_model": "gpt-4-turbo"
"llm_model": "gpt-3.5-turbo"
```

**Ollama models:**
```json
"llm_model": "llama3.1:8b"
"llm_model": "mistral:7b"
```

**Custom endpoint models** (e.g., CMU AI Gateway):
```json
"llm_model": "claude-haiku-4-5-20251001-v1:0"
```

---

#### `llm_endpoint` (string, optional)
Custom API endpoint URL. **Only needed for non-standard endpoints.**

**Examples:**
```json
"llm_endpoint": "https://ai-gateway.andrew.cmu.edu/v1"
"llm_endpoint": "http://localhost:11434"  // Local Ollama
```

**Default endpoints:**
- Anthropic: `https://api.anthropic.com/v1/messages`
- OpenAI: `https://api.openai.com/v1/chat/completions`
- Ollama: `http://localhost:11434`

**Tip:** Leave this `null` unless using a custom gateway or local server.

---

#### `api_key_env` (string, optional)
Environment variable name containing the API key.

**Common values:**
```json
"api_key_env": "ANTHROPIC_API_KEY"
"api_key_env": "OPENAI_API_KEY"
```

**How it works:**
1. Create a `.env` file in the project root:
   ```
   ANTHROPIC_API_KEY=sk-ant-api03-...
   OPENAI_API_KEY=sk-proj-...
   ```
2. The system reads the key from this environment variable

**Tip:** Never put API keys directly in config files! Always use environment variables.

---

#### `turn_token_budget` (integer, optional)
Maximum tokens for the LLM response.

**Common values:**
- `256` - Very short responses (action indices only)
- `512` - Concise advisors
- `1024` - Standard agents with brief reasoning
- `2048` - Detailed explanations
- `4096` - Long-form strategic analysis

**Example:**
```json
{
  "llm_model": "claude-haiku-4-5",
  "turn_token_budget": 512  // Keep responses concise
}
```

**Cost vs Quality tradeoff:**
- Lower budget = Cheaper, faster, more focused
- Higher budget = More detailed reasoning, but slower and more expensive

---

## Prompt Configuration

### Global Prompt

The global prompt is defined in `config/global_prompt_config.json` and provides shared game knowledge to all agents.

**Structure:**
```json
{
  "global_system_prompt": "GAME CONTEXT: You are advising on disaster relief operations...",
  "enabled": true,
  "version": "2.0",
  "last_updated": "2026-03-02"
}
```

**Settings:**
- `"enabled": true` - Include global prompt for all agents with `use_global_prompt: true`
- `"enabled": false` - Disable global prompt entirely
- `global_system_prompt` - The prompt text (can be very long, includes game mechanics)

**When to edit:**
- Change game mechanics descriptions
- Add new strategic guidance
- Update constraints or rules

---

### Agent-Specific Prompts

#### `use_global_prompt` (boolean, optional, default: `true`)
Whether to prepend the global prompt before this agent's system prompt.

**Values:**
- `true` - Agent gets global game context + their specific role
- `false` - Agent only gets their system prompt (no shared context)

**Example:**
```json
{
  "use_global_prompt": true,
  "system_prompt": "You are a construction expert managing infrastructure."
}
```

**Result:** Agent sees both global game mechanics AND their construction-specific role.

**When to disable:**
- Agent doesn't need full game context (e.g., narrow specialist)
- Testing prompt variations
- Reducing token usage

---

#### `system_prompt` (string, optional)
Custom instructions for this agent's role and behavior.

**Best practices:**

1. **Be specific about the role:**
   ```json
   "system_prompt": "You are a logistics coordinator managing workers and assignments. Select action indices to hire, train, and assign workers efficiently."
   ```

2. **Include strategic guidance:**
   ```json
   "system_prompt": "You are a construction expert. Prioritize critical infrastructure gaps and long-term sustainability. Balance urgent needs with budget constraints."
   ```

3. **Specify output format:**
   ```json
   "system_prompt": "You are the director. Select actions by their index numbers to maximize satisfaction while maintaining positive budget."
   ```

**Examples by actor_type:**

**Auto agent:**
```json
"system_prompt": "You manage disaster response operations. Each turn, select up to 5 action indices. Prioritize critical needs while staying within budget."
```

**Choices agent:**
```json
"system_prompt": "You are a resource allocation strategist. Propose strategic packages of actions for the director. Consider budget constraints and satisfaction priorities."
```

**Coach agent:**
```json
"system_prompt": "You are a strategic operations coach. Analyze situations deeply, identify key problems, and provide multi-turn action plans with clear rationale."
```

---

#### `can_address` (array, optional)
List of agent names this agent can send messages to. **Used for multi-agent conversations.**

**Format:**
```json
"can_address": ["Director", "Construction Officer"]
```

**Common patterns:**

**Agent can talk to director:**
```json
"can_address": ["Director"]
```

**Agent can talk to multiple agents:**
```json
"can_address": ["Director", "Resource Manager", "Logistics Officer"]
```

**No conversations:**
```json
"can_address": []
```

**Use case:** Enable agents to request information, coordinate, or propose collaborations.

---

## Common Configuration Patterns

### Pattern 1: Single AI Director

Simplest setup - one AI agent makes all decisions.

```json
{
  "agent_order_rule": "sequential",
  "agents": [
    {
      "subagent_name": "AI Director",
      "role": "subagent",
      "actor_type": "auto",
      "talkinghead_endpoint": null,
      "subaction_space": [{"category": "all"}],
      "subobservation_space": ["all"],
      "llm_provider": "anthropic",
      "llm_model": "claude-sonnet-4-6",
      "api_key_env": "ANTHROPIC_API_KEY",
      "turn_token_budget": 2048,
      "use_global_prompt": true,
      "system_prompt": "You manage all disaster response operations. Select action indices to maximize satisfaction while maintaining budget.",
      "max_actions_per_turn": 5
    },
    {
      "subagent_name": "Director",
      "role": "director",
      "actor_type": "manual",
      "subaction_space": [{"category": "all"}],
      "subobservation_space": ["all"]
    }
  ]
}
```

---

### Pattern 2: Specialized Multi-Agent Team

Division of labor - each agent handles specific domains.

```json
{
  "agent_order_rule": "sequential",
  "agents": [
    {
      "subagent_name": "Construction Officer",
      "role": "subagent",
      "actor_type": "auto",
      "talkinghead_endpoint": "DisasterOfficer",
      "subaction_space": [
        {"category": "construction"},
        {"category": "deconstruction"}
      ],
      "subobservation_space": [
        "sessionInfo",
        "satisfactionAndBudget",
        "constructionState",
        "mapState"
      ],
      "llm_provider": "anthropic",
      "llm_model": "claude-haiku-4-5",
      "api_key_env": "ANTHROPIC_API_KEY",
      "turn_token_budget": 512,
      "use_global_prompt": true,
      "system_prompt": "You manage infrastructure. Build facilities to meet critical needs.",
      "max_actions_per_turn": 2
    },
    {
      "subagent_name": "Workforce Manager",
      "role": "subagent",
      "actor_type": "auto",
      "talkinghead_endpoint": "WorkforceService",
      "subaction_space": [
        {"category": "worker"},
        {"category": "worker_assignment"}
      ],
      "subobservation_space": [
        "sessionInfo",
        "satisfactionAndBudget",
        "workers",
        "mapState"
      ],
      "llm_provider": "anthropic",
      "llm_model": "claude-haiku-4-5",
      "api_key_env": "ANTHROPIC_API_KEY",
      "turn_token_budget": 512,
      "use_global_prompt": true,
      "system_prompt": "You manage workers. Hire, train, and assign workers efficiently.",
      "max_actions_per_turn": 3
    },
    {
      "subagent_name": "Player",
      "role": "director",
      "actor_type": "manual",
      "subaction_space": [{"category": "all"}],
      "subobservation_space": ["all"]
    }
  ]
}
```

---

### Pattern 3: Advisory System with Choices

Agents propose options, player chooses.

```json
{
  "agent_order_rule": "sequential",
  "agents": [
    {
      "subagent_name": "Strategy Advisor",
      "role": "subagent",
      "actor_type": "choices",
      "num_choices": 3,
      "max_actions_per_package": 4,
      "talkinghead_endpoint": "FoodMassCare",
      "subaction_space": [{"category": "all"}],
      "subobservation_space": ["all"],
      "llm_provider": "anthropic",
      "llm_model": "claude-sonnet-4-6",
      "api_key_env": "ANTHROPIC_API_KEY",
      "turn_token_budget": 1024,
      "use_global_prompt": true,
      "system_prompt": "Propose 3 strategic packages. Each package should have a clear theme and rationale."
    },
    {
      "subagent_name": "Player",
      "role": "director",
      "actor_type": "manual",
      "subaction_space": [{"category": "all"}],
      "subobservation_space": ["all"]
    }
  ]
}
```

---

### Pattern 4: Coach-Guided Play

AI provides multi-turn strategic plan.

```json
{
  "agent_order_rule": "sequential",
  "agents": [
    {
      "subagent_name": "Strategic Coach",
      "role": "subagent",
      "actor_type": "coach",
      "num_turns": 3,
      "max_actions_per_turn": 3,
      "talkinghead_endpoint": "DisasterOfficer",
      "subaction_space": [{"category": "all"}],
      "subobservation_space": ["all"],
      "llm_provider": "anthropic",
      "llm_model": "claude-opus-4-6",
      "api_key_env": "ANTHROPIC_API_KEY",
      "turn_token_budget": 2048,
      "use_global_prompt": true,
      "system_prompt": "Analyze the situation deeply and provide a 3-turn strategic plan with rationale.",
      "can_address": []
    },
    {
      "subagent_name": "Player",
      "role": "director",
      "actor_type": "manual",
      "subaction_space": [{"category": "all"}],
      "subobservation_space": ["all"]
    }
  ]
}
```

---

## Troubleshooting

### Common Issues

**Problem:** "Config must have exactly one director"
- **Solution:** Ensure exactly one agent has `"role": "director"`

**Problem:** "Invalid actor_type 'xyz'"
- **Solution:** Use only: `"auto"`, `"choices"`, `"manual"`, `"coach"`

**Problem:** "Invalid action category"
- **Solution:** Check spelling. Valid: `"construction"`, `"deconstruction"`, `"worker"`, `"worker_assignment"`, `"resource_transfer"`, `"all"`

**Problem:** Agent produces no output
- **Solution:** Check `llm_provider`, `llm_model`, and `api_key_env` are set correctly
- **Solution:** Verify API key in `.env` file

**Problem:** "Failed to connect to LLM"
- **Solution:** For Anthropic: Check `ANTHROPIC_API_KEY` in `.env`
- **Solution:** For OpenAI: Check `OPENAI_API_KEY` in `.env`
- **Solution:** For custom endpoints: Verify `llm_endpoint` URL

**Problem:** Agent sees wrong actions
- **Solution:** Check `subaction_space` includes the right categories
- **Solution:** Example: If agent should build, include `{"category": "construction"}`

**Problem:** Agent makes poor decisions
- **Solution:** Adjust `system_prompt` with clearer instructions
- **Solution:** Increase `turn_token_budget` for more detailed reasoning
- **Solution:** Try a more capable model (e.g., Sonnet instead of Haiku)

---

## Testing Your Configuration

1. **Start with a working example:**
   ```bash
   python agent_router.py --config config/single_agent_config.json
   ```

2. **Make one change at a time:**
   - Change agent name
   - Test
   - Change action space
   - Test
   - Change model
   - Test

3. **Check the logs:**
   - Agent names appear in Unity UI
   - Action selections are logged
   - LLM responses are printed to console

4. **Common test scenarios:**
   - Can the agent build facilities? (needs `"construction"` in subaction_space)
   - Can the agent hire workers? (needs `"worker"` in subaction_space)
   - Does the agent see budget info? (needs `"satisfactionAndBudget"` in subobservation_space)

---

## Advanced Tips

### Performance Optimization

1. **Use faster models for simple tasks:**
   ```json
   "llm_model": "claude-haiku-4-5"  // Fast, cheap
   ```

2. **Limit token budgets:**
   ```json
   "turn_token_budget": 256  // Short responses only
   ```

3. **Restrict observation spaces:**
   ```json
   "subobservation_space": ["sessionInfo", "satisfactionAndBudget", "workers"]
   // Instead of ["all"]
   ```

### Cost Optimization

1. **Mix model tiers:**
   - Strategic coach: Sonnet or Opus (expensive, high quality)
   - Execution agents: Haiku (cheap, fast)

2. **Disable global prompt for specialists:**
   ```json
   "use_global_prompt": false  // Saves tokens
   ```

3. **Reduce num_choices:**
   ```json
   "num_choices": 2  // Instead of 4
   ```

---

## Example Configurations Summary

| Config | Pattern | Agents | Use Case |
|--------|---------|--------|----------|
| `single_agent_config.json` | Single AI | 1 AI agent | Simplest autonomous play |
| `claude_director_config.json` | AI director | 1 AI director | Autonomous director with no advisors |
| `claude_multi_agent_config.json` | Specialists + choices | 2 auto + 1 choices | Team of specialists with advisor |
| `openai_multi_agent_config.json` | OpenAI specialists | 3 auto agents | Multi-agent with OpenAI models |
| `coach_agent_example.json` | Coach + advisors | 1 coach + 2 agents | Strategic planning with execution |

---

**Need help?** Check the example configs in `config/` for working templates you can modify.
