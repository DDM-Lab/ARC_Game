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
    public bool requiresDelivery = false;
    public ResourceType deliveryCargoType = ResourceType.FoodPacks;
    public int deliveryQuantity = 0;
    public MonoBehaviour deliverySource;
    public MonoBehaviour deliveryDestination;
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
                if (GlobalClock.Instance == null || GlobalClock.Instance.CanPlayerInteract())
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
            if (task.requiresDelivery && task.status == TaskStatus.InProgress)
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
        GameTask newTask = new GameTask(nextTaskId++, taskData.taskTitle, taskData.taskType, taskData.affectedFacility);
        
        // copy basic info
        newTask.description = taskData.description;
        newTask.taskImage = taskData.taskImage;

        // copy time settings
        newTask.roundsRemaining = taskData.roundsRemaining;
        newTask.realTimeRemaining = taskData.realTimeRemaining;
        newTask.hasRealTimeLimit = taskData.hasRealTimeLimit;

        // copy impact list
        newTask.impacts = new List<TaskImpact>();
        foreach (TaskImpact impact in taskData.impacts)
        {
            newTask.impacts.Add(new TaskImpact(impact.impactType, impact.value, impact.isCountdown, impact.customLabel));
        }

        // copy agent messages
        newTask.agentMessages = new List<AgentMessage>();
        foreach (AgentMessage message in taskData.agentMessages)
        {
            newTask.agentMessages.Add(new AgentMessage(message.messageText, message.agentAvatar)
            {
                useTypingEffect = message.useTypingEffect,
                typingSpeed = message.typingSpeed
            });
        }

        // copy choices
        newTask.agentChoices = new List<AgentChoice>();
        foreach (AgentChoice choice in taskData.agentChoices)
        {
            AgentChoice newChoice = new AgentChoice(choice.choiceId, choice.choiceText);
            newChoice.choiceImpacts = new List<TaskImpact>();
            foreach (TaskImpact impact in choice.choiceImpacts)
            {
                newChoice.choiceImpacts.Add(new TaskImpact(impact.impactType, impact.value, impact.isCountdown, impact.customLabel));
            }
            newTask.agentChoices.Add(newChoice);
        }

        // copy numerical inputs
        newTask.numericalInputs = new List<AgentNumericalInput>();
        foreach (AgentNumericalInput input in taskData.numericalInputs)
        {
            newTask.numericalInputs.Add(new AgentNumericalInput(input.inputId, input.inputLabel, input.currentValue, input.minValue, input.maxValue)
            {
                stepSize = input.stepSize
            });
        }
        
        activeTasks.Add(newTask);
        OnTaskCreated?.Invoke(newTask);
        
        if (showDebugInfo)
            Debug.Log($"Created task from data: {taskData.taskTitle} ({taskData.taskType})");
        
        return newTask;
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
        
        // check vehicle capacity
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
        int maxVehicleCapacity = vehicles.Length > 0 ? vehicles.Max(v => v.GetMaxCapacity()) : 10;
        
        // compute transport amount
        int availablePopulation = community.GetCurrentPopulation();
        int motelSpace = motel.GetPopulationCapacity() - motel.GetCurrentPopulation();
        int transportAmount = Mathf.Min(availablePopulation, motelSpace, 30);
        if (transportAmount <= 0)
        {
            Debug.LogError("Cannot create transport task - no available population or motel space");
            return;
        }
        GameTask transportTask = CreateTask("Emergency Population Transport", TaskType.Emergency, community.name,
            $"We have {transportAmount} displaced families that need immediate transport to temporary housing at the motel.");

        transportTask.requiresDelivery = true;
        transportTask.deliveryCargoType = ResourceType.Population;
        transportTask.deliveryQuantity = transportAmount;
        transportTask.deliverySource = community;
        transportTask.deliveryDestination = motel;
        transportTask.roundsRemaining = 2;
        transportTask.hasRealTimeLimit = false;
        transportTask.deliveryFailureSatisfactionPenalty = 15f; // -15 sat Penalty for failure
        
        transportTask.impacts.Add(new TaskImpact(ImpactType.Clients, transportAmount, false, "People to Transport"));
        transportTask.impacts.Add(new TaskImpact(ImpactType.TotalTime, 2, false, "Rounds Remaining"));
        transportTask.impacts.Add(new TaskImpact(ImpactType.Satisfaction, -15, false, "Failure Penalty"));
        transportTask.impacts.Add(new TaskImpact(ImpactType.Budget, -15, false, "Cost of Transport"));

        transportTask.agentMessages.Add(new AgentMessage("We have an urgent situation!", defaultAgentAvatar));
        transportTask.agentMessages.Add(new AgentMessage($"{transportAmount} families at {community.name} need immediate transport to {motel.name}."));
        transportTask.agentMessages.Add(new AgentMessage("We must get them relocated within 4 hours or they'll lose faith in our response."));
        transportTask.agentMessages.Add(new AgentMessage("Are you ready to dispatch a vehicle?"));
        
        AgentChoice confirmChoice = new AgentChoice(1, "Confirm - Dispatch vehicle immediately");
        confirmChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 5, false, "Quick Response Bonus"));
        transportTask.agentChoices.Add(confirmChoice);
        
        AgentChoice delayChoice = new AgentChoice(2, "Wait - Check vehicle availability first");
        delayChoice.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -3, false, "Delayed Response"));
        transportTask.agentChoices.Add(delayChoice);
        
        if (showDebugInfo)
            Debug.Log($"Created test population transport task: {transportAmount} people from {community.name} to {motel.name}. Max vehicle capacity: {maxVehicleCapacity}");
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