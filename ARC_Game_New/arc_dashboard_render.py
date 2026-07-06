#!/usr/bin/env python3
"""Synthetic, tile-accurate operations map + decision dashboard for the ARC game.

The LEFT panel draws the REAL tile lattice exported from Unity (`map_grid`
endpoint -> arc_map_grid.json): grass, rivers, the road network, forest/mountain
blocking, and active flood — on true world coordinates, with per-tile grid lines.
Facilities, communities, the motel and build-sites are overlaid at their real
world positions so a VLM can read spatial structure (what's near what, where the
roads/rivers run, which sites sit across water) directly from pixels.

The RIGHT panel is the decision layer: per-community service deficits, facility
staffing, the workforce economy, budget/spend, and a diagnosis callout.

Usage:
    render_dashboard(game_state_dict, out_path="dashboard.png",
                     grid="arc_map_grid.json")
`game_state_dict` is the INNER game_state (sessionInfo, mapState, ...).
`grid` may be a path or the already-loaded map_grid dict; if missing, the map
falls back to a plain scatter (no tiles).
"""
import os, json
os.environ.setdefault("MPLCONFIGDIR", os.path.join(os.environ.get("TMPDIR", "/tmp"), "mpl"))
import matplotlib
matplotlib.use("Agg")
import numpy as np
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle, RegularPolygon, FancyBboxPatch
from matplotlib.lines import Line2D

# Reward-relevant community resource: green good -> red crisis.
GOOD, WARN, CRIT = "#2e9e4f", "#e8a33d", "#d7443e"
GRIDC = "#dfe3e8"

# Tile palette (RGB 0-255). Chosen to read unambiguously as terrain types.
TILE_RGB = {
    "g": (138, 191, 109),   # grass
    "r": (108, 170, 222),   # river
    "R": (124, 118, 110),   # road (paved grey-tan)
    "b": (52, 92, 54),      # blocking: forest / mountain
    "f": (40, 104, 200),    # active flood
    ".": (244, 246, 248),   # empty / off-map
}
TILE_NAME = {"g": "grass", "r": "river", "R": "road", "b": "forest/mtn",
             "f": "flood", ".": "off-map"}


def _fill_color(ratio):
    if ratio >= 0.67: return GOOD
    if ratio >= 0.34: return WARN
    return CRIT


def _pos(f):
    p = f.get("position", {}) or {}
    return float(p.get("x", 0)), float(p.get("y", 0))


def _load_grid(grid):
    if grid is None:
        return None
    if isinstance(grid, str):
        if not os.path.exists(grid):
            return None
        grid = json.load(open(grid))
    return grid if grid.get("rows") else None


def _draw_tilemap(ax, grid):
    """Paint the tile lattice as the base layer; return worldRect (l,r,b,t)."""
    rows = grid["rows"]
    H, W = len(rows), len(rows[0])
    img = np.empty((H, W, 3), dtype=np.uint8)
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            img[y, x] = TILE_RGB.get(ch, TILE_RGB["."])
    wr = grid["worldRect"]
    l, r, b, t = wr["left"], wr["right"], wr["bottom"], wr["top"]
    ax.imshow(img, extent=[l, r, b, t], origin="upper",
              interpolation="nearest", zorder=0)
    # per-tile grid lines so the model can count tiles / gauge spacing
    cs = grid.get("cellSize", {}).get("x", 1.0) or 1.0
    xs = np.arange(np.floor(l), np.ceil(r) + 1, cs)
    ys = np.arange(np.floor(b), np.ceil(t) + 1, cs)
    ax.set_xticks(xs); ax.set_yticks(ys)
    ax.grid(True, color="#000000", alpha=0.10, lw=0.5)
    ax.tick_params(labelsize=6, length=0)
    ax.set_xlim(l, r); ax.set_ylim(b, t)
    return l, r, b, t


def render_dashboard(gs, out_path="arc_dashboard.png", grid="arc_map_grid.json"):
    sess = gs.get("sessionInfo", {})
    sb = gs.get("satisfactionAndBudget", {})
    facs = gs.get("mapState", {}).get("facilities", [])
    sites = gs.get("constructionState", {}).get("availableSites", [])
    wf = gs.get("workforceState", {})
    rm = gs.get("rewardMetrics", {})
    env = gs.get("environmentalConditions", {})
    grid = _load_grid(grid)

    communities = [f for f in facs if f.get("buildingType") == "Community"]
    motels = [f for f in facs if f.get("buildingType") == "Motel"]
    builts = [f for f in facs if f.get("buildingType") not in ("Community", "Motel")]

    fig = plt.figure(figsize=(17, 9), dpi=110)
    fig.patch.set_facecolor("white")
    g = fig.add_gridspec(1, 2, width_ratios=[1.65, 1.0], wspace=0.04,
                         left=0.035, right=0.975, top=0.90, bottom=0.06)
    axm = fig.add_subplot(g[0, 0])
    axp = fig.add_subplot(g[0, 1])

    # ── Title bar ───────────────────────────────────────────────────────────
    sat = sb.get("satisfaction", 0); bud = sb.get("budget", 0)
    day, rnd = sess.get("currentDay", "?"), sess.get("currentRound", "?")
    haz = "FLOODING" if env.get("isFlooding") else env.get("weatherCondition", "Clear")
    fig.suptitle(f"ARC Operations Map  —  Day {day}, Round {rnd}",
                 fontsize=17, fontweight="bold", x=0.035, ha="left", y=0.965)
    fig.text(0.035, 0.925,
             f"Satisfaction {sat}/100      Budget \\${bud:,}      Conditions: {haz}",
             fontsize=12, color="#333", ha="left")

    # ── MAP ───────────────────────────────────────────────────────────────
    if grid is not None:
        _draw_tilemap(axm, grid)
        axm.set_title("Real tile map  (grass / road / river / forest / flood)  +  "
                      "sites #id = build targets", fontsize=10.5, color="#333")
    else:
        axm.set_facecolor("#f6f8fa"); axm.grid(True, color=GRIDC, lw=0.6)
        axm.set_title("Spatial layout (no tile grid available)", fontsize=10.5)
    axm.set_xlabel("x (world units = tiles)"); axm.set_ylabel("y")
    axm.set_aspect("equal")

    def halo(x, y, txt, dy, color, size=8.0, weight="bold"):
        axm.annotate(txt, (x, y), textcoords="offset points", xytext=(0, dy),
                     ha="center", fontsize=size, fontweight=weight, color=color,
                     path_effects=_PE, zorder=8)

    import matplotlib.patheffects as pe
    _PE = [pe.withStroke(linewidth=2.4, foreground="white")]

    # build sites
    for s in sites:
        p = s.get("position", {}) or {}
        x, y = float(p.get("x", 0)), float(p.get("y", 0))
        axm.scatter(x, y, marker="*", s=300, c="#ffd23f", edgecolors="#6e4d00",
                    linewidths=1.0, zorder=7)
        halo(x, y, f"#{s.get('siteId')}", 10, "#5a3d00", size=9.5)

    # built facilities: staffing + stock + operational status
    for f in builts:
        x, y = _pos(f)
        res = f.get("resources", {}) or {}
        assigned = f.get("assignedWorkforce", 0); required = f.get("requiredWorkforce", 0)
        operational = f.get("isOperational") and (required == 0 or assigned >= required)
        edge = GOOD if operational else (WARN if assigned > 0 else "#7a7f85")
        axm.add_patch(Rectangle((x - 0.5, y - 0.5), 1.0, 1.0, facecolor="white",
                      edgecolor=edge, lw=2.4, zorder=6, alpha=0.95))
        cap = res.get("foodPacksCapacity", 0) or 0; stock = res.get("foodPacks", 0) or 0
        halo(x, y, f"{f.get('buildingType','?')[:4]}\n{stock}/{cap}f {assigned}/{required}w",
             -26, edge, size=6.6, weight="normal")

    # communities: colored by food deficit
    for c in communities:
        x, y = _pos(c)
        res = c.get("resources", {}) or {}
        cap = res.get("foodPacksCapacity", 0) or 1; food = res.get("foodPacks", 0) or 0
        ratio = food / cap if cap else 1.0
        axm.add_patch(Rectangle((x - 0.62, y - 0.62), 1.24, 1.24, facecolor=_fill_color(ratio),
                      edgecolor="black", lw=1.5, zorder=6, alpha=0.92))
        deficit = cap - food
        lbl = f"{c.get('facilityName','C')}\nfood {food}/{cap}" + (f" ⚠-{deficit}" if deficit > 0 else "")
        halo(x, y, lbl, 16, "black", size=8.2)

    # motel
    for m in motels:
        x, y = _pos(m)
        cap = m.get("populationCapacity", 0) or 1; pop = m.get("currentPopulation", 0) or 0
        axm.add_patch(RegularPolygon((x, y), 6, radius=0.72, facecolor="#8a6fd6",
                      edgecolor="black", lw=1.4, zorder=6, alpha=0.92))
        halo(x, y, f"Motel\nlodg {pop}/{cap}", 17, "black", size=8.0)

    # legend (terrain + markers)
    terrain = [Line2D([0], [0], marker="s", color="w", markersize=11,
                      markerfacecolor=tuple(c / 255 for c in TILE_RGB[k]),
                      markeredgecolor="#888", label=TILE_NAME[k])
               for k in ("g", "R", "r", "b", "f")]
    markers = [
        Line2D([0], [0], marker="*", color="w", markerfacecolor="#ffd23f",
               markeredgecolor="#6e4d00", markersize=15, label="build site #id"),
        Line2D([0], [0], marker="s", color="w", markerfacecolor="white",
               markeredgecolor=GOOD, markeredgewidth=2, markersize=12, label="facility (staffed)"),
        Line2D([0], [0], marker="s", color="w", markerfacecolor="white",
               markeredgecolor="#7a7f85", markeredgewidth=2, markersize=12, label="facility (idle)"),
        Line2D([0], [0], marker="s", color="w", markerfacecolor=CRIT,
               markeredgecolor="black", markersize=12, label="community deficit"),
        Line2D([0], [0], marker="h", color="w", markerfacecolor="#8a6fd6",
               markeredgecolor="black", markersize=13, label="motel"),
    ]
    leg = axm.legend(handles=terrain + markers, loc="upper left", fontsize=7.5,
                     ncol=2, framealpha=0.92, borderpad=0.6)
    leg.set_zorder(20)

    # ── PANEL ───────────────────────────────────────────────────────────────
    axp.set_xlim(0, 1); axp.set_ylim(0, 1); axp.axis("off")

    def bar(y, label, frac, sub, color):
        axp.text(0.0, y + 0.022, label, fontsize=10.5, fontweight="bold", color="#222")
        axp.add_patch(FancyBboxPatch((0.0, y - 0.028), 0.78, 0.034,
                      boxstyle="round,pad=0.002", facecolor="#eceff1", edgecolor="none"))
        axp.add_patch(FancyBboxPatch((0.0, y - 0.028), 0.78 * max(0.0, min(1.0, frac)),
                      0.034, boxstyle="round,pad=0.002", facecolor=color, edgecolor="none"))
        axp.text(0.80, y - 0.012, sub, fontsize=9.5, color="#333", va="center")

    y = 0.97
    axp.text(0.0, y, "SERVICE PIPELINES  (community demand vs supply)",
             fontsize=11, fontweight="bold", color="#444"); y -= 0.06

    tot_cap = sum((c.get("resources", {}) or {}).get("foodPacksCapacity", 0) for c in communities)
    tot_food = sum((c.get("resources", {}) or {}).get("foodPacks", 0) for c in communities)
    frac = tot_food / tot_cap if tot_cap else 1.0
    bar(y, "Food (delivered to communities)", frac,
        f"{tot_food}/{tot_cap}  ({frac*100:.0f}%)", _fill_color(frac)); y -= 0.075

    stocked = sum((f.get("resources", {}) or {}).get("foodPacks", 0) for f in builts)
    axp.text(0.0, y + 0.012, f"   -> {stocked} food packs stocked in kitchens, awaiting delivery",
             fontsize=8.5, color="#a0410a", style="italic"); y -= 0.05

    mcap = sum(m.get("populationCapacity", 0) for m in motels)
    mpop = sum(m.get("currentPopulation", 0) for m in motels)
    bar(y, "Lodging (motel occupancy)", (mpop / mcap if mcap else 0.0), f"{mpop}/{mcap}", "#8a6fd6"); y -= 0.075

    creq = rm.get("caseworkRequested", 0); cproc = rm.get("caseworkProcessed", 0)
    bar(y, "Casework processed", (cproc / creq if creq else 1.0), f"{cproc}/{creq}", GOOD); y -= 0.09

    axp.text(0.0, y, "WORKFORCE", fontsize=11, fontweight="bold", color="#444"); y -= 0.05
    ft, fu = wf.get("freeTrainedWorkers", 0), wf.get("freeUntrainedWorkers", 0)
    wt, wu = wf.get("workingTrainedWorkers", 0), wf.get("workingUntrainedWorkers", 0)
    intr = wf.get("untrainedWorkersInTraining", 0)
    total = max(1, ft + fu + wt + wu + intr)
    xx = 0.0
    for name, val, col in [("free", ft + fu, "#8bc34a"), ("working", wt + wu, "#3d7fc9"),
                           ("training", intr, "#e8a33d")]:
        w = 0.78 * val / total
        if w > 0:
            axp.add_patch(Rectangle((xx, y - 0.03), w, 0.034, facecolor=col, edgecolor="white"))
        xx += w
    axp.text(0.80, y - 0.012, f"free {ft+fu} | work {wt+wu} | trn {intr}", fontsize=9, va="center")
    y -= 0.045
    axp.text(0.0, y, f"   trained {ft+wt} / untrained {fu+wu}   (cap {wf.get('totalWorkforceCapacity','?')})",
             fontsize=8.5, color="#555"); y -= 0.07

    axp.text(0.0, y, "BUDGET & SPEND", fontsize=11, fontweight="bold", color="#444"); y -= 0.05
    spend = [("food", rm.get("foodSpend", 0)), ("lodging", rm.get("lodgingSpend", 0)),
             ("workers", rm.get("workerSpend", 0)), ("casework", rm.get("caseworkSpend", 0))]
    axp.text(0.0, y, "   " + "    ".join(f"{n} \\${v:,}" for n, v in spend),
             fontsize=9.5, color="#333"); y -= 0.06

    deficits = sum(1 for c in communities
                   if (c.get("resources", {}) or {}).get("foodPacks", 0) <
                      (c.get("resources", {}) or {}).get("foodPacksCapacity", 0))
    note = (f"{len(builts)} facilities built, {stocked} food stocked, "
            f"but {deficits}/{len(communities)} communities still in deficit "
            f"— supply built, not delivered.")
    axp.add_patch(FancyBboxPatch((0.0, y - 0.085), 0.97, 0.085,
                  boxstyle="round,pad=0.01", facecolor="#fff3e0", edgecolor="#e8a33d"))
    axp.text(0.015, y - 0.042, "⚠  " + note, fontsize=9.0, color="#7a4a00", va="center")

    fig.savefig(out_path, facecolor="white", bbox_inches="tight")
    plt.close(fig)
    return out_path


if __name__ == "__main__":
    import sys
    src = sys.argv[1] if len(sys.argv) > 1 else "arc_midgame_state.json"
    out = sys.argv[2] if len(sys.argv) > 2 else "arc_dashboard.png"
    grd = sys.argv[3] if len(sys.argv) > 3 else "arc_map_grid.json"
    print(render_dashboard(json.load(open(src)), out, grd))
