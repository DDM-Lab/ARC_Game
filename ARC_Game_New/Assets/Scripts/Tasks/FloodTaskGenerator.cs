using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class FloodTaskGenerator : MonoBehaviour
{
    [Header("Emergency Task Configuration")]
    public bool enableFloodTasks = true;
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
            FloodSystem.Instance.OnFloodTileAdded += OnFloodExpanded;

        if (TaskSystem.Instance != null)
            TaskSystem.Instance.OnTaskCompleted += OnAnyTaskCompleted;
    }

    void OnFloodExpanded(Vector3Int floodPosition)
    {
        // Check if any vehicles are now blocked by this new flood tile
        CheckForBlockedVehicles();
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
        if (!enableFloodTasks || TaskSystem.Instance == null)
        {
            if (showDebugInfo)
                Debug.Log("Flood tasks disabled or TaskSystem not found");
            return;
        }

        if (blockedVehicle == null || originalDelivery == null)
        {
            if (showDebugInfo)
                Debug.LogWarning("Cannot create road blockage task - missing vehicle or delivery");
            return;
        }

        bool hasLoadedCargo = blockedVehicle.GetCargoAmount(originalDelivery.cargoType) > 0;
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
        roadBlockageTask.agentMessages.Add(new AgentMessage(
            $"It was carrying {originalDelivery.quantity} {cargoLabel} from {srcName} to {dstName}.", icon));

        if (originalDelivery.cargoType == ResourceType.FoodPacks)
        {
            CreateFoodBlockageChoices(roadBlockageTask, originalDelivery, blockedVehicle);
        }
        else if (originalDelivery.cargoType == ResourceType.Population)
        {
            if (hasLoadedCargo)
            {
                ReturnCargoToSource(blockedVehicle, originalDelivery);
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

    void CreateFoodBlockageChoices(GameTask task, DeliveryTask originalDelivery, Vehicle blockedVehicle)
    {
        // Choice 1: Find alternative route (if possible)
        AgentChoice altRouteChoice = new AgentChoice(1, "Find alternative route (may take longer)");
        altRouteChoice.triggersDelivery = true;
        altRouteChoice.deliveryCargoType = originalDelivery.cargoType;
        altRouteChoice.deliveryQuantity = originalDelivery.quantity;
        altRouteChoice.sourceType = DeliverySourceType.ManualAssignment;
        altRouteChoice.specificSourceName = originalDelivery.sourceBuilding.name;
        altRouteChoice.destinationType = DeliveryDestinationType.ManualAssignment;
        altRouteChoice.specificDestinationName = originalDelivery.destinationBuilding.name;
        altRouteChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 5, false, "Problem Solved"));
        task.agentChoices.Add(altRouteChoice);

        // Choice 2: Send from different kitchen
        AgentChoice altSourceChoice = new AgentChoice(2, "Send food from nearest available kitchen");
        altSourceChoice.triggersDelivery = true;
        altSourceChoice.deliveryCargoType = originalDelivery.cargoType;
        altSourceChoice.deliveryQuantity = originalDelivery.quantity;
        altSourceChoice.sourceType = DeliverySourceType.SpecificBuilding;
        altSourceChoice.sourceBuilding = BuildingType.Kitchen;
        altSourceChoice.destinationType = DeliveryDestinationType.ManualAssignment;
        altSourceChoice.specificDestinationName = originalDelivery.destinationBuilding.name;
        altSourceChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -200, false, "Extra Transport Cost"));
        altSourceChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 8, false, "Quick Resolution"));
        task.agentChoices.Add(altSourceChoice);

        // Choice 3: Emergency fast food delivery (expensive)
        AgentChoice fastDeliveryChoice = new AgentChoice(3, "Emergency fast food delivery ($1000)");
        fastDeliveryChoice.triggersDelivery = false; // No vehicle needed
        fastDeliveryChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -1000, false, "Emergency Service"));
        fastDeliveryChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 15, false, "Immediate Relief"));
        task.agentChoices.Add(fastDeliveryChoice);

        // Choice 4: Wait for flood to recede
        AgentChoice waitChoice = new AgentChoice(4, "Wait for flood to recede (high dissatisfaction)");
        waitChoice.triggersDelivery = false;
        waitChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -30, false, "Delayed Response"));
        task.agentChoices.Add(waitChoice);
    }

    // Situation 2: vehicle already loaded clients, now blocked.
    // Clients are returned to source immediately; player can pay to teleport them to shelter.
    // If the task expires without action, OnAnyTaskCompleted applies the abandonment penalty.
    void CreatePopulationLoadedChoices(GameTask task, DeliveryTask originalDelivery, Sprite icon, string srcName)
    {
        task.agentMessages.Add(new AgentMessage(
            $"The clients were already on board when the vehicle was stopped. " +
            $"They have been safely escorted back to {srcName} for now.\n\n" +
            $"⚠ If no emergency transport is arranged in time, the clients will give up and remain at {srcName} — " +
            $"this will severely impact satisfaction.",
            icon));

        AgentChoice emergencyChoice = new AgentChoice(1, "Arrange emergency transport ($1500) — clients reach shelter immediately");
        emergencyChoice.immediateDelivery   = true;
        emergencyChoice.deliveryCargoType   = ResourceType.Population;
        emergencyChoice.deliveryQuantity    = originalDelivery.quantity;
        emergencyChoice.destinationType     = DeliveryDestinationType.SpecificBuilding;
        emergencyChoice.destinationBuilding = BuildingType.Shelter;
        emergencyChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -1500, false, "Emergency Transport"));
        emergencyChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 10, false, "Safe Arrival"));
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
        rerouteChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 5, false, "Resolved"));
        task.agentChoices.Add(rerouteChoice);
    }

    /// <summary>
    /// Create vehicle repair task
    /// </summary>
    public void CreateVehicleRepairTask(Vehicle damagedVehicle)
    {
        if (!enableFloodTasks || TaskSystem.Instance == null) return;

        // Check if repair task already exists for this vehicle (double-check)
        var activeTasks = TaskSystem.Instance.GetAllActiveTasks();
        bool repairTaskExists = activeTasks.Any(t =>
            t.taskTitle.Contains("Vehicle Repair") &&
            t.description.Contains(damagedVehicle.GetVehicleName()));

        if (repairTaskExists)
        {
            if (showDebugInfo)
                Debug.Log($"Repair task already exists for vehicle {damagedVehicle.GetVehicleName()}");
            return;
        }

        string taskTitle = "Vehicle Repair Required";
        string description = $"Vehicle {damagedVehicle.GetVehicleName()} has been damaged by flood and requires repair before it can operate again.";

        GameTask repairTask = TaskSystem.Instance.CreateTask(
            taskTitle, TaskType.Emergency, "Maintenance", description);

        repairTask.taskImage = vehicleDamageImage;
        repairTask.taskOfficer = TaskOfficer.LodgingMassCare;

        // Longer time for repair tasks
        repairTask.roundsRemaining = 2;
        repairTask.hasRealTimeLimit = false;

        // Add impacts
        repairTask.impacts.Add(new TaskImpact(ImpactType.Budget, -800, false, "Repair Cost"));
        repairTask.impacts.Add(new TaskImpact(ImpactType.Workforce, 2, false, "Repair Crew"));

        // Add agent messages
        repairTask.agentMessages.Add(new AgentMessage($"Vehicle {damagedVehicle.GetVehicleName()} needs repair after flood damage.", TaskSystem.Instance.foodMassCareSprite));
        repairTask.agentMessages.Add(new AgentMessage("We can either repair it now or wait, but the vehicle won't be available until fixed.", TaskSystem.Instance.foodMassCareSprite));

        // Add repair choices
        AgentChoice immediateRepairChoice = new AgentChoice(1, "Repair immediately ($1200)");
        immediateRepairChoice.triggersDelivery = false;
        immediateRepairChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -1200, false, "Repair Cost"));
        repairTask.agentChoices.Add(immediateRepairChoice);

        AgentChoice delayRepairChoice = new AgentChoice(2, "Delay repair (vehicle remains unavailable, Satisfaction - 5)");
        delayRepairChoice.triggersDelivery = false;
        delayRepairChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -5, false, "Reduced Capacity"));
        repairTask.agentChoices.Add(delayRepairChoice);

        // Store vehicle reference for later repair
        repairTask.description += $"|VEHICLE_ID:{damagedVehicle.GetVehicleId()}";

        if (showDebugInfo)
            Debug.Log($"Created vehicle repair task for {damagedVehicle.GetVehicleName()}");
        GameLogPanel.Instance.LogTaskEvent($"Created vehicle repair task for {damagedVehicle.GetVehicleName()}");
    }

    void OnDestroy()
    {
        if (FloodSystem.Instance != null)
            FloodSystem.Instance.OnFloodTileAdded -= OnFloodExpanded;

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

    [ContextMenu("Test: Force Vehicle Repair")]
    public void TestForceVehicleRepair()
    {
        Vehicle testVehicle = FindObjectOfType<Vehicle>();
        if (testVehicle == null)
        {
            Debug.LogWarning("No vehicle found for repair test");
            return;
        }
        
        CreateVehicleRepairTask(testVehicle);
        Debug.Log($"Force-created vehicle repair task for {testVehicle.GetVehicleName()}");
    }

    [ContextMenu("Test: Damage All Vehicles")]
    public void TestDamageAllVehicles()
    {
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
        
        foreach (Vehicle vehicle in vehicles)
        {
            vehicle.isDamaged = true;
            vehicle.SetStatus(VehicleStatus.Damaged);
            CreateVehicleRepairTask(vehicle);
        }
        
        Debug.Log($"Damaged {vehicles.Length} vehicles and created repair tasks");
    }
}