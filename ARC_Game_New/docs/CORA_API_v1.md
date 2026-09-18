# CORA API v1 — the core contributor contract

`cora_api_version: "1.0"`

This is the **stable contract** every uploaded bundle targets. Engine internals may change; this
contract is a promise. A bundle's `manifest.cora_api_version` declares the version it was authored
against; the loader **warns on minor drift and refuses on major drift** (SemVer). Changing anything
in this document is a versioned event (bump `cora_api_version`), not a silent edit.

Kept deliberately OUT of the plugin API surface: contributors' tools/loops (later phases) reach
these only through harness-provided context, never by constructing them directly.

---

## 1. Action grammar (the shared action representation)

All arms — LLM officers, the RL policy, and the benchmark — serialize actions to ONE text
command-tag DSL, parsed by `cmd_parser.parse_commands`. This is the single most important frozen
surface (it's what makes frontier-officer rollouts, the RL policy, and the benchmark comparable —
see `tool_schema_research.md`). The v1 grammar is exactly seven tags:

| Tag | Form | Meaning |
|---|---|---|
| `build` | `<build>TYPE,SITE</build>` | construct a facility of TYPE at SITE |
| `hire` | `<hire>untrained\|trained,N</hire>` | hire N workers |
| `train` | `<train>N</train>` | train N untrained workers |
| `staff` | `<staff>BUILDING,N</staff>` | assign N workers to BUILDING |
| `deconstruct` | `<deconstruct>NAME</deconstruct>` | demolish facility NAME |
| `transfer` | `<transfer>food\|people,SRC,DST,N</transfer>` | move N food/people SRC→DST |
| `task` | `<task>TOKEN,CHOICE</task>` | answer task TOKEN with option CHOICE |

`TOKEN` is the stable task token (`obs_encoder.stable_task_token`), not a turn-volatile integer.
Regex authority: `cmd_parser._CMD_RE`.

## 2. Observation schema

Officers/policies receive an observation assembled by `obs_encoder` and scoped per-agent. The v1
observation vocabulary (`agent_config.VALID_OBS_KEYS` + aliases):

- Section keys: `sessionInfo`, `satisfactionAndBudget`, `constructionState`, `logistics`, `tasks`,
  `workers` (alias → `workforceState`), `buildings` (alias → `mapState`), `all`.
- Task narrowing: `tasks:<group>` where group ∈ `{budget, workforce, food, lodging, disaster}`.

## 3. Action scoping vocabulary (`subaction_space`)

Category ∈ `{construction, deconstruction, worker, worker_assignment, resource_transfer,
task_choice, all}`; `task_choice` takes an optional `{"group": <slug>}` sub-scope (same groups as
above); construction/assignment/deconstruction take an optional `building_types` list (substring
match). Authority: `agent_config.VALID_CATEGORIES`, `VALID_TASK_GROUPS`.

## 4. Provider vocabulary (enum, not raw endpoints)

A bundle names a provider by **enum**, never a raw endpoint or secret. The server-side
`PROVIDER_REGISTRY` resolves the enum → `{provider, base_url, key_env}`. v1 providers:
`anthropic`, `anthropic-ddmlab`, `cmu-gateway`, `openai`, `ollama-local`. Adding a provider is a
server-side registry edit (auditable), not a bundle field.

## 5. Bundle manifest

```jsonc
{
  "name": "<owner>/<slug>",        // namespaced; owner set from the uploader's key on live upload
  "author": "<free text>",
  "version": "MAJOR.MINOR.PATCH",  // immutable SemVer once published
  "cora_api_version": "1.0",       // this contract
  "description": "<one line>",
  "dependencies": []               // reserved: ["owner/other>=1.0.0"] style, Factorio ?/>=/! model
}
```

## 6. Compatibility policy

- `cora_api_version` MAJOR mismatch → loader refuses the bundle.
- MINOR mismatch (bundle older than server) → loader warns, proceeds (additive-only within a major).
- The action grammar (§1) and provider enum (§4) are the surfaces most likely to grow; growth is
  additive within v1.x (new tags/providers), breaking changes bump to v2.
