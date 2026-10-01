"""The score: Unity's satisfaction and efficiency, read from the game rather than re-implemented.

Unity exports DailyReportData's own component ratios and the live satisfaction/efficiency in
game_state["rewardMetrics"] (RewardMetricsTracker.FillUnityScore). Reading them means there is
one implementation of the score: fix it in DailyReportData and the RL reward, the benchmark and
the router follow.

    satisfaction = liveSatisfaction / 1000     (what the daily report shows a player, including
                                                task-choice impacts, so it is not the sum of sat_*)
    efficiency   = liveEfficiency   / 1000
    score        = w_sat * satisfaction + w_eff * efficiency

The RL reward is the per-round change in score, so it sums to the final score. Unity has no
single combined score; equal weights are the one choice made here.
"""
from __future__ import annotations

REWARD_WEIGHTS = {"w_sat": 1.0, "w_eff": 1.0}

# DailyReportData.SAT_W / EFF_W: each satisfaction term is 0.2 of satisfaction, each efficiency
# term 1/3 of efficiency.
_SAT_W, _EFF_W = 0.2, 1.0 / 3.0
_SAT_TERMS = {"sat_food": "sFood", "sat_lodging": "sLodging", "sat_worker_use": "sWorkerUse",
              "sat_waste": "sWaste", "sat_casework": "sCasework"}
_EFF_TERMS = {"eff_food": "cFood", "eff_lodging": "cLodging", "eff_worker": "cWorker"}
COMPONENTS = (*_SAT_TERMS, *_EFF_TERMS, "satisfaction", "efficiency", "score")


def score_components(reward_metrics: dict, w: dict = REWARD_WEIGHTS) -> dict:
    """Every score term on a 0..1 scale (each sat_*/eff_* is its contribution: ratio x Unity's
    weight). sat_waste is Unity's S_Waste = 1 - wasted/(consumed+wasted), so higher is less waste.

    All terms are 0.0 before the game has reported a score (no rewardMetrics yet, or a build
    that predates the export); `scored` says whether the numbers came from the game."""
    rm = reward_metrics or {}
    out = {k: 0.0 for k in COMPONENTS}
    out["scored"] = bool(rm.get("scoreAvailable"))
    if not out["scored"]:
        return out
    for k, src in _SAT_TERMS.items():
        out[k] = rm.get(src, 0.0) * _SAT_W
    for k, src in _EFF_TERMS.items():
        out[k] = rm.get(src, 0.0) * _EFF_W
    out["satisfaction"] = rm.get("liveSatisfaction", 0.0) / 1000.0
    out["efficiency"] = rm.get("liveEfficiency", 0.0) / 1000.0
    out["score"] = w["w_sat"] * out["satisfaction"] + w["w_eff"] * out["efficiency"]
    return out
