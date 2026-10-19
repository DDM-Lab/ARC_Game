---
name: cora-surrogate-parity
description: Check or restore exact parity between CORA's Python surrogate (oracle.sim) and the Unity game — replay surrogate plans or benchmark games on the headless build, find the first diverging decision and field, and refresh the parity fixtures. Use after changing game rules (C#) or the surrogate, or before trusting surrogate scores.
---

# Surrogate parity

`oracle.sim.SimEnv` is an exact Python port of the headless Unity build: same game state
payload, same action menu, same observation text, same score. Benchmarks and RL run on it
because it is ~1,000x faster; that is only valid while it stays exact. As of Unity 1.0.2 it
gives the same final score on all 4,217 replayed search plans and all 32 LLM games checked.

**Any change to the game's C# rules makes the surrogate stale until the rule is ported.** Port
rules by reading the C#, never by fitting constants: every divergence found so far was a rule.

## The ratchet (run after any rules or surrogate change)

```bash
pytest tests/test_surrogate_parity.py tests/test_sim_env.py tests/test_sim_rng.py
```

The committed fixtures are captured Unity games; every one must stay exact.

## Capture a new Unity game and compare

Unity needs an unsandboxed shell (batchmode fails with "unable to open database file" in the
sandbox) and a headless build under `Build/Headless/`.

```bash
python -m oracle.sim.capture 8101 --policy rules --epsilon 0.05 --explore-seed 1   # play + record on Unity
python -m oracle.sim.obs_diff oracle/sim/runs/<run> 8101 [--all]                   # first diverging decision/field
python -m oracle.sim.parity oracle/sim/runs/<run>                                  # -> tests/fixtures/sim_parity
python tests/test_sim_env.py build oracle/sim/runs/<run>                           # -> tests/fixtures/sim_env
```

`--epsilon` with `--explore-seed` plays random baskets on a share of decisions, so captures
reach states the plain policy never does.

## Replay plans found on the surrogate

Search agents and the benchmark write each decision's tool calls. Replay them on Unity and
compare the two scores:

```bash
python -m oracle.search 7003 --algo uct-f7 --budget 4000 --plan plan.json   # find a plan on the surrogate
python -m oracle.sim.capture 7003 --calls plan.json                          # replay it on Unity
python -m oracle.sim.capture 7000 --episode out/episodes.jsonl --index 0     # replay a benchmark game
python -m oracle.sim.divergence_triage RUN_DIR [RUN_DIR ...] [--all]         # why any plan scored differently
```

On the cluster, `ops/search_cluster.sbatch` does the replay for every seed unless `REPLAY=0`.

## Known gaps

The surrogate has the default map and parameter sheet only and renders nothing: `map_config`,
`param_config`, images and frame capture are refused by `CoraEnv(backend="sim")`. Refusal
reason *text* can differ from Unity's; the policy never sees it, and scores are unaffected.
