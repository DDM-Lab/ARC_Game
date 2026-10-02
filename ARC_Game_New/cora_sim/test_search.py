"""RHEA tests: does the search work, and does it drive the surrogate.

TWO SEPARATE QUESTIONS, KEPT SEPARATE ON PURPOSE.

1. IS THE SEARCH CORRECT? Checked on a toy environment whose optimum is known in closed
   form, so a failure means the evolutionary machinery is broken rather than the game model
   being hard. It also checks the parts that fail quietly: the shift buffer (a rolling plan
   must beat a cold restart on a problem with structure), and that search randomness never
   touches the simulated stream.

2. DOES IT DRIVE THE SURROGATE? Checked by planning against the real ported dynamics and
   reporting throughput. This does NOT check play quality: budget, construction, workforce
   and deliveries are not ported, so no action changes the world and every plan scores
   alike. The number reported is a rate, not a score, and the stub says so.
"""
import json
import os
import random
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.flood import FloodState                      # noqa: E402
from cora_sim.floodmap import FloodMap, pack               # noqa: E402
from cora_sim.rng import UnityRandom                       # noqa: E402
from cora_sim.search import ActionModel, NoOpActions, RHEA  # noqa: E402
from cora_sim.sim import World, step_round                 # noqa: E402

_FIXTURE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "corpus", "flood_rounds.json")


# ── 1. the search, on a problem with a known answer ─────────────────────────────────
class _Toy:
    """State is a position on a line; the reward is hitting a fixed target sequence."""
    __slots__ = ("t", "score", "target")

    def __init__(self, target):
        self.t, self.score, self.target = 0, 0, target

    def clone(self):
        c = _Toy.__new__(_Toy)
        c.t, c.score, c.target = self.t, self.score, self.target
        return c


class _ToyActions(ActionModel):
    def legal(self, world):
        return (0, 1, 2, 3)

    def apply(self, world, action):
        if world.t < len(world.target) and action == world.target[world.t]:
            world.score += 1

    def value(self, world):
        return world.score


def _toy_step(world):
    world.t += 1


def test_finds_known_optimum():
    target = [2, 0, 3, 1, 2, 0, 3, 1]
    model = _ToyActions()
    search = RHEA(_toy_step, model, horizon=len(target), population=24,
                  generations=40, elites=3, mutation_rate=0.25, rng=random.Random(7))
    plan, fitness = search.plan(_Toy(target))
    ok = fitness == len(target)
    print(f"  known optimum           : {int(fitness)}/{len(target)} "
          f"({search.rollouts} rollouts){'' if ok else '  <-- SEARCH FAILED'}")
    return ok


def test_rolling_beats_cold_start():
    """The shift buffer must actually carry information forward.

    Run a real rolling loop: plan, commit the first action, advance one round, replan --
    against an identical loop that plans from scratch each round. With a deliberately tight
    budget (small population, few generations) the warm start should win, because after the
    world advances one step the shifted plan is still aligned with what remains.

    The first version of this test replanned against a FRESH world each decision, so the
    shifted plan was misaligned with a target that had not moved, and the shift looked
    harmful. The bug was in the test, not the buffer -- worth keeping in mind, since a
    rolling search evaluated without rolling the world will always look useless."""
    target = [2, 0, 3, 1, 2, 0, 3, 1, 0, 2, 1, 3]
    model = _ToyActions()
    horizon = 6

    def run(warm):
        total = 0
        for trial in range(24):
            world, seed = _Toy(target), None
            for _ in range(6):                      # six consecutive decisions
                s = RHEA(_toy_step, model, horizon=horizon, population=6, generations=2,
                         elites=2, mutation_rate=0.25, rng=random.Random(200 + trial))
                plan, _ = s.plan(world, seed_plan=seed if warm else None)
                model.apply(world, plan[0])         # commit the first action
                _toy_step(world)                    # and the world moves on
                seed = plan
            total += world.score
        return total / 24

    warm, cold = run(True), run(False)
    ok = warm > cold
    print(f"  rolling vs cold start   : warm {warm:.2f} vs cold {cold:.2f} hits per episode"
          f"{'' if ok else '  <-- SHIFT BUFFER ADDS NOTHING'}")
    return ok


# ── 2. driving the surrogate ────────────────────────────────────────────────────────
def _world_from_fixture(fmap):
    rounds = json.load(open(_FIXTURE))["rounds"]
    rd = next(r for r in rounds if r["weather"] == "Storm" and r["tiles"])
    st = rd["rng"]
    return World(rng=UnityRandom(state=(st["s0"], st["s1"], st["s2"], st["s3"])),
                 weather=rd["weather"], day=rd["day"], segment=rd["segment"],
                 flood=FloodState({pack(x, y) for x, y in rd["tiles"]}, rd["lastWeather"]),
                 fmap=fmap)


def test_search_does_not_disturb_the_stream():
    """Planning must leave the real game untouched.

    RHEA explores by cloning, and its own choices come from a separate random.Random. If
    either leaked, planning would advance the Unity stream and change the future it just
    finished evaluating -- a bug that would look like the surrogate being 'noisy'."""
    fmap = FloodMap.load()
    w = _world_from_fixture(fmap)
    before, tiles_before = w.rng.get_state(), set(w.flood.tiles)
    RHEA(step_round, NoOpActions(), horizon=3, population=6, generations=3,
         rng=random.Random(1)).plan(w)
    ok = w.rng.get_state() == before and w.flood.tiles == tiles_before
    print(f"  planning is side-effect free: {'yes' if ok else 'NO - the search mutated the live world'}")
    return ok


def test_throughput():
    fmap = FloodMap.load()
    w = _world_from_fixture(fmap)
    search = RHEA(step_round, NoOpActions(), horizon=6, population=12, generations=6,
                  elites=2, rng=random.Random(3))
    t = time.perf_counter()
    search.plan(w)
    dt = time.perf_counter() - t
    rounds = search.rollouts * search.horizon
    print(f"  surrogate throughput    : {search.rollouts} rollouts x {search.horizon} rounds "
          f"= {rounds} rounds in {dt*1000:.0f} ms "
          f"({dt/rounds*1e6:.0f} us/round, {rounds/dt:,.0f} rounds/s)")
    print("  NOTE: the CORA action model is a stub (NoOpActions) until the economy is "
          "ported, so this is a RATE, not a measure of play quality.")
    return dt / rounds < 1e-3            # the plan's stated failure threshold


def main():
    print("cora_sim.search (RHEA)")
    results = [test_finds_known_optimum(), test_rolling_beats_cold_start(),
               test_search_does_not_disturb_the_stream(), test_throughput()]
    print("\nRESULT:", "ALL PASS" if all(results) else "FAILURES PRESENT")
    return 0 if all(results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
