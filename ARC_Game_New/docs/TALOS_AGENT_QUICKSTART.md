# CORA — agent quickstart (Talos)

You are an AI coding agent onboarding to the **CORA** project on the Talos server. This is a
fast orientation. Paths below reflect the pre-reprovision layout — **verify them first** (the box
was rebuilt), then trust what you find on disk over this doc.

## What CORA is

A disaster-relief simulation game (Unity client + Python server) where **LLM "officers" advise a
human "Director."** Research goals: benchmark LLMs as officers, train RL policies, and let
collaborators contribute configs/prompts/tools. Three arms share ONE action representation (a
text command-tag DSL parsed by `cmd_parser.py`): the live LLM officers, the RL gym, and the
benchmark.

## Repo & environment

- Repo (verify): `/home/conner/arc-game/ARC_Game_New/` — the Python server + tooling live here.
- Python: use the project venv, e.g. `./.venv/bin/python` (recreate with `requirements.txt` if the
  reprovision wiped it).
- **Proxy gotcha:** if proxy env vars are set, LLM gateway calls can fail — clear them for Python:
  `env -u ALL_PROXY -u HTTP_PROXY -u HTTPS_PROXY -u http_proxy -u https_proxy ./.venv/bin/python …`
- **Secrets (never commit, never print):** `config/keys.json` (API keys for the router) and `.env`
  (LLM provider keys, e.g. `ANTHROPIC_API_KEY` / `DDMLAB_ANTHROPIC_API_KEY` / `OPENAI_API_KEY`).
  Both are gitignored. If missing after the wipe, they must be restored from a secure source.

## Run it

- **Router** (FastAPI, serves the LLM officers over WebSocket):
  ```
  ./.venv/bin/python agent_router.py --config-dir config --keys-file config/keys.json \
      --port 9876 --log-dir logs/sessions
  ```
  Health: `GET /health`. List configs: `GET /configs` (Bearer key). Omit `--keys-file` for a dev
  key (`dev-local-key`) in local testing only.
- **Benchmark:** `benchmark_models.py` (model tiers, prompt packs via `--prompt-pack`,
  `--base-url/--api-key` for local/OpenAI-compatible endpoints). Logs to W&B (`cpulling/CORA_RL`).
- **RL gym:** `arc_game_gym_env_tcp.py` (text command-tag action space, same `cmd_parser`).
- **Unity build** (if needed): `./build_client.sh webgl` — must run **unsandboxed** with the Editor
  closed; a large merge can require wiping `Library/Bee` for a clean IL2CPP rebuild.

## Deployment on Talos (same-origin)

- Public URL: **`https://cora_game_llm.dev.ddmlab.com`** (WebGL client served by Apache).
- Apache vhost (`arc-game.conf`) reverse-proxies to the router: `/ws`, `/configs`, `/health`,
  `/bundles`; plus `Alias /sheet.csv`. Router runs on its port behind the proxy.
- Deploy mirror lives OUTSIDE the WebGL docroot (served via Apache Alias), so `rsync --delete` is
  safe. **Do not push to Talos without the maintainer's approval.**

## Contributor platform (recent work — read the design docs)

CORA now takes uploadable **bundles** (config + prompts, tools later):
- `provider_registry.py` — LLM backends are named by a **`provider` enum** (`anthropic`,
  `anthropic-ddmlab`, `cmu-gateway`, `openai`, `ollama-local`); configs NEVER carry raw
  `llm_endpoint`/`api_key_env` (security boundary). All `config/*.json` migrated to `provider`.
- `cora_schema.py` (Pydantic gate) + `bundle.py` (validate + base/delta compose).
- `cora_bundle.py` — CLI: `new` / `validate` / `render` / `push`.
- `POST /bundles` — authenticated config upload (per-key namespace, `config/_uploads/`).
- Phase 2 (planned): `cora_ext.py` tool/loop registries + `ctx` + a plugin dev workflow.

## Conventions (how the maintainer wants work done)

- **Verify before commit** — don't commit/push fixes until confirmed working; verify root causes,
  don't assert.
- **No AI/Claude branding in git/GitHub** — no `Co-Authored-By` or AI footers; the maintainer is
  sole author.
- **Don't modify configs without asking** — flag the issue, let the maintainer own the edit.
- **Don't push to shared infra (Talos)** without approval.
- Secrets (`keys.json`, `.env`) are never committed or printed.

## Read next (in `docs/`)

- `CORA_API_v1.md` — the frozen action grammar + observation schema (the core contract).
- `contributor-platform-design.md` — the platform synthesis (Pydantic-not-Hydra, provider-enum
  security, bundle/plugin model).
- `phase2-plugin-spec.md` — tool/hook extensibility + the `ctx` surface + staging dev workflow.
- `CONTINUOUS_AGENT.md` / `CHOICES_AGENT.md` — officer agent internals (if present).
