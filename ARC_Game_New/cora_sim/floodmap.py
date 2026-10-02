"""Static terrain, exported from Unity once and never re-derived.

The bounds check in CanFloodSpreadTo GATES WHETHER A DRAW HAPPENS, so an off-by-one here
shifts the entire RNG stream. That is why terrain comes from Unity's own map_grid RPC
rather than being reconstructed.

Coordinates are packed to ints WITH AN OFFSET. Unity cell coords are negative (xMin=-16,
yMin=-12), and a naive (x<<16)|y would break the property that integer sort order equals
(x,y,z) lexicographic order -- which is exactly the ordering the C# comparators rely on
after this session's determinism fixes.
"""
from __future__ import annotations

import json
import os

# Offset chosen to keep every packed value non-negative and order-preserving for the
# actual map bounds (xMin=-16, yMin=-12, 31x22). 512 leaves enormous headroom.
_OFF = 512
_SHIFT = 10

# blockingRadius comes from the RUNNING GAME: the scene overrides the C# initialiser
# (source says 1, runtime is 5). See cora_sim/flood.py for the full note.
def _blocking_radius():
    import json as _j, os as _o
    p = _o.path.join(_o.path.dirname(_o.path.abspath(__file__)), "corpus", "sim_constants.json")
    try:
        return int(_j.load(open(p))["flood"]["blockingRadius"])
    except Exception:
        return 1


BLOCKING_RADIUS = _blocking_radius()


def pack(x: int, y: int) -> int:
    return ((x + _OFF) << _SHIFT) | (y + _OFF)


def unpack(p: int):
    return ((p >> _SHIFT) - _OFF, (p & ((1 << _SHIFT) - 1)) - _OFF)


class FloodMap:
    """Terrain classification + bounds, keyed by packed coordinate."""

    __slots__ = ("x_min", "y_min", "width", "height", "ground", "river", "blocking",
                 "in_bounds", "initial_flood", "blocking_tiles")

    def __init__(self, grid: dict):
        b = grid["bounds"]
        self.x_min, self.y_min = b["xMin"], b["yMin"]
        self.width, self.height = b["width"], b["height"]
        self.ground, self.river, self.blocking = set(), set(), set()
        self.in_bounds, self.initial_flood, self.blocking_tiles = set(), set(), set()

        # Prefer the LAYERED export. `rows` collapses tilemaps into one character by
        # priority, which hides blocking under road/flood and river under road -- and the
        # flood port branches per layer, so a collapsed grid takes the wrong draw branch.
        layers = grid.get("layers")
        if layers:
            for x in range(self.x_min, self.x_min + self.width):
                for y in range(self.y_min, self.y_min + self.height):
                    self.in_bounds.add(pack(x, y))
            for x, y in layers.get("ground", []):
                self.ground.add(pack(x, y))
            for x, y in layers.get("river", []):
                self.river.add(pack(x, y))
            for x, y in layers.get("blocking", []):
                self.blocking_tiles.add(pack(x, y))
            self._dilate()
            return

        rows = grid["rows"]
        for r, row in enumerate(rows):
            y = self.y_min + (self.height - 1 - r)
            for c, ch in enumerate(row):
                x = self.x_min + c
                p = pack(x, y)
                self.in_bounds.add(p)
                if ch == "g":
                    self.ground.add(p)
                elif ch == "r":
                    self.river.add(p); self.ground.add(p)   # river IS a ground tile
                elif ch == "b":
                    self.blocking_tiles.add(p)
                elif ch == "f":
                    self.initial_flood.add(p)
                    self.ground.add(p)
                elif ch == "R":
                    self.ground.add(p)                      # road sits on ground

        self._dilate()

    def _dilate(self):
        """blockedPositions is a DILATION of the blocking tiles by blockingRadius, not the
        tiles themselves (FloodSystem.CacheBlockingPositions). Using the raw tiles made the
        port classify a neighbour as river/empty where Unity classified it blocked, taking
        a different draw site and desyncing the stream at the first divergence."""
        for p in self.blocking_tiles:
            x, y = unpack(p)
            for dx in range(-BLOCKING_RADIUS, BLOCKING_RADIUS + 1):
                for dy in range(-BLOCKING_RADIUS, BLOCKING_RADIUS + 1):
                    self.blocking.add(pack(x + dx, y + dy))

    @classmethod
    def load(cls, path=None):
        path = path or os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                    "corpus", "map_grid.json")
        return cls(json.load(open(path)))

    def neighbours(self, p: int):
        """Unity's GetAdjacentPositions order: up, down, left, right. ORDER IS
        LOAD-BEARING -- it sets the sequence of draws during candidate build."""
        x, y = unpack(p)
        return (pack(x, y + 1), pack(x, y - 1), pack(x - 1, y), pack(x + 1, y))
