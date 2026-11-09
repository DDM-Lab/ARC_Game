"""The study-1.0.2 GUI build's flood iteration order (study rules, see sim.new_world).

The surrogate (oracle/sim/flood.py) models the bench build, whose FloodSystem SORTS every
iteration that draws (FloodTilesInOrder, RiverTilesInOrder, the deduped expansion candidates).
The study build that humans played (tag v1.0.2-study-2026-10-12) is the "PARITY BUILD (ledger
D24)": FloodTilesInOrder/RiverTilesInOrder return the HashSet's raw enumeration and the
candidate list is `new List(new HashSet(candidates))` with no sort. Same draw count, different
tiles -- the human game's flood (and everything that reads it) parts from the port on day 1.

.NET (Mono reference-source) HashSet<T> enumerates its slot array in index order; Add appends at
the end unless a removed slot is free, and freed slots are reused LIFO. That is modelled here:
`StudyFloodState.order()` is the enumeration order. Draw sites and thresholds are flood.py's.
"""
from __future__ import annotations

from . import flood as F
from .flood import FloodState


class StudyFloodState(FloodState):
    def __init__(self, tiles=None, last_weather="Sunny"):
        super().__init__(None, last_weather)
        self.slots = []        # packed tile or None (free)
        self.index = {}        # tile -> slot
        self.free = []         # free-list stack (last freed reused first)
        for t in tiles or ():
            self.add(t)

    def add(self, p) -> bool:
        if p in self.tiles:
            return False
        if self.free:
            i = self.free.pop()
            self.slots[i] = p
        else:
            i = len(self.slots)
            self.slots.append(p)
        self.index[p] = i
        self.tiles.add(p)
        return True

    def discard(self, p) -> bool:
        i = self.index.pop(p, None)
        if i is None:
            return False
        self.slots[i] = None
        self.free.append(i)
        self.tiles.discard(p)
        if not self.tiles:
            # Reference-source HashSet<T>.Remove: when the count reaches 0, m_lastIndex = 0 and
            # m_freeList = -1 -- the layout resets, so the next adds fill from slot 0 in order.
            self.slots, self.free, self.index = [], [], {}
        return True

    def order(self):
        return [p for p in self.slots if p is not None]

    def clone(self):
        s = StudyFloodState.__new__(StudyFloodState)
        s.tiles = set(self.tiles)
        s.last_weather = self.last_weather
        s.prev_count = self.prev_count
        s.change_this_round = self.change_this_round
        s.slots = list(self.slots)
        s.index = dict(self.index)
        s.free = list(self.free)
        return s


def update_flood_study(fs, fmap, rng, weather, rain_intensity, marks=None, stats=None,
                     on_added=None, on_removed=None):
    """flood.update_flood with the study build's HashSet iteration order."""
    if not isinstance(fs, StudyFloodState):
        return F.update_flood(fs, fmap, rng, weather, rain_intensity, marks, stats, on_added, on_removed)
    C = F.C
    expansion_rate, spread_mult, shrink_chance = C["weather"].get(weather, C["weather"].get("Sunny", (0.0, 0.5, 0.3)))
    weather_changed = fs.last_weather != weather
    fs.last_weather = weather
    before = len(fs.tiles)
    fs.prev_count = before
    if rain_intensity >= C["min_rain"]:
        if before == 0 or (weather_changed and rain_intensity > 0):
            spawn_thr = F.threshold_for(F.f32add(C["spawn_chance"], F.f32mul(rain_intensity, C["rain_spawn_bonus"])))
            # riverTiles: filled x-major by CacheRiverTiles, never removed -> sorted order
            for p in sorted(fmap.river):
                if marks is not None:
                    marks.append("draw:Flood.1")
                if rng.value_lt(spawn_thr):
                    fs.add(p)
    if expansion_rate > 0 and len(fs.tiles) > 0:
        thr = F.spread_thresholds(spread_mult)
        candidates = []
        for p in fs.order():
            for d in F._NEIGH_DELTAS:
                n = p + d
                if F._can_spread_to(n, fs, fmap, rng, thr, marks):
                    candidates.append(n)
        seen, dedup = set(), []
        for c in candidates:                    # new HashSet(list): first-occurrence order
            if c not in seen:
                seen.add(c)
                dedup.append(c)
        candidates = dedup
        for _ in range(F._round_to_int(expansion_rate)):
            if not candidates:
                break
            if marks is not None:
                marks.append("draw:Flood.2")
            idx = rng.range_int(0, len(candidates))
            new = fs.add(candidates[idx])
            candidates.pop(idx)
            if new and on_added is not None:
                on_added(fs.tiles)
        if marks is not None:
            marks.append("draw:Flood.3")
        if rng.value_lt(F._RANDOM_EXPANSION_LE_THR) and fs.tiles:
            arr = fs.order()
            if marks is not None:
                marks.append("draw:Flood.4")
            src = arr[rng.range_int(0, len(arr))]
            if marks is not None:
                marks.append("draw:Flood.5")
            dx, dy = F._DIRS[rng.range_int(0, 4)]
            if marks is not None:
                marks.append("draw:Flood.6")
            dist = rng.range_int(1, C["max_random_distance"] + 1)
            sx, sy = F.unpack(src)
            target = F.pack(sx + dx * dist, sy + dy * dist)
            if F._can_spread_to(target, fs, fmap, rng, thr, marks):
                new = fs.add(target)
                if new and on_added is not None:
                    on_added(fs.tiles)
    do_shrink = rain_intensity < C["min_rain"] or shrink_chance > 0
    if do_shrink:
        chance = F.f32add(shrink_chance, C["base_shrink"])
        if rain_intensity < C["min_rain"]:
            chance = F.f32add(chance, 0.8)
        flat_thr = F.threshold_for(chance)
        edge_thr = F.threshold_for(F.f32add(chance, C["edge_shrink_bonus"]))
        to_remove = []
        for p in fs.order():
            if marks is not None:
                marks.append("draw:Flood.7")
            if rng.value_lt(edge_thr if F._is_edge(p, fs) else flat_thr):
                to_remove.append(p)
        for p in to_remove:
            fs.discard(p)
        if to_remove and on_removed is not None:
            on_removed(fs.tiles)
    fs.change_this_round = len(fs.tiles) - before
