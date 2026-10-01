# CORA contributor platform — design synthesis

_Synthesis of four independent research passes (config management, AI/agentic-loop
extensibility, game modding/extensibility, AI-platform collaboration + upload/key security),
mapped onto CORA's code at the time. Design memo; for what was built, see the status note below._

> **Status (2026-10).** Built: the Pydantic bundle gate (`router/schema.py`), base + delta
> composition (`router/bundles.py`), the provider enum (`cora/llm/providers.py`), live upload
> (`POST /bundles`, `POST /plugins`), hashed cohort keys (`router/key_store.py`), and tool/hook
> plugins with an injected `ctx` (`router/plugin_api.py`, see docs/phase2-plugin-spec.md). The
> command-tag grammar this memo originally froze was retired: the shared action representation
> is now the typed tool calls in `cora/tools.py`, executed by `cora/executor.py`. Not built: the
> loop registry, bundle-declared tools, the worker-process sandbox.

Goal (maintainer): let external collaborators **upload their own config/prompt bundles now**,
**tools next** (requires code extensibility), and eventually **agentic-loop code**, and run
benchmarks — accessible on `cora_game_llm.dev.ddmlab.com`, git-versioned now + live-upload later,
**trusted lab collaborators** for now. Constant constraint from prior decisions: **one shared
action representation** across officers (frontier rollouts → SFT), the RL policy, and the benchmark
(the typed tool calls of `cora/tools.py`) — the plugin layer must never own it.

---

## The four passes converge on six rules

1. **Maximize the declarative tier; refuse *code* for now.** Games ship mostly pure data
   (Factorio prototypes, RimWorld XML Defs); OpenAI Evals accepts only YAML/JSONL naming existing
   classes; Open LLM Leaderboard bans `trust_remote_code`. CORA's bundle = **validated JSON only**.
2. **Validate uploads with Pydantic v2, not Hydra.** Hydra's entrypoint value is CLI-bound (its own
   maintainers discourage the Compose API for non-CLI use) and its `_target_` instantiation is a
   documented RCE vector for untrusted configs (2025–26 CVEs). Pydantic (`extra='forbid'`,
   `strict=True`) parses uploads against a schema *we own*, with precise 422s. Optional OmegaConf as
   the merge engine underneath.
3. **Never let a bundle name an endpoint, secret, or import path.** THE critical finding: today
   `llm_endpoint` → `openai.OpenAI(base_url=...)` and `api_key_env` → a real key read from env and
   sent to that endpoint. An uploaded `{llm_endpoint, api_key_env}` = SSRF + credential exfil.
   Resolve endpoints/secrets from a **server-side provider registry keyed by an enum**; strip those
   fields from anything uploaded. Same enum-dispatch spine later refuses import-path/callable fields
   (the Hydra `_target_` / `trust_remote_code` anti-pattern).
4. **Freeze two contracts NOW (cheap now, ruinous to move later):**
   (a) a **bundle manifest** — `name`, `author`, **immutable SemVer `version`**, a
   **`cora_api_version`** the bundle targets, `description`, `dependencies` (Factorio's
   `?`/`>=`/`!` model); (b) a **named, versioned core action/observation contract** — our
   officer↔director interface (the action tool schema + the obs schema; docs/CORA_API_v1.md). Bundles declare
   the version; the loader warns/refuses on mismatch.
5. **Override, don't fork; base + delta composition.** A contributor uploads a small *delta* that
   layers on a maintained base (RimWorld `PatchOperation`, Factorio `data.raw`). Pattern:
   validate base → validate delta (all-optional model, `extra='forbid'`) → deep-merge →
   **re-validate merged result**. Core updates then propagate instead of silently breaking stale copies.
6. **Host-mediated boundary from day one.** Even in-process/trusted, route the first custom tool
   through a harness-provided context (`ctx.execute(calls)`), never raw access to the session or
   the executor. Then the jump to real sandboxing (subprocess → gVisor/Firecracker) is a substrate
   swap, not a rewrite. The plugin does policy; the harness keeps orchestration, logging, and
   action execution.

---

## The bundle contract

Single JSON envelope (one uploadable artifact; future-proofs `tools`):

```jsonc
{
  "manifest": {
    "name": "cmu-lab/food-terse",        // namespaced (Gymnasium-style) to avoid collisions
    "author": "cmu-lab",
    "version": "1.2.0",                  // immutable SemVer once published
    "cora_api_version": "1.0",           // the core contract this targets
    "description": "terser food officer, higher hire caps",
    "dependencies": []
  },
  "config": { /* CoraConfig (full roster), or "delta": CoraConfigDelta. NO endpoint/secret */ },
  "global_prompt": { /* optional override */ },
  "tools": []                            // RESERVED; validated-empty today
}
```

`config` uses a **`provider` enum** (`anthropic` | `cmu-gateway` | `ollama-local` | …), NOT raw
`llm_endpoint`/`api_key_env`. A server-side `PROVIDER_REGISTRY` maps the enum → `{base_url, key_env}`.
Maintainer owns the registry; contributors pick from it.

---

## Extensibility seams

What was built (`router/plugin_api.py`; details in docs/phase2-plugin-spec.md):

- **Tool registry** — `register_tool(name, schema, *, acting, override_of)` / `get_tool`. The
  officer's dispatch (`router/officer_tools.py` `_dispatch_continuous_tool`) runs the typed action
  tools through `cora.executor`, then checks the plugin registry before the other built-ins, so a
  plugin can add a tool or explicitly replace a built-in. Plugin schemas join the officer's palette
  by name, filtered by its `tools` allowlist.
- **Hooks** — `register_hook(event)` for game events plus two loop-shaping hooks
  (`on_turn_start`, `on_step_end`). These stand in for the loop registry this memo proposed
  (`register_loop` / `make_loop`), which was not built.
- **`ctx` injection** — tools and hooks get a `ToolContext`: reads, `refresh_state`,
  `execute(calls)` (typed action calls through the shared executor), `propose_choices`, three
  state scopes, `log`, `run_blocking`. Action execution stays harness-owned, preserving the single
  shared action representation (the RL/SFT-parity constraint).
- **Discovery** — every `*.py` under `plugins/` plus `entry_points(group="cora.plugins")`.
  Versioning is additive-only (grow `ctx` by adding members; collision → error unless
  `override_of`).

---

## Live upload + multi-tenant keys (Platform B — later)

**Upload endpoint security checklist** (when built): API key required, namespace derived from the
**token not the body**; Apache `LimitRequestBody` + streaming byte cap (413); JSON only with
max-depth/keys/length caps (JSON-bomb); Pydantic `extra='forbid'`+`strict`; **strip
`llm_endpoint`/`api_key_env`/import-paths**; per-user namespaced storage (UUID filename, `realpath`
prefix-check, outside web root, non-exec); if any URL is ever config-derived, full OWASP SSRF
handling (scheme/host allowlist, resolve+pin IP, reject private/metadata ranges, no redirects).
Run bundle validation/execution in its **own worker process** (dropped caps, no ambient egress,
read-only FS, resource limits) so untrusted code later = swap what runs in the box.

**API-key issuance** (hardening of today's plaintext `keys.json`): opaque tokens
`rk_live_<prefix8>_<secret>` / `admin_live_...`, `secrets.token_urlsafe(32)`; store **SHA-256 hash
+ prefix only** (256-bit entropy → plain SHA-256, *not* bcrypt/argon2); SQLite
`api_keys/cohorts/usage_events`. **Opaque-token + DB lookup, not JWT** (research needs instant
revocation — kill a leaked key / pull a cohort mid-experiment = one-row update). Admin key mints
scoped cohort keys (`POST /admin/keys`): scopes = config-allowlist + quota + rate_limit + expiry +
cohort_id; raw key shown once. Per-request: prefix→row, `hmac.compare_digest`, revoked/expired
check, `slowapi` rate-limit, atomic quota decrement, insert `usage_events` (per-key + per-cohort
attribution). Maps onto existing `AgentService.keys` + `allowed_configs_for()`.

**Sandbox target for eventual untrusted code:** gVisor (10–30% I/O overhead, good default) or
Firecracker microVM (strongest, ~125ms boot); seccomp + cap-drop + no-egress + read-only FS
regardless.

---

## Phased plan

- **Phase 0 — freeze contracts (write-down, ~no code):** the bundle manifest schema + the versioned
  core action/observation contract (`cora_api_version` 1.0; now the typed tool schema + obs schema).
- **Phase 1 — bundle core + git-native (build first):** `router/schema.py` (Pydantic `CoraConfig` +
  all-optional `CoraConfigDelta` + `BundleManifest`), `router/bundles.py` (validate → base+delta
  merge → revalidate), `bundles/<owner>/<name>.json` in-repo. **Provider enum registry**
  (`cora/llm/providers.py`) replacing raw `llm_endpoint`/`api_key_env` (the security fix; also
  unifies) — flagged as a change to trusted configs the maintainer owns.
- **Phase 2 — extensibility seams:** `router/plugin_api.py` tool and hook registries + `ctx`
  injection (docs/phase2-plugin-spec.md).
- **Phase 3 — live upload endpoint:** `POST /bundles` per-key namespace + the security checklist;
  Apache proxy passthrough (Talos change — maintainer-owned).
- **Phase 4 — multi-tenant keys:** hashed opaque keys + admin→cohort minting + SQLite usage metering.
- **Later — untrusted code:** worker-process sandbox (gVisor/Firecracker).

## What to get right NOW (so we don't paint into a corner)
1. Bundle manifest with immutable SemVer + `cora_api_version`.
2. Versioned core action/observation contract, kept out of the plugin API.
3. Provider **enum** (never raw endpoint/secret) from day one.
4. Action execution harness-owned (`cora.executor`); plugins act via `ctx.execute`.
5. Every contributor artifact is namespaced (`labname/…`).

---

## Sources (representative; full lists in the four agent memos)
- Config: Hydra Compose-API caveat & `_target_` RCE CVEs; Pydantic v2 strict/`extra=forbid`; OmegaConf merge.
- Extensibility: Gymnasium `register`/`make`; DSPy `Module.forward`; Transformers `Auto*.register`;
  LangChain/LlamaIndex/OpenAI-Agents tool schemas; `importlib.metadata` entry points; `pluggy`.
- Game modding: Factorio data-lifecycle & `info.json`; RimWorld Defs/PatchOperation; Thunderstore
  manifest (immutable SemVer); WoW taint; Traction Point WASM capability API.
- Platform/security: lm-eval-harness/HELM/OpenAI-Evals/Inspect/HF submission models; OWASP SSRF;
  Unkey/Stripe key issuance; SHA-256-vs-bcrypt for high-entropy keys; gVisor/Firecracker.
