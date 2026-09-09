# Unity game bugs found by the surrogate (v1_testing bug hunt)

Each entry: what the game does, where in the C# it happens, how it was observed, and what the
surrogate does about it. The surrogate reproduces every one of these on purpose -- it must match
the game as shipped -- so fixing one in Unity means changing the port and re-capturing. Evidence
paths are under `cora_sim/runs/` (headless captures with `ARC_SNAPSHOT_DEBUG=1`).

## Found 2026-09-09 on the merged build (v1_testing)

1. **No database Emergency task can ever be generated when the config sheet is unreachable.**
   `GameDataManager.SetDefaults()` assigns `defaultEmergencyTaskFrequency` (4) to
   `InitialExternalRelationFrequency` and never sets `InitialEmergencyTaskFrequency`, which stays 0.
   `TaskSystem.numEmergencyTasks` becomes 0 and `currEmergencyTaskCount >= numEmergencyTasks` is
   true from the first generation pass, so Emergency Budget Crisis, Shelter Flood Damage and
   Community Emergency Evacuation are all `[Limit] Skipping ...: Max emergencies reached` for the
   whole game. Every headless run hits this (the `/sheet.csv` fetch fails offline); the deployed
   WebGL client fetches the sheet and runs with 2. Evidence: `runs/validate_v1/staff_5901.log`
   (38 skips, 0 creations; "GameDataManager: External config disabled or missing loader. Using
   Hardcoded Defaults."). Surrogate: `_NUM_EMERGENCY_TASKS = 0` (sim.py). Also means the
   external-relation frequency is silently 4 instead of 3 under defaults.

## Reproduced since the first calibration (see STATUS.md "Unity bugs reproduced on purpose")

2. Population deliveries register the client group twice (`Vehicle.UnloadCargo ->
   HandlePopulationDelivery` with the actual count, and the legacy branch in
   `DeliverySystem.OnVehicleDeliveryCompleted` with the nominal count).
3. Casework-site deliveries remove clients from the source twice (unload actual, then completion
   nominal), crediting `caseworkProcessed` twice.
4. `FloodTaskGenerator.HandleDeliveryFailure` closes a flood-stopped task without
   `RecordTaskResolution` -- the demand is never counted -- and `RemoveActiveDeliveryTask` then
   fires `OnTaskCompleted`, so the stranded cargo is credited as a LATE DELIVERY
   (`lodgingFulfilled` rises with nobody housed).
5. Emergency lodging eviction records nothing in the reward metrics.
6. Construction advertises $1000 in the action list and deducts $2000.
7. `caseworkRequested` credits the whole client group, not the members flagged as needing casework.
8. Coroutine race after a load abort: `AssignDeliveryTask` never stops the old `ExecuteDeliveryTask`
   coroutine, so two coroutines advance one `currentPathIndex`; the source leg is skipped, the
   destination leg runs at two cells a frame, and the cargo is whatever the old coroutine loaded.
9. Departed (caseworkless) clients keep being billed at the motel: `OnCaseworklessClientsDeparted`
   has no subscribers and motel population only drops through `LoadCargo`.
10. Segment 4 of every day fires no `OnTimeSegmentChanged` (`GlobalClock.AdvanceTimeSegment`
    returns early at `roundsPerDay`), so ageing, the client tracker, generation and consumption
    all skip the last round of each day.
11. Road Blockage Emergency: the food-cargo and not-yet-loaded choices do nothing
    (`FindTriggeringFacility` fails for the code-built task); only the loaded-population
    choice acts.
