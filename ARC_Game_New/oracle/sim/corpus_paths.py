"""The exported game data the surrogate runs on, in one place.

Each corpus is a directory of RPC exports from one headless build: sim_constants.json
(`sim_constants`) and map_grid.json (`map_grid`), plus the map spec it pairs with
(oracle/sim/maps/<map>.json, from dump_map). They are build-specific -- a scene or parameter
change means a new export -- so the port reads them from here and nowhere else.

    CORA_SIM_CORPUS=v6   the current rules (bench-v6 build)
    CORA_SIM_CORPUS=     (unset) the merge-sep15 corpus the merge_v6 captures were taken on
"""
import json
import os

PKG = os.path.dirname(os.path.abspath(__file__))
NAME = os.environ.get("CORA_SIM_CORPUS", "")
DIR = os.path.join(PKG, "corpus", NAME) if NAME else os.path.join(PKG, "corpus")
CONSTANTS = os.path.join(DIR, "sim_constants.json")
MAP_GRID = os.path.join(DIR, "map_grid.json")
MAP = NAME or "default"                          # oracle/sim/maps/<MAP>.json
# The rules generation: the v6 corpus is the bench-v6 build, whose clock and food rules differ
# from the merge-sep15 build the default corpus came from.
V6 = NAME == "v6"

_constants = None


def constants() -> dict:
    """sim_constants.json of the selected corpus (parsed once)."""
    global _constants
    if _constants is None:
        with open(CONSTANTS) as f:
            _constants = json.load(f)
    return _constants
