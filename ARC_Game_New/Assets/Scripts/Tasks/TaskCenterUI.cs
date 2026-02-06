using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class TaskCenterUI : MonoBehaviour
{
    [Header("Main Panel")]
    public GameObject taskCenterPanel;
    public Button closeButton;

    [Header("Filter Buttons")]
    public Button allTasksButton;
    public Button emergencyTasksButton;
    public Button demandTasksButton;
    public Button advisoryTasksButton;
    public Button alertTasksButton;

    [Header("Task List")]
    public ScrollRect taskScrollView;
    public Transform taskListContent;
    public GameObject taskItemPrefab;

    [Header("Task Detail")]
    public TaskDetailUI taskDetailUI;

    [Header("Filter Colors")]
    public Color activeFilterColor = Color.green;
    public Color inactiveFilterColor = Color.white;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private TaskType? currentFilter = null;
    private List<GameObject> currentTaskItems = new List<GameObject>();
    private bool isUIOpen = false;

    void Start()
    {
        SetupUI();

        // Subscribe to task system events
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCreated += OnTaskCreated;
            TaskSystem.Instance.OnTaskCompleted += OnTaskUpdated;
            TaskSystem.Instance.OnTaskExpired += OnTaskUpdated;
        }

        // Hide panel initially
        if (taskCenterPanel != null)
            taskCenterPanel.SetActive(false);
            
    }

    void SetupUI()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(CloseTaskCenter);

        // Setup filter buttons
        if (allTasksButton != null)
            allTasksButton.onClick.AddListener(() => SetFilter(null));

        if (emergencyTasksButton != null)
            emergencyTasksButton.onClick.AddListener(() => SetFilter(TaskType.Emergency));

        if (demandTasksButton != null)
            demandTasksButton.onClick.AddListener(() => SetFilter(TaskType.Demand));

        if (advisoryTasksButton != null)
            advisoryTasksButton.onClick.AddListener(() => SetFilter(TaskType.Advisory));

        if (alertTasksButton != null)
            alertTasksButton.onClick.AddListener(() => SetFilter(TaskType.Alert));
    }

    

    public void OpenTaskCenter()
    {
        if (taskCenterPanel != null)
        {
            taskCenterPanel.SetActive(true);
            RefreshTaskList();
            UpdateFilterButtons();
            isUIOpen = true;

            if (showDebugInfo)
                Debug.Log("Task Center opened");
        }
    }

    public void CloseTaskCenter()
    {
        if (taskCenterPanel != null)
        {
            taskCenterPanel.SetActive(false);
            isUIOpen = false;

            if (showDebugInfo)
                Debug.Log("Task Center closed");
        }
    }

    void SetFilter(TaskType? filter)
    {
        currentFilter = filter;
        RefreshTaskList();
        UpdateFilterButtons();

        string filterName = filter?.ToString() ?? "All";
        if (showDebugInfo)
            Debug.Log($"Task filter set to: {filterName}");
    }

    void UpdateFilterButtons()
    {
        // Reset all button colors
        SetButtonColor(allTasksButton, currentFilter == null ? activeFilterColor : inactiveFilterColor);
        SetButtonColor(emergencyTasksButton, currentFilter == TaskType.Emergency ? activeFilterColor : inactiveFilterColor);
        SetButtonColor(demandTasksButton, currentFilter == TaskType.Demand ? activeFilterColor : inactiveFilterColor);
        SetButtonColor(advisoryTasksButton, currentFilter == TaskType.Advisory ? activeFilterColor : inactiveFilterColor);
        SetButtonColor(alertTasksButton, currentFilter == TaskType.Alert ? activeFilterColor : inactiveFilterColor);
    }

    void SetButtonColor(Button button, Color color)
    {
        if (button != null)
        {
            Image buttonImage = button.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = color;
        }
    }

    public void RefreshTaskList()
    {
        if (TaskSystem.Instance == null || taskListContent == null)
            return;

        // Clear existing items
        ClearTaskList();

        // Get filtered tasks
        List<GameTask> tasksToShow = GetFilteredTasks();

        // Create task items
        foreach (GameTask task in tasksToShow)
        {
            CreateTaskItem(task);
        }

        if (showDebugInfo)
            Debug.Log($"Refreshed task list: {tasksToShow.Count} tasks shown");
    }

    List<GameTask> GetFilteredTasks()
    {
        List<GameTask> allTasks = new List<GameTask>();

        // Get active tasks
        allTasks.AddRange(TaskSystem.Instance.GetAllActiveTasks());

        // Get inactive tasks
        allTasks.AddRange(TaskSystem.Instance.GetTasksByStatus(TaskStatus.Incomplete));
        allTasks.AddRange(TaskSystem.Instance.GetTasksByStatus(TaskStatus.Completed));
        allTasks.AddRange(TaskSystem.Instance.GetTasksByStatus(TaskStatus.Expired));

        // FILTER OUT Other tasks
        allTasks = allTasks.Where(t => t.taskType != TaskType.Other).ToList();
        
        // Apply filter
        if (currentFilter.HasValue)
        {
            allTasks = allTasks.Where(t => t.taskType == currentFilter.Value).ToList();
        }

        // sort by：Active > InProgress > Incomplete > Expired > Completed
        allTasks = allTasks.OrderBy(t => GetTaskStatusPriority(t.status))
                     .ThenBy(t => GetTaskPriority(t.taskType))
                     .ThenBy(t => t.timeCreated).ToList();

        return allTasks;
    }

    int GetTaskPriority(TaskType type)
    {
        switch (type)
        {
            case TaskType.Emergency: return 1;
            case TaskType.Demand: return 2;
            case TaskType.Alert: return 3;
            case TaskType.Advisory: return 4;
            default: return 5;
        }
    }

    int GetTaskStatusPriority(TaskStatus status)
    {
        switch (status)
        {
            case TaskStatus.Active: return 1;
            case TaskStatus.InProgress: return 2;
            case TaskStatus.Incomplete: return 3;
            case TaskStatus.Expired: return 4;
            case TaskStatus.Completed: return 5;
            default: return 6;
        }
    }

    void CreateTaskItem(GameTask task)
    {
        if (taskItemPrefab == null)
        {
            Debug.LogError("Task item prefab is not assigned!");
            return;
        }

        GameObject taskItem = Instantiate(taskItemPrefab, taskListContent);
        TaskItemUI taskItemUI = taskItem.GetComponent<TaskItemUI>();

        if (taskItemUI != null)
        {
            taskItemUI.Initialize(task, this);
        }
        else
        {
            Debug.LogError("TaskItemUI component not found on task item prefab!");
        }

        currentTaskItems.Add(taskItem);
    }

    void ClearTaskList()
    {
        foreach (GameObject item in currentTaskItems)
        {
            if (item != null)
                Destroy(item);
        }
        currentTaskItems.Clear();
    }

    public void OnTaskItemClicked(GameTask task)
    {
        if (showDebugInfo)
            Debug.Log($"Task item clicked: {task.taskTitle}");

        if (taskDetailUI != null)
        {
            taskDetailUI.ShowTaskDetail(task);
            // Note: Don't close task center automatically - let user decide

            if (showDebugInfo)
                Debug.Log($"Opening task detail for: {task.taskTitle}");
        }
        else
        {
            Debug.LogError("TaskDetailUI reference not set! Please assign it in the inspector.");
        }
    }

    void OnTaskCreated(GameTask task)
    {
        // Refresh the list if the task center is open
        if (taskCenterPanel != null && taskCenterPanel.activeInHierarchy)
        {
            RefreshTaskList();
        }
    }

    void OnTaskUpdated(GameTask task)
    {
        // Refresh the list if the task center is open
        if (taskCenterPanel != null && taskCenterPanel.activeInHierarchy)
        {
            RefreshTaskList();
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCreated -= OnTaskCreated;
            TaskSystem.Instance.OnTaskCompleted -= OnTaskUpdated;
            TaskSystem.Instance.OnTaskExpired -= OnTaskUpdated;
        }
    }

    // Public method to toggle task center
    public void ToggleTaskCenter()
    {
        if (isUIOpen)
            CloseTaskCenter();
        else
            OpenTaskCenter();
    }

    public bool IsUIOpen()
    {
        return isUIOpen;
    }
}

