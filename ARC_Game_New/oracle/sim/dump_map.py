"""Produce a MapSpec JSON from a running build. New map support is running this.

Everything it reads is already emitted by the game:
  [ROADDUMP]         RoadTilemapManager.CacheRoadPositions, the whole road cell set
  road:connection    RoadConnection.GetRoadConnectionPoint, per building
  pathfind_matrix    the gym RPC, every AbandonedSite's position
  round:length       GlobalClock, simulationDuration / timeSpeed / fixedDelta
  delivery:leg       Vehicle, the live moveSpeed

The motion constants are read from the game rather than from the .cs files on purpose: a
scene value has overridden a field initialiser seven times in this port, moveSpeed most
recently (8, where Vehicle.cs says `= 5f`).

    ./.venv/bin/python -m oracle.sim.dump_map <port> [name]
"""
import json
import os
import re
import sys

# The map the headless build loads (GameConfigLoader, StreamingAssets).
_MAP_CONFIG = "Build/Headless/macOS/ARC_Headless.app/Contents/Resources/Data/StreamingAssets/map_config.json"


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 9899
    name = sys.argv[2] if len(sys.argv) > 2 else "dumped"
    from cora.env import GameEnv
    from cora.env.unity_process import default_exe
    os.environ["ARC_SNAPSHOT_DEBUG"] = "1"
    log = os.path.abspath(f"dump_map_{name}.log")
    env = GameEnv(unity_exe_path=default_exe(), unity_port=port, seed=1, unity_log_path=log)
    env.reset()
    matrix = env.request({"type": "pathfind_matrix"})
    # Decisions until GlobalClock emits round:length and a vehicle drives a leg (Day 1's setup
    # decision advances no simulated round).
    try:
        for _ in range(3):
            env.step([])
    except Exception:
        pass
    env.close()

    text = open(log, errors="ignore").read()
    m = re.search(r"\[ROADDUMP\] (\{.*?\})\n", text, re.S)
    if not m:
        raise SystemExit("no [ROADDUMP] in the log -- was ARC_SNAPSHOT_DEBUG=1 set?")
    dump = json.loads(m.group(1))
    anchor = float(re.search(r"\(([\d.]+),", dump["anchor"]).group(1))

    conns, positions = connections(text)

    rl = re.search(r"round:length \{.*?\} (\{.*?\})\n", text)
    clock = json.loads(rl.group(1)) if rl else {"simulationDuration": 10, "timeSpeed": 1,
                                                "fixedDelta": 0.3}
    lg = re.search(r"delivery:leg \{.*?\} (\{.*?\})\n", text)
    speed = float(json.loads(lg.group(1))["speed"]) if lg else 8.0

    nodes = (matrix or {}).get("nodes") or []
    spec = {
        "name": name,
        "description": f"dumped from a running build on port {port}",
        "anchor": anchor,
        "move_speed": speed,
        "simulation_duration": float(clock["simulationDuration"]),
        "time_speed": int(clock["timeSpeed"]),
        "fixed_delta": float(clock["fixedDelta"]),
        "depots": vehicles_from_map_config(_MAP_CONFIG) if os.path.exists(_MAP_CONFIG) else [],
        "road_cells": (roads_from_map_config(_MAP_CONFIG) if os.path.exists(_MAP_CONFIG)
                       else sorted([list(c) for c in dump["cells"]])),
        "building_cell": dict(sorted(conns.items())),
        "facility_cell": dict(sorted(conns.items())),     # buildings are keyed by display name
        "building_pos": dict(sorted(positions.items())),
        "site_cell": {str(n["site_id"]): [n["x"], n["y"]] for n in nodes
                      if n.get("kind") == "site"},
    }
    resolve_sites(spec)
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "maps", name + ".json")
    json.dump(spec, open(out, "w"), indent=1)
    print(f"wrote {out}: {len(spec['road_cells'])} cells, "
          f"{len(spec['building_cell'])} buildings, {len(spec['site_cell'])} sites, "
          f"moveSpeed {speed}")
    print("NOTE: a facility whose road:connection did not fire here (and every site's road cell) "
          "is added with add_connections(<map>, <capture log>); move_speed needs a delivery:leg.")
    return 0


def roads_from_map_config(path) -> list:
    """Road cells from a map config's roadLayer (row-major gridWidth x gridHeight, cell
    (gx - 14, gy - 10)). [ROADDUMP] fires before GameConfigLoader applies the config, so on a
    build that loads one it lists the scene's built-in roads, not the ones the game drives on;
    the offset is the one under which every observed road:connection cell is a road."""
    d = json.load(open(path))
    w = d["gridWidth"]
    return sorted([i % w - 14, i // w - 10] for i, v in enumerate(d["roadLayer"]) if v)


# MapConfigApplier.gridOrigin as the scene serializes it (the .cs initialiser says -14.5): the
# value under which every spawned community and vehicle lands where the game reports it.
_GRID_ORIGIN = (-13.5, -9.5)


def vehicles_from_map_config(path) -> list:
    """Each Vehicle object's spawn cell, in spawn order (MapConfigApplier.SpawnVehicle: the
    footprint centre, 0.5 down)."""
    from math import floor
    d = json.load(open(path))
    out = []
    for o in d["objects"]:
        if o.get("type") == 3:                                    # PlacedObjectType.Vehicle
            x = _GRID_ORIGIN[0] + o["gridX"] + o.get("width", 1) * 0.5
            y = _GRID_ORIGIN[1] + o["gridY"] + o.get("height", 1) * 0.5 - 0.5
            out.append([floor(x), floor(y)])
    return out


def connections(text):
    """{facility: road cell}, {facility: transform} from RoadConnection's road:connection marks.
    The game emits one per facility the first time it is routed to, keyed by display name."""
    conns, positions = {}, {}
    for mm in re.finditer(r"road:connection \{.*?\} (\{.*?\})\n", text):
        j = json.loads(mm.group(1))
        if re.fullmatch(r"[A-Za-z]+_\d+", j["building"]):
            continue                    # a built building (<Type>_<site>): see site_connections
        conns[j["building"]] = list(j["cell"])
        if j.get("pos"):
            positions[j["building"]] = [float(x) for x in j["pos"]]
    return conns, positions


def site_connections(text) -> dict:
    """{site id: road cell} from the road:connection marks of buildings built this episode
    (GameObject name <Type>_<site>). RoadConnection's own pick, which a nearest-road search
    from the site's transform does not reproduce."""
    out = {}
    for mm in re.finditer(r"road:connection \{.*?\} (\{.*?\})\n", text):
        j = json.loads(mm.group(1))
        m = re.fullmatch(r"[A-Za-z]+_(\d+)", j["building"])
        if m:
            out[m.group(1)] = list(j["cell"])
    return out


def resolve_sites(spec):
    """AbandonedSite world positions -> their road cells, the way RoadConnection resolves a
    building on that site: nearest_road(world_to_cell(x, y))."""
    from oracle.sim.map_spec import MapSpec
    from oracle.sim import roads
    m = MapSpec(dict(spec, depots=spec.get("depots") or [], building_pos=spec.get("building_pos") or {}))
    spec["site_cell"] = {k: list(roads.nearest_road(roads.world_to_cell(*v), m)) if any(
        isinstance(x, float) and x != int(x) for x in v) else v for k, v in spec["site_cell"].items()}


def add_connections(map_path, log_path):
    """Merge road:connection marks from a capture log into a map spec: facilities the dump run
    never routed to, and the road cell of every site something was built on."""
    spec = json.load(open(map_path))
    text = open(log_path, errors="ignore").read()
    conns, positions = connections(text)
    for key, table in (("building_cell", conns), ("facility_cell", conns), ("building_pos", positions)):
        spec.setdefault(key, {}).update(table)
        spec[key] = dict(sorted(spec[key].items()))
    resolve_sites(spec)
    spec["site_cell"].update(site_connections(text))      # observed beats resolved
    json.dump(spec, open(map_path, "w"), indent=1)


if __name__ == "__main__":
    raise SystemExit(main())
