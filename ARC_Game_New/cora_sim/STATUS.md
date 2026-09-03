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

## The generation-ordering hypothesis: REFUTED (both forms)

`gen:pass` settles where Unity generates. Seed 5901, round 5:

    f310  unload  Food Kitchen_0->Community01
    f312  unload  Popu Community01->Motel      first relocation resolves
    f319  GENERATION PASS (activeTasks=7)      after the deliveries, same round
    f327  queue   Popu Community01->Motel      the re-request

That frame ordering is real and reproducible. The INFERENCE drawn from it -- that the port
should therefore generate after its delivery tick -- is wrong. Both forms were built and
measured, and both take the exact-trace count 2 -> 0:

  form 1  move the whole creation block (rollover + segment)      2 -> 0
  form 2  move ONLY the segment pass, rollover left in place      2 -> 0   <- the "scoped edit"

Form 2 was the one this file previously recommended. It is refuted. The draws stayed at
their original point in both forms (only task CONSTRUCTION moved), so this is not an RNG
re-ordering artifact.

What the failure says, read by direction rather than by count: with generation moved late,
the port resolves MORE than Unity, not fewer --

    staff_5802  lodgingResolved  unity=300  port=400     (was exact)
    staff_5504  foodResolved     unity=3    port=4       (new)

So the freed slot admits extra tasks that Unity does not generate. Unity's pass runs on a
later FRAME but does not behave as though the facility slot were free. The 5901 re-request
at f327 is therefore probably not gated on a facility slot at all -- it is a community
re-requesting, which plausibly keys off the community's own population/occupancy rather
than the destination's slot. Those are two different admission gates and this session
conflated them.

The next hypothesis to test is consequently about WHICH gate, not about WHERE the pass sits:
instrument what Unity's generator actually reads when it admits the f327 re-request
(community occupancy vs facility slot occupancy) before moving any port code again. Leave
sim.py's ordering alone until that mark exists.

## Method notes that cost time

- Rank changes by **exact-trace count and first-divergence depth**, never by summed
  diverging counters — that scalar penalises depth and once hid two traces going exact.
- A failed pre-check needs its **timing** checked before it counts as a refutation; one
  sampled the wrong instant and nearly cost the session's largest fix.
- When a rate looks wrong, measure the **smallest interval**, not the span.
