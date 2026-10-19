"""The surrogate behind the RL / benchmark environment (CoraEnvConfig.backend="sim"), and its
export of the choices the player's panel greys out (oracle.sim.export._feasibility)."""
import pytest

from bench.baselines.rules import rules_calls
from cora import executor
from cora.env.game import DECISIONS
from cora.scoring import score_components
from oracle.sim.env import SimEnv
from rl import CoraEnv, CoraEnvConfig


def _sim_rules_score(seed):
    env = SimEnv(seed=seed, max_episode_steps=DECISIONS + 4)
    env.reset()
    for d in range(DECISIONS):
        _, (_, _, term, trunc, _) = executor.execute_turn(env, rules_calls(env, d, DECISIONS))
        if term or trunc:
            break
    return score_components(env.game_state.get("rewardMetrics"))["score"]


def test_sim_backend_plays_the_surrogate():
    cenv = CoraEnv(CoraEnvConfig(seed=7000, backend="sim"))
    user, info = cenv.reset()
    assert info["backend"] == "sim" and isinstance(user, str) and user
    total = 0.0
    for d in range(DECISIONS):
        user, reward, term, trunc, info = cenv.step(rules_calls(cenv.game, d, DECISIONS))
        total += reward
        assert info["env_s"] >= 0
        if term or trunc:
            break
    assert term
    # The benchmark's rules arm on Unity scored 1.5619 on seed 7000 (bench_v7/rules_episodes.jsonl).
    assert total == pytest.approx(_sim_rules_score(7000), abs=1e-6)
    assert total == pytest.approx(1.5619, abs=1e-4)
    cenv.close()


@pytest.mark.parametrize("kw", [{"map_config": "m.json"}, {"param_config": "p.csv"}, {"image_mode": "synthetic"}])
def test_sim_backend_refuses_what_it_cannot_model(kw):
    with pytest.raises(ValueError):
        CoraEnv(CoraEnvConfig(seed=7000, backend="sim", **kw)).reset()


def test_unknown_backend_is_an_error():
    with pytest.raises(ValueError):
        CoraEnv(CoraEnvConfig(seed=7000, backend="nope")).reset()


def test_kitchen_food_greyed_out_with_no_kitchen():
    """A kitchen food delivery with no kitchen on the map is unavailable, with Unity's reason
    (TaskDetailUI.IsChoiceFeasibleFor -> FoodDeliveryHandler.CanExecute); an airlift is not."""
    env = SimEnv(seed=7000, max_episode_steps=DECISIONS + 4)
    env.reset()
    seen = []
    for _ in range(4):
        for t in env.game_state.get("allActiveTasks") or []:
            for c in t.get("choices") or []:
                if c.get("triggersDelivery") and not c.get("immediateDelivery") and "Food" in (t.get("taskTitle") or ""):
                    seen.append(c)
                if c.get("immediateDelivery"):
                    assert c["feasible"], c
        executor.execute_turn(env, [])
    assert seen and all(not c["feasible"] for c in seen)
    assert all(c["unavailableReason"].startswith("No kitchen is reachable") for c in seen)
