"""Client stay tracking: the third stochastic subsystem, and the one that was missed.

WHY THIS MODULE EXISTS AT ALL. The first draw census concluded the round's stochastic
surface was three sites -- flood, weather, probability triggers -- and that the economy drew
nothing. That was measured on IDLE episodes, and it was wrong for real play. Re-running the
census on genuinely action-bearing episodes found ~1500 unexplained draws per episode across
two thirds of all rounds. They are all here:

    draw:Client.caseworkNeed   one Random.value PER CLIENT in every delivered group
    draw:Client.stayDuration   one Random.Range per group
    draw:Client.caseworkGen    one Random.value per unresolved group per round

On a played episode caseworkNeed alone outnumbers every non-flood draw site combined (1270
vs 69 trigger draws in one capture), because it fires once per PERSON and relocations move
people in hundreds. Miss it and the stream desynchronises the moment anyone is housed --
which is exactly why an idle-episode census is not evidence about a game being played.

CONSTANTS, AS ALWAYS, FROM THE SCENE. caseworkNeedProbability is 23.4 in the running game
against 40 in the .cs initialiser.
"""
from __future__ import annotations

import json as _json
import os as _os

from .rng import f32, f32mul, threshold_for

_CONST_PATH = _os.path.join(_os.path.dirname(_os.path.abspath(__file__)),
                            "corpus", "sim_constants.json")


def load_client_constants(path=None):
    d = _json.load(open(path or _CONST_PATH))
    c = d.get("clientStay")
    if not c:
        raise KeyError("sim_constants.json has no 'clientStay' block -- re-export it from "
                       "a build that includes it")
    return {"casework_need_pct": float(c["caseworkNeedProbability"]),
            "base_casework_pct": float(c["baseCaseworkProbability"]),
            "growth": float(c["probabilityGrowthFactor"]),
            "min_stay": int(c["minStayRounds"]),
            "max_stay": int(c["maxStayRounds"]),
            "overstay": int(c["overstayThreshold"])}


C = load_client_constants()
# `Random.value < (pct / 100f)` -- the division is float32 in C#, so the threshold is
# bisected against the float32 quotient rather than the float64 one.
_NEED_THRESHOLD = threshold_for(f32(C["casework_need_pct"] / 100.0))


class ClientGroup:
    """One delivered group of people, tracked from arrival to departure."""

    __slots__ = ("count", "with_need", "arrival_round", "departure_round",
                 "departed", "casework_generated", "facility")

    def __init__(self, count, with_need, arrival_round, departure_round, facility=""):
        self.count = count
        self.with_need = with_need
        self.arrival_round = arrival_round
        self.departure_round = departure_round
        self.departed = False
        self.casework_generated = False
        self.facility = facility

    def clone(self):
        g = ClientGroup.__new__(ClientGroup)
        g.count, g.with_need = self.count, self.with_need
        g.arrival_round, g.departure_round = self.arrival_round, self.departure_round
        g.departed, g.casework_generated = self.departed, self.casework_generated
        g.facility = self.facility
        return g

    @property
    def without_need(self):
        return self.count - self.with_need


class ClientTracker:
    """All tracked groups. Iteration order is insertion order, as in the C# List."""

    __slots__ = ("groups",)

    def __init__(self):
        self.groups = []

    def clone(self):
        t = ClientTracker.__new__(ClientTracker)
        t.groups = [g.clone() for g in self.groups]
        return t

    def register_arrival(self, rng, count, current_round, facility="", marks=None):
        """A delivery to a shelter or motel. Draws count+1 randoms, in this exact order.

        The per-client loop runs to `count` REGARDLESS of outcome -- it is not a sampled
        proportion, it is one Bernoulli per person -- so a 300-person relocation consumes
        301 draws. Batching this into a single binomial would give the same distribution
        and the wrong stream position, which is the sort of optimisation that looks free
        and silently desynchronises everything downstream."""
        with_need = 0
        for _ in range(count):
            if marks is not None:
                marks.append("draw:Client.caseworkNeed")
            if rng.value_lt(_NEED_THRESHOLD):
                with_need += 1
        if marks is not None:
            marks.append("draw:Client.stayDuration")
        stay = rng.range_int(C["min_stay"], C["max_stay"] + 1)
        self.groups.append(ClientGroup(count, with_need, current_round,
                                       current_round + stay, facility))

    def update(self, rng, current_round, counters, marks=None):
        """Per-round evaluation: natural departures, then casework generation.

        The casework probability GROWS with the stay: base * growth^(Y-1) where Y is rounds
        in facility, clamped to 100. So a group that lingers becomes near-certain to
        request casework -- the demand is created by leaving people housed, which is what
        makes the motel a compounding rather than a flat cost.

        caseworkRequested is credited the WHOLE group count, not just the members flagged
        as needing casework. That asymmetry is in RewardMetricsTracker and it matters: the
        denominator of the casework satisfaction term is bigger than the population that
        triggered it."""
        for group in list(self.groups):
            rounds_in = current_round - group.arrival_round
            if (not group.departed and group.without_need > 0
                    and current_round >= group.departure_round):
                group.departed = True
            if group.with_need > 0 and not group.casework_generated:
                y = max(1, rounds_in)
                pct = f32mul(C["base_casework_pct"], C["growth"] ** (y - 1))
                pct = min(max(pct, 0.0), 100.0)
                if marks is not None:
                    marks.append("draw:Client.caseworkGen")
                if rng.value_lt(threshold_for(f32(pct / 100.0))):
                    group.casework_generated = True
                    counters["caseworkRequested"] += group.count

    def process_home(self, quantity, counters):
        """A delivery to a casework site sends people home. Credits caseworkProcessed."""
        remaining = quantity
        for group in self.groups:
            if remaining <= 0:
                break
            take = min(group.count, remaining)
            group.count -= take
            group.with_need = max(0, group.with_need - take)
            remaining -= take
        processed = quantity - remaining
        if processed > 0:
            counters["caseworkProcessed"] += processed
        self.groups = [g for g in self.groups if g.count > 0]
        return processed
