// TaskDatabase.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[CreateAssetMenu(fileName = "Task Database", menuName = "Task System/Task Database")]
public class TaskDatabase : ScriptableObject
{
    [Header("Available Tasks")]
    public List<TaskData> allTasks = new List<TaskData>();
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    /// <summary>
    /// Check all tasks and return those that meet trigger conditions
    /// </summary>
    public List<TaskData> CheckTriggeredTasks()
    {
        List<TaskData> triggeredTasks = new List<TaskData>();
        
        foreach (TaskData taskData in allTasks)
        {  

            if (AreTriggersActivated(taskData))
            {
                triggeredTasks.Add(taskData);

                if (showDebugInfo)
                    Debug.Log($"Task triggered: {taskData.taskId} - {taskData.taskTitle}");
            }
        }
        
        return triggeredTasks;
    }
    
    /// <summary>
    /// Check if all trigger conditions are met for a task
    /// </summary>
    bool AreTriggersActivated(TaskData taskData)
    {
        List<bool> triggerResults = new List<bool>();
        
        // Check all trigger types
        foreach (var trigger in taskData.roundTriggers)
            triggerResults.Add(trigger.CheckCondition());

        foreach (var trigger in taskData.dayTriggers)
            triggerResults.Add(trigger.CheckCondition());
        
        foreach (var trigger in taskData.resourceTriggers)
            triggerResults.Add(trigger.CheckCondition());
        
        foreach (var trigger in taskData.probabilityTriggers)
            triggerResults.Add(trigger.CheckCondition());

        foreach (var trigger in taskData.floodTileTriggers)
            triggerResults.Add(trigger.CheckCondition());

        foreach (var trigger in taskData.floodedFacilityTriggers)
            triggerResults.Add(trigger.CheckCondition());

        foreach (var trigger in taskData.budgetTriggers)
            triggerResults.Add(trigger.CheckCondition());

        foreach (var trigger in taskData.satisfactionTriggers)
            triggerResults.Add(trigger.CheckCondition());

        foreach (var trigger in taskData.workforceTriggers)
            triggerResults.Add(trigger.CheckCondition());

        foreach (var trigger in taskData.facilityStatusTriggers)
            triggerResults.Add(trigger.CheckCondition());

        foreach (var trigger in taskData.weatherTriggers)
            triggerResults.Add(trigger.CheckCondition());

        if (triggerResults.Count == 0) return false; // No triggers = never activate
        
        if (taskData.requireAllTriggers)
        {
            // AND logic - all triggers must be true
            return triggerResults.All(result => result);
        }
        else
        {
            // OR logic - at least one trigger must be true
            return triggerResults.Any(result => result);
        }
    }
    
    /// <summary>
    /// Get task by ID for debug panel
    /// </summary>
    public TaskData GetTaskById(string taskId)
    {
        return allTasks.FirstOrDefault(task => task.taskId == taskId);
    }
    
    /// <summary>
    /// Get all task IDs for debug panel dropdown
    /// </summary>
    public List<string> GetAllTaskIds()
    {
        return allTasks.Select(task => task.taskId).ToList();
    }
    
    /// <summary>
    /// Find suitable facility for task (single facility - backwards compatibility)
    /// </summary>
    public MonoBehaviour FindSuitableFacility(TaskData taskData)
    {
        if (taskData.isGlobalTask) return null;
        
        if (!taskData.autoSelectFacility && taskData.specificFacility != null)
            return taskData.specificFacility;
        
        // Auto-find suitable facility
        Building[] buildings = FindObjectsOfType<Building>();
        
        foreach (Building building in buildings)
        {
            if (building.GetBuildingType() == taskData.targetFacilityType && 
                building.IsOperational())
            {
                return building;
            }
        }
        
        // FIXED: Also check PrebuiltBuildings
        PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>();
        foreach (PrebuiltBuilding prebuilt in prebuilts)
        {
            if (prebuilt.GetBuildingType() == taskData.targetFacilityType)
            {
                return prebuilt;
            }
        }
        
        return null;
    }

    /// <summary>
    /// NEW: Find ALL suitable facilities for task (instead of just first one)
    /// </summary>
    public List<MonoBehaviour> FindAllSuitableFacilities(TaskData taskData)
    {
        List<MonoBehaviour> suitableFacilities = new List<MonoBehaviour>();
        
        if (taskData.isGlobalTask) return suitableFacilities;
        
        if (!taskData.autoSelectFacility && taskData.specificFacility != null)
        {
            suitableFacilities.Add(taskData.specificFacility);
            return suitableFacilities;
        }
        
        // Check Buildings
        Building[] buildings = FindObjectsOfType<Building>();
        foreach (Building building in buildings)
        {
            if (building.GetBuildingType() == taskData.targetFacilityType && 
                building.IsOperational())
            {
                suitableFacilities.Add(building);
            }
        }
        
        // Check PrebuiltBuildings 
        PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>();
        foreach (PrebuiltBuilding prebuilt in prebuilts)
        {
            if (prebuilt.GetBuildingType() == taskData.targetFacilityType)
            {
                suitableFacilities.Add(prebuilt);
            }
        }
        
        if (showDebugInfo)
            Debug.Log($"Found {suitableFacilities.Count} suitable facilities for {taskData.taskTitle}: {string.Join(", ", suitableFacilities.Select(f => f.name))}");
        
        return suitableFacilities;
    }

    /// <summary>
    /// NEW: Check triggers for each facility individually
    /// </summary>
    public List<(TaskData taskData, MonoBehaviour facility)> CheckTriggeredTasksPerFacility()
    {
        List<(TaskData, MonoBehaviour)> triggeredTasksWithFacilities = new List<(TaskData, MonoBehaviour)>();
        
        foreach (TaskData taskData in allTasks)
        {
            if (taskData == null) continue;
            
            // For global tasks, check triggers once globally
            if (taskData.isGlobalTask)
            {
                if (AreTriggersActivated(taskData))
                {
                    triggeredTasksWithFacilities.Add((taskData, null));
                    if (showDebugInfo)
                        Debug.Log($"Global task triggered: {taskData.taskId} - {taskData.taskTitle}");
                }
                continue;
            }
            
            // For facility-specific tasks, check triggers per facility
            List<MonoBehaviour> potentialFacilities = FindAllSuitableFacilities(taskData);
            
            foreach (MonoBehaviour facility in potentialFacilities)
            {
                // Check triggers specifically for this facility
                if (AreTriggersActivatedForFacility(taskData, facility))
                {
                    triggeredTasksWithFacilities.Add((taskData, facility));
                    if (showDebugInfo)
                        Debug.Log($"Task triggered for {facility.name}: {taskData.taskId} - {taskData.taskTitle}");
                }
            }
        }
        
        return triggeredTasksWithFacilities;
    }

    /// <summary>
    /// NEW: Check if triggers are activated for a specific facility
    /// </summary>
    bool AreTriggersActivatedForFacility(TaskData taskData, MonoBehaviour facility)
    {
        List<bool> triggerResults = new List<bool>();
        
        // Check all trigger types - some need facility context, others are global
        foreach (var trigger in taskData.roundTriggers)
            triggerResults.Add(trigger.CheckCondition()); // Global
            
        foreach (var trigger in taskData.dayTriggers)
            triggerResults.Add(trigger.CheckCondition()); // Global
        
        // Per-facility resource triggers
        foreach (var trigger in taskData.resourceTriggers)
            triggerResults.Add(CheckResourceTriggerForFacility(trigger, facility));
        
        // Per-facility probability triggers (each facility rolls independently)
        foreach (var trigger in taskData.probabilityTriggers)
            triggerResults.Add(trigger.CheckCondition()); // Each call is independent random roll
        
        foreach (var trigger in taskData.floodTileTriggers)
            triggerResults.Add(trigger.CheckCondition()); // Global
            
        // Per-facility flood triggers
        foreach (var trigger in taskData.floodedFacilityTriggers)
            triggerResults.Add(CheckFloodedFacilityTriggerForFacility(trigger, facility));
        
        foreach (var trigger in taskData.budgetTriggers)
            triggerResults.Add(trigger.CheckCondition()); // Global
            
        foreach (var trigger in taskData.satisfactionTriggers)
            triggerResults.Add(trigger.CheckCondition()); // Global
            
        foreach (var trigger in taskData.workforceTriggers)
            triggerResults.Add(trigger.CheckCondition()); // Global
        
        // Per-facility status triggers
        foreach (var trigger in taskData.facilityStatusTriggers)
            triggerResults.Add(CheckFacilityStatusTriggerForFacility(trigger, facility));
        
        foreach (var trigger in taskData.weatherTriggers)
            triggerResults.Add(trigger.CheckCondition()); // Global

        if (triggerResults.Count == 0) return false;
        
        if (taskData.requireAllTriggers)
        {
            return triggerResults.All(result => result);
        }
        else
        {
            return triggerResults.Any(result => result);
        }
    }

    /// <summary>
    /// Check resource trigger for specific facility
    /// </summary>
    bool CheckResourceTriggerForFacility(ResourceTrigger trigger, MonoBehaviour facility)
    {
        // Check if this specific facility matches the trigger's facility type
        BuildingType facilityType = BuildingType.Community; // Default
        
        if (facility is Building building)
        {
            facilityType = building.GetBuildingType();
        }
        else if (facility is PrebuiltBuilding prebuilt)
        {
            facilityType = prebuilt.GetBuildingType();
        }
        
        // Only check this facility if it matches the trigger's target type
        if (facilityType != trigger.facilityType)
            return false;
        
        // Check the resource condition for this specific facility
        BuildingResourceStorage storage = facility.GetComponent<BuildingResourceStorage>();
        if (storage == null) return false;
        
        int currentResource = storage.GetResourceAmount(trigger.resourceType);
        int capacity = storage.GetResourceCapacity(trigger.resourceType);
        
        switch (trigger.condition)
        {
            case ResourceTrigger.ResourceCondition.Empty:
                return currentResource == 0;
            case ResourceTrigger.ResourceCondition.Full:
                return currentResource >= capacity;
            case ResourceTrigger.ResourceCondition.LessThan:
                return currentResource < trigger.resourceThreshold;
            case ResourceTrigger.ResourceCondition.MoreThan:
                return currentResource > trigger.resourceThreshold;
            default:
                return false;
        }
    }

    /// <summary>
    /// Check flooded facility trigger for specific facility
    /// </summary>
    bool CheckFloodedFacilityTriggerForFacility(FloodedFacilityTrigger trigger, MonoBehaviour facility)
    {
        if (FloodSystem.Instance == null) return false;
        
        // Check if this facility matches the trigger's facility type criteria
        bool facilityMatches = false;
        
        switch (trigger.facilityType)
        {
            case FloodedFacilityTrigger.FacilityFloodType.AnyFacility:
                facilityMatches = true;
                break;
                
            case FloodedFacilityTrigger.FacilityFloodType.AnyBuilding:
                facilityMatches = facility is Building;
                break;
                
            case FloodedFacilityTrigger.FacilityFloodType.AnyPrebuilt:
                facilityMatches = facility is PrebuiltBuilding;
                break;
                
            case FloodedFacilityTrigger.FacilityFloodType.SpecificBuildingType:
                if (facility is Building building)
                    facilityMatches = building.GetBuildingType() == trigger.specificBuildingType;
                break;
                
            case FloodedFacilityTrigger.FacilityFloodType.SpecificPrebuiltType:
                if (facility is PrebuiltBuilding prebuilt)
                    facilityMatches = prebuilt.GetPrebuiltType() == trigger.specificPrebuiltType;
                break;
        }
        
        if (!facilityMatches) return false;
        
        // Count flood tiles near this specific facility
        Vector3 facilityWorldPos = facility.transform.position;
        int floodCount = 0;
        
        for (int x = -trigger.detectionRadius; x <= trigger.detectionRadius; x++)
        {
            for (int y = -trigger.detectionRadius; y <= trigger.detectionRadius; y++)
            {
                Vector3 checkPos = facilityWorldPos + new Vector3(x, y, 0);
                if (FloodSystem.Instance.IsFloodedAt(checkPos))
                {
                    floodCount++;
                }
            }
        }
        
        // Check if this facility's flood count meets the trigger condition
        switch (trigger.comparison)
        {
            case FloodedFacilityTrigger.ComparisonType.ExactMatch:
                return floodCount == trigger.floodTileThreshold;
            case FloodedFacilityTrigger.ComparisonType.AtLeast:
                return floodCount >= trigger.floodTileThreshold;
            case FloodedFacilityTrigger.ComparisonType.MoreThan:
                return floodCount > trigger.floodTileThreshold;
            case FloodedFacilityTrigger.ComparisonType.LessThan:
                return floodCount < trigger.floodTileThreshold;
            case FloodedFacilityTrigger.ComparisonType.AtMost:
                return floodCount <= trigger.floodTileThreshold;
            default:
                return false;
        }
    }

    /// <summary>
    /// Check facility status trigger for specific facility
    /// </summary>
    bool CheckFacilityStatusTriggerForFacility(FacilityStatusTrigger trigger, MonoBehaviour facility)
    {
        if (facility is Building building)
        {
            // Check if this building matches the trigger's facility type (if specific)
            if (trigger.specificFacilityOnly && building.GetBuildingType() != trigger.facilityType)
                return false;
            
            // Check the status condition for this specific building
            switch (trigger.requiredStatus)
            {
                case FacilityStatusTrigger.FacilityStatus.UnderConstruction:
                    return building.IsUnderConstruction();
                case FacilityStatusTrigger.FacilityStatus.NeedWorker:
                    return building.NeedsWorker();
                case FacilityStatusTrigger.FacilityStatus.InUse:
                    return building.IsOperational();
                case FacilityStatusTrigger.FacilityStatus.Disabled:
                    return building.IsDisabled();
                default:
                    return false;
            }
        }
        
        // PrebuiltBuildings don't have the same status system, so return false
        return false;
    }
}