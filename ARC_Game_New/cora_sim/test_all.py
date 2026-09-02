"""Run every equivalence suite. Exit non-zero if any of them regresses.

    ./.venv/bin/python cora_sim/test_all.py

Everything here compares the port against captured Unity ground truth, never against the
port's own previous output. Rebuild the fixtures with cora_sim/build_corpus.py after any
change to FloodSystem.cs, WeatherSystem.cs, TaskDatabases.cs or the scene's serialized
parameters -- a stale fixture turns a real divergence into a passing test.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from cora_sim import (test_economy, test_flood, test_play, test_rng,     # noqa: E402
                      test_search, test_sim, test_triggers, test_weather)

# Order matters for reading the output: per-mechanic suites first, then the closed-loop
# chain that depends on all of them, then the search that runs on top.
SUITES = (("rng", test_rng), ("flood", test_flood),
          ("weather", test_weather), ("triggers", test_triggers),
          ("sim (closed loop)", test_sim), ("search (RHEA)", test_search),
          ("play (CORA wiring)", test_play), ("economy", test_economy))


def main():
    failed = []
    for name, mod in SUITES:
        print(f"\n=== {name} " + "=" * (60 - len(name)))
        rc = mod.main() if hasattr(mod, "main") else 0
        if rc:
            failed.append(name)
    print("\n" + "=" * 64)
    if failed:
        print("FAILING SUITES:", ", ".join(failed))
        return 1
    print("ALL SUITES PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
