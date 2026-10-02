"""DailyReportData: the cumulative ledger Unity's score is computed from.

On the bench-v6 build satisfaction and efficiency are not set by task outcomes; DailyReportData
recomputes each component of the score as its counters move and pushes the CHANGE into the live
fields (ApplyDelta):

    satisfaction += d(S_x * 0.2 * 1000)   for x in food, lodging, worker use, casework
    efficiency   += d(C_x * (1/3) * 1000) for x in food, lodging, worker

so the live values are the components as they were last applied, plus whatever else moved them
directly (choice Satisfaction impacts, training completions). S_Waste is computed but only the
daily report UI applies it, which the headless game never shows. Report.reward_metrics() exports
the same fields RewardMetricsTracker does, so cora.scoring scores the surrogate with the code it
scores Unity with.
"""
from __future__ import annotations

from .rng import f32

# Unity stores every value as float32 but evaluates each expression at higher precision,
# rounding only when a result is stored (a return, a field, a local). The port does the same:
# compute in double, f32() at each store. The exported satisfaction is truncated to an int, so
# a value one ulp low (15.999998 where Unity holds 16.0) reads one point low.
SCORE_SCALE = 1000.0
SAT_W = f32(0.2)
EFF_W = f32(1.0 / 3.0)

_SAT = ("food", "lodging", "worker", "casework")
_EFF = ("food", "lodging", "worker")


def _clamp01(x: float) -> float:
    return 0.0 if x < 0 else 1.0 if x > 1 else x


def _div(a, b) -> float:
    """(float)a / b, stored."""
    return f32(a / b)


class Report:
    __slots__ = ("food_consumed", "food_needed", "food_wasted",
                 "lodging_requested", "lodging_satisfied", "nights_consumed", "nights_needed",
                 "idle_rounds", "working_rounds", "training_rounds", "pool_rounds",
                 "awaiting_casework", "requested_casework", "casework_available", "casework_groups",
                 "food_spend", "lodging_spend", "worker_request_cost", "worker_training_cost",
                 "applied_sat", "applied_eff", "mins")

    def __init__(self, mins: dict):
        """`mins`: the cost-efficiency minimums (per pack, per lodging night, per worker-round)."""
        for k in self.__slots__:
            setattr(self, k, 0)
        self.casework_groups = {}          # group id -> FROZEN requested size
        self.applied_sat = dict.fromkeys(_SAT, 0.0)
        self.applied_eff = dict.fromkeys(_EFF, 0.0)
        self.mins = mins

    def clone(self) -> "Report":
        r = Report.__new__(Report)
        for k in self.__slots__:
            setattr(r, k, getattr(self, k))
        r.casework_groups = dict(self.casework_groups)
        r.applied_sat = dict(self.applied_sat)
        r.applied_eff = dict(self.applied_eff)
        return r

    # ── the component ratios (DailyReportData.S_* / C_*) ──
    def s_food(self):
        return _clamp01(_div(self.food_consumed, self.food_needed)) if self.food_needed > 0 else 0.0

    def s_lodging(self):
        return _clamp01(_div(self.lodging_satisfied, self.lodging_requested)) if self.lodging_requested > 0 else 0.0

    def s_worker(self):
        active = self.idle_rounds + self.working_rounds + self.training_rounds
        return f32(_clamp01(1.0 - self.idle_rounds / active)) if active > 0 else 0.0

    def s_waste(self):
        total = self.food_consumed + self.food_wasted
        return f32(1.0 - self.food_wasted / total) if total > 0 else 0.0

    def s_casework(self):
        if self.casework_available <= 0:
            return 0.0
        return f32(_clamp01(1.0 - self.awaiting_casework / self.casework_available))

    def _cost(self, spend, units, minimum):
        if units <= 0:
            return 0.0
        if not minimum:
            return 1.0
        minimum = f32(minimum)
        raw = _div(spend, units)
        return f32(_clamp01(1.0 - (raw - minimum) / (49.0 * minimum)))

    def c_food(self):
        return self._cost(self.food_spend, self.food_consumed, self.mins.get("food"))

    def c_lodging(self):
        return self._cost(self.lodging_spend, self.nights_consumed, self.mins.get("lodging"))

    def c_worker(self):
        return self._cost(self.worker_training_cost + self.worker_request_cost,
                          self.working_rounds, self.mins.get("worker"))

    # ── ApplyDelta ──
    def _apply(self, econ, part, sat=True):
        if sat:
            new = {"food": self.s_food, "lodging": self.s_lodging, "worker": self.s_worker,
                   "casework": self.s_casework}[part]()
            new = f32(new * SAT_W * SCORE_SCALE)
            delta = f32(new - self.applied_sat[part])
            if abs(delta) >= 0.001:
                self.applied_sat[part] = new
                econ.add_satisfaction(delta)
        else:
            new = f32({"food": self.c_food, "lodging": self.c_lodging,
                       "worker": self.c_worker}[part]() * EFF_W * SCORE_SCALE)
            delta = f32(new - self.applied_eff[part])
            if abs(delta) >= 0.001:
                self.applied_eff[part] = new
                econ.efficiency = f32(econ.efficiency + delta)

    # ── the events that feed it ──
    def food(self, econ, consumed=0, needed=0):
        """RecordFoodConsumptionCumulative (storage feeding cycles, community demand and food)."""
        self.food_consumed += consumed
        self.food_needed += needed
        self._apply(econ, "food")
        self._apply(econ, "food", sat=False)

    def food_spent(self, econ, amount):
        self.food_spend += amount
        self._apply(econ, "food", sat=False)

    def lodging_spent(self, econ, amount):
        self.lodging_spend += amount
        self._apply(econ, "lodging", sat=False)

    def worker_requested(self, econ, cost):
        self.worker_request_cost += cost
        self._apply(econ, "worker", sat=False)

    def worker_trained(self, econ, cost):
        self.worker_training_cost += cost
        self._apply(econ, "worker", sat=False)

    def lodging(self, econ, requested=0, satisfied=0):
        """RecordLodgingRequestedToday / RecordLodgingSatisfiedToday / ReverseLodgingRequested
        (a negative `requested`, floored at 0)."""
        self.lodging_requested = max(0, self.lodging_requested + requested)
        self.lodging_satisfied += satisfied
        self._apply(econ, "lodging")

    def nights(self, econ, housed, waiting):
        """OnDayChangedForLodgingNights: housed tonight, and what open lodging tasks still need."""
        self.nights_consumed += housed
        self.nights_needed += housed + waiting
        self._apply(econ, "lodging", sat=False)

    def casework_requested(self, econ, gid, size):
        """ClientStayTracker.OnCaseworkRequested for one group."""
        self.requested_casework += size
        self.casework_groups[gid] = self.casework_groups.get(gid, 0) + size
        self._apply(econ, "casework")

    def round_end(self, econ, idle, working, training, total, awaiting):
        """AccumulateRoundMetrics (GlobalClock.OnRoundEnd). `awaiting`: clients still needing
        casework in groups that have requested it."""
        self.idle_rounds += idle
        self.working_rounds += working
        self.training_rounds += training
        self.pool_rounds += total
        self.awaiting_casework += awaiting
        self.casework_available += sum(self.casework_groups.values())
        self._apply(econ, "worker")
        self._apply(econ, "worker", sat=False)
        self._apply(econ, "casework")

    def reward_metrics(self, econ) -> dict:
        """RewardMetricsTracker.FillUnityScore's fields."""
        return {"scoreAvailable": True,
                "liveSatisfaction": econ.satisfaction, "liveEfficiency": econ.efficiency,
                "sFood": self.s_food(), "sLodging": self.s_lodging(), "sWorkerUse": self.s_worker(),
                "sWaste": self.s_waste(), "sCasework": self.s_casework(),
                "cFood": self.c_food(), "cLodging": self.c_lodging(), "cWorker": self.c_worker(),
                "foodPacksConsumed": self.food_consumed, "foodPacksNeeded": self.food_needed,
                "foodPacksWasted": self.food_wasted,
                "lodgingNightsConsumed": self.nights_consumed, "lodgingNightsNeeded": self.nights_needed,
                "clientRoundsAwaitingCasework": self.awaiting_casework,
                "clientsRequestedCasework": self.requested_casework,
                "idleWorkerRounds": self.idle_rounds, "workingWorkerRounds": self.working_rounds,
                "trainingWorkerRounds": self.training_rounds}
