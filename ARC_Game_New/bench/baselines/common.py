"""Helpers shared by the baseline policies."""
from __future__ import annotations

from collections import Counter

from cora import params
from cora.actions import as_tool_call
from cora.observation import task_token

def _building_value(gs, building_type, key, param):
    """`key` of an existing `building_type`, else the parameter sheet's value (the state shows a
    building's numbers only once one exists)."""
    for f in (gs.get("mapState") or {}).get("facilities") or []:
        if f.get("buildingType") == building_type and f.get(key):
            return f[key]
    return params.load()[param]


def shelter_beds(gs) -> int:
    return int(_building_value(gs, "Shelter", "populationCapacity", "initialShelterCapacity"))


def workforce_per_building(gs) -> int:
    return int(_building_value(gs, "Shelter", "requiredWorkforce", "initialWorkerUnitsNeededPerLocation"))


def rounds_per_day(gs) -> int:
    return int((gs.get("sessionInfo") or {}).get("roundsPerDay") or params.load()["initialRoundsPerGameDay"])


def motel_rate(gs) -> float:
    """The motel's charge per person per day, as the game exports it."""
    return float(gs["motelCostPerPersonPerDay"])


# $ -> value in the baselines' task-choice heuristic (spend is weighed against acting on demand).
CHOICE_COST_WEIGHT = 0.0002


def impacts_dict(choice):
    return {i.get("type"): i.get("value", 0) for i in (choice.get("impacts") or [])}


# ── shared: move people into shelters we already paid for ────────────────────
# Both rules-based policies BUILD and STAFF shelters and then never fill them: measured
# across 10 episodes each, shelter population was 0/3500 (rules-based) and 0/5000
# (rules-based-v2) while 2,532 and 6,000 people respectively sat in the Motel. The Motel
# bills $200/person/DAY; a staffed shelter is $0/day once built. So the policies were
# paying construction AND the full motel bill, which is why both end deeply negative.
#
# The gap was simply that neither emitted `resource_transfer` actions at all — the
# affordance works (the random baseline used it 1,340 times, and opus 137), it was just
# never in their action set. This helper closes that: drain the Motel first (it is the
# only source that costs money per day), then Communities, into any InUse shelter with
# free beds.
def fill_shelters(env, actions, max_transfers=4):
    """Append transfer-action indices that move people into free shelter capacity.

    Ordering matters: the Motel is drained BEFORE Communities because Motel occupancy is
    the recurring cost. Moving a Community resident into a shelter helps satisfaction but
    saves nothing; moving a Motel resident saves $200/day, every day, for the rest of the
    game.
    """
    gs = env.game_state or {}
    va = env.valid_actions or []
    facs = (gs.get("mapState", {}) or {}).get("facilities", []) or []

    free = {}
    for f in facs:
        if f.get("buildingType") == "Shelter" and f.get("buildingStatus") == "InUse":
            spare = (f.get("populationCapacity") or 0) - (f.get("currentPopulation") or 0)
            if spare > 0:
                free[f.get("facilityName")] = spare
    if not free:
        return

    def _src_rank(name):
        # Motel first (it is the one bleeding money), then anything else.
        return 0 if "motel" in str(name).lower() else 1

    cands = []
    for i, a in enumerate(va):
        if a.get("action_type") != "resource_transfer":
            continue
        tr = a.get("transfer") or {}
        if tr.get("resource_type") == "FoodPacks":
            continue                      # people only; food routing is a separate concern
        dst, src = tr.get("destination_facility"), tr.get("source_facility")
        if dst not in free:
            continue
        cands.append((_src_rank(src), -(tr.get("quantity") or 0), i, dst, tr.get("quantity") or 0))

    cands.sort()                          # motel sources first, largest quantity first
    used = 0
    for _rank, _negq, idx, dst, qty in cands:
        if used >= max_transfers or free.get(dst, 0) <= 0:
            continue
        actions.append(idx)
        free[dst] -= qty
        used += 1


def tool_calls(env, dec) -> list:
    """A baseline's decision (task choices + menu indices) as typed tool calls, so baselines act
    through cora.executor exactly like models. Tasks are named by their stable token, as the
    observation shows them (the raw id if two active tasks share a token). A building listed
    for staffing or teardown more than once becomes one call."""
    tasks = {t["taskId"]: t for t in (env.game_state or {}).get("allActiveTasks") or []}
    shared = {tok for tok, n in Counter(task_token(t) for t in tasks.values()).items() if n > 1}
    calls = []
    for c in dec.get("choices") or []:
        t = tasks.get(c["taskId"])
        tok = task_token(t) if t is not None else None
        calls.append(("task", {"task_id": tok if tok and tok not in shared else str(c["taskId"]),
                               "choice_id": int(c["choiceId"])}))
    once = set()
    for i in dec.get("actions") or []:
        name, args = as_tool_call(env.valid_actions[i])
        if name in ("staff", "deconstruct"):
            if (name, args["site"]) in once:
                continue
            once.add((name, args["site"]))
        calls.append((name, args))
    return calls
