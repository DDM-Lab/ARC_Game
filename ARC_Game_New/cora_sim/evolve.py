"""Evolve many full-episode plans on the surrogate and log EVERY evaluated trajectory.

    python -m cora_sim.evolve --out runs/evo.jsonl --seeds 8 --population 32 --generations 30

Each line of the JSONL is one evaluated trajectory: the seed (a Unity xorshift state, so any
line can be replayed on the headless server), the genome, the game's own score components
at the end of 32 rounds, and a feature vector for clustering. Nothing is filtered: the
Pareto frontier and the strategy clusters are computed afterwards by pareto.py from the
whole population, not from the winners.

Seeds are the xorshift states Unity recorded at the start of the captured episodes (Unity
seeds it internally; the state is observed, not set), so every trajectory here can be
replayed on the headless server with the same Unity seed.
"""
from __future__ import annotations

import argparse
import json
import os
import random
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim.actions import CoraActions          # noqa: E402
from cora_sim.floodmap import FloodMap            # noqa: E402
from cora_sim.rng import UnityRandom              # noqa: E402
from cora_sim.search import RHEA                  # noqa: E402
import cora_sim.sim as S                          # noqa: E402

_CAPTURES = os.environ.get("STAFF_TRACES") or (
    "/private/tmp/claude-501/-Users-cpulling-Work-CORA/b762a1aa-9f0c-4053-9897-bfd6aeeb9623/"
    "scratchpad/cap32{b,_fresh}/staff_*.json")


def captured_seeds():
    """(unity_seed, xorshift state) for every captured episode: the only states the headless
    server can actually be started on, so every trajectory here is replayable for real."""
    import glob
    from cora_sim.test_replay_forward import seed_state
    out = []
    for pat in _CAPTURES.replace("{b,_fresh}", "\0").split("\0") if "{" not in _CAPTURES else \
            [_CAPTURES.replace("{b,_fresh}", x) for x in ("b", "_fresh")]:
        for p in sorted(glob.glob(pat)):
            st = seed_state(p.replace(".json", ".log"))
            if st:
                out.append((int(os.path.basename(p)[6:-5]), st))
    return out


def fresh_world(state, fmap):
    w = S.World(rng=UnityRandom(state=state), weather="Sunny", fmap=fmap)
    w.use_generation = True
    return w


def features(executed, world, comps):
    """What a strategy IS, for clustering: when and how much it builds, hires, trains, and
    what the run ended with. Counted from the actions the game actually EXECUTED, not the
    genome -- a gene the state made illegal is not a strategy. Flat and numeric."""
    f = {"builds_kitchen": 0, "builds_shelter": 0, "builds_casework": 0,
         "hire_untrained": 0, "hire_trained": 0, "train": 0,
         "first_build_round": 33, "first_hire_round": 33, "menu_turns": 0}
    for r, acts in enumerate(executed):
        if acts:
            f["menu_turns"] += 1
        for aid in acts:
            if aid.startswith("build_"):
                f["first_build_round"] = min(f["first_build_round"], r)
                kind = aid.split("_")[1].lower()
                f["builds_" + ("casework" if kind.startswith("casework") else kind)] += 1
            elif aid.startswith("hire_untrained"):
                f["hire_untrained"] += int(aid.rsplit("_", 1)[1]); f["first_hire_round"] = min(f["first_hire_round"], r)
            elif aid.startswith("hire_trained"):
                f["hire_trained"] += int(aid.rsplit("_", 1)[1]); f["first_hire_round"] = min(f["first_hire_round"], r)
            elif aid.startswith("train_"):
                f["train"] += int(aid.rsplit("_", 1)[1])
    m = world.economy.metrics()
    for k in ("foodSpend", "lodgingSpend", "workerSpend", "caseworkSpend", "foodResolved",
              "foodFulfilled", "lodgingResolved", "lodgingFulfilled", "caseworkRequested",
              "caseworkProcessed", "cumWorkingWorkers", "totalWorkers"):
        f[k] = m.get(k, 0)
    f["final_budget"] = world.economy.budget
    f["motel_population"] = world.economy.motel_population
    for k in ("sat_food", "sat_lodging", "sat_worker_use", "casework_processing_sat",
              "cost_food", "cost_lodging", "cost_worker", "casework_efficiency",
              "satisfaction", "cost_efficiency", "score"):
        f[k] = comps.get(k, 0.0)
    return f


class LoggingActions(CoraActions):
    """Records every terminal rollout so the whole population is kept, not just winners."""

    def __init__(self, *a, sink=None, seed_state=None, unity_seed=None, **k):
        super().__init__(*a, **k)
        self.sink, self.seed_state, self.unity_seed = sink, seed_state, unity_seed
        self._plan, self._executed = None, []

    def apply(self, world, action):
        self._executed.append(super().apply(world, action) or [])

    def value(self, world):
        v = super().value(world)
        if self.sink is not None and self._plan is not None:
            comps = self.components(world)
            self.sink.write(json.dumps({"unity_seed": self.unity_seed, "seed_state": list(self.seed_state), "score": v,
                                        "plan": [g for _, g in self._plan],
                                        "executed": self._executed,
                                        "features": features(self._executed, world, comps)}) + "\n")
        return v


class LoggingRHEA(RHEA):
    def _rollout(self, world, plan):
        self.model._plan, self.model._executed = plan, []
        return super()._rollout(world, plan)




def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--seeds", type=int, default=0, help="use only the first N captured seeds (0 = all)")
    ap.add_argument("--menu", type=int, default=24, help="basket size offered to the genome (site coverage)")
    ap.add_argument("--population", type=int, default=32)
    ap.add_argument("--generations", type=int, default=30)
    ap.add_argument("--elites", type=int, default=6)
    ap.add_argument("--mutation", type=float, default=0.12)
    ap.add_argument("--crossover", type=float, default=0.5)
    ap.add_argument("--rounds", type=int, default=32)
    ap.add_argument("--search-seed", type=int, default=0)
    args = ap.parse_args()
    fmap = FloodMap.load()
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    t0 = time.time(); total = 0
    with open(args.out, "a") as sink:
        seeds = captured_seeds()[:args.seeds or None]
        for i, (unity_seed, state) in enumerate(seeds):
            model = LoggingActions(random.Random(args.search_seed + i), sink=sink, seed_state=state,
                                   unity_seed=unity_seed, max_menu=args.menu)
            search = LoggingRHEA(S.step_round, model, horizon=args.rounds, population=args.population,
                                 generations=args.generations, elites=args.elites,
                                 mutation_rate=args.mutation, crossover=args.crossover,
                                 rng=random.Random(args.search_seed * 1000 + i))
            plan, fit = search.plan(fresh_world(state, fmap), seed_plan=CoraActions.baseline_plan(args.rounds))
            total += search.rollouts
            print(f"  seed {unity_seed}: best {fit:.4f} after {search.rollouts} rollouts "
                  f"({total / (time.time() - t0):.0f}/s cumulative)", flush=True)
    print(f"wrote {total} trajectories to {args.out} in {time.time() - t0:.0f}s")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
