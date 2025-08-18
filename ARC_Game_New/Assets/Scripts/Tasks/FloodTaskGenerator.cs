using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class FloodTaskGenerator : MonoBehaviour
{
    [Header("Emergency Task Configuration")]
    public bool enableFloodTasks = true;
    public TaskDatabase emergencyTaskDatabase;

    [Header("Debug")]
    public bool showDebugInfo = true;

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
        // Subscribe to flood events for automatic task generation
        if (FloodSystem.Instance != null)
        {
            FloodSystem.Instance.OnFloodTileAdded += OnFloodExpanded;
        }
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
        

        string taskTitle = "Road Blockage Emergency";
        string description = $"Vehicle {blockedVehicle.GetVehicleName()} is blocked by flood while transporting {originalDelivery.quantity} {originalDelivery.cargoType}. Choose how to handle this emergency.";

        GameTask roadBlockageTask = TaskSystem.Instance.CreateTask(
            taskTitle, TaskType.Emergency, "Emergency Response", description);

        // Set tight timing for emergency
        roadBlockageTask.roundsRemaining = 2;
        roadBlockageTask.hasRealTimeLimit = false;

        // Add impacts
        roadBlockageTask.impacts.Add(new TaskImpact(ImpactType.Satisfaction, -20, false, "Emergency Penalty"));
        roadBlockageTask.impacts.Add(new TaskImpact(ImpactType.TotalTime, 1, false, "Urgent Response"));

        // Add agent messages
        roadBlockageTask.agentMessages.Add(new AgentMessage($"Emergency! Vehicle {blockedVehicle.GetVehicleName()} is blocked by flood!", TaskSystem.Instance.foodMassCareSprite));
        roadBlockageTask.agentMessages.Add(new AgentMessage($"The vehicle was transporting {originalDelivery.quantity} {originalDelivery.cargoType} from {originalDelivery.sourceBuilding.name} to {originalDelivery.destinationBuilding.name}.", TaskSystem.Instance.foodMassCareSprite));
        roadBlockageTask.agentMessages.Add(new AgentMessage("We need to decide how to handle this situation immediately.", TaskSystem.Instance.foodMassCareSprite));

        // Create choices based on cargo type
        if (originalDelivery.cargoType == ResourceType.FoodPacks)
        {
            CreateFoodBlockageChoices(roadBlockageTask, originalDelivery, blockedVehicle);
        }
        else if (originalDelivery.cargoType == ResourceType.Population)
        {
            CreatePopulationBlockageChoices(roadBlockageTask, originalDelivery, blockedVehicle);
        }

        if (showDebugInfo)
            Debug.Log($"Created road blockage emergency task for vehicle {blockedVehicle.GetVehicleName()}");
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

    void CreatePopulationBlockageChoices(GameTask task, DeliveryTask originalDelivery, Vehicle blockedVehicle)
    {
        // Choice 1: Find alternative route
        AgentChoice altRouteChoice = new AgentChoice(1, "Find alternative route for people transport");
        altRouteChoice.triggersDelivery = true;
        altRouteChoice.deliveryCargoType = originalDelivery.cargoType;
        altRouteChoice.deliveryQuantity = originalDelivery.quantity;
        altRouteChoice.sourceType = DeliverySourceType.ManualAssignment;
        altRouteChoice.specificSourceName = originalDelivery.sourceBuilding.name;
        altRouteChoice.destinationType = DeliveryDestinationType.ManualAssignment;
        altRouteChoice.specificDestinationName = originalDelivery.destinationBuilding.name;
        altRouteChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 5, false, "Safe Transport"));
        task.agentChoices.Add(altRouteChoice);

        // Choice 2: Emergency helicopter evacuation (very expensive)
        AgentChoice helicopterChoice = new AgentChoice(2, "Emergency helicopter evacuation ($2000)");
        helicopterChoice.triggersDelivery = false; // Direct resolution
        helicopterChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -2000, false, "Helicopter Service"));
        helicopterChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 20, false, "Heroic Response"));
        task.agentChoices.Add(helicopterChoice);

        // Choice 3: Temporary shelter at current location
        AgentChoice shelterChoice = new AgentChoice(3, "Set up temporary shelter ($500)");
        shelterChoice.triggersDelivery = false;
        shelterChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -500, false, "Temporary Shelter"));
        shelterChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -10, false, "Suboptimal Solution"));
        task.agentChoices.Add(shelterChoice);

        // Choice 4: Wait (worst option for people)
        AgentChoice waitChoice = new AgentChoice(4, "Wait for flood to recede (people at risk)");
        waitChoice.triggersDelivery = false;
        waitChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -40, false, "People in Danger"));
        task.agentChoices.Add(waitChoice);
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
        AgentChoice immediateRepairChoice = new AgentChoice(1, "Repair immediately ($800, 2 workforce)");
        immediateRepairChoice.triggersDelivery = false;
        immediateRepairChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -800, false, "Repair Cost"));
        immediateRepairChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Workforce, -2, false, "Crew Assignment"));
        repairTask.agentChoices.Add(immediateRepairChoice);
        
        AgentChoice delayRepairChoice = new AgentChoice(2, "Delay repair (vehicle remains unavailable)");
        delayRepairChoice.triggersDelivery = false;
        delayRepairChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -5, false, "Reduced Capacity"));
        repairTask.agentChoices.Add(delayRepairChoice);
        
        // Store vehicle reference for later repair
        repairTask.description += $"|VEHICLE_ID:{damagedVehicle.GetVehicleId()}";
        
        if (showDebugInfo)
            Debug.Log($"Created vehicle repair task for {damagedVehicle.GetVehicleName()}");
    }

    void OnDestroy()
    {
        if (FloodSystem.Instance != null)
        {
            FloodSystem.Instance.OnFloodTileAdded -= OnFloodExpanded;
        }
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