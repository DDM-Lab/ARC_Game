# CORA bug reports — `v1_testing`

Pinned to commit `868c192e` (branch `v1_testing` = `feature/game-state-snapshot` + `origin/main-bugfixes`).
Every `file:line` below is on that commit. Evidence paths are headless captures under
`ARC_Game_New/cora_sim/runs/` (`validate_v1/staff_<seed>.log` = merged build driven by evolved plans,
`validate_v1_replay/` = merged build replaying the pre-merge actions). Where no capture exercises a
path, the example is constructed from the code and says so.

Goal: `v1_testing` is to be debugged until every behaviour is intended, so one build serves human
play-testing, RL training and frontier-model benchmarking. The surrogate (`cora_sim/`) currently
reproduces the game *as shipped*, bugs included; each fix below therefore also changes the surrogate
and needs a recapture (see `cora_sim/UNITY_BUGS.md` for the surrogate-side index).

Each entry: **Where** · **What happens** (with an example) · **Intended** · **Proposed patch** ·
**Scope / status**. Status values: CONFIRMED (every branch read, or seen in a capture), PLAUSIBLE
(read in code, needs a runtime check), NEEDS DECISION (the fix is a design choice).

**Triage for the review meeting (highest impact first).**

1. **A1** — delivered food is wasted at the day rollover before anyone eats it, in every capture; the
   food mechanic does not function. Fix is a timing decision (Part C.1).
2. **A2 / A3 / A7 / B25** — client tracking double-counts every relocation and drains the wrong group;
   casework demand, processing and departures are all off by roughly 2x.
3. **B1 / B2 / B3 / B4** — cargo destroyed on partial unload, cancellation credited as success,
   stranded food erased, aborted deliveries left reserving stock forever.
4. **B11 / B12 / B21 / B22 (+ A6)** — the agent path validates less, has no budget gate, charges what the
   client says and ignores staffing rules; RL and benchmark runs are not playing the humans' game.
5. **A9 / B6 / B7** — emergencies are effectively off (cap 0 headless; at most one global emergency
   anywhere; facility emergencies uncounted).
6. **B35** — sheet parameters that do nothing (weather, resident count, food capacities, flood knobs).

Part C lists the behaviours that need a design ruling before the fixes above are ordered.

Part A = the 12 bugs the surrogate work had already found. Part B = new findings from the 2026-09-09
audit (six subsystem reviews, each claim re-read by hand before inclusion). Part C = intended-but-
questionable behaviours for the design discussion. Part D = claims that did not survive verification.

---

## Fix status (branch `v1_fixes`, 2026-09-09)

All fixes live on `v1_fixes` (from `v1_testing`, not merged, not pushed) in seven commits, each of
which compiled as a headless build: `d5441454` deliveries, `21d05150` tasks + clients, `16d1a106`
choice execution, `d17eb70f` workers + clock + config + report, `fee6733d` action menu prices, and a
seventh batch (R1, R2 below) found by replaying the fixes.
Verification: the recorded actions of seeds 5503, 5504 and 5801 (the last two carry flood stops,
road blockages and delivery failures) replayed through the fixed build ran all 32 rounds with
**zero exceptions** (the old build threw NullReferenceExceptions on each), every day delivers
segment events 1-4, database generation passes fall on exactly the old schedule (day 1: rounds 1-2;
later days: start of day, rounds 1-2), Daily Budget Allocation fires 7 times and both offboarding
alerts fire as before, and the same shelters that logged `consumed 0/N` every day now log
`consumed 100/400`, `100/300`, `100/200`. Trajectories are not comparable with the old captures
beyond day 1 (the RNG stream now includes emergency tasks and the daily weather differs), so
budget/score totals of old and new runs must not be read against each other. The surrogate has NOT
been changed and now diverges from the fixed game by design (first at round 5); re-deriving it is
the next job.

## Merge of `origin/main-bugfixes` d5e5f683 (branch `v1_merge_test`, 2026-09-10)

Two colleague commits (5d922203 self-walk relocation, d5e5f683 food overhaul) were merged on top of
`v1_fixes` in a test branch. Eleven files conflicted; resolution policy: their new mechanics win
(no-vehicle relocation, daily kitchen fill, motel food, community depletion events, overnight food
cancellation, priced fast delivery on road blockage), our fixes win wherever both touched the same
line (loader source chain and consumer-side row application, null-safe blockage facility, C.3 refusal
of covered requests, unified validation/budget gate). Scenes: MainScene = theirs + our five component
edits (WebSocketManager active/URLs, DebugUI URL, allowNegativeBudget, loader URLs); TutorialScene =
ours minus the deleted `shelterFoodReq` field.

Defects in the incoming commits, fixed during the merge (their branch still has them):
- `CommunityFoodDepletionManager` was never placed in a scene, so community depletion (the ONLY
  source of community food requests now) never ran. Added to MainScene's TaskSystem object.
- The scene copies of the three communities had `enableFoodWaste: 1`, so their 400 meals were wasted
  at the first rollover and no depletion could ever fire (their prefab and commit message say never
  waste). Set to 0.
- The scene copy of the Motel storage had no FoodPacks capacity and consumption off, so the new
  Motel food-request tasks could not deliver anywhere. Given their MotelPrefab values (6000, on).
- The manager read `initialFoodDemandFrequency` from the loader at Start, before the sheet loads.
  It now waits for GameDataManager, and its draw carries a `draw:CommunityFoodDepletion` mark.
- `TaskDetailUI.ExecuteFallbackDelivery` did not return on the new self-walk branch (compile error).
- Task titles/descriptions/choice texts now use placeholders (`[facility_name_plain]`,
  `[food_amount]`, `[relocation_rounds]`); they were resolved only in the UI, so agents (gym and
  router payloads) received the raw templates. Resolved in `TaskSystem.GetTaskContext` and
  `WebSocketManager` too.
- Self-walk departures/arrivals now go through `ClientStayTracker.HandleSelfWalk{Departure,Arrival}`
  (casework credited at departure, only lodging destinations register groups) instead of the raw
  `RemoveClientsByQuantity`/`RegisterClientArrival` calls, which would have registered client
  groups at casework sites; marks `relocation:queue` / `relocation:arrive` added.

Consequences to note: kitchens no longer produce per round, so `initialKitchenCapacity` lost its
consumer and was removed from the sheet copy (the daily report uses `initialKitchenFoodCapacity`);
`initialShelterFoodCapacity` follows their prefab (200); shelters consume every 2 rounds and workers
no longer eat there (their prefab); the community/motel consumption rulings of 2026-09-09 are
superseded by their design (communities lose food to events, the motel eats). Verified: 5503, 5504,
5801 replayed on the merged build, 32 rounds each, zero exceptions; depletion events, community and
motel requests, self-walk arrivals, overnight cancellations all observed. Recorded actions no longer
match the changed choice sets, so these captures are smoke tests, not parity evidence.

### Verification of the colleague's mechanism note (2026-09-10, branch `v1_merge_test`)

Checked on the merged headless build with scripted probes in `cora_sim/probes/` (`probe_followup.py`;
`probe_covered_motel.py` / `probe_covered_shelter.py`: fresh game, one kitchen (+ one shelter), 100 people
walked to the motel / shelter, the first request answered with the "double" choice) and the three smoke replays.

| claim in the note | status | evidence / note |
|---|---|---|
| Kitchens produce once at day start, to capacity | HOLDS | `Kitchen_0 wasted N` then `stocked to full capacity: +200` at every rollover; a kitchen that is not operational at rollover stays empty until the next one |
| No overnight carry-over for kitchens, shelters, motel; in-transit food is waste | HOLDS | rollover waste lines; end-of-round-4 cancellation returns cargo to the kitchen, which then wastes it (counted once) |
| Communities stay full except depletion | HOLDS after the scene fix (`enableFoodWaste` 0) | depletion events observed; nothing else refills a community, so a community whose replacement delivery fails stays short |
| Shelter and motel: two requests per day, rounds 1 and 3 | HOLDS | `Motel_FoodRequest_First` at every day's round-1 pass, `_Second` at the round-3 pass |
| First request: fulfil current or both; follow-up skipped if covered | HOLDS (motel and shelter) after the routing fix below | motel: double queued 200 for 100 people, the follow-up did not spawn that day (`_Second` needs `FoodPacks Empty` AND `NeedsFood`); shelter probe day 3: same, and the second 100 was eaten by the periodic tick two rounds later. "Covered" means stock on hand: shelter probe day 4, the double was still in transit at the round-3 pass, so the follow-up spawned anyway; the agent's attempt to fill it was refused "200 meals already inbound — need is covered" (C.3), and after arrival "No food is currently needed". No double-payment, but an extra task the agent has to decline. |
| Clients arriving during the day generate demand the next day | PARTLY | no explicit mechanism; quantities are computed from the live population at each request, so arrivals before round 3 are in the follow-up, arrivals after it wait for the next day |
| Food is consumed immediately on delivery, population-based | HOLDS, with a leftover | `fed 100 people immediately after delivery`; BUT the old periodic cycle still runs (shelters every 2 rounds, motel every 4). It records `FOOD SHORTAGE` when the stock is empty and, when its tick falls in the same round as a delivery, the once-per-round guard skips the on-delivery consumption (motel probe day 4: tick at round 1 logged `Need 22, only had 0`, the 44 meals delivered that round were never eaten). At the shelter the periodic tick also eats the second half of a "double" (probe day 3: 100 on delivery + 100 two rounds later = 200 meals for 100 people in one day), at the motel the second half sits until the overnight waste. NEEDS RULING: keep the periodic cycle (then define what a shelter/motel "meal" is) or remove it for shelters and motel. |
| Community requests: day 2+, rounds 1-3, probability = initialFoodDemandFrequency, 100 packs, exact replacement | HOLDS after the manager fixes | 63 draws per game = 3 communities x 3 passes x 7 days; request quantity overridden to the amount lost |
| Deliveries still queued/in transit at end of round 4 are cancelled and fail | HOLDS after B37/B38 | `Cancelled incomplete food delivery ... at end of day`; the parent task takes the normal 15-point delivery-failure satisfaction penalty (the note does not mention a penalty). On the agent path the check fired at the end of EVERY round of days 2-8 (B37: the end-of-day flag was only ever cleared by the human "End Today" button), and it also cancelled deliveries whose cargo had already landed (B38). 5504 replay: 15 cancellations / 16 penalties before, 7 / 10 after, all at "Day N complete". |
| Relocation without vehicles, immediate departure, arrival after 2 rounds, delay configurable | HOLDS | `relocation:queue` at dXrN, `relocation:arrive` two round-ends later; zero Population vehicle deliveries in any capture; `relocationDelayRounds` on the TaskSystem object |
| Balance (not a claim): one kitchen = 200 meals/day vs. a motel of 300+ eating twice a day | — | most shelter/motel requests in the replays were refused for lack of unreserved stock; expect many kitchens or a bigger sheet capacity |

Defects found by the probes and fixed in this pass (`v1_merge_test`):
- Their shelter/motel food choices carry `enableMultipleDeliveries`, and our batch-3 routing sent every such choice to the generic multi-delivery path, which does not understand population-based quantities: every "Fulfil [food_amount]" / "double" choice was refused ("No resources available at any of the 1 sources") in the gym and the UI alike. Cargo now decides the path (food -> FoodDeliveryHandler, people -> ClientRelocationHandler) as in their code; the multi flag only routes other cargo.
- Self-walk arrivals never set `deliveredQuantity`, so every relocation resolved with 0 people housed for the lodging metric (RL reward). Fixed in `FinalizeRelocation`.
- Agent payloads: population-based choices reported `deliveryQuantity 0`; now the resolved need (what the button says). `logistics.pendingRelocations` added (task, source, destination, quantity, roundsRemaining) and rendered by `obs_encoder` as a `walking:` line.
- `ActionExecutor` population transfers (`<transfer>` grammar, manual_transfers mode) created vehicle deliveries; they now walk via the relocation handler, linked to the source's open lodging task when one exists.
- B37 (headless/gym only): `GlobalClock.isWaitingForReport` is set at the end of round 4 and was cleared only by the "End Today" confirm button. `GymAdvanceRound` → `ProceedToNextDay` never cleared it, so from day 2 on `TaskSystem.OnSimulationEndedCheckDayComplete` treated every round end as end-of-day and cancelled every in-flight food delivery with a 15-point penalty. Cleared in `ProceedToNextDay`.
- B38: `Vehicle.UnloadCargo` lands the cargo and sets `deliveredQuantity` before its unload wait ends; a round ending inside that window left the delivery in the active list and the overnight cancel failed the parent task for food that was on the shelf (shelter probe day 3, `Vehicle2`). Deliveries with `deliveredQuantity > 0` are no longer cancelled.
- Walk path capacity (B13 reopened by the cargo routing): `ClientRelocationHandler.ExecuteToSpecificDestination` capped only by the source population, so a walk to a full destination departed, bounced on arrival and returned people the stay tracker had already discharged. Now capped by the destination's effective space (capacity minus reserved vehicle inbound minus walkers already en route), refused at zero (`relocation:refused` mark). No bounce or refusal occurred in the three smoke replays; code-verified.
- Data typo (their side): `Shelter_FoodRequest_First.asset` writes the placeholder as `\u3010food_amount]` (full-width left bracket), so the shelter's first food request reached agents and players as "has requested 【food_amount] meals". Corrected to `[food_amount]`.
- Still leaking to HUMANS (their side, not fixed here): `GameTask.taskTitle` itself keeps the raw template, so the game log and the satisfaction history read `Delivery Failure Penalty from [[facility_name_plain] Food Request]`. Agent payloads are clean (`WebSocketManager`/`GetTaskContext` resolve); fixing it for humans means resolving at task creation, which is a their-side call.

Still stale for agents (config files, not changed here): the officer prompts state "consumes 1 food/person every 4 rounds", "Food: produced by kitchens, distributed via vehicles", "Vehicles: transfer resources between buildings", "Resource transfers require available vehicles", and the domain config's "moving resources (food packs, population) between facilities with available vehicles" / "a kitchen needs staff to produce food packs". Replace with: kitchens are stocked to capacity each morning; food is consumed on delivery; people walk (2 rounds); vehicles carry food only; food does not keep overnight. The surrogate (`cora_sim`) still models the old game.

Regressions found and fixed while verifying:

| entry | status |
|---|---|
| R1 | FIXED (batch 7) — the A1 change removed the rollover's segment-0 event, and three database tasks trigger on `Round == 0` (`Budget_Allocation`, `Offboarding_alert_day6/7`: `RoundTrigger.CheckCondition` compares `GetCurrentTimeSegment()`), so the +$5,000 Daily Budget Allocation and the offboarding alerts never appeared on the first fixed build (5503 replay: 0 of 7). Fix: `GlobalClock.OnDayStarted` fires right after all `OnDayChanged` handlers (segment 0, the old position); `TaskSystem.OnDayStarted` runs the generation pass there, and `OnRoundChanged` generates only for `segment < roundsPerDay - 1` so the end-of-day tick (4) does not add a fourth pass. Passes per day are back to the old three. |
| R2 | FIXED (batch 7) — pre-existing, not a regression: at application quit `Building.OnDestroy → DeliverySystem.CancelAllDeliveriesInvolving → CancelDeliveryTask` dereferences `GameLogPanel.Instance` after the panel is gone (the NullReferenceException at the end of every old 5504/5801 log). Null-guarded. |

| entry | status |
|---|---|
| A1 | FIXED `d17eb70f` — the last round's tick fires in `AdvanceTimeSegment` before the report; rollover no longer raises a segment event. Timing decision taken as in "Intended". |
| A2, A3 | FIXED `d5441454` — nominal-quantity registration block removed. |
| A4 | FIXED `d5441454` — failure records the demand; external stop is a cancel, not a completion. |
| A5 | FIXED `21d05150` — superseded task closed properly (`SupersedeTask`), demand carried by the emergency task. |
| A6 | FIXED `d17eb70f` + `fee6733d` — `ActionExecutor` prices builds and workers from the game's fields; the Python action menu reads the live prices. Price = the scene values (build 2000; workers 200 / 1000 / 300 — see note below). |
| A7 | FIXED `21d05150`. |
| A8 | FIXED `d5441454` — coroutine handle stopped on reassignment; abort unwound on the outer run. |
| A9 | FIXED `d17eb70f` — both defects (SetDefaults + loader lookup in Awake). Headless now reads the loader's fallbacks instead of `default*`. |
| A10 | FIXED `21d05150` — departures leave the building's storage. |
| A11 | FIXED `21d05150` — blockage tasks name their facility; fast-food choice delivers. |
| A12 | FIXED `d17eb70f` (layout + mesh pass on creation) — **unverified in the editor**. |
| B1-B4 | FIXED `d5441454`. |
| B5 | FIXED (batch 8) — `Vehicle.RepairVehicle` tows a vehicle that is still in the flood to the nearest dry road tile (`vehicle:towed` mark). Code-verified only: no replay repairs a vehicle. |
| B6-B10 | FIXED `21d05150`. |
| B11-B15 | FIXED `16d1a106`. Part C.3 resolved as "refuse" (inbound-covered request is rejected on every path). |
| B16 | FIXED `16d1a106` as validation: headless confirms are validated against the task's input data; they are NOT refused for input tasks (deciding that would block officer flows). |
| B17-B24 | FIXED `d17eb70f`. B22: an agent's "assign N workers" must hit the building's exact workforce like the human panel and honours the lock. |
| B25 | FIXED `21d05150`. |
| B26 | RULED (2026-09-09): communities keep consuming, the motel does not. `initialCommunityResidentCount` now sets the community population (batch 8); the sheet copy carries 400 so nothing changes until the Google Sheet is updated. |
| B27, B28 | FIXED `d17eb70f`. |
| B29-B34 | FIXED `d17eb70f`. B31 made deterministic (advisory takes the smaller half); the count-vs-interval semantics is still Part C. |
| B35 | FIXED (batch 8) — every sheet row now drives the game and the headless build reads the sheet too; see the row table under B35 and the `parameters in effect` log line. |
| C.2, C.4, C.5, C.6, C.7, C.8, C.9 | unchanged, pending the design discussion. |

**Price note (found while fixing A6/B21).** The scene charges humans 200 per untrained hire,
1000 per trained hire and 300 per training (`MainScene.unity:66151-66173`), while the agent client
claimed and was charged 100 / 300 / 500. Agents now pay what humans pay. Every RL/benchmark result
produced before `v1_fixes` was played at the cheaper prices.


## Part A — previously known bugs, re-derived on `868c192e`

### A1. Delivered food is wasted at the day rollover *before* the day's consumption tick — nobody eats it

**Where.** `GlobalClock.cs:702-720` (`AdvanceTimeSegment`), `GlobalClock.cs:769-771`
(`ProceedToNextDay`), `Delivery/BuildingResourceStorage.cs:88-89` (subscriptions),
`:130-137` (`OnRoundChanged`), `:282-298` (`HandleDailyReset`), `:213-236`
(`HandlePopulationConsumptionCycle`, `consumptionRoundInterval = 4`).

**What happens.** `AdvanceTimeSegment` returns early when the segment reaches `roundsPerDay`, so the
event for round 4 is never raised there. `ProceedToNextDay` raises `OnDayChanged` first and *then*
`OnTimeSegmentChanged(0)`. Every storage subscribes to both: `OnDayChanged` → `HandleDailyReset` wastes
**all** stored food packs; `OnTimeSegmentChanged(0)` → production + the consumption cycle. Because a day
delivers exactly four segment events (1, 2, 3, then 0 at rollover) and consumption fires every four
events, every building that existed at game start consumes on the rollover event — one instant after
its stock was emptied. Food delivered during the day is never eaten.

Example, `validate_v1/staff_5503.log`:

| line | event |
|---|---|
| 7925 | `Shelter_0 received 100 FoodPacks (100/100)` |
| 8304 | `Shelter_0 wasted 100 unused meals at end of day` |
| 8418 | `Shelter_0 fed 86 people after 4 rounds, consumed 0/86 meals` |

The same pattern repeats every day of every capture (`consumed 0/N` on lines 825, 3001, 8418, 11071,
14505, 15289 for Shelter_0 alone; communities likewise). Food deliveries therefore only ever move
budget and satisfaction, never nutrition, and every daily report shows a food shortage regardless of
play. A building constructed mid-day has a different phase and *can* eat, which makes the effect
plan-dependent and hard to see.

Two further consequences of the missing round-4 event: the last day's fourth round never runs its
tick at all (no consumption, no production, no `roundsRemaining--`, no task generation), and every
segment-keyed subscriber sees the sequence 1, 2, 3, 0 instead of 1, 2, 3, 4 (`TaskSystem.cs:798-804`
skips generation on `newSegment == 3`, i.e. after round 3, while the round-4 slot runs at day start).

**Intended.** Round 4's tick belongs to round 4: consume/produce/age/expire, then the daily report,
then the day rollover wastes what is left.

**Proposed patch.** In `AdvanceTimeSegment`, raise `OnTimeSegmentChanged(currentTimeSegment)` (value 4)
*before* the early return, and remove the `OnTimeSegmentChanged?.Invoke(currentTimeSegment)` call from
`ProceedToNextDay` (keep `OnDayChanged`). Then audit the subscribers for the new numbering:
`BuildingResourceStorage.OnRoundChanged` guards `newRound <= 4` (already fine),
`TaskSystem.OnRoundChanged` `newSegment != 3` becomes whichever round is meant to be skipped,
`ClientStayTracker.OnRoundChanged` derives `currentRound` from the clock (fine), `FloodSystem` /
`WeatherReportSystem` need a read. **NEEDS DECISION**: this changes the difficulty balance (a fourth
consumption/generation tick per day at a different time), so it must be agreed before the surrogate
is re-derived. Alternative minimal fix if the timing is to stay: in `HandleDailyReset`, run the
pending consumption before wasting.

**Scope / status.** Headless and WebGL alike. CONFIRMED. Surrogate: `sim.py` rollover passes and
`tick_epilogue` must change; full recapture.

### A2. Population deliveries are registered twice (two client groups per delivery)

**Where.** `Delivery/Vehicle.cs:604-614` (`UnloadCargo` → `ClientStayTracker.HandlePopulationDelivery`
with `actualDelivered`) and `Delivery/DeliverySystem.cs:691-706` (`OnVehicleDeliveryCompleted`, the
older branch: `RegisterClientArrival(dest, completedTask.quantity)` + `RemoveClientsByQuantity(source,
quantity)`).

**What happens.** Both run for every population delivery. `validate_v1/staff_5503.log`:

| line | event |
|---|---|
| 1677 | `Registered 100 clients at Shelter_0 (Group: Relocate_2_Community02_to_Shelter_0, Round: 5)` |
| 1683 | `DeliverySystem: Task 2 completed by Vehicle1` |
| 1785 | `Registered 100 clients at Shelter_0 (Group: VehicleDeliv_2, Round: 5)` |

One vehicle load of 100 becomes 200 tracked clients: two casework-request tasks, doubled casework
demand in the reward metrics, doubled "clients without casework need" departures (lines 3054, 8471),
while the shelter's physical population is 100.

**Intended.** One tracked group per delivery, sized by what was actually unloaded.

**Proposed patch.** Delete the `Population` block in `DeliverySystem.OnVehicleDeliveryCompleted`
(`DeliverySystem.cs:691-706`); `Vehicle.UnloadCargo` already does the registration with the real
count, and the casework-site removal (A3) with it.

**Scope / status.** Both builds. CONFIRMED. Surrogate: `tasks.py` / client tracker port.

### A3. Casework-site deliveries remove clients from the source twice

**Where.** Same two call sites as A2. `HandlePopulationDelivery` (`Map/ClientStayTracker.cs:514-527`)
calls `RemoveClientsByQuantity(source, actual)` for a casework-site destination; the legacy branch calls
`RemoveClientsByQuantity(source, nominal)` again, and each call credits `RecordCaseworkProcessed`.

**What happens.** `validate_v1/staff_5503.log` lines 7095-7108: `CaseworkSite_14 received 24
Population`, `Partially removed 24 clients from group Relocate_3_Community01_to_Motel`, then after
`Task 6 completed`, `Partially removed 24 clients from group Relocate_3_Community01_to_Motel` again.
48 people are removed from the tracker and `caseworkProcessed` is credited 48 for 24 processed.

**Intended.** Remove and credit once, by the delivered count.

**Proposed patch.** Same deletion as A2.

**Scope / status.** Both builds. CONFIRMED.

### A4. A flood-stopped delivery closes its task without counting the demand, then the stranded cargo is credited as a late delivery

**Where.** `Delivery/Vehicle.cs:398-425` (`TriggerRoadBlockageTask` → `TaskSystem.HandleDeliveryFailure`),
`Tasks/TaskSystem.cs:759-781` (`HandleDeliveryFailure`: status → Incomplete, satisfaction penalty,
`OnTaskCompleted`, **no** `RecordTaskResolution`), `Delivery/Vehicle.cs:389` →
`DeliverySystem.RemoveActiveDeliveryTask` (`DeliverySystem.cs:516-526`, which raises the *delivery*
`OnTaskCompleted`), `Tasks/TaskSystem.cs:675-712` (`OnDeliveryTaskCompleted`: parent found in
`completedTasks` → `AddLateDelivery`).

**What happens.** Flood stops a vehicle: the parent task is marked Incomplete and penalised, but the
food/lodging demand is never added to `*Resolved`. The vehicle's delivery record is then removed via
`RemoveActiveDeliveryTask`, which fires the same event a real completion fires, so the parent (already
in `completedTasks`) is credited a late delivery for cargo that never arrived. Example,
`validate_v1/staff_5504.log` line 23679 (`Delivery Failure Penalty from [Food Request From Community]`)
and line 23689 (`Road blockage task created for Vehicle1 (en route to pick-up)`); the surrogate had to
add a "stranded cargo credited late" rule to match the counters on 5503.

**Intended.** A failed delivery counts as an unfulfilled demand; nothing is credited unless cargo lands.

**Proposed patch.** In `HandleDeliveryFailure`, call `RewardMetricsTracker.RecordTaskResolution(task,
fulfilled:false)` before `OnTaskCompleted`. In `RemoveActiveDeliveryTask`, raise a separate
`OnTaskCancelled` event (or none) instead of `OnTaskCompleted`, and have `TaskSystem.OnDeliveryTaskCompleted`
only credit when the delivery is in `completedTasks`.

**Scope / status.** Both builds. CONFIRMED.

### A5. An emergency lodging task silently discards the ordinary lodging task it supersedes

**Where.** `Tasks/TaskSystem.cs:911-935` (per-facility lodging dedup in `GenerateTasksFromDatabase`).

**What happens.** When an Emergency lodging task arrives for a facility that already has a normal
lodging task, the older task is `activeTasks.Remove`d with no status change, no
`RecordTaskResolution`, no `OnTaskExpired`/`OnTaskCompleted`, and it is not added to `completedTasks`.
Its demand vanishes from the reward metrics, any linked in-flight delivery loses its parent (the late
credit in A4 then finds nothing), and the UI list simply drops it. No capture exercises this path
(headless never creates database emergencies, see A9); example constructed from the code.

**Intended.** Either carry the old task's demand into the emergency task or resolve it as unfulfilled.

**Proposed patch.** Replace the bare `activeTasks.Remove(stale)` with `ExpireTask(stale)` (records the
resolution and raises the event) or transfer `stale.demandQuantity` to the new task.

**Scope / status.** WebGL (headless cannot reach it until A9 is fixed). CONFIRMED by code.

### A6. Construction is advertised at $1000 to agents and charged at $2000

**Where.** `Map/BuildingSystem.cs:25-27` (code defaults 1000), `Assets/Scenes/MainScene.unity:33578-33580`
(`shelterConstructionCost: 2000`, `kitchenConstructionCost: 2000`, `caseworkSiteConstructionCost: 2000`
— the serialized scene value wins), `BuildingSystem.cs:201-229` (deducts the per-type field),
`Actions/ActionExecutor.cs:143` (logs the agent-supplied `action.cost`), `cora_sim/actions.py:43`
(`ADVERTISED["build"] = 1000`, the action menu shown to RL/LLM agents),
`GymServerManager.cs:1138-1149` (game_state exports the live 2000 values).

**What happens.** `validate_v1/staff_5503.log` lines 1477-1490: `Recorded budget change: -2000 -
Construction Cost for CaseworkSite at AbandonedSite_14` beside `Built CaseworkSite at site 14 (cost:
$1000)`. Human players see the scene value in `UI/BuildingSelectionUI.cs:220-225` (2000) and pay 2000;
agents are told 1000 and pay 2000, so their budget reasoning is wrong by $1000 per build.

**Intended.** One price, read from one place.

**Proposed patch.** NEEDS DECISION on the price (the parameter sheet has no construction-cost row).
Whichever it is, the action menu must read `game_state.construction.<type>Cost` instead of a constant,
and `ActionExecutor` should ignore the agent-supplied `cost` for builds (see B-W7).

**Scope / status.** Agent paths (headless, router). CONFIRMED.

### A7. `caseworkRequested` credits the whole client group, not the people who need casework

**Where.** `Map/ClientStayTracker.cs:539` (`RecordCaseworkRequested(group.clientCount)`) vs `:558`
(`caseworkClientCount = group.clientsWithCaseworkNeed`, the quantity the task actually asks to move).

**What happens.** A group of 100 with 22 needing casework adds 100 to the demand counter; processing
all 22 then scores 22/100. Combined with A2 the same 100 people are counted twice more.

**Intended.** Demand = people flagged as needing casework.

**Proposed patch.** `RecordCaseworkRequested(group.clientsWithCaseworkNeed)`.

**Scope / status.** Both builds. CONFIRMED.

### A8. After an empty-source abort, two delivery coroutines drive one vehicle

**Where.** `Delivery/Vehicle.cs:556-572` (`LoadCargo` aborts: `currentTask = null`, `SetStatus(Idle)`
from inside the nested coroutine), `Vehicle.cs:195-205` (`ExecuteDeliveryTask` resumes after the nested
coroutine and re-reads `currentTask`), `Vehicle.cs:141-171` (`AssignDeliveryTask` starts a new
`ExecuteDeliveryTask` without stopping the old one), `DeliverySystem.cs:309-317` (`Update` assigns
pending tasks to any Idle vehicle on a timer), `Vehicle.cs:313-356` (shared `currentPathIndex`).

**What happens.** The vehicle is set Idle while the old outer coroutine is still suspended. If
`DeliverySystem.Update` assigns a new task before that coroutine resumes, `currentTask` is non-null
again when it does, so the old coroutine skips to "move to destination" while the new one starts "move
to source". Both advance the same `currentPathIndex`: the source leg is skipped, the destination leg
runs at two cells per frame, and the cargo is whatever the old coroutine happened to load. Observed on
capture 7002 (surrogate port note: "orphaned trips after empty-source aborts"); the
`delivery:leg` marks in the headless log show `idx` advancing by two.

**Intended.** One coroutine per vehicle; an abort returns the vehicle to Idle only after the run has
fully unwound.

**Proposed patch.** Keep the coroutine handle (`deliveryCoroutine = StartCoroutine(...)`) and
`StopCoroutine` it in `AssignDeliveryTask`, `CancelCurrentTask` and `StopVehicleDueToFlood`; in the
abort path set a flag and let `ExecuteDeliveryTask` perform the reset after `LoadCargo` returns; also
call `DeliverySystem.RemoveActiveDeliveryTask` there (see B-D4).

**Scope / status.** Both builds. CONFIRMED.

### A9. Headless runs with an Emergency task cap of 0 — no database emergency ever appears

**Where.** `GameDataManager.cs:249-250` (the last two lines of `SetDefaults()`, which starts at `:218`):
`InitialExternalRelationFrequency = defaultExternalRelationFrequency;` then
`InitialExternalRelationFrequency = defaultEmergencyTaskFrequency;` — `InitialEmergencyTaskFrequency`
is never assigned. `Assets/Scenes/MainScene.unity:61916` `configLoader: {fileID: 0}` (the MainScene
GameDataManager has no loader wired; TutorialScene's copy does), `GameDataManager.cs:103-110`
(`LoadAllData` takes `SetDefaults` when the loader is null).

**What happens.** A build that starts in MainScene (headless) logs `GameDataManager: External config
disabled or missing loader. Using Hardcoded Defaults.` at Awake, before its own `GameConfigLoader`
even starts fetching, and every `Initial*` value comes from the `default*` fields. The emergency
frequency stays 0, `TaskSystem.numEmergencyTasks` = 0, and Emergency Budget Crisis, Shelter Flood
Damage and Community Emergency Evacuation are `[Limit] Skipping ...: Max emergencies reached` for the
whole game (`validate_v1/staff_5901.log`: 38 skips, 0 creations). The external-relation frequency is
silently 4 instead of 3 in the same branch. The WebGL client enters via TutorialScene, whose
GameDataManager is wired, so it reads the sheet (2) or the loader fallback (4) — not yet confirmed
from a WebGL log.

**Intended.** Defaults set every field; every scene entry point reads the same configuration.

**Proposed patch.** (a) `InitialEmergencyTaskFrequency = defaultEmergencyTaskFrequency;` (b) in
`GameDataManager.Awake`, `if (configLoader == null) configLoader = FindObjectOfType<GameConfigLoader>();`
and wire the reference in MainScene.

**Scope / status.** Headless (and any MainScene-first entry). CONFIRMED.

### A10. Clients who leave lodging are never removed from the building

**Where.** `Map/ClientStayTracker.cs:120` (`OnCaseworklessClientsDeparted` declared), `:415` (raised in
`TriggerNonCaseworkDeparture`); no subscriber anywhere in `Assets/Scripts`. Physical population only
falls through `Vehicle.LoadCargo` (`Vehicle.cs:560`). `Map/MotelCostManager.cs:57-72` bills
`motel.GetCurrentPopulation()` per day.

**What happens.** The tracker drops the people; the shelter/motel storage keeps them. They occupy
capacity, are fed (A1 permitting) and, at the motel, are billed $200/day each. `validate_v1/staff_5503.log`:
line 3054 `Group VehicleDeliv_2: 78 clients without casework departed`, yet the motel bill grows from
`100 residents` (line 2782) to `254 residents` (line 8296) with no departures ever reducing it.

**Intended.** A departure frees the beds and stops the bill.

**Proposed patch.** Subscribe in `ClientStayTracker` itself (or `MotelCostManager` / `Building`):
on departure, `storage.RemoveResource(Population, group.clientsWithoutCaseworkNeed)` on
`group.currentFacility` (both `Building` shelters and the `PrebuiltBuilding` motel via
`RemovePopulation`).

**Scope / status.** Both builds. CONFIRMED. Note A2 doubles the departure count, so fix A2 first.

### A11. Road Blockage Emergency: only the loaded-population variant can be acted on

**Where.** `Tasks/FloodTaskGenerator.cs:68-130` (`CreateRoadBlockageTask` sets `affectedFacility` only in
the `hasLoadedCargo` population branch), `:135-166` (`CreateFoodBlockageChoices`: choices 1-2 are
`triggersDelivery` with `ManualAssignment` source/destination names, choice 3 "Emergency fast food
delivery ($1000)" has **no** delivery at all), `Tasks/TaskSystem.cs:1755-1766` (`FindTriggeringFacility`
returns null for an empty `affectedFacility`), `Tasks/FoodDeliveryHandler.cs:~95-100` (null destination
→ `Execute` returns false).

**What happens.** For a food blockage or a not-yet-loaded population blockage the delivery choices
resolve no facility, `CompleteTaskAction` returns false, and nothing happens — no delivery, no
impacts, no completion; the task just expires with its -20. Choice 3 charges $1000 and adds
satisfaction but moves no food even when it "succeeds". No capture exercises the choices (the surrogate
treats them as inert); example constructed from the code.

**Intended.** Every listed choice does what its label says.

**Proposed patch.** Set `roadBlockageTask.affectedFacility` to the destination name in all branches; make
the `ManualAssignment` choices resolve `specificSourceName`/`specificDestinationName` (or store the
building references on the choice); give choice 3 an `immediateDelivery` of the original quantity from
the original source.

**Scope / status.** Both builds. CONFIRMED by code.

### A12. Editor-only: the server launcher's text boxes take no keyboard input

**Where.** `UI/ServerLauncherUI.cs:424-480` (`MakeInput` builds a `TMP_InputField` at runtime);
exception from `TMP_InputField.OnPointerDown` → `TMP_TextUtilities.GetCursorIndexFromPosition` →
`FindNearestCharacterOnLine` (`IndexOutOfRangeException`, TMP 3.0.7).

**What happens.** In the editor (seen 2026-09-09), clicking the URL or API-key box throws before the
field activates, so typing is neither captured nor rendered. WebGL never hits it because the key is
entered through a DOM overlay. The pre-filled defaults (`ws://localhost:9876/ws`, `dev-local-key`) are
used correctly, so the editor still connects.

**Intended.** Editable fields in the editor.

**Proposed patch.** After building the field, force a layout and text pass
(`LayoutRebuilder.ForceRebuildLayoutImmediate(rt); text.ForceMeshUpdate();`) or create the field with
`TMP_DefaultControls.CreateInputField` instead of assembling it by hand. To be verified in the editor.

**Scope / status.** Editor only. CONFIRMED (observed).

---

## Part B — new findings from the 2026-09-09 audit

Numbering continues per subsystem. "Grep" is the log line to look for in a headless capture
(`ARC_SNAPSHOT_DEBUG=1`).

### Deliveries and vehicles

#### B1. Cargo that does not fit at the destination is destroyed, and the nominal quantity is credited anyway

**Where.** `Delivery/Vehicle.cs:604-606` (`actualDelivered = destStorage.AddResource(...)`; then
`currentCargo[type] = 0`), `BuildingResourceStorage.cs:317-341` (`AddResource` clamps to free space),
`DeliverySystem.cs:694-705` and `TaskSystem.cs:705` credit `completedTask.quantity`, not the actual.
**What happens.** Shelter at 96/100, two vehicles each bringing 5: the first lands 4 and deletes 1, the
second lands 0 and deletes 5. Six people cease to exist (the source was debited at load,
`Vehicle.cs:560`), while `deliveredQuantity` and `RegisterClientArrival` still say 5 each. Constructed
example; the `delivery:unload` mark exposes it wherever `"actual" < "nominal"`.
**Intended.** Deliver `min(cargo, space)`; keep or return the rest; credit what landed.
**Patch.** After `AddResource`, `currentCargo[type] = cargoAmount - actualDelivered`; if > 0, return it
to the source (a return trip, or an immediate `sourceStorage.AddResource`) and pass `actualDelivered`
into the completion event (`OnDeliveryCompleted(this, task, actualDelivered)`), which the two credit
sites then use. Space-check at assignment time as well as at creation.
**Status.** CONFIRMED (code). Both builds.

#### B2. Cancelling a delivery runs the *success* path

**Where.** `Delivery/Vehicle.cs:740-750` (`CancelCurrentTask` → `CompleteDelivery`), `:667-681`
(`CompleteDelivery` raises `OnDeliveryCompleted`), callers `DeliverySystem.cs:500` (`CancelDeliveryTask`),
`:903` (`RemoveVehicle`).
**What happens.** The handler registers arrivals at the destination, removes clients from the source,
records a completed delivery in the daily report, adds the task to `completedTasks`, and — via
`AreAllLinkedDeliveriesComplete` — completes the parent task with its satisfaction reward, for cargo
that never moved (and is still in `currentCargo`). A flood-damaged vehicle has `currentTask == null`,
so `RemoveVehicle` → `CompleteDelivery(null)` → `DeliverySystem.cs:684` dereferences null.
**Intended.** Cancel resets the vehicle and returns or voids the cargo; nothing is credited.
**Patch.** Give `Vehicle` a `CancelDelivery()` that stops the coroutine, returns cargo to the source,
clears state and raises a distinct `OnDeliveryCancelled`; `DeliverySystem` moves the task to a
`cancelledTasks` list.
**Status.** CONFIRMED (code). Both builds.

#### B3. Cargo is never cleared when a vehicle is stopped by flood; it is erased by the next load

**Where.** `Vehicle.cs:372-395` (`StopVehicleDueToFlood`), `:456-463` (`RepairVehicle` → Idle with cargo
aboard), `:575` (`LoadCargo` assigns `=`, not `+=`).
**What happens.** Vehicle carrying 5 meals is stopped; the source was already debited; after repair the
next load overwrites the 5. The population case is handled (`FloodTaskGenerator.ReturnCargoToSource`),
the food case is not.
**Intended.** Stranded cargo returns to its source or is recorded as lost.
**Patch.** In `StopVehicleDueToFlood`, return `currentCargo` to `sourceBuilding` for every cargo type
(reuse `ReturnCargoToSource`), then zero it.
**Status.** CONFIRMED (code). Both builds.

#### B4. An empty-source abort leaves the delivery in `activeTasks` forever

**Where.** `Vehicle.cs:563-572` (abort nulls `currentTask`, sets Idle, never tells `DeliverySystem`);
`activeTasks` is only pruned in `CancelDeliveryTask`, `RemoveActiveDeliveryTask`,
`OnVehicleDeliveryCompleted`.
**What happens.** The orphan keeps counting as reserved outbound stock at the kitchen
(`GetReservedOutgoingQuantity`, used by `FoodDeliveryHandler.cs` and, since 419f5762,
`TaskSystem.IsValidDeliverySource`), reserved inbound at the destination, and keeps
`HasPendingOrActiveDeliveries()` true so the clock never takes its skip-simulation branch again. The
linked task can only expire. The surrogate had to model exactly this orphan to match capture 7002.
**Intended.** An aborted delivery is removed (and its parent task told).
**Patch.** In the abort branch call `DeliverySystem.Instance.RemoveActiveDeliveryTask(taskId)` (once B2/A4
make that a cancel, not a completion) and `TaskSystem.HandleDeliveryFailure(parent, "source empty")`.
**Status.** CONFIRMED (capture 7002). Both builds.

#### B5. A repaired vehicle that is still on a flooded tile re-fails on its next assignment

**Where.** `Vehicle.cs:456-463`, `:303` (no-path branch), `:335` (`CheckForFloodCollision`).
**What happens.** Repair leaves the vehicle where it stopped. If that tile is still flooded, the next
assignment immediately re-triggers `StopVehicleDueToFlood` → another failure penalty, repair task and
blockage task, every round while the flood persists.
**Intended.** Repair relocates the vehicle to the nearest dry road tile, or a repair is refused while
the tile is flooded.
**Status.** PLAUSIBLE (no capture repairs a vehicle). FIXED batch 8: `Vehicle.RelocateToDryRoadIfFlooded`, called from
`RepairVehicle`, moves the vehicle to the nearest unflooded road cell (ties on lower x, then y), clears its path and
emits `vehicle:towed`. If no dry road exists it stays and logs a warning.

### Task lifecycle and generation

#### B6. The emergency spacing gate compares against a constant — at most one global emergency per game

**Where.** `Tasks/TaskSystem.cs:835` (`currentRound = lastDay * roundsPerDay`, i.e. 32, every call),
`:858` (gate: `currentRound < lastEmergencyTaskRound + dynamicInterval`), `:894`
(`lastEmergencyTaskRound = currentRound`).
**What happens.** First global emergency passes (32 < 0 + interval is false), sets `last = 32`; every
later check is `32 < 32 + interval` → skipped forever. With the sheet's `initialEmergencyTaskFrequency
= 2`, exactly one fires. Invisible in headless today because of A9.
**Intended.** Spacing measured in elapsed rounds.
**Patch.** `int currentRound = (GlobalClock.Instance.GetCurrentDay() - 1) * roundsPerDay +
GlobalClock.Instance.GetCurrentTimeSegment();`
**Status.** CONFIRMED (code). WebGL now, headless after A9.

#### B7. Facility-specific emergencies are gated by the cap but never counted toward it

**Where.** `TaskSystem.cs:889-895` (`currEmergencyTaskCount++` only in the `facility == null` branch);
the per-facility branch at `:946` creates without incrementing. `Community_Flood_Damge.asset` and
`Shelter_Flood_Damage.asset` are emergencies with `isGlobalTask: 0`.
**What happens.** Six flooded shelters → six emergency tasks in one round under a cap of 2, while the
counter stays at whatever the global ones set.
**Intended.** Every created emergency counts.
**Patch.** Move the increment/`lastEmergencyTaskRound` update to just after `CreateTaskFromDatabase` in
both branches.
**Status.** CONFIRMED (code).

#### B8. The road-blockage "clients gave up" penalty (-30) can never fire

**Where.** `Tasks/FloodTaskGenerator.cs:40-41` (subscribes `OnTaskCompleted` only), `:288-300`
(`OnAnyTaskCompleted` applies -30 when status is Expired/Incomplete), `TaskSystem.ExpireTask`
(raises `OnTaskExpired`, never `OnTaskCompleted`). The loaded-population choice is `immediateDelivery`
(`:184`), so the task is never InProgress and the only Incomplete path that raises `OnTaskCompleted`
(`SetTaskIncomplete`) never runs for it.
**What happens.** Ignoring a loaded blockage costs only the task's own -20; the advertised "severe"
penalty never lands, and `blockageTaskLoadedState[id]` is never removed.
**Intended.** -30 on expiry when clients were aboard.
**Patch.** Also subscribe `OnTaskExpired += OnAnyTaskCompleted`.
**Status.** CONFIRMED (code). No capture expires a loaded blockage.

#### B9. Delivery quantities and lodging demand are computed against the first building of the *type*, not the triggering facility

**Where.** `TaskSystem.cs:1590` (`CreateTaskFromData` sets `affectedFacility = targetFacilityType.ToString()`),
`:1639-1660` (delivery block calls `FindTriggeringFacility` with that type string; `FindFacilityByName`
(`:1768-1805`) matches by `Contains`, returning the first operational building whose name contains
e.g. "Shelter"); the real facility name is written only afterwards at `:2058`. The
`deliveryQuantity = 1` fallback at `:1653` is dead — overwritten at `:1660`.
**What happens.** Shelter Flood Damage triggered by Shelter_3 (60 residents) sizes its relocation
choices from Shelter_1; if no building of the type is operational, `source == null` and the quantity is
0, so `RecordTaskResolution` (`RewardMetricsTracker.cs`) falls back to counting the task as 1 unit of
demand. `demandQuantity` for lodging tasks (`:1710`) inherits the same error.
**Intended.** Quantities from the facility that raised the task.
**Patch.** Pass the facility into `CreateTaskFromData(taskData, facility)` and set `affectedFacility`
before the choice loop (the per-facility caller already has it).
**Status.** CONFIRMED (code).

#### B10. Alert tasks are counted as lodging demand

**Where.** `Flood_Alert.asset` (`taskType: 3` Alert, `taskTag: 2` Lodging, `roundsRemaining: 1`);
`RewardMetricsTracker.RecordTaskResolution` filters on `taskTag` only; `TaskSystem.ExpireTask` calls it
for every expiring task. Dismissing the alert via `CompleteAlertTask` (`:1997`) records nothing.
**What happens.** Each unread flood alert adds 1 to `lodgingResolved` with 0 fulfilled. Seen in every
capture: all 42 `validate*` logs contain a `task:resolved` mark with `"title":"Flood Alert - Rising
Water"`, `"tag":"Lodging"`, `"fulfilled":false`.
**Intended.** Alerts and advisories are not demand.
**Patch.** `if (task.taskType == TaskType.Alert || task.taskType == TaskType.Other) return;` at the top
of `RecordTaskResolution`, or give alerts no tag.
**Status.** CONFIRMED (capture).

### Choice execution (UI path vs headless / agent path)

#### B11. The headless choice path skips every validation the UI runs

**Where.** `Tasks/TaskDetailUI.cs:1277-1316` (`SelectTaskChoiceHeadless` → `CompleteTaskAction()`
directly), vs `:1063-1129` (`OnConfirmButtonClicked`: `ValidateNumericalInputs`, `ValidateChoiceDelivery`,
`WorkerAssignmentHandler.ValidateForConfirm`), vs `:1038-1062` (`TryConfirmTask`, a third gate set used
by the officer conversation UI). Callers: `GymServerManager.cs:657`, `WebSocketManager.cs:510`.
**What happens.** The same choice can be rejected for a human and accepted for an agent. Example: a
food request whose quantity is already covered by inbound deliveries — the UI refuses; headless enters
`FoodDeliveryHandler.Execute` EXIT A (`FoodDeliveryHandler.cs:105-117`), which `CompleteTask`s the
request as **fulfilled** with no food moved (`food:exit {"branch":"inbound-covered"}` in the captures).
**Intended.** One validation function shared by all three entry points.
**Patch.** Route `SelectTaskChoiceHeadless` through `TryConfirmTask` (after fixing its
`currentConversationItems` dependency) and delete the duplicated checks in `OnConfirmButtonClicked`.
**Status.** CONFIRMED (code; EXIT A seen in captures). Agent paths.

#### B12. `CompleteTaskAction` has no budget gate, and every failure is reported to the agent as "insufficient budget"

**Where.** `TaskDetailUI.cs:1139-1207` (no affordability check; `ApplyChoiceImpacts` → `RemoveBudget` →
`AddBudget(-x)` unconditionally), `:1305-1315` (headless assumes the only `false` is the no-debt gate and
builds `failReason` accordingly), `SatisfactionAndBudget.cs:638` (`WouldAllowSpend` exists but is only
used by `ActionExecutor`).
**What happens.** A -$5000 choice at $1000 succeeds regardless of `allowNegativeBudget`, while the same
$5000 as a build action is refused by `ActionExecutor.HasBudget` (`ActionExecutor.cs:127`). A choice
that fails for a real reason (no source, no route) returns `rejected: insufficient budget` to the
RL/LLM agent — a wrong learning signal.
**Intended.** Same affordability rule for choices and actions; truthful failure reasons.
**Patch.** In `CompleteTaskAction`, check `WouldAllowSpend(GetChoiceImmediateCost(choice))` first and
return a reason string from each failing branch.
**Status.** CONFIRMED (code). Agent paths and UI.

#### B13. Multi-delivery and casework choices report success even when zero deliveries were created

**Where.** `TaskDetailUI.cs:1330-1335` (`ExecuteGeneratorDelivery` returns -1 unconditionally after
`ExecuteMultipleDeliveries`), `:1409-1415` (`ExecuteClientRelocation` discards `ExecuteFallbackDelivery`'s
result for non-shelter destinations), `:1177-1184` (impacts applied and task set InProgress on -1).
**What happens.** No reachable casework site → 0 deliveries, cost charged, satisfaction credited, task
InProgress with no linked deliveries until it expires, then `SetTaskIncomplete` applies the failure
penalty on top.
**Intended.** No effect without a delivery.
**Patch.** Return 0 when `linkedDeliveryTaskIds.Count` did not grow (or propagate the handler's bool).
**Status.** CONFIRMED (code).

#### B14. Multi-source dispatch sends the full fixed quantity from *each* source; multi-destination drops the remainder

**Where.** `TaskDetailUI.cs:2603-2647` (`ExecuteMultiSourceSingleDest`: `CalculateDeliveryQuantity` per
source, no running remainder; `Fixed` returns `choice.deliveryQuantity` each time), `:2578-2579`
(`ExecuteSingleSourceMultiDest`: `totalQuantity / destinations.Count`, integer division), and the three
`ValidateMultipleDeliveries*` methods at `:1887/1911/1991` have no callers — `ValidateChoiceDelivery`
(`:1773-1777`) returns true for the pure multi-delivery case. `DeliverySystem.CreateDeliveryTask`
(`:360-415`) never clamps to source stock or destination space.
**What happens.** Three kitchens, choice `Fixed 50` → 150 meals dispatched to a site needing 50 (the
surrogate models this as "full quantity per site, no space cap"). Ten clients over three shelters → 9.
**Intended.** Split the requested quantity across sources/destinations and clamp to stock and space.
**Patch.** Track `remaining` in both loops, clamp per source to `effectiveStock` and per destination to
free space, and call the existing validators from `ValidateChoiceDelivery`.
**Status.** CONFIRMED (code; behaviour matches captures). Both builds.

#### B15. The immediate food "airdrop" creates food from nothing and cannot fail

**Where.** `Tasks/FoodDeliveryHandler.cs:170-183` (`ExecuteImmediate`: `AddResource` on the destination,
no kitchen debit, return value ignored), `TaskDetailUI.cs:1215-1219` (`ExecuteFoodDelivery(immediate:true)`
returns true even for a null destination); the validator for this branch (`:1799-1806`) requires an
undamaged vehicle that the execution never uses.
**What happens.** The request is credited for the full amount even when storage clamps it; food total
in the world rises without a source.
**Intended.** NEEDS DECISION whether an airdrop is external supply (no debit) — but it must still fail
on a missing destination and credit only what fit.
**Status.** CONFIRMED (code).

#### B16. Worker-input tasks confirmed headless apply their default slider values

**Where.** `WorkerAssignment/WorkerAssignmentHandler.cs:254-255`, `WorkerRequestSystem.cs:201-202`,
`WorkerTrainingSystem.cs:182`, `WorkerReturnSystem.cs:176-177` read `task.numericalInputs[i].currentValue`,
which only `NumericalInputUI` mutates; `SelectTaskChoiceHeadless` sets none and skips
`ValidateForConfirm`.
**What happens.** An officer confirming a staffing task through the WebSocket path applies the seeded
default (often 0), e.g. releasing a building's workers to zero. Headless RL never creates these tasks
(they come from UI buttons), so the exposure is the WebGL officer path.
**Intended.** Headless confirmation carries explicit input values or is refused for input tasks.
**Status.** PLAUSIBLE (needs a WebGL check). Officer path.

### Workers

#### B17. Training is charged, then silently aborted

**Where.** `WorkerAssignment/WorkerTrainingSystem.cs:198` (`RemoveBudget`) → `:212-216`
(`StartWorkerTraining` re-queries free untrained workers and returns if fewer than requested). The
task's maximum was snapshotted at creation (`:103/:111`).
**What happens.** 5 free untrained → open the task (max 5) → assign 3 elsewhere → confirm 5: $2500
deducted, 0 trained.
**Intended.** Validate against the live pool before debiting; clamp or refund.
**Patch.** Move the pool check before `RemoveBudget`, or refund when `StartWorkerTraining` returns false.
**Status.** CONFIRMED (code).

#### B18. Training task expiry checks the wrong title

**Where.** `WorkerTrainingSystem.cs:72` (`"Worker Training Program"`) vs `:120/:170`
(`"Responder Training Program"`).
**What happens.** An expired training task never clears `currentTrainingTask`; the next open creates a
second task and the expired one's inputs stay live.
**Patch.** One shared constant.
**Status.** CONFIRMED (code).

#### B19. The locked-building release check compares workforce points with a head count

**Where.** `WorkerAssignmentHandler.cs:337` (`isLocked && newWorkforce < currentHeadCount`;
`newWorkforce = trained*2 + untrained`, `currentHeadCount` = number of workers).
**What happens.** Building locked with 4 untrained (4 heads, 4 points). Set 2 trained + 0 untrained: 4
points is not < 4 heads, passes → two locked workers are released.
**Intended.** The commented-out head-count comparison at `:323-328`.
**Patch.** `newHeadCount < currentHeadCount`.
**Status.** CONFIRMED (code).

#### B20. Deconstruction leaks workers unless the building was fully staffed, and runs twice

**Where.** `Map/BuildingSystem.cs:438-443` (returns workers only `if (building.IsOperational())`, i.e.
status `InUse`; a building with 3 of 4 points is `NeedWorker`), `Map/Building.cs:401-404`
(`DeconstructBuilding(this)` called twice, then unconditional `return`, making the fallback at
`:427-436` dead).
**What happens.** Deconstruct a shelter staffed 3/4: the 3 keep `assignedBuildingId` for a site that no
longer exists — permanently neither free nor assignable. If `DeconstructBuilding` fails (site not
registered) the building stays `Deconstructing` forever with its residents trapped
(`ClientStayTracker.cs:357-360` skips casework for a deconstructing facility).
**Intended.** Release workers unconditionally; one call; handle failure.
**Patch.** Drop the `IsOperational()` condition; `successfullyHandled = buildingSystem.DeconstructBuilding(this);`
once and fall through to the fallback on false.
**Status.** CONFIRMED (code).

#### B21. Agent worker actions charge whatever `cost` the agent sent, and never multiply by quantity

**Where.** `Actions/ActionExecutor.cs:155` (`HasBudget(action.cost)`), `:168, :193, :226`
(`RemoveBudget(action.cost, ...)`); nothing consults `WorkerRequestSystem.trainedWorkerCost` /
`untrainedWorkerCost` / `WorkerTrainingSystem.trainingCostPerWorker`; same for builds (`:143`).
**What happens.** The executor trusts the client's arithmetic. The maintained gym/router client does
compute the total (`validate_v1/staff_5503.log` lines 1488-1493: `Hire 4 untrained worker(s) ($100
each)` → `-400`), so today's captures are correct. Any other client — a free-form command path, a
model emitting its own JSON, or a deliberately adversarial one — can send `{hire_trained, quantity: 10,
cost: 300}` and receive 10 trained workers for $300 (the UI path charges $3000), or `cost: 0` and play
for free. Builds have the same shape (`:143`, and A6 shows the menu price is already wrong).
**Intended.** Price computed server-side from quantity × configured rate.
**Patch.** Ignore `action.cost`; compute from the systems' fields; reject on mismatch if you want the
agent to state a price.
**Status.** CONFIRMED (code). Agent paths — matters directly for RL and benchmarking.

#### B22. Agent worker assignment bypasses the required-workforce rule and the lock

**Where.** `ActionExecutor.cs:341` → `WorkerSystem.TryReassignWorkerCountToBuilding` (`WorkerSystem.cs:218-247`):
checks pool size only; never `GetRequiredWorkforce()`, never `WorkerAssignmentTracker.IsLockedForRelease`,
never `RecordAssignment`. The UI path enforces all three (`WorkerAssignmentHandler.cs:337-363`).
**What happens.** `quantity: 6` on a 4-point building parks 12 points there; an agent can strip a locked
building to 0; agent-staffed buildings are never locked.
**Intended.** Same rules for both paths.
**Patch.** Route the action through `WorkerAssignmentHandler`'s validation, or replicate the three checks.
**Status.** CONFIRMED (code). Agent paths.

#### B23. Worker utilisation is normalised by a hard-coded pool of 200

**Where.** `DailyReport/DailyReportUI.cs:72` (`assumedTotalWorkerPoolSize = 200`), `:1253`, `:1270`.
**What happens.** With 8 workers over 10 rounds the working ratio spans 0.00–0.04, so the worker term of
the satisfaction score moves ~2.7% across the entire behavioural range; above 200 workers the ratios
exceed 1.
**Intended.** Divide by the live pool-rounds (`DailyReportData.cs:342` already accumulates them).
**Status.** CONFIRMED (arithmetic). NEEDS DECISION if 200 is deliberate.

#### B24. Agent worker actions record cumulative cost but not today's cost

**Where.** `ActionExecutor.cs:171, 196, 229` (only `Record*CostCumulative`), vs the UI path which also
calls `Record*CostToday`.
**What happens.** The daily report shows $0 worker spend for a day in which an agent hired.
**Patch.** Add the `...Today` calls.
**Status.** CONFIRMED (code). Reporting only.

### Buildings, clients, flood

#### B25. A casework delivery drains people from the wrong client group

**Where.** `Map/ClientStayTracker.cs:515-527` → `RemoveClientsByQuantity(source, count)` (`:447-491`)
iterates `GetClientsInShelter` (`:603-606`, every group at the facility in insertion order) and removes
from the front; the requesting group is known (`|CLIENT_GROUP_ID:` in the description, `:585`, parsed at
`:257-275`) but unused. `RecordCaseworkProcessed(totalRemoved)` credits everyone removed.
**What happens.** Shelter with Group_2 (6 people, no casework need, registered first) and Group_1 (8,
all needing casework, raised the request): sending 8 removes all of Group_2 and 2 of Group_1; Group_1
still has 6 casework clients and re-raises the request every few rounds; 6 people with no need are
credited as processed.
**Intended.** Drain the requesting group's `clientsWithCaseworkNeed`.
**Patch.** Resolve the group id from the parent task and remove from that group first.
**Status.** CONFIRMED (code).

#### B26. Communities consume 400 meals a day each, and their population never comes from the configuration

**Where.** `Prefabs/CommunityPrefab.prefab:127-137` (the Population resource entry: `maxCapacity: 400`,
`amount: 400`; `startingFoodPacks: 0`; `enablePopulationBasedConsumption: 1`;
`foodPerPersonPerNRounds: 1`). No script under `Assets/Scripts` other than `GameDataManager`,
`GameConfigLoader` and the instructor panel reads `InitialResidentsPerCommunityNumber`, so the sheet's
`initialCommunityResidentCount = 30` never reaches a community.
**What happens.** `validate_v1/staff_5503.log` lines 820-824: `Community01 FOOD SHORTAGE: Need 400,
only had 0` (and 02, 03) every day. Three communities add 1200/day of need the food-coverage score
(`DailyReportUI.cs:1234-1236`, `consumed / needed`) can never meet, so coverage reads a few percent
whatever the player does. Relocating a client from a shelter to the motel makes their need vanish
(motel does not consume).
**Intended.** NEEDS DECISION: do communities consume at all, and at what population? The
`initialCommunityResidentCount` parameter must actually set the community population either way.
**Status.** CONFIRMED (capture).

#### B27. Trained workers eat two meals

**Where.** `BuildingResourceStorage.cs:259-266` adds `building.GetAssignedWorkforce()` (workforce
*points*, trained = 2) to the mouths to feed when `workersConsumeFoodToo`.
**Patch.** `workerSystem.GetWorkersByBuildingId(id).Count`.
**Status.** CONFIRMED (code).

#### B28. The last day of motel lodging is never billed

**Where.** `Map/MotelCostManager.cs:27` (bills on `OnDayChanged`), `GlobalClock.cs:670` (the last day ends
without a rollover).
**Status.** CONFIRMED (code). Minor; fold into the A1 timing decision.

### Clock, economy, parameters

#### B29. `initialWeather` is dead — day 1 is always Sunny

**Where.** `WeatherSystem.cs:84-100` (`Start` launches `InitializeWithCentralConfig`, then synchronously
`SetWeather(startWeather)` with the field still `Sunny`; the coroutine assigns `startWeather` at `:112`
but never re-applies it; `SetWeather` returns early on equality at `:200`).
**What happens.** Sheet says HeavyRain; day 1 runs Sunny (flood expansion 0), weather first changes at
the day-2 rollover.
**Patch.** Call `SetWeather(startWeather)` at the end of `InitializeWithCentralConfig`.
**Status.** CONFIRMED (code). Both builds (headless via A9 defaults).

#### B30. Copy-paste in the loader: the flood detection range overwrites the tile threshold

**Where.** `ScenarioLoader/GameConfigLoader.cs:334-338` (`initialShelterFloodDamageFloodDetectionRange`
assigns `loadedInitialShelterFloodThreshold`).
**What happens.** Sheet threshold 1 / range 4 → applied threshold 4, radius left at the default 5:
shelter flood-damage tasks fire far less often than configured.
**Patch.** `loadedInitialShelterFloodRadius = floodDetectionRange;`
**Status.** CONFIRMED (code). WebGL (sheet path).

#### B31. External-relation frequency is split by an unseeded random and used as a day interval

**Where.** `GameConfigLoader.cs:535-576` (`ApplyInitExternalRelationFrequency`: `new System.Random()`
coin-flip splits the count into halves written as `DayTrigger.intervalDays`, `startDay = 2`); runs only
when the sheet loads (`:356-359`).
**What happens.** "5 per game" → advisory every 2 days + emergency every 3 days = 7 events over 8 days,
or 3 + 4 the other way round, decided by an RNG outside the game's seeded stream — so two WebGL runs
with the same seed can differ.
**Intended.** A total count, deterministic.
**Patch.** Derive both intervals from the seeded RNG (or fix the split), and schedule so the total equals
the parameter.
**Status.** CONFIRMED (code). WebGL. Breaks seed reproducibility for the benchmark client.

#### B32. Four rounds per day is hard-coded in several places

**Where.** `GlobalClock.cs:329, 476` (`(currentDay-1)*4`), `:419` (`round < 4`), `:528` (`>= 4`), vs the
configurable `roundsPerDay` at `:656/:707`; `GymServerManager.cs:1560-1566` and `TaskSystem.cs:2593`
also use `seg >= 4`.
**What happens.** `initialRoundsPerGameDay = 3` runs a fifth simulation per day and reports wrong round
numbers to the router.
**Patch.** Replace with `roundsPerDay`.
**Status.** CONFIRMED (code).

#### B33. Two different game horizons

**Where.** `DailyReport/DailyReportManager.cs:29` (`finalDay = 8`, never set from config) is what
`GymServerManager.IsFiniteHorizonOver` and `TaskSystem.GetSessionInfo` use; `GlobalClock.lastDay`
(`:134`, from config) gates the end-game panel.
**What happens.** `initialDaysPerRun = 5`: the GUI ends at day 5, the gym reports `isGameOver = false`
and keeps stepping to day 8.
**Patch.** Set `finalDay` from `GameDataManager.InitialGameDays` (or read `GlobalClock.lastDay`).
**Status.** CONFIRMED (code).

#### B34. The daily report mixes a 0–1000 scale with the authoritative 0–100 scale

**Where.** `DailyReportUI.cs:276-277` (seeds current values 0–100), `:744-745` (`*1000f`), `:766-768`
(`new - current`), `:802-803` (writes back `*100f`).
**What happens.** Day 2 with an unchanged 0.50 score reports a +450 "change"; the report's final value
(500) disagrees with the game_state value (50) the agents see.
**Patch.** One scale.
**Status.** CONFIRMED (code). Display and any analysis reading the report.

#### B35. Parameter sheet rows that do nothing, and loader fields no row can reach

**Where (as found).** The sheet is fetched by `GameConfigLoader` from `/sheet.csv`; a root-relative URL only
resolves inside a browser, so the editor and every headless build failed the fetch, waited out the 5 s
timeout and ran on the loader's serialized fallbacks (`MainScene.unity`: budget 5000, satisfaction 50,
3 ERVs, emergency cap 4 ...). The StreamingAssets copy of the sheet was never read. Rows the human WebGL
game did apply reached it by mutating ScriptableObjects from the loader (`ApplyInitBudgetAllocation`,
`ApplyInitFoodDemandFrequency`, `ApplyInitShelterFloodDamage`; `ApplyInitExternalRelationFrequency`
replaced the assets' day triggers with a `DayInterval` whose check ignores `startDay`), and only through
the three references wired in TutorialScene's loader. So humans on Talos played with allocation 2000,
shelter-request probability 0.2 and the sheet's flood-damage trigger, while RL and the benchmarks played
the asset values. Rows nothing consumed anywhere: `initialCommunityResidentCount`,
`initialKitchenCapacity`, `initialShelterCapacity`, `initialCaseworkCapacity`,
`initialWorkerUnitsNeededPerLocation`, `initialExternalRelationFrequency` (refs unwired in both scenes),
`initialKitchenFoodCapacity`, `initialShelterFoodCapacity` (not even parsed), the `rainExpansionRate` /
`rainFloodProbability` placeholders (ranges, not values; the loader expects ten per-weather rows the
sheet lacked) and the blank `initialCommunityDistanceSpread` / `initialShelterRepairTime`.

**Fixed (batch 8).** Source chain `ARC_PARAM_CONFIG` env var (a CSV path; the gym env's
`param_config=` and `validate_plan --param-config` export it) → sheet URL (browser only) → the
StreamingAssets copy → serialized fallbacks; the source and every value in effect are logged
(`GameDataManager: parameters in effect {...}`, mark `config:loaded`). Rows are applied where they are
consumed and never by mutating a ScriptableObject:

| row | consumer now | value in the StreamingAssets copy (Talos sheet value) |
|---|---|---|
| `initialBudget`, `initialSatisfaction`, days, rounds, volunteers, `initialERVCount`, `initialWeather`, `initialEmergencyTaskFrequency` | as before (already live where the sheet loaded) | sheet values: 8000, 0, 8, 4, 5/5, 5, HeavyRain, 2 |
| `initialCommunityResidentCount` | `BuildingResourceStorage.ApplyConfiguredCapacities` sets each community's population | **400 (30)** — 30 never crosses the food-request threshold of 100 or sources a 100-client relocation |
| `initialShelterCapacity`, `initialCaseworkCapacity` | Population capacity of built shelters / casework sites | **100 (20)**, **400 (15)** — prefab values kept |
| `initialKitchenCapacity` | meals a kitchen produces per round (the report already used it as throughput) | **100 (5)** |
| `initialKitchenFoodCapacity`, `initialShelterFoodCapacity` | FoodPacks capacity (new loader rows) | **200 (285)**, **100 (340)** |
| `initialWorkerUnitsNeededPerLocation` | `Building.requiredWorkforce` | **4 (2)** |
| `initialDailyBudgetAdditions` | `TaskSystem.ApplyConfiguredAllocation` on the task instance (impact, choice text, message) | 2000 (2000; the asset says 5000, the message said 3000 — humans already got 2000) |
| `initialFoodDemandFrequency` | `TaskDatabases.CheckProbability` for food-request tasks that have a probability trigger (Shelter only) | 0.2 (0.2) |
| `initialShelterFloodDamage{Comparison,FloodTileThreshold,FloodDetectionRange}` | `TaskDatabases.Configured` builds the Shelter Flood Damage trigger from the config | AtMost / 1 / 4 (same) — see B36 |
| `initialExternalRelationFrequency` | total cap on Storm Funding Advisory + Emergency Budget Crisis creations (`TaskSystem.numExternalRelationTasks`, `[Limit]` log), the sheet's stated meaning; the old day-interval rewrite is gone | 5 (5) — newly enforced: seed 5801 lost 3 advisories |
| ten `initial<Weather>Flood{ExpansionRateMultiplier,SpreadChanceMultiplier}` | as before | the `GameDataManager` defaults (0/0.5, 0.5/0.8, 1.5/1, 3/1.2, 5/1.5) |
| `initialCommunityCount` | informational: the map decides; a mismatch is logged | 3 (4) |
| removed rows | `rainExpansionRate`, `rainFloodProbability`, `initialCommunityDistanceSpread`, `initialShelterRepairTime` | — |

Bold values are behaviour-preserving choices, NOT the Talos sheet's numbers: every people-scale row in
the sheet is about a tenth of the prefabs and would ship a different game. The Google Sheet must be
brought in line with the StreamingAssets copy before a WebGL deploy from this branch, because the wired
rows take effect the moment the sheet loads. Verification: seed 5503 replayed with a CSV that
reproduces the old fallbacks is RNG-identical to the batch-7 capture (`compare_captures`), and a probe
CSV that changes every wired row shows each value in the log.

Also fixed earlier: `ParseCSV` used culture-sensitive `float.TryParse` (comma-decimal locales lost every
float) and skipped any row whose first cell contained "parameter".
**Status.** CONFIRMED (code + captures).

#### B36. Shelter Flood Damage fires when the flood is absent

**Where.** `TaskData/Shelter_Flood_Damage.asset` (`comparison: 4` = AtMost, threshold 4, radius 5) and the
sheet (`AtMost`, 1, 4); `FloodedFacilityTrigger.CheckComparison` (`TaskTrigger.cs:518-532`).
**What happens.** "AtMost N flooded tiles within R" is true for a shelter with zero flooded tiles, so the
trigger holds exactly when the shelter is dry and fails once the flood arrives. On the batch-8 replay of
5503 the trigger was evaluated true only on passes with ≤1 flooded tile nearby and was then stopped by
the emergency spacing gate; it never fired in any capture. The description in the sheet ("how much of the
shelter has been flooded") and the emergency's own text describe the opposite condition.
**Intended.** Almost certainly `AtLeast` with a threshold ≥ 1. NEEDS DECISION on the values.
**Status.** CONFIRMED (code + capture). Not fixed: the values are configuration.

---

#### B37. Agent-path day rollover never clears the end-of-day flag; every round end cancels food deliveries

**Where.** `GlobalClock.cs` `EndSimulation` (`isWaitingForReport = true` once `currentTimeSegment >= roundsPerDay`),
`OnExecuteButtonClicked` (the only reset), `ProceedToNextDay` (no reset); `TaskSystem.OnSimulationEndedCheckDayComplete`.
**What happens.** Humans clear the flag by confirming "End Today" (the report manager then calls `ProceedToNextDay`).
The gym (`GymServerManager` → `GymAdvanceRound`) calls `ProceedToNextDay` directly, so the flag stays true for the rest of the game and the end-of-day
cancellation runs at the end of every round from day 2: 5504 smoke replay, cancellations after
`Simulation ended - Now at Day 3, Round 3` etc. (8 round ends, 15 deliveries, 16 penalties); shelter probe
day 3: the second vehicle of a 200-meal double, dispatched at round 2, cancelled at the end of round 2.
**Intended.** Cancellation only at the end of round 4.
**Scope.** Headless/gym only (RL + benchmark + the surrogate's replays). WebGL and editor play go through
the button, so human sessions and the router's officers on a human clock were never affected; nothing was
pushed, so no published result is affected.
**Status.** FIXED (`v1_merge_test`): `ProceedToNextDay` clears the flag. After the fix all cancellations
follow `Day N complete`; 5504: 7 cancellations / 10 penalties, 5801: 4 → 0.

#### B38. Overnight cancellation fails deliveries whose cargo has already landed

**Where.** `Vehicle.UnloadCargo` (cargo added to the destination and `deliveredQuantity` set at the top of the
coroutine, then the unload wait), `Vehicle.ExecuteDeliveryTask` step 5 (`CompleteDelivery` after the wait),
`TaskSystem.CancelIncompleteFoodDeliveries` (took every pending + active food delivery).
**What happens.** A round ending inside the unload wait leaves the task in `activeTasks`; the cancel removed
it, marked the parent task failed with the 15-point penalty, and the food that was already on the shelf was
then eaten by the periodic tick (shelter probe day 3, `Vehicle2`: `received 100 FoodPacks (100/200)`, then
`Cancelled incomplete food delivery 2`, then `fed 100 people after 2 rounds, consumed 100/100`).
**Intended.** A delivery whose cargo landed is complete for the purpose of the overnight rule.
**Status.** FIXED (`v1_merge_test`): deliveries with `deliveredQuantity > 0` are skipped by the cancel.

---

## Part C — intended behaviour to confirm (design decisions, not defects)

1. **Round-4 timing (A1).** Fixing the missing segment event moves consumption, production, ageing
   and generation earlier and adds the final day's fourth tick. Difficulty changes; the surrogate is
   re-derived either way.
2. **Reserved-quantity validity and nearest-kitchen ordering** (main-bugfixes 419f5762,
   `TaskSystem.cs` `IsValidDeliverySource/Destination`, `FoodDeliveryHandler.GetKitchensSorted`). Applied
   to the single-source path only; the multi-source path (B14) disagrees. Never fired on any capture.
3. **Food request already covered by inbound deliveries counts as fulfilled** (`FoodDeliveryHandler.cs:105-117`).
   Unity credits the task with no delivery; the UI path refuses the same choice (B11).
4. **Expiry of an InProgress task while its vehicle is en route** (`TaskSystem.cs:567-573`,
   `CancelTaskDeliveries` commented out): failure penalty now, late-delivery credit on arrival, penalty
   never refunded.
5. **Probability triggers are rolled per facility before caps/dedup** (`TaskDatabases.cs:229-278`) — an
   RNG-stream and starvation question, not a counter bug; the surrogate matches it today. The
   `FloodChangeTrigger` (`TaskTrigger.cs:326-352`) keeps per-instance state and would let only the first
   facility see a flood change (PLAUSIBLE; no shipped asset uses it).
6. **Worker units.** Operational checks use workforce points (trained = 2); reward counters
   (`RewardMetricsTracker.cs:94-102`) and the daily report count heads. Training a worker lowers the
   cumulative "working" credit for an equally staffed building.
7. **Airdrop food without a kitchen debit** (B15).
8. **Community food consumption** (B26) and the 200-worker normaliser (B23).
9. **Officer display names** changed by main-bugfixes (`Tasks/AlertUIController.cs`: Logistics →
   Lodging, Mass Care → Food Services, External Relationship → External Relations) while officer tab
   names still come from the router roster.
10. **Day 1 in the human GUI skips the round ticks.** `GlobalClock.Day1SkipCoroutine` (main-bugfixes)
   sets `currentTimeSegment` directly and raises only `OnRoundEnd` for the four rounds, never
   `OnTimeSegmentChanged`, so a human player's day 1 has no task generation, consumption, ageing or
   client-tracker ticks; the gym/router path takes `SimulationCoroutine` and gets the normal passes
   (generation at rounds 1-2, ticks 1-4). RULED 2026-09-09 and FIXED (batch 8): the day-1 branch and
   `Day1SkipCoroutine` are gone, day 1 runs through the normal round flow for humans too (four clicks,
   the "no active deliveries" clock skip per round, officers asked for proposals from round 1).
   `FirstDayTutorialManager` still completes on the day-1 report; the setup-phase banner text in
   `ClockAnimationUI` is now unused.

## Part D — claims rejected during verification

- "DelayedBudgetManager never credits the delayed budget." False: it is display-only; the credit is
  `BudgetAllocationManager.ScheduleAllocation` (`TaskDetailUI.cs:3032`, `BudgetAllocationManager.cs:63-81`)
  and the captures show it arriving.
- "`SetTaskInProgress` after `CompleteTask` double-processes." It guards on `activeTasks.Contains`; no-op.
- "A task can be resolved twice." `CompleteTask`, `ExpireTask` and `SetTaskIncomplete` all remove from
  `activeTasks` first; no double `RecordTaskResolution` path exists (A4/A5 are the *missing* ones).

---

## Part E — questions for the food-overhaul / self-walk merge (asked 2026-09-10)

Everything below is on `34d2133d` (`v1_merge_test`), i.e. `origin/main-bugfixes` `d5e5f683`
("major food related mechanism update and client relocation changed (no vehicle)") plus
`5d922203` (self-walk relocation) merged onto `v1_fixes`. The rest of this document is still
pinned to `868c192e`, so line numbers elsewhere predate these changes.

These are questions, not defect claims: the mechanics below are being **kept as they are** until
they are ruled on. Each says what the code does today and what an answer would change.

### E.1 Motel eats once a day, shelters twice — intended?

`Shelter.prefab:310-313` `consumptionRoundInterval: 2`; `MotelPrefab.prefab:129-133`
`consumptionRoundInterval: 4`. With the A1 clock fix the day delivers four segment events
(1, 2, 3, 4), so a shelter runs its population-consumption tick twice a day and the motel once,
even though both were given the same *pair* of daily food-request tasks (`*_FoodRequest_First`
at `targetRound: 0` = round 1, `*_FoodRequest_Second` at `targetRound: 2` = round 3).
`d5e5f683`'s message says the motel change "mirrors Shelter", which the interval does not.

*Follow-on:* if the motel only eats once, is its round-3 follow-up request ever legitimately
open, or does `GetFoodNeed() > 0` (`NeedsFood`, `resourceTriggers.condition: 3`) simply fail and
the task never appears? Same question for the "Deliver double to cover the current and follow-up
requests" choice (`quantityType: PopulationBased`, `deliveryPercentage` 200) on the motel task:
double of a once-a-day need is a full day of waste at the rollover (`enableFoodWaste: 1`).

*Changes:* difficulty and the food-coverage score for lodging; the surrogate's per-building
consumption schedule.

### E.2 Communities no longer consume; depletion is the whole demand model

`CommunityPrefab.prefab` now has `enablePopulationBasedConsumption: 0`, `enableFoodWaste: 0`, and
communities start and stay at food capacity (400). `Tasks/CommunityFoodDepletionManager.cs` rolls
`UnityEngine.Random.value < depletionChancePerRound` (sheet `initialFoodDemandFrequency`, 0.2)
per community per round, days ≥ 2, rounds 1-3, removes `depletionAmount` (100) and spawns
`Community_FoodRequest` for exactly what was lost, skipping if a request for that community is
already active.

Questions: (a) is 100 packs per event / 0.2 per community-round the intended pressure, or a
placeholder? (b) communities are never refilled except by fulfilled requests — a community that
loses four events and gets none fulfilled sits at 0 and (with `enableFoodWaste: 0`) stays there;
is starvation meant to be absorbing? (c) B26 (communities consuming 400/day and the sheet's
resident count never reaching them) is superseded by this — confirm B26 is closed rather than
re-derived.

*Changes:* B26's status, the RNG stream (this is a new per-community draw per round —
`SnapshotDebug.Mark("draw:CommunityFoodDepletion")`), and the surrogate's food-request generator.

### E.3 Two consumption paths: the round tick and consume-on-delivery

`BuildingResourceStorage.AddResource` now feeds the whole population the moment food arrives, and
the round tick does the same; both go through `ConsumeFoodForPopulationOncePerRound`, keyed on
`day*100 + segment`. So a facility eats at most once per round, but *which* round it eats in now
depends on when a delivery lands.

Question: when a delivery consumes in a round where the interval tick would also have fired, the
tick returns false and `roundsSinceLastConsumption` is **not** reset by the tick path (the
delivery path resets it), so the phase of the cycle shifts with delivery timing. Is that
intended, or should a delivery-triggered feed count as that cycle's feed?

*Changes:* whether the surrogate can model consumption on a fixed schedule at all, or has to
model it as an event keyed to delivery arrival.

### E.4 Shelters stopped feeding their workers — RULED 2026-09-10: workers do not consume food

`Shelter.prefab` `workersConsumeFoodToo: 1 → 0` in `d5e5f683`. No building now feeds staff.
**Ruling (2026-09-10):** intended — workers never consume food. Every prefab already carries the
flag off and the surrogate reads it from the export, so nothing changes; the flag is now a
constraint, not a question.
Deliberate simplification, or collateral? (Note `GetTotalPeopleCount` was simultaneously fixed to
count heads rather than workforce points, B27, which only matters if some building turns the flag
back on.)

### E.5 Overnight cancellation of food deliveries

`TaskSystem.CancelIncompleteFoodDeliveries` cancels every food delivery still queued or in
transit when round 4 ends and fails the parent task ("Food cannot be delivered overnight").
B37 already fixed the case where the cargo had landed. Remaining question: a request generated at
round 3 whose delivery cannot physically arrive before the day ends is a guaranteed failure —
should such a request be generated at all (that is what `TaskData.latestGenerationRound` exists
for), or should the delivery survive the night?

*Changes:* whether a rational agent should ever answer a late-day food request; the penalty
distribution in benchmark and RL runs.

### E.6 Food requests only from day 2

Both `*_FoodRequest_First/Second` carry `dayTriggers: conditionType: 3, startDay: 2`, and the
community depletion manager has `firstEligibleDay = 2`. Day 1 therefore has no food demand of any
kind. Intended tutorial ramp, or an artefact of the day-1 rework (C.10)?

### E.7 Kitchens: fill-to-capacity replaces production

`Kitchen.prefab` `fillFoodToCapacityDaily: 1`; the whole `roundProduction` mechanism is deleted.
A kitchen is now a daily bucket of `initialKitchenFoodCapacity` (sheet: 200) that resets each
morning, and `initialKitchenCapacity` has no consumer any more. Confirm 200/day/kitchen is the
intended supply ceiling — with a shelter of 100 people eating twice a day, one kitchen feeds one
full shelter and nothing else.

### E.8 Self-walk relocation

`ClientRelocationHandler.QueueSelfWalk` — population moves without a vehicle, departing
immediately and arriving after `relocationDelayRounds` (default 2, Inspector-configurable, not in
the sheet); vehicles are food-only now. Questions: is the delay meant to be uniform regardless of
distance or flooding? Should a walk be blocked or delayed by flooded roads (deliveries are
flood-aware, walks are not)? And is `relocationDelayRounds` meant to be a sheet parameter?

*Changes:* the surrogate's transfer model (currently instantaneous-at-round-end), and the RL
action semantics for relocation.

### E.9 The shipped parameter sheet starts satisfaction at 0 — this breaks headless runs

`Assets/StreamingAssets/game_param_config.csv` (merged in with `d5e5f683`) has
`initialSatisfaction,0` (its own "default" column says 50) and `initialBudget,8000` (default
10000). Since `3d8c8b00` the sheet drives every build, including headless.

`arc_game_gym_env_tcp.py:684` terminates an episode on `satisfaction <= 0`, so **every** gym /
benchmark episode now ends after round 1 with reward 0. Verified on this build: with the sheet as
committed, 1 round played, `terminated: true`; with `ARC_PARAM_CONFIG` pointing at the same sheet
edited to `initialSatisfaction,50`, the episode runs normally.

Question: is satisfaction 0 the intended start for play-testing (in which case the gym's
termination rule needs to change — e.g. terminate only on a *drop* to 0 after round 1, or on
`isGameOver`), or is the committed value a leftover from a local test? Nothing has been edited
here pending the answer.

### E.12 `MainScene.unity` on main-bugfixes reverted the Motel's food mechanic

**Where.** `Assets/Scenes/MainScene.unity` at `origin/main-bugfixes` (48a2582f), the Motel's
`BuildingResourceStorage`.

**What happens.** `d5e5f683` says "Motel: added FoodPacks storage/consumption (it previously had
none) and a matching pair of food-request tasks, mirroring Shelter". The scene on that branch now
carries `enablePopulationBasedConsumption: 0` and NO FoodPacks capacity entry at all — the
6000-pack store is gone; only `Population 3000` remains. Our pre-merge scene (34d2133d) has
`consumption: 1` and `caps [(Population, 3000), (FoodPacks, 6000)]`.

The scene is LFS-tracked, so a merge takes one side of the whole file with no 3-way. A later
scene edit made from a checkout without the motel change therefore reverts it silently — the same
hazard that deactivated `WebSocketManager` in a9bf1135, and `MainScene.unity` on that branch now
also ships `WebSocketManager` with `m_IsActive: 0` (LLM officers never connect).

**Why it is not cosmetic.** `BuildingResourceStorage.GetFoodNeed()` is `population x rate - stock`
and never checks `enablePopulationBasedConsumption`, so `Motel_FoodRequest_First/Second` still fire
on their `NeedsFood` trigger for a motel that cannot hold a single pack. Every one of those tasks
is unwinnable and expires Incomplete with its penalty (satisfaction -1, budget +1 each).

**Fix applied here.** Restored on our merge: consumption on, FoodPacks capacity 6000, and
`WebSocketManager` re-activated. Upstream should re-apply both on their side, or the next scene
edit will revert them again.

**Scope / status.** CONFIRMED (read off both scenes). Needs an upstream fix, not just ours.

### E.11 Per-facility worker counts are never serialised

`GameStateStructures.cs:156-157` declares `ResourceInventory.trainedWorkers/untrainedWorkers`;
the only place a facility's `resources` block is built (`TaskSystem.cs:2982-2988`) sets food and
population and nothing else, so both read 0 for every building in every observation. Nothing
downstream reads them (`obs_encoder`, router, prompts, tools all use `assignedWorkforce` and the
global `workforceState`, which are correct), so agents were not misled — but the fields are dead
weight in the payload and the per-building mix is genuinely unavailable to a consumer that wanted
it. Populate from `WorkerSystem.GetWorkersByBuildingId`, or remove the fields.

### E.10 Smaller items

- **`[food_amount]`** is resolved from `GameTask.foodAmount`, a snapshot taken at task creation
  (`TaskSystem`), while the delivered amount is recomputed live by
  `FoodDeliveryHandler.ResolveQuantity`. The number the officer/agent reads can therefore differ
  from the number delivered. Intended?
- **Immediate ("Rapid Response Vehicle", $1000, 100 meals)** still debits no kitchen (B15/C.7).
  Now that kitchens are a fixed daily bucket, is external supply meant to be the release valve?
- **Motel food capacity is 6000** (`MotelPrefab.prefab` `resourceCapacities`), not sheet-driven,
  while shelter/kitchen food capacities are. Intended asymmetry?
- **Flood road-blockage (food)** is now a single immediate-delivery choice priced per meal, and
  vehicle repair's "delay" choice lost its satisfaction penalty. Confirm both are final.
