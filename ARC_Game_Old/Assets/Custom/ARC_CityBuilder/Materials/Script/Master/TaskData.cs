using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data structure for task information
/// </summary>
[System.Serializable]
public class TaskData
{
    public string taskId;
    public string title;
    public string description;
    public Sprite taskIcon;
    public TaskStatus status = TaskStatus.Todo;
    public string targetLocation; // Optional: reference to a location/shelter
    public int requiredAmount; // For tasks like "10 packs of food"
    
    // Additional task-specific data
    public Dictionary<string, object> customData = new Dictionary<string, object>();
    
    // Constructor
    public TaskData()
    {
        customData = new Dictionary<string, object>();
    }
    
    public TaskData(string id, string taskTitle, string taskDescription)
    {
        taskId = id;
        title = taskTitle;
        description = taskDescription;
        customData = new Dictionary<string, object>();
    }
}