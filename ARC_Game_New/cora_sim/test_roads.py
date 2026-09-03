"""The road graph and the travel time that rides on it, against captured Unity legs.

Delivery travel was the last unmodelled subsystem and the only SPATIAL one: a delivery's
duration is the distance a vehicle drives, so it moves with the flood and with where the
vehicle last parked. These suites check the pathfinder against every movement leg Unity
actually drove, with the exact obstacle set it saw.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

def test_roads():
    """The pathfinder, against every movement leg Unity actually drove.

    fixtures_legs.json carries each leg's endpoints, the resulting A* length, AND the exact
    set of flooded road cells at that instant. Pinning the obstacle set matters: checking the
    port's A* against the port's own flood prediction is two unknowns in one equation, and a
    length mismatch could not then be blamed on the pathfinder rather than on the flood model.
    """
    import json
    from cora_sim import roads
    path = os.path.join(os.path.dirname(__file__), "fixtures_legs.json")
    legs = json.load(open(path))
    assert legs, "fixture is empty"

    routed = cut = 0
    for leg in legs:
        # moveSpeed is scene-serialized, and the scene has overridden a .cs initialiser six
        # other times in this port. If someone changes it, this fails loudly here rather than
        # showing up later as drift in delivery timing.
        assert leg["speed"] == roads.MOVE_SPEED, (
            f"Vehicle.moveSpeed changed in the scene: {leg['speed']} vs {roads.MOVE_SPEED}")
        flooded = frozenset(tuple(c) for c in leg["flood"])
        a = roads.nearest_road(roads.world_to_cell(*leg["from"]))
        b = roads.nearest_road(roads.world_to_cell(*leg["to"]))
        pred = roads.path_length(a, b, flooded)
        if leg["nodes"] == 0:
            # nodes==0 is Unity finding NO route: MoveToPosition bails to
            # StopVehicleDueToFlood and the order never lands. It is not a zero-length trip
            # (that is nodes==1), and conflating the two silently turns a stranded delivery
            # into an instant one.
            assert pred is None, f"port routed {a}->{b} where the flood had cut it"
            cut += 1
        else:
            assert pred == leg["len"], f"{a}->{b}: port {pred}, Unity {leg['len']}"
            routed += 1
    assert routed and cut, f"fixture should cover both cases (routed={routed}, cut={cut})"


def test_fleet_carries_position():
    """A trip is two legs, so cost depends on where the vehicle last parked.

    This is the property that makes travel time irreducible to a per-route constant, and the
    reason the same Kitchen->Community delivery was measured at 0 rounds one day and 5 the
    next. If a refactor ever resets vehicles to a depot between trips, this fails.
    """
    from cora_sim.roads import Fleet, BUILDING_CELL
    far = Fleet()
    v = far.free_vehicle()
    far.dispatch(v, "a", BUILDING_CELL["Community03"], BUILDING_CELL["Kitchen_0"])
    assert far.pos[v] == BUILDING_CELL["Kitchen_0"], "vehicle must end its trip at the destination"

    parked_near = Fleet()
    parked_near.pos[0] = BUILDING_CELL["Kitchen_0"]
    parked_far = Fleet()
    parked_far.pos[0] = BUILDING_CELL["Community03"]
    job = (BUILDING_CELL["Kitchen_0"], BUILDING_CELL["Community01"])
    parked_near.dispatch(0, "x", *job)
    parked_far.dispatch(0, "x", *job)
    assert parked_far.busy_seconds[0] > parked_near.busy_seconds[0], (
        "identical order, different parking, must cost different time")


def test_fleet_drops_cut_routes():
    """A flood-cut route is not a slow delivery -- it is one that never arrives."""
    from cora_sim.roads import Fleet, BUILDING_CELL, ROAD_CELLS
    f = Fleet()
    assert f.dispatch(0, "x", BUILDING_CELL["Kitchen_0"], BUILDING_CELL["Community01"])
    g = Fleet()
    assert not g.dispatch(0, "x", BUILDING_CELL["Kitchen_0"], BUILDING_CELL["Community01"],
                          flooded=ROAD_CELLS), "everything flooded must cut the route"
    assert g.carrying[0] is None and g.busy_seconds[0] == 0


def test_flood_damages_the_vehicle_and_drops_the_order():
    """A cut route is not a delay and not a retry.

    StopVehicleDueToFlood calls RemoveActiveDeliveryTask and nulls currentTask, so the order
    is dropped; it also sets isDamaged and the status to Damaged, and IsAvailable() is
    `status == Idle`, so the vehicle leaves the fleet until the repair task is answered.
    Flood therefore costs a third of the delivery capacity indefinitely, which no amount of
    latency tuning can express.
    """
    from cora_sim.roads import Fleet, BUILDING_CELL, ROAD_CELLS
    f = Fleet()
    v = f.best_vehicle(BUILDING_CELL["Kitchen_0"])
    assert not f.dispatch(v, "x", BUILDING_CELL["Kitchen_0"], BUILDING_CELL["Community01"],
                          flooded=ROAD_CELLS)
    assert f.damaged[v], "a flood-stopped vehicle is damaged, not merely idle"
    assert v not in f.available(), "a damaged vehicle must leave the fleet"
    assert f.carrying[v] is None, "the order is dropped, not held for retry"
    f.repair(v)
    assert v in f.available(), "RepairVehicle returns it to service"


def test_vehicle_choice_is_nearest_to_source():
    """FindSuitableVehicle scores 100/(1+distanceToSource), so the closest free vehicle wins.

    Picking the first free vehicle instead pins every trip to vehicle 0, and since leg 1 runs
    from wherever that vehicle parked, the whole travel model drifts on any round with more
    than one delivery.
    """
    from cora_sim.roads import Fleet, BUILDING_CELL
    f = Fleet()
    near_kitchen = f.best_vehicle(BUILDING_CELL["Kitchen_0"])
    near_c3 = f.best_vehicle(BUILDING_CELL["Community03"])
    assert near_kitchen != near_c3, "different sources must pick different vehicles"


def test_occupancy_is_real():
    """Occupancy binds on TIME, not on trip count.

    A vehicle is not spent for a whole round by one trip -- a two-leg Kitchen->Community run
    is a few seconds against a 10-second round, so it can serve several orders before the
    round ends. That is what the delivery:queue marks show Unity doing (three food orders
    created in one round). What DOES bind is the round's seconds: keep dispatching and the
    fleet runs out of time. This test previously asserted the opposite -- that three trips
    exhaust three vehicles -- which encoded a one-trip-per-round model the marks refute.
    """
    from cora_sim.roads import Fleet, BUILDING_CELL, ROUND_SECONDS
    f = Fleet()
    job = (BUILDING_CELL["Kitchen_0"], BUILDING_CELL["Community03"])

    v = f.best_vehicle(job[0])
    assert v is not None and f.dispatch(v, "x", *job)
    assert f.best_vehicle(job[0]) is not None, (
        "one short trip must not spend the fleet for the whole round")

    dispatched = 1
    while f.best_vehicle(job[0]) is not None and dispatched < 50:
        w = f.best_vehicle(job[0])
        assert f.dispatch(w, "x", *job)
        dispatched += 1
    assert dispatched > 3, f"a 10s round should fit more than one trip per vehicle, got {dispatched}"
    assert all(b >= ROUND_SECONDS for b in f.busy_seconds), "the fleet ran out of TIME"

    for _ in range(dispatched + 2):
        f.advance()
    assert f.best_vehicle(job[0]) is not None, "they must come home"


def test_repair_task_returns_the_vehicle():
    """Two distinct outcomes that used to be conflated, plus the repair loop.

    An order whose SOURCE-TO-DESTINATION route is cut is never created: CreateDeliveryTask
    bails on the route estimate before a vehicle is involved, so the fleet is untouched.
    This test previously asserted the opposite, and that mistake let three unroutable food
    orders disable an entire fleet in one round and send every following delivery to the
    fitted constant.

    Damage happens on the other path -- a vehicle already dispatched that cannot reach its
    source. MoveToPosition fails, StopVehicleDueToFlood marks it Damaged and spawns a
    repair task, and only choice 1 on that task returns it to service.
    """
    from cora_sim.tasks import TaskBoard
    from cora_sim import roads
    from cora_sim.roads import BUILDING_CELL, ROAD_CELLS

    b = TaskBoard(cell_for=roads.FACILITY_CELL.get)
    assert b.travel_rounds("Kitchen Alpha", "Community Charleston",
                           flooded=ROAD_CELLS) is False
    assert not any(b.fleet.damaged), "an order that is never created damages nothing"
    assert not b.repair_for, "and spawns no repair task"

    # Strand every vehicle in one corner by flooding only the cells around it, so the
    # source->destination route stays open (the pre-check passes) and the failure happens
    # on leg 1, which is the case Unity damages on.
    stuck = BUILDING_CELL["Community03"]
    b.fleet.pos = [stuck] * 3
    cut = frozenset(c for c in ROAD_CELLS
                    if abs(c[0] - stuck[0]) + abs(c[1] - stuck[1]) == 1)
    assert b.travel_rounds("Kitchen Alpha", "Community Charleston", flooded=cut) is False
    assert any(b.fleet.damaged), "a dispatched vehicle that cannot reach its source is damaged"
    assert b.repair_for, "damage spawns a repair task"

    tid = next(iter(b.repair_for))
    assert b.answer_repair(tid, 2) is False, "delaying leaves the vehicle out"
    assert any(b.fleet.damaged)
    hurt = b.fleet.damaged.index(True)
    b.open_repair_task(hurt)
    tid = next(iter(b.repair_for))
    assert b.answer_repair(tid, 1) is True, "choice 1 repairs"
    assert not b.fleet.damaged[hurt]


def test_busy_fleet_queues_instead_of_guessing():
    """With every vehicle out, a trip WAITS -- it does not fall back to a constant.

    DeliverySystem leaves the trip in pendingTasks and assigns it the moment a vehicle
    lands, so the cost is that wait plus the drive, which is still measured. Treating a busy
    fleet as "no opinion" reverted 51 of the replay's deliveries to the fitted constant.
    """
    from cora_sim.tasks import TaskBoard
    from cora_sim import roads
    b = TaskBoard(cell_for=roads.FACILITY_CELL.get)
    first = [b.travel_rounds("Kitchen Alpha", "Community Trinity") for _ in range(3)]
    assert all(isinstance(x, int) for x in first), first
    queued = b.travel_rounds("Kitchen Alpha", "Community Trinity")
    assert isinstance(queued, int), "a busy fleet must still produce a measured time"
    assert queued >= max(first), "a queued trip cannot land sooner than an unqueued one"


def test_map_spec_is_behaviour_neutral():
    """The MapSpec refactor must not have changed the shipped map by one cell.

    roads.py's module constants are now aliases onto MapSpec.default(), loaded from
    cora_sim/maps/default.json. This pins the contents against the numbers that were
    measured from the running game, so a refactor or a bad re-dump fails here rather than
    silently shifting every route.
    """
    from cora_sim import roads
    from cora_sim.map_spec import MapSpec
    m = MapSpec.default()
    assert len(m.road_cells) == 106, len(m.road_cells)
    assert len(m.building_cell) == 5 and len(m.site_cell) == 15
    assert m.move_speed == 8.0, "scene-serialized moveSpeed"
    assert m.round_seconds == 10.0 and m.fixed_delta == 0.3
    assert m.depots == ((-4, -4), (2, -4), (3, 5))
    # Kitchen_0's dumped RoadConnection cell, and the site it stands on, must agree.
    assert m.building_cell["Kitchen_0"] == (-8, -3) == m.site_cell[0]
    # The aliases really are the same objects, not a drifting copy.
    assert roads.ROAD_CELLS is m.road_cells and roads.MOVE_SPEED == m.move_speed


def test_a_second_map_is_usable_without_touching_code():
    """The point of the refactor: routing follows the spec it is handed.

    Search over many maps means the pathfinder cannot read module globals. A trimmed spec
    routes differently from the default, and nothing in roads.py needs editing to say so.
    """
    import copy, json, os
    from cora_sim.map_spec import MapSpec
    from cora_sim import roads
    raw = json.load(open(os.path.join(os.path.dirname(roads.__file__), "maps", "default.json")))
    small = copy.deepcopy(raw)
    small["name"] = "test-trimmed"
    # Slow enough that a unit step takes longer than one frame: 1/2 = 0.5s against a 0.3s
    # frame. Below that threshold speed does NOT change travel time -- the frame quantum
    # dominates, which is why the shipped map's moveSpeed of 8 gives exactly one frame per
    # step. Halving 8 to 4 still yields 0.25s per step, under the quantum, and correctly
    # changes nothing; testing with 4 asserted a falsehood.
    small["move_speed"] = 2.0
    other = MapSpec(small)
    steps = 16
    assert roads.leg_seconds(steps, other) > roads.leg_seconds(steps), (
        "below the frame quantum a slower vehicle must take longer")
    faster = copy.deepcopy(raw); faster["move_speed"] = 40.0
    assert roads.leg_seconds(steps, MapSpec(faster)) == roads.leg_seconds(steps), (
        "above the quantum, extra speed cannot help -- one step still costs one frame")
    f = roads.Fleet(other)
    assert f.spec is other and f.pos, "the fleet places vehicles using its own spec"


def test_leg_timing_is_one_frame_per_step():
    """Measured against the frame marks, which contradicted the obvious arithmetic.

        leg len  5 ->  6 frames        leg len 11 -> 12 frames
        leg len 19 -> 20 frames

    journeyLength/moveSpeed would give 3, 5 and 8. MoveToPosition walks the path segment by
    segment; each segment is one unit, so its journeyTime is 1/8 = 0.125s, and the inner
    loop adds a whole captureDeltaTime of 0.3 per iteration -- more than the segment needs.
    One frame per unit step, whatever moveSpeed says.
    """
    from cora_sim import roads
    for steps, frames in ((5, 6), (11, 12), (19, 20), (17, 18), (25, 26)):
        got = round(roads.leg_seconds(steps) / roads.GYM_FIXED_DELTA)
        assert got == frames, f"len {steps}: {got} frames, Unity measured {frames}"


def test_round_completes_two_then_three():
    """The whole fleet model in one assertion, taken straight from Unity's marks.

        d2r1: queued 5, completed 2
        d2r2: queued 1, completed 3

    Five orders against three vehicles: three go out, two finish inside the round's 10
    seconds, one is still driving. Next round it lands and the two that waited are picked
    up. Reproducing this was the point of the event-driven rewrite -- no per-order latency
    constant can produce 2-then-3.
    """
    from cora_sim.roads import Fleet, BUILDING_CELL
    f = Fleet()
    K = BUILDING_CELL["Kitchen_0"]
    C1, C2, C3 = (BUILDING_CELL["Community01"], BUILDING_CELL["Community02"],
                  BUILDING_CELL["Community03"])
    M = BUILDING_CELL["Motel"]
    pending = [(0, "f0", K, C1, 100), (1, "f1", K, C3, 100), (2, "f2", K, C2, 100),
               (3, "p3", C1, M, 100), (4, "p4", C3, M, 100)]
    first, rest = f.run_round(pending)
    assert len(first) == 2, f"Unity completes 2 in the first round, port {len(first)}"
    second, rest = f.run_round(rest)
    assert len(second) == 3, f"Unity completes 3 in the second, port {len(second)}"
    assert not rest, "and nothing is left over"


def main():
    print("cora_sim road graph + travel time vs Unity")
    fails = 0
    for fn in (test_roads, test_fleet_carries_position, test_fleet_drops_cut_routes,
               test_flood_damages_the_vehicle_and_drops_the_order,
               test_vehicle_choice_is_nearest_to_source, test_occupancy_is_real,
               test_repair_task_returns_the_vehicle,
               test_busy_fleet_queues_instead_of_guessing,
               test_map_spec_is_behaviour_neutral,
               test_a_second_map_is_usable_without_touching_code,
               test_leg_timing_is_one_frame_per_step,
               test_round_completes_two_then_three):
        try:
            fn()
            print(f"  {fn.__name__}: OK")
        except AssertionError as e:
            print(f"  {fn.__name__}: FAIL — {e}")
            fails += 1
    print("\nRESULT:", "ALL PASS" if not fails else f"{fails} FAILING")
    return 1 if fails else 0


if __name__ == "__main__":
    raise SystemExit(main())
