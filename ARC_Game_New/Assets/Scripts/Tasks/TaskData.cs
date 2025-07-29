using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[CreateAssetMenu(fileName = "New Task", menuName = "Task System/Task Data")]
public class TaskData : ScriptableObject
{
    [Header("Basic Info")]
    public string taskId; // Unique identifier for debug panel
    public string taskTitle;
    public TaskType taskType;

    [TextArea(3, 5)]
    public string description;
    public Sprite taskImage;

    [Header("Task Triggers")]
    public List<TaskTrigger> allTriggers = new List<TaskTrigger>();
    public List<RoundTrigger> roundTriggers = new List<RoundTrigger>();
    public List<PopulationTrigger> populationTriggers = new List<PopulationTrigger>();
    public List<ResourceTrigger> resourceTriggers = new List<ResourceTrigger>();
    public List<ProbabilityTrigger> probabilityTriggers = new List<ProbabilityTrigger>();
    public bool requireAllTriggers = true; // AND vs OR logic

    [Header("Facility Targeting")]
    public bool isGlobalTask = false; // Weather advisory, general announcements
    public BuildingType targetFacilityType = BuildingType.Shelter; // Which facility type to associate with
    public bool autoSelectFacility = true; // Auto-find suitable facility or use specific one
    public MonoBehaviour specificFacility; // Manual facility assignment (optional). Only used if autoSelectFacility = false

    [Header("Timing")]
    public int roundsRemaining = 1;
    public float realTimeRemaining = 300f;
    public bool hasRealTimeLimit = false;

    [Header("Impacts")]
    public List<TaskImpact> impacts = new List<TaskImpact>();

    [Header("Agent Conversation")]
    public List<AgentMessage> agentMessages = new List<AgentMessage>();
    public List<AgentChoice> agentChoices = new List<AgentChoice>();
    public List<AgentNumericalInput> numericalInputs = new List<AgentNumericalInput>();

    [Header("General Delivery Settings")]
    public float deliveryFailureSatisfactionPenalty = 10f;
    public float deliveryTimeLimit = 300f;


    // delivery source/destination settings moved to individual agent choices


}