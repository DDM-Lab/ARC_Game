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
        if (destination == null) { errorMessage = "Destination not found"; return false; }

        DeliverySystem ds = DeliverySystem.Instance;
        if (ds == null) { errorMessage = "Delivery System missing"; return false; }

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
            // Only kitchens a vehicle can actually reach count (flood-aware path), and only their
            // unreserved stock (BUG_REPORTS B11/B12: the old check counted unreachable kitchens).
            int totalReachableFood = 0;
            bool atLeastOneKitchenReachable = false;
            foreach (var k in FindObjectsOfType<Building>()
                         .Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational()))
            {
                if (!ds.CanCreateDeliveryWithEstimate(k, destination, out var estimate)) continue;
                atLeastOneKitchenReachable = true;
                int stock = k.GetComponent<BuildingResourceStorage>()?.GetResourceAmount(ResourceType.FoodPacks) ?? 0;
                int reserved = ds.GetReservedOutgoingQuantity(k, ResourceType.FoodPacks);
                totalReachableFood += Mathf.Max(0, stock - reserved);
            }
            if (!atLeastOneKitchenReachable)
            {
                // Covers both "no operational kitchen exists on the map at all" (the loop above
                // never ran) and "kitchens exist but every route to them is flooded" — the loop
                // can't tell those apart, so the wording names both possibilities instead of
                // asserting either one specifically (a plain "no accessible kitchen" reads as if
                // one exists but is merely unreachable, which is wrong when none exists at all).
                errorMessage = "No kitchen is reachable: either none exist on the map, or every route to one is blocked.";
                return false;
            }
            if (totalReachableFood <= 0)
            {
                int totalRawStock = GetTotalRawFood();
                errorMessage = totalRawStock > 0
                    ? "All available meals are already scheduled for other deliveries"
                    : "No meals available across any kitchen";
                return false;
            }

            // Some choices (e.g. "deliver double") must be fully coverable rather than just
            // partially helped — otherwise they'd silently under-deliver relative to what the
            // player asked for. totalEffective already excludes meals reserved for other
            // deliveries, so a shortfall here can mean either not enough raw stock or enough
            // stock but most of it already scheduled elsewhere.
            // MERGE FIX (74304870): upstream's new requireFullQuantity check reads `totalEffective`,
            // which it defines earlier in a region our side had rewritten (BUG_REPORTS B11/B12
            // made the reachability check flood-aware). Resolving to upstream's block left the
            // name undefined; GetTotalEffectiveFood is the same helper upstream computes it from.
            int totalEffective = GetTotalEffectiveFood(ds);
            if (choice.requireFullQuantity && totalEffective < effectiveNeed)
            {
                errorMessage = $"Not enough food across all kitchens for this request (some meals may already be scheduled for other deliveries). Available: {totalEffective}, Required: {effectiveNeed}";
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
            // EXIT A: destination already covered by inbound. CompleteTask -> resolved AND
            // fulfilled, so Unity counts this exactly as a delivery even though none is made.
            SnapshotDebug.MarkContext("food:exit", "{\"branch\":\"inbound-covered\",\"dst\":\""
                + destination.name + "\",\"requested\":" + choice.deliveryQuantity
                + ",\"inbound\":" + alreadyInbound + "}");
            if (showDebugInfo)
                Debug.Log($"[FoodDeliveryTaskGenerator] Inbound deliveries already cover {alreadyInbound}/{requestedQuantity} for {destination.name}");
            // Nothing to deliver. This is a refusal (the UI already refuses it), not a fulfilment:
            // completing the task here credited a delivery that was never made (BUG_REPORTS C.3).
            return false;
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
        {
            TaskSystem.Instance.SetTaskInProgress(parentTask);
            GameLogPanel.Instance?.LogTaskEvent($"Food delivery choice fulfilled for '{parentTask.taskTitle}': queued from {kitchens.Count} kitchen(s)");
        }
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
    public int ExecuteImmediate(GameTask parentTask, AgentChoice choice)
    {
        // External emergency supply: no kitchen is debited (design call), but the drop must have a
        // destination and only what fits is credited (BUG_REPORTS B15).
        MonoBehaviour destination = TaskSystem.Instance.FindTriggeringFacility(parentTask);
        if (destination == null) return 0;
        BuildingResourceStorage destStorage = GetStorage(destination);
        if (destStorage == null) return 0;

        int requestedQuantity = ResolveQuantity(choice, destination);
        int amount = requestedQuantity > 0 ? requestedQuantity : destStorage.GetAvailableSpace(ResourceType.FoodPacks);
        int added = destStorage.AddResource(ResourceType.FoodPacks, amount);

        // Food actually credited to a community counts as Food Used, same as a vehicle delivery
        // (DeliverySystem.OnVehicleDeliveryCompleted) — this path never goes through DeliverySystem.
        if (added > 0 && (destination as PrebuiltBuilding)?.GetPrebuiltType() == PrebuiltBuildingType.Community)
            DailyReportData.Instance?.RecordCommunityFoodUsedToday(destination.name, added);

        if (showDebugInfo)
            Debug.Log($"[FoodDeliveryTaskGenerator] Immediate drop: added {added}/{amount} food to {destination.name}");
        GameLogPanel.Instance?.LogTaskEvent($"Immediate food drop executed for '{parentTask.taskTitle}': delivered {added} meals to {destination.name}");
        return added;
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
    public int ResolveQuantity(AgentChoice choice, MonoBehaviour destination)
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
