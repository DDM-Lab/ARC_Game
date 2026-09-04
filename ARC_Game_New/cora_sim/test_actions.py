"""The search-side action model: what it offers, what it does, and that search never
returns a plan worse than the policy the replay harness already reproduces exactly."""
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.actions import CoraActions, ADVERTISED          # noqa: E402
from cora_sim.floodmap import FloodMap                         # noqa: E402
from cora_sim.rng import UnityRandom                           # noqa: E402
from cora_sim.search import RHEA                               # noqa: E402
import cora_sim.sim as S                                       # noqa: E402

_STATE = (1789241802, -836569722, -735555501, -958919132)


def _world():
    w = S.World(rng=UnityRandom(state=_STATE), weather="Sunny", fmap=FloodMap.load())
    w.use_generation = True
    return w


def test_basket_is_pruned_and_spanned():
    m = CoraActions(random.Random(0))
    w = _world()
    ids = [a["action_id"] for a in m.basket(w)]
    assert any(i.startswith("build_Kitchen_") for i in ids), ids
    assert len({i.rsplit("_", 1)[1] for i in ids if i.startswith("build_Kitchen_")}) >= 2, "sites collapsed to one"
    assert "hire_untrained_1" in ids and "hire_untrained_5" in ids, "quantity endpoints missing"
    assert len(ids) <= m.max_menu + 6, ids
    # a used site disappears; an unaffordable action disappears
    w.economy.used_sites.add(0)
    assert not any(i.endswith("_0") and i.startswith("build_") for i in
                   [a["action_id"] for a in m.basket(w)])
    w.economy.budget = 50
    ids = [a["action_id"] for a in m.basket(w)]
    assert not any(i.startswith(("build_", "hire_", "train_")) for i in ids), ids
    assert not any(i.startswith("transfer_") for i in ids), "transfers must stay off by default"


def test_apply_answers_every_open_task_and_spends():
    m = CoraActions(random.Random(0))
    w = _world()
    for _ in range(5):
        m.apply(w, ("turn", {"choices": {}, "menu": ()}))
        S.step_round(w)
    open_before = len({t for t, _ in S.open_choices(w)})
    assert open_before > 0
    b0 = w.economy.budget
    m.apply(w, ("turn", {"choices": {}, "menu": ("hire_untrained_2",)}))
    assert not S.open_choices(w), "every open task should have been answered"
    assert w.economy.budget == b0 - 2 * ADVERTISED["untrained"], (b0, w.economy.budget)


def test_search_never_below_baseline_and_is_reproducible():
    m = CoraActions(random.Random(0))
    base = _world()
    plan0 = CoraActions.baseline_plan(12)
    for a in plan0:
        m.apply(base, a)
        S.step_round(base)
    baseline = m.value(base)
    fits = []
    plans = []
    for seed in (1, 1):
        w = _world()
        s = RHEA(S.step_round, CoraActions(random.Random(0)), horizon=12, population=8,
                 generations=3, elites=2, mutation_rate=0.2, rng=random.Random(seed))
        plan, fit = s.plan(w, seed_plan=[plan0[0]] + plan0[1:])
        fits.append(fit); plans.append(plan)
    assert fits[0] >= baseline - 1e-9, (fits[0], baseline)
    assert fits[0] == fits[1] and plans[0] == plans[1], "same seed must give the same plan"


def main():
    print("cora_sim action model")
    fails = 0
    for fn in (test_basket_is_pruned_and_spanned, test_apply_answers_every_open_task_and_spends,
               test_search_never_below_baseline_and_is_reproducible):
        try:
            fn(); print(f"  {fn.__name__}: ok")
        except AssertionError as e:
            fails += 1; print(f"  {fn.__name__}: FAIL -- {e}")
    print("\nRESULT:", "ALL PASS" if not fails else f"{fails} FAILING")
    return 1 if fails else 0


if __name__ == "__main__":
    raise SystemExit(main())
