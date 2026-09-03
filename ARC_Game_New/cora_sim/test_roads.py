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
    """Flood damage spawns a repair task, and answering it with choice 1 restores service.

    FloodTaskGenerator.CreateVehicleRepairTask makes an Emergency task with two choices --
    repair now for $1200, or delay at -5 satisfaction -- and ApplyChoiceImpacts repairs only
    on choiceId 1. This looked GUI-only (it lives in TaskDetailUI) but the headless gym
    routes through SelectTaskChoiceHeadless -> CompleteTaskAction -> ApplyChoiceImpacts, so
    the headless server really does repair. Without this loop a vehicle damaged once was out
    for the rest of the episode and the fleet drained to nothing.
    """
    from cora_sim.tasks import TaskBoard
    from cora_sim import roads
    b = TaskBoard(cell_for=roads.FACILITY_CELL.get)
    assert b.travel_rounds("Kitchen Alpha", "Community Charleston",
                           flooded=roads.ROAD_CELLS) is False
    assert any(b.fleet.damaged), "a cut route must damage the vehicle"
    assert b.repair_for, "damage must spawn a repair task"
    tid = next(iter(b.repair_for))
    assert b.answer_repair(tid, 2) is False, "delaying leaves the vehicle out"
    assert any(b.fleet.damaged)
    b.open_repair_task(0)
    tid = next(iter(b.repair_for))
    assert b.answer_repair(tid, 1) is True, "choice 1 repairs"
    assert not any(b.fleet.damaged)


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


def main():
    print("cora_sim road graph + travel time vs Unity")
    fails = 0
    for fn in (test_roads, test_fleet_carries_position, test_fleet_drops_cut_routes,
               test_flood_damages_the_vehicle_and_drops_the_order,
               test_vehicle_choice_is_nearest_to_source, test_occupancy_is_real,
               test_repair_task_returns_the_vehicle,
               test_busy_fleet_queues_instead_of_guessing):
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
