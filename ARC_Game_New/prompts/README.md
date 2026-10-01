# Prompt packs

Every system prompt a model is given comes from a JSON file in this folder. To try a new prompt,
copy a pack, edit its text, and run the benchmark with it — no Python needed:

```bash
cp prompts/minimal_v6_1.json prompts/my_prompt.json      # then edit the "sections"
./run_benchmark.sh my_prompt gpt-5-mini 5
python -m cora.prompts my_prompt                          # print it exactly as the model sees it
```

| Pack | What it is |
|---|---|
| `minimal_v6_1` | default. Game rules and the action grammar, no strategy; numbers come from the observation; unavailable choices are marked |
| `minimal_v6` | the Sep 2026 benchmark prompt (`prompt_sha 09b23ffd5e31`), kept to reproduce those runs |

## Format (schema_version 2)

```json
{
  "schema_version": 2,
  "name": "my_prompt",
  "description": "one line",
  "sections": {"preamble": "...", "how_header_typed": "...", "how_header_hermes": "...", "...": "..."},
  "template": "{preamble}{how_header}{tool_signatures}{transfer_signature}{order_head}{order_transfer}{order_tail}{image_preamble}",
  "gates": {"how_header": "wire_format", "transfer_signature": "manual_transfers",
            "order_transfer": "manual_transfers", "image_preamble": "image"},
  "observation": {"mark_unavailable_choices": true}
}
```

- `template` joins the `sections` in order; edit, add or reorder sections freely.
- `gates` switch a placeholder on the run's settings:
  - `wire_format`: filled from `<name>_typed` or `<name>_hermes` (the RL trainer uses hermes);
  - `manual_transfers`: included only when standalone transfers are offered;
  - `image`: filled from `<name>_synthetic` / `<name>_real` when the model is shown an image.
- `observation` (optional) lists observation features the text relies on; `mark_unavailable_choices`
  marks the choices the game would grey out for a player.

Every episode records the pack name and `prompt_sha` (sha1 of the exact text sent), so results stay
attributable when a pack is edited.

## Rule ablation

`ablation/rules.json` maps rule ids (R01–R34) to passages of `minimal_v6`;
`ablation/paraphrases.json` holds reworded variants of individual rules. Use them with
`--ablate R07`, `--ablate R07,R09,R10` or `--ablate R07_P1_direct`
(see `cora/prompt_ablation.py`).
