# CORA Surrogate — Implementation Plan

## Goal
A pure-Python simulator that is **bit-exact** with the Unity headless build and runs
**sub-millisecond per step**, so RHEA/MCTS can search where Unity cannot.

Measured Unity cost: save_state 99ms, load_state 602ms, advance_round 428ms
=> ~1.0 s per search node => ~1 node/sec. Search needs 10^4+ nodes.
Target: <1 ms/step => ~30 ms per 32-round episode => >10^4 nodes/sec. ~30,000x.

## Core shape
    step(state: GameState, actions: list[Action]) -> None     # MUTATES IN PLACE
    state.clone()                                             # explicit, only at branch points

RNG STATE LIVES INSIDE GameState AND CLONES WITH IT. An external rng parameter is a
determinism bug: an MCTS node re-expanded later would see a different stream than it saw
the first time. (Caught in review; the earlier signature had this defect.)

No frames, no scheduler, no engine. Unity's 34 frames/round (simulationDuration 10s /
GYM_FIXED_DELTA 0.3s) are an artifact of a rendering engine, not semantics:
  - DeliverySystem.AssignPendingTasks  early-returns on empty queue, drains greedily;
                                       10 calls == 1 call. Collapses.
  - ResourceFlowManager                enableAutomaticFlow = false. Dead. STUB + ASSERT.
  - TaskSystem.UpdateRealTimeCountdowns  linear in elapsed time; 34 x 0.3s == one 10.2s
                                       decrement. CheckExpiredTasks idempotent. Collapses.
LOAD-BEARING CLAIM TO VERIFY FIRST: no per-frame path draws randomness. If true, frame
count cannot affect the RNG stream and the collapse is exact rather than approximate.

## Round phase order (mirrors Unity)
 1. apply actions (build / hire / train / staff / transfer / task choices)
 2. resolve deliveries — reachable this round or not at all (user-confirmed)
 3. tick construction + training countdowns
 4. motel billing, food consumption, food production
 5. flood spread + shrink   <-- highest draw volume
 6. triggers -> task generation -> expiry -> penalties
 7. update the 15 accumulators
 8. day boundary: weather roll + day-rollover sequence

## Reuse (no reimplementation)
  reward_scoring.compute_score_components   scoring, unchanged
  cmd_parser.parse_commands                 action vocabulary, so plans are executable in Unity
  cora_tools                                typed action schema
  obs_encoder                               optional rendering for LLM-in-the-loop

## Bit-exactness
Achievable ONLY because this session made Unity's iteration order contents-dependent
(5 sorts: currentFloodTiles, riverTiles, expansionCandidates, floodArray, roadList).
Those sorts are now load-bearing for the surrogate and must be commented as such.
  - xorshift128 matching UnityEngine.Random.State {s0,s1,s2,s3}
  - same draw sites, same order, same count
  - SnapshotDebug ([RNGMARK] label + state) is BOTH the specification and the oracle:
    run instrumented Unity, capture the ordered draw-site sequence, require the port to
    emit the identical sequence. This also resolves multicast-delegate ordering
    (WeatherSystem.OnDayChanged vs TaskSystem.OnTimeSegmentChanged) empirically rather
    than by reading C#.

## Performance design (target ~100 us/round; 1 ms is the FAILURE threshold)

THE RNG DRAW PATH IS THE COST CENTRE, not state copying. Measured draw volume per round,
counted from the actual C#:
  flood spawn    one draw per RIVER tile, unconditional (verified FloodSystem.cs:486-492)
  candidate build one draw per NEIGHBOUR OCCURRENCE, dedup happens AFTER (verified :541)
  flood shrink   one draw per flood tile
  client check-in clientCount + 1 draws (verified ClientStayTracker.cs:39/46 - per PERSON)
  ERV purchase   2 draws per vehicle
=> ~300-500 draws/round, ~16k/episode. At ~0.3-0.5us per naive draw that is 0.15-0.25ms
   of the budget from RNG alone. Optimise here first:

  1. INTEGER-THRESHOLD COMPARISON. `Random.value < chance` is equivalent to `raw < T` for
     an integer T computed ONCE per distinct chance per round. ExpandFlood has exactly 3.
     Keeps float32 emulation off the per-draw path entirely. Pin Unity's raw->float
     mapping empirically via RNGMARK before relying on it.
  2. BATCH PRE-GENERATION. Draw counts are known before drawing (the flood set is frozen
     during candidate build). Generate k raw uint32s in one tight loop with s0..s3 in
     LOCALS - no attribute access, no method call per draw.
  3. NO INSTRUMENTATION IN RELEASE. A mark per draw (string + append) would cost more than
     the draw. MarkingStream is a separate class, not an `if debug:` in the hot loop.

  Structural:
  - dataclasses with __slots__; int/enum keys not strings; no dicts in hot paths
  - the 15 accumulators as a fixed array, laid out so
    `partial + best_possible_remaining()` is ~1us for rollout cutoff
  - flood tiles as packed ints WITH OFFSET (`((x+512)<<10)|(y+512)`) - coords can be
    negative and naive packing breaks the sort order the C# comparators rely on
  - sort the 50 packed ints per use (~2us); do NOT build an incrementally-sorted structure
  - NEVER dedup neighbours before checking them: the draw is per occurrence and dedup is
    after. Deduping early silently changes the draw count and desyncs the whole stream.
  - state layout must make ACTION LEGALITY MASKS cheap (flat ints for facility
    status/capacity, workforce counts as a small array). Search spends real time
    enumerating legal actions; retrofitting this onto an object graph is the expensive one.
  - static map data (terrain, cell bounds, blocked set, river tiles, roads) exported from
    Unity to JSON ONCE, not re-derived. The bounds check gates whether a draw happens.
  - clone(): hand-rolled, 5-20us. NOT undo-log (one missed entry = silent divergence, and
    the whole method rests on exactness), NOT persistent structures (~10x in Python).
    copy.deepcopy is BANNED BY NAME - 50-100x slower and reflexively reached for.
  - scoring lazily at episode end, not per round
  - scaling path: PyPy first (near-zero cost if the code stays pure; ~10x), then
    multiprocessing across candidates/rollouts (embarrassingly parallel - keep state
    cheaply picklable). mypyc later if wanted.
  - REJECTED: numpy batching across RHEA candidates. Control flow diverges per candidate
    (different flood sets -> different candidate counts -> different stream offsets), and
    SIMD does not survive divergent branching. numpy scalars are also slower than Python
    ints at this size and foreclose PyPy.

## RngSource: one simulator, two modes
Inject an RngSource; write the mechanics once.
  UnityStream        single xorshift128, bit-exact. All validation, and any plan meant to
                     be replayed in Unity.
  DecoupledStreams   per-subsystem, keyed (seed, round, site). This is the SEARCH mode and
                     it is better than "approximate": it gives common random numbers for
                     free, so a candidate's weather/flood no longer shift because its
                     actions consumed a different number of client draws. Large variance
                     reduction on candidate comparison.
  MarkingStream      wraps either, emits RNGMARK. Validation builds only.

## Validation ladder
 1. mark-sequence equality: port emits the same [RNGMARK] site sequence as Unity
 2. state equality per round: same seed + same actions => identical state, all fields
 3. trajectory equality across seeds/depths, reusing the snapshot_equivalence harness
 4. adversarial differential on MOTEL BILLING specifically (89-97% of all spend; an
    off-by-one at the day boundary is worth more than every other mechanic combined and
    RHEA would pump it). Drive Unity via save_state/load_state through check-in/check-out
    at every round offset; demand exact lodgingSpend agreement.
 5. conservation invariants as runtime asserts: population conserved, sum(spend) == budget
    delta, workforce status counts == totalWorkers

## Drift control
Ideal is one source of truth; not achievable across C#/Python. Therefore:
  - co-locate the port with the C# so a Unity change and its mirror land in one commit
  - commit a golden corpus: per-seed [RNGMARK] sequences + per-round state from Unity
  - CI test fails if the port diverges from the corpus
  - regenerate the corpus deliberately when Unity mechanics change

## Build order (by draw volume and risk)
  1. rng.py + equivalence harness      (nothing lands without a test)
  2. flood.py                          highest draw volume; what broke this session
  3. triggers.py                       ~7 state thresholds + ProbabilityTrigger
  4. tasks.py                          generation, expiry, choices, late-delivery retro-credit
  5. mechanics.py                      budget, construction, workforce, motel, deliveries
  6. search.py                         RHEA + MCTS
Each mechanic lands only when mark sequence AND state match Unity exactly.

## Float32 discipline
Unity computes in float32; Python in float64. Integer-valued floats below 2^24 are exact,
so only genuinely fractional quantities need care:
  - weather cumulative sums, trigger thresholds, casework `base * pow(growth, Y-1)`
    (Mathf.Pow is float32; Python ** is double and WILL differ in low bits, occasionally
    flipping a `<`)
  - validation must compare float fields BY FLOAT32 BIT PATTERN. A float64 tolerance
    comparison "passes" on drift that later flips a comparison.

## Search design to bake in NOW
  1. common random numbers via DecoupledStreams (unbolttable later)
  2. optimistic bound `partial + best_possible_remaining` for rollout cutoff - drives the
     accumulator array layout
  3. action legality masks (see state layout)
  4. a heuristic rollout policy slot (greedy staffing); uniform-random rollouts starve
     everyone and give flat signal
  5. NOT transposition tables - stochastic transitions plus set-valued flood state make
     repeats rare; skip Zobrist unless search profiling says otherwise

## Known fidelity risks
  - late-delivery retro-crediting (RewardMetricsTracker.AddLateDelivery/AddLateFoodTask):
    fulfillment credited when a delivery lands AFTER its task closed. Round-granular given
    "reachable in 1 round or not at all", but the ordering rule within a round must be
    ported from the actual increment sites, not inferred.
  - food production/consumption constants not yet located (GameDataManager/config)
  - casework resolution rule not yet read in full
  - WITHIN-ROUND PHASE ORDER IS NOT PROVEN BY THE FRAME-COLLAPSE ARGUMENT. Unity
    interleaves delivery-landing / countdown / expiry per frame; a delivery landing
    "before" vs "after" an expiry lands in a different accumulator (retro-credit vs
    on-time). The phase order must be derived EMPIRICALLY from mark sequences + per-round
    state equality, not asserted. Ladder step 2 is what catches this.
  - RNG SITE INVENTORY MUST COME FIRST, before mechanics.py. Vehicle-purchase and
    client-check-in draws live in phases 1/4; if the inventory is deferred, mark-sequence
    validation passes on action-free episodes and explodes the first time a plan buys an ERV.
  - dead-site asserts at init rather than trusting the collapse: ResourceFlowManager
    (enableAutomaticFlow == false), PathfindingSystem.TestRandomPath (ContextMenu only),
    TaskSystem.DebugTestPerFacilityProbability (no gameplay callers), ForestVisual.Awake
    (draws on instantiation - assert no forests spawn mid-episode)
  - regenerate the golden corpus after ANY SCENE OR BUILD CHANGE, not just C# mechanics
    changes: multicast subscription order is a property of the scene, and MainScene is an
    LFS file where merges have silently changed component state before.

---

## Status (as built)

Rungs cleared, with the evidence each one rests on:

| rung | state | evidence |
|---|---|---|
| RNG transition | done | 2663/2663 (before, after) state pairs from the live build |
| `Random.value` mapping | done | `(raw & 0x7FFFFF) / (2^23-1)`, fitted to two spawn rows Unity logged; rivals rejected in-test |
| `Random.Range(int,int)` | done | `lo + raw % n`; scaled variants break 37-39 rounds of the fixture |
| `Random.Range(float,float)` | done | reversed lerp `min*t + (1-t)*max`; 15/15 vs 5/15 for the forward form |
| flood | done | 72/72 rounds, 8573 draws, three levels (marks, phase counts, tile sets) |
| weather | done | 15/15 selections across three episodes |
| full-round draw census | done | every inter-round interval explained; no uninstrumented drawer |
| triggers / tasks | **next** | blocked on the facility model, see below |
| budget / construction / workforce / deliveries | not started | |
| RHEA / MCTS | not started | deliberately last; searching a stream that desyncs mid-round launders the divergence into noise |

### What the census bought

Chaining each round's exit RNG state to the next round's entry state accounts for every
draw with flood, `TaskTrigger.probability` and `Weather.select` alone. The stochastic
surface of a round is three sites, not the eighteen this plan assumed. Two are now ported.
It is a measurement on episodes with no construction and no deliveries, so re-run it on an
episode that exercises those before treating it as general.

### Corrections to this plan, from building against it

1. **"Transcribe the constants from the C# source" was wrong** and would have corrupted
   every mechanic downstream. `FloodParameters` is serialized on a MonoBehaviour, so the
   scene overrides every field initialiser: `blockingRadius` 1 -> 5,
   `randomExpansionChance` 0.15 -> 0.4, `maxRandomExpansionDistance` 2 -> 5,
   `terrainBlockMultiplier` 0.1 -> 1, every `shrinkageChance` -> 0.3. Constants now come
   from the `sim_constants` RPC and no number is read out of a `.cs` file.

2. **The plan's validation ladder was too weak at the top.** Matching totals hide
   mismatched phases, and a correct draw sequence can still write the wrong tile. Every
   mechanic is now checked at three levels: mark sequence, per-phase counts Unity prints
   itself, and resulting state against the next round's captured input.

3. **Rival rejection belongs in the tests.** `raw/2^32` for `value()` and the forward lerp
   for `Range(float,float)` are the natural simplifications, both wrong, both
   plausible-looking. The tests assert the rivals FAIL, so neither can be reintroduced as
   a cleanup.

### Open hazard: facility iteration order

`FindAllSuitableFacilities` returns `FindObjectsOfType<Building>()` order, which Unity does
not specify. Observed: `Community01, Community03, Community02` — not creation order, not
name order. Each facility rolls its own `ProbabilityTrigger`, so this order decides WHICH
community gets a task, not just how many draws happen.

This is the same class as the five HashSet-ordering bugs fixed in `FloodSystem`, but it has
not been shown to actually diverge (snapshot equivalence passes 9/9), and sorting it would
change which community receives tasks in a shipped, already-benchmarked game. So the port
will reproduce Unity's order from an export rather than assume one, and whether to sort it
in C# is a call for the maintainer, not a silent fix.
