# Officer configuration guide

A config file defines the roster for one GUI game: one human Director plus any number of LLM
officers, each with its scope, model and prompt. This is the field reference. The officer loop
itself is described in [CONTINUOUS_AGENT.md](CONTINUOUS_AGENT.md).

Sources of truth: `router/config.py` (`AgentConfig`, `RouterConfig`, the `VALID_*` vocabularies)
for config files, and `router/schema.py` (`OfficerConfig`, `CoraConfig`, `Bundle`) for uploaded
bundles.

## How a config is chosen

The router serves every `*.json` in `--config-dir` (default `config/`) that loads, except files
whose name starts with `keys`. The client fetches the catalog (`GET /configs`) and picks a config
by its file stem in the websocket hello frame; there is no `--config` flag. A key in
`config/keys.json` may carry a `configs` allowlist that limits which configs it sees (see
`config/keys.example.json`). Uploaded bundles appear as `<label>__<slug>`.

Shipped configs:

| File | Roster |
|---|---|
| `continuous_agent_local.json` | one all-scope officer (CMU gateway) + Director |
| `continuous_agents_domain.json` | four scoped officers (CMU gateway) + Director |
| `continuous_all_officers_local.json` | five scoped officers, one per talking-head slot (CMU gateway) |
| `continuous_all_officers_anthropic.json` | the same roster on `anthropic` |
| `continuous_all_officers_ddmlab.json` | the same roster on `anthropic-ddmlab`, `opening_mode: reactive` |
| `continuous_all_officers_qwen.json` | the same roster on `qwen-auton` |
| `continuous_all_officers_qwen_interagent.json` | as above, with officer-to-officer messaging (`can_address`) |

`config/global_prompt_config.json` holds the server-wide officer prompt (see below).

## Two validation layers

- **Config files** (trusted, maintained in the repo) load through `router.config.load_config` →
  `AgentConfig.__post_init__` / `RouterConfig.__post_init__`. Unknown keys are ignored. An officer
  may name its model backend either by `provider` (preferred) or by the raw
  `llm_provider` / `llm_endpoint` / `api_key_env` trio, not both.
- **Uploaded bundles** are validated first by the Pydantic models in `router/schema.py`
  (`extra='forbid'`, so a typo is an error), then by the same runtime checks. The raw trio and
  `llm_port` are rejected; `provider` is required for a `continuous` officer. Keys of the retired
  actors (`num_choices`, `max_actions_per_package`, `num_turns`, `max_actions_per_turn`,
  `choices_*`, `explain_*`) are accepted and dropped so older files still load
  (`RETIRED_OFFICER_KEYS`).

## Top level

```json
{
  "agent_order_rule": "sequential",
  "agents": [ { ... officer ... }, { ... director ... } ]
}
```

| Field | Values | Notes |
|---|---|---|
| `agent_order_rule` | `sequential` \| `random` \| `priority` | order in which officers run each round (`router/ordering.py`). `priority` passes validation but has no implementation in `ordering.py` and raises at the first round; use `sequential` or `random` |
| `agents` | list | exactly one entry with `role: "director"` |
| `global_prompt` | string or object | per-config override of the shared prompt; see Prompts |
| `tool_policy` | string | full replacement of the tool-use policy appended to every officer's system message |
| `turn_instructions` | object | authored strings of the per-turn message: `preamble`, `actions_header`, `default`, `brief_only`, `addressed`, `first_brief`, `after_brief`; `{title}` and `{capabilities}` are substituted |
| `tool_descriptions` | `{tool_name: text}` | rewords a built-in tool's description; parameters stay fixed. An unknown name is a warning, not an error |
| `title` / `display_name` | string | config files only: the name shown in the client's dropdown (default: file stem) |

Two officers may not share a `talkinghead_endpoint`; the config is rejected at load.

## Officer fields

| Field | Type / values | Default | Meaning |
|---|---|---|---|
| `subagent_name` | string | required | display name; also the address used by `can_address` |
| `role` | `subagent` \| `director` | required | exactly one director |
| `actor_type` | `manual` \| `continuous` | required | `manual` = the human Director; `continuous` = an LLM officer |
| `talkinghead_endpoint` | `DisasterOfficer`, `WorkforceService`, `LodgingMassCare`, `ExternalRelationship`, `FoodMassCare`, or null | null | the GUI's fixed officer slot (one tab each). An officer without one runs but its messages cannot appear in the UI. Also sets task jurisdiction for a bare `tasks` observation key |
| `subaction_space` | list of entries | `[]` | which game actions the officer may take; see Scopes |
| `subobservation_space` | list of keys | `["all"]` | which parts of the game state it sees; see Scopes |
| `provider` | provider enum | null | model backend, resolved server-side (`cora/llm/providers.py`) |
| `llm_model` | string | null | model id sent to the provider |
| `llm_provider`, `llm_endpoint`, `api_key_env` | strings | null | config files only: raw backend (`anthropic` \| `openai` \| `ollama`), base URL, and the name of the env var holding the key |
| `turn_token_budget` | int | 1024 when unset | max output tokens per model call; clamped to the endpoint's context window when that is known |
| `system_prompt` | string | a one-line default | the officer's role text, appended after the global prompt as `AGENT ROLE:` |
| `use_global_prompt` | bool | true | prepend the shared global prompt |
| `can_address` | list of officer names | `[]` | peers this officer may message with `send_message`, besides the Director. Names not in the roster are dropped |
| `tools` | list of tool names | null (all) | allowlist narrowing the tool palette; may include plugin tool names |
| `max_steps` | int | 8 | max model steps (tool-call rounds) per officer turn |
| `tool_mode` | `auto` \| `native` \| `text` | `auto` | `auto` = native tool calling, except a text (JSON) fallback for the `ollama` backend |
| `opening_mode` | `emergent` \| `brief_first` \| `reactive` | `emergent` | see below |
| `ledger_mode` | `block` \| `annotate` | `block` | `block`: a non-repeatable action already committed this phase is not re-sent; hire, train and transfer are never blocked |

Providers (`cora.llm.providers.Provider`): `anthropic`, `anthropic-ddmlab`, `cmu-gateway`,
`openai`, `ollama-local`, `qwen-local`, `qwen-auton`, `qwen3-4b-auton`, `minicpm5-2b-auton`. Each
maps to a backend, base URL and key env var; adding one is an edit to `providers.py`. API keys are
read from the environment (`setup.sh` writes them to `.env`).

`opening_mode`:

- `emergent` — the officer may act on any turn, including the unprompted round-start turn.
- `brief_first` — opens with a situation briefing and a question until the Director first
  speaks, then behaves as `emergent`.
- `reactive` — on an unprompted turn the acting tools (the action tools, `propose_choices`,
  `add_to_autonomy_list`) are removed from the palette and refused if called anyway; the officer
  can only brief. A turn triggered by the Director or by a peer's message gets the full palette.
  Standing orders the Director approved add their one tool back on unprompted turns.

## Scopes

`subaction_space` entries (an action is allowed if it matches any entry; `router/scope.py`):

| Entry | Admits |
|---|---|
| `{"category": "all"}` | everything |
| `{"category": "construction"}` | building new facilities |
| `{"category": "deconstruction"}` | tearing facilities down |
| `{"category": "worker"}` | hiring and training |
| `{"category": "worker_assignment"}` | staffing facilities |
| `{"category": "resource_transfer"}` | moving food or people |
| `{"category": "task_choice"}` | answering tasks; add `"group"` (`budget`, `workforce`, `food`, `lodging`, `disaster`) to limit to one task group |

Construction, deconstruction and assignment entries may add `"building_types": ["Kitchen", ...]`,
a case-insensitive substring match against the building type (`Kitchen`, `Shelter`,
`CaseworkSite`) or name. A tool call outside the officer's scope resolves as invalid and is
reported back to it.

`subobservation_space` keys: `all`, `sessionInfo`, `satisfactionAndBudget`, `workers` (alias of
`workforceState`), `buildings` (alias of `mapState`), `constructionState`, `logistics`, `tasks`,
`workforceState`, `mapState`, and `tasks:<group>`. Bare `tasks` shows the tasks Unity routes to
this officer's talking-head slot; `tasks:<group>` shows the tasks of that group.

`router.bundles.config_warnings` (run by `python -m router.cli check` and at upload) warns about an
officer with no `talkinghead_endpoint`, an empty `subaction_space`, a `task_choice`-only scope
(skipped in rounds with no matching task), or no `provider`.

## Prompts

An officer's system message is: the global prompt (if `use_global_prompt`), then
`AGENT ROLE: <system_prompt>`, then the tool policy (`tool_policy`, or the built-in
`OfficerLoopMixin._CONTINUOUS_TOOL_POLICY` in `router/officer_loop.py`).

The server-wide global prompt is `config/global_prompt_config.json`:
`{"global_system_prompt": "...", "enabled": true, "version": "...", "last_updated": "..."}`.
Its text has two halves, behavior rules and the game manual, separated by a line of 60 `=`. A
config's `global_prompt` may override them:

- a string → replaces the behavior half only; the manual is inherited;
- `{"behavior": "...", "manual": "..."}` → replaces either half;
- `{"global_system_prompt": "..."}` → replaces the whole text (`"enabled": false` means use the
  server default).

The router's developer panel (`/dev`) can edit prompts of a live session. Setting
`ARC_LOG_PROMPTS=1` writes the exact prompt each officer is sent to `logs/prompt_debug/<agent>.txt`.

## Minimal example

```json
{
  "agent_order_rule": "sequential",
  "agents": [
    {
      "subagent_name": "Food Officer",
      "role": "subagent",
      "actor_type": "continuous",
      "talkinghead_endpoint": "FoodMassCare",
      "subaction_space": [
        {"category": "construction", "building_types": ["Kitchen"]},
        {"category": "worker_assignment", "building_types": ["Kitchen"]},
        {"category": "task_choice", "group": "food"}
      ],
      "subobservation_space": ["sessionInfo", "satisfactionAndBudget", "buildings", "workers", "tasks:food"],
      "provider": "cmu-gateway",
      "llm_model": "us.anthropic.claude-sonnet-4-6",
      "turn_token_budget": 4096,
      "max_steps": 8,
      "opening_mode": "reactive",
      "system_prompt": "You are the Food Mass Care officer."
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

## Troubleshooting

- `Config must have exactly one director` — exactly one entry needs `"role": "director"`.
- `Invalid actor_type` — only `manual` and `continuous` exist; `auto`, `choices` and `coach` were
  retired.
- `Duplicate talkinghead_endpoint` — give each officer its own slot.
- `set provider OR raw llm_provider/llm_endpoint/api_key_env, not both` — pick one form.
- A config missing from the client's dropdown — the router printed `Skipping unloadable config`
  with the reason at startup, or the key's `configs` allowlist excludes it.
- An officer that never acts — check its `subaction_space`, and whether `opening_mode: reactive`
  is waiting for the Director to address it.
