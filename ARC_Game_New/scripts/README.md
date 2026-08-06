# CORA collaborator scripts

Thin wrappers over the CLI + HTTP API for the common procedures. Point them at any router:
```bash
export CORA_URL=http://localhost:9876      # or the Talos URL
export CORA_KEY=dev-local-key              # your key (admin key for minting)
```

| Script | What it does |
|---|---|
| `upload_config.sh <bundle.json> [plugin.py]` | validate a config, check a **companion plugin** if present, warn about tool references with no provider, then push. Auto-detects a plugin at `<bundle>.py`. |
| `keys.sh mint <cohort> <cfg1,cfg2> [count] [quota] [expires_days]` | mint scoped participant keys (needs `mint` cap) |
| `keys.sh list [cohort]` · `keys.sh revoke <prefix>` | audit / revoke |
| `get_data.sh [session_id]` | list your sessions, or download one session's log |

Quick start from a template:
```bash
cp templates/config.bundle.json bundles/mylab/mycfg.json     # edit it
cp templates/tool_plugin.py     plugins/mylab_tools.py        # optional; edit, then it auto-loads on restart
scripts/upload_config.sh bundles/mylab/mycfg.json            # validate + tool-check + push
scripts/keys.sh mint study-A dev__mycfg 20 50 30            # 20 keys, quota 50, 30-day expiry
scripts/get_data.sh                                         # list your cohort's sessions
```
Note: an uploaded config's name is `<your-label>__<slug>` (e.g. dev key → `dev__mycfg`) — use that
when minting keys. A newly-added plugin file requires a **router restart** to load.
