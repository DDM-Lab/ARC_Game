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
        if (destination == null)
        {
            errorMessage = $"Cannot find destination facility '{parentTask.affectedFacility}'";
            return false;
        }

        DeliverySystem ds = DeliverySystem.Instance;
        if (ds == null) { errorMessage = "DeliverySystem not found"; return false; }

        // How much does the destination still actually need, after accounting for already-inbound food?
        int alreadyInbound = ds.GetReservedIncomingQuantity(destination, ResourceType.FoodPacks);
        int effectiveNeed   = Mathf.Max(0, requestedQuantity - alreadyInbound);

        if (effectiveNeed <= 0)
        {
            errorMessage = $"{alreadyInbound} food packs already inbound — need is covered";
            return false;
        }

        // Is there at least enough food across all kitchens (minus already-outbound) to partially help?
        int totalEffective = GetTotalEffectiveFood(ds);
        if (totalEffective <= 0)
        {
            errorMessage = "No food packs available across any kitchen";
            return false;
        }

        // At least one vehicle must be capable
        bool hasVehicle = FindObjectsOfType<Vehicle>()
            .Any(v => v.GetAllowedCargoTypes().Contains(ResourceType.FoodPacks)
                   && v.GetCurrentStatus() != VehicleStatus.Damaged);
        if (!hasVehicle)
        {
            errorMessage = "No undamaged vehicle available for food delivery";
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
    public bool Execute(GameTask parentTask, int requestedQuantity)
    {
        MonoBehaviour destination = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (destination == null)
        {
            Debug.LogError($"[FoodDeliveryTaskGenerator] Cannot find destination for '{parentTask.taskTitle}'");
            return false;
        }

        DeliverySystem ds = DeliverySystem.Instance;
        if (ds == null) return false;

        // Subtract already-inbound food so we don't over-deliver
        int alreadyInbound = ds.GetReservedIncomingQuantity(destination, ResourceType.FoodPacks);
        int remaining       = Mathf.Max(0, requestedQuantity - alreadyInbound);

        if (remaining <= 0)
        {
            if (showDebugInfo)
                Debug.Log($"[FoodDeliveryTaskGenerator] Inbound deliveries already cover {alreadyInbound}/{requestedQuantity} for {destination.name}");
            TaskSystem.Instance.CompleteTask(parentTask);
            return true;
        }

        // Collect kitchens sorted by effective available stock (actual - already-outbound), highest first
        var kitchens = GetKitchensSorted(ds);
        if (kitchens.Count == 0)
        {
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
                TaskSystem.Instance.LinkDeliveriesToTask(parentTask, deliveries);
                remaining  -= sendAmount;
                anyCreated  = true;

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

    List<(MonoBehaviour building, int effectiveStock)> GetKitchensSorted(DeliverySystem ds)
    {
        return FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational())
            .Select(b =>
            {
                int stock       = GetStorage(b)?.GetResourceAmount(ResourceType.FoodPacks) ?? 0;
                int outbound    = ds.GetReservedOutgoingQuantity(b, ResourceType.FoodPacks);
                int effective   = Mathf.Max(0, stock - outbound);
                return (building: (MonoBehaviour)b, effectiveStock: effective);
            })
            .Where(k => k.effectiveStock > 0)
            .OrderByDescending(k => k.effectiveStock)
            .ToList();
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
