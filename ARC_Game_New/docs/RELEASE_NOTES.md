# CORA release notes — study rules 1.0.2

This release brings the game, the LLM officers, the benchmark, the search agents and the RL
environment onto the study build's 1.0.2 rules, with one shared core (`cora/`) so that a
benchmark score, an RL return and a playtest are the same quantity. Start from the README; the
task-level guides are the skills in `.claude/skills/` (also readable as plain Markdown):

| Skill | For |
|---|---|
| `cora-benchmark` | scoring an LLM, a decision model, a baseline or a search agent, locally or on Auton |
| `cora-rl-training` | PPO on the Verlog fork (branch `release/cora-rl-v1`), resuming, diagnosing failures |
| `cora-surrogate-parity` | keeping the Python surrogate exact after a rules change |

## What changed

- **Surrogate backend.** `CoraEnv(backend="sim")` plays `oracle.sim.SimEnv`, a Python port of the
  headless build at ~1 ms a turn (Unity: ~2.8 s). Replayed on Unity 1.0.2 it gives the same final
  score on all 4,217 search plans and all 32 LLM games checked.
- **Search agents** (`oracle.search`): MCTS and RHEA over macro-moves, UCT over sampled baskets
  (progressive widening `--alpha`, common random numbers `--crn`, tree reuse `--reuse`), and
  greedy-N. **Baselines:** `rules` is the reference; `greedy-1/2/3` (best basket N decisions
  ahead, surrogate only). `build-potential`, `combined` and `pareto` are retired.
- **Observation:** facilities print `pop+N` for people already on their way, which the game
  counts against free space.
- **Benchmark harness:** `--tool-rounds` for sequential tool callers (gpt-oss), harmony tool-name
  cleanup, `--decision-schema v1|v2`, `--env sim`, servers for JEV and for an LLM answering
  typed decision questions.
- **Playtest fixes** (Oct 7 Talos playtest): a task can be answered once (the +$50,000 Storm
  Funding could be booked twice) and delayed funds are no longer counted twice; officers offer
  and execute only full staffing; the stale "committed this phase" ledger is gone; officers see
  the room left to relocate into.

## Verification status

| Component | Status |
|---|---|
| Unit and parity tests | 273 passed, 2 skipped (`pytest`) |
| WebGL client | built from this commit (Unity 2022.3.62f3) |
| Surrogate vs Unity 1.0.2 | exact final score on every replayed game checked |
| Verlog trainer path on the surrogate (CPU) | whole rules-policy games through `get_balrog_env`: rewards sum to the final score, equal to `python -m bench` on the same seed (`smoke_arc_game_env.py --backend sim --policy rules`) |
| PPO on Qwen3-4B, 4xA6000 | 2-step smoke passed on the surrogate (14k actor packing with fused kernels); a 60-step run reached step 9 before a full scratch disk failed its checkpoint write. **The release sbatch's own smoke and the 60-step run are pending: the Auton cluster is down.** |
| Playtest with Qwen3.8-27B officers on Talos | **pending Auton** (the officer model is served there) |

## Reference numbers (surrogate, seeds 7000-7031, 32 games each)

| Policy | Score |
|---|---|
| UCT-f7, 16k steps/decision, CRN 32 + reuse | 1.633 |
| MCTS, 64k | 1.607 |
| rules | 1.498 |
| Qwen3.5-35B-A3B | 1.22 |
| Qwen3.8-27B | 1.19 |
| Qwen3.8-27B answering typed decision questions (v1) | 1.09 |
| gpt-oss-20b (`--tool-rounds 8`) | 0.97 |
| Qwen3-4B | 0.84 |
| Clef / JEV-9B (v1) | 0.72 / 0.72 |

## Open questions for the team

- **Reward shaping vs task reward.** With the sbatch defaults (format penalty 0.05, semantic
  penalty 0.05 per refused call, +0.10 for a turn whose calls all execute) the rules policy
  collects −0.05 to +1.3 of shaping a game against a task score of ~1.5, and an empty turn — often
  the right move — costs 0.05.
- **Capacity refusals the GUI does not grey out.** Most refused task answers from strong models
  are capacity limits (casework site full, not enough shelter room, meals already scheduled) that
  human players also only learn by trying. Marking them unavailable for models would make the game
  easier for them than for people.
- **Decision models overspend.** Every decision model ends ~$470k-680k in debt under both schemas
  (rules ends +$632k): they serve demand and do not weigh cost.
- **Game text:** casework task descriptions read "Clients have been in shelter for Motel rounds"
  (a template substitution bug in the game, not the harness).
- **Waste can exceed kitchen output** (the Oct 7 playtest; BUG_REPORTS A1): kitchen leftovers
  still count as waste, and vehicle cargo or a closing kitchen can be counted twice. Needs a rules
  ruling.
