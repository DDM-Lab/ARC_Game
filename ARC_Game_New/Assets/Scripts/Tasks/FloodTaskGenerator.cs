using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class FloodTaskGenerator : MonoBehaviour
{
    [Header("Emergency Task Configuration")]
    public TaskDatabase emergencyTaskDatabase;
    public Sprite vehicleDamageImage;

    [Header("Debug")]
    public bool showDebugInfo = true;

    // Tracks whether each active blockage task had cargo already loaded (Population only)
    private Dictionary<int, bool> blockageTaskLoadedState = new Dictionary<int, bool>();

    // Singleton
    public static FloodTaskGenerator Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        if (FloodSystem.Instance != null)
        {
            FloodSystem.Instance.OnFloodTileAdded += OnFloodExpanded;
            FloodSystem.Instance.OnFloodTileRemoved += OnFloodTileRemoved;
        }

        if (TaskSystem.Instance != null)
            TaskSystem.Instance.OnTaskCompleted += OnAnyTaskCompleted;
    }

    void OnFloodExpanded(Vector3Int floodPosition)
    {
        // Check if any vehicles are now blocked by this new flood tile
        CheckForBlockedVehicles();
    }

    void OnFloodTileRemoved(Vector3Int floodPosition)
    {
        // The paid Vehicle Repair task is permanently disabled (see CreateVehicleRepairTask
        // below), so this is now the only way a flood-damaged vehicle becomes available again.
        RestoreVehiclesClearOfFlood();
    }

    void RestoreVehiclesClearOfFlood()
    {
        if (FloodSystem.Instance == null) return;

        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            if (vehicle.GetCurrentStatus() != VehicleStatus.Damaged) continue;
            if (FloodSystem.Instance.IsFloodedAt(vehicle.transform.position)) continue;

            vehicle.RepairVehicle();

            if (showDebugInfo)
                Debug.Log($"[FloodTaskGenerator] Auto-restored {vehicle.GetVehicleName()} — flood cleared");
            GameLogPanel.Instance?.LogVehicleEvent($"{vehicle.GetVehicleName()} automatically restored after flood cleared");
        }
    }

    void CheckForBlockedVehicles()
    {
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>();

        foreach (Vehicle vehicle in vehicles)
        {
            if (vehicle.GetCurrentStatus() == VehicleStatus.InTransit &&
                FloodSystem.Instance.IsFloodedAt(vehicle.transform.position))
            {
                // This vehicle just got flooded
                vehicle.GetComponent<Vehicle>().StopVehicleDueToFlood();
            }
        }
    }

    /// <summary>
    /// Create road blockage emergency task
    /// </summary>
    public void CreateRoadBlockageTask(Vehicle blockedVehicle, DeliveryTask originalDelivery)
    {
        if (blockedVehicle == null || originalDelivery == null)
        {
            if (showDebugInfo)
                Debug.LogWarning("Cannot create road blockage task - missing vehicle or delivery");
            return;
        }

        bool hasLoadedCargo = blockedVehicle.GetCargoAmount(originalDelivery.cargoType) > 0;

        // Resolve the vehicle's in-progress cargo before building the task below.
        if (originalDelivery.cargoType == ResourceType.FoodPacks)
            DiscardVehicleCargo(blockedVehicle, originalDelivery);
        else if (originalDelivery.cargoType == ResourceType.Population && hasLoadedCargo)
            ReturnCargoToSource(blockedVehicle, originalDelivery);

        if (TaskSystem.Instance == null)
        {
            if (showDebugInfo)
                Debug.Log("TaskSystem not found - cannot create road blockage task");
            return;
        }
        string cargoLabel   = originalDelivery.cargoType == ResourceType.Population ? "clients" : "meals";
        string phase        = hasLoadedCargo ? "en route to drop-off" : "en route to pick-up";
        string srcName      = GetBuildingDisplayName(originalDelivery.sourceBuilding);
        string dstName      = GetBuildingDisplayName(originalDelivery.destinationBuilding);

        GameTask roadBlockageTask = TaskSystem.Instance.CreateTask(
            "Road Blockage Emergency", TaskType.Emergency, "Emergency Response",
            $"Vehicle {blockedVehicle.GetVehicleName()} is blocked by flood while {phase} with {originalDelivery.quantity} {cargoLabel}.");

        roadBlockageTask.taskImage        = vehicleDamageImage;
        roadBlockageTask.taskOfficer      = TaskOfficer.LodgingMassCare;
        roadBlockageTask.roundsRemaining  = 2;
        roadBlockageTask.hasRealTimeLimit = false;

        roadBlockageTask.impacts.Add(new TaskImpact(ImpactType.Satisfaction, -20, false, "Emergency Penalty"));

        Sprite icon = TaskSystem.Instance.foodMassCareSprite;
        roadBlockageTask.agentMessages.Add(new AgentMessage(
            $"Emergency! Vehicle {blockedVehicle.GetVehicleName()} is blocked by flooding while {phase}.", icon));

        // Only true if the vehicle had actually picked the cargo up — while still en route to
        // pick-up, it isn't "carrying" anything yet. The type-specific choice builders below
        // (CreateFoodBlockageChoices / CreatePopulationUnloadedChoices) already explain the
        // not-yet-loaded case, so nothing is lost by omitting this line then.
        if (hasLoadedCargo)
        {
            roadBlockageTask.agentMessages.Add(new AgentMessage(
                $"It was carrying {originalDelivery.quantity} {cargoLabel} from {srcName} to {dstName}.", icon));
        }

        if (originalDelivery.cargoType == ResourceType.FoodPacks)
        {
            roadBlockageTask.affectedFacility = originalDelivery.destinationBuilding.name;
            CreateFoodBlockageChoices(roadBlockageTask, originalDelivery, blockedVehicle, hasLoadedCargo);
        }
        else if (originalDelivery.cargoType == ResourceType.Population)
        {
            if (hasLoadedCargo)
            {
                // ReturnCargoToSource already called unconditionally above.
                roadBlockageTask.affectedFacility = originalDelivery.sourceBuilding.name;
                CreatePopulationLoadedChoices(roadBlockageTask, originalDelivery, icon, srcName);
            }
            else
            {
                CreatePopulationUnloadedChoices(roadBlockageTask, originalDelivery, icon, srcName, dstName);
            }
        }

        // Track loaded state so expiry handler can apply the correct penalty
        if (originalDelivery.cargoType == ResourceType.Population)
            blockageTaskLoadedState[roadBlockageTask.taskId] = hasLoadedCargo;

        if (showDebugInfo)
            Debug.Log($"[FloodTaskGenerator] Road blockage task created for {blockedVehicle.GetVehicleName()} ({phase})");
        GameLogPanel.Instance?.LogTaskEvent($"Road blockage: {blockedVehicle.GetVehicleName()} ({phase})");
    }

    // Food already on the blocked vehicle (if any) is treated as spoiled/discarded — it cannot be
    // recovered, so the only path forward is an emergency immediate delivery of a fresh batch,
    // priced per meal needed rather than a flat fee. If the vehicle hadn't picked the food up yet,
    // nothing was actually on board, so nothing went to waste — only the delivery itself failed.
    void CreateFoodBlockageChoices(GameTask task, DeliveryTask originalDelivery, Vehicle blockedVehicle, bool hasLoadedCargo)
    {
        // DiscardVehicleCargo already called unconditionally in CreateRoadBlockageTask above
        // (it's a no-op when the vehicle had nothing loaded).

        string wasteMessage = hasLoadedCargo
            ? $"The {originalDelivery.quantity} meals already on board have gone to waste and cannot be recovered."
            : $"The vehicle had not yet picked up the {originalDelivery.quantity} meals, so nothing was lost — but the delivery itself has failed.";

        task.agentMessages.Add(new AgentMessage(
            $"{wasteMessage} Emergency fast food delivery can make up this shortfall.",
            TaskSystem.Instance.foodMassCareSprite));

        int cost = originalDelivery.quantity * 10;

        AgentChoice fastDeliveryChoice = new AgentChoice(1, $"Emergency fast food delivery (${cost})");
        fastDeliveryChoice.immediateDelivery  = true;
        fastDeliveryChoice.deliveryCargoType  = ResourceType.FoodPacks;
        fastDeliveryChoice.deliveryQuantity   = originalDelivery.quantity;
        fastDeliveryChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -cost, false, "Emergency Service"));
        task.agentChoices.Add(fastDeliveryChoice);
    }

    // Situation 2: vehicle already loaded clients, now blocked.
    // Clients are returned to source immediately; player can pay to teleport them to shelter.
    // If the task expires without action, OnAnyTaskCompleted applies the abandonment penalty.
    void CreatePopulationLoadedChoices(GameTask task, DeliveryTask originalDelivery, Sprite icon, string srcName)
    {
        task.agentMessages.Add(new AgentMessage(
            $"The clients were already on board when the vehicle was stopped. " +
            $"They have been safely escorted back to {srcName} for now.\n\n",
            icon));

        AgentChoice emergencyChoice = new AgentChoice(1, "Arrange emergency transport ($1500) — clients reach shelter immediately");
        emergencyChoice.immediateDelivery   = true;
        emergencyChoice.deliveryCargoType   = ResourceType.Population;
        emergencyChoice.deliveryQuantity    = originalDelivery.quantity;
        emergencyChoice.destinationType     = DeliveryDestinationType.SpecificBuilding;
        emergencyChoice.destinationBuilding = BuildingType.Shelter;
        emergencyChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -1500, false, "Emergency Transport"));
        task.agentChoices.Add(emergencyChoice);
    }

    // Situation 1: vehicle not yet loaded, blocked on the way to pick up.
    // Clients are still at source; dispatch a new vehicle via an alternative route.
    void CreatePopulationUnloadedChoices(GameTask task, DeliveryTask originalDelivery, Sprite icon, string srcName, string dstName)
    {
        task.agentMessages.Add(new AgentMessage(
            $"The clients have not yet been picked up — they are still at {srcName}. " +
            $"Dispatch a new vehicle to take them to {dstName} via an alternative route.",
            icon));

        AgentChoice rerouteChoice = new AgentChoice(1, "Dispatch new vehicle via alternative route");
        rerouteChoice.triggersDelivery        = true;
        rerouteChoice.deliveryCargoType       = originalDelivery.cargoType;
        rerouteChoice.deliveryQuantity        = originalDelivery.quantity;
        rerouteChoice.sourceType              = DeliverySourceType.ManualAssignment;
        rerouteChoice.specificSourceName      = originalDelivery.sourceBuilding.name;
        rerouteChoice.destinationType         = DeliveryDestinationType.ManualAssignment;
        rerouteChoice.specificDestinationName = originalDelivery.destinationBuilding.name;
        task.agentChoices.Add(rerouteChoice);
    }

    /// <summary>
    /// Vehicle Repair Required task — permanently disabled. Flood-damaged vehicles now
    /// self-recover automatically once the flood clears (see RestoreVehiclesClearOfFlood),
    /// so this paid repair task no longer gets created. Kept as a no-op stub (rather than
    /// removed) so existing callers (Vehicle.TriggerVehicleRepairTask, debug context menus
    /// below) don't need to change — restoring this would mean reinstating the task-building
    /// logic that used to live here (title "Vehicle Repair Required", $1200 repair choice,
    /// VEHICLE_ID-tagged description for TaskDetailUI.RepairVehicleById).
    /// </summary>
    public void CreateVehicleRepairTask(Vehicle damagedVehicle)
    {
        // Intentionally does nothing.
    }

    void OnDestroy()
    {
        if (FloodSystem.Instance != null)
        {
            FloodSystem.Instance.OnFloodTileAdded -= OnFloodExpanded;
            FloodSystem.Instance.OnFloodTileRemoved -= OnFloodTileRemoved;
        }

        if (TaskSystem.Instance != null)
            TaskSystem.Instance.OnTaskCompleted -= OnAnyTaskCompleted;
    }

    void OnAnyTaskCompleted(GameTask task)
    {
        if (!blockageTaskLoadedState.TryGetValue(task.taskId, out bool wasLoaded)) return;
        blockageTaskLoadedState.Remove(task.taskId);

        if (wasLoaded && (task.status == TaskStatus.Expired || task.status == TaskStatus.Incomplete))
        {
            SatisfactionAndBudget.Instance?.RemoveSatisfaction(30, "Abandoned Clients");
            ToastManager.ShowToast(
                "Clients gave up waiting and returned to their origin. Satisfaction severely impacted.",
                ToastType.Warning, true);
            GameLogPanel.Instance?.LogPlayerAction(
                "Flood blockage: clients returned to origin due to inaction.");
        }
    }
    

    // ─────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────

    void ReturnCargoToSource(Vehicle vehicle, DeliveryTask delivery)
    {
        if (vehicle == null || delivery?.sourceBuilding == null) return;

        int amount = vehicle.GetCargoAmount(delivery.cargoType);
        if (amount <= 0) return;

        MonoBehaviour src = delivery.sourceBuilding;

        BuildingResourceStorage storage = null;
        PrebuiltBuilding pb = src.GetComponent<PrebuiltBuilding>();
        if (pb != null)
            storage = pb.GetResourceStorage();
        else
            storage = src.GetComponent<Building>()?.GetComponent<BuildingResourceStorage>()
                   ?? src.GetComponent<BuildingResourceStorage>();

        if (storage == null)
        {
            Debug.LogWarning("[FloodTaskGenerator] Could not find source storage to return cargo");
            return;
        }

        storage.AddResource(delivery.cargoType, amount);
        vehicle.ClearAllCargo();

        if (showDebugInfo)
            Debug.Log($"[FloodTaskGenerator] Returned {amount} {delivery.cargoType} to {src.name}");
    }

    /// <summary>
    /// Food already loaded on a blocked vehicle can't be salvaged — it spoils. Clears the cargo
    /// and records it as waste (same accounting as food lost to a day change or overnight cancel).
    /// </summary>
    void DiscardVehicleCargo(Vehicle vehicle, DeliveryTask delivery)
    {
        if (vehicle == null) return;

        int amount = vehicle.GetCargoAmount(delivery.cargoType);
        if (amount <= 0) return;

        vehicle.ClearAllCargo();

        if (delivery.cargoType == ResourceType.FoodPacks && DailyReportData.Instance != null)
        {
            DailyReportData.Instance.RecordFoodWasted(amount);
            DailyReportData.Instance.RecordFoodWasteCumulative(amount);
        }

        if (showDebugInfo)
            Debug.Log($"[FloodTaskGenerator] Discarded {amount} {delivery.cargoType} from blocked vehicle {vehicle.GetVehicleName()}");
        GameLogPanel.Instance?.LogResourceChange($"[FloodTaskGenerator] Discarded {amount} {delivery.cargoType} from blocked vehicle {vehicle.GetVehicleName()}");
    }

    static string GetBuildingDisplayName(MonoBehaviour building)
    {
        if (building == null) return "Unknown";
        PrebuiltBuilding pb = building.GetComponent<PrebuiltBuilding>();
        if (pb != null) return pb.GetBuildingName();
        Building b = building.GetComponent<Building>();
        if (b != null) return b.GetDisplayName();
        return building.name;
    }

    [ContextMenu("Test: Force Road Blockage")]
    public void TestForceRoadBlockage()
    {
        Vehicle testVehicle = FindObjectOfType<Vehicle>();
        Building[] buildings = FindObjectsOfType<Building>();
        
        if (testVehicle == null || buildings.Length < 2)
        {
            Debug.LogWarning("Need vehicle and buildings for road blockage test");
            return;
        }
        
        // Create test delivery task
        DeliveryTask testDelivery = new DeliveryTask(
            buildings[0], buildings[1], 
            ResourceType.FoodPacks, 8, 997);
        
        CreateRoadBlockageTask(testVehicle, testDelivery);
        Debug.Log("Force-created road blockage task");
    }

    [ContextMenu("Test: Damage All Vehicles")]
    public void TestDamageAllVehicles()
    {
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>();

        foreach (Vehicle vehicle in vehicles)
        {
            vehicle.isDamaged = true;
            vehicle.SetStatus(VehicleStatus.Damaged);
        }

        Debug.Log($"Damaged {vehicles.Length} vehicles (Vehicle Repair task is disabled — use FloodSystem's 'Clear All Flood' or 'Debug: Flood Entire Map' + clear to test auto-recovery)");
    }
}