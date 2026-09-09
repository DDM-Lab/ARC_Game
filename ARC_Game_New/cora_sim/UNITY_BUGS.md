# Unity game bugs found by the surrogate (v1_testing bug hunt)

> Colleague-facing write-ups (where / example / intended / patch) for these and ~35 further findings
> from the 2026-09-09 audit live in `docs/BUG_REPORTS_v1_testing.md` (A1-A12 map to entries here:
> A1=10 (+ the food-waste-before-consumption consequence), A2=2, A3=3, A4=4, A5=5, A6=6, A7=7, A8=8,
> A9=1, A10=9, A11=11, A12=12). This file stays the surrogate-side index.
> 2026-09-09: all of these are FIXED on branch `v1_fixes` (see the Fix status table there). The
> surrogate still reproduces the OLD behaviour until it is re-derived; see STATUS.md.

Each entry: what the game does, where in the C# it happens, how it was observed, and what the
surrogate does about it. The surrogate reproduces every one of these on purpose -- it must match
the game as shipped -- so fixing one in Unity means changing the port and re-capturing. Evidence
paths are under `cora_sim/runs/` (headless captures with `ARC_SNAPSHOT_DEBUG=1`).

## Found 2026-09-09 on the merged build (v1_testing)

1. **The headless game runs with an Emergency task cap of 0, so no database Emergency task is
   ever generated.** Two defects combine:
   (a) `GameDataManager.SetDefaults()` assigns `defaultEmergencyTaskFrequency` (4) to
   `InitialExternalRelationFrequency` and never sets `InitialEmergencyTaskFrequency`, which stays 0
   (the external-relation frequency is silently 4 instead of 3 at the same time).
   (b) MainScene's GameDataManager has its `configLoader` reference unwired (`configLoader:
   {fileID: 0}` in MainScene.unity; the TutorialScene copy is wired to its loader). The headless
   build loads MainScene directly, so `LoadAllData` takes the `SetDefaults()` branch at Awake --
   "GameDataManager: External config disabled or missing loader. Using Hardcoded Defaults." is
   logged BEFORE MainScene's GameConfigLoader even starts its fetch (log lines 71 vs 187) -- and
   every `Initial*` value in headless comes from the `default*` fields, never from the sheet or
   the loader fallbacks, whether or not the fetch succeeds.
   Result: `TaskSystem.numEmergencyTasks` = 0 and `currEmergencyTaskCount >= numEmergencyTasks`
   is true from the first generation pass, so Emergency Budget Crisis, Shelter Flood Damage and
   Community Emergency Evacuation are `[Limit] Skipping ...: Max emergencies reached` for the
   whole game. Evidence: `runs/validate_v1/staff_5901.log` (38 skips, 0 creations).
   Surrogate: `_NUM_EMERGENCY_TASKS = 0` (sim.py). The WebGL client enters through TutorialScene,
   whose GameDataManager is wired (DontDestroyOnLoad, the MainScene copy self-destroys), so it
   reads the loader: sheet value if the fetch succeeds, else the loader's fallback (4). NOT yet
   confirmed from a WebGL log.

12. **Editor-only: the server launcher's text boxes take no keyboard input.** Clicking the URL or
   API-key box throws `IndexOutOfRangeException` in `TMP_TextUtilities.FindNearestCharacterOnLine`
   (via `TMP_InputField.OnPointerDown`, TMP 3.0.7) before the field activates, so typing is neither
   captured nor rendered. The fields are built at runtime by `ServerLauncherUI.MakeInput`; the caret
   lookup runs before the field's text has ever been laid out. WebGL never hits it because the key is
   entered through a DOM overlay there. The pre-filled defaults (ws://localhost:9876/ws, dev-local-key)
   are used correctly, so the editor still connects. Seen 2026-09-09 in the editor on v1_testing.

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
6. Construction advertises $1000 in the action list and deducts $2000. Still so on the merged
   build: `runs/validate_v1/staff_5503.log` lines 1477-1490 show "Recorded budget change: -2000 -
   Construction Cost for CaseworkSite" next to "Built CaseworkSite at site 14 (cost: $1000)".
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
