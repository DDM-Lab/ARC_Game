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
/// (Execute) has clients depart immediately and arrive after a configurable
/// number of rounds. The immediate/emergency path (ExecuteImmediate) is
/// unaffected and stays a zero-delay teleport.
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

        if (totalEffectiveSpace <= 0)
        {
            string destLabel = includeShelters && includeMotels ? "shelter/motel"
                             : includeShelters ? "shelter" : "motel";
            errorMessage = $"No reachable {destLabel} with available space — routes may be blocked by flooding";
            return false;
        }

        return true;
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
            TaskSystem.Instance.SetTaskInProgress(parentTask);
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
        if (toSend <= 0) return false;

        int removed = RemovePopulation(source, toSend);
        if (removed <= 0) return false;

        QueueSelfWalk(parentTask, source, destination, removed);
        TaskSystem.Instance.SetTaskInProgress(parentTask);
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
            ClientStayTracker.Instance.RemoveClientsByQuantity(source, quantity);

        PendingRelocation relocation = new PendingRelocation
        {
            parentTask      = parentTask,
            source          = source,
            destination     = destination,
            quantity        = quantity,
            roundsRemaining = Mathf.Max(1, relocationDelayRounds),
            groupName       = $"Relocate_{parentTask.taskId}_{source.name}_to_{destination.name}"
        };
        pendingRelocations.Add(relocation);

        if (showDebugInfo)
            Debug.Log($"[ClientRelocationHandler] {quantity} clients departing {source.name} → {destination.name} on foot, arriving in {relocationDelayRounds} round(s)");
        GameLogPanel.Instance?.LogTaskEvent($"Client relocation for task '{parentTask.taskTitle}': {quantity} clients departing {source.name} -> {destination.name}, arriving in {relocationDelayRounds} round(s)");

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

        // Return overflow if the destination filled up while clients were en route.
        if (delivered < r.quantity)
            AddPopulation(r.source, r.quantity - delivered);

        if (ClientStayTracker.Instance != null && delivered > 0)
            ClientStayTracker.Instance.RegisterClientArrival(r.destination, delivered, r.groupName);

        if (showDebugInfo)
            Debug.Log($"[ClientRelocationHandler] {delivered} clients arrived on foot at {r.destination.name}");
        GameLogPanel.Instance?.LogTaskEvent($"Client relocation for task '{r.parentTask?.taskTitle}': {delivered} clients arrived at {r.destination.name}");
        ToastManager.ShowToast($"{delivered} clients arrived at {GetDisplayName(r.destination)}", ToastType.Info, true);

        OnRelocationArrived?.Invoke(r);

        // Complete the parent task once all of its self-walk relocations have arrived.
        if (r.parentTask != null
            && r.parentTask.status == TaskStatus.InProgress
            && !pendingRelocations.Any(p => p.parentTask == r.parentTask))
        {
            TaskSystem.Instance.CompleteTask(r.parentTask);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC: IMMEDIATE (teleport) DELIVERY
    // ─────────────────────────────────────────────────────────────────

    public bool ExecuteImmediate(GameTask parentTask, int requestedQuantity,
                                  bool includeShelters = true, bool includeMotels = false)
    {
        MonoBehaviour source = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (source == null) return false;

        int available = GetPopulation(source);
        int toSend    = requestedQuantity > 0 ? Mathf.Min(requestedQuantity, available) : available;
        if (toSend <= 0) return false;

        DeliverySystem ds = DeliverySystem.Instance;
        var destinations  = GetDestinationsSorted(ds, includeShelters, includeMotels, source);

        if (destinations.Count == 0)
        {
            string destLabel = includeShelters && includeMotels ? "shelter or motel"
                             : includeShelters ? "shelter" : "motel";
            Debug.LogWarning($"[ClientRelocationHandler] No available {destLabel} for immediate relocation.");
            return false;
        }

        int remaining  = toSend;
        bool anyMoved  = false;

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
                ClientStayTracker.Instance.RemoveClientsByQuantity(source, removed);
            }

            // Add to destination
            int delivered = AddPopulation(dest, removed);

            // Return overflow if destination was fuller than expected
            if (delivered < removed)
                AddPopulation(source, removed - delivered);

            if (delivered > 0) anyMoved = true;

            // Track client arrivals
            Building destBuilding = dest.GetComponent<Building>();
            //if (destBuilding != null && ClientStayTracker.Instance != null && delivered > 0)
            //{
            //    string groupName = $"Relocate_{parentTask.taskId}_{source.name}_to_{dest.name}";
            //    ClientStayTracker.Instance.RegisterClientArrival(destBuilding, delivered, groupName);
            //}
            // Track client arrivals for both Shelters and Motels
            if (ClientStayTracker.Instance != null && delivered > 0)
            {
                string groupName = $"Relocate_{parentTask.taskId}_{source.name}_to_{dest.name}";
                ClientStayTracker.Instance.RegisterClientArrival(dest, delivered, groupName);
            }

            remaining -= delivered;

            if (showDebugInfo)
                Debug.Log($"[ClientRelocationHandler] Immediate {delivered} clients {source.name} → {dest.name}");
            GameLogPanel.Instance?.LogTaskEvent($"Client relocation (immediate) for task '{parentTask.taskTitle}': {delivered} clients {source.name} -> {dest.name}");
        }

        return anyMoved;
    }

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

    int GetPopulation(MonoBehaviour building)
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