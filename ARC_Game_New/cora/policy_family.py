"""The pareto policy family: standing commitments shared by the surrogate search and the game.

The surrogate searches this family (oracle/pareto_sweep.py) and the benchmark's pareto baseline
plays a member of it in Unity (bench/baselines/pareto.py). Both read the family from here, so an
engine-vs-engine comparison measures the two engines rather than two implementations of the
policy.

A member is nine knobs:
    n_shelter, n_kitchen, n_casework   how many of each to build
    start, spacing                     first build round, and rounds between builds
    food       "kitchen10" haul from a kitchen  | "paid" buy the immediate option
    reloc      "shelter" route to shelters      | "motel" route to the motel
    answer_cw  take offered casework actions (0/1)
    switch     round at which food and reloc both flip to the other rule (None = never)

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

# The map ships 15 AbandonedSite objects (Scenes/MainScene.unity), one building each; a plan that
# asks for more is not buildable.
MAX_SITES = 15

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
    """(food rule, relocation rule) in force at round `rnd`, applying `switch`."""
    food, reloc = cfg["food"], cfg["reloc"]
    if cfg.get("switch") is not None and rnd >= cfg["switch"]:
        food = "paid" if food == "kitchen10" else "kitchen10"
        reloc = "motel" if reloc == "shelter" else "shelter"
    return food, reloc


def build_schedule(cfg: dict) -> dict:
    """round -> building type to start that round."""
    order = (["CaseworkSite"] * cfg["n_casework"] + ["Shelter"] * cfg["n_shelter"]
             + ["Kitchen"] * cfg["n_kitchen"])
    return {cfg["start"] + i * cfg["spacing"]: b for i, b in enumerate(order)}


def macro(cfg: dict):
    """The member as a per-round macro for the surrogate:
    rnd -> (building or None, workers to hire, food rule, relocation rule, answer_cw)."""
    sched = build_schedule(cfg)

    def at(rnd):
        b = sched.get(rnd)
        food, reloc = rules(cfg, rnd)
        return (b, 4 if b else 0, food, reloc, cfg["answer_cw"])
    return at
