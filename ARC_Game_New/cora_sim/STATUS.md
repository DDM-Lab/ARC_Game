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

LEG 2 HAS NOW ALSO BEEN TRIED and is ALSO INERT. `experiments/sim_leg2_tracker_cadence.py`
adds Unity's `currentRound = segment + (day-1)*4` for both the arrival stamp and the tracker
evaluation, and runs the tracker once per SEGMENT ADVANCE (twice on a rollover, after the
advance, before that advance's generation pass). The mark diff is byte-identical to the
reorder alone and to the reorder+stamp. Ratchet stays 0.

THAT IS THE INFORMATIVE RESULT, and it kills the hypothesis it was built to confirm. Three
independent changes to Y and to the tracker cadence produce the SAME diff, so the step-8
residue is NOT threshold-driven. (In hindsight one of the three was inert by construction:
shifting the stamp and the evaluation by the same formula leaves rounds_in unchanged.)

WHAT THE RESIDUE MUST BE INSTEAD. The port draws caseworkGen at step 8 for 3-6 groups;
Unity draws none. Not fewer -- none. Since the draw counts match through step 7, Unity is
not evaluating those groups at all by step 8, which means Unity's live group SET is smaller
than the port's. The best candidate is the sixth game bug Fable found, which has the right
shape and is already documented: casework-site deliveries DOUBLE-PROCESS.
`HandlePopulationDelivery` calls `RemoveClientsByQuantity` at unload
(ClientStayTracker.cs:515) and `DeliverySystem.OnVehicleDeliveryCompleted` calls it AGAIN at
complete (DeliverySystem.cs:701). Removing twice drains groups to empty, and an empty group
has `clientsWithCaseworkNeed == 0` and stops drawing forever. The port removes once, so its
groups survive and keep drawing -- which is exactly the observed asymmetry, in the right
direction, from a mechanism already confirmed in the source.

Test that next, on top of the reorder: double the removal in `process_home` the same way the
arrival was doubled. It is the same bug class as the double-spawn, on the same two call
sites, and it is cheap. Note it interacts with the PARKED conflict below, since both concern
who removes population from a group.

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
