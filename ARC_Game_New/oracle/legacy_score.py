"""The pre-export Python score, kept only for the surrogate (its metrics carry no Unity score).

Parked here from the deleted reward_scoring.py (pre-cleanup tag) until the surrogate's future is
decided; nothing outside oracle/ may import it — the platform's score is cora.scoring.
"""

REWARD_WEIGHTS = {
    # (the platform's score is cora.scoring; these weights only drive the formula below)
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


