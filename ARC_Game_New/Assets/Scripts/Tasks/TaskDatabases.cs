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
    /// Find suitable facility for task
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
        
        return null;
    }
}