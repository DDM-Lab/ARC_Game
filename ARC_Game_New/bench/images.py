"""Decision-time images for the vision arms (synthetic dashboard or a real game frame)."""
from __future__ import annotations

import base64

MAP_GRID_JSON = "arc_map_grid.json"   # static tile lattice for the synthetic renderer (bench/export_map_grid.py)


def decision_image(image_mode, env, grid, tmp_png):
    """Return base64 PNG of a decision-time view of the CURRENT state, or None.

    synthetic -> render the dashboard from env.game_state + the static tile grid
                 (pure Python, no graphics build needed).
    real      -> ask Unity to capture the live game frame right now (no advance).
    Any failure degrades to None (text-only round) rather than crashing the episode."""
    try:
        if image_mode == "synthetic":
            from bench.dashboard_render import render_dashboard
            render_dashboard(env.game_state or {}, out_path=tmp_png, grid=grid)
            with open(tmp_png, "rb") as f:
                return base64.b64encode(f.read()).decode()
        if image_mode == "real":
            resp = env.request({"type": "capture_frame"})
            return (resp or {}).get("frame_base64")
    except Exception as e:
        print(f"    [image:{image_mode}] capture failed: {type(e).__name__}: {str(e)[:80]}")
    return None
