"""Wiring tests for the CORA player, on a stub env -- no Unity needed.

These guard the three things that were actually wrong at some point while building it, and
that a Unity-dependent test would be too slow to catch early:

  1. Task choices must be in the action space. A policy that only builds and hires cannot
     move food, lodging or casework, and the first scripted captures proved it by running
     twenty rounds to all-zero fulfilment counters.
  2. The objective must be the game's own score. compute_score() returns a 3-tuple, so
     using it directly gives a tuple where a number is needed -- which is how the first run
     failed. score_of() is the single place that indexing happens.
  3. Menu actions are capped, choices are not. The cap keeps the branching factor off ~69
     near-identical builds; capping choices would silently remove the only actions that
     score.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.play import UnityActions, score_of      # noqa: E402


class _StubEnv:
    """Minimal stand-in: enough game_state for the action space and the objective."""

    def __init__(self, n_menu=40, n_tasks=3, budget=5000):
        self._menu = [{"action_id": f"a{i}", "cost": 100} for i in range(n_menu)]
        self._tasks = [{"taskId": t, "choices": [{"choiceId": 0}, {"choiceId": 1}]}
                       for t in range(n_tasks)]
        self._budget = budget
        self.executed, self.chosen = [], []

    def _game_state_dict(self):
        return {"allActiveTasks": self._tasks,
                "satisfactionAndBudget": {"budget": self._budget},
                "workforceState": {"workingTrainedWorkers": 2, "workingUntrainedWorkers": 1},
                "mapState": {"facilities": [
                    {"buildingType": "Motel", "resources": {"population": 50, "foodPacks": 0}}]},
                "rewardMetrics": {"foodResolved": 10, "foodFulfilled": 5,
                                  "lodgingResolved": 0, "lodgingFulfilled": 0,
                                  "daysCompleted": 2, "totalWorkers": 10,
                                  "cumWorkingWorkers": 8, "workerSpend": 400}}

    def get_valid_actions(self):
        return self._menu

    def execute(self, payload):
        self.executed.append(payload)

    def choose(self, t, c):
        self.chosen.append((t, c))


class _StubWorld:
    def __init__(self, env):
        self.env = env

    def metrics(self):
        return self.env._game_state_dict()["rewardMetrics"]


def main():
    env = _StubEnv()
    world = _StubWorld(env)
    model = UnityActions(env, max_menu=8)
    actions = model.legal(world)
    kinds = [a[0] for a in actions]
    ok = True

    n_choice, n_menu = kinds.count("choice"), kinds.count("menu")
    if n_choice != 6:                       # 3 tasks x 2 choices, never capped
        print(f"  task choices in action space : {n_choice}/6  <-- MISSING")
        ok = False
    else:
        print(f"  task choices in action space : {n_choice} (uncapped, as required)")

    if n_menu != 8:
        print(f"  menu cap                     : {n_menu} (expected 8)  <-- CAP NOT APPLIED")
        ok = False
    else:
        print(f"  menu cap                     : {n_menu} of 40 offered")

    if kinds.count("noop") != 1:
        print("  explicit no-op               : MISSING - the planner cannot choose to wait")
        ok = False
    else:
        print("  explicit no-op               : present")

    # Affordability: nothing above budget should be offered.
    poor = _StubEnv(budget=50)
    if any(a[0] == "menu" for a in UnityActions(poor, max_menu=8).legal(_StubWorld(poor))):
        print("  affordability filter         : unaffordable actions offered  <-- WRONG")
        ok = False
    else:
        print("  affordability filter         : unaffordable actions excluded")

    # The objective is a number, and it is the game's own score.
    v = model.value(world)
    if not isinstance(v, float):
        print(f"  objective is a scalar        : got {type(v).__name__}  <-- WRONG")
        ok = False
    else:
        expected = score_of(world.metrics())
        same = abs(v - expected) < 1e-12
        print(f"  objective is the game's score: {v:+.4f} "
              f"({'matches reward_scoring' if same else 'DIVERGES from reward_scoring'})")
        ok &= same

    # Shaping must be off by default, and additive when on.
    shaped = UnityActions(env, max_menu=8, shaping=1.0).value(world)
    if shaped <= v:
        print("  shaping is opt-in and additive: shaped value did not exceed raw  <-- CHECK")
        ok = False
    else:
        print(f"  shaping is opt-in and additive: raw {v:+.4f} -> shaped {shaped:+.4f}")

    # Rejected actions must not abort a rollout.
    class _Boom(_StubEnv):
        def execute(self, payload):
            raise RuntimeError("Unity rejected this action")
    boom = _Boom()
    try:
        UnityActions(boom).apply(_StubWorld(boom), ("menu", 0))
        print("  rejected action              : treated as a no-op, rollout survives")
    except Exception as e:
        print(f"  rejected action              : propagated ({e})  <-- KILLS THE ROLLOUT")
        ok = False

    print("\nRESULT:", "ALL PASS" if ok else "FAILURES PRESENT")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
