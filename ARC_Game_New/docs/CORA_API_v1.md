# CORA API v1 — the core contributor contract

`cora_api_version: "1.0"` (`router/schema.py` `CORA_API_VERSION`)

This is the contract an uploaded bundle targets: the action tools, the observation and scope
vocabularies, the provider names and the bundle manifest. A bundle's `manifest.cora_api_version`
declares the version it was written against; the loader refuses a major mismatch and warns on a
newer minor (`router/bundles.py` `_check_api_version`). Changing anything here is a versioned
event, not a silent edit.

---

## 1. Action tools (the shared action representation)

Every front end acts through the same typed tool calls: the LLM officers in the GUI, the
benchmark, the RL policy and the baseline policies. The schema is defined once in `cora/tools.py`
(`TOOLS`, rendered by `openai_tools()` and, for Verlog, `arc_tools_yaml()`), and calls are resolved
and executed by `cora/executor.py`. Tools return nothing to a benchmark or RL policy; the router
reports each call's outcome to the officer.

| Tool | Arguments | Meaning |
|---|---|---|
| `build` | `type` (`kitchen` \| `shelter` \| `casework`), `site_id` (int) | construct at a free site offered this turn |
| `hire` | `kind` (`untrained` \| `trained`), `count` (int) | hire workers; they arrive later, not this turn |
| `train` | `count` (int) | train untrained workers |
| `staff` | `site` (facility name), `count` (optional, workforce units) | staff a built facility; omit `count` to staff it fully (partial staffing is refused) |
| `deconstruct` | `site` (facility name) | tear a facility down; no refund |
| `task` | `task_id` (string), `choice_id` (int) | answer an active task with one of its offered choices |
| `transfer` | `resource` (`food` \| `people`), `source`, `dest`, `qty` | move a resource with a free vehicle; manual-transfer mode only |

- Facility names match case-insensitively by substring.
- `task_id` is the stable task token from `cora.observation.task_token` (e.g. `BUDGET_DAILY`), the
  same across turns. `choice_id` is the id printed for that choice this turn; ids are neither
  0-based nor contiguous.
- `transfer` is `manual_only`. The benchmark and RL offer it only with manual transfers
  (`--transfers manual`); router officers always have it.
- Calls run in a fixed order whatever order they were written in: task answers, deconstruct,
  build, hire, train, staff, transfer (`executor.ORDER`).
- Each call ends `executed`, `refused` (the game said no, with its reason) or `invalid` (unknown
  tool, bad arguments, nothing to apply it to, or outside the officer's scope).

Bundles cannot change tool parameters. `tool_descriptions` may reword a tool's description only.

## 2. Observation vocabulary (`subobservation_space`)

Officers receive the observation built by `cora/observation.py`, filtered per officer by
`router/scope.py` `filter_observation` and `Session._filter_state`. Keys (authority:
`router/config.py` `VALID_OBS_KEYS`):

- Sections: `all`, `sessionInfo`, `satisfactionAndBudget`, `constructionState`, `logistics`,
  `tasks`, `workers` (alias of `workforceState`), `buildings` (alias of `mapState`),
  `workforceState`, `mapState`.
- Task narrowing: `tasks:<group>`, group ∈ `{budget, workforce, food, lodging, disaster}`
  (`cora.observation.task_group`).

## 3. Action scope vocabulary (`subaction_space`)

Category ∈ `{construction, deconstruction, worker, worker_assignment, resource_transfer,
task_choice, all}`. `task_choice` takes an optional `{"group": <slug>}` (same groups as above).
Other categories take an optional `building_types` list (case-insensitive substring of the building
type or name). Authority: `router/config.py` `VALID_CATEGORIES`, `VALID_TASK_GROUPS`; filtering:
`router/scope.py` `filter_actions`.

## 4. Provider vocabulary (enum, not raw endpoints)

A bundle names its model backend by enum, never by endpoint or secret. The server-side
`PROVIDER_REGISTRY` in `cora/llm/providers.py` resolves each name to `(backend, base_url,
key_env)`. Current names: `anthropic`, `anthropic-ddmlab`, `cmu-gateway`, `openai`,
`ollama-local`, `qwen-local`, `qwen-auton`, `qwen3-4b-auton`, `minicpm5-2b-auton`. Adding a
provider is a reviewed edit to that file, not a bundle field.

## 5. Bundle manifest

```jsonc
{
  "name": "<owner>/<slug>",        // namespaced; owner set from the uploader's key on live upload
  "author": "<free text>",
  "version": "MAJOR.MINOR.PATCH",  // immutable SemVer once published
  "cora_api_version": "1.0",       // this contract
  "description": "<one line>",
  "dependencies": []               // reserved
}
```

The full bundle envelope (`router/schema.py` `Bundle`): `manifest`; exactly one of `config` (a
full roster) or `delta` (overrides matched to a base config by `subagent_name`); optional
`global_prompt`, `tool_policy`, `turn_instructions`, `tool_descriptions`; and `tools`, which must
be empty in v1.0. Officer fields are listed in [AGENT_CONFIG_GUIDE.md](../AGENT_CONFIG_GUIDE.md).

## 6. Compatibility policy

- `cora_api_version` major mismatch → the loader refuses the bundle.
- A bundle targeting a newer minor than the server → warning, then proceeds.
- The tools (§1) and providers (§4) are the surfaces most likely to grow; growth within v1.x is
  additive, and breaking changes bump to v2.
