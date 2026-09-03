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
    assert parked_far.busy_frames[0] > parked_near.busy_frames[0], (
        "identical order, different parking, must cost different time")


def test_fleet_drops_cut_routes():
    """A flood-cut route is not a slow delivery -- it is one that never arrives."""
    from cora_sim.roads import Fleet, BUILDING_CELL, ROAD_CELLS
    f = Fleet()
    assert f.dispatch(0, "x", BUILDING_CELL["Kitchen_0"], BUILDING_CELL["Community01"])
    g = Fleet()
    assert not g.dispatch(0, "x", BUILDING_CELL["Kitchen_0"], BUILDING_CELL["Community01"],
                          flooded=ROAD_CELLS), "everything flooded must cut the route"
    assert g.carrying[0] is None and g.busy_frames[0] == 0


def main():
    print("cora_sim road graph + travel time vs Unity")
    fails = 0
    for fn in (test_roads, test_fleet_carries_position, test_fleet_drops_cut_routes):
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
