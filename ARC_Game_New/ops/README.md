# ops/

Launch, cluster, deploy and build scripts. Each `cd`s to the repo root itself, so run them from
anywhere as `ops/<script>`.

## Running

| Script | What it does |
|---|---|
| `run_router.sh` | start the officer router: `.venv/bin/python -m router --port 9876 --config-dir config --log-dir logs/sessions`, adding `--keys-file config/keys.json` when that file exists. Env overrides: `PORT`, `CONFIG_DIR`, `LOG_DIR`, `PYTHON` |
| `run_benchmark.sh <pack> <model> [episodes] [-- extra args]` | `python -m bench --prompt <pack> --models <model> --episodes N --out bench_packs/<pack>__<model>`; extra args pass through to `python -m bench` |
| `benchmark_cluster.sbatch` | Slurm benchmark job: `MODE=api` (gateway models, `MODELS=...`) or `MODE=local` (starts `vllm serve $MODEL`, then benchmarks it) |
| `Caddyfile`, `Caddyfile.local` | same-origin reverse proxy for the WebGL client and the router (production with TLS; local HTTP on :8080) |

## Builds and deploy

| Script | What it does |
|---|---|
| `build_client.sh [mac\|windows\|linux\|webgl\|all]` | batch-mode Unity player build (Unity 2022.3.62f3; close the Editor first) |
| `upload_linux_build.sh <login-host>` | rsync `Build/Headless/Linux/` to the Auton cluster |
| `deploy_talos.sh [--no-pull] [--dry-run]` | run on the Talos server: pull, restart the router, verify (docs/TALOS_DEPLOY_RUNBOOK.md) |
| `verify_talos.sh [url]` | run from a laptop: endpoint checks against a deployment (`CORA_KEY` for authed checks) |

## Collaborator scripts

These source `env.sh`, which sets `CORA_URL` (default `http://localhost:9876`) and `CORA_KEY`
(default `dev-local-key`) unless already exported.

| Script | What it does |
|---|---|
| `get_data.sh [session_id]` | list your sessions (`/my/sessions`), or download one to `<session_id>.jsonl` |
| `keys.sh mint <cohort> <cfg1,cfg2> [count] [quota] [expires_days]` · `keys.sh list [cohort]` · `keys.sh revoke <prefix>` | key management (`mint` capability) |
| `reload_plugins.sh` | `POST /admin/plugins/reload` (`upload_code` capability) |
| `upload_config.sh <bundle.json> [plugin.py]` | validate a bundle, check a companion plugin, warn about unknown tool names, push |
| `demo_preference_model.py` | drive `plugins/preference_model.py` without a game |

Caveats in the current scripts:

- `keys.sh` and `reload_plugins.sh` call `/admin/*` on `CORA_URL`, but the router serves
  `/admin/*` only on its loopback admin port (`--admin-port`, default 9877). Point `CORA_URL` at
  `http://127.0.0.1:9877` (through an SSH tunnel for a remote server) when running them.
- `upload_config.sh` and `demo_preference_model.py` still call modules that no longer exist
  (`cora_bundle.py`, `cora_plugin.py`, `cora_ext`, `continuous_agent`, top-level `plugin_store`)
  and fail as written. Until they are updated, use the CLI:

```bash
python -m router.cli check bundles/mylab/mycfg.json              # validate + authoring warnings
python -m router.plugin_cli check plugins/mylab_tools.py         # offline plugin check (see note below)
python -m router.cli push bundles/mylab/mycfg.json               # upload (CORA_URL / CORA_KEY)
```

The plugin check currently crashes with `AttributeError ... 'emitted'` when it smoke-runs a tool
(bug in `router/plugin_cli.py`), after the import and registration checks pass.

An uploaded config's name is `<your-label>__<slug>` (e.g. the dev key's label `dev` gives
`dev__mycfg`); use that name when minting keys scoped to it. A new plugin file in `plugins/` loads
on router restart or on `POST /admin/plugins/reload`.
