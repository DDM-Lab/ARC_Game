# CORA prompt packs — swap prompts, get benchmark numbers (low-code)

A **prompt pack** is a single JSON file that holds the director's system prompt as named,
editable text *sections* plus a `template` that composes them. Edit the JSON, run one command,
read the scores and transcripts — no Python required.

## TL;DR

```bash
# 1. see what's installed
.venv/bin/python prompt_packs.py

# 2. copy a built-in pack and edit the prose
cp prompts/cmd_minimal.json prompts/my_prompt.json
$EDITOR prompts/my_prompt.json          # change the text inside "sections"

# 3. preview EXACTLY what the model will receive
.venv/bin/python prompt_packs.py my_prompt

# 4. benchmark it (spawns the game for you) and get numbers + transcripts
./run_benchmark.sh my_prompt gpt-5-mini 5
```

Results land in `bench_packs/my_prompt__gpt-5-mini/`:
`episodes.jsonl` (full per-round transcripts) and `summary.json` (aggregate scores).

## What a pack looks like

```jsonc
{
  "name": "cmd_minimal",
  "description": "one-line summary",
  "format": "cmd",                 // "cmd" (command tags) or "idx" (numbered menu + JSON)
  "variant": "minimal",            // free-text label, logged with every episode
  "sections": {                    // <-- EDIT THESE. Each is a plain block of prompt text.
    "intro":      "You are the director ...",
    "objective":  "OBJECTIVE: ...",
    "entities":   "ENTITIES & RULES ...",
    "how_to_act": "HOW TO ACT ...",
    "response":   "RESPOND with ...",
    "transfer_doc": "...",                  // only shown when transfers are enabled
    "image_preamble_synthetic": "...",      // only shown in image runs
    "image_preamble_real": "..."
  },
  "template": "{intro}{objective}{entities}{how_to_act}{response}{transfer_doc}{image_preamble}",
  "gates": { "transfer_doc": "manual_transfers", "image_preamble": "image" }
}
```

### The `template` is where you "present the information as you like"
It's just the section names in `{braces}`, concatenated in order. Reorder them, drop one,
or split a section into two (add a new key to `sections` and a matching `{key}` to the
template). Whatever text ends up between the braces is exactly what the model sees.

### Gates (leave these alone unless you know you want them)
A section named in `gates` only appears when its condition holds:
- `transfer_doc` → only when the run enables manual resource transfers (`--transfers manual`).
- `image_preamble` → only in image runs; filled from `image_preamble_<mode>`.

You almost always benchmark in `task_only` (no transfers) + text-only, so those two collapse
to nothing and you're just editing the five base sections.

## Built-in packs

| pack | format | notes |
|------|--------|-------|
| `cmd_original` | cmd | strategy-laden default (lodging economics, casework timing, horizon) |
| `cmd_minimal` | cmd | PIMMUR minimal-control: mechanics + objective only, no strategy |
| `cmd_minimal_v2` | cmd | minimal + fix layer (Passive-fixtures note; build-then-staff rule) |
| `idx_original` | idx | strategy-laden, numbered-menu + JSON output |
| `idx_minimal` | idx | minimal-control, numbered-menu + JSON output |
| `cmd_minimal_flagship_v0` | cmd | **historical** exact prompt behind the published flagship numbers (`prompt_sha cce393cd9095`), pinned for reproducibility |

Every built-in renders **byte-identically** to the original hardcoded prompt, so the
`prompt_sha` logged per episode is preserved and old runs stay reproducible.

## Reproducibility

Each episode record logs `prompt_pack` (the name), `system_variant`, and `prompt_sha`
(a sha1 fingerprint of the exact system text sent). Two runs with the same pack + model +
settings produce the same `prompt_sha`. To recover the exact text of any past run, read the
`system_prompt` field stored in its `episodes.jsonl`.

## Running against a self-hosted / local model

`run_benchmark.sh` passes extra args straight through. Point it at any OpenAI-compatible
endpoint:

```bash
./run_benchmark.sh my_prompt Qwen3-4B 5 -- \
  --base-url http://localhost:8080/v1 --api-key placeholder
```

## Notes / current limits

- The benchmark **spawns its own headless game per episode**, so you need the local
  `Build/Headless/<platform>/…` build present. (Connecting to a *shared, already-running*
  game server — the "upload your pack and hit an API key" flow — is a planned extension; the
  gym env already supports attach mode, it just isn't wired into this wrapper yet.)
- Only the **prose** (system-prompt sections, transfer doc, image preambles) is pack-editable.
  How the game *state* is serialized (the observation encoders) stays in Python by design —
  it's logic, not prompt text.
