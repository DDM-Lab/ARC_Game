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

public enum DeliveryDestinationType
{
    AutoFind,
    SpecificBuilding,
    SpecificPrebuilt,
    RequestingFacility,
    ManualAssignment
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
    public ResourceType deliveryCargoType = ResourceType.Population;
    public int deliveryQuantity = 0;
    
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
    public Sprite defaultAgentAvatar;

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
        if (task.status == TaskStatus.InProgress)
        {
            task.status = TaskStatus.Incomplete;
            activeTasks.Remove(task);
            completedTasks.Add(task);
            
            // apply penalties for delivery failure
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
        // Check for new tasks at the start of each round (segment 0)
        if (newSegment == 0 && enableAutoTaskGeneration)
        {
            GenerateTasksFromDatabase();
        }
    }

    void GenerateTasksFromDatabase()
    {
        if (taskDatabase == null) return;
        
        List<TaskData> triggeredTasks = taskDatabase.CheckTriggeredTasks();
        
        foreach (TaskData taskData in triggeredTasks)
        {
            // Check if task already exists to avoid duplicates
            if (activeTasks.Any(t => t.taskTitle == taskData.taskTitle)) continue;
            
            CreateTaskFromDatabase(taskData);
        }
    }

    public GameTask CreateTaskFromDatabase(TaskData taskData)
    {
        // Find suitable facility that triggered the task
        MonoBehaviour triggeringFacility = taskDatabase.FindSuitableFacility(taskData);
        string facilityName = triggeringFacility?.name ?? taskData.targetFacilityType.ToString();
        
        GameTask newTask = CreateTaskFromData(taskData);
        newTask.affectedFacility = facilityName;
        
        // No need to set up delivery here - it's handled by choices now
        
        if (showDebugInfo)
            Debug.Log($"Generated task from database: {taskData.taskId} for facility {facilityName}");
        
        return newTask;
    }

    // Enhanced facility finding methods
    MonoBehaviour FindNearestBuilding(BuildingType buildingType, Vector3? referencePosition, 
                                    ResourceType cargoType, bool isSource)
    {
        Building[] buildings = FindObjectsOfType<Building>().Where(b => 
            b.GetBuildingType() == buildingType && b.IsOperational()).ToArray();
        
        Building bestBuilding = null;
        float closestDistance = float.MaxValue;
        
        foreach (Building building in buildings)
        {
            // Check if building can provide/accept the cargo
            if (!CanBuildingHandleCargo(building, cargoType, isSource))
                continue;
            
            if (referencePosition.HasValue)
            {
                float distance = Vector3.Distance(building.transform.position, referencePosition.Value);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    bestBuilding = building;
                }
            }
            else
            {
                return building; // Return first suitable if no reference position
            }
        }
        
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
        BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();
        if (storage == null) return false;
        
        if (isSource)
        {
            // Check if building has resources to give
            return storage.GetResourceAmount(cargoType) > 0;
        }
        else
        {
            // Check if building has space to receive
            return storage.GetAvailableSpace(cargoType) > 0;
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

    public GameTask CreateTaskFromData(TaskData taskData)
    {
        GameTask newTask = new GameTask(nextTaskId++, taskData.taskTitle, taskData.taskType, taskData.targetFacilityType.ToString());
        
        // Copy basic info
        newTask.description = taskData.description;
        newTask.taskImage = taskData.taskImage;

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
            newTask.agentMessages.Add(new AgentMessage(message.messageText, message.agentAvatar)
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
            
            // Copy delivery configuration
            newChoice.triggersDelivery = choice.triggersDelivery;
            newChoice.deliveryCargoType = choice.deliveryCargoType;
            newChoice.deliveryQuantity = choice.deliveryQuantity;
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

    public MonoBehaviour FindTriggeringFacility(GameTask task)
    {
        // This should return the facility that originally triggered this task
        // For now, we can find by name or implement a better tracking system
        
        if (string.IsNullOrEmpty(task.affectedFacility)) return null;
        
        return FindFacilityByName(task.affectedFacility);
    }


    MonoBehaviour FindFacilityByName(string facilityName)
    {
        if (string.IsNullOrEmpty(facilityName)) return null;
        
        // Search in Buildings
        Building[] buildings = FindObjectsOfType<Building>();
        foreach (Building building in buildings)
        {
            if (building.name.Contains(facilityName) || building.name == facilityName)
                return building;
        }
        
        // Search in PrebuiltBuildings
        PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>();
        foreach (PrebuiltBuilding prebuilt in prebuilts)
        {
            if (prebuilt.name.Contains(facilityName) || prebuilt.name == facilityName)
                return prebuilt;
        }
        
        return null;
    }

    public MonoBehaviour DetermineChoiceDeliverySource(AgentChoice choice, MonoBehaviour triggeringFacility)
    {
        switch (choice.sourceType)
        {
            case DeliverySourceType.RequestingFacility:
                return triggeringFacility;
                
            case DeliverySourceType.ManualAssignment:
                return FindFacilityByName(choice.specificSourceName);
                
            case DeliverySourceType.SpecificBuilding:
                return FindNearestBuilding(choice.sourceBuilding, triggeringFacility?.transform.position, 
                                        choice.deliveryCargoType, true);
                
            case DeliverySourceType.SpecificPrebuilt:
                return FindNearestPrebuiltBuilding(choice.sourcePrebuilt, triggeringFacility?.transform.position,
                                                choice.deliveryCargoType, true);
                
            case DeliverySourceType.AutoFind:
            default:
                return FindBestDeliverySource(choice.deliveryCargoType, triggeringFacility?.transform.position);
        }
    }

    public MonoBehaviour DetermineChoiceDeliveryDestination(AgentChoice choice, MonoBehaviour triggeringFacility)
    {
        switch (choice.destinationType)
        {
            case DeliveryDestinationType.RequestingFacility:
                return triggeringFacility;
                
            case DeliveryDestinationType.ManualAssignment:
                return FindFacilityByName(choice.specificDestinationName);
                
            case DeliveryDestinationType.SpecificBuilding:
                return FindNearestBuilding(choice.destinationBuilding, triggeringFacility?.transform.position,
                                        choice.deliveryCargoType, false);
                
            case DeliveryDestinationType.SpecificPrebuilt:
                return FindNearestPrebuiltBuilding(choice.destinationPrebuilt, triggeringFacility?.transform.position,
                                                choice.deliveryCargoType, false);
                
            case DeliveryDestinationType.AutoFind:
            default:
                return FindBestDeliveryDestination(choice.deliveryCargoType, triggeringFacility?.transform.position);
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

    // Debug methods for testing
    [ContextMenu("Create Test Food Demand Task")]
    public void CreateTestFoodDemandTask()
    {
        GameTask foodTask = CreateTask("Food Shortage", TaskType.Demand, "Kitchen 1",
            "Multiple families have reported running out of food supplies. Immediate food distribution is required.");

        // Add impacts
        foodTask.impacts.Add(new TaskImpact(ImpactType.FoodPacks, 50));
        foodTask.impacts.Add(new TaskImpact(ImpactType.Budget, 2000));
        foodTask.impacts.Add(new TaskImpact(ImpactType.Satisfaction, -10));

        // Add agent messages
        foodTask.agentMessages.Add(new AgentMessage("Hello! We have an urgent food shortage situation.", defaultAgentAvatar));
        foodTask.agentMessages.Add(new AgentMessage("Several families in Shelter 1 have completely run out of food supplies."));
        foodTask.agentMessages.Add(new AgentMessage("We need to decide how to respond quickly. What would you like to do?"));

        // Add agent choices
        AgentChoice choice1 = new AgentChoice(1, "Emergency food distribution (50 food packs, $2000)");
        choice1.choiceImpacts.Add(new TaskImpact(ImpactType.FoodPacks, -50));
        choice1.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -2000));
        choice1.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 15));
        foodTask.agentChoices.Add(choice1);

        AgentChoice choice2 = new AgentChoice(2, "Limited food distribution (25 food packs, $1000)");
        choice2.choiceImpacts.Add(new TaskImpact(ImpactType.FoodPacks, -25));
        choice2.choiceImpacts.Add(new TaskImpact(ImpactType.Budget, -1000));
        choice2.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 5));
        foodTask.agentChoices.Add(choice2);

        AgentChoice choice3 = new AgentChoice(3, "Delay until next shipment arrives");
        choice3.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -20));
        foodTask.agentChoices.Add(choice3);
    }

    [ContextMenu("Create Test Advisory Task")]
    public void CreateTestAdvisoryTask()
    {
        GameTask advisoryTask = CreateTask("Equipment Upgrade", TaskType.Advisory, "Kitchen 2",
            "Kitchen equipment could be upgraded to improve efficiency. This is not urgent but would provide long-term benefits.");

        advisoryTask.impacts.Add(new TaskImpact(ImpactType.Budget, 5000));
        advisoryTask.impacts.Add(new TaskImpact(ImpactType.Satisfaction, 25));

        advisoryTask.agentMessages.Add(new AgentMessage("I've been reviewing our kitchen operations.", defaultAgentAvatar));
        advisoryTask.agentMessages.Add(new AgentMessage("We could upgrade our equipment to serve more people efficiently."));
    }

    [ContextMenu("Create Test Numerical Task")]
    public void CreateTestNumericalTask()
    {
        GameTask numericalTask = CreateTask("Worker Assignment", TaskType.Advisory, "Kitchen 1",
            "We need to assign workers to this facility. Please specify how many workers to assign.");
        
        // add numerical inputs
        AgentNumericalInput workerInput = new AgentNumericalInput(1, "Workers to Assign", 2, 0, 8);
        numericalTask.numericalInputs.Add(workerInput);
        
        AgentNumericalInput budgetInput = new AgentNumericalInput(2, "Budget Allocation", 1000, 500, 5000);
        budgetInput.stepSize = 500;
        numericalTask.numericalInputs.Add(budgetInput);
        
        // add agent messages
        numericalTask.agentMessages.Add(new AgentMessage("We need to configure this facility.", defaultAgentAvatar));
        numericalTask.agentMessages.Add(new AgentMessage("Please use the controls below to set the parameters."));
        
        // add impacts
        numericalTask.impacts.Add(new TaskImpact(ImpactType.Workforce, 0));
        numericalTask.impacts.Add(new TaskImpact(ImpactType.Budget, 0));
    }

    [ContextMenu("Create Test Delivery Choice Task")]
    public void CreateTestDeliveryChoiceTask()
    {
        GameTask choiceTask = CreateTask("Population Relocation Decision", TaskType.Demand, "Community Center",
            "A family needs relocation. Choose where to send them.");

        // Add agent messages
        choiceTask.agentMessages.Add(new AgentMessage("We have a family that needs immediate relocation.", defaultAgentAvatar));
        choiceTask.agentMessages.Add(new AgentMessage("We have two options available. Where would you like to send them?"));

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
        AgentChoice keepChoice = new AgentChoice(3, "Keep them here for now");
        keepChoice.triggersDelivery = false;
        keepChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -5));
        choiceTask.agentChoices.Add(keepChoice);

        if (showDebugInfo)
            Debug.Log("Created test delivery choice task");
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