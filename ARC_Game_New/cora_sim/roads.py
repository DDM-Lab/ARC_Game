"""The road graph, and the travel time that rides on it.

Ported from PathfindingSystem + RoadTilemapManager + Vehicle. This is the last
subsystem the surrogate was missing, and the only one that is SPATIAL: a delivery's
duration is the A* distance a vehicle drives, so it changes when the flood forces a
detour and it changes with where the vehicle last parked.

Everything here is integer work on a 106-cell grid, so it does not cost the
surrogate its speed -- no float geometry is needed, because every A* edge in the real
game measures exactly 1.0 (verified: len == nodes-1 on 39 of 39 observed legs).

The cell set and the building connection points are DUMPED FROM THE RUNNING GAME
([ROADDUMP] / road:connection), not transcribed from the scene file. Scene-serialized
values have overridden .cs field initialisers six separate times in this port, so
reading them from the live object is the only trustworthy source.
"""
from heapq import heappush, heappop

# Tilemap.tileAnchor is (0.5, 0.5), so a cell's world centre is cell + 0.5.
ANCHOR = 0.5

# Vehicle.moveSpeed, and Application.targetFrameRate set in GymServerManager.
#
# moveSpeed is 8, NOT the `public float moveSpeed = 5f` written in Vehicle.cs -- the scene
# serializes 8 and the field initialiser never runs. That is the SEVENTH time in this port a
# scene value has overridden a .cs initialiser (blockingRadius 1->5, caseworkNeedProbability
# 40->23.4, workersConsumeFoodToo true->false, ...), which is why this is read off the running
# game via the delivery:leg mark rather than from the source line. Every leg mark carries the
# live speed so a future scene change shows up as a test failure instead of silent drift.
MOVE_SPEED = 8.0
TARGET_FPS = 10.0

ROAD_CELLS = frozenset((
    (-14,-9), (-14,-3), (-14,6), (-13,-9), (-13,-3), (-13,6), (-12,-9), (-12,-3),
    (-12,6), (-11,-9), (-11,-3), (-11,6), (-10,-9), (-10,-3), (-10,6), (-9,-9),
    (-9,-3), (-9,5), (-9,6), (-8,-9), (-8,-8), (-8,-7), (-8,-6), (-8,-3),
    (-8,5), (-7,-6), (-7,-3), (-7,4), (-7,5), (-6,-6), (-6,-3), (-6,4),
    (-5,-7), (-5,-6), (-5,-5), (-5,-4), (-5,-3), (-5,-2), (-5,-1), (-5,0),
    (-5,1), (-5,2), (-5,3), (-5,4), (-4,-9), (-4,-8), (-4,-7), (-4,-4),
    (-4,4), (-3,-4), (-3,4), (-3,5), (-2,-4), (-2,5), (-2,6), (-2,7),
    (-2,8), (-2,9), (-1,-4), (-1,5), (0,-4), (0,5), (1,-4), (1,5),
    (2,-4), (2,5), (3,-4), (3,5), (4,-7), (4,-6), (4,-5), (4,-4),
    (4,-3), (4,-2), (4,-1), (4,0), (4,1), (4,2), (4,3), (4,4),
    (4,5), (5,-7), (5,3), (6,-9), (6,-8), (6,-7), (6,3), (7,-7),
    (7,3), (8,-7), (8,3), (9,-7), (9,3), (10,-7), (10,3), (11,-7),
    (11,-6), (11,3), (11,4), (11,5), (11,6), (12,-6), (12,3), (12,6),
    (13,-6), (13,6),
))

# RoadConnection.nearestRoadPosition per building -- the actual A* endpoints. Vehicles
# drive to these, NOT to the building's own position.
BUILDING_CELL = {
    'Community01': (1, 5),
    'Community02': (-10, -3),
    'Community03': (9, 3),
    'Kitchen_0': (-8, -3),
    'Motel': (-5, 4),
}


def world_to_cell(wx, wy):
    """Tilemap.WorldToCell: floor, since the anchor offsets the centre not the origin."""
    from math import floor
    return (int(floor(wx)), int(floor(wy)))


def cell_to_world(cell):
    return (cell[0] + ANCHOR, cell[1] + ANCHOR)


def nearest_road(cell):
    """RoadTilemapManager.FindNearestRoadPosition.

    Expanding square out to radius 10, keeping the EUCLIDEAN-nearest road cell and
    breaking ties by visit order (x ascending, then y ascending) because the C# keeps
    the first strictly-smaller distance. Returns the input cell when nothing is found,
    which is what the original does too.
    """
    if cell in ROAD_CELLS:
        return cell
    best, best_d2 = cell, None
    for radius in range(0, 11):
        for x in range(-radius, radius + 1):
            for y in range(-radius, radius + 1):
                if radius > 0 and abs(x) < radius and abs(y) < radius:
                    continue
                c = (cell[0] + x, cell[1] + y)
                if c not in ROAD_CELLS:
                    continue
                d2 = x * x + y * y
                if best_d2 is None or d2 < best_d2:
                    best, best_d2 = c, d2
        if best_d2 is not None:
            # C# keeps expanding to radius 10 regardless, but a strictly closer cell can
            # only appear at a smaller Euclidean distance than the current best, and every
            # later ring is at least `radius` away -- so stopping once a ring has produced
            # a hit whose distance cannot be beaten is equivalent and much cheaper.
            if best_d2 <= radius * radius:
                break
    return best


_DIRS = ((0, 1), (0, -1), (-1, 0), (1, 0))   # GetNeighbors: up, down, left, right


def path_length(start, goal, flooded=frozenset()):
    """Steps along PathfindingSystem's flood-aware A*, or None when no route exists.

    Only the LENGTH matters: Vehicle.MoveToPosition spends journeyLength/moveSpeed
    seconds on the leg, and the per-frame CheckForFloodCollision never fired in any
    captured episode, so the particular route taken has no other observable effect.
    Length is tie-break independent, which is why this does not have to reproduce the
    C#'s OrderBy(FCost).ThenBy(HCost) node ordering exactly.
    """
    if start not in ROAD_CELLS or goal not in ROAD_CELLS:
        return None
    if start in flooded or goal in flooded:
        return None
    if start == goal:
        return 0
    def h(c):
        return abs(c[0] - goal[0]) + abs(c[1] - goal[1])
    open_ = [(h(start), 0, start)]
    best = {start: 0}
    while open_:
        _, g, cur = heappop(open_)
        if cur == goal:
            return g
        if g > best.get(cur, 1 << 30):
            continue
        for dx, dy in _DIRS:
            nb = (cur[0] + dx, cur[1] + dy)
            if nb not in ROAD_CELLS or nb in flooded:
                continue
            ng = g + 1
            if ng < best.get(nb, 1 << 30):
                best[nb] = ng
                heappush(open_, (ng + h(nb), ng, nb))
    return None


def leg_frames(steps):
    """Frames a leg of `steps` unit edges occupies.

    Vehicle.MoveToPosition accumulates Time.deltaTime once per frame until it covers
    journeyLength/moveSpeed, so at targetFrameRate 10 and moveSpeed 8 a leg costs 1.25
    frames per unit step.
    """
    return int(steps * (TARGET_FPS / MOVE_SPEED) + 0.5)


# Frames a round occupies, measured between consecutive round-start marks over two captured
# episodes. advance_round holds for about four seconds at targetFrameRate 10, so the nominal
# budget is 40; the first segment of a day is shorter and the day-rollover segment is only a
# handful of frames. The spread within a segment (39-50) is real frame-timing jitter, not
# noise in the measurement, and is the one place this model is approximate.
FRAMES_PER_ROUND = {0: 6, 1: 34}
FRAMES_PER_ROUND_DEFAULT = 40


def frames_in_segment(segment):
    return FRAMES_PER_ROUND.get(segment, FRAMES_PER_ROUND_DEFAULT)


class Fleet:
    """DeliverySystem's vehicles, carrying position, occupancy and damage between trips.

    A trip is TWO legs -- Vehicle.RunDelivery drives to the source road connection, loads,
    then drives to the destination -- so a delivery's cost depends on where the assigned
    vehicle last parked, not on the source/destination pair alone. That is why the same
    Kitchen->Community route was measured at 0 rounds one day and 5 the next.
    """

    __slots__ = ("pos", "busy_frames", "carrying", "damaged")

    # Where the three vehicles start, read off the first leg each one drove.
    DEPOTS = ((-4, -4), (2, -4), (3, 5))

    def __init__(self):
        self.pos = [nearest_road(c) for c in self.DEPOTS]
        self.busy_frames = [0, 0, 0]
        self.carrying = [None, None, None]
        # Flood does not merely delay a vehicle, it DISABLES it: StopVehicleDueToFlood sets
        # isDamaged and the status to Damaged, and IsAvailable() is `status == Idle`, so the
        # vehicle leaves the fleet until RepairVehicle() runs -- which only happens if the
        # player answers the repair task StopVehicleDueToFlood spawns. A flood that cuts one
        # route therefore costs a third of the delivery capacity indefinitely.
        self.damaged = [False, False, False]

    def clone(self):
        f = Fleet.__new__(Fleet)
        f.pos = list(self.pos)
        f.busy_frames = list(self.busy_frames)
        f.carrying = list(self.carrying)
        f.damaged = list(self.damaged)
        return f

    def available(self):
        return [i for i in range(len(self.pos))
                if not self.damaged[i] and self.busy_frames[i] <= 0 and self.carrying[i] is None]

    def best_vehicle(self, src_cell, quantity=0, capacity=100.0, speed=None):
        """DeliverySystem.FindSuitableVehicle / CalculateVehicleSuitability.

        Score is 100/(1+distanceToSource) + (quantity/capacity)*50 + moveSpeed*10, and the
        highest wins. With three identical vehicles the capacity and speed terms are equal
        across candidates, so the pick is simply the free vehicle CLOSEST to the source --
        but the full formula is kept because it is what decides if the vehicles ever differ.

        Distance is Vector3.Distance on world positions, NOT the A* length: the C# scores on
        straight-line proximity and only then drives the road network.
        """
        if speed is None:
            speed = MOVE_SPEED
        best, best_score = None, -1.0
        sx, sy = cell_to_world(src_cell)
        for i in self.available():
            vx, vy = cell_to_world(self.pos[i])
            d = ((vx - sx) ** 2 + (vy - sy) ** 2) ** 0.5
            score = 100.0 / (1.0 + d) + (quantity / capacity) * 50.0 + speed * 10.0
            if score > best_score:
                best, best_score = i, score
        return best

    def free_vehicle(self):
        av = self.available()
        return av[0] if av else None

    def dispatch(self, vehicle, payload, src_cell, dst_cell, flooded=frozenset()):
        """Cost the two legs. Returns False when the flood has cut either one.

        A cut route is NOT a slow delivery and NOT a retry. StopVehicleDueToFlood calls
        RemoveActiveDeliveryTask and nulls currentTask, so the order is dropped outright --
        and the vehicle is left damaged at wherever it had reached, out of the fleet.
        """
        leg1 = path_length(self.pos[vehicle], src_cell, flooded)
        if leg1 is None:
            self.damaged[vehicle] = True
            return False
        # The vehicle really is at the source once leg 1 is done, which is where an ABORTED
        # trip leaves it (LoadCargo bails when the source is empty, before leg 2 exists).
        self.pos[vehicle] = src_cell
        leg2 = path_length(src_cell, dst_cell, flooded)
        if leg2 is None:
            self.damaged[vehicle] = True
            return False
        self.busy_frames[vehicle] = leg_frames(leg1) + leg_frames(leg2)
        self.carrying[vehicle] = payload
        self.pos[vehicle] = dst_cell
        return True

    def advance(self, segment):
        """Burn one round's frames; return the payloads that arrived."""
        budget = frames_in_segment(segment)
        landed = []
        for i, b in enumerate(self.busy_frames):
            if self.carrying[i] is None:
                continue
            self.busy_frames[i] = b - budget
            if self.busy_frames[i] <= 0:
                self.busy_frames[i] = 0
                landed.append(self.carrying[i])
                self.carrying[i] = None
        return landed

    def repair(self, vehicle):
        """Vehicle.RepairVehicle, reached by answering the repair task flood spawns."""
        self.damaged[vehicle] = False


# The same connection cells, keyed by the DISPLAY name the port's own economy uses
# ("Community Charleston"), because BUILDING_CELL above is keyed by GameObject name
# ("Community01") and the port never sees those. Derived by resolving each facility's
# reported position through nearest_road, which reproduced the dumped RoadConnection cell
# for all five prebuilts exactly.
#
# PREBUILTS ONLY. A facility the player constructs sits on a site whose position the port
# does not model, so travel_rounds returns None for it and the caller falls back to
# DEFERRED_LATENCY. That fallback is a fitted constant and should be treated as one; closing
# it needs the site coordinates dumped from Unity the same way the road graph was.
FACILITY_CELL = {
    'Community Amherst': (-10, -3),
    'Community Charleston': (1, 5),
    'Community Trinity': (9, 3),
    'Kitchen Alpha': (-8, -3),
    'Motel': (-5, 4),
}
