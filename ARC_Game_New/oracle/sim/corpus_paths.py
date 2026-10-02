"""The exported game data the surrogate runs on, in one place.

corpus/ holds RPC exports from the headless build: sim_constants.json (`sim_constants`),
map_grid.json (`map_grid`) and task_assets.json (oracle/sim/export_assets.py); maps/default.json
is the map spec (dump_map). They are build-specific -- a scene, parameter or TaskData change
means a new export -- so the port reads them from here and nowhere else.
"""
import json
import os

PKG = os.path.dirname(os.path.abspath(__file__))
DIR = os.path.join(PKG, "corpus")
CONSTANTS = os.path.join(DIR, "sim_constants.json")
MAP_GRID = os.path.join(DIR, "map_grid.json")
MAP = "default"                                  # oracle/sim/maps/<MAP>.json

_constants = None


def constants() -> dict:
    """sim_constants.json (parsed once)."""
    global _constants
    if _constants is None:
        with open(CONSTANTS) as f:
            _constants = json.load(f)
    return _constants
