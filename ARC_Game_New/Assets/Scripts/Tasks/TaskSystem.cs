using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

public enum TaskType
{
    Emergency,
    Demand, 
    Advisory,
    Alert
}

public enum TaskStatus
{
    Active, // Task is currently active
    InProgress, // Task is being worked on
    Incomplete, // Task failed to complete (e.g., lack of resource, didn't deliver on time)
    Expired, // Task expired without being completed (e.g., no action taken)
    Completed // Task completed successfully
}

public enum ImpactType
{
    Satisfaction,
    Budget,
    FoodPacks,
    Clients,
    Workforce,
    TotalTime,
    TrainingTime,
    TotalCosts,
    TotalLodging
}

public enum DeliverySourceType
{
    AutoFind,
    SpecificBuilding,
    SpecificPrebuilt,
    RequestingFacility,
    ManualAssignment
}

public enum DeliveryQuantityType
{
    Fixed,          // Use deliveryQuantity value
    Percentage,     // Use deliveryPercentage of available resources
    All             // Move all available resources
}

public enum DeliveryDestinationType
{
    AutoFind,
    SpecificBuilding,
    SpecificPrebuilt,
    RequestingFacility,
    ManualAssignment
}

public enum TaskOfficer
{
    DisasterOfficer,    // Default
    WorkforceService,
    LodgingMassCare,
    ExternalRelationship,
    FoodMassCare
}

[System.Serializable]
public class TaskImpact
{
    public ImpactType impactType;
    public int value;
    public bool isCountdown = false;
    public string customLabel = "";
    
    public TaskImpact(ImpactType type, int val, bool countdown = false, string label = "")
    {
        impactType = type;
        value = val;
        isCountdown = countdown;
        customLabel = label;
    }
}

[System.Serializable]
public class GameTask
{
    public int taskId;
    public string taskTitle;
    public TaskType taskType;
    public TaskStatus status;
    public string affectedFacility;
    public string description;
    public Sprite taskImage;

    [Header("Task Officer")]
    public TaskOfficer taskOfficer = TaskOfficer.DisasterOfficer;
    
    [Header("Timing")]
    public int roundsRemaining = 3; // Rounds until expiry
    public float realTimeRemaining = 300f; // Real-time countdown in seconds
    public bool hasRealTimeLimit = false;
    
    [Header("Impacts")]
    public List<TaskImpact> impacts = new List<TaskImpact>();
    
    [Header("Agent Conversation")]
    public List<AgentMessage> agentMessages = new List<AgentMessage>();
    public List<AgentChoice> agentChoices = new List<AgentChoice>();
    public List<AgentNumericalInput> numericalInputs = new List<AgentNumericalInput>();

    [Header("Delivery Link")]
    public float deliveryTimeLimit = 300f;
    public float deliveryFailureSatisfactionPenalty = 10f;
    public List<int> linkedDeliveryTaskIds = new List<int>(); // support multiple linked delivery tasks

    public float timeCreated;
    public bool isExpired 
    { 
        get 
        {
            // rounds remaining check
            if (roundsRemaining <= 0) return true;
            
            // realtime check, only if has real-time limit and is in simulation
            if (hasRealTimeLimit && GlobalClock.Instance != null && 
                GlobalClock.Instance.IsSimulationRunning() && realTimeRemaining <= 0)
            {
                return true;
            }
            
            return false;
        }
    }

    public GameTask(int id, string title, TaskType type, string facility)
    {
        taskId = id;
        taskTitle = title;
        taskType = type;
        affectedFacility = facility;
        status = TaskStatus.Active;
        timeCreated = Time.time;
    }
}

[System.Serializable]
public class AgentMessage
{
    public string messageText;
    public Sprite agentAvatar;
    public bool useTypingEffect = true;
    public float typingSpeed = 0.5f;
    
    public AgentMessage(string text, Sprite avatar = null)
    {
        messageText = text;
        agentAvatar = avatar;
    }
}

[System.Serializable]
public class AgentChoice
{
    public int choiceId;
    public string choiceText;
    public List<TaskImpact> choiceImpacts = new List<TaskImpact>();
    public bool isSelected = false;
    
    [Header("Delivery Configuration")]
    public bool triggersDelivery = false;
    public bool immediateDelivery = false;
    public ResourceType deliveryCargoType = ResourceType.Population;

    [Header("Multi-Delivery Options")]
    public bool enableMultipleDeliveries = false;
    public MultiDeliveryType multiDeliveryType = MultiDeliveryType.SingleSourceMultiDest;
    public enum MultiDeliveryType
    {
        SingleSourceSingleDest,     // Current behavior
        SingleSourceMultiDest,      // One source → Multiple destinations  
        MultiSourceSingleDest,      // Multiple sources → One destination
        MultiSourceMultiDest        // Multiple sources → Multiple destinations
    }

    [Header("Dynamic Delivery Quantity")]
    public DeliveryQuantityType quantityType = DeliveryQuantityType.Fixed;
    public int deliveryQuantity = 5; // Keep existing field for fixed amounts
    public float deliveryPercentage = 100f; // For percentage-based
    public bool deliverAll = false; // For "all available" option

    [Header("Delivery Source")]
    public DeliverySourceType sourceType = DeliverySourceType.RequestingFacility;
    public BuildingType sourceBuilding = BuildingType.Community;
    public PrebuiltBuildingType sourcePrebuilt = PrebuiltBuildingType.Community;
    public string specificSourceName = ""; // Use name instead of direct reference
    
    [Header("Delivery Destination")]
    public DeliveryDestinationType destinationType = DeliveryDestinationType.SpecificPrebuilt;
    public BuildingType destinationBuilding = BuildingType.Shelter;
    public PrebuiltBuildingType destinationPrebuilt = PrebuiltBuildingType.Motel;
    public string specificDestinationName = ""; // Use name instead of direct reference
    
    [Header("Distance Priority")]
    public bool prioritizeNearestSource = true;
    public bool prioritizeNearestDestination = true;
    
    public AgentChoice(int id, string text)
    {
        choiceId = id;
        choiceText = text;
    }
}

[System.Serializable]
public class AgentNumericalInput
{
    public int inputId;
    public string inputLabel;
    public int currentValue;
    public int minValue;
    public int maxValue;
    public int stepSize = 1;

    public AgentNumericalInput(int id, string label, int current, int min, int max)
    {
        inputId = id;
        inputLabel = label;
        currentValue = current;
        minValue = min;
        maxValue = max;
    }
}

[System.Serializable]
public class PlayerMessage
{
    public string messageText;
    public float timeStamp;
    
    public PlayerMessage(string text)
    {
        messageText = text;
        timeStamp = Time.time;
    }
}

public class TaskSystem : MonoBehaviour
{
    [Header("Task Management")]
    public List<GameTask> activeTasks = new List<GameTask>();
    public List<GameTask> completedTasks = new List<GameTask>();

    [Header("UI References")]
    public TaskCenterUI taskCenterUI;
    public TaskDetailUI taskDetailUI;

    [Header("Default Assets")]
    public Sprite defaultTaskImage;
    
        [Header("Agent Icons")]
    public Sprite defaultAgentSprite;
    public Sprite workforceServiceSprite;
    public Sprite lodgingMassCareSprite;
    public Sprite externalRelationshipSprite;
    public Sprite foodMassCareSprite;

    [Header("Task Database")]
    public TaskDatabase taskDatabase;

    [Header("Auto Task Generation")]
    public bool enableAutoTaskGeneration = true;

    [Header("Debug")]
    public bool showDebugInfo = true;

    // Task ID counter
    private int nextTaskId = 1;

    // Events
    public event Action<GameTask> OnTaskCreated;
    public event Action<GameTask> OnTaskCompleted;
    public event Action<GameTask> OnTaskExpired;

    // Singleton
    public static TaskSystem Instance { get; private set; }

    private HashSet<string> shownAlertIds = new HashSet<string>();

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
        // Subscribe to global clock for round-based countdown
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged += OnTimeSegmentAdvanced;
        }
        // Subscribe to round changes for task generation
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;
        }

        // Listen for delivery task completion events
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem != null)
        {
            deliverySystem.OnTaskCompleted += OnDeliveryTaskCompleted;
            Debug.Log("TaskSystem subscribed to DeliverySystem events");
        }
        else
        {
            Debug.LogError("DeliverySystem not found! Delivery tasks will not be tracked.");
        }

        if (showDebugInfo)
            Debug.Log("Task System initialized");
    }

    void Update()
    {
        // Update real-time countdowns
        UpdateRealTimeCountdowns();

        // Check for expired tasks
        CheckExpiredTasks();
    }

    void UpdateRealTimeCountdowns()
    {
        foreach (GameTask task in activeTasks)
        {
            if (task.hasRealTimeLimit && task.realTimeRemaining > 0)
            {
                // Only countdown when game is not paused
                if (GlobalClock.Instance != null && GlobalClock.Instance.IsSimulationRunning())
                {
                    task.realTimeRemaining -= Time.unscaledDeltaTime;
                }
            }
        }
    }

    void CheckExpiredTasks()
    {
        List<GameTask> expiredTasks = activeTasks.Where(t => t.roundsRemaining <= 0 || 
            (t.hasRealTimeLimit && t.realTimeRemaining <= 0)).ToList();
        
        foreach (GameTask task in expiredTasks)
        {
            // check if task has delivery unfinished
            if (task.status == TaskStatus.InProgress)
            {
                // cancel related delivery tasks
                CancelTaskDeliveries(task);

                // mark as incomplete (has delivery but not finished)
                SetTaskIncomplete(task);
            }
            else
            {
                // regular expiration handling
                ExpireTask(task);
            }
        }
    }

    /// <summary>
    /// Cancel all delivery tasks related to a game task
    /// </summary>
    void CancelTaskDeliveries(GameTask task)
    {
        if (task.linkedDeliveryTaskIds == null || task.linkedDeliveryTaskIds.Count == 0)
            return;
        
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return;
        
        int cancelledCount = 0;
        foreach (int taskId in task.linkedDeliveryTaskIds)
        {
            if (deliverySystem.CancelDeliveryTask(taskId))
            {
                cancelledCount++;
            }
        }
        
        if (showDebugInfo)
            Debug.Log($"Cancelled {cancelledCount} delivery tasks for expired game task: {task.taskTitle}");
    }

    void OnTimeSegmentAdvanced(int newSegment)
    {
        // Reduce rounds remaining for all active tasks
        foreach (GameTask task in activeTasks)
        {
            if (task.roundsRemaining > 0)
            {
                task.roundsRemaining--;

                if (showDebugInfo)
                    Debug.Log($"Task '{task.taskTitle}' rounds remaining: {task.roundsRemaining}");
            }
        }
        CheckForUnrepairedVehicles();
    }

    /// <summary>
    /// Check for damaged vehicles that don't have repair tasks and create them
    /// </summary>
    void CheckForUnrepairedVehicles()
    {
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
        
        foreach (Vehicle vehicle in vehicles)
        {
            if (vehicle.GetCurrentStatus() == VehicleStatus.Damaged)
            {
                // Check if this vehicle already has a repair task
                bool hasRepairTask = HasVehicleRepairTask(vehicle);
                
                if (!hasRepairTask)
                {
                    // Create repair task for this vehicle
                    if (FloodTaskGenerator.Instance != null)
                    {
                        FloodTaskGenerator.Instance.CreateVehicleRepairTask(vehicle);
                        
                        if (showDebugInfo)
                            Debug.Log($"Created repair task for damaged vehicle: {vehicle.GetVehicleName()}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Check if a vehicle already has an active repair task
    /// </summary>
    bool HasVehicleRepairTask(Vehicle vehicle)
    {
        foreach (GameTask task in activeTasks)
        {
            if (task.taskTitle.Contains("Vehicle Repair") && 
                task.description.Contains(vehicle.GetVehicleName()))
            {
                return true;
            }
        }
        
        return false;
    }

    void OnDeliveryTaskCompleted(DeliveryTask deliveryTask)
    {
        // find any active tasks that are linked to this delivery task
        GameTask gameTask = activeTasks.FirstOrDefault(t => 
            t.linkedDeliveryTaskIds != null && t.linkedDeliveryTaskIds.Contains(deliveryTask.taskId));
        
        if (gameTask != null && gameTask.status == TaskStatus.InProgress)
        {
            // check if all deliveries are completed
            DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
            List<DeliveryTask> completedTasks = deliverySystem.GetCompletedTasks();
            
            bool allCompleted = gameTask.linkedDeliveryTaskIds.All(id => 
                completedTasks.Any(ct => ct.taskId == id));
            
            if (allCompleted)
            {
                CompleteTask(gameTask);
                
                if (showDebugInfo)
                    Debug.Log($"All deliveries completed for task: {gameTask.taskTitle}");
            }
        }
    }

    public void HandleDeliveryFailure(GameTask task)
    {
        if (task == null)
        {
            if (showDebugInfo)
                Debug.LogWarning("HandleDeliveryFailure called with null task - skipping");
            return;
        }
        
        if (task.status == TaskStatus.InProgress)
        {
            task.status = TaskStatus.Incomplete;
            activeTasks.Remove(task);
            completedTasks.Add(task);
            
            // Apply penalties for delivery failure
            if (SatisfactionAndBudget.Instance != null && task.deliveryFailureSatisfactionPenalty > 0)
            {
                SatisfactionAndBudget.Instance.RemoveSatisfaction(task.deliveryFailureSatisfactionPenalty);
            }
            
            OnTaskCompleted?.Invoke(task);
            
            if (showDebugInfo)
                Debug.Log($"Task marked incomplete due to delivery failure: {task.taskTitle}. Satisfaction penalty: {task.deliveryFailureSatisfactionPenalty}");
        }
    }

    void OnRoundChanged(int newSegment)
    {
        Debug.Log($"OnRoundChanged called: segment {newSegment}, auto generation: {enableAutoTaskGeneration}");
        // Check for new tasks at the start of each round
        if (enableAutoTaskGeneration)
        {
            Debug.Log("Attempting to generate tasks from database...");
            GenerateTasksFromDatabase();
        }
    }

    void GenerateTasksFromDatabase()
    {
        if (taskDatabase == null)
        {
            Debug.LogError("TaskDatabase is null! Cannot generate tasks.");
            return;
        }

        Debug.Log("Checking for triggered tasks...");
        List<TaskData> triggeredTasks = taskDatabase.CheckTriggeredTasks();
        Debug.Log($"Found {triggeredTasks.Count} triggered tasks");
        
        foreach (TaskData taskData in triggeredTasks)
        {
            if (taskData == null)
            {
                Debug.LogWarning("Found null TaskData in database");
                continue;
            }

            // Skip if this alert was already shown
            if (taskData.taskType == TaskType.Alert)
            {
                if (shownAlertIds.Contains(taskData.taskId))
                {
                    Debug.Log($"Alert {taskData.taskTitle} already shown, skipping");
                    continue;
                }
                else
                {
                    shownAlertIds.Add(taskData.taskId);
                }
            }

            // Check if task already exists to avoid duplicates
            if (activeTasks.Any(t => t.taskTitle == taskData.taskTitle))
            {
                Debug.Log($"Task {taskData.taskTitle} already exists, skipping");
                continue;
            }

            Debug.Log($"Creating task: {taskData.taskTitle}");

            CreateTaskFromDatabase(taskData);
        }
    }

    public GameTask CreateTaskFromDatabase(TaskData taskData)
    {
        if (taskData == null)
        {
            Debug.LogError("TaskData is null in CreateTaskFromDatabase");
            return null;
        }

        // Find suitable facility that triggered the task
        MonoBehaviour triggeringFacility = taskDatabase.FindSuitableFacility(taskData);
        string facilityName = triggeringFacility?.name ?? taskData.targetFacilityType.ToString();
        
        GameTask newTask = CreateTaskFromData(taskData);
        if (newTask == null)
        {
            if (showDebugInfo)
                Debug.LogWarning($"Failed to create task from database: {taskData.taskTitle} - insufficient resources or other validation failure");
            return null;
        }

        newTask.affectedFacility = facilityName;

        if (showDebugInfo)
            Debug.Log($"Generated task from database: {taskData.taskId} for facility {facilityName}");
        
        return newTask;
    }

    // Enhanced facility finding methods
    MonoBehaviour FindNearestBuilding(BuildingType buildingType, Vector3? referencePosition, 
                                ResourceType cargoType, bool isSource)
    {
        Debug.Log($"=== FIND NEAREST BUILDING DEBUG ===");
        Debug.Log($"Looking for: {buildingType}, Cargo: {cargoType}, IsSource: {isSource}");
        
        Building[] buildings = FindObjectsOfType<Building>().Where(b => 
            b.GetBuildingType() == buildingType && b.IsOperational()).ToArray();
        
        Debug.Log($"Found {buildings.Length} operational {buildingType} buildings");
        
        Building bestBuilding = null;
        float closestDistance = float.MaxValue;
        
        foreach (Building building in buildings)
        {
            Debug.Log($"Checking building: {building.name}");
            
            // Check if building can provide/accept the cargo
            bool canHandle = CanBuildingHandleCargo(building, cargoType, isSource);
            Debug.Log($"  Can handle cargo: {canHandle}");
            
            if (!canHandle)
                continue;
            
            if (referencePosition.HasValue)
            {
                float distance = Vector3.Distance(building.transform.position, referencePosition.Value);
                Debug.Log($"  Distance: {distance}");
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    bestBuilding = building;
                    Debug.Log($"  New best building: {building.name}");
                }
            }
            else
            {
                Debug.Log($"  No reference position, returning first suitable: {building.name}");
                return building;
            }
        }
        
        Debug.Log($"Final result: {bestBuilding?.name}");
        return bestBuilding;
    }

    MonoBehaviour FindNearestPrebuiltBuilding(PrebuiltBuildingType prebuiltType, Vector3? referencePosition,
                                            ResourceType cargoType, bool isSource)
    {
        PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>().Where(pb =>
            pb.GetPrebuiltType() == prebuiltType).ToArray();
        
        PrebuiltBuilding bestPrebuilt = null;
        float closestDistance = float.MaxValue;
        
        foreach (PrebuiltBuilding prebuilt in prebuilts)
        {
            // Check if prebuilt can provide/accept the cargo
            if (!CanPrebuiltHandleCargo(prebuilt, cargoType, isSource))
                continue;
            
            if (referencePosition.HasValue)
            {
                float distance = Vector3.Distance(prebuilt.transform.position, referencePosition.Value);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    bestPrebuilt = prebuilt;
                }
            }
            else
            {
                return prebuilt; // Return first suitable if no reference position
            }
        }
        
        return bestPrebuilt;
    }

    bool CanBuildingHandleCargo(Building building, ResourceType cargoType, bool isSource)
    {
        Debug.Log($"=== CAN HANDLE CARGO DEBUG ===");
        Debug.Log($"Building: {building.name}, Cargo: {cargoType}, IsSource: {isSource}");
        
        BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();
        if (storage == null)
        {
            Debug.Log($"❌ No BuildingResourceStorage component on {building.name}");
            return false;
        }
        
        if (isSource)
        {
            int available = storage.GetResourceAmount(cargoType);
            Debug.Log($"📦 {building.name} has {available} {cargoType} available (need > 0)");
            return available > 0;
        }
        else
        {
            int space = storage.GetAvailableSpace(cargoType);
            int capacity = storage.GetResourceCapacity(cargoType);
            int current = storage.GetResourceAmount(cargoType);
            Debug.Log($"🏠 {building.name} space check: Current={current}, Capacity={capacity}, Available={space} (need > 0)");
            return space > 0;
        }
    }

    bool CanPrebuiltHandleCargo(PrebuiltBuilding prebuilt, ResourceType cargoType, bool isSource)
    {
        if (cargoType == ResourceType.Population)
        {
            if (isSource)
            {
                return prebuilt.GetCurrentPopulation() > 0;
            }
            else
            {
                return prebuilt.CanAcceptPopulation(1);
            }
        }
        
        // For other resource types, check storage
        BuildingResourceStorage storage = prebuilt.GetResourceStorage();
        if (storage == null) return false;
        
        if (isSource)
        {
            return storage.GetResourceAmount(cargoType) > 0;
        }
        else
        {
            return storage.GetAvailableSpace(cargoType) > 0;
        }
    }

    // Improved auto-find methods
    MonoBehaviour FindBestDeliverySource(ResourceType cargoType, Vector3? referencePosition)
    {
        switch (cargoType)
        {
            case ResourceType.FoodPacks:
                return FindNearestBuilding(BuildingType.Kitchen, referencePosition, cargoType, true);
                
            case ResourceType.Population:
                // Prefer communities, then shelters with people
                MonoBehaviour communitySource = FindNearestPrebuiltBuilding(PrebuiltBuildingType.Community, 
                                                                        referencePosition, cargoType, true);
                if (communitySource != null) return communitySource;
                
                return FindNearestBuilding(BuildingType.Shelter, referencePosition, cargoType, true);
                
            default:
                return null;
        }
    }

    MonoBehaviour FindBestDeliveryDestination(ResourceType cargoType, Vector3? referencePosition)
    {
        switch (cargoType)
        {
            case ResourceType.FoodPacks:
                return FindNearestBuilding(BuildingType.Shelter, referencePosition, cargoType, false);
                
            case ResourceType.Population:
                // Prefer shelters, then motels
                MonoBehaviour shelterDest = FindNearestBuilding(BuildingType.Shelter, referencePosition, cargoType, false);
                if (shelterDest != null) return shelterDest;
                
                return FindNearestPrebuiltBuilding(PrebuiltBuildingType.Motel, referencePosition, cargoType, false);
                
            default:
                return null;
        }
    }

    // Helper methods for auto delivery setup
    MonoBehaviour FindDeliverySource(ResourceType cargoType)
    {
        if (cargoType == ResourceType.FoodPacks)
        {
            // Find kitchen with food
            Building[] kitchens = FindObjectsOfType<Building>().Where(b => 
                b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational()).ToArray();
            
            foreach (Building kitchen in kitchens)
            {
                BuildingResourceStorage storage = kitchen.GetComponent<BuildingResourceStorage>();
                if (storage != null && storage.GetResourceAmount(ResourceType.FoodPacks) > 0)
                    return kitchen;
            }
        }
        else if (cargoType == ResourceType.Population)
        {
            // Find community with people
            PrebuiltBuilding[] communities = FindObjectsOfType<PrebuiltBuilding>().Where(pb =>
                pb.GetPrebuiltType() == PrebuiltBuildingType.Community).ToArray();
            
            foreach (PrebuiltBuilding community in communities)
            {
                if (community.GetCurrentPopulation() > 0)
                    return community;
            }
        }
        
        return null;
    }

    MonoBehaviour FindDeliveryDestination(ResourceType cargoType)
    {
        if (cargoType == ResourceType.FoodPacks)
        {
            // Find shelter with space
            Building[] shelters = FindObjectsOfType<Building>().Where(b =>
                b.GetBuildingType() == BuildingType.Shelter && b.IsOperational()).ToArray();
            
            foreach (Building shelter in shelters)
            {
                BuildingResourceStorage storage = shelter.GetComponent<BuildingResourceStorage>();
                if (storage != null && storage.GetAvailableSpace(ResourceType.FoodPacks) > 0)
                    return shelter;
            }
        }
        else if (cargoType == ResourceType.Population)
        {
            // Find shelter with space, then motel
            Building[] shelters = FindObjectsOfType<Building>().Where(b =>
                b.GetBuildingType() == BuildingType.Shelter && b.IsOperational()).ToArray();
            
            foreach (Building shelter in shelters)
            {
                BuildingResourceStorage storage = shelter.GetComponent<BuildingResourceStorage>();
                if (storage != null && storage.GetAvailableSpace(ResourceType.Population) > 0)
                    return shelter;
            }
            
            // Fallback to motel
            PrebuiltBuilding[] motels = FindObjectsOfType<PrebuiltBuilding>().Where(pb =>
                pb.GetPrebuiltType() == PrebuiltBuildingType.Motel).ToArray();
            
            foreach (PrebuiltBuilding motel in motels)
            {
                if (motel.CanAcceptPopulation(1))
                    return motel;
            }
        }
        
        return null;
    }

    public GameTask CreateTask(string title, TaskType type, string facility, string description)
    {
        GameTask newTask = new GameTask(nextTaskId++, title, type, facility);
        newTask.description = description;
        newTask.taskImage = defaultTaskImage;

        // Set default timing based on task type
        switch (type)
        {
            case TaskType.Emergency:
                newTask.roundsRemaining = 1;
                newTask.hasRealTimeLimit = false;
                break;
            case TaskType.Demand:
                newTask.roundsRemaining = 2;
                newTask.hasRealTimeLimit = false;
                break;
            case TaskType.Advisory:
                newTask.roundsRemaining = 3;
                newTask.hasRealTimeLimit = false;
                break;
            case TaskType.Alert:
                newTask.roundsRemaining = 2;
                newTask.hasRealTimeLimit = false;
                break;
        }

        activeTasks.Add(newTask);
        OnTaskCreated?.Invoke(newTask);

        if (showDebugInfo)
            Debug.Log($"Created task: {title} ({type}) for {facility}");

        return newTask;
    }

    public void CompleteTask(GameTask task)
    {
        if (activeTasks.Contains(task))
        {
            task.status = TaskStatus.Completed;
            activeTasks.Remove(task);
            completedTasks.Add(task);

            OnTaskCompleted?.Invoke(task);

            if (taskCenterUI != null && taskCenterUI.taskCenterPanel.activeInHierarchy)
            {
                taskCenterUI.RefreshTaskList();
            }

            if (showDebugInfo)
                Debug.Log($"Completed task: {task.taskTitle}");
        }
    }

    public void ExpireTask(GameTask task)
    {
        if (activeTasks.Contains(task))
        {
            task.status = task.taskType == TaskType.Emergency || task.taskType == TaskType.Demand
                ? TaskStatus.Incomplete : TaskStatus.Expired;

            activeTasks.Remove(task);
            completedTasks.Add(task);

            // Apply penalties for incomplete emergency/demand tasks
            if (task.status == TaskStatus.Incomplete)
            {
                ApplyTaskPenalties(task);
            }

            OnTaskExpired?.Invoke(task);

            if (showDebugInfo)
                Debug.Log($"Expired task: {task.taskTitle} (Status: {task.status})");
        }
    }

    void ApplyTaskPenalties(GameTask task)
    {
        // Apply penalties based on task impacts
        foreach (TaskImpact impact in task.impacts)
        {
            switch (impact.impactType)
            {
                case ImpactType.Satisfaction:
                    if (SatisfactionAndBudget.Instance != null)
                        SatisfactionAndBudget.Instance.RemoveSatisfaction(impact.value);
                    break;
                case ImpactType.Budget:
                    if (SatisfactionAndBudget.Instance != null)
                        SatisfactionAndBudget.Instance.RemoveBudget(impact.value);
                    break;
            }
        }

        if (showDebugInfo)
            Debug.Log($"Applied penalties for incomplete task: {task.taskTitle}");
    }

    public void IgnoreTask(GameTask task)
    {
        // For advisory tasks, this removes them without penalty
        if (task.taskType == TaskType.Advisory)
        {
            task.status = TaskStatus.Completed;
            activeTasks.Remove(task);
            completedTasks.Add(task);

            if (showDebugInfo)
                Debug.Log($"Ignored advisory task: {task.taskTitle}");
        }
    }
    
    public void SetTaskInProgress(GameTask task)
    {
        if (activeTasks.Contains(task))
        {
            task.status = TaskStatus.InProgress;

            if (showDebugInfo)
                Debug.Log($"Task set to in progress: {task.taskTitle}");
        }
    }

    public void SetTaskIncomplete(GameTask task)
    {
        if (activeTasks.Contains(task))
        {
            task.status = TaskStatus.Incomplete;
            activeTasks.Remove(task);
            completedTasks.Add(task);
            
            ApplyTaskPenalties(task);
            OnTaskCompleted?.Invoke(task);
            
            if (showDebugInfo)
                Debug.Log($"Task marked as incomplete: {task.taskTitle}");
        }
    }

    // Methods for getting filtered task lists
    public List<GameTask> GetTasksByType(TaskType type)
    {
        return activeTasks.Where(t => t.taskType == type).ToList();
    }

    public List<GameTask> GetTasksByStatus(TaskStatus status)
    {
        return completedTasks.Where(t => t.status == status).ToList();
    }

    public List<GameTask> GetAllActiveTasks()
    {
        return new List<GameTask>(activeTasks);
    }

    public GameTask GetTaskById(int taskId)
    {
        GameTask task = activeTasks.FirstOrDefault(t => t.taskId == taskId);
        if (task == null)
            task = completedTasks.FirstOrDefault(t => t.taskId == taskId);
        return task;
    }

    public int GetAvailableResourceAmount(MonoBehaviour source, ResourceType resourceType)
    {
        int amount = source.GetComponent<BuildingResourceStorage>()?.GetResourceAmount(resourceType) ?? 0;
        return amount;
    }

    public int CalculateDeliveryQuantity(AgentChoice choice, MonoBehaviour source)
    {
        switch (choice.quantityType)
        {
            case DeliveryQuantityType.Fixed:
                return choice.deliveryQuantity;

            case DeliveryQuantityType.All:
                return GetAvailableResourceAmount(source, choice.deliveryCargoType);

            case DeliveryQuantityType.Percentage:
                int available = GetAvailableResourceAmount(source, choice.deliveryCargoType);
                return Mathf.RoundToInt(available * (choice.deliveryPercentage / 100f));

            default:
                return choice.deliveryQuantity;
        }
    }

    public GameTask CreateTaskFromData(TaskData taskData)
    {
        GameTask newTask = new GameTask(nextTaskId++, taskData.taskTitle, taskData.taskType, taskData.targetFacilityType.ToString());

        // Copy basic info
        newTask.description = taskData.description;
        newTask.taskImage = taskData.taskImage;
        newTask.taskOfficer = taskData.taskOfficer;

        // Copy time settings
        newTask.roundsRemaining = taskData.roundsRemaining;
        newTask.realTimeRemaining = taskData.realTimeRemaining;
        newTask.hasRealTimeLimit = taskData.hasRealTimeLimit;

        // Copy impact list
        newTask.impacts = new List<TaskImpact>();
        foreach (TaskImpact impact in taskData.impacts)
        {
            newTask.impacts.Add(new TaskImpact(impact.impactType, impact.value, impact.isCountdown, impact.customLabel));
        }

        // Copy agent messages
        newTask.agentMessages = new List<AgentMessage>();
        foreach (AgentMessage message in taskData.agentMessages)
        {
            Sprite agentIcon = GetOfficerAvatar(taskData.taskOfficer);
            newTask.agentMessages.Add(new AgentMessage(message.messageText, agentIcon)
            {
                useTypingEffect = message.useTypingEffect,
                typingSpeed = message.typingSpeed
            });
        }

        // Copy choices with delivery configuration
        newTask.agentChoices = new List<AgentChoice>();
        foreach (AgentChoice choice in taskData.agentChoices)
        {
            AgentChoice newChoice = new AgentChoice(choice.choiceId, choice.choiceText);

            // Copy choice impacts
            newChoice.choiceImpacts = new List<TaskImpact>();
            foreach (TaskImpact impact in choice.choiceImpacts)
            {
                newChoice.choiceImpacts.Add(new TaskImpact(impact.impactType, impact.value, impact.isCountdown, impact.customLabel));
            }

            // NEW: Only validate delivery choices that actually trigger delivery
            if (choice.triggersDelivery || choice.immediateDelivery)
            {
                MonoBehaviour source = FindTriggeringFacility(newTask);
                if (source == null)
                {
                    Debug.LogWarning($"No triggering facility found for task: {newTask.taskTitle}");
                    return null;
                }

                // Calculate dynamic quantity according to type
                int actualQuantity = CalculateDeliveryQuantity(choice, source);

                if (actualQuantity <= 0)
                {
                    Debug.LogWarning($"No resources available for delivery from {source.name} for choice: {choice.choiceText}");
                    return null;
                }

                // Use calculated quantity for delivery choices
                newChoice.deliveryQuantity = actualQuantity;
            }

            // Copy delivery configuration
            newChoice.triggersDelivery = choice.triggersDelivery;
            newChoice.immediateDelivery = choice.immediateDelivery; // New
            newChoice.enableMultipleDeliveries = choice.enableMultipleDeliveries; // New
            newChoice.deliveryCargoType = choice.deliveryCargoType;
            newChoice.quantityType = choice.quantityType; // NEW: Copy quantity type
            newChoice.deliveryPercentage = choice.deliveryPercentage; // NEW: Copy percentage
            newChoice.sourceType = choice.sourceType;
            newChoice.sourceBuilding = choice.sourceBuilding;
            newChoice.sourcePrebuilt = choice.sourcePrebuilt;
            newChoice.specificSourceName = choice.specificSourceName;
            newChoice.destinationType = choice.destinationType;
            newChoice.destinationBuilding = choice.destinationBuilding;
            newChoice.destinationPrebuilt = choice.destinationPrebuilt;
            newChoice.specificDestinationName = choice.specificDestinationName;
            newChoice.prioritizeNearestSource = choice.prioritizeNearestSource;
            newChoice.prioritizeNearestDestination = choice.prioritizeNearestDestination;

            newTask.agentChoices.Add(newChoice);
        }

        // Copy numerical inputs
        newTask.numericalInputs = new List<AgentNumericalInput>();
        foreach (AgentNumericalInput input in taskData.numericalInputs)
        {
            newTask.numericalInputs.Add(new AgentNumericalInput(input.inputId, input.inputLabel, input.currentValue, input.minValue, input.maxValue)
            {
                stepSize = input.stepSize
            });
        }

        // Set delivery time limit from task data
        newTask.deliveryTimeLimit = taskData.deliveryTimeLimit;
        newTask.deliveryFailureSatisfactionPenalty = taskData.deliveryFailureSatisfactionPenalty;

        activeTasks.Add(newTask);
        OnTaskCreated?.Invoke(newTask);

        if (showDebugInfo)
            Debug.Log($"Created task from data: {taskData.taskTitle} ({taskData.taskType})");

        return newTask;
    }
    
    Sprite GetOfficerAvatar(TaskOfficer officer)
    {
        switch (officer)
        {
            case TaskOfficer.DisasterOfficer: return defaultAgentSprite;
            case TaskOfficer.WorkforceService: return workforceServiceSprite;
            case TaskOfficer.LodgingMassCare: return lodgingMassCareSprite;
            case TaskOfficer.ExternalRelationship: return externalRelationshipSprite;
            case TaskOfficer.FoodMassCare: return foodMassCareSprite;
            default: return defaultAgentSprite;
        }
    }

    public MonoBehaviour FindTriggeringFacility(GameTask task)
    {
        Debug.Log("=== FIND TRIGGERING FACILITY ===");
        Debug.Log($"Task affected facility: {task.affectedFacility}");

        if (string.IsNullOrEmpty(task.affectedFacility))
        {
            Debug.Log("Affected facility is null or empty");
            return null;
        }

        MonoBehaviour result = FindFacilityByName(task.affectedFacility);
        Debug.Log($"FindFacilityByName result: {(result != null ? result.name : "NULL")}");
        return result;
    }


    MonoBehaviour FindFacilityByName(string facilityName)
    {
        Debug.Log($"=== FIND FACILITY BY NAME ===");
        Debug.Log($"Looking for: '{facilityName}'");
        
        if (string.IsNullOrEmpty(facilityName)) return null;
        
        // Search in Buildings
        Building[] buildings = FindObjectsOfType<Building>();
        Debug.Log($"Found {buildings.Length} buildings:");
        foreach (Building building in buildings)
        {
            Debug.Log($"  Building: {building.name}");
            if (building.name.Contains(facilityName) || building.name == facilityName)
            {
                Debug.Log($"  ✅ MATCH: {building.name}");
                return building;
            }
        }
        
        // Search in PrebuiltBuildings
        PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>();
        Debug.Log($"Found {prebuilts.Length} prebuilt buildings:");
        foreach (PrebuiltBuilding prebuilt in prebuilts)
        {
            Debug.Log($"  Prebuilt: {prebuilt.name}");
            if (prebuilt.name.Contains(facilityName) || prebuilt.name == facilityName)
            {
                Debug.Log($"  ✅ MATCH: {prebuilt.name}");
                return prebuilt;
            }
        }
        
        Debug.Log("❌ No facility found");
        return null;
    }

    public MonoBehaviour DetermineChoiceDeliverySource(AgentChoice choice, MonoBehaviour triggeringFacility)
    {
        Debug.Log($"Determining source: Type={choice.sourceType}");
    
        switch (choice.sourceType)
        {
            case DeliverySourceType.RequestingFacility:
                Debug.Log($"Using requesting facility as source: {triggeringFacility?.name}");
                return triggeringFacility;
                
            case DeliverySourceType.ManualAssignment:
                Debug.Log($"Looking for facility by name: {choice.specificSourceName}");
                return FindFacilityByName(choice.specificSourceName);
                
            case DeliverySourceType.SpecificBuilding:
                /*Debug.Log($"Looking for specific building type: {choice.sourceBuilding}");
                MonoBehaviour foundBuilding = FindNearestBuilding(choice.sourceBuilding, triggeringFacility?.transform.position, 
                                        choice.deliveryCargoType, true);
                Debug.Log($"Found building: {foundBuilding?.name}");
                return foundBuilding;*/

                // NEW: Exclude the triggering facility if it's also the destination
                Building[] buildings = FindObjectsOfType<Building>()
                    .Where(b => b.GetBuildingType() == choice.sourceBuilding)
                    .Where(b => b != triggeringFacility || choice.destinationType != DeliveryDestinationType.RequestingFacility) // Conditional exclude
                    .ToArray();

                if (buildings.Length == 0) return null;

                if (choice.prioritizeNearestSource && triggeringFacility != null)
                {
                    return FindNearestBuilding(choice.sourceBuilding, triggeringFacility?.transform.position, 
                                        choice.deliveryCargoType, true);
                }
                return buildings[0];
                
                
            case DeliverySourceType.SpecificPrebuilt:
                /*Debug.Log($"Looking for specific prebuilt type: {choice.sourcePrebuilt}");
                return FindNearestPrebuiltBuilding(choice.sourcePrebuilt, triggeringFacility?.transform.position,
                                                choice.deliveryCargoType, true);*/
                    // NEW: Exclude the triggering facility from prebuilt search
                PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>()
                .Where(p => p.GetPrebuiltType() == choice.sourcePrebuilt)
                .Where(p => p != triggeringFacility || choice.destinationType != DeliveryDestinationType.RequestingFacility)
                .ToArray();

                if (prebuilts.Length == 0) return null;

                if (choice.prioritizeNearestSource && triggeringFacility != null)
                {
                    return FindNearestPrebuiltBuilding(choice.sourcePrebuilt, triggeringFacility?.transform.position,
                                                choice.deliveryCargoType, true);
                }
                return prebuilts[0];

                
            case DeliverySourceType.AutoFind:
            default:
                Debug.Log($"Auto-finding source for cargo type: {choice.deliveryCargoType}");
                return FindBestDeliverySource(choice.deliveryCargoType, triggeringFacility?.transform.position);
        }
    }

    public MonoBehaviour DetermineChoiceDeliveryDestination(AgentChoice choice, MonoBehaviour triggeringFacility)
    {
        Debug.Log($"=== DESTINATION DEBUG ===");
        Debug.Log($"Destination Type: {choice.destinationType}");
        Debug.Log($"Destination Building: {choice.destinationBuilding}");
        Debug.Log($"Cargo Type: {choice.deliveryCargoType}");

        switch (choice.destinationType)
        {
            case DeliveryDestinationType.RequestingFacility:
                Debug.Log($"Using requesting facility as destination: {triggeringFacility?.name}");
                return triggeringFacility;

            case DeliveryDestinationType.ManualAssignment:
                Debug.Log($"Looking for facility by name: {choice.specificDestinationName}");
                return FindFacilityByName(choice.specificDestinationName);

            case DeliveryDestinationType.SpecificBuilding:
                /*Debug.Log($"Looking for specific building type: {choice.destinationBuilding}");

                // Debug: List all buildings
                Building[] allBuildings = FindObjectsOfType<Building>();
                Debug.Log($"Found {allBuildings.Length} total buildings:");
                foreach (Building b in allBuildings)
                {
                    Debug.Log($"  - {b.name}: Type={b.GetBuildingType()}, Status={b.GetCurrentStatus()}, Operational={b.IsOperational()}");
                }

                MonoBehaviour foundBuilding = FindNearestBuilding(choice.destinationBuilding, triggeringFacility?.transform.position,
                                        choice.deliveryCargoType, false);
                Debug.Log($"Found building result: {foundBuilding?.name}");
                return foundBuilding;*/

                // NEW: Exclude the triggering facility from destination search
                Building[] buildings = FindObjectsOfType<Building>()
                .Where(b => b.GetBuildingType() == choice.destinationBuilding)
                .Where(b => b != triggeringFacility) // Exclude source facility
                .ToArray();

                if (buildings.Length == 0)
                {
                    if (showDebugInfo)
                        Debug.LogWarning($"No available {choice.destinationBuilding} buildings found (excluding source)");
                    return null;
                }

                if (choice.prioritizeNearestDestination && triggeringFacility != null)
                {
                    return FindNearestBuilding(choice.destinationBuilding, triggeringFacility?.transform.position,
                                        choice.deliveryCargoType, false);
                }
                return buildings[0];
                


            case DeliveryDestinationType.SpecificPrebuilt:
                /*Debug.Log($"Looking for specific prebuilt type: {choice.destinationPrebuilt}");
                return FindNearestPrebuiltBuilding(choice.destinationPrebuilt, triggeringFacility?.transform.position,
                                            choice.deliveryCargoType, false);*/
                                                
                // NEW: Exclude the triggering facility from prebuilt search
                PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>()
                    .Where(p => p.GetPrebuiltType() == choice.destinationPrebuilt)
                    .Where(p => p != triggeringFacility) // Exclude source facility
                    .ToArray();

                if (prebuilts.Length == 0)
                {
                    if (showDebugInfo)
                        Debug.LogWarning($"No available {choice.destinationPrebuilt} prebuilts found (excluding source)");
                    return null;
                }

                if (choice.prioritizeNearestDestination && triggeringFacility != null)
                {
                    return FindNearestPrebuiltBuilding(choice.destinationPrebuilt, triggeringFacility?.transform.position,
                                            choice.deliveryCargoType, false);
                }
                return prebuilts[0];


            case DeliveryDestinationType.AutoFind:
            default:
                Debug.Log($"Auto-finding destination for cargo type: {choice.deliveryCargoType}");
                return FindBestDeliveryDestination(choice.deliveryCargoType, triggeringFacility?.transform.position);
        }
    }

    public void CompleteAlertTask(GameTask alertTask)
    {
        if (alertTask == null) return;
        
        // Mark as completed
        alertTask.status = TaskStatus.Completed;
        
        // Remove from active tasks
        if (activeTasks.Contains(alertTask))
        {
            activeTasks.Remove(alertTask);
            completedTasks.Add(alertTask);
            
            OnTaskCompleted?.Invoke(alertTask);
            
            if (showDebugInfo)
                Debug.Log($"Alert task completed: {alertTask.taskTitle}");
        }
    }

    // Utility methods for impact display
    public static string GetImpactIcon(ImpactType type)
    {
        switch (type)
        {
            case ImpactType.Satisfaction: return "😊";
            case ImpactType.Budget: return "💰";
            case ImpactType.FoodPacks: return "🍞";
            case ImpactType.Clients: return "👥";
            case ImpactType.Workforce: return "👷";
            case ImpactType.TotalTime: return "⏰";
            case ImpactType.TrainingTime: return "📚";
            case ImpactType.TotalCosts: return "💸";
            case ImpactType.TotalLodging: return "🏠";
            default: return "❓";
        }
    }

    public static string GetImpactLabel(ImpactType type)
    {
        switch (type)
        {
            case ImpactType.Satisfaction: return "Satisfaction";
            case ImpactType.Budget: return "Budget";
            case ImpactType.FoodPacks: return "Food Packs";
            case ImpactType.Clients: return "Clients";
            case ImpactType.Workforce: return "Workforce";
            case ImpactType.TotalTime: return "Total Time";
            case ImpactType.TrainingTime: return "Training Time";
            case ImpactType.TotalCosts: return "Total Costs";
            case ImpactType.TotalLodging: return "Total Lodging";
            default: return "Unknown";
        }
    }

    

    [ContextMenu("Create Test Food Demand Task")]
    public void CreateTestFoodDemandTask()
    {
        GameTask foodTask = CreateTask("Food Shortage", TaskType.Demand, "Shelter Complex",
            "Multiple families have reported running out of food supplies. Immediate food distribution is required.");

        // Add impacts
        foodTask.impacts.Add(new TaskImpact(ImpactType.FoodPacks, 50));
        foodTask.impacts.Add(new TaskImpact(ImpactType.Budget, 2000));
        foodTask.impacts.Add(new TaskImpact(ImpactType.Satisfaction, -10));

        // Add agent messages
        foodTask.agentMessages.Add(new AgentMessage("Hello! We have an urgent food shortage situation.", defaultAgentSprite));
        foodTask.agentMessages.Add(new AgentMessage("Several families in our shelter have completely run out of food supplies."));
        foodTask.agentMessages.Add(new AgentMessage("We need to decide how to respond quickly. What would you like to do?"));

        // Add agent choices with delivery options
        AgentChoice choice1 = new AgentChoice(1, "Emergency food distribution from nearby kitchen (50 food packs, $2000)");
        choice1.triggersDelivery = true;
        choice1.deliveryCargoType = ResourceType.FoodPacks;
        choice1.deliveryQuantity = 50;
        choice1.sourceType = DeliverySourceType.SpecificBuilding;
        choice1.sourceBuilding = BuildingType.Kitchen;
        choice1.destinationType = DeliveryDestinationType.AutoFind;
        choice1.choiceImpacts.Add(new TaskImpact(ImpactType.FoodPacks, -50));
        choice1.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -2000));
        choice1.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 15));
        foodTask.agentChoices.Add(choice1);

        AgentChoice choice2 = new AgentChoice(2, "Limited food distribution (10 food packs, $1000)");
        choice2.triggersDelivery = true;
        choice2.deliveryCargoType = ResourceType.FoodPacks;
        choice2.deliveryQuantity = 10;
        choice2.sourceType = DeliverySourceType.SpecificBuilding;
        choice2.sourceBuilding = BuildingType.Kitchen;
        choice2.destinationType = DeliveryDestinationType.RequestingFacility;
        choice2.choiceImpacts.Add(new TaskImpact(ImpactType.FoodPacks, -10));
        choice2.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -1000));
        choice2.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 5));
        foodTask.agentChoices.Add(choice2);

        AgentChoice choice3 = new AgentChoice(3, "Delay until next shipment arrives (no delivery)");
        choice3.triggersDelivery = false;
        choice3.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -20));
        foodTask.agentChoices.Add(choice3);
    }

    [ContextMenu("Create Test Advisory Task")]
    public void CreateTestAdvisoryTask()
    {
        GameTask advisoryTask = CreateTask("Equipment Upgrade", TaskType.Advisory, "Kitchen Operations",
            "Kitchen equipment could be upgraded to improve efficiency. This is not urgent but would provide long-term benefits.");

        advisoryTask.impacts.Add(new TaskImpact(ImpactType.Budget, 5000));
        advisoryTask.impacts.Add(new TaskImpact(ImpactType.Satisfaction, 25));

        advisoryTask.agentMessages.Add(new AgentMessage("I've been reviewing our kitchen operations.", defaultAgentSprite));
        advisoryTask.agentMessages.Add(new AgentMessage("We could upgrade our equipment to serve more people efficiently."));
        advisoryTask.agentMessages.Add(new AgentMessage("This would cost $5000 but improve long-term satisfaction."));

        // Add choices without delivery
        AgentChoice upgradeChoice = new AgentChoice(1, "Approve equipment upgrade ($5000)");
        upgradeChoice.triggersDelivery = false;
        upgradeChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -5000));
        upgradeChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 25));
        advisoryTask.agentChoices.Add(upgradeChoice);

        AgentChoice delayChoice = new AgentChoice(2, "Delay upgrade for now");
        delayChoice.triggersDelivery = false;
        delayChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -5));
        advisoryTask.agentChoices.Add(delayChoice);
    }

    [ContextMenu("Create Test Numerical Task")]
    public void CreateTestNumericalTask()
    {
        GameTask numericalTask = CreateTask("Worker Assignment", TaskType.Advisory, "Kitchen Operations",
            "We need to assign workers to this facility. Please specify how many workers to assign.");
        
        // Add numerical inputs
        AgentNumericalInput workerInput = new AgentNumericalInput(1, "Workers to Assign", 2, 0, 8);
        numericalTask.numericalInputs.Add(workerInput);
        
        AgentNumericalInput budgetInput = new AgentNumericalInput(2, "Budget Allocation", 1000, 500, 5000);
        budgetInput.stepSize = 500;
        numericalTask.numericalInputs.Add(budgetInput);
        
        // Add agent messages
        numericalTask.agentMessages.Add(new AgentMessage("We need to configure this facility.", defaultAgentSprite));
        numericalTask.agentMessages.Add(new AgentMessage("Please use the controls below to set the parameters."));
        numericalTask.agentMessages.Add(new AgentMessage("Confirm your settings when ready."));
        
        // Add impacts
        numericalTask.impacts.Add(new TaskImpact(ImpactType.Workforce, 0, false, "Workers to Assign"));
        numericalTask.impacts.Add(new TaskImpact(ImpactType.Budget, 0, false, "Budget Allocation"));

        // Add simple confirmation choice
        AgentChoice confirmChoice = new AgentChoice(1, "Confirm worker assignment");
        confirmChoice.triggersDelivery = false;
        confirmChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 5));
        numericalTask.agentChoices.Add(confirmChoice);
    }

    [ContextMenu("Create Test Population Transport Task")]
    public void CreateTestPopulationTransportTask()
    {
        PrebuiltBuilding community = null;
        PrebuiltBuilding motel = null;
        
        PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>();
        foreach (var pb in prebuilts)
        {
            if (pb.GetPrebuiltType() == PrebuiltBuildingType.Community && pb.GetCurrentPopulation() > 0)
                community = pb;
            else if (pb.GetPrebuiltType() == PrebuiltBuildingType.Motel)
                motel = pb;
        }
        
        if (community == null || motel == null)
        {
            Debug.LogError("Cannot create transport task - missing Community with population or Motel");
            return;
        }
        
        // Check vehicle capacity and available resources
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
        int maxVehicleCapacity = vehicles.Length > 0 ? vehicles.Max(v => v.GetMaxCapacity()) : 10;
        
        int availablePopulation = community.GetCurrentPopulation();
        int motelSpace = motel.GetPopulationCapacity() - motel.GetCurrentPopulation();
        int transportAmount = Mathf.Min(availablePopulation, motelSpace, maxVehicleCapacity, 3);
        
        if (transportAmount <= 0)
        {
            Debug.LogError("Cannot create transport task - no available population or space");
            return;
        }

        GameTask transportTask = CreateTask("Emergency Population Transport", TaskType.Emergency, community.name,
            $"We have {transportAmount} displaced families that need immediate transport to temporary housing.");

        // Set timing
        transportTask.roundsRemaining = 2;
        transportTask.hasRealTimeLimit = false;
        transportTask.deliveryTimeLimit = 300f;
        transportTask.deliveryFailureSatisfactionPenalty = 15f;
        
        // Add impacts
        transportTask.impacts.Add(new TaskImpact(ImpactType.Clients, transportAmount, false, "People to Transport"));
        transportTask.impacts.Add(new TaskImpact(ImpactType.TotalTime, 2, false, "Rounds Remaining"));
        transportTask.impacts.Add(new TaskImpact(ImpactType.Satisfaction, -15, false, "Failure Penalty"));

        // Add agent messages
        transportTask.agentMessages.Add(new AgentMessage("We have an urgent situation!", defaultAgentSprite));
        transportTask.agentMessages.Add(new AgentMessage($"{transportAmount} families at {community.name} need immediate transport."));
        transportTask.agentMessages.Add(new AgentMessage("We must get them relocated within 2 rounds or they'll lose faith in our response."));
        transportTask.agentMessages.Add(new AgentMessage("Where should we send them?"));
        
        // Add transport choices
        AgentChoice shelterChoice = new AgentChoice(1, "Send to Shelter (Free, but limited space)");
        shelterChoice.triggersDelivery = true;
        shelterChoice.deliveryCargoType = ResourceType.Population;
        shelterChoice.deliveryQuantity = transportAmount;
        shelterChoice.sourceType = DeliverySourceType.RequestingFacility;
        shelterChoice.destinationType = DeliveryDestinationType.SpecificBuilding;
        shelterChoice.destinationBuilding = BuildingType.Shelter;
        shelterChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 5, false, "Quick Response"));
        transportTask.agentChoices.Add(shelterChoice);

        AgentChoice motelChoice = new AgentChoice(2, "Send to Motel ($200, always available)");
        motelChoice.triggersDelivery = true;
        motelChoice.deliveryCargoType = ResourceType.Population;
        motelChoice.deliveryQuantity = transportAmount;
        motelChoice.sourceType = DeliverySourceType.RequestingFacility;
        motelChoice.destinationType = DeliveryDestinationType.SpecificPrebuilt;
        motelChoice.destinationPrebuilt = PrebuiltBuildingType.Motel;
        motelChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -200));
        motelChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 10, false, "Premium Housing"));
        transportTask.agentChoices.Add(motelChoice);

        AgentChoice airDrop = new AgentChoice(2, "Emergency airdrop to Motel(instant)");
        airDrop.immediateDelivery = true;
        airDrop.deliveryCargoType = ResourceType.Population;
        airDrop.deliveryQuantity = transportAmount;
        airDrop.sourceType = DeliverySourceType.RequestingFacility;
        airDrop.destinationType = DeliveryDestinationType.SpecificPrebuilt;
        airDrop.destinationPrebuilt = PrebuiltBuildingType.Motel;
        airDrop.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -1000, false, "Airdrop Cost"));
        airDrop.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 20, false, "Premium Housing"));
        transportTask.agentChoices.Add(airDrop);

        AgentChoice delayChoice = new AgentChoice(3, "Wait for better options (Risk satisfaction loss)");
        delayChoice.triggersDelivery = false;
        delayChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -8, false, "Delayed Response"));
        transportTask.agentChoices.Add(delayChoice);
        
        if (showDebugInfo)
            Debug.Log($"Created test population transport task: {transportAmount} people from {community.name}. " +
                    $"Max vehicle capacity: {maxVehicleCapacity}");
    }

    [ContextMenu("Create Test Delivery Choice Task")]
    public void CreateTestDeliveryChoiceTask()
    {
        GameTask choiceTask = CreateTask("Population Relocation Decision", TaskType.Demand, "Community Center",
            "A family needs relocation. Choose where to send them based on available options and budget.");

        // Add impacts to show the stakes
        choiceTask.impacts.Add(new TaskImpact(ImpactType.Clients, 2, false, "Family Members"));
        choiceTask.impacts.Add(new TaskImpact(ImpactType.TotalTime, 2, false, "Rounds Remaining"));

        // Add agent messages
        choiceTask.agentMessages.Add(new AgentMessage("We have a family that needs immediate relocation.", defaultAgentSprite));
        choiceTask.agentMessages.Add(new AgentMessage("They've been displaced and need somewhere to stay tonight."));
        choiceTask.agentMessages.Add(new AgentMessage("We have several options available. Where would you like to send them?"));

        // Choice 1: Send to Shelter
        AgentChoice shelterChoice = new AgentChoice(1, "Send to Shelter (Free, limited space)");
        shelterChoice.triggersDelivery = true;
        shelterChoice.deliveryCargoType = ResourceType.Population;
        shelterChoice.deliveryQuantity = 2;
        shelterChoice.sourceType = DeliverySourceType.SpecificPrebuilt;
        shelterChoice.sourcePrebuilt = PrebuiltBuildingType.Community;
        shelterChoice.destinationType = DeliveryDestinationType.SpecificBuilding;
        shelterChoice.destinationBuilding = BuildingType.Shelter;
        shelterChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 5));
        choiceTask.agentChoices.Add(shelterChoice);

        // Choice 2: Send to Motel
        AgentChoice motelChoice = new AgentChoice(2, "Send to Motel ($500, always available)");
        motelChoice.triggersDelivery = true;
        motelChoice.deliveryCargoType = ResourceType.Population;
        motelChoice.deliveryQuantity = 2;
        motelChoice.sourceType = DeliverySourceType.SpecificPrebuilt;
        motelChoice.sourcePrebuilt = PrebuiltBuildingType.Community;
        motelChoice.destinationType = DeliveryDestinationType.SpecificPrebuilt;
        motelChoice.destinationPrebuilt = PrebuiltBuildingType.Motel;
        motelChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -500));
        motelChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 10));
        choiceTask.agentChoices.Add(motelChoice);

        // Choice 3: Keep them (no delivery)
        AgentChoice keepChoice = new AgentChoice(3, "Keep them here for now (overcrowding risk)");
        keepChoice.triggersDelivery = false;
        keepChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -5));
        choiceTask.agentChoices.Add(keepChoice);

        if (showDebugInfo)
            Debug.Log("Created test delivery choice task with multiple transport options");
    }

    [ContextMenu("Test: Simple Road Blockage (No GameTask)")]
    public void TestSimpleRoadBlockage()
    {
        Vehicle testVehicle = FindObjectOfType<Vehicle>();
        if (testVehicle == null)
        {
            Debug.LogWarning("No vehicle found");
            return;
        }
        
        // Create simple fake delivery without GameTask
        Building[] buildings = FindObjectsOfType<Building>();
        if (buildings.Length < 2)
        {
            Debug.LogWarning("Need at least 2 buildings");
            return;
        }
        
        DeliveryTask fakeDelivery = new DeliveryTask(
            buildings[0], buildings[1], 
            ResourceType.FoodPacks, 5, 999);
        
        // Assign to vehicle for testing
        testVehicle.currentTask = fakeDelivery;
        testVehicle.SetStatus(VehicleStatus.InTransit);
        
        // Force flood stop - this should create road blockage task only
        testVehicle.StopVehicleDueToFlood();
        
        Debug.Log("Created simple road blockage test (no GameTask failure)");
    }

    [ContextMenu("Test Multi-Shelter Delivery")]
    public void TestMultiShelterDelivery()
    {
        GameTask evacTask = TaskSystem.Instance.CreateTask("Multi-Shelter Test", TaskType.Emergency, "Community", "Test multiple shelter delivery");
        evacTask.roundsRemaining = 1;
        AgentChoice multiChoice = new AgentChoice(1, "Evacuate to all available shelters");
        multiChoice.enableMultipleDeliveries = true;
        multiChoice.multiDeliveryType = AgentChoice.MultiDeliveryType.SingleSourceMultiDest;
        multiChoice.triggersDelivery = true;
        multiChoice.deliveryCargoType = ResourceType.Population;
        multiChoice.quantityType = DeliveryQuantityType.Fixed;
        multiChoice.deliveryQuantity = 30;
        multiChoice.sourceType = DeliverySourceType.SpecificPrebuilt;
        multiChoice.sourcePrebuilt = PrebuiltBuildingType.Community;
        multiChoice.destinationType = DeliveryDestinationType.SpecificBuilding;
        multiChoice.destinationBuilding = BuildingType.Shelter;

        evacTask.agentChoices.Add(multiChoice);

        Debug.Log("Created test multi-shelter delivery task");
    }

    
    [ContextMenu("Print Task Statistics")]
    public void PrintTaskStatistics()
    {
        Debug.Log("=== TASK STATISTICS ===");
        Debug.Log($"Active Tasks: {activeTasks.Count}");
        Debug.Log($"Completed Tasks: {completedTasks.Count}");

        foreach (TaskType type in Enum.GetValues(typeof(TaskType)))
        {
            int count = GetTasksByType(type).Count;
            Debug.Log($"{type} Tasks: {count}");
        }
    }
}