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

## The real missing mechanism: CLIENT SPAWN ON POPULATION ARRIVAL

Population deliveries spawn clients. The port models none of this. Measured on seed 5901:

    draw:Client.caseworkNeed    800  in 8 bursts of EXACTLY 100
    draw:Client.stayDuration      8  exactly 1 per burst
    draw:Client.caseworkGen      10  in 2 bursts (2 at s6d2r2f319, 8 at s7d2r3f362)

There are exactly 4 Population unloads in the episode, each nominal=100 actual=100. The 8
burst frames are EXACTLY the union of the 4 Population `delivery:unload` frames and the 4
Population `delivery:complete` frames -- verified by set diff, exact match, not eyeballed.
So the spawn path runs TWICE per population delivery, once at unload and once at complete.
Whether that second run is an unintended double-spawn is being read out of the C# now; it
is a candidate fifth game bug, not yet confirmed.

This unifies two open items that were being tracked separately:

  - `caseworkRequested unity=100/300 port=0` in five of eleven traces. That is ABSENCE, not
    timing skew -- the port never creates casework because it never spawns clients.
  - the 135th draw-census fixture, previously described as "client-stay draws not in the
    round loop". Those are these stayDuration draws. Same mechanism.

Next: implement client spawn-on-arrival once the C# read settles the per-client draw order,
the stayDuration scope (group-level or per-client -- the marks say group), the caseworkGen
emission condition, and what per-client state persists across rounds. Guard with the
exact-trace ratchet (floor 2) and re-run the draw census, which should reach 135/135 if the
draw ORDER is right.

## Method notes that cost time

- Rank changes by **exact-trace count and first-divergence depth**, never by summed
  diverging counters — that scalar penalises depth and once hid two traces going exact.
- A failed pre-check needs its **timing** checked before it counts as a refutation; one
  sampled the wrong instant and nearly cost the session's largest fix.
- When a rate looks wrong, measure the **smallest interval**, not the span.
