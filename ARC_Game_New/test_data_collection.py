"""Data collection for the testing build: the score humans see reaches the RL/benchmark
path, and the router session log records per-round state, the final state, and which
scenario was played. Run: python test_data_collection.py"""
import json, asyncio
from agent_router import Session
from reward_scoring import compute_score_components

# Kent's final state (Kent_2e0148c5 human CSV, day 8): satisfaction 693.2, efficiency 948.2.
UNITY_RM = {"scoreAvailable": True, "liveSatisfaction": 693.2, "liveEfficiency": 948.2,
            "sFood": 0.49, "sLodging": 1.0, "sWorkerUse": 0.5, "sWaste": 0.38, "sCasework": 0.9,
            "cFood": 1.0, "cLodging": 0.83, "cWorker": 1.0,
            "foodResolved": 11, "foodFulfilled": 10}
LEGACY_RM = {"foodResolved": 11, "foodFulfilled": 10, "lodgingResolved": 4, "lodgingFulfilled": 4}


def test_scoring():
    c = compute_score_components(UNITY_RM)
    assert c["formula"] == "unity", c["formula"]
    assert abs(c["satisfaction"] - 0.6932) < 1e-9 and abs(c["efficiency"] - 0.9482) < 1e-9
    assert abs(c["score"] - 1.6414) < 1e-9, c["score"]
    assert abs(c["sat_waste"] - 0.38 * 0.2) < 1e-9          # exported as-is, sign NOT corrected
    assert abs(c["eff_lodging"] - 0.83 / 3) < 1e-9
    l = compute_score_components(LEGACY_RM)
    assert l["formula"] == "legacy" and l["efficiency"] == 0.0 and l["sat_food"] > 0
    assert compute_score_components({})["formula"] == "none"
    # every consumer-visible key exists under every formula
    for d in (c, l, compute_score_components({})):
        for k in ("score", "satisfaction", "efficiency", "cost_efficiency", "sat_waste", "cost_food"):
            assert k in d, k
    print("  scoring: unity / legacy / empty ✓")


def test_router_events():
    events = []

    class Cap:
        def log_event(self, rec): events.append(rec)

    s = Session.__new__(Session)
    s.logger = Cap(); s.session_id = "sid"; s.episode_id = "eid"
    s.round_num, s.day, s.segment = 3, 2, 1
    gs = {"satisfactionAndBudget": {"satisfaction": 693, "budget": 164400, "efficiency": 948.2},
          "allActiveTasks": [{"taskId": 1}, {"taskId": 2}], "rewardMetrics": UNITY_RM}
    s._emit_round_state(gs, phase="round_start")
    asyncio.get_event_loop().run_until_complete(
        s._handle_message(json.dumps({"type": "game_end", "game_state": gs})))
    asyncio.get_event_loop().run_until_complete(s._handle_message(json.dumps({
        "type": "provenance", "seed": 7, "seed_source": "cli", "rng_state": "{}",
        "build_guid": "abc", "game_version": "1.0", "platform": "WebGLPlayer",
        "param_source": "StreamingAssets", "parameters": json.dumps({"workersPerLocation": 4}),
        "map_hash": "h", "map_status": "loaded"})))

    rs = [e for e in events if e["event_type"] == "round_state"]
    assert [e["phase"] for e in rs] == ["round_start", "game_end"], [e["phase"] for e in rs]
    r = rs[0]
    assert r["budget"] == 164400 and r["efficiency"] == 948.2 and r["active_tasks"] == 2
    assert abs(r["score"] - 1.6414) < 1e-9 and r["score_formula"] == "unity"
    assert r["reward_metrics"]["sWaste"] == 0.38
    assert r["session_id"] == "sid" and r["day"] == 2 and r["round"] == 3
    pv = [e for e in events if e["event_type"] == "provenance"]
    assert len(pv) == 1 and pv[0]["seed"] == 7 and pv[0]["parameters"] == {"workersPerLocation": 4}
    assert pv[0]["build_guid"] == "abc"
    json.dumps(events)   # everything serialisable
    print("  router: round_state(round_start, game_end) + provenance ✓")


if __name__ == "__main__":
    test_scoring()
    test_router_events()
    print("\nALL DATA-COLLECTION TESTS PASSED ✓")
