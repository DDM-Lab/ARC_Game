# Phase B3 — Verlog fork swap to shared `cora_tools`

The ARC_Game side is done: `cora_tools.py` is the canonical typed schema, `translate_tool_calls`
is the shared translator, and `generated/arc_tools.yaml` is the generated RL tool_config. What
remains is applying these in the **Verlog fork** (`cpulling/Verlog`, branch `rl_cora_jul26`) —
left to you because it's the live-training repo. All of it is imported via the existing
`ARC_GAME_PATH` channel, so no new plumbing.

## 1. Replace the tool schema (build artifact)
Regenerate and overwrite the fork's tool_config from the canonical schema — never hand-edit:
```bash
# in ARC_Game_New:
python -c "import cora_tools; open('generated/arc_tools.yaml','w').write(cora_tools.arc_tools_yaml())"
cp generated/arc_tools.yaml <verlog>/verl/envs/environments/arc_game/tool_config/arc_tools.yaml
```
(Or have the sbatch generate it at launch so it can never drift.)

## 2. Drop the local synthesizer, import the shared translator
In `verl/envs/environments/arc_game/llm_agents_wrapper.py`:
- **Delete** the local `_TOOL_TO_TAG` mapping and the `_synthesize_tags_from_tool_calls` method.
- **Import** (ARC_GAME_PATH is already on sys.path via `_ensure_smoke_on_path`):
  ```python
  from cora_tools import translate_tool_calls
  ```
- At the call site (where `_synthesize_tags_from_tool_calls(action)` was called), use:
  ```python
  tags, meta = translate_tool_calls(action.get("tool_calls") or [])
  # tags -> parse_commands(...) exactly as before; `meta` = {received, valid, unknown_name, bad_args}
  ```

## 3. Make bad tool-calls reward-visible (kills the silent dropout)
The old synthesizer swallowed bad args in a `try/except` (meta `bad_json`, no signal). Now surface
`meta["unknown_name"]` and `meta["bad_args"]` in the wrapper's metrics dict AND as a small negative
reward term (e.g. a per-call format penalty), so the policy gets gradient on malformed calls instead
of them vanishing.

## 4. (Optional) Share the tool-mode prompt too
`arc_game/__init__.py:get_instruction_prompt` splices a local `_TOOL_HOW_TO_ACT`. Replace the
tool-mode branch with:
```python
from cora_prompts import tool_system_prompt
return tool_system_prompt(manual_transfers=False, variant="minimal")
```
so the RL policy, the live officer, and the benchmark tool mode read the **identical** tool prompt.

## Result
After this, the RL wing consumes the same schema (`arc_tools.yaml`), the same translator
(`translate_tool_calls`), and the same prompt (`tool_system_prompt`) as the officer and benchmark —
the three wings fully unified on the tool surface. Verify with one short `train_arc_game_qwen3_4B_tools*`
rollout: the model should emit native `<tool_call>`s, `translate_tool_calls` should turn them into
tags, and `behavior/*` metrics should show the new `unknown_name`/`bad_args` counts.
