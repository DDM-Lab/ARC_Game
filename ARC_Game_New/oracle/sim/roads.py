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
# onto MapSpec.default(), not a second copy: the data lives in oracle/sim/maps/default.json so
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
# delivery:config reports taskAssignmentInterval = 1, in seconds of game time. Dispatch is
# gated on it in DeliverySystem.Update, so a round runs round_seconds / this many passes.
TASK_ASSIGNMENT_INTERVAL = 1.0
ROAD_CELLS = DEFAULT_MAP.road_cells
BUILDING_CELL = DEFAULT_MAP.building_cell
FACILITY_CELL = DEFAULT_MAP.facility_cell
SITE_CELL = DEFAULT_MAP.site_cell

from heapq import heappush, heappop
import struct


TARGET_FPS = 10.0


# RoadConnection.nearestRoadPosition per building -- the actual A* endpoints. Vehicles
# drive to these, NOT to the building's own position.


def world_to_cell(wx, wy):
    """Tilemap.WorldToCell: floor, since the anchor offsets the centre not the origin."""
    from math import floor
    return (int(floor(wx)), int(floor(wy)))


def _building_at(cell, spec):
    """Reverse the name -> road-cell map, so a routing cell can name its building."""
    for name, c in spec.building_cell.items():
        if c == cell:
            return name
    return None


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


# PathfindingSystem's serialized settings in MainScene: straightCost 1, intersectionCost 1.2,
# allowDiagonalMovement 0, preferIntersections 0.
STRAIGHT_COST = 1.0
INTERSECTION_COST = 1.2
_MAX_ITERATIONS = 1000                  # FindPathAStarFloodAware gives up after this many pops

_f32_struct = __import__("struct").Struct("f")


def _f32(x):
    """Round to single precision: PathNode's costs are floats, and the open-list ordering
    compares them exactly. One double op rounded to float equals the float op."""
    return _f32_struct.unpack(_f32_struct.pack(x))[0]


_STRAIGHT32 = _f32(STRAIGHT_COST)
_INTERSECTION32 = _f32(INTERSECTION_COST)
_INTERSECTIONS = {}                     # id(road_cells) -> (road_cells, frozenset of cells)
_ROUTES = {}                            # (start, goal, flooded, id(road_cells)) -> route or None


def _intersections(cells):
    """RoadTilemapManager.IsIntersection: a road cell with 3+ road neighbours."""
    hit = _INTERSECTIONS.get(id(cells))
    if hit is None or hit[0] is not cells:
        hit = (cells, frozenset(c for c in cells
                                if sum((c[0] + dx, c[1] + dy) in cells for dx, dy in _DIRS) >= 3))
        _INTERSECTIONS[id(cells)] = hit
    return hit[1]


def _distance(a, b, inter):
    """CalculateDistance: Manhattan x straightCost, x intersectionCost when `b` is an
    intersection -- the heuristic is the same call with b = the goal."""
    cost = _f32((abs(b[0] - a[0]) + abs(b[1] - a[1])) * _STRAIGHT32)
    return _f32(cost * _INTERSECTION32) if b in inter else cost


def _astar(start, goal, flooded, cells):
    """FindPathAStarFloodAware, step for step.

    NOT A UNIT-COST SHORTEST PATH. Entering an intersection costs 1.2, so among routes of
    one length the search prefers the one through fewer junctions, and it can take a longer
    one to avoid them. The open list is a List searched with OrderBy(FCost).ThenBy(HCost)
    .First() -- stable, so equal (F, H) go to the node added first, and a node whose G
    improves keeps its place. That order is reproduced with (F, H, insertion seq), stale
    heap entries skipped. MCTS plan 7009 s23: Vehicle 3's 36-cell leg to Kitchen_6 runs in
    along y=3 in Unity; the old unit-cost search (ties to the larger G) took an equal-length
    route over the (-7,-1) bridge, where the round's flood stopped it with an alert Unity
    never raised. Plan 7024 s23: the old search gave a 21-cell leg where Unity drove 22.
    """
    inter = _intersections(cells)
    g = {start: 0.0}
    h = {start: _distance(start, goal, inter)}
    parent = {start: None}
    seq = {start: 0}
    open_ = [(_f32(g[start] + h[start]), h[start], 0, start)]
    closed = set()
    n = 1
    it = 0
    while open_ and it < _MAX_ITERATIONS:
        f, hh, sq, cur = heappop(open_)
        if cur in closed or f != _f32(g[cur] + h[cur]):
            continue                                   # superseded by a cheaper G
        it += 1
        closed.add(cur)
        if cur == goal:
            out = [cur]
            while parent[out[-1]] is not None:
                out.append(parent[out[-1]])
            out.reverse()
            return out
        gc = g[cur]
        for dx, dy in _DIRS:
            nb = (cur[0] + dx, cur[1] + dy)
            if nb not in cells or nb in flooded or nb in closed:
                continue
            ng = _f32(gc + _distance(cur, nb, inter))
            if nb not in g:
                g[nb], h[nb], parent[nb], seq[nb] = ng, _distance(nb, goal, inter), cur, n
                n += 1
            elif ng < g[nb]:
                g[nb], parent[nb] = ng, cur
            else:
                continue
            heappush(open_, (_f32(g[nb] + h[nb]), h[nb], seq[nb], nb))
    return None


def path_cells(start, goal, flooded=frozenset(), spec=None):
    """The cells of PathfindingSystem's flood-aware A* route, start first, or None.

    Vehicle.CheckForFloodCollision tests the vehicle's position against the flood tiles on
    every movement frame, so which cells a leg crosses -- not just how many -- decides
    whether a vehicle survives a flood that spreads onto its route at a round boundary.
    5802 step 27: Unity's Vehicle 1 is stopped ten frames into a leg whose remaining cells
    cross a tile that flooded at the end of the previous round.
    """
    cells = (spec or DEFAULT_MAP).road_cells
    if start not in cells or goal not in cells:
        return None
    # A FLOODED START IS ALLOWED. FindPathAStarFloodAware only rejects flooded NEIGHBOURS
    # (GetFloodAwareNeighbors), never the node it starts from, so a vehicle stopped in water
    # by a collision drives out again once repaired. The port refused it: on 5802 Vehicle 1,
    # repaired on the cell that stopped it at step 27, was dispatched at round 29 while that
    # cell was still flooded, judged to have no path, damaged again, and its order dropped --
    # the one food resolution the port had over Unity. A flooded goal stays unreachable,
    # since it can only be entered as a neighbour.
    if start == goal:
        # Before the flood test: FindPathAStarFloodAware pops the start node, finds it is the
        # end and returns, so a same-cell "route" exists even on a flooded cell (uct-f7 7025
        # s15: Shelter_2 and CaseworkSite_8 share flooded road (8, 3); Unity estimated "0
        # tiles" and queued the casework walk, the port refused it).
        return [start]
    if goal in flooded:
        return None
    key = (start, goal, flooded, id(cells))
    hit = _ROUTES.get(key)
    if hit is None and key not in _ROUTES:
        if len(_ROUTES) > 200_000:
            _ROUTES.clear()
        hit = _ROUTES[key] = _astar(start, goal, flooded, cells)
    return hit


def path_length(start, goal, flooded=frozenset(), spec=None):
    """Steps along PathfindingSystem's flood-aware A* (path_cells), or None when no route
    exists. Vehicle.MoveToPosition walks the route node by node, so a leg costs its node
    count -- of the route this search picks, which is not always the fewest steps."""
    route = path_cells(start, goal, flooded, spec)
    return None if route is None else len(route) - 1


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


# STALL FRAMES, measured and NOT currently applied. leg:tick shows that of 159 consecutive
# in-loop frames on seed 5901, 138 advance the shared path index by one and 21 advance it by
# zero -- the inner `while (elapsedTime < journeyTime)` iterating without finishing its
# segment. A cell therefore costs about 159/138 frames rather than 1.
#
# Applying that ratio uniformly fixes 5601 and 5701 (the port stops landing a long
# relocation Unity spills) and BREAKS 5802, which was exact. So the stall is real but it is
# not uniform across legs, and a single multiplier is the wrong shape for it. Left here as a
# measured fact rather than folded into the constant.
STALL_RATIO = 159.0 / 138.0


def leg_seconds(steps, spec=None):
    """How long a leg takes, in GAME SECONDS -- and it is ONE FRAME PER UNIT STEP.

    I had this as journeyLength / moveSpeed, which is what MoveToPosition's arithmetic
    says in isolation. The frame marks say otherwise:

        leg len  5  ->  6 frames        leg len 11 -> 12 frames
        leg len 19 -> 20 frames

    The coroutine walks the path SEGMENT BY SEGMENT, and each segment is one unit long, so
    its journeyTime is 1/moveSpeed = 0.125s. The inner loop is
    `while (elapsedTime < journeyTime) { elapsedTime += Time.deltaTime; yield return null; }`
    and under captureDeltaTime the increment is 0.3 -- BIGGER than the segment's journeyTime.
    So the loop executes exactly once per segment and the vehicle covers one unit per frame,
    no matter what moveSpeed says. moveSpeed only decides whether a segment needs one frame
    or several; at 8 units/s against a 0.3s frame it is always one.

    That is a factor of 2.4 against the old model, and it is the difference between "all
    five orders land this round" and Unity's "two land, three land next round".

    The +1 is measured too: a leg of L steps consumes L+1 frames, the extra one being the
    final snap to the endpoint after the segment loop ends.
    """
    m = spec or DEFAULT_MAP
    per_step = max(m.fixed_delta, 1.0 / m.move_speed)
    # STALL FRAMES. leg:tick samples currentPathIndex on every frame of the movement loop.
    # Of 159 consecutive in-loop frames, 138 advance the index by one and 21 advance it by
    # ZERO -- the inner `while (elapsedTime < journeyTime)` iterating again without finishing
    # its segment. So a cell costs 159/138 frames, not 1.
    #
    # This is measured, not fitted: it is a count of frames from the marks, and it is the
    # overhead that made every port trip slightly cheaper than Unity's. On the long
    # Community03 -> Motel route it turns 9.9s into 11.4s against a 10s round, which is the
    # difference between the port landing that relocation and Unity spilling it.
    # MEASUREMENT CONFLICT, UNRESOLVED -- the (steps + 1) form stands.
    # 203 deliveries in the 32-round captures give a modal leg-mark-to-unload gap of exactly
    # nodes-1 frames, which argues for `steps * per_step`. But the fixtures in test_roads
    # measure leg-mark to NEXT-leg-mark and give len+1, and they FAIL against the shorter
    # form. Both are real measurements of different intervals, and switching was INERT on
    # both the replay counters and the draw stream, so there is no evidence to break the tie
    # and a suite regression against it. Keeping the form the fixtures encode.
    return (steps + 1) * per_step


def round_frames(spec) -> int:
    """Frames in a round's simulation window: SimulationCoroutine adds the gym timestep to a float
    `elapsed` until it reaches the window, in float32 -- 34 at 0.3 s, but 51 (not 50) at 0.2 s,
    where 50 x 0.2f falls a hair short of 10 (endSim 51 frames after round:length in every round
    of the 0.2 s captures)."""
    step = struct.unpack("f", struct.pack("f", spec.fixed_delta))[0]
    target = struct.unpack("f", struct.pack("f", spec.round_seconds))[0]
    elapsed, n = 0.0, 0
    while elapsed < target:
        elapsed = struct.unpack("f", struct.pack("f", elapsed + step))[0]
        n += 1
    return n


def pass_frames(spec) -> int:
    """Frames between DeliverySystem dispatch passes: `Time.time - lastTaskAssignment >
    taskAssignmentInterval`, strictly greater, so the first whole number of timesteps EXCEEDING
    the interval -- 4 at 0.3 s (1.2 s) and 6 at 0.2 s (1.0 s is not > 1.0; the 0.2 s captures
    dispatch 6 frames apart)."""
    return int(TASK_ASSIGNMENT_INTERVAL / spec.fixed_delta + 1e-9) + 1


class Fleet:
    """DeliverySystem's vehicles, carrying position, occupancy and damage between trips.

    A trip is TWO legs -- Vehicle.RunDelivery drives to the source road connection, loads,
    then drives to the destination -- so a delivery's cost depends on where the assigned
    vehicle last parked, not on the source/destination pair alone. That is why the same
    Kitchen->Community route was measured at 0 rounds one day and 5 the next.
    """

    __slots__ = ("co_order", "drop_veh", "export_status", "export_unloading", "pos", "busy_seconds", "carrying", "damaged", "spec", "frame", "trip", "events", "aborted", "stuck",
                 "pauses", "late_dropped", "day_end", "rounds", "spawn_dx",
                 "drop_frames", "first_land_frame", "land_frames")

    # Kept for callers that reference Fleet.DEPOTS; the live values come from the spec.
    DEPOTS = DEFAULT_MAP.depots

    def __init__(self, spec=None):
        self.drop_veh = {}                 # id(payload) -> (payload, vehicle) for the blockage alert
        self.co_order = []                 # delivery coroutines in Unity's per-frame resume order
        self.export_status = None          # vehicle statuses as Unity exports them (after +35)
        self.export_unloading = []         # deliveries unloaded on +35, completed (reservations freed) on +36
        self.spec = spec or DEFAULT_MAP
        self.pos = [nearest_road(c, self.spec) for c in self.spec.depots]
        n = len(self.pos)                    # one vehicle per depot (MapConfigApplier spawns them)
        self.busy_seconds = [0.0] * n
        self.carrying = [None] * n
        # Flood does not merely delay a vehicle, it DISABLES it: StopVehicleDueToFlood sets
        # isDamaged and the status to Damaged, and IsAvailable() is `status == Idle`, so the
        # vehicle leaves the fleet until RepairVehicle() runs -- which only happens if the
        # player answers the repair task StopVehicleDueToFlood spawns. A flood that cuts one
        # route therefore costs that vehicle's share of the delivery capacity indefinitely.
        self.damaged = [False] * n
        self.frame = 0                       # cumulative SIM frames since the game began
        # DIAGNOSTIC ONLY (validation harness): Unity's per-round PAUSED planning frames,
        # popped one per round. Time.time advances through the pause, so AssignPendingTasks
        # keeps its 4-frame cadence across it and the dispatch offset inside each round
        # depends on how long the pause was -- which is agent latency, not game state. Left
        # empty in normal use, where the counter simply advances 34 a round.
        self.pauses = None
        self.trip = [None] * n               # per-vehicle in-flight state, see run_round
        self.events = None                   # set to a list to record (frame, kind, veh, id)
        self.aborted = []                    # payloads abandoned by an empty-source load this round
        self.stuck = []                      # deliveries cancelled INSIDE AssignDeliveryTask (see run_round)
        self.late_dropped = []               # vehicles blocked on the +36 paused frame (see run_epilogue)
        self.day_end = False                 # this round is the day's last (see run_round's unload)
        self.rounds = 0                      # run_round calls so far: which round a leg started in
        # MapConfigApplier.SpawnVehicle shifts a vehicle on a purely VERTICAL road half a cell
        # left (and turns it); the transform keeps that offset until the vehicle first drives,
        # and DeliverySystem scores vehicles on transform distance. Unity's first legs start at
        # (-3.0, 3.5), (-4.0, -2.5), (10.5, -2.5), (8.0, 7.5) where the cell centres are
        # (-2.5, 3.5), (-3.5, -2.5), (10.5, -2.5), (8.5, 7.5). Zeroed at the first dispatch.
        cells = self.spec.road_cells
        def _vertical_only(c):
            vert = (c[0], c[1] + 1) in cells or (c[0], c[1] - 1) in cells
            horiz = (c[0] - 1, c[1]) in cells or (c[0] + 1, c[1]) in cells
            return vert and not horiz
        # The test reads the map config's road grid at the vehicle's own grid cell, which is
        # one row below its depot cell here (the same half-cell the spawn drops in y);
        # (depot.x, depot.y - 1) reproduces all four measured spawns.
        self.spawn_dx = [-0.5 if _vertical_only((d[0], d[1] - 1)) else 0.0 for d in self.spec.depots]
        # Per run_round: the frame of each dropped entry (aligned with the dropped list) and of
        # the round's first landing, so the board can handle a drop AFTER the landings that
        # preceded it in frame order (see TaskBoard.tick_deliveries_only).
        self.drop_frames = []
        self.first_land_frame = None
        self.land_frames = []

    def clone(self):
        f = Fleet.__new__(Fleet)
        f.drop_veh = dict(getattr(self, 'drop_veh', {}))
        f.co_order = list(getattr(self, 'co_order', []))
        f.export_status = list(self.export_status) if getattr(self, 'export_status', None) is not None else None
        f.export_unloading = list(getattr(self, 'export_unloading', []))
        f.spec = self.spec
        f.pos = list(self.pos)
        f.busy_seconds = list(self.busy_seconds)
        f.carrying = list(self.carrying)
        f.damaged = list(self.damaged)
        f.frame = self.frame
        f.pauses = self.pauses
        f.trip = [dict(t) if t else None for t in self.trip]
        f.events = None
        f.aborted = list(self.aborted)
        f.stuck = list(self.stuck)
        f.late_dropped = list(self.late_dropped)
        f.day_end = self.day_end
        f.rounds = self.rounds
        f.spawn_dx = list(self.spawn_dx)
        f.drop_frames = list(self.drop_frames)
        f.first_land_frame = self.first_land_frame
        f.land_frames = list(self.land_frames)
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
            vx += self.spawn_dx[i]
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


    def run_round(self, pending, flooded=frozenset(), load=None, source_pos=None):
        """One simulated round, FRAME BY FRAME. Returns (landed, still_pending, dropped).

        Everything here is calibrated against the delivery marks of eleven 32-round captures
        (352 rounds, 203 trips), and the rules are all integers:

          * a round is ceil(round_seconds / fixed_delta) sim frames -- 34, in every one of
            the 352 rounds -- and the paused planning frames between rounds move nothing, so
            a vehicle mid-leg at frame 34 simply resumes at frame 1 of the next round;
          * AssignPendingTasks is gated on `Time.time - last > 1s` in Update, and Time.time
            advances 0.3s per sim frame, so passes fire every 4 frames on a schedule that is
            continuous across rounds: at cumulative sim frames = 0 (mod 4). A round whose
            cumulative start is = 2 dispatches at offsets 2, 6, 10, ...; one at = 0 at
            4, 8, 12, ... -- the 77/20 split of first-dispatch offsets in the captures;
          * the source leg starts ON the dispatch frame and takes exactly L1 frames; loading
            is one frame (arrival -> destination-leg start = +1); the destination leg takes
            exactly L2 frames to the unload; complete is +1 and the vehicle is idle on that
            frame. A vehicle already standing on its source starts its destination leg at
            +2. Unload - dispatch - L2 = 2 on 26 of 26 at-source trips.
          * a pass scores every idle vehicle with CalculateVehicleSuitability and each takes
            at most one order; a vehicle freed at frame f is a candidate for a pass at f.

        The previous model ran on seconds with whole-second passes from t = 0, charged
        (L + 1) per leg, and used a 33-frame round. It was ~1.5s fast per trip, which is why
        the port's second kitchen load on 5501 round 9 fit inside the round and Unity's
        landed one frame past endSim -- and why every remaining food and lodging residue had
        the shape of one delivery landing a round early or late.

        Two things the old model did that Unity does not, both dropped: a src->dst route
        check at dispatch (Unity computes the destination path only when that leg starts,
        after loading) and a "zombie speed-up" hand-off on a load abort (a guess; the pass
        cadence covers it -- an aborted vehicle is idle at the source and the next pass,
        at most four frames away, reassigns it).

        `pending` is a list of [seq, payload, src_cell, dst_cell, qty], sorted by the
        caller (priority desc, then creation order, as DeliverySystem does).
        """
        self.export_status = None
        self.export_unloading = []
        from math import ceil
        frames = round_frames(self.spec)
        pass_every = pass_frames(self.spec)
        queue = list(pending)
        landed, dropped = [], []
        self.rounds += 1
        self.drop_frames, self.first_land_frame, self.land_frames = [], None, []
        def _stamp(frame):
            self.drop_frames.extend([frame] * (len(dropped) - len(self.drop_frames)))
            self.land_frames.extend([frame] * (len(landed) - len(self.land_frames)))
            if self.first_land_frame is None and landed:
                self.first_land_frame = frame
        # COROUTINE RESUME ORDER (the movement loop below runs vehicles in it). Unity's leg:tick
        # lines give it frame by frame: a dispatched delivery's coroutine (StartCoroutine from the
        # Update pass) resumes AHEAD of every coroutine already running from the next frame on;
        # several dispatched on one pass keep their dispatch order; a destination leg keeps its
        # vehicle's place. That reproduces 30,844 of 30,845 multi-vehicle frames in 95 cluster
        # logs. Two stops on one frame alert in it (uct-f6 7011 f847: Vehicle 4, then Vehicle 1).
        co = getattr(self, "co_order", None)
        if co is None:
            co = self.co_order = []
        for _o in range(frames):
            if _o:
                _stamp(self.frame)
            self.frame += 1
            _front = []
            def _co_join(v):
                if v in co:
                    co.remove(v)
                _front.append(v)
            # -- dispatch pass, BEFORE this frame's movement --------------------------------
            # AssignPendingTasks runs in Update; coroutines resume after Update in the same
            # frame. So a pass at frame f sees the vehicles as they stood at the end of
            # f-1: one that completes during frame f is not a candidate until the next pass.
            # The captures' complete-to-next-dispatch gaps are 1..4 frames and never 0. With
            # the order reversed, 5802's Vehicle 1 was dispatched on the frame it completed
            # and took the relocation Unity's Vehicle 3 -- idle two frames later and closer
            # -- actually carried.
            if queue and self.frame % pass_every == 0:
                ready = [i for i in range(len(self.pos))
                         if (self.trip[i] is None or "race_ready" in self.trip[i])
                         and not self.damaged[i]]
                while queue and ready:
                    seq, payload, src, dst, qty = queue[0]
                    v = self._closest(ready, src, qty,
                                      src_pos=source_pos(payload) if source_pos else None)
                    if self.trip[v] is not None and self.trip[v].get("race_ready") == self.frame:
                        # THE RACE: two coroutines advance one shared currentPathIndex. The
                        # source leg is skipped, the destination leg runs at two cells a
                        # frame, and the nominal cargo is unloaded although nothing was ever
                        # picked up. Measured on every instance in the captures; a Unity bug
                        # the surrogate reproduces because the game has it.
                        leg2 = path_length(self.pos[v], dst, flooded, self.spec)
                        if leg2 is None:
                            self.damaged[v] = True
                            dropped.append((payload, qty))       # loaded at the source: packs aboard
                            self.drop_veh[id(payload)] = (payload, v)
                            self.trip[v] = None
                            ready.remove(v)
                            queue.pop(0)
                            continue
                        # The cargo IS aboard: 5802 s6, Vehicle3 aborted at f307, raced at
                        # f308 and unloaded "nominal 100, actual 100" from Community01 at
                        # f317, with Community01 down 100. LoadCargo ran, so the source is
                        # debited here, on the race frame (there is no source leg).
                        got = load(payload, qty) if load is not None else qty
                        if got <= 0:
                            self.trip[v] = {"race_ready": self.frame + 1}
                            self.aborted.append(payload)
                            if self.events is not None: self.events.append((self.frame, "abort", v, payload[0]))
                            ready.remove(v)
                            queue.pop(0)
                            continue
                        self.trip[v] = {"payload": payload, "src": src, "dst": dst, "qty": qty,
                                        "phase": "to_dst", "left": max(1, -(-leg2 // 2)) + 1,
                                        "aboard": got, "disp_round": self.rounds}
                        self.carrying[v] = payload
                        self.spawn_dx[v] = 0.0
                        _co_join(v)
                        if self.events is not None: self.events.append((self.frame, "race", v, payload[0], leg2))
                        ready.remove(v)
                        queue.pop(0)
                        continue
                    self.trip[v] = None
                    leg1 = path_length(self.pos[v], src, flooded, self.spec)
                    if leg1 is None:
                        # No flood-free path to the source: blocked on the dispatch frame. This
                        # happens INSIDE AssignDeliveryTask, before AssignPendingTasks adds the
                        # task to activeTasks -- so the cancel finds nothing to remove and the
                        # dead delivery is then added for good: its quantity stays reserved,
                        # incoming at the destination and outgoing at the source (MCTS plan 7028
                        # s24: "100 meals already inbound" for a delivery cancelled the round
                        # before). A Unity bug the surrogate reproduces because the game has it.
                        self.damaged[v] = True
                        dropped.append((payload, 0))         # never reached the source: nothing aboard
                        self.drop_veh[id(payload)] = (payload, v)
                        self.stuck.append((payload, qty))
                        ready.remove(v)
                        queue.pop(0)
                        continue
                    # +1: the pass runs before this frame's movement, which then consumes one
                    # frame of the leg; the calibrated costs (L1 to arrival, +2 for an
                    # at-source vehicle) are measured from the dispatch frame.
                    if leg1 == 0:
                        # Already on the source: the 1-node MoveToPosition returns at once,
                        # the boundary costs a frame, LoadCargo runs at +1, the destination
                        # leg starts at +2 (26 of 26 at-source trips).
                        trip = {"payload": payload, "src": src, "dst": dst, "qty": qty,
                                "phase": "to_src", "left": 1 + 1, "disp_round": self.rounds}
                    else:
                        trip = {"payload": payload, "src": src, "dst": dst, "qty": qty,
                                "phase": "to_src", "left": leg1 + 1, "disp_round": self.rounds,
                                "path": path_cells(self.pos[v], src, flooded, self.spec)}
                    self.trip[v] = trip
                    self.spawn_dx[v] = 0.0
                    _co_join(v)
                    if self.events is not None: self.events.append((self.frame, "dispatch", v, payload[0], leg1))
                    ready.remove(v)
                    queue.pop(0)
            # -- advance every vehicle in flight by one frame ---------------------------
            if _front:
                co[:] = _front + [x for x in co if x not in _front]
            _order = [x for x in co if x < len(self.trip)]
            _order += [x for x in range(len(self.trip)) if x not in _order]
            for v in _order:
                t = self.trip[v]
                if t is None:
                    continue
                if "race_ready" in t:
                    # An aborted vehicle whose pass frame has passed is simply idle.
                    if t["race_ready"] < self.frame:
                        self.trip[v] = None
                    continue
                t["left"] -= 1
                # Whether this trip's endSim-frame move happened AFTER the clock (dispatched in
                # an earlier round): the flood update then still sees it one cell back.
                # A leg that STARTED in the paused frames (run_epilogue's boarding branch) already
                # carries that shift in its +1 start frame; correcting it again put a vehicle
                # cancelled overnight one cell short (rules-leaf 7015 s31: Vehicle 3 at index 33
                # of 40 on f1135, cancelled at f1136 on path cell 34, where the frame count
                # already had it).
                # mac_raw: any after-clock endSim move (the overnight cancel's test);
                # moved_after_clock exempts a leg begun in the paused frames (the flood stop's).
                t["mac_raw"] = (_o == frames - 1 and t["left"] > 0
                                and t.get("disp_round", 0) < self.rounds)
                t["moved_after_clock"] = t["mac_raw"] and not t.get("epi_leg")
                if t["left"] > 0:
                    # A MOVEMENT FRAME: the vehicle has just entered the next cell of its leg.
                    # CheckForFloodCollision runs after every lerp step; the flood set is
                    # constant within a round, so this fires the frame a vehicle enters the
                    # first tile that flooded onto its route at the last round boundary.
                    path = t.get("path")
                    # On the DAY'S last frame a food trip dispatched in an earlier round resumes
                    # after the clock, whose CancelIncompleteFoodDeliveries has already cancelled
                    # it: it never drives into the water (rhea 64000 7014 s26: Vehicle 2 on its
                    # pickup leg "cancelled" at f1030 = endSim; the port stopped it on a flooded
                    # cell there). cancel_overnight_food takes it one cell back (moved_after_clock).
                    cancelled_first = (self.day_end and _o == frames - 1
                                       and t.get("disp_round", 0) < self.rounds
                                       and str(t["payload"][2] or "").startswith("__food__"))
                    if path is not None and not cancelled_first and t["moved_after_clock"]:
                        # An after-clock move on the endSim frame happens after the round end and
                        # the flood update, so its collision check sees the UPDATED flood and
                        # comes after that update's stops and the round-end alerts (rhea 250 7027
                        # s29: Vehicle 3 stopped in the flood update at f1004, then Vehicle 1,
                        # dispatched the round before, "delivery:blocked collision" after it).
                        # run_epilogue checks it.
                        t["clock_collision"] = True
                    elif path is not None and not cancelled_first:
                        idx = len(path) - t["left"]
                        if 0 < idx < len(path) and path[idx] in flooded:
                            self.pos[v] = path[idx]
                            self.damaged[v] = True
                            dropped.append((t["payload"], t.get("aboard", t["qty"]) if t["phase"] == "to_dst" else 0))
                            self.drop_veh[id(t["payload"])] = (t["payload"], v)
                            self.carrying[v] = None
                            self.trip[v] = None
                            if self.events is not None: self.events.append((self.frame, "collision", v, t["payload"][0], path[idx]))
                    continue
                ph = t["phase"]
                if ph == "to_src":
                    # ARRIVAL FRAME. MoveToPosition returns and `yield return
                    # StartCoroutine(LoadCargo())` runs LoadCargo synchronously in this same
                    # frame -- it has no yields -- so the load, or the abort that sets the
                    # vehicle Idle, happens HERE. Only the next leg pays the coroutine
                    # boundary: the destination leg starts on the following frame. That
                    # single frame is what decides the race (see the pass).
                    self.pos[v] = t["src"]
                    if self.events is not None: self.events.append((self.frame, "at_src", v, t["payload"][0]))
                    got = load(t["payload"], t["qty"]) if load is not None else t["qty"]
                    if got <= 0:
                        # Idle from this frame; the aborted ExecuteDeliveryTask only resumes
                        # next frame. A pass on that next frame that hands this vehicle a task
                        # first wakes the old coroutine into the race; otherwise it exits and
                        # the next dispatch is an ordinary trip. 5802 step 6: abort +21, pass
                        # +22, race (9 cells in 5 frames). 5901 step 10: abort +20, no pass
                        # at +21, normal trip landing the following step.
                        self.trip[v] = {"race_ready": self.frame + 1}
                        self.aborted.append(t["payload"])
                        if self.events is not None: self.events.append((self.frame, "abort", v, t["payload"][0]))
                        continue
                    t["phase"], t["left"], t["aboard"] = "boarding", 1, got
                elif ph == "boarding":
                    if _o == frames - 1 and t.get("disp_round", 0) < self.rounds:
                        # A trip dispatched in an earlier round resumes after the clock on the
                        # endSim frame, so the destination leg it starts there is routed on the
                        # flood as the update left it: hand it to run_epilogue's boarding branch
                        # (uct-f6e 7010 s30: Vehicle 2's leg from Kitchen_1 marked at f1068 with
                        # an empty flood and 24 cells; routed on the old water it was 48 and
                        # landed a round late).
                        t["left"] = 1
                        t["endsim_leg"] = True
                        continue
                    leg2 = path_length(t["src"], t["dst"], flooded, self.spec)
                    if leg2 is None:
                        # No flood-free path for the destination leg: StopVehicleDueToFlood
                        # -> HandleDeliveryFailure. Damaged, order gone.
                        self.damaged[v] = True
                        dropped.append((t["payload"], t.get("aboard", t["qty"])))     # loaded, no destination leg
                        self.drop_veh[id(t["payload"])] = (t["payload"], v)
                        self.trip[v] = None
                        continue
                    t["phase"], t["left"] = "to_dst", max(1, leg2)
                    t["path"] = path_cells(t["src"], t["dst"], flooded, self.spec)
                    self.carrying[v] = t["payload"]
                    if self.events is not None: self.events.append((self.frame, "leg2", v, t["payload"][0], leg2))
                elif ph == "to_dst":
                    if (self.day_end and _o == frames - 1 and t.get("disp_round", 0) < self.rounds
                            and str(t["payload"][2] or "").startswith("__food__")):
                        # ...EXCEPT A CARRIED-OVER FOOD TRIP ON THE DAY'S LAST FRAME. EndSimulation
                        # runs inside GlobalClock's window coroutine and its OnSimulationEnded runs
                        # CancelIncompleteFoodDeliveries, which spares only deliveries whose
                        # UnloadCargo has already set deliveredQuantity. Whether the vehicle's
                        # RunDelivery coroutine resumes before or after the clock on that frame is
                        # decided by when it was STARTED: of 198 unloads on an endSim frame in 400
                        # captures, all 173 dispatched this round unloaded before endSim:enter and
                        # all 25 dispatched in an earlier round after it -- vehicle and leg timing
                        # do not matter. On the day's last round the late ones are cancelled
                        # (rhea 7005 s26 Vehicle 1, one cell short at f946, "cancelled" at f947;
                        # 7024 s26 Vehicle 4). Left in transit for cancel_overnight_food.
                        t["left"] = 1
                        continue
                    self.pos[v] = t["dst"]
                    # An unload on the round's LAST movement frame (+34) is split across the
                    # segment boundary: UnloadCargo -> HandlePopulationDelivery registers
                    # the actual group now, but OnVehicleDeliveryCompleted fires on the
                    # completion frame (+35), after the advance, generation and flood. The
                    # "split" tag lets step_round register the nominal group late. Measured
                    # on the 5901 validation run: Unity unload f321, complete f322 = d2r2.
                    if _o == frames - 1 and t.get("disp_round", 0) < self.rounds:
                        # A trip dispatched in an earlier round resumes AFTER the clock on the
                        # endSim frame (see the day-end case above): its unload follows
                        # EndSimulation's OnRoundEnd, segment advance and flood -- the epilogue's
                        # order, not the split's.
                        landed.append(tuple(t["payload"]) + ("late34",))
                    elif _o == frames - 2 and t.get("disp_round", 0) < self.rounds:
                        # One frame earlier, the same split: the completion step runs on the
                        # endSim frame, and a trip dispatched in an earlier round resumes after
                        # the clock there -- so the parent is still pending through the segment
                        # advance's depletion roll and generation pass (uct 7021 s14: Vehicle 1
                        # unloaded at Amherst at f509, endSim f510, then "Delivery 3 completed"
                        # and task 33 Completed; the roll in between found it pending).
                        landed.append(tuple(t["payload"]) + ("split33",))
                    else:
                        landed.append(tuple(t["payload"]) + ("split",) if _o == frames - 1
                                      else t["payload"])          # UnloadCargo, this frame
                    if self.events is not None: self.events.append((self.frame, "unload", v, t["payload"][0]))
                    self.carrying[v] = None
                    t["phase"], t["left"] = "complete", 1
                elif ph == "complete":
                    self.trip[v] = None                        # CompleteDelivery -> Idle
        _stamp(self.frame)
        # busy_seconds is kept for callers that read it: frames still to run, in seconds.
        for v, t in enumerate(self.trip):
            self.busy_seconds[v] = (t["left"] * self.spec.fixed_delta) if (t and "left" in t) else 0.0
        if self.pauses:
            self.frame += self.pauses.pop(0)   # burn the pause: no movement, cadence only
        return landed, queue, dropped

    def settle_clock_collisions(self, flooded) -> list:
        """The after-clock endSim moves run_round deferred: CheckForFloodCollision on the cell each
        entered, against the flood as the update left it -- after the update's own stops, before
        the +35 expiry (rhea 250 7027 s29: Vehicle 1's collision alert 74 between the flood stop's
        73 and the expired Amherst request's follow-up 75). [(payload, cargo aboard)]."""
        dropped = []
        for v, t in sorted(enumerate(self.trip), key=lambda vt: self.co_order.index(vt[0])
                           if vt[0] in self.co_order else len(self.trip) + vt[0]):
            if not (t and t.pop("clock_collision", False) and "path" in t):
                continue
            path = t["path"]
            idx = len(path) - t["left"]
            if 0 < idx < len(path) and path[idx] in flooded:
                self.pos[v] = path[idx]
                self.damaged[v] = True
                dropped.append((t["payload"], t.get("aboard", t["qty"]) if t["phase"] == "to_dst" else 0))
                self.drop_veh[id(t["payload"])] = (t["payload"], v)
                self.carrying[v] = None
                self.trip[v] = None
                if self.events is not None: self.events.append((self.frame, "collision", v, t["payload"][0], path[idx]))
        return dropped

    def settle_zero_unloads(self) -> list:
        """The zero-length destination legs run_epilogue parked: their unloads, after the export.
        [(task_id, quantity, destination)] for the arrival processing."""
        out = []
        for v, t in enumerate(self.trip):
            if t is not None and t.get("phase") == "zero_unload":
                out.append(tuple(t["payload"]))
                self.carrying[v] = None
                self.trip[v] = None
        return out

    def settle_late_drops(self) -> list:
        """The +36 blocks run_epilogue held back: the vehicles stop now. [(payload, aboard)]."""
        out = []
        for v, payload, aboard in self.late_dropped:
            self.drop_veh[id(payload)] = (payload, v)
            self.damaged[v] = True
            self.trip[v] = None
            out.append((payload, aboard))
        self.late_dropped = []
        return out

    def run_epilogue(self, flooded=frozenset(), load=None):
        """The two paused frames after the round, run by the caller AFTER the round's
        invoke and flood update -- that is when Unity runs them. Returns (landed, dropped).

        A destination leg that starts here is routed against the UPDATED flood: 5801
        s14, Vehicle2 reaches Kitchen_0 on +35 (f671, the endSim/flood frame) and its
        leg mark at f672 is a 17-cell route through cells that were flooded until that
        update; the port routed it on the pre-update set, took a 25-cell detour and was
        stopped by the water that the update had moved into its way.
        """
        landed, dropped = [], []
        # THE COLLISION CHECK AGAINST THE UPDATED FLOOD. On the frame after EndSimulation every
        # vehicle mid-leg resumes MoveToPosition: the finished segment's loop exits, the next
        # one starts, and CheckForFloodCollision tests transform.position -- still the cell
        # just reached -- against the flood as the update left it, however the tile got
        # there. stop_flooded (CheckForBlockedVehicles) only runs on the expansions that call
        # AddFloodTile, so a vehicle on a tile flooded any other way drove on in the port
        # (rules-leaf 7016 s18: Vehicle 1 at index 10 of 13 at f604, (8,4) flooded in that
        # update, "delivery:blocked collision" at f605). A vehicle on its LAST segment has no
        # next segment to start, so it is not checked here.
        dropped.extend(self.settle_clock_collisions(flooded))
        for v, t in enumerate(self.trip):
            if (not t or t.get("phase") not in ("to_src", "to_dst") or "path" not in t
                    or t["left"] < 2):
                continue
            path = t["path"]
            idx = len(path) - t["left"]
            if 0 <= idx < len(path) and path[idx] in flooded:
                self.pos[v] = path[idx]
                self.damaged[v] = True
                dropped.append((t["payload"], t.get("aboard", t["qty"]) if t["phase"] == "to_dst" else 0))
                self.drop_veh[id(t["payload"])] = (t["payload"], v)
                self.carrying[v] = None
                self.trip[v] = None
                if self.events is not None: self.events.append((self.frame + 1, "collision", v, t["payload"][0], path[idx]))
        # Time.time stops at frame 34, so nothing moves and no pass fires, but coroutines
        # still step once per frame. MoveToPosition's loop-exit check runs on the frame AFTER
        # the last movement, so a leg whose last movement was frame 34 arrives -- and unloads
        # -- at +35, and its completion lands at +36; a destination leg can also START there.
        # The captures show unloads at +35 and completes and leg marks at +36, never movement.
        # Without this, 5601's race relocation (last movement +34) was credited a round late.
        for _e in range(2):
            self.frame += 1
            for v, t in enumerate(self.trip):
                if t is None or "race_ready" in t:
                    if t is not None and t["race_ready"] < self.frame:
                        self.trip[v] = None
                    continue
                if t["left"] != 1:
                    continue                                   # would need movement
                ph = t["phase"]
                if ph == "to_src":
                    if load is None:
                        continue
                    # A vehicle that DROVE onto its source arrives on the paused frame (+35)
                    # and LoadCargo runs synchronously there, before this decision's state is
                    # exported (MCTS plan 7027 s8: Vehicle 4's 31-cell leg from f362 loads at
                    # f393, Kitchen_11 200 -> 100 in that decision's state). So does one
                    # dispatched while STANDING on its source on the round's last pass: it loads
                    # after the advance (7002 validation: "loading cargo" after "Kitchen_14
                    # produced 100 FoodPacks this round") but still on +35, before the export
                    # (uct-f7 64000 7014 s19: dispatched at f684 = endSim, "Kitchen_2 lost 100"
                    # at f685, the leg at f686; the kitchen reads 0 in that decision's state).
                    self.pos[v] = t["src"]
                    if self.events is not None: self.events.append((self.frame, "at_src", v, t["payload"][0]))
                    got = load(t["payload"], t["qty"])
                    if got <= 0:
                        self.trip[v] = {"race_ready": self.frame + 1}
                        self.aborted.append(t["payload"])
                        if self.events is not None: self.events.append((self.frame, "abort", v, t["payload"][0]))
                        continue
                    t["phase"], t["left"], t["aboard"] = "boarding", 1, got
                    continue
                t["left"] = 0
                if ph == "boarding":
                    leg2 = path_length(t["src"], t["dst"], flooded, self.spec)
                    if leg2 is None and _e == 1:
                        # Blocked on +36, AFTER the decision's state is exported (between +35 and
                        # +36): the stop, its alert and the parent's failure belong to the next
                        # decision's state (qwen3.5-4b 7031 d5: Vehicle 3 loads at Kitchen_4 on
                        # f390, "delivery:blocked nopath" at f391, the alert absent from that
                        # decision's state). settle_late_drops() applies it before the next action.
                        self.late_dropped.append((v, t["payload"], t.get("aboard", t["qty"])))
                        t["phase"], t["left"] = "late_drop", 0
                        continue
                    if leg2 is None:
                        self.damaged[v] = True
                        dropped.append((t["payload"], t.get("aboard", t["qty"])))     # loaded, no destination leg
                        self.drop_veh[id(t["payload"])] = (t["payload"], v)
                        self.trip[v] = None
                        continue
                    # A leg started in a paused frame makes no movement until the next
                    # round's frame 1, so its first movement frame is lost: one extra.
                    if leg2 == 0:
                        # A ZERO-LENGTH destination leg (source and destination share a road
                        # cell) needs no movement: the 1-node MoveToPosition returns at once and
                        # UnloadCargo runs while the game is still paused, so the vehicle is free
                        # for the next round's first pass (rules-leaf 7022 s33: Vehicle 1's 0-cell
                        # leg marked at f1222, unloaded at f1223, dispatched again at f1228; the
                        # port spent two movement frames of the next round on it).
                        # That unload is a frame after the leg mark, so AFTER the state export
                        # (+35/+36): the meals are not in that decision's state or its consumed
                        # count (7022 s33: foodPacksConsumed 2414 there, the port's 2489 counted
                        # Vehicle 1's 75 a decision early). settle_zero_unloads lands it before
                        # the next decision's first action.
                        self.pos[v] = t["dst"]
                        self.carrying[v] = t["payload"]
                        t["phase"], t["left"] = "zero_unload", 0
                        if self.events is not None: self.events.append((self.frame, "unload", v, t["payload"][0]))
                        continue
                    t["phase"], t["left"] = "to_dst", max(1, leg2) + 1
                    # Only a leg handed over from the endSim frame (run_round's boarding hold)
                    # started there; one started on a paused frame (+36: 7025 s25 leg mark at f931,
                    # endSim f929, no movement until the next round) keeps the after-clock shift.
                    t["epi_leg"] = t.pop("endsim_leg", False)
                    t["path"] = path_cells(t["src"], t["dst"], flooded, self.spec)
                    if t["epi_leg"] and _e == 0 and len(t["path"]) > 1:
                        # A leg Unity marked ON the endSim frame (after the clock) takes its first
                        # cell on +35 despite the pause; one marked on a paused frame does not
                        # (reuse 7007 s24: Vehicle 3's leg at f886 = endSim is at idx 1 on f887,
                        # Vehicle 4's leg at f887 sits at idx 0 until f899; they unload at f915 and
                        # f916, and Vehicle 3 is free for the f917 dispatch pass).
                        t["left"] -= 1
                        self.pos[v] = t["path"][1]
                    self.carrying[v] = t["payload"]
                    if self.events is not None: self.events.append((self.frame, "leg2", v, t["payload"][0], leg2))
                elif ph == "to_dst":
                    self.pos[v] = t["dst"]
                    # An epilogue unload happens AFTER the round's invoke and flood update.
                    # Tagged so the caller moves its people and spawns its clients at the end
                    # of the step rather than at the head: Unity's client draws for such a
                    # delivery follow the flood draws (5601 step 30, 6101 step 11), and the
                    # group therefore sits behind that step's caseworkGen rolls.
                    landed.append(tuple(t["payload"]) + ("late",))
                    if self.events is not None: self.events.append((self.frame, "unload", v, t["payload"][0]))
                    self.carrying[v] = None
                    t["phase"], t["left"] = "complete", 1
                elif ph == "complete":
                    self.trip[v] = None
            if _e == 0:
                # THE STATE IS EXPORTED BETWEEN +35 AND +36: GymServerManager's Update on +36 runs
                # before that frame's coroutines. A vehicle that unloaded on +35 is still
                # Unloading (CompleteDelivery is +36), one that reached its source on +35 still
                # Loading (its leg starts +36): reuse 7007 s29, Vehicle 3 unloaded at f1059
                # (endSim f1058) and read "Unloading" in the next decision's state, so
                # availableVehicles was 1 where the port, past both frames, counted 2.
                self.export_status = [
                    "Damaged" if self.damaged[i]
                    else "Unloading" if (t2 and t2.get("phase") == "complete")
                    else "Loading" if (t2 and t2.get("phase") == "boarding")
                    else "Idle" if (t2 is None or "race_ready" in t2) and self.carrying[i] is None
                    else "InTransit"
                    for i, t2 in enumerate(self.trip)]
                # ...and such a delivery is still Active, so its reservations (outgoing at the
                # kitchen, incoming at the destination) are in that state too (uct-f6 7009 d15:
                # Vehicle 1 Unloading 80 meals Kitchen_7 -> Motel, Kitchen Echo's 20 read as
                # "already scheduled for other deliveries").
                self.export_unloading = [tuple(t2["payload"]) for t2 in self.trip
                                         if t2 and t2.get("phase") == "complete" and "payload" in t2]
        self.frame -= 2                                        # sim frames only, for the pass phase
        for v, t in enumerate(self.trip):
            self.busy_seconds[v] = (t["left"] * self.spec.fixed_delta) if (t and "left" in t) else 0.0
        return landed, dropped

    def _closest(self, candidates, src_cell, quantity=0, capacity=100.0, src_pos=None):
        """CalculateVehicleSuitability among a set of already-free vehicles. `src_pos` is the
        source building's transform when the caller knows it (a building constructed this
        episode, which the map dump's building_pos does not carry)."""
        best, best_score = candidates[0], -1.0
        # SCORE AGAINST THE BUILDING TRANSFORM, NOT THE ROAD CELL. Unity scores
        # Vector3.Distance(vehicle.transform.position, task.GetSourcePosition()), and
        # GetSourcePosition returns sourceBuilding.transform.position
        # (DeliverySystem.cs:42-45, 657-673). Routes are computed to the road connection
        # cell, so the port had been scoring against the routing point -- and so did our own
        # `srcpos` instrumentation, which is why the two agreed and hid the bug. They are
        # ~1.6 units apart on this map, which is decisive when two vehicles are a similar
        # distance out. Falls back to the road cell for buildings placed mid-episode, whose
        # transform the map dump does not carry.
        sx, sy = src_pos or self.spec.building_pos.get(
            _building_at(src_cell, self.spec), cell_to_world(src_cell, self.spec))
        for i in candidates:
            vx, vy = cell_to_world(self.pos[i], self.spec)
            vx += self.spawn_dx[i]
            d = ((vx - sx) ** 2 + (vy - sy) ** 2) ** 0.5
            score = (100.0 / (1.0 + d) + (quantity / capacity) * 50.0
                     + self.spec.move_speed * 10.0)
            if score > best_score:
                best, best_score = i, score
        return best

    def stop_flooded(self, flooded) -> list:
        """FloodTaskGenerator.CheckForBlockedVehicles (OnFloodTileAdded): every vehicle in transit
        whose current cell is now flooded is stopped where it stands -- the movement check only
        fires on entering a cell, so a vehicle parked mid-leg at the round boundary is caught by
        the flood update instead (MCTS plan 7009 s13: Vehicle 3 stopped inside the expansion,
        before the next tile was added). Returns [(payload, cargo aboard)] like run_round's drops."""
        dropped = []
        # FindObjectsOfType<Vehicle>(): newest first, Vehicle 4 .. Vehicle 1 -- the stops (and so
        # the alerts' task ids) come in that order (rules-leaf 7011 f1280: Vehicle 2 stopped,
        # alert 141, then Vehicle 1, alert 142).
        for v, t in reversed(list(enumerate(self.trip))):
            if not t or t.get("phase") not in ("to_src", "to_dst") or "path" not in t:
                continue
            path = t["path"]
            idx = min(len(path) - 1, max(0, len(path) - t["left"]))
            if t.get("moved_after_clock") and idx > 0:
                # Its endSim-frame move comes after EndSimulation (the RunDelivery coroutine of a
                # trip dispatched in an earlier round resumes after the clock), so during the
                # update it still stands on the previous cell (uct 7007 s14: Vehicle 2 at index
                # 38 of 60 at f510, stopped by the update at f511; the port had already moved it
                # on and missed the stop). Its move onto the next cell is checked against the
                # updated flood in run_epilogue.
                idx -= 1
            if path[idx] in flooded:
                self.pos[v] = path[idx]
                self.damaged[v] = True
                dropped.append((t["payload"], t.get("aboard", t["qty"]) if t["phase"] == "to_dst" else 0))
                self.drop_veh[id(t["payload"])] = (t["payload"], v)
                self.carrying[v] = None
                self.trip[v] = None
                if self.events is not None: self.events.append((self.frame, "flooded", v, t["payload"][0], path[idx]))
        return dropped

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
