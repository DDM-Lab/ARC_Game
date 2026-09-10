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
    public bool CanExecute(GameTask parentTask, AgentChoice choice, out string errorMessage)
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

        int requestedQuantity = ResolveQuantity(choice, destination);

        // How much does the destination still actually need, after accounting for already-inbound food?
        int alreadyInbound = ds.GetReservedIncomingQuantity(destination, ResourceType.FoodPacks);
        int effectiveNeed   = Mathf.Max(0, requestedQuantity - alreadyInbound);

        if (effectiveNeed <= 0)
        {
            errorMessage = alreadyInbound > 0
                ? $"{alreadyInbound} meals already inbound — need is covered"
                : "No food is currently needed here";
            return false;
        }

        // Immediate deliveries don't draw from kitchens or use a vehicle (see ExecuteImmediate) —
        // the checks below only apply to the queued, vehicle-based path.
        if (!choice.immediateDelivery)
        {
            // Is there at least enough food across all kitchens (minus already-outbound) to partially help?
            int totalEffective = GetTotalEffectiveFood(ds);
            if (totalEffective <= 0)
            {
                int totalRawStock = GetTotalRawFood();
                errorMessage = totalRawStock > 0
                    ? "All available meals are already scheduled for other deliveries"
                    : "No meals available across any kitchen";
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

        int requestedQuantity = ResolveQuantity(choice, destination);
        int alreadyInbound = ds.GetReservedIncomingQuantity(destination, ResourceType.FoodPacks);
        int remaining = Mathf.Max(0, requestedQuantity - alreadyInbound);

        if (remaining <= 0)
        {
            if (showDebugInfo)
                Debug.Log($"[FoodDeliveryTaskGenerator] Inbound deliveries already cover {alreadyInbound}/{requestedQuantity} for {destination.name}");
            TaskSystem.Instance.CompleteTask(parentTask);
            return true;
        }

        var kitchens = GetKitchensSorted(ds, destination.transform.position, choice.prioritizeNearestSource);
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
    public void ExecuteImmediate(GameTask parentTask, AgentChoice choice)
    {
        MonoBehaviour destination = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (destination == null) return;

        BuildingResourceStorage destStorage = GetStorage(destination);
        if (destStorage == null) return;

        int requestedQuantity = ResolveQuantity(choice, destination);
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

        int requestedQuantity = ResolveQuantity(choice, destination);
        int alreadyInbound = ds.GetReservedIncomingQuantity(destination, ResourceType.FoodPacks);
        int remaining = Mathf.Max(0, requestedQuantity - alreadyInbound);
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

    /// <summary>
    /// Resolves a choice's actual requested quantity. PopulationBased ignores the authored
    /// deliveryQuantity and instead asks the destination how much it actually needs right now
    /// (population x consumption rate, minus what's already in storage) — see
    /// BuildingResourceStorage.GetFoodNeed(). deliveryPercentage acts as a configurable multiplier
    /// on that need — 100 = exactly the current need, 200 = double (e.g. "cover this round plus the
    /// follow-up"), 0/unset defaults to 100 for backward compatibility. Every other quantityType
    /// uses the fixed deliveryQuantity value as authored (Percentage/All aren't meaningful for food,
    /// which draws from many kitchens rather than one source, so they fall back to Fixed here).
    /// </summary>
    int ResolveQuantity(AgentChoice choice, MonoBehaviour destination)
    {
        if (choice.quantityType == DeliveryQuantityType.PopulationBased)
        {
            BuildingResourceStorage destStorage = GetStorage(destination);
            int need = destStorage?.GetFoodNeed() ?? 0;
            float multiplier = choice.deliveryPercentage > 0 ? choice.deliveryPercentage / 100f : 1f;
            return Mathf.RoundToInt(need * multiplier);
        }

        return choice.deliveryQuantity;
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
