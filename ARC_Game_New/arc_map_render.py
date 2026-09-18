"""Synthetic top-down schematic renderer for the ARC disaster-response game.

Renders a 2D map of the game world directly from a ``game_state`` dict, with no
dependency on Unity. The output is a PNG suitable for embedding in LLM prompts
(vision models) and for seeding demo / VLM figures.

Coordinate system
-----------------
The world is top-down 2D. We use ``position["x"]`` (horizontal) and
``position["y"]`` (vertical) as plot coordinates and ignore ``position["z"]``.

Data used from ``game_state``
-----------------------------
* ``constructionState.availableSites`` -> a list of construction sites, each
  ``{"siteId": int, "siteName": str, "isAvailable": bool,
     "position": {"x","y","z"}}``. The ``siteId`` is what an LLM references in a
  ``<build>TYPE,SITE_ID</build>`` command, so every site is labeled with its id.
* ``mapState.facilities`` -> a list of facilities. In the observed snapshot each
  facility DOES carry a ``position`` block, plus ``facilityName``,
  ``buildingType`` (Community / Motel / Kitchen / Shelter / CaseworkSite),
  ``buildingStatus``, ``assignedWorkforce``, ``requiredWorkforce``,
  ``currentPopulation``, ``populationCapacity`` and a ``resources`` dict with
  ``foodPacks`` / ``foodPacksCapacity``.

Robustness
----------
If facilities lack a usable ``position`` they are not silently lost: they are
collected into a side text panel instead of being plotted. Sites are always
plotted as long as they have positions. Missing numeric fields default to 0.
"""

from __future__ import annotations

import base64
import json
import os
import tempfile
from typing import Any, Dict, List, Optional, Tuple

import matplotlib

matplotlib.use("Agg")  # headless / non-interactive

import matplotlib.pyplot as plt
from matplotlib.lines import Line2D


# --- visual styling per building type -------------------------------------
# Each entry: (display color, matplotlib marker, short label used in legend)
_FACILITY_STYLE: Dict[str, Tuple[str, str, str]] = {
    "Community": ("#2c7fb8", "s", "Community"),
    "Motel": ("#6a51a3", "h", "Motel"),
    "Kitchen": ("#e6550d", "^", "Kitchen"),
    "Shelter": ("#31a354", "P", "Shelter"),
    "CaseworkSite": ("#756bb1", "D", "Casework Site"),
}
_FACILITY_DEFAULT = ("#636363", "o", "Facility")

_SITE_AVAILABLE = ("#d95f0e", "*", "Available site")
_SITE_OCCUPIED = ("#969696", "x", "Occupied site")


def _get(d: Any, key: str, default: Any = None) -> Any:
    return d.get(key, default) if isinstance(d, dict) else default


def _position(entity: Dict[str, Any]) -> Optional[Tuple[float, float]]:
    """Return (x, y) for an entity, or None if no usable position is present."""
    pos = _get(entity, "position")
    if not isinstance(pos, dict):
        return None
    x, y = pos.get("x"), pos.get("y")
    if x is None or y is None:
        return None
    try:
        return float(x), float(y)
    except (TypeError, ValueError):
        return None


def _short_facility_name(name: str) -> str:
    """Compact a facility name for on-map labels (e.g. 'Community01' -> 'C01')."""
    if not name:
        return "?"
    if name.startswith("Community"):
        return "C" + name[len("Community"):]
    return name


def _facility_label(fac: Dict[str, Any]) -> str:
    """Build a multi-line label: name + status + workers + pop/food."""
    name = _short_facility_name(_get(fac, "facilityName", "?"))
    btype = _get(fac, "buildingType", "")
    status = _get(fac, "buildingStatus", "") or ""

    assigned = int(_get(fac, "assignedWorkforce", 0) or 0)
    required = int(_get(fac, "requiredWorkforce", 0) or 0)
    pop = int(_get(fac, "currentPopulation", 0) or 0)
    cap = int(_get(fac, "populationCapacity", 0) or 0)
    res = _get(fac, "resources", {}) or {}
    food = int(_get(res, "foodPacks", 0) or 0)
    food_cap = int(_get(res, "foodPacksCapacity", 0) or 0)

    lines: List[str] = [name]
    if status:
        lines.append(status)
    if required > 0 or assigned > 0:
        lines.append(f"wf {assigned}/{required}")
    # Show population for places that hold people; food for places that stock it.
    if cap > 0:
        lines.append(f"pop {pop}/{cap}")
    if food_cap > 0:
        lines.append(f"food {food}/{food_cap}")
    return "\n".join(lines)


def _style_for(btype: str) -> Tuple[str, str, str]:
    return _FACILITY_STYLE.get(btype, _FACILITY_DEFAULT)


def render_map(
    game_state: Dict[str, Any],
    out_path: Optional[str] = None,
    title: Optional[str] = None,
) -> str:
    """Render a top-down schematic of the game map to a PNG.

    Parameters
    ----------
    game_state:
        The ``game_state`` dict (the inner value, not the ``{"game_state": ...}``
        wrapper). Reads ``constructionState.availableSites`` and
        ``mapState.facilities``.
    out_path:
        Destination PNG path. If ``None`` a temp file is created.
    title:
        Optional plot title. A sensible default summarizing day/budget is used
        when omitted (falls back gracefully if those fields are absent).

    Returns
    -------
    str
        The path of the written PNG.

    Notes
    -----
    Construction sites are always plotted (when they have positions) and labeled
    with their ``siteId`` — that id is the reference an LLM uses in a build
    command. Facilities are plotted when they expose a position; otherwise they
    are listed in a side text panel so no information is dropped.
    """
    if out_path is None:
        fd, out_path = tempfile.mkstemp(suffix=".png", prefix="arc_map_")
        os.close(fd)

    construction = _get(game_state, "constructionState", {}) or {}
    sites = _get(construction, "availableSites", []) or []
    map_state = _get(game_state, "mapState", {}) or {}
    facilities = _get(map_state, "facilities", []) or []

    fig, ax = plt.subplots(figsize=(11, 8.5), dpi=130)

    # Track which legend entries are actually used.
    used_facility_types: Dict[str, Tuple[str, str, str]] = {}
    used_site_available = False
    used_site_occupied = False

    # Collect coords to set bounds; collect facilities lacking positions.
    xs: List[float] = []
    ys: List[float] = []
    facilities_without_position: List[Dict[str, Any]] = []

    # --- plot construction sites -----------------------------------------
    for site in sites:
        xy = _position(site)
        if xy is None:
            continue
        x, y = xy
        xs.append(x)
        ys.append(y)
        available = bool(_get(site, "isAvailable", True))
        color, marker, _ = _SITE_AVAILABLE if available else _SITE_OCCUPIED
        if available:
            used_site_available = True
        else:
            used_site_occupied = True
        ax.scatter(
            [x], [y],
            c=color, marker=marker, s=200 if available else 120,
            edgecolors="black", linewidths=0.7, zorder=3,
        )
        # Label with the siteId — must be clearly readable for build commands.
        site_id = _get(site, "siteId", "?")
        ax.annotate(
            f"#{site_id}",
            (x, y),
            textcoords="offset points", xytext=(0, 9),
            ha="center", va="bottom",
            fontsize=9, fontweight="bold", color="#7a3b00",
            zorder=5,
        )

    # --- plot facilities --------------------------------------------------
    for fac in facilities:
        xy = _position(fac)
        btype = _get(fac, "buildingType", "") or ""
        color, marker, legend_label = _style_for(btype)
        if xy is None:
            facilities_without_position.append(fac)
            continue
        x, y = xy
        xs.append(x)
        ys.append(y)
        used_facility_types[btype or legend_label] = (color, marker, legend_label)
        ax.scatter(
            [x], [y],
            c=color, marker=marker, s=320,
            edgecolors="black", linewidths=0.9, zorder=4,
        )
        ax.annotate(
            _facility_label(fac),
            (x, y),
            textcoords="offset points", xytext=(0, -10),
            ha="center", va="top",
            fontsize=7.5, color="black",
            bbox=dict(boxstyle="round,pad=0.25", fc="white", ec=color,
                      alpha=0.85, lw=0.8),
            zorder=6,
        )

    # --- axes / framing ---------------------------------------------------
    ax.set_aspect("equal", adjustable="datalim")
    if xs and ys:
        pad_x = max((max(xs) - min(xs)) * 0.12, 1.5)
        pad_y = max((max(ys) - min(ys)) * 0.12, 1.5)
        ax.set_xlim(min(xs) - pad_x, max(xs) + pad_x)
        ax.set_ylim(min(ys) - pad_y, max(ys) + pad_y)
    ax.grid(True, linestyle=":", linewidth=0.5, color="#cccccc", zorder=0)
    ax.set_xlabel("x (world units)")
    ax.set_ylabel("y (world units)")

    if title is None:
        session = _get(game_state, "sessionInfo", {}) or {}
        budget_block = _get(game_state, "satisfactionAndBudget", {}) or {}
        gt = _get(session, "currentGameTime", "")
        budget = _get(budget_block, "budget", None)
        sat = _get(budget_block, "satisfaction", None)
        parts = ["ARC Map Schematic"]
        if gt:
            parts.append(str(gt))
        meta = []
        if budget is not None:
            meta.append(f"budget ${budget}")
        if sat is not None:
            meta.append(f"satisfaction {sat}")
        if meta:
            parts.append("(" + ", ".join(meta) + ")")
        title = "  ".join(parts)
    ax.set_title(title, fontsize=13, fontweight="bold")

    # --- legend -----------------------------------------------------------
    handles: List[Line2D] = []
    if used_site_available:
        c, m, lab = _SITE_AVAILABLE
        handles.append(_legend_handle(c, m, lab))
    if used_site_occupied:
        c, m, lab = _SITE_OCCUPIED
        handles.append(_legend_handle(c, m, lab))
    # Stable ordering for facility legend entries.
    for btype in sorted(used_facility_types.keys()):
        c, m, lab = used_facility_types[btype]
        handles.append(_legend_handle(c, m, lab))
    if handles:
        ax.legend(
            handles=handles, loc="upper left",
            bbox_to_anchor=(1.01, 1.0), frameon=True,
            fontsize=9, title="Legend", title_fontsize=10,
            borderaxespad=0.0,
        )

    # --- side panel for facilities without positions ---------------------
    if facilities_without_position:
        lines = ["Facilities (no position):"]
        for fac in facilities_without_position:
            lines.append("- " + _facility_label(fac).replace("\n", " | "))
        ax.text(
            1.01, 0.0, "\n".join(lines),
            transform=ax.transAxes, ha="left", va="bottom",
            fontsize=7.5, family="monospace",
            bbox=dict(boxstyle="round,pad=0.4", fc="#f7f7f7", ec="#999999"),
        )

    fig.tight_layout()
    fig.savefig(out_path, dpi=130, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    return out_path


def _legend_handle(color: str, marker: str, label: str) -> Line2D:
    return Line2D(
        [0], [0], marker=marker, color="none",
        markerfacecolor=color, markeredgecolor="black",
        markersize=11, label=label, linestyle="none",
    )


def render_map_base64(game_state: Dict[str, Any], **kwargs: Any) -> str:
    """Render the map and return a base64-encoded PNG string (no data: prefix).

    Suitable for an OpenAI-style image content block, e.g.::

        {"type": "image_url",
         "image_url": {"url": "data:image/png;base64," + render_map_base64(gs)}}

    Accepts the same keyword arguments as :func:`render_map` except ``out_path``
    (rendering goes to an in-memory buffer). A passed ``out_path`` is ignored.
    """
    kwargs.pop("out_path", None)
    title = kwargs.pop("title", None)

    tmp_path = render_map(game_state, out_path=None, title=title)
    try:
        with open(tmp_path, "rb") as fh:
            data = fh.read()
    finally:
        try:
            os.remove(tmp_path)
        except OSError:
            pass
    return base64.b64encode(data).decode("ascii")


if __name__ == "__main__":
    snapshot_path = "/tmp/arc_snapshot.json"
    with open(snapshot_path, "r") as fh:
        snapshot = json.load(fh)
    gs = snapshot.get("game_state", snapshot)

    out = render_map(gs, out_path="/tmp/arc_map_test.png")
    size = os.path.getsize(out)
    print(f"Wrote PNG: {out}")
    print(f"PNG size: {size} bytes")

    b64 = render_map_base64(gs)
    print(f"base64 length: {len(b64)} chars (starts: {b64[:24]}...)")
