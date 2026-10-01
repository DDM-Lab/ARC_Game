"""Back-compat shim — the playthrough script moved to `llm_playthrough.py`.

`llm_smoke_test` was a legacy name (a "smoke test" that had accreted load-bearing
library code). That code now lives in appropriately named modules; this module only
re-exports those names so existing callers keep working:
  * `benchmark_models.py`  — `import llm_smoke_test as smoke`
  * the Verlog RL fork     — `from llm_smoke_test import cmd_system_prompt, ...`

Real homes:
  * system prompts      -> cora.prompts (packs in prompts/)
  * tool execution      -> tool_executor
  * observation adapters-> obs_adapters
  * gateway config      -> llm_gateway
  * the playthrough loop-> llm_playthrough

Delete this shim once `benchmark_models.py` and the Verlog fork import from the real
modules directly. It holds NO logic of its own.
"""
# SHARED observation adapters (env -> obs_encoder + A/B toggles; source of truth: obs_adapters).
from obs_adapters import (  # noqa: F401
    PROMPT_VERSION, MOTEL_COST_PER_PERSON_PER_DAY, _set_v2, _set_v3,
    compact_action, summarize, summarize_commands, render_state_compact, render_state_delta,
)
# SHARED gateway config (source of truth: llm_gateway).
from llm_gateway import GATEWAY_BASE, load_env_key  # noqa: F401
