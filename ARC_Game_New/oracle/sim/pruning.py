"""Pruning the basket: dropping actions that cannot possibly help.

WHY THIS IS WORTH DOING, AND WHERE THE LINE IS. A turn is a subset of a ~69-element
basket, so the search space is 2^69 and every element removed halves it. But pruning is
also the easiest way to accidentally cap the ceiling: an action removed for looking silly
is an action the planner can never discover a use for. So this module only removes actions
that are PROVABLY inert or PROVABLY dominated, and every rule below says which of the two
it is and why.

DEAD (the game will do nothing with it):
  * staffing a building that is still UnderConstruction -- UpdateWorkerStatus only runs on
    NeedWorker/InUse, so the request is silently discarded. This is the one the surrogate
    itself got wrong until the building-status lifecycle was ported.
  * building on a site already built on -- the menu keeps offering it, Unity accepts and
    does nothing, and it does not even charge.
  * a transfer of zero people, or into a destination already at capacity.
  * anything the remaining budget cannot cover, counting what the basket has already
    committed this turn.

DOMINATED (legal, but another action in the basket is at least as good in every respect):
  * the same building type on a different interchangeable site. Sites differ only in
    position, and position matters ONLY through flood exposure -- so this collapses to one
    candidate per (type, flood-exposure class) rather than one per type.
  * hiring or training a quantity that exceeds what could be used, when a smaller quantity
    of the same action costs strictly less.

DELIBERATELY NOT PRUNED, though it is tempting:
  * "expensive" options like the Rapid Response Vehicle. Measured, these are the ONLY food
    choices that ever fulfil -- kitchen orders never landed in any capture, because no
    kitchen was staffed. Pruning on price would have removed the single effective option.
  * building late in the episode. It looks wasteful, and the hand-written baseline refuses
    it below 4 rounds remaining, but that is a POLICY judgement rather than a fact about
    the game, and belongs in an evaluator, not in a legality filter.
"""
from __future__ import annotations

from .economy import advertised_cost_error, basket_order


def _true_cost(action):
    return (action.get("cost") or 0) + advertised_cost_error(action)


def prune(actions, econ=None, budget=None, committed=0, flood_class=None):
    """Return the basket with dead and dominated actions removed.

    `flood_class(site_id) -> hashable` groups interchangeable construction sites; pass None
    to keep every site (the safe default, since collapsing sites is the one rule here that
    could in principle discard a genuinely better position)."""
    out, seen_builds, dropped = [], {}, {"dead": 0, "dominated": 0}
    remaining = None if budget is None else budget - committed

    for a in actions:
        kind = a.get("action_type")

        if remaining is not None and _true_cost(a) > remaining:
            dropped["dead"] += 1
            continue

        if kind == "construction":
            c = a.get("construction") or {}
            site = c.get("site_id")
            if econ is not None and site is not None and site in econ.used_sites:
                dropped["dead"] += 1          # site already built on: a silent no-op
                continue
            key = (c.get("building_type"),
                   flood_class(site) if flood_class and site is not None else site)
            if flood_class is not None:
                if key in seen_builds:
                    dropped["dominated"] += 1
                    continue
                seen_builds[key] = True

        elif kind == "worker":
            w = a.get("worker") or {}
            wat = (w.get("worker_action_type") or "")
            if wat.startswith(("staff", "assign")) and econ is not None:
                if not econ.can_staff(int(w.get("building_index", -1))):
                    dropped["dead"] += 1      # still UnderConstruction
                    continue
            if wat.startswith("train") and econ is not None and econ.free_untrained <= 0:
                dropped["dead"] += 1          # nothing to train
                continue

        elif kind == "resource_transfer":
            t = a.get("transfer") or {}
            if (t.get("quantity") or 0) <= 0:
                dropped["dead"] += 1
                continue

        out.append(a)

    out.sort(key=basket_order)
    return out, dropped
