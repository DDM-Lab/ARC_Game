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

NEXT: finish the 32-round capture of the other ten seeds (running), re-run diag_marks across
all of them to confirm draw-exactness generalises, then chase the single lodgingResolved
round-5 lag. Do NOT re-lower the ratchet floor; either fix the counter or make the case to
the maintainer for rebasing the ratchet on the 32-round set.

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
