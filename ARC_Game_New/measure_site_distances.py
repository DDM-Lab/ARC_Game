#!/usr/bin/env python3
"""Measure real road-path delivery distances/rounds between routing nodes.

Queries the new `pathfind_matrix` gym endpoint (A* over roads inside Unity) for
every pair of available build sites, communities, the motel, and built
facilities. Converts road distance -> delivery seconds -> game rounds using the
verified engine constants, and reports how much site LOCATION matters.

Engine constants (traced from code):
  vehicleSpeed   = 5 world-units / sec      (Vehicle.moveSpeed)
  load+unload    = 2 sec                     (PathfindingSystem.EstimateDeliveryTime)
  round window   = 10 simulated sec / round  (GlobalClock.simulationDuration @ Normal)
  => sec      = roadDist / 5 + 2   (returned by the engine directly)
  => rounds   = ceil(sec / 10)     (vehicle moves 10 sim-sec per round, position persists)
"""
import json, math, os, sys
from arc_game_gym_env_tcp import ARCGameGymEnv

RENDER_EXE = "Build/HeadlessRender/macOS/ARC_HeadlessRender.app/Contents/MacOS/ARC_DisasterSimulation"
SEC_PER_ROUND = 10.0


def rounds_for(sec):
    if sec is None or sec < 0:
        return None
    return max(1, math.ceil(sec / SEC_PER_ROUND))


def main():
    env = ARCGameGymEnv(unity_exe_path=RENDER_EXE, unity_port=10931,
                        auto_start_unity=True, max_episode_steps=4)
    env.reset()
    resp = env._send_request({"type": "pathfind_matrix"})
    env.close()

    if resp.get("type") != "pathfind_matrix":
        print("ERROR:", resp)
        sys.exit(1)

    nodes = resp["nodes"]
    edges = resp["edges"]
    out = os.path.join(os.getcwd(), "site_distance_matrix.json")
    json.dump(resp, open(out, "w"), indent=2)
    print(f"Saved raw matrix -> {out}")

    def label(i):
        nd = nodes[i]
        return (f"#{nd['site_id']}" if nd["kind"] == "site" else nd["kind"]) + f"({nd['name']})"

    def euclid(i, j):
        a, b = nodes[i], nodes[j]
        return math.hypot(a["x"] - b["x"], a["y"] - b["y"])

    print(f"\n{len(nodes)} nodes:")
    for nd in nodes:
        print(f"  [{nd['idx']:2d}] {nd['kind']:10s} {nd['name']:24s} "
              f"site_id={nd['site_id']:>3} pos=({nd['x']:+.2f},{nd['y']:+.2f})")

    site_idx = {nd["idx"] for nd in nodes if nd["kind"] == "site"}
    comm_idx = {nd["idx"] for nd in nodes if nd["kind"].lower().startswith("comm")}
    motel_idx = {nd["idx"] for nd in nodes if nd["kind"].lower() == "motel"}

    blocked = [e for e in edges if not e["path"]]
    flood = [e for e in edges if e["flood"]]

    def summarize(name, pred):
        sel = [e for e in edges if pred(e) and e["path"]]
        if not sel:
            print(f"\n{name}: (no routable pairs)")
            return
        sel.sort(key=lambda e: e["dist"])
        mn, mx = sel[0], sel[-1]
        det = [(e["dist"] / euclid(e["i"], e["j"])) if euclid(e["i"], e["j"]) > 0.01 else 1.0
               for e in sel]
        print(f"\n{name}  ({len(sel)} routable pairs)")
        print(f"  MIN road {mn['dist']:6.1f}u  {mn['sec']:5.1f}s  {rounds_for(mn['sec'])} rnd   "
              f"{label(mn['i'])} -> {label(mn['j'])}")
        print(f"  MAX road {mx['dist']:6.1f}u  {mx['sec']:5.1f}s  {rounds_for(mx['sec'])} rnd   "
              f"{label(mx['i'])} -> {label(mx['j'])}")
        med = sorted(e["dist"] for e in sel)[len(sel) // 2]
        print(f"  median road {med:6.1f}u    avg road-detour factor {sum(det)/len(det):.2f}x straight-line")
        rd = {}
        for e in sel:
            r = rounds_for(e["sec"])
            rd[r] = rd.get(r, 0) + 1
        print(f"  rounds-to-arrive histogram: " +
              "  ".join(f"{r}rnd:{c}" for r, c in sorted(rd.items())))

    summarize("SITE -> SITE",       lambda e: e["i"] in site_idx and e["j"] in site_idx)
    summarize("SITE -> COMMUNITY",  lambda e: e["i"] in site_idx and e["j"] in comm_idx)
    summarize("SITE -> MOTEL",      lambda e: e["i"] in site_idx and e["j"] in motel_idx)

    print(f"\nUNROUTABLE pairs: {len(blocked)}   flood-blocked: {len(flood)}")
    print("\n(rounds = ceil(sec/10); sec = roadDist/5 + 2)")


if __name__ == "__main__":
    main()
