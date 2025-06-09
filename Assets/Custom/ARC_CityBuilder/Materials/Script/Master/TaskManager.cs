using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CityBuilderCore;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Manages emergency tasks and task UI
/// 
/// IMPORTANT: This class only TRIGGERS GameEvents.OnTaskCompleted when tasks are completed.
/// It does NOT listen to GameEvents.OnTaskCompleted to avoid circular event loops.
/// External systems (like MasterGameManager) should subscribe to GameEvents.OnTaskCompleted
/// to receive notifications when tasks are finished.
/// </summary>
public class TaskManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform taskListContainer;
    [SerializeField] private GameObject taskEntryPrefab;
    [SerializeField] private GameObject emptyTasksMessage;
    
    [Header("Task Icons")]
    [SerializeField] private Sprite foodDeliveryIcon;
    [SerializeField] private Sprite emergencyRepairIcon;
    [SerializeField] private Sprite evacuationIcon;
    
    // Task management
    private Dictionary<string, TaskData> _allTasks = new Dictionary<string, TaskData>();
    private Dictionary<string, TaskEntryController> _taskEntries = new Dictionary<string, TaskEntryController>();
    private List<string> _completedTasks = new List<string>();
    
    // Events
    public event Action<string> OnTaskAdded;
    public event Action<string> OnTaskCompleted;
    public event Action<string> OnTaskFailed;
    public event Action OnAllTasksCompleted;
    
    public int ActiveTaskCount => _allTasks.Count - _completedTasks.Count;
    public int CompletedTaskCount => _completedTasks.Count;
    public bool HasActiveTasks => ActiveTaskCount > 0;
    
    private void Start()
    {
        ClearTaskList();
    }
    
    private void OnDestroy()
    {
        if (GameEvents.OnTaskCompleted != null)
            GameEvents.OnTaskCompleted -= ProcessTaskCompletion;
    }
    
    /// <summary>
    /// Clear all tasks from the UI
    /// </summary>
    public void ClearTaskList()
    {
        foreach (Transform child in taskListContainer)
        {
            Destroy(child.gameObject);
        }
        
        _taskEntries.Clear();
        _allTasks.Clear();
        _completedTasks.Clear();
        
        UpdateEmptyTasksVisibility();
    }
    
    /// <summary>
    /// Add a new task to the system
    /// </summary>
    public void AddTask(TaskData task)
    {
        if (_allTasks.ContainsKey(task.taskId))
        {
            Debug.LogWarning($"[TaskManager] Task {task.taskId} already exists");
            return;
        }
        
        // Add to data dictionary
        _allTasks[task.taskId] = task;
        
        // Create UI entry
        CreateTaskEntry(task);
        
        // Update UI
        UpdateEmptyTasksVisibility();
        
        Debug.Log($"[TaskManager] Added task: {task.title}");
        
        // Trigger events
        OnTaskAdded?.Invoke(task.taskId);
        GameEvents.OnTaskAdded?.Invoke(task.taskId);
    }
    
    /// <summary>
    /// Create UI entry for a task
    /// </summary>
    private void CreateTaskEntry(TaskData task)
    {
        if (taskEntryPrefab == null || taskListContainer == null)
        {
            Debug.LogError("[TaskManager] Task entry prefab or container not assigned");
            return;
        }
        
        GameObject entryObject = Instantiate(taskEntryPrefab, taskListContainer);
        TaskEntryController entryController = entryObject.GetComponent<TaskEntryController>();
        
        if (entryController == null)
        {
            entryController = entryObject.AddComponent<TaskEntryController>();
        }
        
        // Initialize the entry
        entryController.Initialize(task, OnTaskCompletedByUser);
        
        // Store reference to entry
        _taskEntries[task.taskId] = entryController;
    }
    
    /// <summary>
    /// Remove a task from the system
    /// </summary>
    public void RemoveTask(string taskId)
    {
        if (_taskEntries.TryGetValue(taskId, out TaskEntryController entry))
        {
            Destroy(entry.gameObject);
            _taskEntries.Remove(taskId);
        }
        
        _allTasks.Remove(taskId);
        _completedTasks.Remove(taskId);
        
        UpdateEmptyTasksVisibility();
        
        Debug.Log($"[TaskManager] Removed task: {taskId}");
    }
    
    /// <summary>
    /// Update task status
    /// </summary>
    public void UpdateTaskStatus(string taskId, TaskStatus newStatus)
    {
        if (_allTasks.TryGetValue(taskId, out TaskData task))
        {
            task.status = newStatus;
            
            if (_taskEntries.TryGetValue(taskId, out TaskEntryController entry))
            {
                entry.UpdateStatus(newStatus);
            }
            
            // Handle completion
            if (newStatus == TaskStatus.Complete && !_completedTasks.Contains(taskId))
            {
                _completedTasks.Add(taskId);
                ProcessTaskCompletion(taskId);
            }
        }
    }
    
    /// <summary>
    /// Called when user clicks complete button on a task
    /// </summary>
    private void OnTaskCompletedByUser(string taskId)
    {
        Debug.Log($"[TaskManager] User completed task: {taskId}");
    
        if (_allTasks.TryGetValue(taskId, out TaskData task))
        {
            task.status = TaskStatus.Complete;
        
            if (_taskEntries.TryGetValue(taskId, out TaskEntryController entry))
            {
                entry.UpdateStatus(TaskStatus.Complete);
            }
        
            if (!_completedTasks.Contains(taskId))
            {
                _completedTasks.Add(taskId);
            }
        
            // Process the completion and notify external systems
            ProcessTaskCompletion(taskId);  // Updated method name
        }
    }

    
    // Add this field at the top of your class
    private HashSet<string> _processingTasks = new HashSet<string>();
    
    /// <summary>
    /// Handle task completion
    /// </summary>
    /// <summary>
    /// Handle task completion and notify external systems
    /// </summary>
    private void ProcessTaskCompletion(string taskId)
    {
        // Prevent recursive processing of the same task
        if (_processingTasks.Contains(taskId))
        {
            Debug.LogWarning($"[TaskManager] Task {taskId} already being processed, skipping");
            return;
        }
    
        _processingTasks.Add(taskId);
    
        try
        {
            if (!_allTasks.ContainsKey(taskId)) return;

            var task = _allTasks[taskId];
            Debug.Log($"[TaskManager] Task completed: {task.title} (ID: {taskId})");

            // Handle task-specific completion logic (animations, etc.)
            HandleTaskCompletion(task);

            // Notify external systems (like MasterGameManager)
            GameEvents.OnTaskCompleted?.Invoke(taskId);
            Debug.Log($"[TaskManager] Notified external systems of task completion: {taskId}");

            // Check if all tasks are completed
            if (ActiveTaskCount == 0)
            {
                Debug.Log("[TaskManager] All tasks completed!");
                OnAllTasksCompleted?.Invoke();
            }
        }
        finally
        {
            _processingTasks.Remove(taskId);
        }
    }

    
    /// <summary>
    /// Handle completion logic for specific task types
    /// </summary>
    private void HandleTaskCompletion(TaskData task)
    {
        switch (task.taskId.Split('_')[0])
        {
            case "food":
                HandleFoodDeliveryCompletion(task);
                break;
            case "repair":
                HandleRepairCompletion(task);
                break;
            case "evacuation":
                HandleEvacuationCompletion(task);
                break;
            default:
                Debug.Log($"[TaskManager] Generic task completed: {task.title}");
                break;
        }
    }
    
    /// <summary>
    /// Handle food delivery task completion
    /// </summary>
    private void HandleFoodDeliveryCompletion(TaskData task)
    {
        if (task.customData.TryGetValue("shelterId", out object shelterIdObj))
        {
            string shelterId = shelterIdObj.ToString();
            int amount = task.requiredAmount;
            
            // Deliver food to shelter through MasterGameManager
            if (MasterGameManager.Instance != null)
            {
                MasterGameManager.Instance.DeliverFoodToShelter(shelterId, amount);
            }
            
            Debug.Log($"[TaskManager] Delivered {amount} food to shelter {shelterId}");
        }
    }
    
    /// <summary>
    /// Handle repair task completion
    /// </summary>
    private void HandleRepairCompletion(TaskData task)
    {
        Debug.Log($"[TaskManager] Repair task completed: {task.title}");
        // Add repair logic here
    }
    
    /// <summary>
    /// Handle evacuation task completion
    /// </summary>
    private void HandleEvacuationCompletion(TaskData task)
    {
        Debug.Log($"[TaskManager] Evacuation task completed: {task.title}");
        // Add evacuation logic here
    }
    
    /// <summary>
    /// Generate food delivery tasks for all shelters that need food
    /// </summary>
    public void GenerateFoodDeliveryTasks()
    {
        if (BuildingSystem.Instance == null) return;
        
        var shelters = BuildingSystem.Instance.GetAllShelters();
        
        foreach (var shelter in shelters)
        {
            if (ShelterNeedsFood(shelter))
            {
                CreateFoodDeliveryTask(shelter);
            }
        }
    }
    
    /// <summary>
    /// Create a food delivery task for a specific shelter
    /// </summary>
    private void CreateFoodDeliveryTask(Building shelter)
    {
        string taskId = $"food_delivery_{shelter.GetInstanceID()}";
        
        TaskData foodTask = new TaskData
        {
            taskId = taskId,
            title = "Food Delivery",
            description = $"Deliver 10 food packs to {shelter.name}",
            taskIcon = foodDeliveryIcon,
            status = TaskStatus.Todo,
            targetLocation = shelter.name,
            requiredAmount = 10
        };
        
        // Add shelter reference to custom data
        foodTask.customData["shelterId"] = shelter.GetInstanceID().ToString();
        
        AddTask(foodTask);
    }
    
    /// <summary>
    /// Check if a shelter needs food
    /// </summary>
    private bool ShelterNeedsFood(Building shelter)
    {
        // Basic implementation - can be extended based on shelter logic
        var shelterLogic = shelter.GetComponent<ShelterLogic>();
        if (shelterLogic != null)
        {
            return shelterLogic.NeedsFood();
        }
        
        return true; // Default to needing food
    }
    
    /// <summary>
    /// Update empty tasks message visibility
    /// </summary>
    private void UpdateEmptyTasksVisibility()
    {
        if (emptyTasksMessage != null)
        {
            emptyTasksMessage.SetActive(ActiveTaskCount == 0);
        }
    }
    
    /// <summary>
    /// Get task by ID
    /// </summary>
    public TaskData GetTask(string taskId)
    {
        return _allTasks.TryGetValue(taskId, out TaskData task) ? task : null;
    }
    
    /// <summary>
    /// Get all active tasks
    /// </summary>
    public List<TaskData> GetActiveTasks()
    {
        List<TaskData> activeTasks = new List<TaskData>();
        
        foreach (var task in _allTasks.Values)
        {
            if (!_completedTasks.Contains(task.taskId))
            {
                activeTasks.Add(task);
            }
        }
        
        return activeTasks;
    }
    
    /// <summary>
    /// Get task completion statistics
    /// </summary>
    public TaskStatistics GetTaskStatistics()
    {
        return new TaskStatistics
        {
            totalTasks = _allTasks.Count,
            completedTasks = _completedTasks.Count,
            activeTasks = ActiveTaskCount,
            completionRate = _allTasks.Count > 0 ? (float)_completedTasks.Count / _allTasks.Count : 0f
        };
    }
}

/// <summary>
/// Task statistics data structure
/// </summary>
[System.Serializable]
public class TaskStatistics
{
    public int totalTasks;
    public int completedTasks;
    public int activeTasks;
    public float completionRate;
}

/// <summary>
/// Static class for game events
/// </summary>
public static class GameEvents
{
    public static Action<string> OnTaskCompleted;
    public static Action<string> OnTaskAdded;
    public static Action<string> OnTaskFailed;
}
