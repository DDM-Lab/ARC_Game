"""Pruning: does it remove exactly the actions that cannot help, and nothing else?

The risk with pruning is not that it removes too little -- it is that it removes something
the planner could have used, and then nobody notices because the ceiling just quietly drops.
So every rule is tested in both directions: the dead action IS removed, and the live action
next to it SURVIVES.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.economy import Economy, action_from_id                  # noqa: E402
from cora_sim.pruning import prune                                    # noqa: E402


def _staff(index, untrained=4):
    return {"action_type": "worker", "action_id": f"staff_{index}", "cost": 0,
            "worker": {"worker_action_type": "staff", "building_index": index,
                       "untrained": untrained}}


def main():
    print("cora_sim.pruning")
    ok = True

    # 1. A building under construction cannot be staffed; once it finishes, it can.
    #    Index matters: an Economy starts with four PREBUILT facilities, which are always
    #    operational and can never be staffed, so the new shelter lands after them.
    e = Economy(budget=50000)
    e.build("Shelter", 1)
    # The facility is listed from the round it is ORDERED (UnderConstruction), the way Unity's
    # map state carries it, so it is the last entry already -- it just is not staffable yet.
    idx = len(e.buildings) - 1
    kept, _ = prune([_staff(idx)], econ=e, budget=e.budget)
    during = len(kept)
    for _ in range(4):
        e.on_round_end()
    kept_after, _ = prune([_staff(idx)], econ=e, budget=e.budget)
    prebuilt, _ = prune([_staff(0)], econ=e, budget=e.budget)
    good = during == 0 and len(kept_after) == 1 and len(prebuilt) == 0
    print(f"  staffing under construction   : dropped while building ({during==0}), "
          f"kept once NeedWorker ({len(kept_after)==1}), "
          f"prebuilts never staffable ({len(prebuilt)==0})"
          f"{'' if good else '  <-- WRONG'}")
    ok &= good

    # 2. A consumed site is dropped; a fresh one of the same type survives.
    e2 = Economy(budget=50000)
    e2.build("Kitchen", 3)
    menu = [action_from_id("build_Kitchen_3", 1000), action_from_id("build_Kitchen_7", 1000)]
    kept, drops = prune(menu, econ=e2, budget=e2.budget)
    ids = [a["action_id"] for a in kept]
    good = ids == ["build_Kitchen_7"]
    print(f"  consumed site                 : kept {ids}{'' if good else '  <-- WRONG'}")
    ok &= good

    # 3. Affordability counts the WHOLE turn, not each action alone.
    e3 = Economy(budget=3000)          # one build costs 2000 deducted
    menu = [action_from_id("build_Shelter_1", 1000), action_from_id("build_Shelter_2", 1000)]
    first, _ = prune(menu, econ=e3, budget=e3.budget, committed=0)
    second, _ = prune(menu, econ=e3, budget=e3.budget, committed=2000)
    good = len(first) == 2 and len(second) == 0
    print(f"  whole-turn affordability      : {len(first)} affordable alone, "
          f"{len(second)} once 2000 is committed{'' if good else '  <-- WRONG'}")
    ok &= good

    # 4. Interchangeable sites collapse ONLY when a flood_class is supplied.
    menu = [action_from_id(f"build_Shelter_{i}", 1000) for i in range(5)]
    no_class, _ = prune(menu, budget=50000)
    with_class, d = prune(menu, budget=50000, flood_class=lambda s: 0)
    good = len(no_class) == 5 and len(with_class) == 1 and d["dominated"] == 4
    print(f"  interchangeable sites         : {len(no_class)} kept without a flood class, "
          f"{len(with_class)} with one{'' if good else '  <-- WRONG'}")
    ok &= good

    # 5. Expensive-but-effective options are NOT pruned. Measured: the Rapid Response
    #    option is the only food choice that ever fulfils, so a price-based rule would
    #    remove the single thing that works.
    pricey = {"action_type": "resource_transfer", "action_id": "rapid", "cost": 3000,
              "transfer": {"quantity": 100}}
    kept, _ = prune([pricey], budget=50000)
    good = len(kept) == 1
    print(f"  expensive but effective       : {'kept' if good else 'PRUNED  <-- WRONG'}")
    ok &= good

    print("\nRESULT:", "ALL PASS" if ok else "FAILURES PRESENT")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
