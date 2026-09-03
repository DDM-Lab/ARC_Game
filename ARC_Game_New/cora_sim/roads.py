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
from .map_spec import MapSpec

# The shipped scene, and the module-level names every caller already uses. These are ALIASES
# onto MapSpec.default(), not a second copy: the data lives in cora_sim/maps/default.json so
# a different map is a different file rather than a different module. Functions below take an
# optional `spec` and fall back to this one, which keeps the whole existing call surface
# working while the map becomes a parameter.
DEFAULT_MAP = MapSpec.default()

ANCHOR = DEFAULT_MAP.anchor
MOVE_SPEED = DEFAULT_MAP.move_speed
GYM_FIXED_DELTA = DEFAULT_MAP.fixed_delta
SIMULATION_DURATION = DEFAULT_MAP.simulation_duration
TIME_SPEED = DEFAULT_MAP.time_speed
ROUND_SECONDS = DEFAULT_MAP.round_seconds
ROAD_CELLS = DEFAULT_MAP.road_cells
BUILDING_CELL = DEFAULT_MAP.building_cell
FACILITY_CELL = DEFAULT_MAP.facility_cell
SITE_CELL = DEFAULT_MAP.site_cell

from heapq import heappush, heappop


TARGET_FPS = 10.0


# RoadConnection.nearestRoadPosition per building -- the actual A* endpoints. Vehicles
# drive to these, NOT to the building's own position.


def world_to_cell(wx, wy):
    """Tilemap.WorldToCell: floor, since the anchor offsets the centre not the origin."""
    from math import floor
    return (int(floor(wx)), int(floor(wy)))


def cell_to_world(cell, spec=None):
    a = (spec or DEFAULT_MAP).anchor
    return (cell[0] + a, cell[1] + a)


def nearest_road(cell, spec=None):
    """RoadTilemapManager.FindNearestRoadPosition.

    Expanding square out to radius 10, keeping the EUCLIDEAN-nearest road cell and
    breaking ties by visit order (x ascending, then y ascending) because the C# keeps
    the first strictly-smaller distance. Returns the input cell when nothing is found,
    which is what the original does too.
    """
    cells = (spec or DEFAULT_MAP).road_cells
    if cell in cells:
        return cell
    best, best_d2 = cell, None
    for radius in range(0, 11):
        for x in range(-radius, radius + 1):
            for y in range(-radius, radius + 1):
                if radius > 0 and abs(x) < radius and abs(y) < radius:
                    continue
                c = (cell[0] + x, cell[1] + y)
                if c not in cells:
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


def path_length(start, goal, flooded=frozenset(), spec=None):
    """Steps along PathfindingSystem's flood-aware A*, or None when no route exists.

    Only the LENGTH matters: Vehicle.MoveToPosition spends journeyLength/moveSpeed
    seconds on the leg, and the per-frame CheckForFloodCollision never fired in any
    captured episode, so the particular route taken has no other observable effect.
    Length is tie-break independent, which is why this does not have to reproduce the
    C#'s OrderBy(FCost).ThenBy(HCost) node ordering exactly.
    """
    cells = (spec or DEFAULT_MAP).road_cells
    if start not in cells or goal not in cells:
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
            if nb not in cells or nb in flooded:
                continue
            ng = g + 1
            if ng < best.get(nb, 1 << 30):
                best[nb] = ng
                heappush(open_, (ng + h(nb), ng, nb))
    return None


# THE ROUND'S LENGTH, read out of GlobalClock rather than measured.
#
# GymAdvanceRound sets Time.captureDeltaTime = GYM_FIXED_DELTA and StartSimulation runs
# SimulationCoroutine(simulationDuration / (int)currentTimeSpeed). So a round is exactly
# that many GAME SECONDS of simulation, and every frame advances 0.3 of one. Dumped from
# the running game to be sure (round:length): seconds 10, simulationDuration 10, timeSpeed
# 1, fixedDelta 0.3 -- 33.33 simulated frames.
#
# This is why the frame spans I measured earlier ran 34-50 while the simulation is a fixed
# 34: the extra frames are the PLANNING phase, where the client is choosing and the game is
# not advancing. Timing deliveries against those spans was measuring the wrong thing, and
# it is what made the round-boundary cases unfittable.


def leg_seconds(steps, spec=None):
    """How long a leg takes, in GAME SECONDS. The surrogate has no frames and needs none.

    Vehicle.MoveToPosition spends journeyLength/moveSpeed seconds on a leg. Every A* edge
    measures exactly 1.0, so that is steps/moveSpeed.

    The one thing carried over from Unity's frame loop is QUANTISATION, and it is real
    behaviour rather than an artefact of how this was measured: the coroutine advances only
    once per frame, adding a fixed Time.captureDeltaTime of 0.3s each time, and it keeps
    going while elapsed < journeyTime. So a leg actually consumes ceil(journeyTime / 0.3)
    ticks of 0.3s. That is a rounding rule on seconds, not a loop -- the surrogate still
    steps whole rounds.
    """
    from math import ceil
    m = spec or DEFAULT_MAP
    exact = steps / m.move_speed
    return ceil(exact / m.fixed_delta) * m.fixed_delta


class Fleet:
    """DeliverySystem's vehicles, carrying position, occupancy and damage between trips.

    A trip is TWO legs -- Vehicle.RunDelivery drives to the source road connection, loads,
    then drives to the destination -- so a delivery's cost depends on where the assigned
    vehicle last parked, not on the source/destination pair alone. That is why the same
    Kitchen->Community route was measured at 0 rounds one day and 5 the next.
    """

    __slots__ = ("pos", "busy_seconds", "carrying", "damaged", "spec")

    # Kept for callers that reference Fleet.DEPOTS; the live values come from the spec.
    DEPOTS = DEFAULT_MAP.depots

    def __init__(self, spec=None):
        self.spec = spec or DEFAULT_MAP
        self.pos = [nearest_road(c, self.spec) for c in self.spec.depots]
        self.busy_seconds = [0.0, 0.0, 0.0]
        self.carrying = [None, None, None]
        # Flood does not merely delay a vehicle, it DISABLES it: StopVehicleDueToFlood sets
        # isDamaged and the status to Damaged, and IsAvailable() is `status == Idle`, so the
        # vehicle leaves the fleet until RepairVehicle() runs -- which only happens if the
        # player answers the repair task StopVehicleDueToFlood spawns. A flood that cuts one
        # route therefore costs a third of the delivery capacity indefinitely.
        self.damaged = [False, False, False]

    def clone(self):
        f = Fleet.__new__(Fleet)
        f.spec = self.spec
        f.pos = list(self.pos)
        f.busy_seconds = list(self.busy_seconds)
        f.carrying = list(self.carrying)
        f.damaged = list(self.damaged)
        return f

    def available(self):
        """Vehicles that will be free at some point in THIS round.

        A vehicle is not out of action for a whole round just because it took a trip. A
        two-leg Kitchen->Community run is a few seconds against a 10-second round, so one
        vehicle can serve several orders before the round ends -- which is exactly what the
        delivery:queue marks show Unity doing: three food orders created in a round, landing
        across that round and the next rather than one per vehicle per round.
        """
        return [i for i in range(len(self.pos))
                if not self.damaged[i] and self.busy_seconds[i] < self.spec.round_seconds]

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
            speed = self.spec.move_speed
        best, best_score = None, -1.0
        sx, sy = cell_to_world(src_cell, self.spec)
        for i in self.available():
            vx, vy = cell_to_world(self.pos[i], self.spec)
            d = ((vx - sx) ** 2 + (vy - sy) ** 2) ** 0.5
            score = 100.0 / (1.0 + d) + (quantity / capacity) * 50.0 + speed * 10.0
            if score > best_score:
                best, best_score = i, score
        return best

    def soonest_free(self):
        """The undamaged vehicle that frees earliest, and how many SECONDS until it does.

        DeliverySystem does not drop an order when every vehicle is out -- it leaves it in
        pendingTasks and assigns it the moment one lands. So a trip placed against a busy
        fleet costs its wait PLUS its drive, which is still a measured quantity; falling
        back to a fitted constant here threw that away on 51 of the replay's deliveries.
        """
        best, best_wait = None, None
        for i in range(len(self.pos)):
            if self.damaged[i]:
                continue
            wait = max(0.0, self.busy_seconds[i])
            if best_wait is None or wait < best_wait:
                best, best_wait = i, wait
        return best, best_wait

    def free_vehicle(self):
        av = self.available()
        return av[0] if av else None

    def dispatch(self, vehicle, payload, src_cell, dst_cell, flooded=frozenset()):
        """Cost the two legs. Returns False when the flood has cut either one.

        A cut route is NOT a slow delivery and NOT a retry. StopVehicleDueToFlood calls
        RemoveActiveDeliveryTask and nulls currentTask, so the order is dropped outright --
        and the vehicle is left damaged at wherever it had reached, out of the fleet.
        """
        leg1 = path_length(self.pos[vehicle], src_cell, flooded, self.spec)
        if leg1 is None:
            self.damaged[vehicle] = True
            return False
        # The vehicle really is at the source once leg 1 is done, which is where an ABORTED
        # trip leaves it (LoadCargo bails when the source is empty, before leg 2 exists).
        self.pos[vehicle] = src_cell
        leg2 = path_length(src_cell, dst_cell, flooded, self.spec)
        if leg2 is None:
            self.damaged[vehicle] = True
            return False
        # Accumulate: the vehicle starts this trip when its previous one ends, so its clock
        # is cumulative work measured from the start of the current round. Overwriting it
        # (what this did before) meant a vehicle could only ever run ONE trip per round, and
        # the port landed 2 food deliveries where Unity landed 1 because the fleet had three
        # times too much capacity per round in the wrong shape.
        self.busy_seconds[vehicle] = (max(0.0, self.busy_seconds[vehicle])
                                      + leg_seconds(leg1, self.spec)
                                      + leg_seconds(leg2, self.spec))
        self.carrying[vehicle] = payload
        self.pos[vehicle] = dst_cell
        return True

    def advance(self, segment=None):
        """Burn one round's SECONDS; return the payloads that arrived.

        Every round simulates the same simulationDuration/timeSpeed seconds, so there is no
        per-segment budget -- the table that used to be here was fitted to frame spans that
        wrongly included the planning phase.
        """
        landed = []
        for i, b in enumerate(self.busy_seconds):
            self.busy_seconds[i] = max(0.0, b - self.spec.round_seconds)
            if self.carrying[i] is not None and self.busy_seconds[i] <= 0:
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


# Buildable-site id -> road cell, from the gym's own pathfind_matrix (no new instrumentation
# was needed; it already reports every AbandonedSite's position). Without this, a facility
# the PLAYER builds had no location, so its deliveries fell back to the fitted
# DEFERRED_LATENCY -- which was 176 of 247 travel computations on the replay corpus, i.e.
# most of them. Prebuilts are in FACILITY_CELL above.
