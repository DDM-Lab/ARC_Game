"""Bit-exact port of Unity's FloodSystem round update.

Ten of the eighteen live RNG draw sites are here, and flood is the highest-volume drawer
in the game, so this module sets the RNG stream position for everything after it. Getting
the DRAW COUNT right matters as much as getting the outcomes right.

THREE THINGS THAT ARE EASY TO GET WRONG, ALL VERIFIED AGAINST THE C#:

1. Candidate build draws PER NEIGHBOUR OCCURRENCE, and dedup happens AFTERWARDS. A tile
   adjacent to three flood tiles is checked three times and draws three times. Deduping
   the frontier before checking would silently change the draw count and desync the whole
   stream. (FloodSystem.cs ExpandFlood, dedup at the `new HashSet<>(...)` line.)

2. CanFloodSpreadTo has TWO EARLY RETURNS THAT DRAW NOTHING -- already-flooded, and
   out-of-bounds. The bounds check therefore gates whether a draw happens at all, which is
   why terrain comes from Unity's own map export rather than being reconstructed.

3. Iteration order is sorted by coordinate, matching the five determinism fixes made to
   the C# this session. Those sorts are load-bearing for this port: if anyone "optimises"
   them away in Unity, this module silently stops matching.
"""
from __future__ import annotations

from .floodmap import FloodMap, pack, unpack

# CONSTANTS COME FROM THE RUNNING GAME, NEVER FROM THE C# SOURCE.
#
# FloodParameters is a serialized class on a MonoBehaviour, so the SCENE ASSET overrides
# every field initialiser in the .cs file. Measured: source says blockingRadius = 1, the
# running game uses 5; randomExpansionChance 0.15 vs 0.4; maxRandomExpansionDistance 2 vs
# 5; terrainBlockMultiplier 0.1 vs 1; and every weatherFloodRates row has shrinkageChance
# 0.3 in the scene against 0.0/0.1/0.3 in source. A port built on the source constants
# classified terrain differently from Unity and desynced the RNG stream.
#
# cora_sim/corpus/sim_constants.json is exported by the `sim_constants` RPC. Regenerate it
# whenever the scene changes -- the same rule as the RNG golden corpus.
import json as _json
import os as _os

_CONST_PATH = _os.path.join(_os.path.dirname(_os.path.abspath(__file__)),
                            "corpus", "sim_constants.json")


def load_constants(path=None):
    d = _json.load(open(path or _CONST_PATH))
    f = d["flood"]
    weather = {r["weather"]: (float(r["expansionRate"]),
                              float(r["spreadChanceMultiplier"]),
                              float(r["shrinkageChance"]))
               for r in f["weatherFloodRates"]}
    return {
        "base_spread": float(f["baseSpreadChance"]),
        "random_expansion_chance": float(f["randomExpansionChance"]),
        "max_random_distance": int(f["maxRandomExpansionDistance"]),
        "land_mult": float(f["landSpreadMultiplier"]),
        "block_mult": float(f["terrainBlockMultiplier"]),
        "blocking_radius": int(f["blockingRadius"]),
        "min_rain": float(f["minimumRainForSpawning"]),
        "edge_shrink_bonus": float(f["edgeShrinkageBonus"]),
        "spawn_chance": float(f["floodSpawnChance"]),
        "rain_spawn_bonus": float(f["rainIntensitySpawnBonus"]),
        "base_shrink": float(f["baseShrinkageChance"]),
        "weather": weather,
    }


C = load_constants()

# WeatherSystem.GetRainIntensity()
RAIN_INTENSITY = {"Sunny": 0.0, "SmallRain": 0.3, "MediumRain": 0.6,
                  "HeavyRain": 0.8, "Storm": 1.0}

# Unity's GetAdjacentPositions order: up, down, left, right. Order is load-bearing.
_DIRS = ((0, 1), (0, -1), (-1, 0), (1, 0))


def _round_to_int(v: float) -> int:
    """Mathf.RoundToInt uses banker's rounding (round-half-to-even), NOT Python's round()
    on floats in every case -- but Python's round() is also banker's, so they agree.
    Kept as a named function so the assumption is visible and testable."""
    return int(round(v))


class FloodState:
    """Mutable flood state. Cloned explicitly at search branch points."""

    __slots__ = ("tiles", "last_weather", "prev_count", "change_this_round")

    def __init__(self, tiles=None, last_weather="Sunny"):
        self.tiles = set(tiles) if tiles else set()
        self.last_weather = last_weather
        self.prev_count = 0
        self.change_this_round = 0

    def clone(self) -> "FloodState":
        s = FloodState.__new__(FloodState)
        s.tiles = set(self.tiles)
        s.last_weather = self.last_weather
        s.prev_count = self.prev_count
        s.change_this_round = self.change_this_round
        return s


def _can_spread_to(p, fs: FloodState, fmap: FloodMap, rng, mult: float, marks):
    """Mirror of CanFloodSpreadTo. Returns bool. DRAWS AT MOST ONE random, and draws
    NOTHING on the already-flooded and out-of-bounds paths."""
    if p in fs.tiles:
        return False
    if p not in fmap.in_bounds:
        return False
    if p in fmap.blocking:
        if marks is not None:
            marks.append("draw:Flood.8")
        return rng.value() < (C["base_spread"] * mult * C["block_mult"])
    # groundTile != null && groundTile != riverRuleTile  ->  land
    if p in fmap.ground and p not in fmap.river:
        if marks is not None:
            marks.append("draw:Flood.9")
        return rng.value() < (C["base_spread"] * mult * C["land_mult"])
    if marks is not None:
        marks.append("draw:Flood.10")
    return rng.value() < (C["base_spread"] * mult)


def _is_edge(p, fs: FloodState):
    x, y = unpack(p)
    for dx, dy in _DIRS:
        if pack(x + dx, y + dy) not in fs.tiles:
            return True
    return False


def update_flood(fs: FloodState, fmap: FloodMap, rng, weather: str,
                 rain_intensity: float, marks=None) -> None:
    """One round of flood evolution. Mirrors UpdateFlood's branch structure exactly --
    including which branches are SKIPPED, since a skipped branch draws nothing."""
    expansion_rate, spread_mult, shrink_chance = C["weather"].get(
        weather, C["weather"].get("Sunny", (0.0, 0.5, 0.3)))

    weather_changed = fs.last_weather != weather
    fs.last_weather = weather
    before = len(fs.tiles)
    fs.prev_count = before

    # ── spawn from rain (Flood.1: one draw per RIVER tile, unconditional) ────────────
    if rain_intensity >= C["min_rain"]:
        if before == 0 or (weather_changed and rain_intensity > 0):
            spawn_chance = C["spawn_chance"] + rain_intensity * C["rain_spawn_bonus"]
            for p in sorted(fmap.river):
                if marks is not None:
                    marks.append("draw:Flood.1")
                if rng.value() < spawn_chance:
                    fs.tiles.add(p)

    # ── expansion ───────────────────────────────────────────────────────────────────
    if expansion_rate > 0 and len(fs.tiles) > 0:
        candidates = []
        for p in sorted(fs.tiles):
            x, y = unpack(p)
            for dx, dy in _DIRS:
                n = pack(x + dx, y + dy)
                if _can_spread_to(n, fs, fmap, rng, spread_mult, marks):
                    candidates.append(n)       # WITH multiplicity; dedup is after
        candidates = sorted(set(candidates))

        tiles_to_expand = _round_to_int(expansion_rate)
        for _ in range(tiles_to_expand):
            if not candidates:
                break
            if marks is not None:
                marks.append("draw:Flood.2")
            idx = rng.range_int(0, len(candidates))
            fs.tiles.add(candidates[idx])
            candidates.pop(idx)

        # ── random expansion (Flood.3-6) ────────────────────────────────────────────
        if marks is not None:
            marks.append("draw:Flood.3")
        if rng.value() <= C["random_expansion_chance"] and fs.tiles:
            arr = sorted(fs.tiles)
            if marks is not None:
                marks.append("draw:Flood.4")
            src = arr[rng.range_int(0, len(arr))]
            if marks is not None:
                marks.append("draw:Flood.5")
            dx, dy = _DIRS[rng.range_int(0, 4)]
            if marks is not None:
                marks.append("draw:Flood.6")
            dist = rng.range_int(1, C["max_random_distance"] + 1)
            sx, sy = unpack(src)
            target = pack(sx + dx * dist, sy + dy * dist)
            if _can_spread_to(target, fs, fmap, rng, spread_mult, marks):
                fs.tiles.add(target)

    # ── shrinkage (Flood.7: one draw per flood tile) ────────────────────────────────
    do_shrink = rain_intensity < C["min_rain"] or shrink_chance > 0
    if do_shrink:
        chance = shrink_chance + C["base_shrink"]
        if rain_intensity < C["min_rain"]:
            chance += 0.8
        to_remove = []
        for p in sorted(fs.tiles):
            c = chance + (C["edge_shrink_bonus"] if _is_edge(p, fs) else 0.0)
            if marks is not None:
                marks.append("draw:Flood.7")
            if rng.value() < c:
                to_remove.append(p)
        for p in to_remove:
            fs.tiles.discard(p)

    fs.change_this_round = len(fs.tiles) - before
