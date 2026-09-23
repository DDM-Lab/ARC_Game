"""Shared reward scoring for the ARC Game — dependency-free (no gymnasium/numpy).

Extracted from arc_game_gym_env_tcp.py so the router/episode_logger can score
live-game reward identically to the RL gym WITHOUT importing the heavy gym module
(which pulls gymnasium/numpy + subprocess/atexit machinery not present in the
router env). Both the gym env and episode_logger import from here, so offline RL
and live games agree on the objective.
"""

# ── Reward weights (TUNE THESE) ───────────────────────────────────────────────
# All scoring lives here in Python so it can be retuned without a Unity rebuild.
# Satisfaction is higher-better; Cost-Efficiency is lower-better and is SUBTRACTED.
REWARD_WEIGHTS = {
    # ── Unity formula (DEFAULT, 2026-09-23) ────────────────────────────────────────
    # score = w_sat * satisfaction + w_eff * efficiency, each on 0..1 (Unity's 0..1000 / 1000).
    # These are the two numbers the daily report shows a human, so the RL reward, the
    # benchmark and the router all optimise what a person playing the game sees. Unity has
    # no single combined score; equal weights are the one choice made here.
    "w_sat": 1.0,
    "w_eff": 1.0,
    # ── Legacy formula (only for episodes recorded before Unity exported its score) ──
    # Satisfaction (needs-met ratios are clamped to [0,1])
    "w_food": 1.0,
    "w_lodging": 1.0,
    "w_workeruse": 1.0,
    "w_casework": 1.0,      # casework/return-home processing (fraction of requesters sent home)
    # Worker-use blend (utilization > training > idle; idle = 0 per design)
    "w_working": 1.0,
    "w_training": 0.5,
    "w_idle": 0.0,
    # Cost-efficiency ($ per unit service). Small weights bring $/service into the
    # same scale as satisfaction; each cost term is capped (NOT clamped to 1).
    "w_food_cost": 0.0002,
    "w_lodging_cost": 0.0002,
    "w_worker_cost": 0.0002,
    "w_casework_cost": 0.0002,   # $ per person processed home (casework site spend / processed)
    "cost_term_cap": 1.0,   # max contribution of any single cost term
}


def _clamp01(x: float) -> float:
    return 0.0 if x < 0 else (1.0 if x > 1.0 else x)


def compute_legacy_score_components(rm: dict, w: dict = REWARD_WEIGHTS) -> dict:
    """LEGACY (pre-2026-09-23) composite reward, re-derived in Python from rewardMetrics.

    Kept so episodes recorded before Unity exported its own score can still be scored. It is
    NOT the formula humans see: 4 components not 5, task-based rather than pack/night-based
    ratios, a different worker-use term, no waste term, and a subtracted cost.

    Original description:

    Returns every term so each can be logged/graphed independently:
      satisfaction sub-terms: sat_food, sat_lodging, sat_worker_use
      cost-efficiency sub-terms: cost_food, cost_lodging, cost_worker
      aggregates: satisfaction, cost_efficiency, score (= satisfaction - cost_efficiency)
    The per-step reward is the delta of `score` between rounds (telescopes to the
    final score). All values are cumulative-to-date (so deltas are per-round).
    """
    keys = ["sat_food", "sat_lodging", "sat_worker_use", "casework_processing_sat",
            "cost_food", "cost_lodging", "cost_worker", "casework_efficiency",
            "satisfaction", "cost_efficiency", "score"]
    if not rm:
        return {k: 0.0 for k in keys}

    def ratio(num, den):
        return (num / den) if den else 0.0

    # ── Satisfaction (higher better) ──
    food = _clamp01(ratio(rm.get("foodFulfilled", 0), rm.get("foodResolved", 0))) * w["w_food"]
    lodging = _clamp01(ratio(rm.get("lodgingFulfilled", 0), rm.get("lodgingResolved", 0))) * w["w_lodging"]

    days = max(rm.get("daysCompleted", 1), 1)
    total_workers = max(rm.get("totalWorkers", 0), 1)
    worker_capacity = days * total_workers
    worker_use = _clamp01(
        (w["w_working"] * rm.get("cumWorkingWorkers", 0)
         + w["w_training"] * rm.get("cumTrainingWorkers", 0)
         + w["w_idle"] * rm.get("cumIdleWorkers", 0)) / worker_capacity
    ) * w["w_workeruse"]

    # Casework / return-home: fraction of people who requested casework that were actually
    # processed home. Mirrors the other satisfaction terms (clamped ratio × weight). Neutral (0)
    # when no casework was ever requested.
    casework_processing_sat = _clamp01(
        ratio(rm.get("caseworkProcessed", 0), rm.get("caseworkRequested", 0))
    ) * w["w_casework"]

    satisfaction = food + lodging + worker_use + casework_processing_sat

    # ── Cost-efficiency (lower better; capped, not clamped-to-1) ──
    def cost_term(spend, service, weight):
        # service==0 with spend>0 => maximally inefficient => hits the cap.
        val = (spend / max(service, 1)) * weight
        return min(val, w["cost_term_cap"])

    c_food = cost_term(rm.get("foodSpend", 0), rm.get("foodFulfilled", 0), w["w_food_cost"])
    c_lodging = cost_term(rm.get("lodgingSpend", 0), rm.get("lodgingFulfilled", 0), w["w_lodging_cost"])
    c_worker = cost_term(rm.get("workerSpend", 0), rm.get("cumWorkingWorkers", 0), w["w_worker_cost"])
    # Casework efficiency: $ spent on casework (site construction) per person processed home.
    c_casework = cost_term(rm.get("caseworkSpend", 0), rm.get("caseworkProcessed", 0), w["w_casework_cost"])
    cost_efficiency = c_food + c_lodging + c_worker + c_casework

    return {
        "sat_food": food, "sat_lodging": lodging, "sat_worker_use": worker_use,
        "casework_processing_sat": casework_processing_sat,
        "cost_food": c_food, "cost_lodging": c_lodging, "cost_worker": c_worker,
        "casework_efficiency": c_casework,
        "satisfaction": satisfaction, "cost_efficiency": cost_efficiency,
        "score": satisfaction - cost_efficiency,
    }


def compute_score(rm: dict, w: dict = REWARD_WEIGHTS):
    """Backward-compatible triple: (satisfaction, second term, score).

    The second element is `efficiency` under the Unity formula (higher is better) and
    `cost_efficiency` under the legacy one (lower is better). Prefer
    compute_score_components and read the keys you mean."""
    c = compute_score_components(rm, w)
    second = c["efficiency"] if c.get("formula") == "unity" else c["cost_efficiency"]
    return c["satisfaction"], second, c["score"]


# Every key any consumer may read, so gym metrics / loggers never KeyError whichever
# formula produced the dict. Terms that do not apply to a formula are 0.0.
_ALL_KEYS = [
    # unity formula
    "sat_food", "sat_lodging", "sat_worker_use", "sat_waste", "sat_casework",
    "eff_food", "eff_lodging", "eff_worker", "efficiency",
    # legacy formula
    "casework_processing_sat", "cost_food", "cost_lodging", "cost_worker",
    "casework_efficiency", "cost_efficiency",
    # shared
    "satisfaction", "score",
]


def compute_score_components(rm: dict, w: dict = REWARD_WEIGHTS) -> dict:
    """Composite reward — Unity's formula, read from Unity rather than re-implemented.

    Unity exports DailyReportData's own S_*/C_* ratios and the live satisfaction/efficiency
    inside rewardMetrics (RewardMetricsTracker.FillUnityScore). Reading them — instead of
    porting the math — means there is exactly ONE implementation of the score: fix it in
    DailyReportData and the RL reward, the benchmark and the router follow with no Python
    change.

      satisfaction = liveSatisfaction / 1000   (what the human sees, incl. choice impacts)
      efficiency   = liveEfficiency   / 1000
      score        = w_sat * satisfaction + w_eff * efficiency       (higher is better)

    sat_* / eff_* are each component's contribution on the same 0..1 scale (ratio x its
    Unity weight: 0.2 for the five satisfaction terms, 1/3 for the three efficiency terms).

    KNOWN ISSUE, deliberately NOT corrected here: sat_waste is wasted/(consumed+wasted) and
    Unity ADDS it, although the daily report labels it "Food Waste Penalty" — more waste
    raises satisfaction. That is a game-design bug to fix in DailyReportData, not here.

    Falls back to the legacy formula when rewardMetrics predates the export
    (scoreAvailable absent/false), and says which it used under "formula".
    """
    out = {k: 0.0 for k in _ALL_KEYS}
    if not rm:
        out["formula"] = "none"
        return out
    if not rm.get("scoreAvailable"):
        out.update(compute_legacy_score_components(rm, w))
        out["formula"] = "legacy"
        return out

    SAT_W, EFF_W = 0.2, 1.0 / 3.0   # DailyReportData.SAT_W / EFF_W
    out["sat_food"] = rm.get("sFood", 0.0) * SAT_W
    out["sat_lodging"] = rm.get("sLodging", 0.0) * SAT_W
    out["sat_worker_use"] = rm.get("sWorkerUse", 0.0) * SAT_W
    out["sat_waste"] = rm.get("sWaste", 0.0) * SAT_W
    out["sat_casework"] = rm.get("sCasework", 0.0) * SAT_W
    out["eff_food"] = rm.get("cFood", 0.0) * EFF_W
    out["eff_lodging"] = rm.get("cLodging", 0.0) * EFF_W
    out["eff_worker"] = rm.get("cWorker", 0.0) * EFF_W
    out["satisfaction"] = rm.get("liveSatisfaction", 0.0) / 1000.0
    out["efficiency"] = rm.get("liveEfficiency", 0.0) / 1000.0
    out["score"] = w["w_sat"] * out["satisfaction"] + w["w_eff"] * out["efficiency"]
    out["formula"] = "unity"
    return out
