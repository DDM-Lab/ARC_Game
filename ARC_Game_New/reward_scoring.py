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


def compute_score_components(rm: dict, w: dict = REWARD_WEIGHTS) -> dict:
    """Full breakdown of the composite reward from Unity's rewardMetrics.

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
    """Backward-compatible: (satisfaction, cost_efficiency, score)."""
    c = compute_score_components(rm, w)
    return c["satisfaction"], c["cost_efficiency"], c["score"]
