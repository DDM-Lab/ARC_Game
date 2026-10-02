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

    ARC_SNAPSHOT_DEBUG=1 ./.venv/bin/python -m cora_sim.dump_map <port> [name]
"""
import json
import os
import re
import sys

EXE = ("Build/Headless/macOS/ARC_Headless.app/Contents/MacOS/"
       "Collaborative Operations And Resource Management with Agentic AI")


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 9899
    name = sys.argv[2] if len(sys.argv) > 2 else "dumped"
    sys.path.insert(0, os.getcwd())
    from cora_sim.searchable_env import SearchableEnv

    log = os.path.abspath(f"dump_map_{name}.log")
    env = SearchableEnv(unity_exe_path=EXE, unity_port=port, seed=1, auto_start_unity=True,
                        connection_timeout=120, unity_log_path=log)
    env.reset()
    matrix = env._send_request({"type": "pathfind_matrix"})
    # One round so GlobalClock emits round:length and a vehicle emits a leg.
    try:
        env.advance_round()
    except Exception:
        pass
    env.close()

    text = open(log, errors="ignore").read()
    m = re.search(r"\[ROADDUMP\] (\{.*?\})\n", text, re.S)
    if not m:
        raise SystemExit("no [ROADDUMP] in the log -- was ARC_SNAPSHOT_DEBUG=1 set?")
    dump = json.loads(m.group(1))
    anchor = float(re.search(r"\(([\d.]+),", dump["anchor"]).group(1))

    conns = {}
    for mm in re.finditer(r"road:connection \{.*?\} (\{.*?\})\n", text):
        j = json.loads(mm.group(1))
        conns[j["building"]] = list(j["cell"])

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
        "facility_cell": {},               # display-name table, resolved geometrically
        "site_cell": {str(n["site_id"]): [n["x"], n["y"]] for n in nodes
                      if n.get("kind") == "site"},
    }
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "maps", name + ".json")
    json.dump(spec, open(out, "w"), indent=1)
    print(f"wrote {out}: {len(spec['road_cells'])} cells, "
          f"{len(spec['building_cell'])} buildings, {len(spec['site_cell'])} sites, "
          f"moveSpeed {speed}")
    print("NOTE: site/facility coordinates are WORLD positions here; resolve them through "
          "roads.nearest_road(world_to_cell(x, y)) as the default map's were, and fill "
          "`depots` from the first leg each vehicle drives.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
