"""The officer router service: a FastAPI app hosting many concurrent game clients.

Each client opens a WebSocket and sends a ``hello`` frame with its API key and config name; the
service validates the key, loads the config and creates an isolated router.session.Session to drive
that game. The same app serves the contributor endpoints (bundle and plugin upload, session
export), the admin API (keys) and the developer panel.

Flow per session:
  1. WebSocket accepted; first frame must be ``{type: hello, api_key, config}``
  2. The service validates the key, loads the named config, creates a Session
  3. It replies ``{type: hello_ack, session_id, ...}``
  4. From then on the Session's message protocol drives the game (begin_round, choice_made,
     director_message, ...)

Usage:
    python -m router --keys-file config/keys.json --config-dir config/ \
                     --port 9876 --log-dir logs/sessions
"""
from __future__ import annotations

import asyncio
import json
import argparse
import hashlib
import os
import re
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Optional

from fastapi import FastAPI, WebSocket, Header, HTTPException, Request, Response
from fastapi.middleware.cors import CORSMiddleware
import uvicorn

from router import config as router_config
from router.config import load_config
from router import bundles
from router.bundles import load_bundle, BundleError
from router import plugin_api
from router import plugin_store
from router import key_store
from cora.scoring import REWARD_WEIGHTS

from router.session import PEER_TRIGGER_BUDGET_PER_ROUND, Session, _now



# ── Multi-tenant Service ─────────────────────────────────────────

# Interactive API docs enumerate EVERY route and its schema — a free map of the attack
# surface (including which capabilities exist). Off unless explicitly opted in for local
# dev: safe-by-default, since production is the case you can forget to harden.
_DEV_DOCS = os.environ.get("CORA_DEV_DOCS", "").strip().lower() in ("1", "true", "yes")
_DOCS_KW = {} if _DEV_DOCS else {"docs_url": None, "redoc_url": None, "openapi_url": None}

# CONTROL PLANE / DATA PLANE SPLIT.
# `app` (data plane) is the public surface: gameplay WS, config catalog, bundle upload,
# a caller's own session data. It binds 0.0.0.0 and sits behind the Apache proxy.
# `admin_app` (control plane) carries the privileged surface — minting keys and ACTIVATING
# uploaded code — and binds 127.0.0.1 only, so it is not reachable from the internet at all.
# This is enforced at the socket, not by proxy rules: a missing/incorrect Apache rule can
# no longer expose admin (fail-closed instead of fail-open), and admin routes never appear
# in the public app's schema. Reach it with an SSH tunnel:
#     ssh -L 9877:127.0.0.1:9877 <host>   then hit http://localhost:9877/admin/...
# Paths are unchanged (/admin/...) so existing scripts only need a different base URL.
app = FastAPI(**_DOCS_KW)
admin_app = FastAPI(**_DOCS_KW)


class AgentService:
    """Process-wide service state: API keys, config catalog, live sessions."""

    def __init__(self, keys: Dict[str, dict], config_dir: Path, log_dir: Path):
        self.keys = keys                      # api_key -> {label, ...}
        self.config_dir = config_dir
        self.log_dir = log_dir
        # Contributor bundle uploads land here, namespaced per key-label. A SUBdir of
        # config_dir, so the top-level ``*.json`` catalog glob never picks them up as
        # maintained configs; list_configs/resolve_config include them explicitly.
        self.uploads_dir = config_dir / "_uploads"
        self.uploads_dir.mkdir(parents=True, exist_ok=True)
        # Hashed, mintable cohort/participant keys (additive to the static `keys` map above).
        self.key_store = key_store.default_store()
        self.sessions: Dict[str, Session] = {}  # session_id -> Session
        self.started_at = datetime.now(timezone.utc)

    def sessions_by_label(self) -> Dict[str, int]:
        """Live session count grouped by API-key label (for monitoring)."""
        counts: Dict[str, int] = {}
        for s in self.sessions.values():
            counts[s.api_key_label] = counts.get(s.api_key_label, 0) + 1
        return counts

    def resolve_key(self, presented: Optional[str]) -> Optional[dict]:
        """Normalize a presented key to {label, configs(set|None), caps(frozenset), role, source}.
        Checks the static keys.json map first (trusted maintainer/lab keys), then the hashed
        mintable store. Returns None if unknown/revoked/expired."""
        if not presented:
            return None
        meta = self.keys.get(presented)
        if meta is not None:
            cfgs = meta.get("configs")
            return {"label": meta.get("label"),
                    "configs": set(cfgs) if cfgs else None,
                    "caps": frozenset(meta.get("caps") or []),
                    "role": meta.get("role", "static"), "source": "static"}
        info = self.key_store.verify(presented)
        if info is not None:
            info = dict(info)
            info["configs"] = set(info["configs"]) if info["configs"] else None
            return info
        return None

    def key_known(self, presented: Optional[str]) -> bool:
        return self.resolve_key(presented) is not None

    def caps_for(self, presented: Optional[str]) -> frozenset:
        info = self.resolve_key(presented)
        return info["caps"] if info else frozenset()

    def label_for(self, api_key: str) -> Optional[str]:
        info = self.resolve_key(api_key)
        return info["label"] if info else None

    def allowed_configs_for(self, api_key: str) -> Optional[set]:
        """Configs this key may use. None = unrestricted (all configs)."""
        info = self.resolve_key(api_key)
        return info["configs"] if info else None

    def list_configs(self, include_uploads_for: Optional[str] = None,
                     include_names: Optional[List[str]] = None) -> List[dict]:
        """Return public-facing config descriptors derived from filesystem.

        Maintained configs (top-level config_dir) are shown to everyone. Uploaded configs
        are private to their owner: pass ``include_uploads_for=<key label>`` to also list
        that label's own uploads (files named ``<safe_label>__*.json``).

        ``include_names`` additionally lists uploads GRANTED to a key that live in someone
        ELSE's namespace — the study case: a collaborator uploads ``lab__cfg`` and mints
        participant keys scoped to it. Those participants have a different label, so the
        glob above never finds the file, and without this they could hello into a config the
        launcher never listed — access and discovery would disagree."""
        out: List[dict] = []
        paths = sorted(self.config_dir.glob("*.json"))
        if include_uploads_for:
            safe = re.sub(r"[^A-Za-z0-9_-]+", "_", include_uploads_for)
            paths = paths + sorted(self.uploads_dir.glob(f"{safe}__*.json"))
        if include_names:
            seen = set(paths)
            for name in include_names:
                # Sanitize: a granted name is only ever a bare stem in uploads_dir.
                stem = re.sub(r"[^A-Za-z0-9_.-]+", "_", str(name))
                p = (self.uploads_dir / f"{stem}.json").resolve()
                if p.parent != self.uploads_dir.resolve() or not p.exists() or p in seen:
                    continue
                paths.append(p)
                seen.add(p)
        for path in paths:
            if path.name.startswith("keys"):
                continue  # skip the keys file even if it lives in config_dir
            try:
                cfg = load_config(str(path))
            except Exception as e:
                print(f"[router] Skipping unloadable config {path.name}: {e}")
                continue
            agents = [
                {"name": a.subagent_name, "role": a.role, "actor_type": a.actor_type}
                for a in cfg.agents
            ]
            # Optional human-facing title for the client dropdown; falls back
            # to the filename stem. Read raw so configs need no schema change.
            title = path.stem
            try:
                with open(path) as f:
                    raw = json.load(f)
                title = raw.get("title") or raw.get("display_name") or path.stem
            except Exception:
                pass
            out.append({"name": path.stem, "title": title,
                        "path": path.name, "agents": agents,
                        "uploaded": path.parent == self.uploads_dir})
        return out

    def resolve_config(self, name: str) -> Optional[Path]:
        """Map a config name (without .json) to its path under config_dir or uploads_dir."""
        candidate = self.config_dir / f"{name}.json"
        if candidate.exists():
            return candidate
        # Uploaded configs (namespaced <label>__<slug>).
        up = self.uploads_dir / f"{name}.json"
        if up.exists() and up.parent.resolve() == self.uploads_dir.resolve():
            return up
        # Allow callers to pass an explicit relative or absolute path too.
        as_path = Path(name)
        if as_path.is_absolute() and as_path.exists():
            return as_path
        return None

    def store_upload(self, key_label: str, bundle_name: str, cfg: dict) -> str:
        """Persist a validated uploaded config under the uploader's namespace and return its
        public config name (``<safe_label>__<slug>``). Path-containment checked."""
        safe_label = re.sub(r"[^A-Za-z0-9_-]+", "_", key_label or "anon")
        slug = re.sub(r"[^A-Za-z0-9_.-]+", "_", bundle_name.split("/")[-1]) or "bundle"
        public = f"{safe_label}__{slug}"
        path = (self.uploads_dir / f"{public}.json").resolve()
        if path.parent != self.uploads_dir.resolve():
            raise ValueError("upload path escaped uploads_dir")
        path.write_text(json.dumps(cfg, indent=2, ensure_ascii=False) + "\n")
        return public

    def grant_config(self, api_key: str, name: str) -> None:
        """Add ``name`` to a key's config allowlist if it is restricted (unrestricted keys
        already see everything). Keeps an uploader able to select what they just uploaded."""
        meta = self.keys.get(api_key)
        if meta is None:
            return
        cfgs = meta.get("configs")
        if cfgs and name not in cfgs:
            cfgs.append(name)

    def user_dir(self, key_label: str) -> Path:
        """Per-user log directory: logs are grouped by API-key label so each
        user's games live together (logs/sessions/<label>/)."""
        safe_label = re.sub(r"[^A-Za-z0-9_-]+", "_", key_label or "anon")
        d = self.log_dir / safe_label
        d.mkdir(parents=True, exist_ok=True)
        return d

    def log_path_for(self, session_id: str, key_label: str) -> str:
        ts = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        return str(self.user_dir(key_label) / f"session_{ts}_{session_id[:8]}.jsonl")

    def record_session(self, key_label: str, key_fp: str, session_id: str,
                       config_name: str, log_file: str,
                       player_id: Optional[str] = None) -> None:
        """Append a one-line manifest entry to the user's session index so
        every game a user plays is catalogued (id, config, log file, time).
        player_id is the client's persistent localStorage UUID (may be None)."""
        entry = {
            "session_id": session_id,
            "label": key_label,
            "key_fingerprint": key_fp,
            "player_id": player_id,
            "config": config_name,
            "log_file": Path(log_file).name,
            "started_at": _now(),
        }
        index = self.user_dir(key_label) / "_sessions_index.jsonl"
        with open(index, "a") as f:
            f.write(json.dumps(entry) + "\n")


service: Optional[AgentService] = None


# JSON has no comment syntax, so `_comment` / `_note` keys are the near-universal convention
# for annotating a config file. In a KEY map that convention is dangerous: every top-level
# entry is a credential, so a comment silently becomes a working API key whose label is the
# comment text. Found live on the Talos deployment 2026-08-13 — `Authorization: Bearer _comment`
# returned 200 with config_scope "all" and upload rights, and `_comment` is guessable by anyone
# who has seen a JSON config. Underscore-prefixed entries are therefore ignored, never
# credentials. Also drops non-string/empty keys, which cannot be presented as a bearer token
# anyway but would otherwise sit in the map looking valid.
def _keys_from_mapping(data: dict) -> dict:
    """Normalize a raw key map, dropping annotation entries (see comment above)."""
    out = {}
    for k, v in (data or {}).items():
        if not isinstance(k, str) or not k.strip():
            continue
        if k.startswith("_"):
            print(f"[router] keys file: ignoring annotation entry {k!r} (not a credential)")
            continue
        out[k] = v if isinstance(v, dict) else {"label": str(v)}
    return out


def _load_keys(path: Optional[Path]) -> Dict[str, dict]:
    """Load API keys from a JSON file, env var, or fall back to a dev key.

    JSON file format::

        { "ck_abc...": {"label": "Conner"}, "ck_xyz...": {"label": "Erin"} }

    Env var ``ARC_API_KEYS`` accepts either a JSON object of the same shape,
    or a comma-separated list (each key gets a generic label).
    """
    if path is not None:
        with open(path, "r") as f:
            data = json.load(f)
        return _keys_from_mapping(data)

    env = os.environ.get("ARC_API_KEYS")
    if env:
        try:
            data = json.loads(env)
            if isinstance(data, dict):
                # Same sanitizer as the file path — an annotation entry in ARC_API_KEYS
                # would otherwise become a credential exactly as it did in keys.json.
                return _keys_from_mapping(data)
        except json.JSONDecodeError:
            pass
        out: Dict[str, dict] = {}
        for i, key in enumerate(s.strip() for s in env.split(",") if s.strip()):
            out[key] = {"label": f"user{i+1}"}
        return out

    # Dev fallback for local testing.
    dev_key = "dev-local-key"
    print(f"[router] No --keys-file or ARC_API_KEYS env; accepting dev key '{dev_key}'")
    # Local dev key is an admin: it can mint cohort keys and upload plugin code.
    # `play_tester` is included so the unrestricted dev key exercises the play-tester
    # controls locally; a real cohort key only gets it if minted with it.
    return {dev_key: {"label": "dev",
                      "caps": ["mint", "upload_code", "play_tester", "dev_panel"]}}


# Configs of the retired auto / choices / coach actors (deleted 2026-10) and what a client asking
# for one gets instead: the public demo config Talos serves.
RETIRED_CONFIGS = {"openai_multi_agent_config_local", "openai_multi_agent_config", "single_agent_config",
                   "claude_multi_agent_config", "openai_choices_only_local", "agents_config.example"}
DEFAULT_CONFIG = "continuous_all_officers_anthropic"


def _bearer_to_key(auth: Optional[str]) -> Optional[str]:
    """Pull the key out of an ``Authorization: Bearer <key>`` header."""
    if not auth:
        return None
    parts = auth.strip().split(None, 1)
    if len(parts) == 2 and parts[0].lower() == "bearer":
        return parts[1].strip()
    return None



@app.get("/health")
async def health():
    if service is None:
        return {"status": "starting", "live_sessions": 0, "version": "2.0"}
    uptime = (datetime.now(timezone.utc) - service.started_at).total_seconds()
    return {
        "status": "healthy",
        "live_sessions": len(service.sessions),
        "sessions_by_label": service.sessions_by_label(),
        "uptime_seconds": round(uptime, 1),
        "configs_available": len(service.list_configs()),
        "version": "2.0",
    }


@app.get("/whoami")
async def whoami(authorization: Optional[str] = Header(default=None)):
    """What this key is and what it may do — the self-diagnosis endpoint.

    Without it a collaborator whose key is wrong, expired, or missing a capability only finds
    out as a 401/403 partway through some other call, and cannot tell "bad key" from "valid key,
    wrong capability". `cora.py doctor` reads this to answer both in one shot. Returns no secret:
    the key itself is never echoed, only its label, capabilities and config scope.
    """
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    info = service.resolve_key(_bearer_to_key(authorization))
    if info is None:
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    scoped = info.get("configs")
    return {
        "label": info.get("label"),
        "role": info.get("role"),
        "source": info.get("source"),          # static (keys.json) vs minted (hashed store)
        "capabilities": sorted(info.get("caps") or []),
        "config_scope": sorted(scoped) if scoped else "all",
        # Mirrors the ACTUAL gates: POST /bundles accepts any valid key (key_known), while
        # POST /plugins and the admin mint route are capability-gated. Reported rather than
        # inferred so `doctor` never tells a collaborator they can do something they can't.
        "can_upload_configs": True,
        "can_upload_code": "upload_code" in (info.get("caps") or ()),
        "can_mint_keys": "mint" in (info.get("caps") or ()),
        "can_use_dev_panel": "dev_panel" in (info.get("caps") or ()),
    }


@app.get("/configs")
async def list_configs(authorization: Optional[str] = Header(default=None)):
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    key = _bearer_to_key(authorization)
    if key is None or not service.key_known(key):
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    allowed = service.allowed_configs_for(key)
    # Pass `allowed` through so a config GRANTED to this key is listed even when it was
    # uploaded under another label (collaborator uploads → participant keys scoped to it).
    configs = service.list_configs(include_uploads_for=service.label_for(key),
                                   include_names=allowed)
    if allowed is not None:
        configs = [c for c in configs if c["name"] in allowed]
    return {"configs": configs}


# Cap uploaded bundle size (a config bundle is small JSON; this blocks JSON-bomb DoS).
_MAX_BUNDLE_BYTES = 256 * 1024


@app.post("/bundles")
async def upload_bundle(request: Request,
                        authorization: Optional[str] = Header(default=None),
                        base: Optional[str] = None):
    """Upload a contributor config bundle. Auth via ``Authorization: Bearer <key>``.

    Security posture (see docs/contributor-platform-design.md): namespace is derived from the
    TOKEN's label (never the body); body size is capped; the bundle is validated by cora_schema
    (``extra='forbid'`` + provider-enum, so no endpoint/secret can be smuggled in); it is stored
    in the uploader's private namespace and granted only to the uploading key. No code executes.

    For a *delta* bundle, pass ``?base=<config name>`` naming a config to layer onto.
    """
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    key = _bearer_to_key(authorization)
    if key is None or not service.key_known(key):
        raise HTTPException(status_code=401, detail="Invalid or missing API key")

    raw = await request.body()
    if len(raw) > _MAX_BUNDLE_BYTES:
        raise HTTPException(status_code=413, detail="bundle too large")
    try:
        payload = json.loads(raw)
    except (json.JSONDecodeError, UnicodeDecodeError):
        raise HTTPException(status_code=400, detail="body is not valid JSON")

    base_config = None
    if base is not None:
        bp = service.resolve_config(base)
        if bp is None:
            raise HTTPException(status_code=400, detail=f"unknown base config '{base}'")
        base_config = str(bp)

    try:
        cfg = load_bundle(payload, base_config=base_config)
    except BundleError as e:
        raise HTTPException(status_code=422, detail=str(e))

    # The Pydantic gate and the RUNTIME invariants check different things, and a config can
    # pass the first while being unusable under the second — two officers sharing a
    # talkinghead slot is the canonical case: CoraConfig accepts it, RouterConfig raises. That
    # combination meant such a bundle uploaded 200 and then broke the session of whoever
    # selected it. Enforce both here so an unusable config is refused at upload, not at play.
    try:
        router_config.config_from_dict(cfg)
    except Exception as e:
        raise HTTPException(status_code=422,
                            detail=f"config is schema-valid but not runnable: {e}")

    try:
        manifest_name = payload["manifest"]["name"]
    except (KeyError, TypeError):
        raise HTTPException(status_code=422, detail="bundle missing manifest.name")

    label = service.label_for(key) or "anon"
    name = service.store_upload(label, manifest_name, cfg)
    service.grant_config(key, name)
    warnings = _bundle_warnings(cfg)
    if warnings:
        print(f"[router] upload '{name}' stored with {len(warnings)} warning(s): {warnings}")
    return {"status": "ok", "name": name, "warnings": warnings,
            "message": f"stored as config '{name}'; select it in the hello frame to play"}


def _bundle_warnings(cfg: dict) -> List[str]:
    """Authoring warnings for an uploaded bundle — delegates to the shared implementation in
    bundle.py so the CLI (`cora-bundle validate`) and this endpoint report the SAME problems.
    Previously this logic lived only here, so validating locally gave a clean "OK" for a config
    that could not work in the UI."""
    return bundles.config_warnings(cfg)


def _require_cap(authorization: Optional[str], cap: str) -> dict:
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    info = service.resolve_key(_bearer_to_key(authorization))
    if info is None:
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    if cap not in info["caps"]:
        raise HTTPException(status_code=403, detail=f"requires the '{cap}' capability")
    return info


@admin_app.post("/admin/keys")
async def mint_keys(request: Request, authorization: Optional[str] = Header(default=None)):
    """Mint scoped cohort/participant keys (requires the 'mint' capability). Raw keys are returned
    ONCE — only the prefix + SHA-256 hash persist."""
    admin = _require_cap(authorization, "mint")
    body = await request.json()
    count = int(body.get("count") or 1)
    if not (1 <= count <= 500):
        raise HTTPException(status_code=400, detail="count must be 1..500")
    role = body.get("role") or "cohort"
    if role not in ("cohort", "admin"):
        raise HTTPException(status_code=400, detail="role must be 'cohort' or 'admin'")
    tokens = [service.key_store.mint(
        role=role, cohort=body.get("cohort"), configs=body.get("configs"),
        caps=body.get("caps") or [], quota=body.get("quota"),
        expires_days=body.get("expires_days"),
        created_by=admin.get("label") or "admin") for _ in range(count)]
    return {"cohort": body.get("cohort"), "count": len(tokens), "keys": tokens,
            "note": "store these now — they are not retrievable later"}


@admin_app.get("/admin/keys")
async def list_keys_admin(cohort: Optional[str] = None,
                          authorization: Optional[str] = Header(default=None)):
    """List keys + usage for auditing (requires 'mint'). Never returns secrets, only prefixes."""
    _require_cap(authorization, "mint")
    return {"keys": service.key_store.list_keys(cohort),
            "usage": service.key_store.usage_summary(cohort)}


@admin_app.post("/admin/keys/revoke")
async def revoke_key_admin(request: Request, authorization: Optional[str] = Header(default=None)):
    """Immediately revoke a key by prefix (requires 'mint')."""
    _require_cap(authorization, "mint")
    body = await request.json()
    prefix = str(body.get("prefix") or "")
    if not service.key_store.revoke(prefix):
        raise HTTPException(status_code=404, detail="unknown or already-revoked prefix")
    return {"status": "revoked", "prefix": prefix}


_MAX_PLUGIN_BYTES = 128 * 1024
# Staged uploads land here and are NOT imported until an admin activates them via
# /admin/plugins/reload. Kept beside plugins/ (not inside it) so a stage can never be
# picked up by the startup load.
_PLUGINS_STAGED_DIR = Path(__file__).resolve().parents[1] / "plugins_staged"
# Constructs worth a human's eyes before activation. This is NOT a sandbox and does not
# make untrusted code safe — once activated, a plugin runs in-process with full router
# privileges. The real controls are the upload_code capability + the manual activation
# gate; this scan just tells the reviewer where to look.
_PLUGIN_AUDIT_IMPORTS = {"subprocess", "socket", "shutil", "ctypes", "pickle", "marshal",
                         "importlib", "pty", "multiprocessing", "urllib", "requests", "httpx"}
_PLUGIN_AUDIT_CALLS = {"eval", "exec", "compile", "__import__", "open"}


@app.post("/plugins")
async def upload_plugin(request: Request,
                        name: Optional[str] = None,
                        authorization: Optional[str] = Header(default=None)):
    """Stage a contributor plugin (a cora_ext tool/hook module) for review. Body is the raw
    UTF-8 Python source; name it with ``?name=<slug>``. Requires the 'upload_code' capability.

    SECURITY POSTURE (deliberate, staged-manual): the module is validated and written to
    plugins_staged/<label>__<slug>.py but is **NOT imported and NOT activated**. Uploaded
    Python executes with full router privileges once loaded, so activation is a separate,
    explicit admin action (POST /admin/plugins/reload). The AST findings returned here are
    advisory input to that human review — not a sandbox.
    """
    import ast
    info = _require_cap(authorization, "upload_code")
    raw = await request.body()
    if not raw:
        raise HTTPException(status_code=400, detail="empty body: POST the plugin source as the body")
    if len(raw) > _MAX_PLUGIN_BYTES:
        raise HTTPException(status_code=413, detail=f"plugin too large (max {_MAX_PLUGIN_BYTES} bytes)")
    try:
        source = raw.decode("utf-8")
    except UnicodeDecodeError:
        raise HTTPException(status_code=400, detail="plugin must be UTF-8 text")

    # Namespace by the TOKEN's label (never the body/query), same rule as bundle uploads.
    label = info.get("label") or "anon"
    safe_label = re.sub(r"[^A-Za-z0-9_-]+", "_", label) or "anon"
    slug = re.sub(r"[^A-Za-z0-9_]+", "_", (name or "plugin").strip()).strip("_") or "plugin"
    stem = f"{safe_label}__{slug}"

    # 1) Must parse. A syntax error is a hard reject — it could never import anyway.
    try:
        tree = ast.parse(source, filename=f"{stem}.py")
    except SyntaxError as e:
        raise HTTPException(status_code=422, detail=f"syntax error line {e.lineno}: {e.msg}")

    # 2) Advisory scan: risky constructs + whether it registers anything at all.
    findings: list = []
    registers = False
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for a in node.names:
                root = a.name.split(".")[0]
                if root in _PLUGIN_AUDIT_IMPORTS:
                    findings.append(f"line {node.lineno}: imports '{a.name}'")
        elif isinstance(node, ast.ImportFrom):
            root = (node.module or "").split(".")[0]
            if root in _PLUGIN_AUDIT_IMPORTS:
                findings.append(f"line {node.lineno}: from '{node.module}' import ...")
        elif isinstance(node, ast.Call):
            fn = node.func
            fname = getattr(fn, "id", None) or getattr(fn, "attr", None)
            if fname in _PLUGIN_AUDIT_CALLS:
                findings.append(f"line {node.lineno}: calls {fname}()")
            if fname in ("register_tool", "register_hook", "register_loop"):
                registers = True
    if not registers:
        findings.append("no register_tool/register_hook call found — this may not be a CORA plugin")

    _PLUGINS_STAGED_DIR.mkdir(parents=True, exist_ok=True)
    path = (_PLUGINS_STAGED_DIR / f"{stem}.py").resolve()
    if path.parent != _PLUGINS_STAGED_DIR.resolve():
        raise HTTPException(status_code=400, detail="staged path escaped plugins_staged/")
    path.write_text(source)
    print(f"[router] plugin STAGED (not active): {path.name} by '{label}' "
          f"({len(raw)} bytes, {len(findings)} review note(s))")
    return {
        "status": "staged",
        "name": path.name,
        "bytes": len(raw),
        "active": False,
        "review_notes": findings,
        "message": ("Staged for review — NOT active. An admin must activate it with "
                    "POST /admin/plugins/reload (this imports and runs the module)."),
    }


@app.get("/plugins")
async def list_plugins(authorization: Optional[str] = Header(default=None)):
    """List staged (inactive) and active plugin modules. Requires 'upload_code'."""
    _require_cap(authorization, "upload_code")
    staged = sorted(p.name for p in _PLUGINS_STAGED_DIR.glob("*.py")) \
        if _PLUGINS_STAGED_DIR.exists() else []
    return {"staged_inactive": staged,
            "active_tools": list(plugin_api.all_tools()),
            "active_hooks": {e: len(plugin_api.get_hooks(e)) for e in plugin_api.HOOK_EVENTS
                             if plugin_api.get_hooks(e)},
            "load_errors": plugin_api.load_errors()}


@admin_app.post("/admin/plugins/reload")
async def reload_plugins(authorization: Optional[str] = Header(default=None)):
    """Hot-reload plugins WITHOUT a router restart: clear the plugin registry and re-import every
    module under plugins/ AND plugins_staged/ (edits + new files picked up; deleted files dropped).
    Requires the 'upload_code' capability. This is the ACTIVATION step for anything uploaded via
    POST /plugins — it imports and executes that code, so review the staged file first. Prefer to
    run between games — a tool/hook call arriving during the brief reload window degrades to a tool
    error (exception isolation), never a crash. A plugin with a syntax/import error is skipped and
    reported, not fatal."""
    _require_cap(authorization, "upload_code")
    plugin_api.clear_registry()
    loaded = plugin_api.load_plugins(["plugins", str(_PLUGINS_STAGED_DIR)])
    return {"reloaded_modules": loaded,
            "load_errors": plugin_api.load_errors(),          # files that failed to import
            "tools": list(plugin_api.all_tools()),
            "hooks": {e: len(plugin_api.get_hooks(e)) for e in plugin_api.HOOK_EVENTS
                      if plugin_api.get_hooks(e)}}


@admin_app.get("/admin/plugins/errors")
async def plugin_errors(limit: int = 50, authorization: Optional[str] = Header(default=None)):
    """Recent plugin diagnostics (requires 'upload_code'): load-time import failures + a ring
    buffer of the most recent tool/hook runtime exceptions, each with a full traceback."""
    _require_cap(authorization, "upload_code")
    return {"load_errors": plugin_api.load_errors(),
            "runtime_errors": plugin_api.recent_errors(limit)}


@app.get("/my/sessions")
async def my_sessions(authorization: Optional[str] = Header(default=None)):
    """List the caller's OWN sessions (from their per-label session index). The label — and thus
    the namespace — is derived from the token, never from a parameter, so one key can only ever
    see its own cohort's data."""
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    key = _bearer_to_key(authorization)
    if key is None or not service.key_known(key):
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    label = service.label_for(key) or "anon"
    index = service.user_dir(label) / "_sessions_index.jsonl"
    sessions = []
    if index.exists():
        for line in index.read_text().splitlines():
            line = line.strip()
            if line:
                try:
                    sessions.append(json.loads(line))
                except json.JSONDecodeError:
                    pass
    return {"label": label, "count": len(sessions), "sessions": sessions}


@app.get("/my/sessions/export")
async def my_sessions_export(format: str = "ndjson", config: Optional[str] = None,
                             limit: int = 0,
                             authorization: Optional[str] = Header(default=None)):
    """Bulk-download ALL of the caller's own session logs in one request — the corpus step for
    fine-tuning. Scoped to the token's label exactly like /my/sessions, so a key can only ever
    export its own cohort's data.

    NOTE: this route MUST stay declared above /my/sessions/{session_id}; otherwise FastAPI
    matches "export" as a session_id and this endpoint becomes unreachable.

    format=ndjson (default): every event of every session concatenated, newline-delimited. Each
      line already carries session_id/episode_id, so the stream stays attributable after merging.
    format=tar: a .tar.gz of the individual session .jsonl files (one member per session).
    config=<name>: only sessions played on that config. limit=N: most recent N sessions.
    """
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    key = _bearer_to_key(authorization)
    if key is None or not service.key_known(key):
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    if format not in ("ndjson", "tar"):
        raise HTTPException(status_code=400, detail="format must be 'ndjson' or 'tar'")

    label = service.label_for(key) or "anon"
    udir = service.user_dir(label).resolve()
    index = udir / "_sessions_index.jsonl"
    entries: list = []
    if index.exists():
        for line in index.read_text().splitlines():
            line = line.strip()
            if not line:
                continue
            try:
                e = json.loads(line)
            except json.JSONDecodeError:
                continue
            if config and e.get("config") != config:
                continue
            entries.append(e)
    if limit and limit > 0:
        entries = entries[-limit:]

    # Resolve to real, containment-checked files (skip index rows whose log is gone).
    files: list = []
    for e in entries:
        name = e.get("log_file")
        if not name:
            continue
        p = (udir / name).resolve()
        if p.parent == udir and p.exists():
            files.append(p)

    stamp = _now().replace(":", "").replace("-", "")[:15]
    if format == "tar":
        import io, tarfile
        buf = io.BytesIO()
        with tarfile.open(fileobj=buf, mode="w:gz") as tf:
            for p in files:
                tf.add(str(p), arcname=f"{label}/{p.name}")
        data = buf.getvalue()
        fn = f"cora_sessions_{label}_{stamp}.tar.gz"
        print(f"[router] export: {len(files)} session(s) for '{label}' as tar ({len(data)} bytes)")
        return Response(content=data, media_type="application/gzip",
                        headers={"Content-Disposition": f'attachment; filename="{fn}"',
                                 "X-CORA-Sessions": str(len(files))})

    chunks = []
    for p in files:
        text = p.read_text()
        if text and not text.endswith("\n"):
            text += "\n"
        chunks.append(text)
    body = "".join(chunks)
    fn = f"cora_sessions_{label}_{stamp}.jsonl"
    print(f"[router] export: {len(files)} session(s) for '{label}' as ndjson ({len(body)} bytes)")
    return Response(content=body, media_type="application/x-ndjson",
                    headers={"Content-Disposition": f'attachment; filename="{fn}"',
                             "X-CORA-Sessions": str(len(files))})


@app.get("/my/sessions/{session_id}")
async def my_session_log(session_id: str, authorization: Optional[str] = Header(default=None)):
    """Download one of the caller's own session logs (NDJSON event stream). Scoped to the caller's
    label directory with a path-containment check."""
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    key = _bearer_to_key(authorization)
    if key is None or not service.key_known(key):
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    label = service.label_for(key) or "anon"
    udir = service.user_dir(label).resolve()
    index = udir / "_sessions_index.jsonl"
    log_name = None
    if index.exists():
        for line in index.read_text().splitlines():
            try:
                e = json.loads(line)
            except json.JSONDecodeError:
                continue
            if e.get("session_id") == session_id:
                log_name = e.get("log_file")
                break
    if not log_name:
        raise HTTPException(status_code=404, detail="session not found for your key")
    path = (udir / log_name).resolve()
    if path.parent != udir or not path.exists():
        raise HTTPException(status_code=404, detail="log file not found")
    return Response(content=path.read_text(), media_type="application/x-ndjson",
                    headers={"Content-Disposition": f'attachment; filename="{log_name}"'})


async def _handshake(websocket: WebSocket) -> Optional[Session]:
    """Accept a WebSocket, perform the hello handshake, return a Session.

    On any handshake failure the WebSocket is closed and ``None`` is returned.
    """
    await websocket.accept()
    if service is None:
        await websocket.send_text(json.dumps({"type": "hello_error", "error": "service_not_ready"}))
        await websocket.close(code=1011, reason="Service not initialized")
        return None

    try:
        raw = await asyncio.wait_for(websocket.receive_text(), timeout=15.0)
    except asyncio.TimeoutError:
        await websocket.close(code=1008, reason="hello timeout")
        return None

    try:
        msg = json.loads(raw)
    except json.JSONDecodeError:
        await websocket.send_text(json.dumps({"type": "hello_error", "error": "bad_json"}))
        await websocket.close(code=1008, reason="hello must be JSON")
        return None

    if msg.get("type") != "hello":
        await websocket.send_text(json.dumps({
            "type": "hello_error",
            "error": "expected_hello",
            "got": msg.get("type"),
        }))
        await websocket.close(code=1008, reason="expected hello frame")
        return None

    api_key = msg.get("api_key")
    config_name = msg.get("config")
    if config_name in RETIRED_CONFIGS:
        # A client built before the legacy actors were retired (its scene default, or a saved
        # arc_config_name) still asks for one of their configs.
        print(f"[router] config {config_name!r} is retired; serving {DEFAULT_CONFIG!r}")
        config_name = DEFAULT_CONFIG
    # Optional client-supplied persistent player id (localStorage UUID). It is
    # UNTRUSTED input: sanitize to a bounded safe charset and only ever store it
    # as a log VALUE, never as a path component. Absent/blank -> None (anonymous).
    raw_pid = msg.get("player_id")
    player_id = None
    if isinstance(raw_pid, str):
        player_id = re.sub(r"[^A-Za-z0-9_-]", "", raw_pid)[:64] or None
    # Map provenance the client reports (see the session_start emit below). UNTRUSTED like
    # player_id: bounded and only ever stored as a log VALUE, never used as a path or to
    # fetch anything. "" when an older client doesn't send it.
    def _clip(v, n=300):
        return v[:n] if isinstance(v, str) else ""
    map_url = _clip(msg.get("map_url"))
    map_hash = _clip(msg.get("map_hash"), 32)
    map_status = _clip(msg.get("map_status"), 24)
    if not api_key or not service.key_known(api_key):
        await websocket.send_text(json.dumps({"type": "hello_error", "error": "invalid_api_key"}))
        await websocket.close(code=1008, reason="invalid api key")
        return None
    if not config_name:
        await websocket.send_text(json.dumps({"type": "hello_error", "error": "missing_config"}))
        await websocket.close(code=1008, reason="missing config")
        return None

    allowed = service.allowed_configs_for(api_key)
    if allowed is not None and config_name not in allowed:
        await websocket.send_text(json.dumps({
            "type": "hello_error",
            "error": "config_not_allowed",
            "config": config_name,
        }))
        await websocket.close(code=1008, reason="config not allowed for this key")
        return None

    config_path = service.resolve_config(config_name)
    if config_path is None:
        await websocket.send_text(json.dumps({
            "type": "hello_error",
            "error": "unknown_config",
            "config": config_name,
        }))
        await websocket.close(code=1008, reason="unknown config")
        return None

    try:
        cfg = load_config(str(config_path))
    except Exception as e:
        await websocket.send_text(json.dumps({
            "type": "hello_error",
            "error": "config_load_failed",
            "detail": str(e),
        }))
        await websocket.close(code=1011, reason="config load failed")
        return None

    session_id = str(uuid.uuid4())
    key_label = service.label_for(api_key) or "anon"
    log_path = service.log_path_for(session_id, key_label)
    session = Session(
        config=cfg,
        session_id=session_id,
        api_key_label=key_label,
        log_path=log_path,
        websocket=websocket,
    )
    session.player_id = player_id
    session.config_name = config_name      # for the developer panel's session list
    session.started_at = _now()
    service.sessions[session_id] = session

    # Catalogue this game under the user (per-key index) and stamp a
    # session_start header at the top of the session's own log.
    key_fp = hashlib.sha256(api_key.encode("utf-8")).hexdigest()[:12]
    service.record_session(key_label, key_fp, session_id, config_name, log_path,
                           player_id=player_id)
    session._emit("session_start", {
        "label": key_label,
        "key_fingerprint": key_fp,
        "player_id": player_id,
        "config": config_name,
        "agents": [a.subagent_name for a in cfg.agents],
        # Per-agent specs: which model and messaging rules each seat ran under. The name
        # list above is kept as-is for existing readers; this is the self-describing form.
        "agent_specs": [{
            "name": a.subagent_name,
            "role": a.role,
            "actor_type": a.actor_type,
            "provider": getattr(a, "provider", None),
            "llm_model": getattr(a, "llm_model", None),
            "opening_mode": getattr(a, "opening_mode", None),
            "can_address": list(getattr(a, "can_address", None) or []),
            "max_steps": getattr(a, "max_steps", None),
        } for a in cfg.agents],
        "peer_trigger_budget": int(getattr(cfg, "peer_trigger_budget", None)
                                   or PEER_TRIGGER_BUDGET_PER_ROUND),
        # Map PROVENANCE, reported by the client. Maps are deliberately served outside the
        # router (a partner can expose a map derived from private data), so this is the only
        # record of which map a session actually ran on. Stamping it here keeps a merged
        # corpus self-describing — two map conditions stay separable at training time, and a
        # silent fallback to the default layout (map_status != "loaded") is visible after the
        # fact. The router RECORDS these; it never serves or validates map content.
        "map": {"url": map_url, "hash": map_hash, "status": map_status},
        # The weights that turn Unity's rewardMetrics into `score`. Every front end (live, RL,
        # benchmark) uses cora.scoring, so scores ARE directly
        # comparable — but only under the SAME weights. Without this stamp, retuning a weight
        # silently makes old and new runs incomparable: the numbers still merge and parse,
        # they just quietly mean something different. Raw rewardMetrics are preserved per
        # turn regardless, so a corpus can always be re-scored under new weights.
        "reward_weights": REWARD_WEIGHTS,
    })

    await websocket.send_text(json.dumps({
        "type": "hello_ack",
        "session_id": session_id,
        "config": config_name,
        "agents": [a.subagent_name for a in cfg.agents],
        # Roster the client uses to label the (fixed 5) sidebar talking-head slots by
        # the config's real officer names, keyed by talkinghead_endpoint. Without this
        # the WebGL client has no local config and falls back to the enum slot names,
        # so e.g. a "Logistics Officer" (endpoint=WorkforceService) renders under the
        # "Workforce Service" tab and looks like it never landed.
        "officers": [{"name": a.subagent_name, "endpoint": a.talkinghead_endpoint}
                     for a in cfg.agents if a.talkinghead_endpoint],
        "label": key_label,
        # Capabilities of the presented key, so the CLIENT can gate its own controls.
        # This is a convenience for the UI, NOT a security boundary: anything that must
        # actually be enforced is enforced server-side on the request that does the work.
        # Used today by `play_tester`, which reveals Load .cora and Flag Interaction —
        # both are local-only actions (a file picker, a log line), so a client that lies
        # about its caps gains nothing it could not already do by editing its own save.
        "capabilities": sorted(service.caps_for(api_key)),
        "player_id": player_id,
    }))
    print(f"[router] hello_ack -> {key_label} (session {session_id[:8]}, "
          f"config={config_name}, agents={len(cfg.agents)})")
    return session


# ── Developer panel ──────────────────────────────────────────────
# A plain web page (devpanel/index.html) plus a small JSON API for editing officers' prompts
# in a LIVE game. Every API call needs a key with the `dev_panel` capability; study
# participants' keys never have it. The page itself is static and holds no data.
_DEVPANEL_DIR = Path(__file__).resolve().parents[1] / "devpanel"


def _require_dev_panel(authorization: Optional[str]) -> dict:
    if service is None:
        raise HTTPException(status_code=503, detail="Service not initialized")
    info = service.resolve_key(_bearer_to_key(authorization))
    if info is None:
        raise HTTPException(status_code=401, detail="Invalid or missing API key")
    if "dev_panel" not in (info.get("caps") or ()):
        raise HTTPException(status_code=403,
                            detail="This key lacks the 'dev_panel' capability")
    return info


def _dev_session(session_id: str) -> "Session":
    s = service.sessions.get(session_id)
    if s is None:
        raise HTTPException(status_code=404, detail="No live session with that id")
    return s


@app.get("/dev")
async def dev_panel_page():
    page = _DEVPANEL_DIR / "index.html"
    if not page.is_file():
        raise HTTPException(status_code=404, detail="devpanel/index.html not found")
    return Response(content=page.read_text(encoding="utf-8"), media_type="text/html")


@app.get("/dev/api/sessions")
async def dev_list_sessions(authorization: Optional[str] = Header(default=None)):
    """Live games on this router, newest first."""
    _require_dev_panel(authorization)
    rows = [{
        "session_id": s.session_id,
        "config": getattr(s, "config_name", None),
        "key_label": s.api_key_label,
        "started_at": getattr(s, "started_at", None),
        "round": s.round_num, "day": s.day,
        "officers": [a.subagent_name for a in s.config.agents
                     if a.actor_type == "continuous"],
    } for s in service.sessions.values()]
    rows.sort(key=lambda r: r["started_at"] or "", reverse=True)
    return {"sessions": rows}


@app.get("/dev/api/sessions/{session_id}")
async def dev_session_view(session_id: str,
                           authorization: Optional[str] = Header(default=None)):
    """Prompt layers, each officer's assembled prompt, and live officer status."""
    _require_dev_panel(authorization)
    return _dev_session(session_id).dev_prompt_view()


@app.post("/dev/api/sessions/{session_id}/prompt")
async def dev_set_prompt(session_id: str, request: Request,
                         authorization: Optional[str] = Header(default=None)):
    """Body: {"scope": "agent"|"global_behavior"|"global_manual"|"tool_policy",
              "agent": "<officer name, for scope=agent>", "text": "<new prompt>"}.
    Omit "text" (or send null) to reset that layer to the config's text."""
    info = _require_dev_panel(authorization)
    try:
        body = await request.json()
    except Exception:
        raise HTTPException(status_code=400, detail="Body must be JSON")
    text = body.get("text")
    if text is not None and not isinstance(text, str):
        raise HTTPException(status_code=400, detail="'text' must be a string or null")
    if text is not None and len(text) > 200_000:
        raise HTTPException(status_code=413, detail="Prompt text over 200,000 characters")
    try:
        change = _dev_session(session_id).dev_set_prompt(
            body.get("scope"), text, editor=info.get("label") or "unknown",
            agent_name=body.get("agent"))
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    return {"ok": True, "change": change}


@app.delete("/dev/api/sessions/{session_id}/autonomy/{rule_id}")
async def dev_remove_autonomy_rule(session_id: str, rule_id: str,
                                   authorization: Optional[str] = Header(default=None)):
    """Cancel a standing order from the developer panel. The officer's chat shows it."""
    _require_dev_panel(authorization)
    text = await _dev_session(session_id)._remove_autonomy_rule(None, rule_id, by="developer")
    if text.startswith("ERROR"):
        raise HTTPException(status_code=404, detail=text)
    return {"ok": True, "detail": text}


_MAX_CHECKPOINT_BYTES = 8 * 1024 * 1024


@app.post("/dev/api/sessions/{session_id}/load_checkpoint")
async def dev_load_checkpoint(session_id: str, request: Request,
                              authorization: Optional[str] = Header(default=None)):
    """Body: {"checkpoint": <the .cora JSON, as an object or a string>,
              "officer_memory": "fresh" (default) | "keep"}.
    Loads the checkpoint into the live game; "fresh" also clears the officers' transcripts."""
    info = _require_dev_panel(authorization)
    raw = await request.body()
    if len(raw) > _MAX_CHECKPOINT_BYTES:
        raise HTTPException(status_code=413, detail="Checkpoint over 8 MB")
    try:
        body = json.loads(raw)
    except ValueError:
        raise HTTPException(status_code=400, detail="Body must be JSON")
    cp = body.get("checkpoint")
    if isinstance(cp, dict):
        cp = json.dumps(cp)
    if not isinstance(cp, str) or not cp.strip():
        raise HTTPException(status_code=400, detail="'checkpoint' is required")
    session = _dev_session(session_id)
    try:
        result = await session.dev_load_checkpoint(
            cp, body.get("officer_memory") or "fresh", editor=info.get("label") or "unknown")
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    return {"ok": result["accepted"], "load": result}


@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    session = await _handshake(websocket)
    if session is None:
        return
    try:
        await session.run()
    finally:
        service.sessions.pop(session.session_id, None)


@app.websocket("/")
async def websocket_root_endpoint(websocket: WebSocket):
    """Alias path for clients that connect at the root."""
    session = await _handshake(websocket)
    if session is None:
        return
    try:
        await session.run()
    finally:
        service.sessions.pop(session.session_id, None)


def main():
    global service

    parser = argparse.ArgumentParser(description="ARC Game Multi-Agent Router (multi-tenant)")
    parser.add_argument("--config-dir", default="config",
                        help="Directory containing config JSON files to expose to clients")
    parser.add_argument("--keys-file", default=None,
                        help="JSON file mapping api_key -> {label}. "
                             "If omitted, reads ARC_API_KEYS env var; if that's also "
                             "absent, falls back to a single 'dev-local-key' for testing.")
    parser.add_argument("--log-dir", default="logs/sessions",
                        help="Directory for per-session episode log files")
    parser.add_argument("--port", type=int, default=9876,
                        help="Port to listen on for Unity connections")
    parser.add_argument("--admin-port", type=int, default=9877,
                        help="Port for the ADMIN (control-plane) app: key minting, plugin "
                             "activation, diagnostics. Bound to loopback by default — reach "
                             "it with: ssh -L 9877:127.0.0.1:9877 <host>")
    parser.add_argument("--admin-host", default="127.0.0.1",
                        help="Bind address for the admin app. Keep 127.0.0.1 in production; "
                             "binding it to 0.0.0.0 puts key minting and code activation on "
                             "the network and is almost never what you want.")
    parser.add_argument("--cors-origins", default="*",
                        help="Comma-separated origins allowed for browser (WebGL) "
                             "clients, or '*' for any. Only needed when the WebGL "
                             "page is served from a different origin than this "
                             "router; harmless behind a same-origin reverse proxy.")
    # Legacy single-config flag is no longer used; configs are chosen per-session
    # via the hello frame. Kept here only so old launch scripts don't fail hard.
    parser.add_argument("--config", default=None,
                        help=argparse.SUPPRESS)
    parser.add_argument("--log", default=None,
                        help=argparse.SUPPRESS)
    args = parser.parse_args()

    if args.config is not None:
        print(f"[router] NOTE: --config is ignored in multi-tenant mode "
              f"(clients pick a config via the hello frame).")
    if args.log is not None:
        print(f"[router] NOTE: --log is ignored; logs go to --log-dir as one file per session.")

    keys = _load_keys(Path(args.keys_file) if args.keys_file else None)
    service = AgentService(
        keys=keys,
        config_dir=Path(args.config_dir),
        log_dir=Path(args.log_dir),
    )

    print(f"[router] Starting service on port {args.port}")
    print(f"[router] Config catalog: {service.config_dir} "
          f"({len(service.list_configs())} configs visible)")
    plugin_store.default_store()
    key_store.default_store()
    print(f"[router] Plugin persist store: {plugin_store._DEFAULT_PATH} | "
          f"key store: {key_store._DEFAULT_PATH}")
    _loaded_plugins = plugin_api.load_plugins(["plugins"])
    if _loaded_plugins:
        print(f"[router] Loaded {len(_loaded_plugins)} plugin module(s): {_loaded_plugins} "
              f"| tools={list(plugin_api.all_tools())}")
    print(f"[router] Authorized keys: {[m.get('label') for m in keys.values()]}")
    print(f"[router] Session logs: {service.log_dir}")
    print(f"[router] Clients connect to ws://localhost:{args.port}/ws "
          f"and send a hello frame.")

    # CORS lets a browser-based (WebGL) client call /configs from another
    # origin. WebSockets aren't subject to CORS, so this mainly covers the
    # /configs + /health fetches. Auth is via Bearer header (not cookies),
    # so wildcard origins without credentials is safe.
    origins = (["*"] if args.cors_origins.strip() == "*"
               else [o.strip() for o in args.cors_origins.split(",") if o.strip()])
    app.add_middleware(
        CORSMiddleware,
        allow_origins=origins,
        allow_methods=["*"],
        allow_headers=["*"],
    )
    print(f"[router] CORS allow_origins = {origins}")

    # Serve BOTH planes: the public app on all interfaces (behind the proxy) and the admin
    # app on loopback only. Binding admin to 127.0.0.1 is the actual control — it cannot be
    # reached from off-box regardless of what the reverse proxy is configured to forward.
    async def _serve_both():
        public = uvicorn.Server(uvicorn.Config(
            app, host="0.0.0.0", port=args.port, log_level="info"))
        admin = uvicorn.Server(uvicorn.Config(
            admin_app, host=args.admin_host, port=args.admin_port, log_level="warning"))
        print(f"[router] data plane  : http://0.0.0.0:{args.port} (public, proxied)")
        print(f"[router] control plane: http://{args.admin_host}:{args.admin_port} "
              f"(admin — loopback only; reach via: ssh -L {args.admin_port}:127.0.0.1:{args.admin_port} <host>)")
        print(f"[router] API docs: {'ENABLED (CORA_DEV_DOCS)' if _DEV_DOCS else 'disabled'}")
        await asyncio.gather(public.serve(), admin.serve())

    asyncio.run(_serve_both())


if __name__ == "__main__":
    main()
