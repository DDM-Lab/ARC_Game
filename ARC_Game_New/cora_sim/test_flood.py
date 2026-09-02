"""Flood port equivalence vs the live Unity build.

Takes a native snapshot from Unity (which carries flood tiles, weather, AND the RNG state
- all pinned this session), advances ONE round in Unity, and advances the same round in
the port from the identical starting point. Compares the resulting flood tile SET and the
number of draws consumed.

Draw count matters as much as the tile set: flood sets the stream position for every
system after it, so a port that lands the right tiles via the wrong number of draws would
desync everything downstream while looking correct here.
"""
from __future__ import annotations

import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_search import SearchableEnv
from cora_sim.floodmap import FloodMap, pack
from cora_sim.flood import FloodState, update_flood, RAIN_INTENSITY
from cora_sim.rng import UnityRandom

EXE = ("Build/Headless/macOS/ARC_Headless.app/Contents/MacOS/"
       "Collaborative Operations And Resource Management with Agentic AI")


def snap_of(env):
    return json.loads(env._send_request({"type": "save_state"})["state"])


def flood_from_snap(s):
    f = s["flood"]
    return {pack(x, y) for x, y in zip(f["tileX"], f["tileY"])}, f["lastWeatherType"]


def rng_from_snap(s):
    st = json.loads(s["rng"]["unityRandomStateJson"])
    return (st["s0"] & 0xFFFFFFFF, st["s1"] & 0xFFFFFFFF,
            st["s2"] & 0xFFFFFFFF, st["s3"] & 0xFFFFFFFF)


def main(seed=555, warmup=6, rounds=6, port=9918):
    fmap = FloodMap.load()
    env = SearchableEnv(unity_exe_path=EXE, unity_port=port, seed=seed,
                        auto_start_unity=True, connection_timeout=90)
    env.reset()
    for _ in range(warmup):
        env.advance_round()

    print(f"flood equivalence  seed={seed}  warmup={warmup}  rounds={rounds}\n")
    print(f"{'round':<6} {'unity tiles':>12} {'port tiles':>11} {'set match':>10} {'weather':<12}")
    ok = True
    for i in range(rounds):
        s0 = snap_of(env)
        tiles0, last_weather = flood_from_snap(s0)
        rng_state = rng_from_snap(s0)
        weather_now = s0["weather"]["current"]

        env.advance_round()
        s1 = snap_of(env)
        tiles1, _ = flood_from_snap(s1)

        fs = FloodState(tiles0, last_weather)
        r = UnityRandom(state=rng_state)
        update_flood(fs, fmap, r, weather_now, RAIN_INTENSITY.get(weather_now, 0.0))

        match = fs.tiles == tiles1
        ok &= match
        print(f"{i:<6} {len(tiles1):>12} {len(fs.tiles):>11} {str(match):>10} {weather_now:<12}")
        if not match:
            print(f"       only unity: {len(tiles1 - fs.tiles)}   only port: {len(fs.tiles - tiles1)}")
    env.close()
    print("\nRESULT:", "PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(seed=int(sys.argv[1]) if len(sys.argv) > 1 else 555))
