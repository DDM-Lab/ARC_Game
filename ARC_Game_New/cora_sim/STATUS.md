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

## Client spawn on population arrival: MODELLED, but fired differently than Unity

Correction to a claim made an hour earlier in this session: the port does NOT lack client
spawn. `clients.py` models the whole subsystem -- caseworkNeed once per PERSON, one
stayDuration per group, caseworkGen per undecided group per round, the shrink-on-departure
in TriggerNonCaseworkDeparture, and the whole-group crediting of caseworkRequested. The
gap is in HOW IT IS FIRED, not whether it exists. What follows replaces that claim.

Measured on seed 5901:

    draw:Client.caseworkNeed    800  in 8 bursts of EXACTLY 100
    draw:Client.stayDuration      8  exactly 1 per burst
    draw:Client.caseworkGen      10  in 2 bursts (2 at s6d2r2f319, 8 at s7d2r3f362)

There are exactly 4 Population unloads in the episode, each nominal=100 actual=100. The 8
burst frames are EXACTLY the union of the 4 Population `delivery:unload` frames and the 4
Population `delivery:complete` frames -- verified by set diff, not eyeballed. So Unity runs
the spawn path TWICE per population delivery, once at each, drawing 202 where the port
draws 101.

Two candidate divergences, neither yet settled, and they are independent:

  1. COUNT. Unity bursts twice per delivery; `register_arrival` is called once. Note the
     port's lodging spend counters are exact on every trace, so the port's single spawn
     already yields the RIGHT resident population -- which argues Unity's second burst
     draws without creating a second group (a re-init, or a discarded path) rather than
     doubling the population. If it creates nothing, it is still a stream-position
     difference of 101 draws per delivery and must be reproduced.
  2. TIMING. The port deliberately defers: "a delivery that landed at the end of last round
     becomes a client arrival at the start of this one" (sim.py, step_round docstring).
     Unity spawns at the delivery instant, inside the round. On 5901 Unity credits
     caseworkRequested at s6d2r2f319 while the port, offset by a round, has not yet
     registered the arrival -- which is a plausible cause of `caseworkRequested port=0`
     in five of eleven traces.

The draw census does not currently discriminate: its 135th fixture is exactly the
client-draw round it does not cover. Fixing the census gap and this divergence are the same
task, and the census reaching 135/135 is the test that the draw ORDER is right.

Both candidates are being read out of the C# now. Do not edit clients.py or sim.py until
that read lands -- the count question in particular has two opposite fixes depending on
whether Unity's second burst creates a group.

## Method notes that cost time

- Rank changes by **exact-trace count and first-divergence depth**, never by summed
  diverging counters — that scalar penalises depth and once hid two traces going exact.
- A failed pre-check needs its **timing** checked before it counts as a refutation; one
  sampled the wrong instant and nearly cost the session's largest fix.
- When a rate looks wrong, measure the **smallest interval**, not the span.
