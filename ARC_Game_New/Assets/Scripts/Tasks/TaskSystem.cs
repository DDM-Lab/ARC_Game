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
    public int roundsRemaining = 1; // Rounds until expiry
    public float realTimeRemaining = 300f; // Real-time countdown in seconds
    public bool hasRealTimeLimit = true;
    
    [Header("Impacts")]
    public List<TaskImpact> impacts = new List<TaskImpact>();
    
    [Header("Agent Conversation")]
    public List<AgentMessage> agentMessages = new List<AgentMessage>();
    public List<AgentChoice> agentChoices = new List<AgentChoice>();
    
    public float timeCreated;
    public bool isExpired => roundsRemaining <= 0 || (hasRealTimeLimit && realTimeRemaining <= 0);
    
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
        List<GameTask> expiredTasks = activeTasks.Where(t => t.isExpired && t.status == TaskStatus.Active).ToList();
        
        foreach (GameTask task in expiredTasks)
        {
            ExpireTask(task);
        }
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
                newTask.realTimeRemaining = 180f; // 3 minutes
                break;
            case TaskType.Demand:
                newTask.roundsRemaining = 1;
                newTask.realTimeRemaining = 300f; // 5 minutes
                break;
            case TaskType.Advisory:
                newTask.roundsRemaining = 3;
                newTask.hasRealTimeLimit = false;
                break;
            case TaskType.Alert:
                newTask.roundsRemaining = 2;
                newTask.realTimeRemaining = 600f; // 10 minutes
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