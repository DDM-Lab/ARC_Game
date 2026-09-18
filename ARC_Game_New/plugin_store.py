"""Durable, process-wide key/value store backing `ctx.persist` for CORA plugins.

Host-mediated storage (the game-modding standard: Factorio `global`, WoW SavedVariables): a plugin
sees only the `get`/`set`/`setdefault` KV interface; the host owns the backend. This is the ONE
store scope that survives across games AND across router restarts, so a plugin can accumulate
per-participant state (a Bayesian posterior) or a population prior over a whole study.

Design notes:
  * SQLite — the canonical single-node embedded DB; zero-ops, ACID, fine at tens–hundreds of users.
    Swap for Postgres/Redis later WITHOUT touching plugin code (same get/set/setdefault contract).
  * **WAL** journal mode so concurrent sessions don't block each other on reads.
  * **JSON** values (not pickle) — portable, no deserialization-security footgun. A non-JSON value
    raises on set(), surfacing the bug loudly.
  * Values are namespaced by the plugin's OWN key strings (e.g. "bayes/prior",
    f"bayes/posterior/{participant_id}") — the store is shared, so plugins should prefix keys.

`persist` is LIVE durable state (read back mid-game), NOT the research system-of-record — final
analysis data still flows through ctx.log -> episode logs / W&B.
"""
from __future__ import annotations

import json
import sqlite3
import threading
from pathlib import Path
from typing import Any, Optional


class SqliteKV:
    """Thread-safe JSON KV over SQLite. Implements the same interface as cora_ext._KV."""

    def __init__(self, db_path: "str | Path"):
        self._lock = threading.Lock()
        Path(db_path).parent.mkdir(parents=True, exist_ok=True)
        # check_same_thread=False: run_blocking offloads to executor threads; the lock serializes.
        self._conn = sqlite3.connect(str(db_path), check_same_thread=False)
        self._conn.execute("PRAGMA journal_mode=WAL")
        self._conn.execute("PRAGMA synchronous=NORMAL")
        self._conn.execute(
            "CREATE TABLE IF NOT EXISTS kv (key TEXT PRIMARY KEY, value TEXT NOT NULL, "
            "updated_at TEXT DEFAULT (datetime('now')))")
        self._conn.commit()

    def get(self, key: str, default: Any = None) -> Any:
        cur = self._conn.execute("SELECT value FROM kv WHERE key = ?", (key,))
        row = cur.fetchone()
        if row is None:
            return default
        try:
            return json.loads(row[0])
        except (ValueError, TypeError):
            return default

    def set(self, key: str, value: Any) -> None:
        payload = json.dumps(value)  # raises TypeError on non-JSON value -> loud, correct
        with self._lock:
            self._conn.execute(
                "INSERT INTO kv (key, value, updated_at) VALUES (?, ?, datetime('now')) "
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value, "
                "updated_at = excluded.updated_at",
                (key, payload))
            self._conn.commit()

    def setdefault(self, key: str, default: Any) -> Any:
        with self._lock:
            cur = self._conn.execute("SELECT value FROM kv WHERE key = ?", (key,))
            row = cur.fetchone()
            if row is not None:
                try:
                    return json.loads(row[0])
                except (ValueError, TypeError):
                    pass
            self._conn.execute(
                "INSERT OR REPLACE INTO kv (key, value, updated_at) VALUES (?, ?, datetime('now'))",
                (key, json.dumps(default)))
            self._conn.commit()
            return default

    def keys(self, prefix: str = "") -> list[str]:
        cur = self._conn.execute("SELECT key FROM kv WHERE key LIKE ? ORDER BY key",
                                 (prefix + "%",))
        return [r[0] for r in cur.fetchall()]

    def close(self) -> None:
        with self._lock:
            self._conn.close()


_DEFAULT: Optional[SqliteKV] = None
_DEFAULT_PATH = "data/plugin_store.db"


def default_store(path: Optional[str] = None) -> SqliteKV:
    """Process-wide singleton store (shared across all sessions, so `persist` is genuinely
    cross-game). Call once at startup with an explicit path to override the default location."""
    global _DEFAULT
    if _DEFAULT is None:
        _DEFAULT = SqliteKV(path or _DEFAULT_PATH)
    return _DEFAULT
