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

    [Header("Motel Cost (shown only for Motel)")]
    public TextMeshProUGUI motelCostText;

    [Header("Deliveries")]
    public TextMeshProUGUI expectedDeliveriesText;
    public TextMeshProUGUI outgoingDeliveriesText;

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
        UpdateExpectedDeliveries(facility);
        UpdateOutgoingDeliveries(facility);

        // log all displayed info
        LogFacilityView(facility);
    }

    void LogFacilityView(MonoBehaviour facility)
    {
        var sb = new System.Text.StringBuilder();

        if (facility is Building b)
        {
            BuildingResourceStorage storage = b.GetComponent<BuildingResourceStorage>();
            int pop     = storage?.GetResourceAmount(ResourceType.Population) ?? 0;
            int popCap  = storage?.GetResourceCapacity(ResourceType.Population) ?? 0;
            int food    = storage?.GetResourceAmount(ResourceType.FoodPacks) ?? 0;
            int foodCap = storage?.GetResourceCapacity(ResourceType.FoodPacks) ?? 0;

            var workers = WorkerSystem.Instance?.GetWorkersByBuildingId(b.GetOriginalSiteId());
            int trained   = workers?.Count(w => w.Type == WorkerType.Trained) ?? 0;
            int untrained = workers?.Count(w => w.Type == WorkerType.Untrained) ?? 0;

            sb.Append($"FACILITY_VIEW | name={b.name} | type={b.GetBuildingType()}");
            sb.Append($" | status={b.GetCurrentStatus()}");
            sb.Append($" | population={pop}/{popCap} | food={food}/{foodCap}");
            sb.Append($" | workers={trained}trained+{untrained}untrained | workforce={b.GetAssignedWorkforce()}/{b.GetRequiredWorkforce()}");
            sb.Append($" | flooded={IsAffectedByFlood(b.gameObject)}");
        }
        else if (facility is PrebuiltBuilding pb)
        {
            var storage   = pb.GetResourceStorage();
            int food      = storage?.GetResourceAmount(ResourceType.FoodPacks) ?? 0;
            int foodCap   = storage?.GetResourceCapacity(ResourceType.FoodPacks) ?? 0;
            int pop       = pb.GetCurrentPopulation();
            int popCap    = pb.GetPopulationCapacity();
            string status = pop >= popCap ? "Full" : pop > 0 ? "Occupied" : "Vacant";

            sb.Append($"FACILITY_VIEW | name={pb.GetBuildingName()} | type={pb.GetPrebuiltType()} | id={pb.GetBuildingId()}");
            sb.Append($" | status={status}");
            sb.Append($" | population={pop}/{popCap} | food={food}/{foodCap}");
            sb.Append($" | flooded={IsAffectedByFlood(pb.gameObject)}");
        }

        // Active tasks
        var activeTasks = TaskSystem.Instance?.activeTasks
            .Where(t => t.affectedFacility == (facility is Building bld ? bld.name : ((PrebuiltBuilding)facility).GetBuildingName()))
            .ToList();
        if (activeTasks != null && activeTasks.Count > 0)
            sb.Append($" | tasks={string.Join(";", activeTasks.Select(t => $"[{t.taskType}]{t.taskTitle}({t.roundsRemaining}r)"))}");
        else
            sb.Append(" | tasks=none");

        // Deliveries
        sb.Append($" | incoming={expectedDeliveriesText?.text ?? "N/A"}");
        sb.Append($" | outgoing={outgoingDeliveriesText?.text ?? "N/A"}");

        GameLogPanel.Instance?.LogUIInteraction(sb.ToString());
    }

    void UpdateBuildingInfo(Building building)
    {
        BuildingType type = building.GetBuildingType();

        SetTextSafe(facilityNameText, building.GetDisplayName());
        SetTextSafe(facilityTypeText, type.ToString());
        HideField(siteIdText);
        HideField(positionText);

        BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();

        // Population — Shelter only
        if (type == BuildingType.Shelter && storage != null)
        {
            int pop = storage.GetResourceAmount(ResourceType.Population);
            int popCap = storage.GetResourceCapacity(ResourceType.Population);
            ShowField(populationText);
            SetTextSafe(populationText, $"Residents: {pop}/{popCap}");
            SetTextColor(populationText, GetResourceColor(pop, popCap));
        }
        else
        {
            HideField(populationText);
        }

        // Food packs — Kitchen only
        if (type == BuildingType.Kitchen && storage != null)
        {
            int food = storage.GetResourceAmount(ResourceType.FoodPacks);
            int foodCap = storage.GetResourceCapacity(ResourceType.FoodPacks);
            ShowField(foodPacksText);
            SetTextSafe(foodPacksText, $"Food Packs: {food}/{foodCap}");
            SetTextColor(foodPacksText, GetResourceColor(food, foodCap));
        }
        else
        {
            HideField(foodPacksText);
        }

        HideField(capacityText);

        // Workers — single line, no breakdown
        UpdateWorkerInfo(building);

        BuildingStatus status = building.GetCurrentStatus();
        SetTextSafe(statusText, $"Status: {GetStatusDisplayName(status)}");
        SetTextColor(statusText, GetStatusColor(status));

        UpdateFloodStatus(building.gameObject);
        UpdateRoadConnection(building.gameObject);
    }

    void UpdatePrebuiltBuildingInfo(PrebuiltBuilding prebuilt)
    {
        PrebuiltBuildingType type = prebuilt.GetPrebuiltType();

        SetTextSafe(facilityNameText, prebuilt.GetBuildingName());
        SetTextSafe(facilityTypeText, type.ToString());
        HideField(siteIdText);
        HideField(positionText);
        HideField(workersHeaderText);
        HideField(trainedWorkersText);
        HideField(untrainedWorkersText);
        HideField(totalWorkforceText);
        HideField(capacityText);
        HideField(foodPacksText);

        int population = prebuilt.GetCurrentPopulation();
        int populationCap = prebuilt.GetPopulationCapacity();
        ShowField(populationText);
        SetTextSafe(populationText, $"Residents: {population}/{populationCap}");
        SetTextColor(populationText, GetResourceColor(population, populationCap));

        string statusLabel = population >= populationCap ? "Full" : population > 0 ? "Occupied" : "Vacant";
        SetTextSafe(statusText, $"Status: {statusLabel}");
        SetTextColor(statusText, population >= populationCap ? errorColor : population > 0 ? goodColor : normalColor);

        if (motelCostText != null)
        {
            if (type == PrebuiltBuildingType.Motel)
            {
                var costMgr = FindObjectOfType<MotelCostManager>();
                if (costMgr != null)
                {
                    float dailyCost = costMgr.GetCurrentDailyCost();
                    motelCostText.text = $"Daily Cost: ${dailyCost:F0}/day";
                    motelCostText.color = dailyCost > 0 ? warningColor : normalColor;
                }
                motelCostText.gameObject.SetActive(true);
            }
            else
            {
                motelCostText.gameObject.SetActive(false);
            }
        }

        UpdateFloodStatus(prebuilt.gameObject);
        UpdateRoadConnection(prebuilt.gameObject);
    }

    void UpdateWorkerInfo(Building building)
    {
        HideField(trainedWorkersText);
        HideField(untrainedWorkersText);
        HideField(totalWorkforceText);

        int assigned = building.GetAssignedWorkforce();
        int required = building.GetRequiredWorkforce();

        ShowField(workersHeaderText);
        SetTextSafe(workersHeaderText, $"Workers: {assigned}/{required}");
        SetTextColor(workersHeaderText, assigned >= required ? goodColor : assigned > 0 ? warningColor : errorColor);
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

    string GetStatusDisplayName(BuildingStatus status)
    {
        switch (status)
        {
            case BuildingStatus.UnderConstruction: return "In Progress";
            case BuildingStatus.NeedWorker: return "Need Worker";
            case BuildingStatus.InUse: return "In Use";
            case BuildingStatus.Disabled: return "Closed";
            default: return status.ToString();
        }
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

    void HideField(TextMeshProUGUI field)
    {
        if (field != null) field.gameObject.SetActive(false);
    }

    void ShowField(TextMeshProUGUI field)
    {
        if (field != null) field.gameObject.SetActive(true);
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
    void UpdateExpectedDeliveries(MonoBehaviour facility)
    {
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null || expectedDeliveriesText == null)
            return;
        
        List<DeliveryTask> incoming = deliverySystem.GetIncomingDeliveries(facility);
        
        if (incoming.Count == 0)
        {
            expectedDeliveriesText.text = "No deliveries expected";
            return;
        }
        
        // Count by cargo type
        int foodPacks = incoming.Where(d => d.cargoType == ResourceType.FoodPacks).Sum(d => d.quantity);
        int clients = incoming.Where(d => d.cargoType == ResourceType.Population).Sum(d => d.quantity);
        
        string message = "";
        if (foodPacks > 0) message += $"{foodPacks} food packs on the way. ";
        if (clients > 0) message += $"{clients} clients on the way.";
        
        expectedDeliveriesText.text = message.Trim();
    }
    
    void UpdateOutgoingDeliveries(MonoBehaviour facility)
    {
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null || outgoingDeliveriesText == null)
            return;
        
        List<DeliveryTask> outgoing = deliverySystem.GetOutgoingDeliveries(facility);
        
        if (outgoing.Count == 0)
        {
            outgoingDeliveriesText.text = "No outgoing deliveries";
            return;
        }
        
        // Group by destination
        var grouped = outgoing.GroupBy(d => d.destinationBuilding);
        
        string message = "";
        foreach (var group in grouped)
        {
            int foodPacks = group.Where(d => d.cargoType == ResourceType.FoodPacks).Sum(d => d.quantity);
            int clients = group.Where(d => d.cargoType == ResourceType.Population).Sum(d => d.quantity);
            
            string destName = GetBuildingDisplayName(group.Key);
            
            if (foodPacks > 0) message += $"{foodPacks} food packs leaving, going to {destName}. ";
            if (clients > 0) message += $"{clients} clients leaving, going to {destName}. ";
        }
        
        outgoingDeliveriesText.text = message.Trim();
    }

    string GetBuildingDisplayName(MonoBehaviour building)
    {
        if (building == null) return "Unknown";
        
        Building b = building.GetComponent<Building>();
        if (b != null)
            return b.GetDisplayName();
        
        PrebuiltBuilding pb = building.GetComponent<PrebuiltBuilding>();
        if (pb != null)
            return pb.GetBuildingName();
        
        return building.name;
    }
}