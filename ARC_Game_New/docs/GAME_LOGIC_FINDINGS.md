# Game-logic soundness audit — population relocation & housing-demand satisfaction

Date: 2026-06-16. Scope: does selecting a relocation choice actually house people, and does
"lodging demand" get satisfied when deliveries complete? Verdict: **the fulfillment metric is
disconnected from physical housing in both directions.** Physical population movement and
demand-satisfaction accounting are two systems that do not intersect.

## Evidence (from rollout data)
- **greedy** (picks immediate $3000 "Helicopter Evacuation to Shelters"): community pop = flat
  **120 every round**, motel = 0, **no shelters ever built** — *nobody physically moves* — yet
  `lodgingFulfilled ≈ 13.6` (70%). Fulfillment credited with zero relocation.
- **opus** (picks 100% free/deferred "Send to Motel"/"Send to Shelters"): community pop drains
  120→0 while motel fills 0→120 (population *is* conserved & moved) — yet `lodgingFulfilled = 0`.
  Real housing, zero fulfillment credit.

## Mechanism (from code)
1. **Immediate choices credit fulfillment unconditionally.** `TaskDetailUI.CompleteTaskAction`
   (`TaskDetailUI.cs:690-693`): `if (immediateDelivery) { ExecuteGeneratorDelivery(immediate:true);
   CompleteTask(currentTask); }`. `ClientRelocationHandler.ExecuteImmediate` returns `void` and
   moves 0 people when no operational shelter/motel destination exists — but `CompleteTask` runs
   regardless → `RecordTaskResolution(fulfilled:true)` → `lodgingFulfilled++` (`TaskSystem.cs:1079`,
   `RewardMetricsTracker.cs:65-68`). **→ greedy's no-op fulfillment.**

2. **Deferred relocation deliveries rarely complete.** `if (triggersDelivery) ExecuteGeneratorDelivery(
   immediate:false)` (`TaskDetailUI.cs:695-698`) → `ClientRelocationHandler.Execute` queues a vehicle
   delivery linked to the task. Completion path IS correctly wired: `Vehicle:563 OnDeliveryCompleted →
   DeliverySystem:464/476 OnTaskCompleted → TaskSystem:418/571 OnDeliveryTaskCompleted →
   :604 CompleteTask` (fulfilled++). But empirically these deliveries seldom finish before the
   short-fuse task expires (one shared population vehicle, roundsLeft≈1) → tasks expire unfulfilled.
   **→ opus's deferred picks score 0.**

3. **The reliable physical mover bypasses demand entirely.** opus relocates people via the
   agent-facing `transfer_population` action (`action_enumerator.py:427`), which moves
   community→motel directly with **no parent demand task**. When such a delivery completes,
   `OnDeliveryTaskCompleted` finds no parent (`TaskSystem.cs:588-592`) and credits nothing — but the
   people are now in the motel, triggering the **$200/person/day** `MotelCostManager` charge.
   **→ opus fills the motel, pays the daily fee, and gets 0 fulfillment.**

4. **The intended auto-housing system is dead code.** `DeliverySystem.GeneratePopulationTransportTasks`
   (community→shelter/motel auto-relocation) is only reachable via `GenerateAutoTasks`, called solely
   from the `[ContextMenu] GenerateTestTask` editor method (`DeliverySystem.cs:755-758`). It never runs
   in gameplay/headless. So there is no background process actually housing displaced people.

## Answers to the two questions
- **Are people actually moved by relocation tasks?** Only sometimes: immediate choices move people
  *iff* a staffed destination exists (else 0, but still "complete"); deferred choices move people
  *iff* the vehicle finishes in time (usually it doesn't). The dependable movement in practice is the
  `transfer_population` action, which is not a relocation *task* at all.
- **Is housing demand satisfied when deliveries complete?** The linkage is wired correctly, but it
  fires rarely (deferred deliveries expire) and is bypassed by both the unconditional immediate path
  and the unlinked `transfer_population` path. "Satisfaction" is a task flag, never a check that the
  displaced population was actually housed.

## Suggested fixes (Unity; require rebuild — get user go-ahead)
- Credit fulfillment by **delivered quantity**, not choice click: in `ExecuteImmediate` return the
  count moved and gate `CompleteTask`/`RecordTaskResolution` on `delivered > 0` (and ideally
  `lodgingFulfilled += delivered`, `lodgingResolved += demand`).
- Link `transfer_population` (or relocation in general) to the open lodging demand for that community
  so physically housing people satisfies the demand.
- Either revive `GeneratePopulationTransportTasks` in the round loop, or remove the dead path.
- Reconcile the short task fuse vs. single-vehicle throughput so deferred relocations can complete.

## FIXES APPLIED (2026-06-16) — pending rebuild + verification
People-based fulfillment + demand-linked relocation. Files touched:
- **GameTask** (`TaskSystem.cs`): added `demandQuantity` / `deliveredQuantity`. Demand set at
  creation for Food/Lodging tasks (`CreateTaskFromData`) = largest delivery-choice quantity.
- **RewardMetricsTracker**: `RecordTaskResolution` now credits `resolved += demandQuantity`,
  `fulfilled += min(deliveredQuantity, demand)` (B1/B2 — people housed, not choices clicked);
  added `AddLateDelivery` for deliveries that land after a task closed (D4).
- **ClientRelocationHandler**: `ExecuteImmediate` returns people moved + accumulates onto the task;
  added `HasDestinationSpace` for choice gating (B5).
- **TaskDetailUI**: `CompleteTaskAction` gates immediate completion on `moved != 0` (B1);
  `ExecuteGeneratorDelivery`/`ExecuteClientRelocation` return the moved count.
- **TaskSystem.OnDeliveryTaskCompleted**: accumulates delivered population onto the parent task;
  credits late deliveries. **GetCurrentGameState**: hides relocation choices with no destination
  space (B5/D5). **OnRoundChanged**: calls background housing (B4).
- **ActionExecutor.ExecuteTransfer**: links a `transfer_population` to the community's open lodging
  task so it satisfies demand (B3/D4).
- **DeliverySystem**: revived `GeneratePopulationTransportTasks` as `RunBackgroundHousing` (B4,
  `enableAutoTasks=true`), gated to communities that have an open lodging demand (no draining
  healthy communities), and links its transports to that demand (D4).
- **Assets** (Community_TransportRequest, Community_Flood_Damge, Shelter_Flood_Damage): fuse
  `roundsRemaining 2→4` (D3); motel choices surface "then $200/person/day ongoing" (D2). Live-asset
  satisfaction was already balanced (the inversion was in dead test code), so no rebalance needed.

## Git history verdict — it never worked, it was not overwritten
- **Unconditional immediate completion was present from the first version** (`f585e9ea`): the original
  immediate-delivery path physically transferred via `RemoveResource`/`AddResource` *and* called
  `CompleteTask` unconditionally. The `1e63007c` "fix" reordered the immediate/deferred branches and
  refactored relocation into `ClientRelocationHandler`, but **kept completion unconditional**. So the
  no-op fulfillment (finding 1) is an original design flaw, not a regression.
- **Population-transfer primitives have always worked**: `AddPopulation`/`RemovePopulation` physically
  move people and have since `db121d58` (2025-07-01). Movement was never the broken part.
- **Stale relocation tasks resolve as fulfilled:false** via `ExpireTask` (`TaskSystem.cs:1162-1178`,
  introduced/auto-resolve `4479a4af`): status→Incomplete, `ApplyTaskPenalties` docks satisfaction/budget,
  `RecordTaskResolution(fulfilled:false)` bumps `lodgingResolved` only — matching opus's 0/N.
- **Fulfillment is never quantity-weighted**: `CompleteTask` (`TaskSystem.cs:1079`) passes
  `fulfilled:true` regardless of how many people the delivery actually moved.

**Bottom line:** the *movement* layer is sound and always has been; the *linkage* between physical
housing and demand-credit was never correct. Nothing here is a recoverable past version to restore —
the fixes have to be written fresh.
