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
        "depots": [],                      # filled from observed first legs, see note below
        "road_cells": sorted([list(c) for c in dump["cells"]]),
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
    print("NOTE: fill `depots` from the first leg each vehicle drives (a capture with deliveries); "
          "a facility whose road:connection did not fire here can be added with "
          "add_connections(<map>, <capture log>).")
    return 0


def connections(text):
    """{facility: road cell}, {facility: transform} from RoadConnection's road:connection marks.
    The game emits one per facility the first time it is routed to, keyed by display name."""
    conns, positions = {}, {}
    for mm in re.finditer(r"road:connection \{.*?\} (\{.*?\})\n", text):
        j = json.loads(mm.group(1))
        conns[j["building"]] = list(j["cell"])
        if j.get("pos"):
            positions[j["building"]] = [float(x) for x in j["pos"]]
    return conns, positions


def resolve_sites(spec):
    """AbandonedSite world positions -> their road cells, the way RoadConnection resolves a
    building on that site: nearest_road(world_to_cell(x, y))."""
    from oracle.sim.map_spec import MapSpec
    from oracle.sim import roads
    m = MapSpec(dict(spec, depots=spec.get("depots") or [], building_pos=spec.get("building_pos") or {}))
    spec["site_cell"] = {k: list(roads.nearest_road(roads.world_to_cell(*v), m)) if any(
        isinstance(x, float) and x != int(x) for x in v) else v for k, v in spec["site_cell"].items()}


def add_connections(map_path, log_path):
    """Merge road:connection marks from a capture log into a map spec (facilities the dump run
    never routed to)."""
    spec = json.load(open(map_path))
    conns, positions = connections(open(log_path, errors="ignore").read())
    for key, table in (("building_cell", conns), ("facility_cell", conns), ("building_pos", positions)):
        spec.setdefault(key, {}).update(table)
        spec[key] = dict(sorted(spec[key].items()))
    resolve_sites(spec)
    json.dump(spec, open(map_path, "w"), indent=1)


if __name__ == "__main__":
    raise SystemExit(main())
