"""The pareto policy family: standing commitments shared by the surrogate search and the game.

The benchmark's pareto baseline (bench/baselines/pareto.py) plays a member of it; oracle/pareto_sweep.py
searches the family by playing that same baseline on the exact surrogate (oracle.sim.env.SimEnv), so a
member's sweep score is its game score.

A member is nine knobs:
    n_shelter, n_kitchen, n_casework   how many of each to build
    start, spacing                     first build decision, and decisions between builds
    food       "kitchen10" haul from a kitchen  | "paid" buy the immediate option
    reloc      "shelter" route to shelters      | "motel" route to the motel
    answer_cw  take offered casework actions (0/1)
    switch     decision at which food and reloc both flip to the other rule (None = never)

Build order is casework -> shelters -> kitchens, one building per scheduled round: casework has the
longest chain to payoff (build, staff, then requests mature once residents have been housed a
while), and paid food covers the gap until kitchens come online. DEFAULT is the frontier plan
measured on Unity (job 36031).
"""
from __future__ import annotations

import itertools
import json
import os

DEFAULT = dict(n_shelter=6, n_kitchen=2, n_casework=3, start=0, spacing=1,
               food="kitchen10", reloc="shelter", answer_cw=1, switch=None)

# The pinned map config (StreamingAssets/map_config.json) places 12 AbandonedSite objects, one
# building each; a plan that asks for more is not buildable.
MAX_SITES = 12

GRID = dict(
    n_shelter=[0, 1, 2, 3, 4, 5, 6, 7, 8],
    n_kitchen=[0, 1, 2, 3, 4, 5],
    n_casework=[0, 1, 2, 3],
    start=[0, 2],
    spacing=[1, 2],
    food=["kitchen10", "paid"],
    reloc=["motel", "shelter"],
    answer_cw=[0, 1],
    switch=[None, 6, 10, 14, 18, 22],
)


def from_env() -> dict:
    """DEFAULT updated with the JSON in ARC_FAMILY_CFG (how a sweep hands a member to Unity)."""
    cfg = dict(DEFAULT)
    raw = os.environ.get("ARC_FAMILY_CFG", "").strip()
    if raw:
        cfg.update(json.loads(raw))
    return cfg


def grid_members() -> list:
    """Every buildable member of GRID."""
    keys = list(GRID)
    members = (dict(zip(keys, v)) for v in itertools.product(*(GRID[k] for k in keys)))
    return [m for m in members if m["n_shelter"] + m["n_kitchen"] + m["n_casework"] <= MAX_SITES]


def rules(cfg: dict, rnd: int) -> tuple:
    """(food rule, relocation rule) in force at decision `rnd`, applying `switch`."""
    food, reloc = cfg["food"], cfg["reloc"]
    if cfg.get("switch") is not None and rnd >= cfg["switch"]:
        food = "paid" if food == "kitchen10" else "kitchen10"
        reloc = "motel" if reloc == "shelter" else "shelter"
    return food, reloc


def build_schedule(cfg: dict) -> dict:
    """decision -> building type to start at that decision."""
    order = (["CaseworkSite"] * cfg["n_casework"] + ["Shelter"] * cfg["n_shelter"]
             + ["Kitchen"] * cfg["n_kitchen"])
    return {cfg["start"] + i * cfg["spacing"]: b for i, b in enumerate(order)}


# The two guards a member applies before acting on its rules, in both engines: a rule is only
# followed when the game can carry it out this round; otherwise the member falls back to the
# motel / the paid option (Unity refuses a shelter relocation without beds and a kitchen haul
# without stock).
def route_to_shelter(cfg: dict, rnd: int, free_beds: int, group: int) -> bool:
    """Send a relocation group to shelters: the rule says so and operational shelters have free
    beds for the whole group."""
    return rules(cfg, rnd)[1] == "shelter" and free_beds >= group


def haul_from_kitchen(cfg: dict, rnd: int, free_vehicles: int, kitchen_stock: int, load: int) -> bool:
    """Fill a food request from a kitchen: the rule says so, a vehicle is free and operational
    kitchens hold at least one load."""
    return rules(cfg, rnd)[0] == "kitchen10" and free_vehicles > 0 and kitchen_stock >= load
