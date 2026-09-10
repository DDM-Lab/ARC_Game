# cora_sim -- status

**The surrogate reproduces the Unity game's end-of-turn state exactly on every validation
run we have (2026-09-08).** Four 32-round headless runs driven by EVOLVED plans (seeds 5503,
5901, 6001, 7002; `cora_sim/runs/validate/staff_N.{json,log}`, ARC_SNAPSHOT_DEBUG=1): the RNG draw
stream is identical draw-for-draw, and every reward counter, the budget and the final score
agree at every round. These are off-policy plans that build casework sites, strand vehicles
in floods, run two kitchens and go into debt -- mechanics the earlier LLM-played captures
never touched. The 14 earlier calibration captures were lost with the session scratchpad;
`cora_sim/runs/validate` is the ratchet corpus now (`test_lockstep`, part of `test_all`, fails unless
every seed there is exact). The other ten evolved seeds (5501 5502 5504 5601 5701 5801 5802
6101 7001 7003) were launched on headless with `cora_sim/runs/validate/run_batch.sh` on 2026-09-08 --
check `cora_sim/runs/validate/batch.log` and `python -m cora_sim.debug_lockstep` for their verdicts;
NOTHING beyond the four seeds above has been checked yet.

**2026-09-09, v1_testing (game-state-snapshot + main-bugfixes).** The merged headless build,
driven with the 14 pre-merge captures' recorded actions (`validate_plan --replay`), reproduces
every pre-merge capture round for round (`compare_captures`: 14/14 identical) -- the merge
changed nothing the surrogate can see, and its one gameplay change (reserved-cargo validity,
419f5762) never fired on these trajectories. Capture sets under `cora_sim/runs/`: `validate`
(pre-merge oracle), `validate_v1_replay` (merged build, same inputs), `validate_v1` (merged
build, the CURRENT debt-allowed action model driving -- new trajectories). Select with
`CORA_SIM_VALIDATE=<dir>` (the tools read one set; `test_lockstep` ratchets EVERY `runs/validate*`
set unless the env var is set). Ratchet: validate 10/14, validate_v1_replay 10/14, validate_v1 8/14.
The worktree checkout of v1_testing (`_worktrees/arc-v1-testing`) has `cora_sim/runs` and `.venv` as
symlinks into the main checkout (`ARC_Game/ARC_Game_New/`); a fresh clone has neither -- the run corpus
is untracked (`cora_sim/.gitignore`).
New this day: the headless emergency cap is 0 (a Unity bug, see UNITY_BUGS.md), and a
last-frame sibling landing keeps its parent open through the pass.

**2026-09-09, decisions closed on v1_testing.** Evolution keeps allowing debt (`CoraActions` default
`no_debt=False`). The repo-root `docs/Build` WebGL blobs from main-bugfixes are removed (the pages
scaffold `docs/index.html`, `TemplateData`, `StreamingAssets` remains, inert). The two "lost" UI pieces
are not re-added: their 00134227 deliberately removed the ExpandablePanel from DeliveryQueuePanel
(DeliveryQueuePanel.cs has hooked the same Button/Scroll View since 4aae16fd, so the component was a
second handler on one click), and the tutorial CostEfficiencyButton lives on as their `EffciencyButton`
in the redesigned metrics bar. InstructorConfigScene `serverSaveUrl` now points at our host
(`https://cora_game_llm.dev.ddmlab.com/cgi-bin/new_map_config.cgi`); that CGI is NOT deployed on our
Talos container yet (Apache proxies only /ws /configs /health /bundles + the sheet alias), so instructor
saves 404 until it is. Their `Assets/StreamingAssets/game_param_config.csv` is byte-identical to the
Talos `/sheet.csv` (which refresh-sheet.sh pulls from the Google Sheet) and no code reads it.

**2026-09-10, branch `v1_merge_test` — the surrogate tracks the new build.** Measured on a
32-round lockstep capture of `34d2133d` + the export additions below (`runs/merge_v4`, seed 5503,
the port driving, then replayed with `obs_diff`):

    RNG draw stream    identical for all 32 rounds
    final score        Unity 2.2103   port 2.2101   (delta 0.0002)
    counters           foodResolved/foodFulfilled, caseworkRequested/caseworkProcessed,
                       lodgingFulfilled, cumWorkingWorkers, every spend: EQUAL
    budget             equal to step 15, then +1 for the rest of the episode
    residual           lodgingResolved 700 vs 701; satisfaction 96 vs 95 at step 9;
                       a Casework_Request the port keeps on the board and Unity does not

THE TARGET IS OBSERVATION EQUALITY, NOT BIT-EXACTNESS (2026-09-10 ruling). The surrogate is a
leaf evaluator for RHEA/MCTS: what it owes is the same end-of-round observation and the same
outcome per action, not the same random stream. `obs_diff` is the instrument for that -- it
projects both sides into one canonical observation (budget, satisfaction, per-facility
status/pop/food/workforce, worker pools, task board, in-flight walks, every reward counter) and
diffs field by field, marking each field deterministic or stochastic and reporting the step where
the draw streams part. A deterministic diff while the streams are aligned is a mechanic bug; a
stochastic one after they part is not. That reframing is what made the last ten mechanics
findable -- the draw-stream diff only ever showed the first divergence.

PORTED THIS ROUND, each verified by the diff moving: the A1 clock (segment 4 is a real invoke:
ageing, tracker, storage and expiry all run there; the day-start pass is OnDayStarted, which
TaskSystem and the depletion manager subscribe to and the per-advance subscribers do not);
generation gate `newSegment < roundsPerDay - 1`; depletion draws AFTER the generation pass;
day-1 weather from the sheet; per-type consumption, consume-on-delivery, kitchen daily fill;
`NeedsFood`; the depletion manager; `PopulationBased` quantities incl. "deliver double";
self-walk relocation AND the casework walk (`ExecuteToSpecificDestination`: nearest site with
room, departure is the processing-home event crediting caseworkProcessed, arrival registers a
tracker group only at a lodging building); overnight food cancellation; exact-composition
staffing (B22); game-priced hire/train (B21); the sheet's daily allocation (B35); casework
credited by needy count (A7); departures leaving facility storage; not-yet-arrived hires
counting in the trained/untrained ratio; kitchen legs nearest-first capped by unreserved stock;
and expiry running on the Update AFTER the round -- after OnRoundEnd and the flood -- which is
what lets a relocation be completed by its own people arriving.

EXPORT ADDITIONS (`GymServerManager.HandleSimConstants`): per-building and per-prefab storage
settings, `communityDepletion`, `relocationDelayRounds`, per-choice `quantityType`/
`deliveryPercentage`, per-task `taskImpacts` (what ApplyTaskPenalties removes on expiry -- the
measured penalty table was keyed by pre-overhaul task ids), `initialState` (budget, satisfaction,
horizon, workers, day-1 weather), `buildGUID`, and `eventOrder` -- the clock's subscriber lists
in invocation order, so the port's phase order is a lookup instead of trace archaeology.
`validate_plan` writes a `.meta.json` beside each capture with the build GUID; `obs_diff` warns
when a capture and the corpus come from different builds. The export races the sheet's async
load: `initialState: null` means it was asked too early (the exporter now retries).

TOOLING FIX: `seed_state` seeds the port from the first `round:advance` (a step boundary), not
the first `flood:enter` (mid-step, after that step's generation pass). The old seeding made the
port replay a pass Unity had already run and `debug_lockstep` compensated by dropping Unity's
whole first step, so every draw comparison was an artifact reporting a divergence at index 3
that did not exist.

STALE CORPORA, not regressions: `runs/validate*` are PRE-overhaul oracles, so the ratchet reads
0/42 and `test_sim`'s closed loop fails against them; `test_triggers` fails on `cap_555_24r.log`
because Community_FoodRequest had probability triggers then and has none now. `test_flood` is
172/172. `runs/merge_v4` is the oracle for this build.

**2026-09-09, branch `v1_fixes` (NOT merged into v1_testing).** All 12 known bugs and most of the 35
audit findings are fixed in six commits (see `docs/BUG_REPORTS_v1_testing.md`, "Fix status"). The
surrogate is UNCHANGED and now models the OLD game: every `runs/validate*` set is still the
pre-fix oracle and the ratchet still passes 28/42 against them. A capture of the fixed build
(seed 5503, recorded actions replayed) is at `runs/fixed_v1/` -- deliberately outside the
`validate*` glob so the ratchet ignores it. Next job: re-derive the port against the fixed game
(rollover timing: segment 4 fires before the report and nothing at rollover; single registration
per delivery; actual-quantity credit; cancel path; casework by group; departures leave storage;
emergency spacing; agent prices 200/1000/300/2000; strict staffing composition), then recapture.


    make the numbers yourself, never quote them from memory:
      python -m cora_sim.debug_lockstep                    # every validated seed + the open-bug list
      python -m cora_sim.debug_lockstep 5503               # interactive: board / fleet / groups / draws per step
      python -m cora_sim.diag_lockstep cora_sim/runs/evo14.jsonl 5503
      python -m cora_sim.validate_plan cora_sim/runs/evo14.jsonl 5501 --port 21050   # add a seed (needs Unity, no sandbox)
    older instruments (need STAFF_TRACES captures in the harness format):
      STAFF_TRACES=<dir>/staff_*.json python -m cora_sim.test_replay_forward   # counters, per round
      STAFF_TRACES=<dir>/staff_*.json python -m cora_sim.diag_marks            # draws, per step
      STAFF_TRACES=<dir>/staff_*.json python -m cora_sim.diag_magnitude        # sum |unity - port|
      python -m cora_sim.test_all                                              # all suites + both ratchets

## What it is for

A Python transition function for RHEA / MCTS: `World.clone()` + `step_round()`, ~390 us per
clone+apply+step from a mid-episode state (measured 2026-09-08 after the casework/blockage
ports; ~2,500 search steps/s single-threaded); an idle 32-round episode is ~7 ms, a fully action-bearing one ~16 ms. Map data comes from `maps/*.json`
(`MapSpec`), constants from `corpus/sim_constants.json` -- both dumped from the running game.
The per-frame fleet and the flood update dominate the profile; nothing has been optimised.

## Speed vs fidelity, decided

Replay exactness against captured episodes is the target (user decision). The proposal to
batch the per-person client draws into a binomial for search was measured and rejected:
client draws are a small share of wall-clock, and the draw stream is the instrument that
found most of the bugs below.

## The instruments (use them before theorising -- every wrong turn in this project came from
## reasoning about the C# instead of measuring)

- `diag_marks.replay_steps` -- THE replay loop. Import it; two hand-copied loops dropped the
  staff-action branch and produced false findings.
- `diag_marks` -- ordered draw sequence per step vs Unity's [RNGMARK] stream. Counters can
  agree by luck while the streams have drifted; this cannot be fooled that way.
- `diag_magnitude` -- total |unity - port| over all rounds. First-divergence reports hide
  cumulative counters (lodgingSpend carried 46.7M of a 46.9M total and never diverged first)
  and make fixing the earliest error look like a regression.
- `diag_resolutions` -- which task resolutions the port credits, per round.
- `Fleet.events` -- set it to a list and the fleet logs (frame, event, vehicle, order): line
  it up against Unity's delivery marks with the round-start frame as origin.
- Unity marks worth knowing: `delivery:leg` carries the flooded road cells; `choice:at`
  carries roundsRemaining; `round:length` / `endSim:enter` bracket the 34 sim frames.

## The mechanics that had to be found (all verified against source AND captures)

Clock. GlobalClock.AdvanceTimeSegment returns before OnTimeSegmentChanged once the segment
reaches roundsPerDay, so segment 4 fires nothing: a day's invokes are 0, 1, 2, 3. Nothing
subscribed to the clock -- ageing, tracker, generation, consumption, production -- runs on the
last round of a day. Within an invoke the order is: TaskSystem generation, then
BuildingResourceStorage production then consumption (subscription order), then
CheckExpiredTasks on the next Update -- so a task that hits zero this invoke still holds its
slot through the pass.

Rollover. ProceedToNextDay: currentDay++, segment 0, OnDayChanged (motel billing, daily reset),
Invoke(0) [pass 0], then AdvanceTimeSegment -> Invoke(1) [pass 1]. Tasks created in pass 0 are
decremented by pass 1. Consumption (counter of invokes, interval 4) lands on the segment-0
invoke of every day from day 2. Kitchens produce +100/invoke to 200, are wasted at the day
change, and refill over the rollover's two invokes.

Tasks. resolvedAdd = demand > 0 ? demand : 1, fulfilledAdd = min(delivered, demand);
Emergency/Demand expiries read "Incomplete", others "Expired" -- same mechanism. Three
RecordTaskResolution sites: CompleteTask, ExpireTask, SetTaskIncomplete. In-flight deliveries
are NOT cancelled on expiry (CancelTaskDeliveries is commented out); a late arrival credits
fulfilled only. Route checks: CanCreateDeliveryWithEstimate at order creation, against the
current flood tiles. FloodedFacilityTrigger is per facility: flood tiles in a (2r+1)^2 square
around the building TRANSFORM, compared with its own enum order.

Clients. One population delivery registers TWO ClientGroups (Vehicle.UnloadCargo and
DeliverySystem.OnVehicleDeliveryCompleted -- a legacy branch never removed). Departures mutate
tracker state only; the motel's population is never freed except by a vehicle load, so motel
billing steps by a constant every day. The casework flag re-arms on task completion/expiry.

Fleet (frame-exact; see Fleet.run_round). 34 sim frames per round; the two paused frames after
still step coroutines (loop-exit checks, unloads at +35, completes at +36) but nothing moves.
Passes every 4 frames at cumulative sim frames = 0 mod 4, run before the frame's movement.
Source leg starts on the dispatch frame, L1 frames; LoadCargo has no yields and runs in the
arrival frame; destination leg +1, L2 frames to unload; complete +1. At-source: load +1, leave
+2. Suitability = 100/(1+euclid(vehicle, building TRANSFORM)) + qty/cap*50 + speed*10, over
all idle vehicles, ties to lower index, winner removed per pass. Load abort sets Idle in the
arrival frame; if the NEXT frame is a pass that hands the vehicle an order, the old coroutine
wakes into THE RACE: source leg skipped, destination at two cells a frame, nominal cargo
unloaded. CheckForFloodCollision every movement frame against the cell entered; a flooded
start cell may be left. A blocked/aborted order leaves the queue with its task.

## Unity bugs reproduced on purpose (the surrogate must match the game as it is)

1. Population deliveries spawn clients twice (two call sites).  2. Casework-site deliveries
double-process.  3. HandleDeliveryFailure removes a task without RecordTaskResolution -- a
flood-failed task is permanently uncounted.  4. Emergency lodging eviction records nothing.
5. Builds advertise $1000 and deduct $2000.  6. caseworkRequested credits the whole group.
7. The coroutine race after a load abort (AssignDeliveryTask never stops the old coroutine).
Fixing any of these in Unity means changing the surrogate and re-capturing.

## Scene overrides the .cs defaults (found nine times; read the scene, not the source)

caseworkNeedProbability 23.4 (code 40), moveSpeed 8 (code 5), Kitchen production/capacity
from Kitchen.prefab, FloodedFacilityTrigger parameters from the .asset files, and more in
`corpus/sim_constants.json`.

## Caveats

- One map, one action policy (the capture script's), eleven seeds. Held-out seeds are the
  generalisation check; see below.
- The replay harness answers tasks with a fixed policy (first offered choice), not Unity's
  recorded choices -- it happens to coincide on these captures.
- The race is modelled for a load abort only; a mid-race collision is not modelled.
- Shelter_Flood_Damage's trigger values are the .asset's; the scenario loader overrides them
  at load and the effective values are not yet dumped (no shelter emergency has fired).

## Held-out seeds

Three seeds captured for 32 rounds AFTER all calibration was finished, never used to fit
anything (`scratchpad/cap32_fresh`):

    7001: every tracked counter matches at every round; draw stream identical, steps 2..32
    7002: every tracked counter matches at every round; draw stream identical, steps 2..32
    7003: every tracked counter matches at every round; draw stream draw streams identical across steps 2..32

## 2026-09-09 batch 8 on v1_fixes: parameter sheet is live everywhere
- `GameConfigLoader` source chain: `ARC_PARAM_CONFIG` (CSV path; `validate_plan --param-config`, gym `param_config=`) -> sheet URL (browser only) -> `StreamingAssets/game_param_config.csv` -> serialized fallbacks. Headless no longer runs on the MainScene fallbacks; the log line `GameDataManager: parameters in effect {...}` (mark `config:loaded`) states every value.
- Reference captures: `runs/fixed_v1_b8/` (5503, 5504, 5801; real sheet: budget 8000, satisfaction 0, HeavyRain start, 5 ERVs, emergency cap 2, external cap 5). `runs/fixed_v1/` = batch 7 (old fallbacks). `runs/fixed_v1_parity/staff_5503` = batch 8 with a fallback-equivalent CSV, identical to batch 7 (wiring is behaviour-preserving).
- Surrogate must absorb: sheet values as the default parameterisation, `initialCommunityResidentCount` -> community population, kitchen production/capacities from the sheet, external-relation total cap (`[Limit]` on Storm Funding / Emergency Budget), day-1 human path = gym path, `vehicle:towed` after repair (B5).

## 2026-09-10 branch v1_merge_test: main-bugfixes d5e5f683 merged on v1_fixes
- Self-walk relocation (no vehicles for people; fixed 2-round delay; marks `relocation:queue`/`relocation:arrive`), kitchens fill to capacity daily, food never carries overnight (in-flight food cancelled at end of round 4), motel stores/eats food with its own request tasks, communities lose 100 meals to depletion events (chance = initialFoodDemandFrequency, day>=2, rounds 1-3; mark `draw:CommunityFoodDepletion`) and that spawns their request; shelters eat every 2 rounds.
- Smoke captures: `runs/merge_test/` (5503/5504/5801; not parity). The surrogate's delivery, consumption and generation models must be re-derived on this branch once the user confirms it becomes the mainline.
