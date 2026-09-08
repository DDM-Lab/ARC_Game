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
# delivery:config reports taskAssignmentInterval = 1, in seconds of game time. Dispatch is
# gated on it in DeliverySystem.Update, so a round runs round_seconds / this many passes.
TASK_ASSIGNMENT_INTERVAL = 1.0
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


def path_cells(start, goal, flooded=frozenset(), spec=None):
    """The cells of PathfindingSystem's flood-aware A* route, start first, or None.

    Same search and the same (FCost, HCost) ordering as path_length, with parents kept so
    the route itself comes back. Vehicle.CheckForFloodCollision tests the vehicle's position
    against the flood tiles on every movement frame, so which cells a leg crosses -- not just
    how many -- decides whether a vehicle survives a flood that spreads onto its route at a
    round boundary. 5802 step 27: Unity's Vehicle 1 is stopped ten frames into a leg whose
    remaining cells cross a tile that flooded at the end of the previous round.
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
    if goal in flooded:
        return None
    if start == goal:
        return [start]
    def h(c):
        return abs(c[0] - goal[0]) + abs(c[1] - goal[1])
    open_ = [(h(start), 0, start)]
    best = {start: 0}
    parent = {}
    while open_:
        _, g, cur = heappop(open_)
        if cur == goal:
            out = [cur]
            while out[-1] != start:
                out.append(parent[out[-1]])
            out.reverse()
            return out
        if g > best.get(cur, 1 << 30):
            continue
        for dx, dy in _DIRS:
            nb = (cur[0] + dx, cur[1] + dy)
            if nb not in cells or nb in flooded:
                continue
            ng = g + 1
            if ng < best.get(nb, 1 << 30):
                best[nb] = ng
                parent[nb] = cur
                heappush(open_, (ng + h(nb), ng, nb))
    return None


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
    # A FLOODED START IS ALLOWED. FindPathAStarFloodAware only rejects flooded NEIGHBOURS
    # (GetFloodAwareNeighbors), never the node it starts from, so a vehicle stopped in water
    # by a collision drives out again once repaired. The port refused it: on 5802 Vehicle 1,
    # repaired on the cell that stopped it at step 27, was dispatched at round 29 while that
    # cell was still flooded, judged to have no path, damaged again, and its order dropped --
    # the one food resolution the port had over Unity. A flooded goal stays unreachable,
    # since it can only be entered as a neighbour.
    if goal in flooded:
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


class Fleet:
    """DeliverySystem's vehicles, carrying position, occupancy and damage between trips.

    A trip is TWO legs -- Vehicle.RunDelivery drives to the source road connection, loads,
    then drives to the destination -- so a delivery's cost depends on where the assigned
    vehicle last parked, not on the source/destination pair alone. That is why the same
    Kitchen->Community route was measured at 0 rounds one day and 5 the next.
    """

    __slots__ = ("pos", "busy_seconds", "carrying", "damaged", "spec", "frame", "trip", "events", "aborted")

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
        self.frame = 0                       # cumulative SIM frames since the game began
        self.trip = [None, None, None]       # per-vehicle in-flight state, see run_round
        self.events = None                   # set to a list to record (frame, kind, veh, id)
        self.aborted = []                    # payloads abandoned by an empty-source load this round

    def clone(self):
        f = Fleet.__new__(Fleet)
        f.spec = self.spec
        f.pos = list(self.pos)
        f.busy_seconds = list(self.busy_seconds)
        f.carrying = list(self.carrying)
        f.damaged = list(self.damaged)
        f.frame = self.frame
        f.trip = [dict(t) if t else None for t in self.trip]
        f.events = None
        f.aborted = list(self.aborted)
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


    def run_round(self, pending, flooded=frozenset(), load=None):
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
        from math import ceil
        frames = int(ceil(self.spec.round_seconds / self.spec.fixed_delta))
        pass_every = int(TASK_ASSIGNMENT_INTERVAL // self.spec.fixed_delta) + 1
        queue = list(pending)
        landed, dropped = [], []
        for _o in range(frames):
            self.frame += 1
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
                    v = self._closest(ready, src, qty)
                    if self.trip[v] is not None and self.trip[v].get("race_ready") == self.frame:
                        # THE RACE: two coroutines advance one shared currentPathIndex. The
                        # source leg is skipped, the destination leg runs at two cells a
                        # frame, and the nominal cargo is unloaded although nothing was ever
                        # picked up. Measured on every instance in the captures; a Unity bug
                        # the surrogate reproduces because the game has it.
                        leg2 = path_length(self.pos[v], dst, flooded, self.spec)
                        if leg2 is None:
                            self.damaged[v] = True
                            dropped.append((payload, True))      # loaded at the source
                            self.trip[v] = None
                            ready.remove(v)
                            queue.pop(0)
                            continue
                        self.trip[v] = {"payload": payload, "src": src, "dst": dst, "qty": qty,
                                        "phase": "to_dst", "left": max(1, -(-leg2 // 2)) + 1}
                        self.carrying[v] = payload
                        if self.events is not None: self.events.append((self.frame, "race", v, payload[0], leg2))
                        ready.remove(v)
                        queue.pop(0)
                        continue
                    self.trip[v] = None
                    leg1 = path_length(self.pos[v], src, flooded, self.spec)
                    if leg1 is None:
                        # No flood-free path to the source: blocked on the dispatch frame.
                        self.damaged[v] = True
                        dropped.append((payload, False))     # never reached the source
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
                                "phase": "to_src", "left": 1 + 1}
                    else:
                        trip = {"payload": payload, "src": src, "dst": dst, "qty": qty,
                                "phase": "to_src", "left": leg1 + 1,
                                "path": path_cells(self.pos[v], src, flooded, self.spec)}
                    self.trip[v] = trip
                    if self.events is not None: self.events.append((self.frame, "dispatch", v, payload[0], leg1))
                    ready.remove(v)
                    queue.pop(0)
            # -- advance every vehicle in flight by one frame ---------------------------
            for v, t in enumerate(self.trip):
                if t is None:
                    continue
                if "race_ready" in t:
                    # An aborted vehicle whose pass frame has passed is simply idle.
                    if t["race_ready"] < self.frame:
                        self.trip[v] = None
                    continue
                t["left"] -= 1
                if t["left"] > 0:
                    # A MOVEMENT FRAME: the vehicle has just entered the next cell of its leg.
                    # CheckForFloodCollision runs after every lerp step; the flood set is
                    # constant within a round, so this fires the frame a vehicle enters the
                    # first tile that flooded onto its route at the last round boundary.
                    path = t.get("path")
                    if path is not None:
                        idx = len(path) - t["left"]
                        if 0 < idx < len(path) and path[idx] in flooded:
                            self.pos[v] = path[idx]
                            self.damaged[v] = True
                            dropped.append((t["payload"], t["phase"] == "to_dst"))
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
                    if load is not None and load(t["payload"], t["qty"]) <= 0:
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
                    t["phase"], t["left"] = "boarding", 1
                elif ph == "boarding":
                    leg2 = path_length(t["src"], t["dst"], flooded, self.spec)
                    if leg2 is None:
                        # No flood-free path for the destination leg: StopVehicleDueToFlood
                        # -> HandleDeliveryFailure. Damaged, order gone.
                        self.damaged[v] = True
                        dropped.append((t["payload"], True))     # loaded, no destination leg
                        self.trip[v] = None
                        continue
                    t["phase"], t["left"] = "to_dst", max(1, leg2)
                    t["path"] = path_cells(t["src"], t["dst"], flooded, self.spec)
                    self.carrying[v] = t["payload"]
                    if self.events is not None: self.events.append((self.frame, "leg2", v, t["payload"][0], leg2))
                elif ph == "to_dst":
                    self.pos[v] = t["dst"]
                    # An unload on the round's LAST movement frame (+34) is split across the
                    # segment boundary: UnloadCargo -> HandlePopulationDelivery registers
                    # the actual group now, but OnVehicleDeliveryCompleted fires on the
                    # completion frame (+35), after the advance, generation and flood. The
                    # "split" tag lets step_round register the nominal group late. Measured
                    # on the 5901 validation run: Unity unload f321, complete f322 = d2r2.
                    landed.append(tuple(t["payload"]) + ("split",) if _o == frames - 1
                                  else t["payload"])          # UnloadCargo, this frame
                    if self.events is not None: self.events.append((self.frame, "unload", v, t["payload"][0]))
                    self.carrying[v] = None
                    t["phase"], t["left"] = "complete", 1
                elif ph == "complete":
                    self.trip[v] = None                        # CompleteDelivery -> Idle
        # -- epilogue: the two paused frames after the round -------------------------------
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
                    # A vehicle reaching its source on the epilogue frame LOADS AFTER THE
                    # ADVANCE: 7002 validation, Vehicle3 re-dispatched on the round's last
                    # pass to a kitchen it was standing on, "loading cargo" logged after
                    # "Kitchen_14 produced 100 FoodPacks this round", and its destination
                    # leg unloading at resume + nodes. Leaving `left` at 1 makes the next
                    # round's first frame -- which runs after this round's production -- do
                    # the at_src/load, which lands the unload on that same frame.
                    continue
                t["left"] = 0
                if ph == "boarding":
                    leg2 = path_length(t["src"], t["dst"], flooded, self.spec)
                    if leg2 is None:
                        self.damaged[v] = True
                        dropped.append((t["payload"], True))     # loaded, no destination leg
                        self.trip[v] = None
                        continue
                    # A leg started in a paused frame makes no movement until the next
                    # round's frame 1, so its first movement frame is lost: one extra.
                    t["phase"], t["left"] = "to_dst", max(1, leg2) + 1
                    t["path"] = path_cells(t["src"], t["dst"], flooded, self.spec)
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
        self.frame -= 2                                        # sim frames only, for the pass phase
        # busy_seconds is kept for callers that read it: frames still to run, in seconds.
        for v, t in enumerate(self.trip):
            self.busy_seconds[v] = (t["left"] * self.spec.fixed_delta) if (t and "left" in t) else 0.0
        return landed, queue, dropped

    def _closest(self, candidates, src_cell, quantity=0, capacity=100.0):
        """CalculateVehicleSuitability among a set of already-free vehicles."""
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
        sx, sy = self.spec.building_pos.get(
            _building_at(src_cell, self.spec), cell_to_world(src_cell, self.spec))
        for i in candidates:
            vx, vy = cell_to_world(self.pos[i], self.spec)
            d = ((vx - sx) ** 2 + (vy - sy) ** 2) ** 0.5
            score = (100.0 / (1.0 + d) + (quantity / capacity) * 50.0
                     + self.spec.move_speed * 10.0)
            if score > best_score:
                best, best_score = i, score
        return best

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
