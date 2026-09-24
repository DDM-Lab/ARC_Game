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
            if (!IsWithinGenerationRoundLimit(taskData)) continue;

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
    /// Hard cutoff on when a task may be generated, independent of (and checked before) the
    /// AND/OR trigger combination below — so it applies unconditionally, including skipping the
    /// probability roll entirely once past the limit. taskData.latestGenerationRound uses the
    /// same 1-indexed "Round N" numbering shown to the player; 0 means no limit.
    /// </summary>
    bool IsWithinGenerationRoundLimit(TaskData taskData)
    {
        if (taskData.latestGenerationRound <= 0) return true;
        if (GlobalClock.Instance == null) return true;

        int currentRoundInDay = GlobalClock.Instance.GetCurrentTimeSegment() + 1;
        return currentRoundInDay <= taskData.latestGenerationRound;
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
            triggerResults.Add(CheckProbability(taskData, trigger));

        foreach (var trigger in taskData.floodTileTriggers)
            triggerResults.Add(trigger.CheckCondition());

        foreach (var trigger in taskData.floodedFacilityTriggers)
            triggerResults.Add(Configured(taskData, trigger).CheckCondition());

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
            if (!IsWithinGenerationRoundLimit(taskData)) continue;

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
    static readonly bool TriggerTrace =
        System.Environment.GetEnvironmentVariable("ARC_TRIGGER_TRACE") == "1";

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
        {
            bool rres = CheckResourceTriggerForFacility(trigger, facility);
            if (TriggerTrace && taskData.taskId == "Community_TransportRequest")
            {
                var stor = (facility as PrebuiltBuilding)?.GetResourceStorage();
                Debug.Log($"[D19res] {facility?.name} type={trigger.resourceType} cond={trigger.condition} "
                        + $"thr={trigger.resourceThreshold} -> {rres}  "
                        + $"amount={(stor != null ? stor.GetResourceAmount(trigger.resourceType).ToString() : "no-storage")} "
                        + $"cap={(stor != null ? stor.GetResourceCapacity(trigger.resourceType).ToString() : "-")}");
            }
            triggerResults.Add(rres);
        }
        
        // Per-facility probability triggers (each facility rolls independently)
        foreach (var trigger in taskData.probabilityTriggers)
        {
            // TEMPORARY DIAGNOSTIC (ledger D19). Prints the roll and the threshold for one task
            // so the per-facility outcome can be compared against upstream's log instead of
            // inferred from a matching RNG cursor. Opt-in; remove once D19 is closed.
            if (TriggerTrace && taskData.taskId == "Community_TransportRequest")
            {
                var st = UnityEngine.Random.state;
                bool hit = CheckProbability(taskData, trigger);
                Debug.Log($"[D19] {taskData.taskId} @ {facility?.name} prob={trigger.probability} "
                        + $"-> {hit}  rngBefore={JsonUtility.ToJson(st)}");
                triggerResults.Add(hit);
                continue;
            }
            triggerResults.Add(CheckProbability(taskData, trigger)); // Each call is independent random roll
        }
        
        foreach (var trigger in taskData.floodTileTriggers)
            triggerResults.Add(trigger.CheckCondition()); // Global
            
        // Per-facility flood triggers
        foreach (var trigger in taskData.floodedFacilityTriggers)
            triggerResults.Add(CheckFloodedFacilityTriggerForFacility(Configured(taskData, trigger), facility));
        
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

        if (TriggerTrace && taskData.taskId == "Community_TransportRequest")
            Debug.Log($"[D19] {taskData.taskId} @ {facility?.name} results=[{string.Join(",", triggerResults)}] requireAll={taskData.requireAllTriggers}");
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

    // ── Sheet parameters applied at evaluation time (BUG_REPORTS B35); the ScriptableObjects are never mutated ──

    /// <summary>initialFoodDemandFrequency replaces the asset probability of a food-request task that has one
    /// (Shelter_FoodRequest today; Community_FoodRequest has no probability trigger and is unaffected).</summary>
    bool CheckProbability(TaskData taskData, ProbabilityTrigger trigger)
    {
        return trigger.CheckCondition();
    }

    /// <summary>initialShelterFloodDamage{Comparison,FloodTileThreshold,FloodDetectionRange} -> the
    /// Shelter Flood Damage trigger.</summary>
    FloodedFacilityTrigger Configured(TaskData taskData, FloodedFacilityTrigger trigger)
    {
        var gdm = GameDataManager.Instance;
        if (gdm == null || !gdm.IsDataReady || taskData.taskId != "Shelter_Flood_Damage") return trigger;
        return new FloodedFacilityTrigger
        {
            facilityType = trigger.facilityType,
            specificBuildingType = trigger.specificBuildingType,
            specificPrebuiltType = trigger.specificPrebuiltType,
            comparison = gdm.InitialShelterFloodComparison,
            floodTileThreshold = gdm.InitialShelterFloodThreshold,
            detectionRadius = gdm.InitialShelterFloodRadius
        };
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
            case ResourceTrigger.ResourceCondition.NeedsFood:
                return storage.GetFoodNeed() > 0;
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