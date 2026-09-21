using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

/// <summary>
/// Handles client (Population) relocation for task choices.
/// Source = task's requesting facility (always known from trigger).
/// Destinations = shelters and/or motels, filled largest-effective-space-first.
/// Accounts for in-flight reservations so we never over-promise space.
///
/// Clients relocate under their own mobility — no Vehicle is ever used here
/// (vehicles are reserved for food delivery). The queued relocation path
/// (Execute / ExecuteToSpecificDestination) has clients depart immediately and
/// arrive after a configurable number of rounds; the parent task is marked
/// Completed as soon as the decision is queued rather than waiting for that
/// arrival, since the task's own time limit can otherwise expire mid-walk and
/// wrongly auto-fail a task the player already responded to. The
/// immediate/emergency path (ExecuteImmediate) is unaffected and stays a
/// zero-delay teleport.
///
/// Example: Community B has 30 ppl.
///   Shelter 1: 20/20 (full)         → skip
///   Shelter 2: 10/20 (10 space)     → send 10
///   Shelter 3:  0/20 (20 space)     → send 20
/// </summary>
public class ClientRelocationHandler : MonoBehaviour
{
    [Header("Destination Priority")]
    [Tooltip("Prefer shelters over motels when both have space.")]
    public bool preferShelters = true;

    [Header("Self-Walk Relocation Timing")]
    [Tooltip("Number of rounds after the relocation decision before clients arrive at their destination (self-walk, no vehicle).")]
    public int relocationDelayRounds = 2;

    [Header("Debug")]
    public bool showDebugInfo = true;

    public static ClientRelocationHandler Instance { get; private set; }

    // In-flight self-walk relocations, keyed by nothing (small list, scanned linearly).
    public class PendingRelocation
    {
        public GameTask parentTask;
        public MonoBehaviour source;
        public MonoBehaviour destination;
        public int quantity;
        public int roundsRemaining;
        public string groupName;
    }

    /// <summary>Raised when clients depart on foot — mirrors DeliverySystem.OnTaskCreated so UI (e.g. the delivery queue panel) can list them.</summary>
    public event Action<PendingRelocation> OnRelocationQueued;
    /// <summary>Raised when self-walking clients arrive at their destination — mirrors DeliverySystem.OnTaskCompleted.</summary>
    public event Action<PendingRelocation> OnRelocationArrived;

    private readonly List<PendingRelocation> pendingRelocations = new List<PendingRelocation>();

    /// <summary>Snapshot of all clients currently self-walking (departed, not yet arrived). For UI display.</summary>
    public List<PendingRelocation> GetPendingRelocations() => new List<PendingRelocation>(pendingRelocations);

    // ── save / restore ────────────────────────────────────────────────────────────────
    //
    // THE PEOPLE IN THIS LIST ARE NOWHERE ELSE. QueueSelfWalk is called AFTER the caller has
    // already removed the population from the source's storage, so an in-flight walk exists
    // only here: not at the source, not yet at the destination. A snapshot that skips it
    // loses those clients outright, and since the walk is `relocationDelayRounds` (2) long
    // and a save is taken during the planning pause, having one in flight is ordinary rather
    // than a corner case.
    //
    // References are stored by NAME and by task ID because the objects they point at do not
    // survive the scene rebuild a restore performs.
    [System.Serializable]
    public class Snapshot
    {
        [System.Serializable]
        public class Walk
        {
            public int parentTaskId = -1;
            public string sourceName;
            public string destinationName;
            public int quantity;
            public int roundsRemaining;
            public string groupName;
        }
        public List<Walk> walks = new List<Walk>();
    }

    public Snapshot CaptureState()
    {
        var s = new Snapshot();
        foreach (var r in pendingRelocations)
        {
            if (r == null) continue;
            s.walks.Add(new Snapshot.Walk
            {
                parentTaskId    = r.parentTask != null ? r.parentTask.taskId : -1,
                sourceName      = r.source != null ? r.source.name : null,
                destinationName = r.destination != null ? r.destination.name : null,
                quantity        = r.quantity,
                roundsRemaining = r.roundsRemaining,
                groupName       = r.groupName,
            });
        }
        return s;
    }

    /// <summary>Restore in-flight walks. Must run AFTER TaskSystem.RestoreState, or the
    /// parent lookup finds nothing and an arriving group cannot resolve its task.</summary>
    public void RestoreState(Snapshot s)
    {
        pendingRelocations.Clear();
        if (s == null || s.walks == null) return;
        foreach (var w in s.walks)
        {
            if (w == null) continue;
            MonoBehaviour src = FindFacility(w.sourceName);
            MonoBehaviour dst = FindFacility(w.destinationName);
            if (dst == null)
            {
                // Without a destination the walk can never land, and keeping it would hold
                // its clients in limbo for the rest of the game. Drop it loudly instead.
                Debug.LogWarning($"[ClientRelocationHandler] restore: dropping walk of {w.quantity} "
                               + $"to missing destination '{w.destinationName}'");
                continue;
            }
            pendingRelocations.Add(new PendingRelocation
            {
                parentTask      = w.parentTaskId >= 0 && TaskSystem.Instance != null
                                    ? TaskSystem.Instance.GetTaskById(w.parentTaskId) : null,
                source          = src,
                destination     = dst,
                quantity        = w.quantity,
                roundsRemaining = w.roundsRemaining,
                groupName       = w.groupName,
            });
        }
    }

    static MonoBehaviour FindFacility(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        GameObject go = GameObject.Find(name);
        if (go == null) return null;
        MonoBehaviour mb = go.GetComponent<Building>();
        if (mb == null) mb = go.GetComponent<PrebuiltBuilding>();
        return mb;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void OnEnable()
    {
        GlobalClock.OnRoundEnd += HandleRoundEnd;
    }

    void OnDisable()
    {
        GlobalClock.OnRoundEnd -= HandleRoundEnd;
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC: VALIDATION
    // ─────────────────────────────────────────────────────────────────

    public bool CanExecute(GameTask parentTask, int requestedQuantity,
                           bool includeShelters, bool includeMotels,
                           out string errorMessage, bool requiresPathCheck = true)
    {
        errorMessage = "";

        MonoBehaviour source = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (source == null)
        {
            errorMessage = $"Cannot find source facility '{parentTask.affectedFacility}'";
            return false;
        }

        int available = GetPopulation(source);
        if (available <= 0)
        {
            errorMessage = $"No clients at {source.name} to relocate";
            return false;
        }

        DeliverySystem ds = DeliverySystem.Instance;

        // Immediate delivery is a teleport — skip path check. Self-walk relocation requires a reachable road.
        int totalEffectiveSpace = GetDestinationsSorted(ds, includeShelters, includeMotels, source, filterByPath: requiresPathCheck)
            .Sum(d => d.effectiveSpace);

        string destLabel = includeShelters && includeMotels ? "shelter/motel"
                         : includeShelters ? "shelter" : "motel";

        if (totalEffectiveSpace <= 0)
        {
            // Covers three different causes GetDestinationsSorted can't tell apart from a single
            // count: no {destLabel} building exists at all, one exists but is completely full, or
            // one exists with space but every route to it is blocked — "no accessible X" wrongly
            // implied the third specifically. Naming the possibilities instead of asserting one.
            errorMessage = $"No {destLabel} is reachable: either none exist on the map, every route is blocked, or none have space.";
            return false;
        }

        // Must have room for the FULL requested amount across all eligible destinations combined
        // (Execute() is allowed to split one request across several shelters/motels — see the
        // class doc's example — so requiring a single destination to hold everyone would be wrong
        // here). Without this, a request for more people than the map can currently take would
        // silently pass validation and Execute() would relocate only as many as fit, completing
        // the task and leaving the rest behind with no warning shown to the player.
        int toSend = requestedQuantity > 0 ? Mathf.Min(requestedQuantity, available) : available;
        if (totalEffectiveSpace < toSend)
        {
            errorMessage = $"Only {totalEffectiveSpace} of {toSend} clients could be placed: not enough accessible {destLabel} capacity on the map.";
            return false;
        }

        return true;
    }

    /// <summary>D5/B5 gate: is there any free space at the choice's destination(s) right now?
    /// Source-population + destination-space only (no vehicle check — immediate teleport needs no
    /// vehicle, and vehicle availability is transient). Used to hide relocation choices that
    /// physically can't house anyone.</summary>
    public bool HasDestinationSpace(GameTask parentTask, bool includeShelters, bool includeMotels)
    {
        MonoBehaviour source = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (source == null) return false;
        if (GetPopulation(source) <= 0) return false;
        DeliverySystem ds = DeliverySystem.Instance;
        if (ds == null) return false;
        return GetDestinationsSorted(ds, includeShelters, includeMotels, source).Sum(d => d.effectiveSpace) > 0;
    }

    /// <summary>Can a relocation choice actually be carried out right now? Returns false + a
    /// human-readable reason when not — used to disable the choice and tell the player why.
    /// Immediate (helicopter) only needs destination space (it teleports). Deferred (road) also
    /// needs an undamaged population vehicle AND a flood-free route to a destination with space.</summary>
    public bool CheckFeasibility(GameTask parentTask, bool includeShelters, bool includeMotels,
                                 bool immediate, out string reason)
    {
        reason = "";
        MonoBehaviour source = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (source == null) { reason = "Source facility not found"; return false; }
        if (GetPopulation(source) <= 0) { reason = "No residents left to relocate"; return false; }

        DeliverySystem ds = DeliverySystem.Instance;
        if (ds == null) { reason = "Delivery system unavailable"; return false; }

        var dests = GetDestinationsSorted(ds, includeShelters, includeMotels, source);
        string destLabel = includeShelters && includeMotels ? "shelter/motel"
                         : includeShelters ? "shelter" : "motel";
        if (dests.Sum(d => d.effectiveSpace) <= 0) { reason = $"No space available at any {destLabel}"; return false; }

        // Immediate teleport needs no road or vehicle — space is enough.
        if (immediate) return true;

        // Deferred road delivery needs an undamaged population-capable vehicle...
        bool hasVehicle = FindObjectsOfType<Vehicle>()
            .Any(v => v.GetAllowedCargoTypes().Contains(ResourceType.Population)
                   && v.GetCurrentStatus() != VehicleStatus.Damaged);
        if (!hasVehicle) { reason = "No undamaged vehicle available"; return false; }

        // ...and a flood-free route to at least one destination that has space.
        foreach (var (dest, space) in dests)
        {
            if (space <= 0) continue;
            if (ds.CanCreateDeliveryWithEstimate(source, dest, out _)) return true;
        }
        reason = $"All routes to the {destLabel} are blocked by flooding";
        return false;
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC: QUEUED (self-walk) RELOCATION
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clients depart their source immediately under their own mobility and arrive at
    /// their destination(s) after <see cref="relocationDelayRounds"/> rounds. No Vehicle
    /// is used — vehicles are reserved for food delivery. Returns true if at least one
    /// relocation was scheduled.
    /// </summary>
    public bool Execute(GameTask parentTask, int requestedQuantity,
                        bool includeShelters = true, bool includeMotels = false)
    {
        MonoBehaviour source = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (source == null)
        {
            Debug.LogError($"[ClientRelocationTaskGenerator] Cannot find source for '{parentTask.taskTitle}'");
            return false;
        }

        DeliverySystem ds = DeliverySystem.Instance;

        int available = GetPopulation(source);
        int toSend    = requestedQuantity > 0 ? Mathf.Min(requestedQuantity, available) : available;

        if (toSend <= 0)
        {
            Debug.LogWarning($"[ClientRelocationTaskGenerator] No clients to relocate from {source.name}");
            return false;
        }

        var destinations = GetDestinationsSorted(ds, includeShelters, includeMotels, source, filterByPath: true);
        if (destinations.Count == 0)
        {
            Debug.LogWarning($"[ClientRelocationTaskGenerator] No available destinations for '{parentTask.taskTitle}'");
            return false;
        }

        int remaining   = toSend;
        bool anyCreated = false;

        foreach (var (dest, effectiveSpace) in destinations)
        {
            if (remaining <= 0) break;

            int sendAmount = Mathf.Min(remaining, effectiveSpace);
            if (sendAmount <= 0) continue;

            int removed = RemovePopulation(source, sendAmount);
            if (removed <= 0) continue;

            QueueSelfWalk(parentTask, source, dest, removed);

            remaining  -= removed;
            anyCreated  = true;
        }

        if (anyCreated)
        {
            // Mark the task finished as soon as the decision is queued, not once clients
            // physically arrive — self-walk takes relocationDelayRounds rounds, and the task's
            // own time limit can expire before that arrival happens (e.g. a 2-round task
            // decided on round 1, with a 2-round walk still ahead of it), which was wrongly
            // auto-failing tasks the player had already responded to. The walk itself is
            // unaffected and continues in the background (see HandleRoundEnd/FinalizeRelocation).
            TaskSystem.Instance.CompleteTask(parentTask);
        }
        else
            Debug.LogWarning($"[ClientRelocationTaskGenerator] Could not create any relocations for '{parentTask.taskTitle}'");

        return anyCreated;
    }

    /// <summary>
    /// Self-walk relocation to a single, specific destination building (e.g. Shelter → CaseworkSite).
    /// Used by task choices that target a specific building rather than searching shelters/motels
    /// for space (see TaskDetailUI.ExecuteFallbackDelivery). No Vehicle is used.
    /// </summary>
    public bool ExecuteToSpecificDestination(GameTask parentTask, MonoBehaviour source, MonoBehaviour destination, int requestedQuantity)
    {
        if (source == null || destination == null) return false;

        // Clients still walk along roads — a route blocked by flooding blocks them too.
        DeliverySystem ds = DeliverySystem.Instance;
        DeliveryTimeEstimate pathEstimate;
        if (ds != null && !ds.CanCreateDeliveryWithEstimate(source, destination, out pathEstimate))
            return false;

        int available = GetPopulation(source);
        int toSend    = requestedQuantity > 0 ? Mathf.Min(requestedQuantity, available) : available;
        // Never send more people than the destination can still take (capacity minus vehicle
        // reservations minus clients already walking there). Before the walk mechanic the
        // multi-delivery validator enforced this (B13); a walk that bounces at arrival would
        // return people to a source the stay tracker has already discharged them from.
        int space = GetEffectiveSpace(destination);
        if (space <= 0)
        {
            SnapshotDebug.MarkContext("relocation:refused", "{\"task\":" + (parentTask != null ? parentTask.taskId : -1)
                + ",\"dst\":\"" + destination.name + "\",\"reason\":\"full\"}");
            return false;
        }
        toSend = Mathf.Min(toSend, space);
        if (toSend <= 0) return false;

        int removed = RemovePopulation(source, toSend);
        if (removed <= 0) return false;

        QueueSelfWalk(parentTask, source, destination, removed);
        // See the matching comment in Execute() above — completed immediately on decision,
        // not on arrival, so the task's own time limit can't race the self-walk.
        TaskSystem.Instance.CompleteTask(parentTask);
        return true;
    }

    /// <summary>
    /// Removes departing clients from the source's stay tracking and schedules their arrival
    /// after <see cref="relocationDelayRounds"/> rounds. Assumes population has already been
    /// removed from the source's resource storage by the caller.
    /// </summary>
    void QueueSelfWalk(GameTask parentTask, MonoBehaviour source, MonoBehaviour destination, int quantity)
    {
        if (ClientStayTracker.Instance != null)
            ClientStayTracker.Instance.HandleSelfWalkDeparture(source, destination, quantity, parentTask);

        PendingRelocation relocation = new PendingRelocation
        {
            parentTask      = parentTask,
            source          = source,
            destination     = destination,
            quantity        = quantity,
            roundsRemaining = Mathf.Max(1, relocationDelayRounds),
            groupName       = $"Relocate_{(parentTask != null ? parentTask.taskId : 0)}_{source.name}_to_{destination.name}"
        };
        pendingRelocations.Add(relocation);
        SnapshotDebug.MarkContext("relocation:queue", "{\"task\":" + (parentTask != null ? parentTask.taskId : -1) + ",\"qty\":" + quantity
            + ",\"src\":\"" + source.name + "\",\"dst\":\"" + destination.name + "\",\"rounds\":" + relocation.roundsRemaining + "}");

        if (showDebugInfo)
            Debug.Log($"[ClientRelocationHandler] {quantity} clients departing {source.name} → {destination.name} on foot, arriving in {relocationDelayRounds} round(s)");
        GameLogPanel.Instance?.LogTaskEvent($"Client relocation for task '{parentTask?.taskTitle ?? "manual transfer"}': {quantity} clients departing {source.name} -> {destination.name}, arriving in {relocationDelayRounds} round(s)");

        OnRelocationQueued?.Invoke(relocation);
    }

    /// <summary>
    /// Called once per round (GlobalClock.OnRoundEnd). Advances all in-flight self-walk
    /// relocations and finalizes any that have arrived.
    /// </summary>
    void HandleRoundEnd()
    {
        if (pendingRelocations.Count == 0) return;

        List<PendingRelocation> arrived = null;
        foreach (var r in pendingRelocations)
        {
            r.roundsRemaining--;
            if (r.roundsRemaining <= 0)
            {
                arrived ??= new List<PendingRelocation>();
                arrived.Add(r);
            }
        }

        if (arrived == null) return;

        foreach (var r in arrived)
        {
            pendingRelocations.Remove(r);
            FinalizeRelocation(r);
        }
    }

    void FinalizeRelocation(PendingRelocation r)
    {
        int delivered = AddPopulation(r.destination, r.quantity);
        // People who actually arrived are what the lodging metric credits (RewardMetricsTracker
        // reads task.deliveredQuantity); a vehicle unload used to set this, a walk must too.
        //
        // But writing deliveredQuantity is not enough on THIS path. Execute/
        // ExecuteToSpecificDestination complete the parent task at DECISION time (see the
        // comment there: a 2-round walk would otherwise race the task's own time limit), so
        // RecordTaskResolution has already run — and it read deliveredQuantity == 0. It
        // credited demand to lodgingResolved and nothing to lodgingFulfilled. Without the
        // retroactive call below, every self-walk relocation scores zero fulfillment no matter
        // how it goes, and the lodging term of the RL reward is pinned at 0.
        // This mirrors what the vehicle path already does (TaskSystem.OnDeliveryTaskCompleted).
        if (r.parentTask != null && delivered > 0)
        {
            r.parentTask.deliveredQuantity += delivered;
            RewardMetricsTracker.Instance?.AddLateDelivery(r.parentTask, delivered);
        }

        // Return overflow if the destination filled up while clients were en route.
        if (delivered < r.quantity)
            AddPopulation(r.source, r.quantity - delivered);

        if (delivered > 0)
        {
            Building destBuilding = r.destination.GetComponent<Building>();
            if (destBuilding != null && destBuilding.GetBuildingType() == BuildingType.CaseworkSite)
                //Debug.Log("placehold casework recording");
                DailyReportData.Instance?.RecordCaseworkSatisfiedToday(delivered);
            else
                DailyReportData.Instance?.RecordLodgingSatisfiedToday(delivered);
        }

        if (ClientStayTracker.Instance != null && delivered > 0)
            ClientStayTracker.Instance.HandleSelfWalkArrival(r.destination, delivered, r.groupName);
        SnapshotDebug.MarkContext("relocation:arrive", "{\"task\":" + (r.parentTask != null ? r.parentTask.taskId : -1)
            + ",\"qty\":" + r.quantity + ",\"delivered\":" + delivered + ",\"dst\":\"" + r.destination.name + "\"}");

        if (showDebugInfo)
            Debug.Log($"[ClientRelocationHandler] {delivered} clients arrived on foot at {r.destination.name}");
        GameLogPanel.Instance?.LogTaskEvent($"Client relocation for task '{r.parentTask?.taskTitle}': {delivered} clients arrived at {r.destination.name}");
        ToastManager.ShowToast($"{delivered} clients arrived at {GetDisplayName(r.destination)}", ToastType.Info, true);

        OnRelocationArrived?.Invoke(r);

        // Note: the parent task is already marked Completed back when the relocation was
        // queued (see Execute()/ExecuteToSpecificDestination()) — arrival here is purely
        // physical (moving the population, registering the stay) and no longer gates task
        // completion.
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC: IMMEDIATE (teleport) DELIVERY
    // ─────────────────────────────────────────────────────────────────

    /// Returns the number of people ACTUALLY relocated (0 if no valid destination / no space).
    /// Callers gate task completion + fulfillment credit on this being > 0 (B1).
    public int ExecuteImmediate(GameTask parentTask, int requestedQuantity,
                                  bool includeShelters = true, bool includeMotels = false)
    {
        MonoBehaviour source = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (source == null) return 0;

        int available = GetPopulation(source);
        int toSend    = requestedQuantity > 0 ? Mathf.Min(requestedQuantity, available) : available;
        if (toSend <= 0) return 0;

        DeliverySystem ds = DeliverySystem.Instance;
        var destinations  = GetDestinationsSorted(ds, includeShelters, includeMotels, source);

        // main-bugfixes guard: nothing reachable to receive them.
        if (destinations.Count == 0)
        {
            string destLabel = includeShelters && includeMotels ? "shelter or motel"
                             : includeShelters ? "shelter" : "motel";
            Debug.LogWarning($"[ClientRelocationHandler] No available {destLabel} for immediate relocation.");
            return 0;
        }

        int remaining     = toSend;
        int totalDelivered = 0;

        foreach (var (dest, effectiveSpace) in destinations)
        {
            if (remaining <= 0) break;

            int sendAmount = Mathf.Min(remaining, effectiveSpace);

            // Remove from source
            int removed = RemovePopulation(source, sendAmount);
            if (removed <= 0) continue;

            // Remove from tracker on source facility
            if (ClientStayTracker.Instance != null)
            {
                ClientStayTracker.Instance.RemoveClientsByQuantity(source, removed, -1, creditCasework: false);
            }

            // Add to destination
            int delivered = AddPopulation(dest, removed);

            // Return overflow if destination was fuller than expected
            if (delivered < removed)
                AddPopulation(source, removed - delivered);






            // Track client arrivals
            Building destBuilding = dest.GetComponent<Building>();
            //if (destBuilding != null && ClientStayTracker.Instance != null && delivered > 0)
            //{
            //    string groupName = $"Relocate_{parentTask.taskId}_{source.name}_to_{dest.name}";
            //    ClientStayTracker.Instance.RegisterClientArrival(destBuilding, delivered, groupName);
            //}
            // Track client arrivals for both Shelters and Motels
             // Track arrivals at shelter OR motel for casework (centralized; fixes the
            // motel-not-tracked bug — motels now generate casework like shelters).

            if (delivered > 0)
            {
                // MERGE 76857e88: upstream sets its `anyMoved` flag here, for a bool-returning
                // method. This branch's ExecuteImmediate returns an int count, so the flag has
                // no declaration and no consumer — the enclosing `delivered > 0` already carries
                // the same signal. Upstream's Record*Today calls below are kept.
                Building destBuilding2 = dest.GetComponent<Building>();
                if (destBuilding2 != null && destBuilding2.GetBuildingType() == BuildingType.CaseworkSite)
                    //Debug.Log("placehold casework recording");
                    DailyReportData.Instance?.RecordCaseworkSatisfiedToday(delivered);
                else
                    DailyReportData.Instance?.RecordLodgingSatisfiedToday(delivered);
            }

            if (ClientStayTracker.Instance != null && delivered > 0)
            {
                string groupName = $"Relocate_{parentTask.taskId}_{source.name}_to_{dest.name}";
                ClientStayTracker.Instance.RegisterClientArrival(dest, delivered, groupName);
                ClientStayTracker.Instance.HandlePopulationDelivery(source, dest, delivered, parentTask.taskId);
            }

            remaining -= delivered;
            totalDelivered += delivered;

            if (showDebugInfo)
                Debug.Log($"[ClientRelocationHandler] Immediate {delivered} clients {source.name} → {dest.name}");
            GameLogPanel.Instance?.LogTaskEvent($"Client relocation (immediate) for task '{parentTask.taskTitle}': {delivered} clients {source.name} -> {dest.name}");
        }

        // People-based fulfillment accounting (B2): record how many actually moved.
        if (parentTask != null) parentTask.deliveredQuantity += totalDelivered;
        return totalDelivered;
    }

    // Population/food tasks stranded by an emptied facility are no longer resolved reactively
    // here — TaskSystem runs a round-end sweep (SweepStalePopulationTasks) instead, since it
    // catches every drain path (natural departure, flood, etc.), not just the ones that happen
    // to go through this handler's own methods.

    // ─────────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ─────────────────────────────────────────────────────────────────

    List<(MonoBehaviour dest, int effectiveSpace)> GetDestinationsSorted(
        DeliverySystem ds, bool includeShelters, bool includeMotels, MonoBehaviour excludeSource,
        bool filterByPath = false)
    {
        var results = new List<(MonoBehaviour, int)>();

        if (includeShelters)
        {
            foreach (Building shelter in FindObjectsOfType<Building>()
                .Where(b => b.GetBuildingType() == BuildingType.Shelter
                         && b.IsOperational()
                         && (MonoBehaviour)b != excludeSource))
            {
                BuildingResourceStorage storage = shelter.GetComponent<BuildingResourceStorage>();
                if (storage == null) continue;

                int rawSpace       = storage.GetAvailableSpace(ResourceType.Population);
                int inbound        = ds != null ? ds.GetReservedIncomingQuantity(shelter, ResourceType.Population) : 0;
                int walking        = GetPendingIncomingQuantity(shelter);
                int effectiveSpace = Mathf.Max(0, rawSpace - inbound - walking);

                if (effectiveSpace > 0)
                    results.Add((shelter, effectiveSpace));
            }
        }

        if (includeMotels)
        {
            foreach (PrebuiltBuilding motel in FindObjectsOfType<PrebuiltBuilding>()
                .Where(p => p.GetPrebuiltType() == PrebuiltBuildingType.Motel
                         && (MonoBehaviour)p != excludeSource))
            {
                int rawSpace       = motel.GetPopulationCapacity() - motel.GetCurrentPopulation();
                int inbound        = ds != null ? ds.GetReservedIncomingQuantity(motel, ResourceType.Population) : 0;
                int walking        = GetPendingIncomingQuantity(motel);
                int effectiveSpace = Mathf.Max(0, rawSpace - inbound - walking);

                if (effectiveSpace > 0)
                    results.Add((motel, effectiveSpace));
            }
        }

        // Filter by pathfinding reachability when requested (used at validation time)
        if (filterByPath && ds != null)
        {
            results = results.Where(r =>
            {
                DeliveryTimeEstimate est;
                return ds.CanCreateDeliveryWithEstimate(excludeSource, r.Item1, out est);
            }).ToList();
        }

        // Sort: shelters first if preferred, then most space
        if (preferShelters && includeShelters && includeMotels)
        {
            return results
                .OrderByDescending(r => r.Item1.GetComponent<Building>() != null)
                .ThenByDescending(r => r.Item2)
                .ToList();
        }

        return results.OrderByDescending(r => r.Item2).ToList();
    }

    /// <summary>
    /// Clients already walking toward this destination (departed but not yet arrived).
    /// Prevents over-booking a shelter/motel while multiple self-walk relocations are in flight.
    /// </summary>
    int GetPendingIncomingQuantity(MonoBehaviour destination)
    {
        int total = 0;
        foreach (var r in pendingRelocations)
            if (r.destination == destination) total += r.quantity;
        return total;
    }

    /// <summary>
    /// Total clients currently self-walking toward any building of the given type
    /// (e.g. all in-flight Shelter → CaseworkSite relocations). Used for reporting/UI,
    /// since these no longer show up as DeliverySystem active tasks.
    /// </summary>
    public int GetPendingQuantityToBuildingType(BuildingType buildingType)
    {
        int total = 0;
        foreach (var r in pendingRelocations)
        {
            Building b = r.destination as Building;
            if (b != null && b.GetBuildingType() == buildingType)
                total += r.quantity;
        }
        return total;
    }

    static string GetDisplayName(MonoBehaviour building)
    {
        if (building == null) return "Unknown";
        PrebuiltBuilding pb = building.GetComponent<PrebuiltBuilding>();
        if (pb != null) return pb.GetBuildingName();
        Building b = building.GetComponent<Building>();
        if (b != null) return b.GetDisplayName();
        return building.name;
    }

    /// <summary>Population space still bookable at a destination: storage space minus reserved
    /// vehicle inbound minus clients already walking there. Works for shelters, casework sites
    /// (Building + storage) and motels (PrebuiltBuilding).
    /// Public so callers outside this handler (TaskSystem's stale-task sweep, TaskDetailUI's
    /// choice validation) can check bookable space without duplicating the logic.</summary>
    /// <summary>The destination this task's relocation would actually target, without
    /// committing to it — used by the task card and the agent panel to show where clients
    /// would go. Same ordering GetDestinationsSorted uses for the real move.</summary>
    public MonoBehaviour PeekPrimaryDestination(GameTask parentTask, bool includeShelters, bool includeMotels, bool filterByPath)
    {
        MonoBehaviour source = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (source == null) return null;

        var destinations = GetDestinationsSorted(DeliverySystem.Instance, includeShelters, includeMotels, source, filterByPath);
        return destinations.Count > 0 ? destinations[0].dest : null;
    }

    public int GetEffectiveSpace(MonoBehaviour destination)
    {
        if (destination == null) return 0;
        DeliverySystem ds = DeliverySystem.Instance;
        int rawSpace;
        PrebuiltBuilding pb = destination.GetComponent<PrebuiltBuilding>();
        if (pb != null && pb.GetPrebuiltType() == PrebuiltBuildingType.Motel)
            rawSpace = pb.GetPopulationCapacity() - pb.GetCurrentPopulation();
        else
        {
            BuildingResourceStorage storage =
                destination.GetComponent<Building>()?.GetComponent<BuildingResourceStorage>()
                ?? destination.GetComponent<BuildingResourceStorage>();
            if (storage == null) return 0;
            rawSpace = storage.GetAvailableSpace(ResourceType.Population);
        }
        int inbound = ds != null ? ds.GetReservedIncomingQuantity(destination, ResourceType.Population) : 0;
        int walking = GetPendingIncomingQuantity(destination);
        return Mathf.Max(0, rawSpace - inbound - walking);
    }

    /// <summary>Public so callers outside this handler (TaskSystem's stale-task sweep, TaskDetailUI's
    /// choice validation) can check a facility's current population without duplicating this logic.</summary>
    public int GetPopulation(MonoBehaviour building)
    {
        PrebuiltBuilding pb = building.GetComponent<PrebuiltBuilding>();
        if (pb != null) return pb.GetCurrentPopulation();

        BuildingResourceStorage storage =
            building.GetComponent<Building>()?.GetComponent<BuildingResourceStorage>()
            ?? building.GetComponent<BuildingResourceStorage>();
        return storage?.GetResourceAmount(ResourceType.Population) ?? 0;
    }

    int RemovePopulation(MonoBehaviour building, int amount)
    {
        // Community (PrebuiltBuilding) — remove directly from its resource storage
        PrebuiltBuilding pb = building.GetComponent<PrebuiltBuilding>();
        if (pb != null)
        {
            BuildingResourceStorage storage = pb.GetResourceStorage();
            return storage?.RemoveResource(ResourceType.Population, amount) ?? 0;
        }

        // Shelter (Building)
        BuildingResourceStorage bStorage =
            building.GetComponent<Building>()?.GetComponent<BuildingResourceStorage>()
            ?? building.GetComponent<BuildingResourceStorage>();
        return bStorage?.RemoveResource(ResourceType.Population, amount) ?? 0;
    }

    int AddPopulation(MonoBehaviour building, int amount)
    {
        Building b = building.GetComponent<Building>();
        if (b != null)
            return b.GetComponent<BuildingResourceStorage>()?.AddResource(ResourceType.Population, amount) ?? 0;

        PrebuiltBuilding pb = building.GetComponent<PrebuiltBuilding>();
        if (pb != null)
            return pb.GetResourceStorage()?.AddResource(ResourceType.Population, amount) ?? 0;

        return building.GetComponent<BuildingResourceStorage>()?.AddResource(ResourceType.Population, amount) ?? 0;
    }
}