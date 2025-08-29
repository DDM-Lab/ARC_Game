using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class FacilityInfoPanel : MonoBehaviour
{
    [Header("Basic Info")]
    public TextMeshProUGUI facilityNameText;
    public TextMeshProUGUI facilityTypeText;
    public TextMeshProUGUI siteIdText;
    public TextMeshProUGUI positionText;

    [Header("Resources")]
    public TextMeshProUGUI populationText;
    public TextMeshProUGUI foodPacksText;
    public TextMeshProUGUI capacityText;

    [Header("Workers")]
    public TextMeshProUGUI workersHeaderText;
    public TextMeshProUGUI trainedWorkersText;
    public TextMeshProUGUI untrainedWorkersText;
    public TextMeshProUGUI totalWorkforceText;

    [Header("Status")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI floodStatusText;
    public TextMeshProUGUI roadConnectionText;

    [Header("Tasks")]
    public TextMeshProUGUI tasksHeaderText;
    public Transform taskListParent;
    public GameObject taskListItemPrefab;

    [Header("Close Button")]
    public Button closeButton;

    [Header("Colors")]
    public Color normalColor = Color.white;
    public Color warningColor = Color.yellow;
    public Color errorColor = Color.red;
    public Color goodColor = Color.green;

    private List<GameObject> currentTaskItems = new List<GameObject>();

    void Start()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClicked);
    }

    public void UpdateFacilityInfo(MonoBehaviour facility)
    {
        if (facility == null) return;

        ClearTaskList();

        if (facility is Building building)
        {
            UpdateBuildingInfo(building);
        }
        else if (facility is PrebuiltBuilding prebuilt)
        {
            UpdatePrebuiltBuildingInfo(prebuilt);
        }

        UpdateTasksList(facility);
    }

    void UpdateBuildingInfo(Building building)
    {
        // Basic Info
        SetTextSafe(facilityNameText, building.name);
        SetTextSafe(facilityTypeText, building.GetBuildingType().ToString());
        SetTextSafe(siteIdText, $"Site ID: {building.GetOriginalSiteId()}");
        SetTextSafe(positionText, $"Position: ({building.transform.position.x:F1}, {building.transform.position.y:F1})");

        // Resources
        BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();
        if (storage != null)
        {
            int population = storage.GetResourceAmount(ResourceType.Population);
            int populationCap = storage.GetResourceCapacity(ResourceType.Population);
            int foodPacks = storage.GetResourceAmount(ResourceType.FoodPacks);
            int foodCap = storage.GetResourceCapacity(ResourceType.FoodPacks);

            SetTextSafe(populationText, $"Population: {population}/{populationCap}");
            SetTextSafe(foodPacksText, $"Food Packs: {foodPacks}/{foodCap}");
            SetTextSafe(capacityText, $"Total Capacity: {building.GetCapacity()}");

            // Color coding for resources
            SetTextColor(populationText, GetResourceColor(population, populationCap));
            SetTextColor(foodPacksText, GetResourceColor(foodPacks, foodCap));
        }
        else
        {
            SetTextSafe(populationText, "Population: N/A");
            SetTextSafe(foodPacksText, "Food Packs: N/A");
            SetTextSafe(capacityText, $"Capacity: {building.GetCapacity()}");
        }

        // Workers
        UpdateWorkerInfo(building);

        // Status
        BuildingStatus status = building.GetCurrentStatus();
        SetTextSafe(statusText, $"Status: {status}");
        SetTextColor(statusText, GetStatusColor(status));

        // Flood status
        UpdateFloodStatus(building.gameObject);

        // Road connection
        UpdateRoadConnection(building.gameObject);
    }

    void UpdatePrebuiltBuildingInfo(PrebuiltBuilding prebuilt)
    {
        // Basic Info
        SetTextSafe(facilityNameText, prebuilt.GetBuildingName());
        SetTextSafe(facilityTypeText, prebuilt.GetPrebuiltType().ToString());
        SetTextSafe(siteIdText, $"Building ID: {prebuilt.GetBuildingId()}");
        SetTextSafe(positionText, $"Position: ({prebuilt.transform.position.x:F1}, {prebuilt.transform.position.y:F1})");

        // Resources
        int population = prebuilt.GetCurrentPopulation();
        int populationCap = prebuilt.GetPopulationCapacity();

        SetTextSafe(populationText, $"Population: {population}/{populationCap}");
        SetTextColor(populationText, GetResourceColor(population, populationCap));

        BuildingResourceStorage storage = prebuilt.GetResourceStorage();
        if (storage != null)
        {
            int foodPacks = storage.GetResourceAmount(ResourceType.FoodPacks);
            int foodCap = storage.GetResourceCapacity(ResourceType.FoodPacks);
            SetTextSafe(foodPacksText, $"Food Packs: {foodPacks}/{foodCap}");
            SetTextColor(foodPacksText, GetResourceColor(foodPacks, foodCap));
        }
        else
        {
            SetTextSafe(foodPacksText, "Food Packs: N/A");
        }

        SetTextSafe(capacityText, $"Capacity: {populationCap}");

        // Workers (Prebuilt buildings typically don't have workers)
        SetTextSafe(workersHeaderText, "Workers: N/A (Prebuilt)");
        SetTextSafe(trainedWorkersText, "Trained: N/A");
        SetTextSafe(untrainedWorkersText, "Untrained: N/A");
        SetTextSafe(totalWorkforceText, "Workforce: N/A");

        // Status
        string statusText = population > 0 ? "Occupied" : "Vacant";
        if (population >= populationCap) statusText = "Full";

        SetTextSafe(this.statusText, $"Status: {statusText}");
        SetTextColor(this.statusText, population >= populationCap ? errorColor : (population > 0 ? goodColor : normalColor));

        // Flood status
        UpdateFloodStatus(prebuilt.gameObject);

        // Road connection
        UpdateRoadConnection(prebuilt.gameObject);
    }

    void UpdateWorkerInfo(Building building)
    {
        if (WorkerSystem.Instance == null)
        {
            SetTextSafe(workersHeaderText, "Workers: System Unavailable");
            SetTextSafe(trainedWorkersText, "");
            SetTextSafe(untrainedWorkersText, "");
            SetTextSafe(totalWorkforceText, "");
            return;
        }

        var assignedWorkers = WorkerSystem.Instance.GetWorkersByBuildingId(building.GetOriginalSiteId());
        int trainedWorkers = assignedWorkers.Count(w => w.Type == WorkerType.Trained);
        int untrainedWorkers = assignedWorkers.Count(w => w.Type == WorkerType.Untrained);
        int totalWorkforce = building.GetAssignedWorkforce();
        int requiredWorkforce = building.GetRequiredWorkforce();

        SetTextSafe(workersHeaderText, $"Workers ({assignedWorkers.Count} assigned):");
        SetTextSafe(trainedWorkersText, $"Trained: {trainedWorkers}");
        SetTextSafe(untrainedWorkersText, $"Untrained: {untrainedWorkers}");
        SetTextSafe(totalWorkforceText, $"Workforce: {totalWorkforce}/{requiredWorkforce}");

        // Color coding for workforce
        Color workforceColor = totalWorkforce >= requiredWorkforce ? goodColor :
                              totalWorkforce > 0 ? warningColor : errorColor;
        SetTextColor(totalWorkforceText, workforceColor);
    }

    void UpdateFloodStatus(GameObject facilityObj)
    {
        // Check if facility is affected by flood
        bool isFlooded = IsAffectedByFlood(facilityObj);

        if (isFlooded)
        {
            SetTextSafe(floodStatusText, "Flood Status: FLOODED");
            SetTextColor(floodStatusText, errorColor);
        }
        else
        {
            SetTextSafe(floodStatusText, "Flood Status: Clear");
            SetTextColor(floodStatusText, goodColor);
        }
    }

    void UpdateRoadConnection(GameObject facilityObj)
    {
        RoadConnection roadConn = facilityObj.GetComponent<RoadConnection>();
        if (roadConn != null)
        {
            bool isConnected = roadConn.IsConnectedToRoad;
            SetTextSafe(roadConnectionText, $"Road Access: {(isConnected ? "Connected" : "Blocked")}");
            SetTextColor(roadConnectionText, isConnected ? goodColor : errorColor);
        }
        else
        {
            SetTextSafe(roadConnectionText, "Road Access: N/A");
            SetTextColor(roadConnectionText, normalColor);
        }
    }

    bool IsAffectedByFlood(GameObject facilityObj)
    {
        // Check if there's a FloodSystem and if this facility is flooded
        if (FloodSystem.Instance != null)
        {
            // You might need to implement this method in FloodSystem
            // For now, return false as placeholder
            return false;
        }
        return false;
    }

    void UpdateTasksList(MonoBehaviour facility)
    {
        if (TaskSystem.Instance == null)
        {
            SetTextSafe(tasksHeaderText, "Active Tasks: System Unavailable");
            return;
        }

        List<GameTask> relatedTasks = GetRelatedTasks(facility);

        SetTextSafe(tasksHeaderText, $"Active Tasks ({relatedTasks.Count}):");

        if (relatedTasks.Count > 0)
        {
            foreach (GameTask task in relatedTasks)
            {
                CreateTaskListItem(task);
            }
        }
        // Remove the empty prefab creation when no tasks
    }

    List<GameTask> GetRelatedTasks(MonoBehaviour facility)
    {
        var allTasks = TaskSystem.Instance.GetAllActiveTasks();
        var relatedTasks = new List<GameTask>();

        foreach (GameTask task in allTasks)
        {
            // Skip alert tasks
            if (task.taskType == TaskType.Alert)
                continue;

            // Skip global tasks (tasks without specific facility)
            if (string.IsNullOrEmpty(task.affectedFacility) ||
                task.affectedFacility.ToLower().Contains("global"))
                continue;

            // Check if task is specifically related to THIS facility (exact match)
            if (IsTaskSpecificToFacility(task, facility))
            {
                relatedTasks.Add(task);
            }
        }

        return relatedTasks;
    }

    bool IsTaskSpecificToFacility(GameTask task, MonoBehaviour facility)
    {
        // TBA: Need better way to filter tasks
        // Direct facility name match
        if (task.affectedFacility != null &&
        (facility.name.Equals(task.affectedFacility, System.StringComparison.OrdinalIgnoreCase) ||
        task.affectedFacility.Contains(facility.name) ||
        facility.name.Contains(task.affectedFacility)))
        {
            return true;
        }

        // Check if task has delivery specifically involving THIS facility
        if (HasDeliveryInvolvingSpecificFacility(task, facility))
        {
            return true;
        }

        return false;
    }

    bool HasDeliveryInvolvingSpecificFacility(GameTask task, MonoBehaviour facility)
    {
        // Check delivery system for active deliveries involving this specific facility
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return false;

        var activeTasks = deliverySystem.GetActiveTasks();

        foreach (var deliveryTask in activeTasks)
        {
            if (deliveryTask.sourceBuilding == facility || deliveryTask.destinationBuilding == facility)
            {
                // Check if this delivery is linked to the game task
                if (task.linkedDeliveryTaskIds != null && task.linkedDeliveryTaskIds.Contains(deliveryTask.taskId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    void CreateTaskListItem(GameTask task)
    {
        if (taskListItemPrefab == null || taskListParent == null) return;

        GameObject item = Instantiate(taskListItemPrefab, taskListParent);
        currentTaskItems.Add(item);

        // Find text components in the task item
        TextMeshProUGUI taskTitle = item.transform.Find("Statlayout")?.Find("TaskTitle")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI taskType = item.transform.Find("Statlayout")?.Find("TaskType")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI taskRounds = item.transform.Find("Statlayout")?.Find("TaskRounds")?.GetComponent<TextMeshProUGUI>();
        Button viewButton = item.transform.Find("ViewButton")?.GetComponent<Button>();

        if (taskTitle != null)
        {
            taskTitle.text = task.taskTitle;
        }

        if (taskType != null)
        {
            taskType.text = task.taskType.ToString();
            taskType.color = GetTaskTypeColor(task.taskType);
        }

        if (taskRounds != null)
        {
            taskRounds.text = $"{task.roundsRemaining} rounds";
            taskRounds.color = task.roundsRemaining <= 1 ? errorColor :
                             task.roundsRemaining <= 2 ? warningColor : normalColor;
        }

        // Setup View button
        if (viewButton != null)
        {
            // Remove existing listeners to avoid duplicates
            viewButton.onClick.RemoveAllListeners();

            // Add click listener to open the task
            viewButton.onClick.AddListener(() => OnViewTaskButtonClicked(task));
        }
    }

    void OnViewTaskButtonClicked(GameTask task)
    {
        if (task == null) return;

        // Close this facility panel first
        if (FacilityInfoManager.Instance != null)
        {
            FacilityInfoManager.Instance.CloseFacilityPanel();
        }

        // Open the task in TaskDetailUI
        TaskDetailUI taskDetailUI = FindObjectOfType<TaskDetailUI>();
        if (taskDetailUI != null)
        {
            taskDetailUI.ShowTaskDetail(task);
            Debug.Log($"Opened task detail for: {task.taskTitle}");
        }
        else
        {
            Debug.LogWarning("TaskDetailUI not found - cannot open task detail");

            // Fallback: Try to open TaskCenterUI and select the task
            TaskCenterUI taskCenterUI = FindObjectOfType<TaskCenterUI>();
            if (taskCenterUI != null)
            {
                taskCenterUI.OpenTaskCenter();
                Debug.Log($"Opened task center - please locate task: {task.taskTitle}");
            }
        }
    }

    void CreateNoTasksItem()
    {
        if (taskListItemPrefab == null || taskListParent == null) return;

        GameObject item = Instantiate(taskListItemPrefab, taskListParent);
        currentTaskItems.Add(item);

        TextMeshProUGUI taskTitle = item.transform.Find("TaskTitle")?.GetComponent<TextMeshProUGUI>();
        if (taskTitle != null)
        {
            taskTitle.text = "No active tasks";
            taskTitle.color = normalColor;
        }

        // Hide other components
        Transform taskType = item.transform.Find("TaskType");
        Transform taskRounds = item.transform.Find("TaskRounds");
        if (taskType != null) taskType.gameObject.SetActive(false);
        if (taskRounds != null) taskRounds.gameObject.SetActive(false);
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

    // Helper methods for colors
    Color GetResourceColor(int current, int capacity)
    {
        if (capacity == 0) return normalColor;

        float ratio = (float)current / capacity;
        if (ratio >= 0.9f) return errorColor;   // Nearly full/overloaded
        if (ratio >= 0.7f) return warningColor; // Getting full
        if (ratio > 0) return goodColor;        // Has resources
        return normalColor;                     // Empty
    }

    Color GetStatusColor(BuildingStatus status)
    {
        switch (status)
        {
            case BuildingStatus.InUse: return goodColor;
            case BuildingStatus.UnderConstruction: return warningColor;
            case BuildingStatus.NeedWorker: return warningColor;
            case BuildingStatus.Disabled: return errorColor;
            default: return normalColor;
        }
    }

    Color GetTaskTypeColor(TaskType taskType)
    {
        switch (taskType)
        {
            case TaskType.Emergency: return errorColor;
            case TaskType.Demand: return warningColor;
            case TaskType.Advisory: return normalColor;
            case TaskType.Alert: return goodColor;
            default: return normalColor;
        }
    }

    // Utility methods
    void SetTextSafe(TextMeshProUGUI textComponent, string value)
    {
        if (textComponent != null)
            textComponent.text = value;
    }

    void SetTextColor(TextMeshProUGUI textComponent, Color color)
    {
        if (textComponent != null)
            textComponent.color = color;
    }

    void OnCloseButtonClicked()
    {
        if (FacilityInfoManager.Instance != null)
        {
            FacilityInfoManager.Instance.CloseFacilityPanel();
        }
    }

    public bool IsUIOpen()
    {
        return gameObject.activeSelf;
    }
}