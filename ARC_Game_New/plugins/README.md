# plugins/

Drop-in directory for officer tool/hook plugins. At startup the router imports every `*.py` here
(`router.plugin_api.load_plugins(["plugins"])`; files starting with `_` are skipped), which runs
their `@register_tool` / `@register_hook` decorators. A plugin imports only `router.plugin_api`
and reaches the game only through the injected `ToolContext` (`ctx`).

Loaded by default:
- `example_tools.py` — `unmet_needs` (read-only), `preference_choices` (acting), and an
  `on_choice_resolved` hook.
- `preference_model.py` — a Dirichlet model of the human Director's choices in `ctx.persist`, and
  the acting tool `preferred_choices`.

Check a plugin offline first: `python -m router.plugin_cli check <path>`. Note: as of 2026-10 the check crashes with `AttributeError: 'MockToolContext' object has no attribute 'emitted'` (a bug in `router/plugin_cli.py`) once it smoke-runs a tool, after the import and registration checks have passed.

More examples are in `examples/plugins/` (not auto-loaded). They and `templates/tool_plugin.py`
still import the old module name `cora_ext`; change that import to `router.plugin_api` before
using them. See `docs/phase2-plugin-spec.md` for the full contract.
