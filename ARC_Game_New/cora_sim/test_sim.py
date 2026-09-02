"""CLOSED-LOOP equivalence: chain whole episodes, don't replay single rounds.

WHY THIS IS THE TEST THAT MATTERS. Every other suite replays one mechanic from its own
captured entry state, so Unity silently re-synchronises the port at every round boundary.
That hides exactly the errors the surrogate exists to avoid: a wrong phase ORDER, a
generation pass on a segment Unity skips, a missed day rollover. Here the port is seeded
ONCE, from the first round of an episode, and runs to the end on its own -- so any
divergence compounds instead of being erased, and the round it first appears in is the
round that caused it.

Three things are compared at every round, in increasing strictness:

  weather      the day's weather, which the port must have drawn for itself
  flood tiles  the exact set, not the count
  RNG state    the stream position, which is what every later mechanic will depend on

The RNG check is the strict one: it fails on a phase that draws a single random too many
or too few, even when the visible state still happens to agree.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.flood import FloodState                    # noqa: E402
from cora_sim.floodmap import FloodMap, pack             # noqa: E402
from cora_sim.rng import UnityRandom                     # noqa: E402
from cora_sim.sim import World, step_round               # noqa: E402
from cora_sim.triggers import INVENTORY                  # noqa: E402

_FIXTURE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "corpus", "flood_rounds.json")


def episodes(rounds):
    """Group fixture rounds by capture, preserving order."""
    out, cur = [], []
    for rd in rounds:
        if cur and rd["source"] != cur[-1]["source"]:
            out.append(cur)
            cur = []
        cur.append(rd)
    if cur:
        out.append(cur)
    return out


def facilities_from(triggers, source):
    """The facility order Unity used in this capture, keyed by task title.

    Taken from Unity's own "Found N suitable facilities" line rather than reconstructed:
    FindObjectsOfType order is not sorted, not creation order, and not contractual."""
    order = {}
    for row in triggers:
        if row["source"] == source:
            order.update(row.get("facilityOrder") or {})
    by_title = {t["taskTitle"]: t for t in INVENTORY if t.get("taskTitle")}

    def facilities_for(task):
        return order.get(task.get("taskTitle"), []) if task.get("taskTitle") in by_title else []
    return facilities_for


def main():
    fx = json.load(open(_FIXTURE))
    fmap = FloodMap.load()
    failures, chained, total_rounds = [], 0, 0

    print("cora_sim closed-loop equivalence vs Unity")
    for eps in episodes(fx["rounds"]):
        head = eps[0]
        st = head["rng"]
        w = World(rng=UnityRandom(state=(st["s0"], st["s1"], st["s2"], st["s3"])),
                  weather=head["weather"], day=head["day"], segment=head["segment"],
                  flood=FloodState({pack(x, y) for x, y in head["tiles"]},
                                   head["lastWeather"]),
                  fmap=fmap,
                  facilities_for=facilities_from(fx.get("triggers", []), head["source"]))

        # Round 0 of the episode is the seed; its flood still has to run.
        from cora_sim.flood import update_flood
        from cora_sim.weather import RAIN_INTENSITY
        update_flood(w.flood, fmap, w.rng, w.weather, RAIN_INTENSITY[w.weather])

        survived = 0
        for rd in eps[1:]:
            total_rounds += 1
            where = f"{rd['source']} d{rd['day']}r{rd['segment']}"
            problem = []

            def compare(world, _rd=rd, _where=where, _out=problem):
                """Runs at Unity's flood:enter instant -- the one moment the capture and
                the port describe the same state."""
                want_state = tuple(_rd["rng"][k] for k in ("s0", "s1", "s2", "s3"))
                want_tiles = {pack(x, y) for x, y in _rd["tiles"]}
                if world.weather != _rd["weather"]:
                    _out.append(f"{_where}: weather unity={_rd['weather']} "
                                f"port={world.weather}")
                elif world.flood.tiles != want_tiles:
                    _out.append(f"{_where}: flood set differs (port "
                                f"{len(world.flood.tiles)} tiles, unity {len(want_tiles)})")
                elif world.rng.get_state() != want_state:
                    _out.append(f"{_where}: RNG stream position differs -- the port drew a "
                                f"different number of randoms this round")

            step_round(w, on_flood_enter=compare)
            if problem:
                failures.append(problem[0])
                break
            survived += 1
        chained += survived
        mark = "OK" if survived == len(eps) - 1 else f"diverged after {survived}"
        print(f"  {head['source']:<16} {survived}/{len(eps)-1} rounds chained  {mark}")

    if failures:
        print("\nFAIL:")
        for f in failures:
            print("  " + f)
    else:
        print(f"\nPASS: {chained}/{total_rounds} rounds chained from a single seed -- "
              f"weather, flood tile sets and RNG stream position identical to Unity "
              f"throughout, with no re-synchronisation")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
