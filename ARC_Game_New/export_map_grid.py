#!/usr/bin/env python3
"""Query the `map_grid` gym endpoint and save the real tile lattice to JSON.

Output (arc_map_grid.json):
  bounds {xMin,yMin,width,height}, cellSize, worldRect {left,bottom,right,top},
  legend, rows[]  -- one string per row, top->bottom, one char per cell:
    g ground/grass   r river   R road   b blocking(forest/mountain)   f flood   . empty
The worldRect lets the renderer overlay world-space facilities on the same axes.
"""
import json, os, sys
from arc_game_gym_env_tcp import ARCGameGymEnv

RENDER_EXE = "Build/HeadlessRender/macOS/ARC_HeadlessRender.app/Contents/MacOS/ARC_DisasterSimulation"


def main():
    env = ARCGameGymEnv(unity_exe_path=RENDER_EXE, unity_port=10937,
                        auto_start_unity=True, max_episode_steps=4)
    env.reset()
    resp = env._send_request({"type": "map_grid"})
    env.close()

    if resp.get("type") != "map_grid":
        print("ERROR:", resp); sys.exit(1)

    out = os.path.join(os.getcwd(), "arc_map_grid.json")
    json.dump(resp, open(out, "w"), indent=2)

    b = resp["bounds"]; wr = resp["worldRect"]
    rows = resp["rows"]
    print(f"Saved {out}")
    print(f"grid {b['width']}x{b['height']} cells   cellSize={resp['cellSize']}")
    print(f"worldRect L{wr['left']} B{wr['bottom']} R{wr['right']} T{wr['top']}")
    counts = {}
    for r in rows:
        for ch in r:
            counts[ch] = counts.get(ch, 0) + 1
    print("tile counts:", {resp['legend'].get(k, k): v for k, v in sorted(counts.items())})
    print("\nASCII preview (top rows):")
    for r in rows[:min(len(rows), 40)]:
        print("  " + r)


if __name__ == "__main__":
    main()
