using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Handles client (Population) relocation for task choices.
/// Source = task's requesting facility (always known from trigger).
/// Destinations = shelters and/or motels, filled largest-effective-space-first.
/// Accounts for in-flight reservations so we never over-promise space.
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

    [Header("Debug")]
    public bool showDebugInfo = true;

    public static ClientRelocationHandler Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC: VALIDATION
    // ─────────────────────────────────────────────────────────────────

    public bool CanExecute(GameTask parentTask, int requestedQuantity,
                           bool includeShelters, bool includeMotels,
                           out string errorMessage)
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
        if (ds == null) { errorMessage = "DeliverySystem not found"; return false; }

        int totalEffectiveSpace = GetDestinationsSorted(ds, includeShelters, includeMotels, source)
            .Sum(d => d.effectiveSpace);

        if (totalEffectiveSpace <= 0)
        {
            string destLabel = includeShelters && includeMotels ? "shelter/motel"
                             : includeShelters ? "shelter" : "motel";
            errorMessage = $"No space available at any {destLabel}";
            return false;
        }

        bool hasVehicle = FindObjectsOfType<Vehicle>()
            .Any(v => v.GetAllowedCargoTypes().Contains(ResourceType.Population)
                   && v.GetCurrentStatus() != VehicleStatus.Damaged);
        if (!hasVehicle)
        {
            errorMessage = "No undamaged vehicle available for client transport";
            return false;
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC: QUEUED (vehicle) DELIVERY
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates vehicle delivery tasks distributing clients across available
    /// shelters and/or motels. Returns true if at least one delivery was queued.
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
        if (ds == null) return false;

        int available = GetPopulation(source);
        int toSend    = requestedQuantity > 0 ? Mathf.Min(requestedQuantity, available) : available;

        if (toSend <= 0)
        {
            Debug.LogWarning($"[ClientRelocationTaskGenerator] No clients to relocate from {source.name}");
            return false;
        }

        var destinations = GetDestinationsSorted(ds, includeShelters, includeMotels, source);
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
            List<DeliveryTask> deliveries = ds.CreateDeliveryTask(source, dest, ResourceType.Population, sendAmount, 3);

            if (deliveries.Count > 0)
            {
                TaskSystem.Instance.LinkDeliveriesToTask(parentTask, deliveries);
                remaining  -= sendAmount;
                anyCreated  = true;

                if (showDebugInfo)
                    Debug.Log($"[ClientRelocationTaskGenerator] Queued {sendAmount} clients from {source.name} → {dest.name}");
            }
        }

        if (anyCreated)
            TaskSystem.Instance.SetTaskInProgress(parentTask);
        else
            Debug.LogWarning($"[ClientRelocationTaskGenerator] Could not create any deliveries for '{parentTask.taskTitle}'");

        return anyCreated;
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC: IMMEDIATE (teleport) DELIVERY
    // ─────────────────────────────────────────────────────────────────

    public void ExecuteImmediate(GameTask parentTask, int requestedQuantity,
                                  bool includeShelters = true, bool includeMotels = false)
    {
        MonoBehaviour source = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (source == null) return;

        int available = GetPopulation(source);
        int toSend    = requestedQuantity > 0 ? Mathf.Min(requestedQuantity, available) : available;
        if (toSend <= 0) return;

        DeliverySystem ds = DeliverySystem.Instance;
        var destinations  = GetDestinationsSorted(ds, includeShelters, includeMotels, source);
        int remaining     = toSend;

        foreach (var (dest, effectiveSpace) in destinations)
        {
            if (remaining <= 0) break;

            int sendAmount = Mathf.Min(remaining, effectiveSpace);

            // Remove from source
            int removed = RemovePopulation(source, sendAmount);
            if (removed <= 0) continue;

            // Add to destination
            int delivered = AddPopulation(dest, removed);

            // Return overflow if destination was fuller than expected
            if (delivered < removed)
                AddPopulation(source, removed - delivered);

            // Track client arrivals
            Building destBuilding = dest.GetComponent<Building>();
            if (destBuilding != null && ClientStayTracker.Instance != null && delivered > 0)
            {
                string groupName = $"Relocate_{parentTask.taskId}_{source.name}_to_{dest.name}";
                ClientStayTracker.Instance.RegisterClientArrival(destBuilding, delivered, groupName);
            }

            remaining -= delivered;

            if (showDebugInfo)
                Debug.Log($"[ClientRelocationTaskGenerator] Immediate {delivered} clients {source.name} → {dest.name}");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ─────────────────────────────────────────────────────────────────

    List<(MonoBehaviour dest, int effectiveSpace)> GetDestinationsSorted(
        DeliverySystem ds, bool includeShelters, bool includeMotels, MonoBehaviour excludeSource)
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
                int effectiveSpace = Mathf.Max(0, rawSpace - inbound);

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
                int effectiveSpace = Mathf.Max(0, rawSpace - inbound);

                if (effectiveSpace > 0)
                    results.Add((motel, effectiveSpace));
            }
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