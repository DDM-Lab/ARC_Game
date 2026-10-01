"""The game's parameter sheet (StreamingAssets/game_param_config.csv), read the way Unity reads it.

Unity's GameConfigLoader takes parameters from ARC_PARAM_CONFIG if set, else the bundled CSV.
Python reads the same sheet, for code that has no game state to read a number from: the
surrogate, and a policy planning for something the state does not show yet (a shelter's beds
before any shelter exists). Whenever the game state exports a number, read it from the state.

    params = load()                       # ARC_PARAM_CONFIG, else the bundled sheet
    params["initialShelterCapacity"]      # 100
    load("runs/x/params.csv")             # the sheet a particular Unity run used
"""
from __future__ import annotations

import csv
import os
from functools import lru_cache
from pathlib import Path

BUNDLED = Path(__file__).resolve().parents[1] / "Assets" / "StreamingAssets" / "game_param_config.csv"


def _typed(v: str):
    for cast in (int, float):
        try:
            return cast(v)
        except ValueError:
            pass
    return v


@lru_cache(maxsize=None)
def _read(path: str) -> dict:
    with open(path, newline="", encoding="utf-8") as f:
        rows = csv.reader(f)
        next(rows)                                     # header: Parameter, Value, ...
        return {r[0].strip(): _typed(r[1].strip()) for r in rows if r and r[0].strip()}


def load(path=None) -> dict:
    """Parameter name -> value (int, float or string) from `path`, ARC_PARAM_CONFIG, or the
    bundled sheet, in that order."""
    return dict(_read(str(path or os.environ.get("ARC_PARAM_CONFIG") or BUNDLED)))
