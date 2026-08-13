"""Back-compat shim — the playthrough script moved to `llm_playthrough.py`.

`llm_smoke_test` was a legacy name (a "smoke test" that had accreted load-bearing
library code). That code now lives in appropriately named modules; this module only
re-exports those names so existing callers keep working:
  * `benchmark_models.py`  — `import llm_smoke_test as smoke`
  * the Verlog RL fork     — `from llm_smoke_test import cmd_system_prompt, ...`

Real homes:
  * system prompts      -> cora_prompts
  * command parser      -> cmd_parser
  * observation adapters-> obs_adapters
  * gateway config      -> llm_gateway
  * the playthrough loop-> llm_playthrough

Delete this shim once `benchmark_models.py` and the Verlog fork import from the real
modules directly. It holds NO logic of its own.
"""
# SHARED command-grammar parser (source of truth: cmd_parser).
from cmd_parser import (  # noqa: F401
    _BUILD_ALIASES, _TRANSFER_RESOURCE, _action_index, _bundle_indices, _CMD_RE, parse_commands,
)
# SHARED system prompts (source of truth: cora_prompts).
from cora_prompts import (  # noqa: F401
    OLD_SYSTEM_PROMPT, NEW_SYSTEM_PROMPT, SYSTEM_PROMPT, MINIMAL_SYSTEM_PROMPT, idx_system_prompt,
    CMD_SYSTEM_PROMPT, CMD_TRANSFER_DOC, CMD_MINIMAL_SYSTEM_PROMPT, CMD_MINIMAL_V2_SYSTEM_PROMPT,
    cmd_system_prompt,
)
# SHARED observation adapters (env -> obs_encoder + A/B toggles; source of truth: obs_adapters).
from obs_adapters import (  # noqa: F401
    PROMPT_VERSION, MOTEL_COST_PER_PERSON_PER_DAY, _set_v2,
    compact_action, summarize, summarize_commands, render_state_compact, render_state_delta,
)
# SHARED gateway config (source of truth: llm_gateway).
from llm_gateway import GATEWAY_BASE, load_env_key  # noqa: F401
