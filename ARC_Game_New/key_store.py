"""SQLite-backed API-key store: an admin key mints scoped **cohort** keys for participants.

Design (from the platform security research):
  * Opaque tokens `pk_<prefix8>_<secret>` (cohort) / `ak_<prefix8>_<secret>` (admin);
    `secret = secrets.token_urlsafe(32)` (256-bit). Shown ONCE at mint.
  * Stored **hashed** (`sha256` — the key already has 256 bits of entropy, so a slow hash like
    bcrypt buys nothing and caps input at 72 bytes). Lookup by the indexed `prefix`, then a
    constant-time `hmac.compare_digest` on the hash.
  * **Opaque-token + DB lookup, not JWT** → instant revocation (one-row update; no wait for exp).
  * Per-key: role, cohort (= log namespace/label), config allowlist, capabilities, quota, expiry.

This is ADDITIVE: the static `config/keys.json` map still works for trusted maintainer/lab keys;
AgentService.resolve_key checks that first, then this store.
"""
from __future__ import annotations

import hashlib
import hmac
import json
import secrets
import sqlite3
import threading
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Optional


def _sha256(s: str) -> str:
    return hashlib.sha256(s.encode("utf-8")).hexdigest()


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


class KeyStore:
    def __init__(self, db_path: "str | Path"):
        self._lock = threading.Lock()
        Path(db_path).parent.mkdir(parents=True, exist_ok=True)
        self._conn = sqlite3.connect(str(db_path), check_same_thread=False)
        self._conn.row_factory = sqlite3.Row
        self._conn.execute("PRAGMA journal_mode=WAL")
        self._conn.execute("PRAGMA synchronous=NORMAL")
        self._conn.executescript(
            """
            CREATE TABLE IF NOT EXISTS api_keys(
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              prefix TEXT UNIQUE NOT NULL,
              key_hash TEXT NOT NULL,
              role TEXT NOT NULL,                 -- 'cohort' | 'admin'
              cohort TEXT,
              config_allowlist TEXT,              -- JSON list, or NULL = all
              caps TEXT,                          -- JSON list of capability strings
              quota_total INTEGER,                -- NULL = unlimited
              quota_remaining INTEGER,
              created_by TEXT,
              created_at TEXT,
              expires_at TEXT,
              revoked_at TEXT,
              enabled INTEGER DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS ix_api_keys_prefix ON api_keys(prefix);
            CREATE TABLE IF NOT EXISTS usage_events(
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              prefix TEXT, cohort TEXT, ts TEXT, event TEXT, detail TEXT
            );
            """)
        self._conn.commit()

    # -- minting --------------------------------------------------------------
    def mint(self, *, role: str = "cohort", cohort: Optional[str] = None,
             configs: Optional[list] = None, caps: Optional[list] = None,
             quota: Optional[int] = None, expires_days: Optional[int] = None,
             created_by: str = "admin") -> str:
        """Create one key and return the raw token (shown once; only prefix+hash persist)."""
        prefix = secrets.token_hex(4)                     # 8 hex chars, the lookup index
        secret = secrets.token_urlsafe(32)
        token = f"{'ak' if role == 'admin' else 'pk'}_{prefix}_{secret}"
        expires_at = None
        if expires_days:
            expires_at = (datetime.now(timezone.utc) + timedelta(days=expires_days)).isoformat()
        with self._lock:
            self._conn.execute(
                "INSERT INTO api_keys(prefix,key_hash,role,cohort,config_allowlist,caps,"
                "quota_total,quota_remaining,created_by,created_at,expires_at,enabled) "
                "VALUES(?,?,?,?,?,?,?,?,?,?,?,1)",
                (prefix, _sha256(token), role, cohort,
                 json.dumps(configs) if configs else None, json.dumps(caps or []),
                 quota, quota, created_by, _now_iso(), expires_at))
            self._conn.commit()
        return token

    # -- verification ---------------------------------------------------------
    def _row(self, presented: str):
        parts = presented.split("_", 2)
        if len(parts) != 3 or parts[0] not in ("pk", "ak"):
            return None
        cur = self._conn.execute("SELECT * FROM api_keys WHERE prefix = ?", (parts[1],))
        return cur.fetchone()

    def verify(self, presented: str) -> Optional[dict]:
        """Return normalized key info if the token is valid/active, else None."""
        row = self._row(presented)
        if row is None:
            return None
        if not hmac.compare_digest(_sha256(presented), row["key_hash"]):
            return None
        if not row["enabled"] or row["revoked_at"]:
            return None
        if row["expires_at"] and row["expires_at"] < _now_iso():
            return None
        return {
            "label": row["cohort"] or f"key-{row['prefix']}",
            "configs": json.loads(row["config_allowlist"]) if row["config_allowlist"] else None,
            "caps": frozenset(json.loads(row["caps"] or "[]")),
            "role": row["role"],
            "prefix": row["prefix"],
            "source": "minted",
        }

    # -- lifecycle / admin ----------------------------------------------------
    def revoke(self, prefix: str) -> bool:
        with self._lock:
            cur = self._conn.execute(
                "UPDATE api_keys SET revoked_at = ?, enabled = 0 "
                "WHERE prefix = ? AND revoked_at IS NULL", (_now_iso(), prefix))
            self._conn.commit()
            return cur.rowcount > 0

    def list_keys(self, cohort: Optional[str] = None) -> list[dict]:
        q = ("SELECT prefix,role,cohort,config_allowlist,caps,quota_total,quota_remaining,"
             "created_at,expires_at,revoked_at,enabled FROM api_keys")
        args: tuple = ()
        if cohort:
            q += " WHERE cohort = ?"
            args = (cohort,)
        q += " ORDER BY created_at DESC"
        return [dict(r) for r in self._conn.execute(q, args).fetchall()]

    # -- metering -------------------------------------------------------------
    def try_consume_quota(self, prefix: str) -> bool:
        """Atomically decrement remaining quota; True if allowed (unlimited or >0)."""
        with self._lock:
            row = self._conn.execute(
                "SELECT quota_remaining FROM api_keys WHERE prefix = ?", (prefix,)).fetchone()
            if row is None:
                return False
            if row["quota_remaining"] is None:
                return True
            if row["quota_remaining"] <= 0:
                return False
            self._conn.execute(
                "UPDATE api_keys SET quota_remaining = quota_remaining - 1 WHERE prefix = ?",
                (prefix,))
            self._conn.commit()
            return True

    def record_usage(self, prefix: str, event: str, detail: Optional[dict] = None) -> None:
        row = self._conn.execute("SELECT cohort FROM api_keys WHERE prefix = ?", (prefix,)).fetchone()
        with self._lock:
            self._conn.execute(
                "INSERT INTO usage_events(prefix,cohort,ts,event,detail) VALUES(?,?,?,?,?)",
                (prefix, row["cohort"] if row else None, _now_iso(), event,
                 json.dumps(detail) if detail else None))
            self._conn.commit()

    def usage_summary(self, cohort: Optional[str] = None) -> list[dict]:
        q = "SELECT cohort, event, COUNT(*) n FROM usage_events"
        args: tuple = ()
        if cohort:
            q += " WHERE cohort = ?"
            args = (cohort,)
        q += " GROUP BY cohort, event"
        return [dict(r) for r in self._conn.execute(q, args).fetchall()]

    def close(self) -> None:
        with self._lock:
            self._conn.close()


_DEFAULT: Optional[KeyStore] = None
_DEFAULT_PATH = "data/keys.db"


def default_store(path: Optional[str] = None) -> KeyStore:
    global _DEFAULT
    if _DEFAULT is None:
        _DEFAULT = KeyStore(path or _DEFAULT_PATH)
    return _DEFAULT
