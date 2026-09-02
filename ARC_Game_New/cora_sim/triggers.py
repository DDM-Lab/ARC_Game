"""Port of the ProbabilityTrigger rolls in one task-generation pass.

SCOPE. This models the DRAW SEQUENCE and each roll's outcome -- not yet which tasks
actually fire. Firing needs the AND/OR reduction over every trigger category (resource,
budget, satisfaction, flood, ...), which needs the facility and economy model that
tasks.py will bring. Stream position is worth having on its own: it is the last of the
three draw sites the census found, so with this the surrogate accounts for every random
number a round consumes.

THREE THINGS THAT SET THE DRAW COUNT, NONE OF THEM OBVIOUS FROM THE TASK LIST:

1. AreTriggersActivatedForFacility does NOT short-circuit. Every trigger of every task is
   evaluated into a list and the AND/OR reduction happens afterwards, so a task whose first
   trigger already failed still rolls its probability trigger. Short-circuiting here would
   silently shorten the stream.

2. Non-global tasks roll ONCE PER SUITABLE FACILITY. The draw count is therefore state
   dependent -- build a shelter and the pass gets longer. Measured: 3 communities and no
   shelters gives exactly 3 draws per pass, in all 85 passes of the fixture.

3. `Random.Range(0f, 1f) < probability` is REVERSED (see rng.range_float), so the test is
   `mantissa >= T`, not `< T`. Same marginal probability, opposite outcome on any given
   draw. Pinned against 55 logged firings: reversed 55/55, forward 0/55.

FACILITY ORDER IS NOT SORTED. FindAllSuitableFacilities returns FindObjectsOfType order,
observed as Community01, Community03, Community02 -- neither name nor creation order, and
not contractual in Unity. Each facility rolls its own trigger, so this order decides which
community gets a task. The port takes the order from its caller rather than inventing one;
see PLAN.md for why the C# is not being silently sorted.
"""
from __future__ import annotations

import json as _json
import os as _os

from .rng import range01_threshold_lt

_CONST_PATH = _os.path.join(_os.path.dirname(_os.path.abspath(__file__)),
                            "corpus", "sim_constants.json")


def load_inventory(path=None):
    """allTasks in database order, with the probability triggers each one rolls."""
    d = _json.load(open(path or _CONST_PATH))
    rows = d.get("taskTriggers")
    if rows is None:
        raise KeyError("sim_constants.json has no 'taskTriggers' block -- re-export it "
                       "from a build that includes the trigger inventory")
    return [t for t in rows if t]


INVENTORY = load_inventory()

# One bisected threshold per distinct probability, computed once at import. Every roll in
# the game reuses these, so the per-draw cost is an integer compare.
_THRESHOLDS = {}


def threshold(p: float) -> int:
    t = _THRESHOLDS.get(p)
    if t is None:
        t = _THRESHOLDS[p] = range01_threshold_lt(p)
    return t


def roll_pass(rng, facilities_for, inventory=None, marks=None):
    """One CheckTriggeredTasksPerFacility pass, as far as the RNG is concerned.

    `facilities_for(task)` returns the ordered facility identifiers a non-global task
    rolls against; global tasks roll once against None. Returns a list of
    (task_id, facility, [outcome per probability trigger]) in draw order."""
    out = []
    for task in (INVENTORY if inventory is None else inventory):
        probs = task.get("probabilities") or []
        targets = [None] if task.get("isGlobalTask") else list(facilities_for(task))
        if not probs:
            continue                      # no draws; other trigger categories are silent
        for facility in targets:
            outcomes = []
            for p in probs:
                if marks is not None:
                    marks.append("draw:TaskTrigger.probability")
                outcomes.append(rng.range01_lt(threshold(p)))
            out.append((task["taskId"], facility, outcomes))
    return out
