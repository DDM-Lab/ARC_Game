using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Handles food delivery routing for task choices.
/// Destination = task's requesting facility (always known from trigger).
/// Sources = all kitchens, drained highest-stock-first until quantity is fulfilled.
/// Accounts for in-flight reservations on both source and destination to avoid double-booking.
/// </summary>
public class FoodDeliveryHandler : MonoBehaviour
{
    [Header("Debug")]
    public bool showDebugInfo = true;

    public static FoodDeliveryHandler Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC: VALIDATION
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Call from TaskDetailUI before showing a food-delivery choice as valid.
    /// </summary>
    public bool CanExecute(GameTask parentTask, int requestedQuantity, out string errorMessage)
    {
        errorMessage = "";
        MonoBehaviour destination = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (destination == null) { errorMessage = "Destination not found"; return false; }

        DeliverySystem ds = DeliverySystem.Instance;
        if (ds == null) { errorMessage = "Delivery System missing"; return false; }

        int alreadyInbound = ds.GetReservedIncomingQuantity(destination, ResourceType.FoodPacks);
        int effectiveNeed = Mathf.Max(0, requestedQuantity - alreadyInbound);
        if (effectiveNeed <= 0) {
            errorMessage = $"{alreadyInbound} meals already inbound.";
            return false;
        }

        int totalReachableFood = 0;
        var kitchens = FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational());

        bool atLeastOneKitchenReachable = false;

        foreach (var k in kitchens) {
            if (ds.CanCreateDeliveryWithEstimate(k, destination, out var estimate)) {
                atLeastOneKitchenReachable = true;
                int stock = k.GetComponent<BuildingResourceStorage>()?.GetResourceAmount(ResourceType.FoodPacks) ?? 0;
                int reserved = ds.GetReservedOutgoingQuantity(k, ResourceType.FoodPacks);
                totalReachableFood += Mathf.Max(0, stock - reserved);
            }
        }

        if (!atLeastOneKitchenReachable) {
            errorMessage = "All routes from kitchens are blocked by flooding.";
            return false;
        }

        if (totalReachableFood <= 0) {
            errorMessage = "No unreserved meals available in reachable kitchens.";
            return false;
        }
        bool hasVehicle = FindObjectsOfType<Vehicle>()
            .Any(v => v.GetAllowedCargoTypes().Contains(ResourceType.FoodPacks) && v.GetCurrentStatus() != VehicleStatus.Damaged);
        
        if (!hasVehicle) {
            errorMessage = "No undamaged delivery vehicles available.";
            return false;
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC: QUEUED (vehicle) DELIVERY
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates vehicle delivery tasks from one or more kitchens to the requesting facility.
    /// Drains kitchens with most effective stock first; stops once quantity is fulfilled.
    /// Returns true if at least one delivery was queued.
    /// </summary>
    public bool Execute(GameTask parentTask, AgentChoice choice)
    {
        MonoBehaviour destination = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (destination == null)
        {
            Debug.LogError($"[FoodDeliveryTaskGenerator] Cannot find destination for '{parentTask.taskTitle}'");
            return false;
        }

        DeliverySystem ds = DeliverySystem.Instance;
        if (ds == null) return false;

        int alreadyInbound = ds.GetReservedIncomingQuantity(destination, ResourceType.FoodPacks);
        int remaining = Mathf.Max(0, choice.deliveryQuantity - alreadyInbound);

        if (remaining <= 0)
        {
            // EXIT A: destination already covered by inbound. CompleteTask -> resolved AND
            // fulfilled, so Unity counts this exactly as a delivery even though none is made.
            SnapshotDebug.MarkContext("food:exit", "{\"branch\":\"inbound-covered\",\"dst\":\""
                + destination.name + "\",\"requested\":" + choice.deliveryQuantity
                + ",\"inbound\":" + alreadyInbound + "}");
            if (showDebugInfo)
                Debug.Log($"[FoodDeliveryTaskGenerator] Inbound deliveries already cover {alreadyInbound}/{choice.deliveryQuantity} for {destination.name}");
            TaskSystem.Instance.CompleteTask(parentTask);
            return true;
        }

        var kitchens = GetKitchensSorted(ds, destination.transform.position, choice.prioritizeNearestSource);
        if (kitchens.Count == 0)
        {
            // EXIT B: no kitchen has effective stock. Returns false and completes NOTHING --
            // the task stays on the board. Collapsing this with EXIT A is what made four
            // traces read foodResolved 0 against Unity's 1.
            SnapshotDebug.MarkContext("food:exit", "{\"branch\":\"no-kitchen-stock\",\"dst\":\""
                + destination.name + "\",\"requested\":" + choice.deliveryQuantity + "}");
            Debug.LogWarning($"[FoodDeliveryTaskGenerator] No kitchens with available food for '{parentTask.taskTitle}'");
            return false;
        }

        bool anyCreated = false;

        foreach (var (kitchen, effectiveStock) in kitchens)
        {
            if (remaining <= 0) break;

            int sendAmount = Mathf.Min(remaining, effectiveStock);
            List<DeliveryTask> deliveries = ds.CreateDeliveryTask(kitchen, destination, ResourceType.FoodPacks, sendAmount, 3);

            if (deliveries.Count > 0)
            {
                SnapshotDebug.MarkContext("food:exit", "{\"branch\":\"created\",\"dst\":\""
                    + destination.name + "\",\"kitchen\":\"" + kitchen.name
                    + "\",\"send\":" + sendAmount + ",\"effective\":" + effectiveStock + "}");
                TaskSystem.Instance.LinkDeliveriesToTask(parentTask, deliveries);
                remaining -= sendAmount;
                anyCreated = true;

                if (showDebugInfo)
                    Debug.Log($"[FoodDeliveryTaskGenerator] Queued {sendAmount} food from {kitchen.name} → {destination.name}");
            }
        }

        if (anyCreated)
            TaskSystem.Instance.SetTaskInProgress(parentTask);
        else
            Debug.LogWarning($"[FoodDeliveryTaskGenerator] Could not create any deliveries for '{parentTask.taskTitle}'");

        return anyCreated;
    }

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC: IMMEDIATE (teleport) DELIVERY
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Immediately transfers food from kitchens to destination (no vehicle needed).
    /// Used for "airdrop" / emergency-bypass choices.
    /// </summary>
    public void ExecuteImmediate(GameTask parentTask, int requestedQuantity)
    {
        MonoBehaviour destination = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (destination == null) return;

        BuildingResourceStorage destStorage = GetStorage(destination);
        if (destStorage == null) return;

        int amount = requestedQuantity > 0 ? requestedQuantity : destStorage.GetAvailableSpace(ResourceType.FoodPacks);
        destStorage.AddResource(ResourceType.FoodPacks, amount);

        if (showDebugInfo)
            Debug.Log($"[FoodDeliveryTaskGenerator] Immediate drop: added {amount} food to {destination.name}");
    }

    // ─────────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ─────────────────────────────────────────────────────────────────

    List<(MonoBehaviour building, int effectiveStock)> GetKitchensSorted(DeliverySystem ds, Vector3? referencePosition, bool prioritizeNearest)
    {
        var kitchens = FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational())
            .Select(b =>
            {
                int stock     = GetStorage(b)?.GetResourceAmount(ResourceType.FoodPacks) ?? 0;
                int outbound  = ds.GetReservedOutgoingQuantity(b, ResourceType.FoodPacks);
                int effective = Mathf.Max(0, stock - outbound);
                return (building: (MonoBehaviour)b, effectiveStock: effective);
            })
            .Where(k => k.effectiveStock > 0);

        if (prioritizeNearest && referencePosition.HasValue)
            return kitchens.OrderBy(k => Vector3.Distance(k.building.transform.position, referencePosition.Value)).ToList();

        return kitchens.OrderByDescending(k => k.effectiveStock).ToList();
    }

    /// <summary>
    /// returns kitchens Execute() picks
    /// </summary>
    public List<(MonoBehaviour kitchen, int amount)> PlanSources(GameTask parentTask, AgentChoice choice)
    {
        var plan = new List<(MonoBehaviour, int)>();

        MonoBehaviour destination = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (destination == null) return plan;

        DeliverySystem ds = DeliverySystem.Instance;
        if (ds == null) return plan;

        int alreadyInbound = ds.GetReservedIncomingQuantity(destination, ResourceType.FoodPacks);
        int remaining = Mathf.Max(0, choice.deliveryQuantity - alreadyInbound);
        if (remaining <= 0) return plan;

        foreach (var (kitchen, effectiveStock) in GetKitchensSorted(ds, destination.transform.position, choice.prioritizeNearestSource))
        {
            if (remaining <= 0) break;
            int sendAmount = Mathf.Min(remaining, effectiveStock);
            if (sendAmount <= 0) continue;
            plan.Add((kitchen, sendAmount));
            remaining -= sendAmount;
        }

        return plan;
    }

    int GetTotalRawFood()
    {
        return FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational())
            .Sum(b => GetStorage(b)?.GetResourceAmount(ResourceType.FoodPacks) ?? 0);
    }

    int GetTotalEffectiveFood(DeliverySystem ds)
    {
        return FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational())
            .Sum(b =>
            {
                int stock    = GetStorage(b)?.GetResourceAmount(ResourceType.FoodPacks) ?? 0;
                int outbound = ds.GetReservedOutgoingQuantity(b, ResourceType.FoodPacks);
                return Mathf.Max(0, stock - outbound);
            });
    }

    BuildingResourceStorage GetStorage(MonoBehaviour building)
    {
        Building b = building.GetComponent<Building>();
        if (b != null) return b.GetComponent<BuildingResourceStorage>();
        PrebuiltBuilding pb = building.GetComponent<PrebuiltBuilding>();
        if (pb != null) return pb.GetResourceStorage();
        return building.GetComponent<BuildingResourceStorage>();
    }
}
