"""The map as DATA, so the surrogate is not welded to the shipped scene.

Everything here was hard-coded in roads.py: the road cells, the per-building road
connections, the buildable sites, the vehicle depots. That was fine while the goal was
"reproduce this one scene exactly" and wrong the moment the goal became "search over many
maps". A MapSpec is one frozen bundle of that data, loadable from JSON, so a new map is a
new file rather than a new module.

THE MOTION CONSTANTS LIVE HERE TOO, and that is deliberate rather than tidy-minded.
moveSpeed, simulationDuration and the tile anchor are all SERIALIZED BY THE SCENE, and in
this port a scene value has overridden a .cs field initialiser seven separate times --
moveSpeed reads 8 where Vehicle.cs says `= 5f`. They are properties of a map, not of the
game, so a second map may legitimately carry different ones and must be able to say so.

Build one with cora_sim/dump_map.py against a running build; nothing here is transcribed
from the scene file by hand.
"""
import json
import os

_MAPS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "maps")
_cache = {}


class MapSpec:
    """One map's geometry and motion constants. Treat as immutable."""

    __slots__ = ("name", "description", "anchor", "move_speed", "simulation_duration",
                 "time_speed", "fixed_delta", "depots", "road_cells", "building_cell",
                 "facility_cell", "site_cell")

    def __init__(self, d):
        self.name = d.get("name", "unnamed")
        self.description = d.get("description", "")
        self.anchor = float(d["anchor"])
        self.move_speed = float(d["move_speed"])
        self.simulation_duration = float(d["simulation_duration"])
        self.time_speed = int(d["time_speed"])
        self.fixed_delta = float(d["fixed_delta"])
        self.depots = tuple(tuple(c) for c in d["depots"])
        self.road_cells = frozenset(tuple(c) for c in d["road_cells"])
        self.building_cell = {k: tuple(v) for k, v in d["building_cell"].items()}
        self.facility_cell = {k: tuple(v) for k, v in d["facility_cell"].items()}
        # Site ids are ints in the game and strings in JSON.
        self.site_cell = {int(k): tuple(v) for k, v in d["site_cell"].items()}

    @property
    def round_seconds(self):
        """SimulationCoroutine(simulationDuration / (int)currentTimeSpeed)."""
        return self.simulation_duration / self.time_speed

    @classmethod
    def load(cls, name="default"):
        if name not in _cache:
            with open(os.path.join(_MAPS, name + ".json")) as fh:
                _cache[name] = cls(json.load(fh))
        return _cache[name]

    @classmethod
    def default(cls):
        return cls.load("default")

    def __repr__(self):
        return (f"<MapSpec {self.name}: {len(self.road_cells)} road cells, "
                f"{len(self.building_cell)} buildings, {len(self.site_cell)} sites, "
                f"moveSpeed {self.move_speed}, round {self.round_seconds}s>")
