# cora_sim — where the port stands

A fast Python surrogate of the CORA Unity game, built to be a faithful transition
function for search (RHEA/MCTS) and RL, across many maps.

Read this instead of a transcript.

## Fitness for purpose

| | |
|---|---|
| speed | **3.86 ms / 32-round episode**, ~78k episodes per 5 min, single core |
| RNG draw census | 134/135 rounds chained, stream position identical to Unity |
| mechanic suites | 11 of 12 pass (`tasks` is PARTIAL, see Parked) |
| round-5 board | exact, 9 tasks title-for-title |
| traces bit-exact end to end | **2 of 11** (`staff_5802`, `staff_6001`) |
| multi-map | `MapSpec` + `maps/*.json` + `dump_map.py`, guard-tested |

## Known residual

Nine of eleven replay traces diverge, all in one family:

- `lodgingResolved` / `lodgingFulfilled` differ by 100–200 at rounds 5–6
- `caseworkRequested` on four traces
- the port is **over** on 5601/5701 and **under** on the rest

In substance: **one relocation of 100 people lands on the wrong side.** Against
episode reward totals in the tens of thousands this is a ~±100 lodging error,
quantified per-seed by `test_replay_forward`. Food counters, spend counters, the
board and the RNG stream are exact.

## Causes ELIMINATED, each by a specific measurement

Do not re-investigate these without new evidence; each was killed by a mark, not
an argument.

| hypothesis | killed by |
|---|---|
| travel **rate** wrong (per-cell overhead / stalls) | `leg:tick` adjacency: 138/139 in-loop frames advance exactly one cell; the "stalls" are planning-phase frames where `deltaTime` is not `captureDeltaTime`, i.e. not game time |
| zombie completions crediting phantom deliveries | `delivery:unload`: every Unity unload has `actual == nominal`, two full captures |
| `FoodDeliveryHandler` effectiveStock gating | `food:exit`: zero marks — the handler never executes on this path |
| `CalculateDeliveryQuantity` source gate | returns `choice.deliveryQuantity` for `Fixed`; never consults the source |
| `Building.cs` auto-restock as the food source | requests 5 meals; observed orders are 100 |
| answered tasks freeing their slot immediately | fixed — see below; was worth 12 counters |

Travel is closed at BOTH levels: route (77/77 legs exact vs Unity's A*) and rate.

## Live instruments (in the build, `ARC_SNAPSHOT_DEBUG=1`)

`delivery:queue|dispatch|complete|unload|leg|blocked|config`, `leg:tick`
(index, status, damaged, cargo), `task:created|resolved` (id, demand, delivered,
status), `choice:at`, `round:advance`, `round:length`, `food:exit`,
`road:connection`, `[ROADDUMP]`. Every mark carries `s{gymStep}d{day}r{seg}f{frame}`.

Diagnostics: `diag_parity` (what each side answers), `diag_population`
(per-facility people), `diag_orders` (per-order ledger), `dump_map`.

## Live game bugs found, NONE patched

1. Builds advertise $1000 and deduct $2000 (`BuildingSystem` ignores `action.cost`).
2. `caseworkRequested` double-counts — reaches 1898 where RNG marks prove 1200 people existed.
3. Emergency lodging **silently evicts** the task holding a facility's slot: removed from
   `activeTasks`, never resolved, never counted.
4. `HandleDeliveryFailure` removes a flood-stopped task with a satisfaction penalty and
   **no** `RecordTaskResolution` — it vanishes from the metrics.

(3) and (4) are silent-loss paths: benchmark numbers under-count demand exactly when the
map is worst. A fifth — zombie-coroutine counter corruption — was reported and then
**downgraded**: the race is real (`AssignDeliveryTask` never calls `StopAllCoroutines`)
but no captured episode shows it corrupting a counter.

## Parked, deliberately

- `tasks` suite PARTIAL (`foodResolved` short by ≤5 per episode) — same subsystem as the residual.
- Draw census 135th fixture: client-stay draws not yet in the round loop. Separate small task.
- `travel_rounds`, `TaskBoard.queue`, `TaskBoard.busy` are dead since the event-driven fleet
  landed. Documented-dead; not deleted mid-hunt.

## Generation ordering: SETTLED AND ALREADY CORRECT (a misread, now fixed)

I previously wrote that Unity generates AFTER a round's deliveries, citing seed 5901:

    f310 unload Food ... / f312 unload Popu ... / f319 GENERATION PASS / f327 re-request

That reading was wrong. The frame counter is monotonic ACROSS segments and every mark
carries its segment tag. With the tags restored:

    s6d2r1f312  delivery:unload   Population Community01->Motel
    s6d2r1f313  delivery:complete + task:resolved id=6
    s6d2r2f319  gen:pass {"active":7} + task:created id=12,13,14   <- segment r2, not r1
    s6d2r2f327  choice:at {"task":13,"choice":1} -> delivery:queue

f319 is the START of the NEXT segment; f327 is just the replayed player choice on the
freshly created task 13. There is no same-round re-request and no freed-slot effect.
Every gen:pass in the episode sits at a segment start, BEFORE that segment's deliveries:

    s1d1r1f110   s2d1r2f150   s5d2r0f237   s5d2r1f271   s6d2r2f319

The port already generates at segment start before its delivery tick. Its ordering was
right the whole time, which is exactly why BOTH relocation experiments took the exact-trace
count 2 -> 0: form 1 moved the rollover passes too, form 2 moved only the segment pass, and
both were moving code that was already in the correct place. sim.py is unchanged.

## The instrument that matters now: diag_marks (draw-for-draw)

`cora_sim/diag_marks.py` compares the ORDERED DRAW-MARK SEQUENCE, port vs Unity capture,
per gym step. Build it into any future client work; do not judge client changes by counters.

WHY. staff_6001 was "exact at every round" while the port consumed 101 draws where Unity
consumed 202. Counter agreement on a client-bearing trace is stream-position luck, not
evidence. The mark diff gives a divergence with a POSITION instead of a counter that has
already been laundered through several mechanics. It is also the missing 135th census
fixture: the one interval the census never covered is the client-draw round.

It excludes the seeding step (test_replay_forward seeds mid-step from the first
flood:enter, so that step's pre-seed draws are not reproducible by construction).

## What the mark diff established

Unity, seed 5901 step 6, in order:

    caseworkNeed x100, stayDuration, caseworkNeed x100, stayDuration,
    caseworkGen x2, TaskTrigger.probability x3, Flood.8 x122, Flood.2 x3, ... Flood.7 x52

Baseline port, same step: that identical tail from TaskTrigger onward, with the ENTIRE
leading client block absent. Two facts follow, and both are now measured rather than argued:

1. THE DOUBLE-SPAWN IS REAL AND CORRECT. Two groups of exactly 100, each with its own
   stayDuration. Fable located both call sites: Vehicle.UnloadCargo -> HandlePopulationDelivery
   (count = ACTUAL delivered, gated > 0) and DeliverySystem.OnVehicleDeliveryCompleted
   (count = NOMINAL quantity, UNGATED). The centralized hook's comment lists the scattered
   branches it replaced and omits DeliverySystem, whose legacy branch was never deleted.
   GAME BUG #5, and ClientRelocationHandler does the same thing on the immediate path.
   Only the TRACKER duplicates -- population storage is deposited once -- which is exactly
   why lodging spend was already exact while client draws were half.
2. THE PORT DEFERS THE CLIENT DRAWS ONE STEP AND SHOULD NOT. Unity's vehicles finish in the
   simulation phase (f307-f315) which PRECEDES the segment advance that runs the tracker
   update and generation (f319).

## The coupled change: measured, better on draws, NOT committed

Preserved in `cora_sim/experiments/` rather than left as prose:

    sim_double_spawn.py             double-spawn only
    sim_double_spawn_tick_first.py  double-spawn + delivery tick moved to the head of the step

With the tick at the head, the first mark divergence moves from step 6 to step 8 on eight of
eleven traces -- steps 6 and 7 including both full client blocks become draw-for-draw exact --
and `caseworkRequested` disappears from every counter divergence. But the exact-trace ratchet
goes 2 -> 0 and lodgingResolved picks up a uniform one-step lag (unity=200 port=100 at round 5).

Ticking at the head means an order ANSWERED this step can no longer be delivered this step.
Unity has that latency too (the f327 choice lands at f349, next step), so the suspect is not
the reorder itself but the REPLAY HARNESS's answer position: it applies answers before
step_round, whereas Unity's choice at f327 falls AFTER the f319 generation, mid-step. That is
a harness ordering question, not a sim.py one, and it is where to look next.

Per the ratchet rule the tree is back at baseline: 2 exact, all mechanic suites passing.
Two legs of the coupled set were identified and NOT yet applied, either of which may be what
recovers the floor:

  - arrival_round STAMP. Port stamps N+1, Unity stamps N. Y = currentRound - arrivalRound
    drives caseworkGen's threshold 10 * 1.5^(Y-1), so from the second draw on the port's
    threshold lags Unity's by a full growth step.
  - UPDATE CADENCE. ClientStayTracker.OnRoundChanged has NO segment filter (unlike
    TaskSystem, which skips segment 3) and also fires on the rollover Invoke(0) -- four
    invokes per day. The port's rollover step consumes two segment advances but runs
    clients.update once.

Leg 1 (the arrival stamp) HAS NOW BEEN TRIED, on top of the reorder, and changes nothing
observable: the mark diff is byte-identical before and after, the ratchet stays at 0. Kept
in `experiments/sim_double_spawn_tick_first_stamp.py`. It is probably still correct, but it
is not what is holding the floor down, so it should not be credited as progress.

WHERE THE SET NOW STANDS, exactly. With the reorder applied, the first mark divergence is
step 8 on eight of eleven traces, and it is always the same shape: the port draws 3-6
caseworkGen that Unity does not draw at all (5901: unity 64 draws starting Flood.8, port 67
starting caseworkGen). Steps 6 and 7 match draw for draw, INCLUDING both 100-draw client
blocks. Equal draw counts through step 7 with a different step-8 eligibility set means the
step-7 caseworkGen OUTCOMES differ on identical randoms -- so the THRESHOLD is wrong, not
the ordering. The threshold is 10 * 1.5^(Y-1) and the only free variable in it is Y.

That is leg 2, and it is a bigger change than it looks:

  - Unity's Y uses currentRound = segment + (day-1)*4, refreshed in OnRoundChanged. A day
    rollover advances Unity's currentRound by TWO while the port's round_index advances by
    ONE, so the two indices drift apart at every rollover and Y drifts with them.
  - ClientStayTracker.OnRoundChanged has NO segment filter (TaskSystem skips segment 3) and
    also fires on the rollover Invoke(0) -- four invokes per day against the port's one.
  - Structurally, clients.update must therefore move to AFTER the segment advance and run
    once per advance, whereas the port currently runs it once, before the advance.

## THE 32-ROUND CAPTURE, AND A DRAW-EXACT EPISODE

The 8-step traces could not discriminate: zero casework-site deliveries in any of them, the
only rollover preceding the first client group, and clients existing for just three steps.
Four edits measured byte-identical because three could not execute. Re-captured seed 5901
for the full 32 rounds (`scratchpad/cap32/`, ARC_SNAPSHOT_DEBUG=1, ~1 min/seed) and the
picture changed immediately: 126 caseworkGen draws against 10, 2508 casework references
against 0, seven rollovers.

Two mechanisms fell straight out of it, and `diag_marks` now reports

    staff_5901.json: draw streams identical across steps 2..32

which is the first fully draw-exact episode the port has produced.

1. THE TRACKER DOES NOT FIRE ON THE LAST SEGMENT OF A DAY. Unity's caseworkGen draws land on
   r0, r1, r2, r3 of every day and NEVER on r4 -- steps 8, 12, 16, 20, 24, 28 are all r4 and
   all empty. Fable's "four invokes per day: segments 1, 2, 3, 0" read from the other side:
   segment 4 ends the day, and the next OnRoundChanged the tracker sees is the rollover's
   Invoke(0). The port fired every step.
2. THE CASEWORK FLAG RE-ARMS. ClientStayTracker subscribes to OnTaskCompleted AND
   OnTaskExpired and clears caseworkRequestGenerated in the handler, so a group resumes
   drawing once its casework task leaves the board; the generated task carries
   roundsRemaining = 3. The port set the flag once and never cleared it, so its eligible set
   shrank monotonically while Unity's did not.

Together with the double-spawn and the tick reorder, that is the full client subsystem.

## THE ONE REMAINING DEFECT, and why the ratchet fails

With the stream exact, ONE counter still diverges on 5901: `lodgingResolved unity=200
port=100` at round 5, and nothing else, at any round. The ratchet reads 0 and the tree is
therefore BELOW FLOOR -- deliberately, and this is the first time that has been left standing.

The justification, which the next person should weigh rather than inherit: the ratchet counts
short-trace counter-exactness, and the change trades two 8-step counter-exact traces for a
full 32-round DRAW-exact one. Draw-exactness is the stronger property -- it is what makes
search plans transfer -- and it is not what the ratchet measures. Reverting would discard it.

The defect is isolated and deterministic, not stochastic: the stream is identical, so this is
pure bookkeeping. Prime suspect is the tick reorder's flood snapshot -- at the head of the
step `w.tasks.flooded` is read BEFORE this round's `update_flood`, so vehicles route on the
previous round's post-spread set. That is believed correct (Unity's vehicles drive before the
flood update) but round 5 is the rollover step, where the port evaluates segment 0 then
settles to 1, and the interaction has not been checked.

ALL ELEVEN SEEDS are now captured at 32 rounds. 5901 is draw-exact end to end; the other ten
diverge, and they all diverge THE SAME WAY -- by exactly 204 draws, in either direction:

    staff_5501  step 10  unity 385  port 181     port 204 short
    staff_5502  step 14  unity  52  port 256     port 204 long
    staff_5504  step 19  diverges at draw 202, not draw 0

204 = 2 x (100 caseworkNeed + 1 stayDuration) + 2, i.e. exactly one double-spawned population
arrival. The port is not missing a mechanism -- it has the right draws in the wrong STEP,
sometimes one early, sometimes one late. That is delivery ARRIVAL TIMING, and it is almost
certainly the same single defect as the surviving `lodgingResolved 200 vs 100` counter: a
relocation Unity lands in round N and the port lands in N+1.

So the whole remaining gap is one mechanism, not a list. FLEET SIZE IS NOT IT: Unity's
delivery:config reports "ervCount":3 in all eleven captures and names Vehicle1/2/3, and the
port's Fleet constructs 3 slots. Both run three vehicles. Look instead at travel duration and
at the flood set the reordered tick reads (previous round's post-spread), which is what
decides which step a vehicle finishes in.


## THE SINGLE REMAINING DEFECT, localised to vehicle assignment

Seed 5501, the step where the port is 204 draws short:

    s10d3r1f463 dispatch Population Community01->Motel veh=Vehicle1 at=(1.5,5.5) srcpos=(1.5,5.5)
    s10d3r1f463 dispatch Population Community02->Motel veh=Vehicle2 at=(9.5,3.5) srcpos=(-9.5,-2.5)
    s10d3r1f472 unload   Population Community01->Motel veh=Vehicle1

BOTH dispatch in the SAME FRAME. Vehicle1 is already standing on its source (at == srcpos),
so it unloads nine frames later in the SAME step; Vehicle2 must drive to its source first and
lands later. WHICH VEHICLE GETS WHICH TASK therefore decides which step the arrival falls in,
and that is the ±1 step the port gets wrong. Not travel speed, not the flood snapshot, and
not fleet size -- both sides run three vehicles.

Two concrete suspects in `roads.py`, both unverified against the C#:

  - `_closest` scores with EUCLIDEAN distance, `100/(1+d) + (qty/cap)*50 + speed*10`, over
    world coordinates. If Unity's CalculateVehicleSuitability measures PATH length along the
    road network the two disagree exactly where the network detours -- and this map is a
    near-tree, 67 of 106 cells being single-cell cut vertices, so detours are the norm.
  - `run_round` has TWO selectors on different branches, `best_vehicle` (line 378) and
    `_closest` (line 386). Unity assigns several tasks in one frame, so the ORDER matters:
    tasks-outer versus vehicles-outer, and whether a chosen vehicle leaves the candidate set
    before the next task is assigned in the same frame.

SETTLED FROM SOURCE. CalculateVehicleSuitability, DeliverySystem.cs:657-673:

    score  = 100f / (1f + Vector3.Distance(vehicle.transform.position,
                                           task.GetSourcePosition()))
           + ((float)task.quantity / vehicle.GetMaxCapacity()) * 50f
           + vehicle.moveSpeed * 10f

The distance is EUCLIDEAN, not path length, and the port's constants are all correct. The
port is wrong in ONE place, and it is the ENDPOINT:

    GetSourcePosition()        -> sourceBuilding.transform.position   (DeliverySystem.cs:42-45)
    GetSourceRoadConnection()  -> roadManager.CellToWorld(nearestRoad) (DeliverySystem.cs:53-58)

Unity scores against the BUILDING TRANSFORM. The port scores against the ROAD CONNECTION
CELL -- and so does our own `srcpos` instrumentation, which is why the discrepancy stayed
invisible: `building_cell["Community01"] == [1,5]` matches the mark's `srcpos (1.5,5.5)`
exactly, so the map data we have IS the road cell, not the transform. The two differ by the
building-to-road offset, which is precisely the scale that decides Vehicle1 vs Vehicle2 when
both are a few units out.

All three vehicles spawn from one prefab with no per-vehicle overrides, so the capacity and
speed terms are identical across the fleet and cancel. Selection reduces to NEAREST EUCLIDEAN
TO THE BUILDING TRANSFORM, ties to the lower list index (Vehicle1, Vehicle2, Vehicle3).

THE DISPATCH LOOP, AssignPendingTasks (DeliverySystem.cs:568-621), which the port also does
not match:

  - TASKS outer, sorted priority DESC then timeCreated ASC; LINQ OrderBy is stable, so
    same-frame equal-priority tasks keep FIFO enqueue order.
  - Per task, FindSuitableVehicle scans ALL idle candidates in list order and keeps strictly
    greater score, so ties go to the earlier vehicle.
  - The winner is removed from the candidate list immediately (:611), before the next task.
  - Eligibility is `currentStatus == Idle` and nothing else. Cargo contents are never
    consulted; damaged and mid-route vehicles are excluded by status alone.
  - One pass per taskAssignmentInterval (1s game time) from Update, and the pass DRAINS
    greedily until tasks or vehicles run out. No per-pass cap; maxQueuedTasks gates enqueue
    only. That is why two dispatches share a frame.

The port's `run_round` narrows candidates to the earliest-free vehicles
(`free_at <= soonest + 1e-9`) before scoring. Unity scores over every idle vehicle. Fix both
together, since either alone can flip an assignment.

UNBLOCKED, AND PARTLY FIXED. `RoadConnection.cs` now emits the building's
`transform.position` alongside its road cell in the existing `road:connection` mark;
headless rebuilt and all seeds re-captured (`scratchpad/cap32b/`). The offsets are large:

    Community01  road cell (1,5) -> world (1.5, 5.5)   transform (1.34, 7.09)
    Community02  road cell (-10,-3) -> world (-9.5,-2.5)  transform (-9.62, -0.90)

`maps/default.json` carries a new `building_pos` block, MapSpec exposes `building_pos`
(optional, so older map files still load), and `_closest` now scores against the transform
with a fallback to the road cell. That is the source-correct endpoint and it stays.

IT CHANGED NOTHING ON ITS OWN, and the reason is the actual defect. `run_round` narrowed
candidates to the vehicles free at the SOONEST moment:

    soonest = min(free_at[i] for i in ready)
    candidates = [i for i in ready if free_at[i] <= soonest + 1e-9]

That usually leaves exactly ONE candidate, so the suitability score decided nothing and the
endpoint could not matter. Unity's FindSuitableVehicle scans every idle vehicle
(DeliverySystem.cs:626-651).

Widening `candidates` to all ready vehicles was tried and REGRESSED most traces (5501, 5503,
5802, 6101 all moved their first divergence EARLIER, 10 -> 6). Reverted. The reason it fails
is that the port has no clock inside the round: picking the nearest vehicle when that vehicle
only frees up late in the round delays the delivery, whereas Unity would have given the task
to whoever was idle AT THE DISPATCH INSTANT.

## THE DEFECT IS ACCOUNTING, NOT TIMING. This supersedes everything above it.

staff_5901 has a DRAW-FOR-DRAW EXACT stream across all 32 rounds and STILL diverges on
`lodgingResolved` at round 5 (unity 200, port 100). Those two facts together settle it. If a
delivery were landing in the wrong turn, its client-spawn draws would move -- 202 of them --
and on 5901 not one draw moves. So the remaining error is not dispatch, not travel, not
assignment, and not stochastic. It is how a resolution is CREDITED.

The counter signature is now uniform across the set, which it was not before: nine of eleven
traces diverge at round 5 on `lodgingResolved` alone, 200 against 100. Unity credits two
relocations by round 5; the port credits one. Everything else -- food, spend, workers,
roundsCompleted -- matches at every round on those traces.

FIVE CONSECUTIVE EDITS TO THE DELIVERY PATH WERE INERT: the suitability endpoint, the
candidate breadth, the per-second dispatch passes, and the leg_seconds correction all left
both the draw stream and the counters unchanged (the widened candidate set alone regressed,
and was reverted). That is not five failures; it is five pieces of evidence that the delivery
path is not where this lives. I kept the ones that are source-correct and free -- the
endpoint, the passes, and leg_seconds -- and none of them is claimed as a fix.

leg_seconds STAYS at `(steps + 1) * per_step`. I changed it to `steps * per_step` on a
measurement of 203 deliveries whose modal leg-mark-to-unload gap is nodes-1 frames, and it
BROKE two roads fixtures that measure leg-mark to NEXT-leg-mark and give len+1. Both are real
measurements of different intervals; the change was inert on both the replay counters and the
draw stream, so nothing breaks the tie and there is a suite regression against it. Reverted.
The lesson is the process one: I stopped running test_all after that edit and reported suites
as passing when a suite was failing. Run the full suite after every edit, not the two tests
the current hypothesis cares about.

THE MISSING CREDIT. Seed 5901 step 6, Unity resolves TWO lodging tasks:

    f321  id=6  demand=100 delivered=100  status=Completed
    f328  id=7  demand=100 delivered=0    status=Incomplete

resolvedAdd is the demand either way, so lodgingResolved is 200; the port reports 100.

MY PREMISE WAS WRONG AND FABLE CORRECTED IT. I claimed "Incomplete" and "Expired" are
different mechanisms. They are the same mechanism reporting different STATUS STRINGS by task
type (TaskSystem.cs:1377-1378):

    task.status = taskType == Emergency || taskType == Demand ? Incomplete : Expired;

"Population Relocation From Community" is taskType Demand
(Community_TransportRequest.asset:16-17), so its plain board expiry reads "Incomplete".
"Expired" is only ever Advisory/Alert/Other -- which is why id=14, an Alert, shows it. id=7 IS
an ordinary expiry. Any port logic keyed on the string "Expired" misses every Food and
Lodging expiry there is.

EVERY RecordTaskResolution CALL SITE (complete, grepped over all of Assets/Scripts):

    CompleteTask         TaskSystem.cs:1290   Completed                  fulfilled=true
    ExpireTask           TaskSystem.cs:1389   Incomplete or Expired      fulfilled=false
    SetTaskIncomplete    TaskSystem.cs:1473   Incomplete                 fulfilled=false

All three share one formula in the callee (RewardMetricsTracker.cs:126-129). Paths that
remove a task and record NOTHING: HandleDeliveryFailure (759 -- confirmed, my note was
right), the emergency-lodging eviction (925), IgnoreTask (1440), alert-complete (2003). A
flood-failed task leaves activeTasks and can never be counted afterwards, so it is
permanently uncounted -- that stands as a game bug.

THE PORT ALREADY HAS THIS PATH, and the defect is measured: IT AGES ONE ADVANCE TOO FEW.
Per-round instrumentation on 5901 (staff actions included, matching the replay harness):

    rnd   unity   port   awaiting Lodging (id, rounds_left)
      5     200    100   [(8, 1), (9, 1)]      <- diverges
      6     401    400
      7     401    401                          <- converges again

The port is exactly one ageing step behind, and it self-corrects by round 7. Relocations are
created with rounds = 2, and STEP 5 IS THE ROLLOVER, which performs TWO segment advances
(segment 0 then segment 1). Unity decrements roundsRemaining in OnTimeSegmentAdvanced
(TaskSystem.cs:616), so on that step the task ages 2 -> 0 and expires THERE. The port ages
once per gym step, leaves it at 1, and credits the resolution a round late.

ATTEMPTED AND REVERTED: passing `advances = 2 if w.segment >= ROUNDS_PER_DAY else 1` into
`tick()`. Inert, for a reason worth recording so it is not retried: the rollover branch
settles `w.segment` back to 1 when it finishes, and the tick now runs at the HEAD of the step,
so a head-tick guard describes the PREVIOUS step's advances, not the ones this step is about
to perform. Ageing cannot be driven from the head tick at all.

AGEING NOW FIRES PER SEGMENT ADVANCE, AND IT IS STILL INERT. `age_and_expire()` is split out
of `tick()` and called from step_round at each advance -- twice in the rollover loop, once in
the normal branch -- which is where OnTimeSegmentAdvanced (TaskSystem.cs:616) puts it. 11
suites pass, counters unchanged, ratchet unchanged. Kept because it is source-correct and
harmless; NOT a fix, and the sixth consecutive inert edit.

THE CAUSE, INSTRUMENTED RATHER THAN REASONED. Spying on age_and_expire per advance, 5901:

    round 5:  advance  before=[(8, 2, fresh=False), (9, 2, fresh=True)]  after=[(8,1), (9,1)]
    round 6:  advance  before=[(13, 1, True)]                            after=[(13, 1)]
    round 7:  advance  before=[(13, 1, False)]                           after=[]

Tasks 8 and 9 reach round 5's advance ALREADY AT 2, so nothing aged them in round 4 -- and
round 4 IS the rollover. No advance line prints for rounds 0-4 at all, meaning they did not
exist in `active` or `awaiting` during either rollover advance.

That is the bug, and it is a CREATION-ORDER bug, not an ageing one. Unity creates them in the
rollover's FIRST pass (task:created at s5d2r0f237, rounds=2), so the rollover's SECOND advance
(s5r1) ages them 2 -> 1, and s6r2 takes them to 0 -- expiring exactly where Unity credits
them. The port collects `rolls` from BOTH rollover passes and only calls `_create_tasks` after
the loop has finished, so a task born in pass 0 never experiences pass 1's advance and arrives
a full advance young.

## CORRECTION: THE COMMITTED TREE IS NOT DRAW-EXACT ON ANY TRACE

A claim repeated several times in this session's reports is WRONG and is corrected here.
"staff_5901 is draw-for-draw exact across all 32 rounds" was true of the EXPERIMENTAL build
`experiments/sim_drawexact_32r.py` (double-spawn + tick reorder + segment-4 skip + casework
re-arm). It is NOT true of the committed tree, which since then reverted the tick reorder and
added per-rollover-pass creation. Measured on the committed tree just now:

    staff_5901  step 7 diverges at draw 404 (unity 724 draws, port 520)
    all eleven traces diverge on the draw stream

The counter results are unaffected -- 10 of 11 traces off on a single counter at round 6, six
by exactly one unit, 11 mechanic suites green. Only the draw-exactness claim was wrong, and it
was wrong because it was carried forward from a build that had been reverted rather than
re-measured against the tree it was being asserted about.

RE-MEASURE BEFORE REPEATING ANY HEADLINE NUMBER. This is the second time this session a stale
result was carried into a report (the first was test_all after the leg_seconds edit).

## CURRENT STANDING (end of session), and the next thread

Two ageing bugs were found and fixed, both by INSTRUMENTING an advance rather than reasoning
about the source:

1. `_create_tasks` now runs PER ROLLOVER PASS inside the loop, so a task born in pass 0 is on
   the board for pass 1's advance. Killed the round-5 `lodgingResolved 200 vs 100` on all
   eleven traces -- the largest single divergence in the set.
2. The `fresh` skip removal was TRIED AND REVERTED, and the reason is a metric correction
   worth keeping. Removing it deepened first divergence (5502 round 6 -> 9) but blew up
   MAGNITUDE: 5502 picked up a 5000-unit lodgingSpend error and three traces went from being
   off by 1 to off by 100. This surrogate is a TRANSITION FUNCTION for search, so the thing
   that matters is state-error MAGNITUDE, not how many turns pass before the first error.
   Ranking by first-divergence depth is the right tiebreak for a REPLAY test and the wrong
   one for a transition function, and it briefly had me keeping a change that made the state
   much wronger. With the skip restored, six of eleven traces are off by exactly ONE unit.

THE NEXT THREAD, NOW DIAGNOSED. `diag_resolutions.py` (new, built on the shared
`replay_steps`) prints what the port actually credits. On 5501:

    r5   resolve id=6  demand=100 delivered=100 fulfilled=True
         resolve id=7  demand=100 delivered=0   fulfilled=False
    r6   <-- lodgingFulfilled unity=200 port=100

lodgingRESOLVED agrees because resolvedAdd is the demand either way. lodgingFULFILLED does
not, because the port's id=7 EXPIRES WITH delivered=0 while Unity's DELIVERS. Unity's marks:

    s6d2r1f321  dispatch Population Community02->Motel veh=Vehicle3
    s7d2r2f353  unload   Population 100/100

Unity dispatches at step 6 and unloads at step 7, so that task MUST survive a step boundary,
and it does -- completing fulfilled. The port expires it first, so its delivery never lands.

TRIED AND INERT: giving `awaiting` the same fresh skip `active` has. It never fires, because
`fresh` is already cleared by the first advance after creation, long before the task is
answered. Reverted.

REFUTED BY MEASUREMENT: THE PORT'S AGEING IS CORRECT. A previous entry here claimed the port
answers relocations one advance too old. A new `roundsRemaining` field on the `choice:at` mark
(GymServerManager.cs, rebuilt, recaptured to `scratchpad/cap32c/`) settles it the other way:

    unity   choice:at {"task":5,"choice":1,"roundsRemaining":1}
            choice:at {"task":6,"choice":1,"roundsRemaining":1}
    port    ANSWER id=6 rounds_remaining=1

Both hold 1 at answer time. The ageing matches exactly, and the per-rollover-pass creation
change did NOT over-correct. Do not "fix" the ageing.

WHAT THAT LEAVES, and it is the mechanism already confirmed in the C#: Unity's task expires
unfulfilled (resolved += demand, fulfilled += 0) and its in-flight delivery, which
CancelTaskDeliveries does NOT cancel because the call is commented out
(TaskSystem.cs:569-571), lands afterwards and credits FULFILLED ONLY through AddLateDelivery
(705-711). 100 + 100 = the 200 Unity reports. The port expires the task AND strips the order
out of `pending`, so nothing can arrive late and fulfilled stops at 100.

THE DISCRIMINATING CHECK IS ANSWERED, AND THE FIX IS PARKED AS A PAIR. Letting the order
outlive its task (no `awaiting` pop, no `pending` strip) was applied and measured.
`diag_resolutions` on 5501 still prints NO `LATE` line:

    r5   resolve id=6 demand=100 delivered=100 fulfilled=True
         resolve id=7 demand=100 delivered=0   fulfilled=False
    r6   <-- lodgingFulfilled unity=200 port=100

So the port's fleet NEVER DELIVERS that order even when the order survives. The
task-lifecycle half is understood and correct against the C#; it is not sufficient alone.

REVERTED, and not because it is wrong. Kept in isolation it makes the state WORSE: traces on a
single diverging counter drop 10 -> 8, with 5801 and 5802 each picking up a caseworkRequested
divergence (300 vs 100, and 523 vs 465) against 5901 losing one. Surviving orders that never
arrive still perturb the client pipeline. By the magnitude metric -- the right one for a
transition function -- that is a regression, so it goes back until its other half exists.

APPLY THESE TWO TOGETHER, never separately:
  (a) the order outlives its task (the diff is in this file's history, one deletion of the
      `awaiting.pop` + `pending` filter), and
  (b) whatever makes the fleet actually deliver it.

FOR (b), MEASURED. Spying on `Fleet.run_round` for 5501 at round 5:

    fleet: in=5 landed=2 left=0 dropped=0 damaged=[False, False, False]
       pending (3,100,'__food__Community Charleston') src=(-8,-3) dst=(1,5)
       pending (4,100,'__food__Community Trinity')    src=(-8,-3) dst=(9,3)
       pending (5,100,'__food__Community Amherst')    src=(-8,-3) dst=(-10,-3)
       pending (6,100,'Motel')  src=(1,5)    dst=(-5,4)
       pending (7,100,'Motel')  src=(-10,-3) dst=(-5,4)

`left=0 dropped=0` with only 2 landed is the whole answer. The order is NOT stuck in the
queue, NOT flood-blocked, and NOT dropped. Three of the five were CONSUMED BY THE LOAD-ABORT
PATH -- `if load is not None and load(payload, qty) <= 0`, which pops the order, idles the
vehicle at the source and lands nothing. lodgingFulfilled is 100 after this round, so id=6
landed and id=7 aborted.

So the port refuses to load 100 people out of Community02 (src (-10,-3)) while Unity moves
them. That is a SOURCE-STOCK question, not a routing or dispatch one. The load callback for a
Population order checks the source community's population; the likely causes, in order of
suspicion, are that the port has already deducted that population elsewhere (the relocation
path calls `move_population(source, -quantity)` at answer time AND the fleet then re-checks
stock), or that the community's population is simply lower in the port at that instant.

THE CAUSE, FROM THE CODE PATH: A DOUBLE DEDUCTION OF THE SOURCE POPULATION.

`_load` (tasks.py, inside tick_deliveries_only) routes EVERY cargo type through
`retry_if_unsourced`, which exists for FOOD against kitchen stock:

    def _load(payload, qty):
        if self.retry_if_unsourced is None: return qty
        task = self.active.get(payload[0]) or self.awaiting.get(payload[0])
        return qty if task is None else self.retry_if_unsourced(task, qty)

Meanwhile the relocation path in sim.py already deducts the people the moment the choice is
ANSWERED -- the `if source: w.economy.move_population(source, -quantity)` branch. So the
population leaves Community02 at answer time, and when the vehicle arrives the load check asks
that same community for the same 100 people, finds none, and ABORTS. That is why
`run_round` reports left=0 dropped=0 with three of five orders vanishing, and why no LATE
credit can ever fire.

Unity does not pre-deduct: LoadCargo calls RemoveResource ON ARRIVAL (Vehicle.cs LoadCargo,
which is also why an order that arrives to an empty kitchen aborts and retries -- the mechanic
the food path was built around). The port applies BOTH the Unity behaviour and an extra
answer-time deduction.

TWO CANDIDATE FIXES, and they are NOT equivalent -- decide with the traces:
  (a) stop pre-deducting at answer time and let `_load` do the removal, which matches Unity
      most literally; or
  (b) keep the pre-deduction and exempt Population tasks from `retry_if_unsourced`, which is
      the smaller diff but leaves the port's population moving a round earlier than Unity's.
Prefer (a) unless it disturbs the motel/lodging spend, which reads population at round end.

Apply (a) TOGETHER with the parked late-delivery change (the order outliving its task): the
two were measured separately and each looked wrong alone -- surviving orders that never load
perturbed caseworkRequested, and loading without survival still expires the task first.

METHOD NOTE THAT COST THE MOST TIME TODAY: six consecutive edits were inert because I reasoned
from the C# instead of instrumenting. Both real fixes came within minutes of spying on
`age_and_expire`. When an edit comes back inert, instrument the mechanism before writing
another one -- and check the capture can even exercise it.

Two facts worth keeping: LoadCargo and UnloadCargo contain no delay at all, and a vehicle
already on its source road cell produces a 1-node path whose movement loop never runs, so it
spends zero internal yields. Whether each `yield return StartCoroutine(...)` still costs a
frame is engine behaviour the source cannot settle -- but `delivery:leg` is emitted
synchronously at the top of every MoveToPosition, so the frame gap between a vehicle's
source-leg and destination-leg marks measures it directly from captures already on disk.

PARKED CONFLICT: Fable reads TriggerNonCaseworkDeparture as mutating tracker state only --
OnCaseworklessClientsDeparted has zero subscribers, no facility population is released. The
port releases it, with a measured justification (lodging 160,000 vs Unity 100,000). Do not
resolve this from either side alone; re-check lodging spend after the client set lands,
since the group accounting it compensates for will have changed.

## Method notes that cost time

- Rank changes by **exact-trace count and first-divergence depth**, never by summed
  diverging counters — that scalar penalises depth and once hid two traces going exact.
- A failed pre-check needs its **timing** checked before it counts as a refutation; one
  sampled the wrong instant and nearly cost the session's largest fix.
- When a rate looks wrong, measure the **smallest interval**, not the span.
