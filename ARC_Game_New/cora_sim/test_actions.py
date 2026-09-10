"""The search-side action model: what it offers, what it does, that search never returns a
plan worse than the policy the replay harness already reproduces exactly, and that clone()
-- which only search uses -- is a faithful, independent copy."""
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.actions import CoraActions, ADVERTISED          # noqa: E402
from cora_sim.economy import C as ECON_C                     # noqa: E402
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
    assert not any(i.startswith("transfer_") for i in ids), "transfers must stay off by default"
    w.economy.used_sites.add(0)
    assert not any(i.startswith("build_") and i.endswith("_0") for i in
                   [a["action_id"] for a in m.basket(w)]), "built-on site still offered"
    # The game allows debt (allowNegativeBudget is true in the scene), so a poor budget
    # prunes nothing by default; the no_debt model keeps the old search preference.
    w.economy.budget = 50
    ids = [a["action_id"] for a in m.basket(w)]
    assert any(i.startswith("hire_") for i in ids), "debt-allowed model must still offer hires"
    ids = [a["action_id"] for a in CoraActions(random.Random(0), no_debt=True).basket(w)]
    assert not any(i.startswith(("build_", "hire_", "train_")) for i in ids), ids


def test_apply_answers_every_open_task_and_spends():
    m = CoraActions(random.Random(0))
    w = _world()
    for _ in range(5):
        m.apply(w, ("turn", {"choices": {}, "menu": ()}))
        S.step_round(w)
    assert S.open_choices(w), "expected open tasks by round 5"
    b0 = w.economy.budget
    m.apply(w, ("turn", {"choices": {}, "menu": ("hire_untrained_2",)}))
    # Everything ANSWERABLE is answered. A food request in a world with no kitchen is not:
    # FoodDeliveryHandler refuses ("No meals available across any kitchen"), the choice
    # returns false and the task stays on the board until it expires -- so this scenario,
    # which builds nothing, always ends with its community food requests still open. Before
    # the food overhaul those requests came from a probability trigger and were rare enough
    # that the old blanket assertion held by luck; the depletion manager now raises one per
    # community per day.
    left = {tid for tid, _c in S.open_choices(w)}
    unanswerable = {tid for tid in left
                    if (w.generated_specs.get(tid) or ("", None, {}))[0] == "Community_FoodRequest"}
    assert left == unanswerable, sorted(left - unanswerable)
    assert not [b for b in w.economy.buildings if b["type"] == "Kitchen"], "scenario builds no kitchen"
    # The game prices a hire itself (quantity x the configured rate, BUG_REPORTS B21); the
    # menu's advertised number is not what moves the budget.
    assert w.economy.budget == b0 - 2 * ECON_C["untrained_cost"], (b0, w.economy.budget)


def test_search_never_below_baseline_and_is_reproducible():
    m = CoraActions(random.Random(0))
    base = _world()
    plan0 = CoraActions.baseline_plan(12)
    for a in plan0:
        m.apply(base, a)
        S.step_round(base)
    baseline = m.value(base)
    results = []
    for _ in range(2):
        s = RHEA(S.step_round, CoraActions(random.Random(0)), horizon=12, population=8,
                 generations=3, elites=2, mutation_rate=0.2, rng=random.Random(1), crossover=0.5)
        results.append(s.plan(_world(), seed_plan=plan0))
    (plan_a, fit_a), (plan_b, fit_b) = results
    assert fit_a >= baseline - 1e-9, (fit_a, baseline)
    assert fit_a == fit_b and plan_a == plan_b, "same seed must give the same plan"


def test_clone_is_a_faithful_independent_copy():
    """A search rollout runs on clone(); it must see the same future as a fresh world and
    must not touch the original. Both failed silently until search used clone()."""
    m = CoraActions(random.Random(0))
    plan = [("turn", {"choices": {}, "menu": ("build_Kitchen_0", "hire_untrained_3")})] + \
        CoraActions.baseline_plan(19)
    a, b = _world(), _world()
    c = b.clone()
    for g in plan:
        m.apply(a, g); S.step_round(a)
        m.apply(c, g); S.step_round(c)
    assert a.economy.metrics() == c.economy.metrics(), (a.economy.metrics(), c.economy.metrics())
    assert a.economy.budget == c.economy.budget
    assert b.economy.metrics()["roundsCompleted"] == 0, "stepping the clone touched the original"
    assert b.economy.budget == _world().economy.budget


def main():
    print("cora_sim action model")
    fails = 0
    for fn in (test_basket_is_pruned_and_spanned, test_apply_answers_every_open_task_and_spends,
               test_search_never_below_baseline_and_is_reproducible,
               test_clone_is_a_faithful_independent_copy):
        try:
            fn(); print(f"  {fn.__name__}: ok")
        except AssertionError as e:
            fails += 1; print(f"  {fn.__name__}: FAIL -- {str(e)[:160]}")
    print("\nRESULT:", "ALL PASS" if not fails else f"{fails} FAILING")
    return 1 if fails else 0


if __name__ == "__main__":
    raise SystemExit(main())
