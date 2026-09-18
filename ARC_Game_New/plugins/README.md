# plugins/

Drop-in directory for CORA tool/hook plugins. Every `*.py` here is imported at router startup
(`cora_ext.load_plugins(["plugins"])`), which runs its `@register_tool` / `@register_hook`
decorators. A plugin reaches the game only through the injected `ToolContext` (`ctx`).

- Validate a plugin offline first:  `python cora_plugin.py check <path>`
- Reference example (not auto-loaded): `examples/plugins/example_tools.py` — copy it here to activate.
- See `docs/phase2-plugin-spec.md` for the full contract.

This directory is intentionally empty of plugins by default so a fresh router loads none.
