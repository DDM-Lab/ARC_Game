"""End-to-end demo of plugins/preference_model.py WITHOUT a live game.

Drives on_choice_resolved events (as the game would), watches the posterior update, calls the tool,
then simulates a SECOND session with a NEW context and shows the posterior loaded from the durable
store — i.e. preferences persist across sessions/restarts. Run:
  env -u ALL_PROXY -u HTTP_PROXY -u HTTPS_PROXY -u http_proxy -u https_proxy ./.venv/bin/python scripts/demo_preference_model.py
"""
import asyncio
import importlib.util
import os
import sys
import tempfile

# run from the repo root regardless of where this script is invoked
_REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, _REPO)
os.chdir(_REPO)

import cora_ext
import plugin_store
from cora_ext import MockToolContext, get_tool, run_hooks, run_tool

# load the plugin fresh
cora_ext.clear_registry()
_spec = importlib.util.spec_from_file_location("pref_model", "plugins/preference_model.py")
_m = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_m)

# one durable SQLite store, shared across the two simulated sessions
_DB = os.path.join(tempfile.mkdtemp(), "persist.db")


def ctx_for(participant):
    c = MockToolContext(participant_id=participant)
    c.persist = plugin_store.SqliteKV(_DB)     # real durable store (not the in-memory mock KV)
    return c


async def main():
    print("── SESSION 1 · P1 · mix of HUMAN picks (is_human) and OFFICER picks ──")
    ctx = ctx_for("P1")
    events = [
        {"is_human": True, "choice": 1},                               # human leans choice 1
        {"is_human": False, "actor": "Food Officer", "choice": 0},     # officer picks 0
        {"is_human": True, "choice": 1},
        {"is_human": True, "choice": 0},
        {"is_human": False, "actor": "Food Officer", "choice": 0},
        {"is_human": True, "choice": 1},
    ]
    for ev in events:
        await run_hooks("on_choice_resolved", ctx, ev)
    print("  HUMAN model  pref/human/P1 :", ctx.persist.get("pref/human/P1"))
    print("  OFFICER model pref/officer/Food Officer:", ctx.persist.get("pref/officer/Food Officer"))
    print("  -> the two are learned SEPARATELY (differentiated by event['is_human'])")
    await run_tool(get_tool("preferred_choices"), ctx, {})
    print("  agent proposes (weighted by the HUMAN model):", [p["label"] for p in ctx.proposed[-1]])

    print("\n── SESSION 2 · same P1 · NEW context (later game / after restart) ──")
    ctx2 = ctx_for("P1")
    print("  human model loaded from durable store:", ctx2.persist.get("pref/human/P1"), " <- persisted")

    print("\n── different participant P2 (isolation) ──")
    print("  P2 human model:", ctx_for("P2").persist.get("pref/human/P2"), "(empty — participant-scoped)")


asyncio.run(main())
