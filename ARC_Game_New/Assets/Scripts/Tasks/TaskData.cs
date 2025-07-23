using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[CreateAssetMenu(fileName = "New Task", menuName = "Task System/Task Data")]
public class TaskData : ScriptableObject
{
    [Header("Basic Info")]
    public string taskTitle;
    public TaskType taskType;
    public string affectedFacility;
    [TextArea(3, 5)]
    public string description;
    public Sprite taskImage;
    
    [Header("Timing")]
    public int roundsRemaining = 1;
    public float realTimeRemaining = 300f;
    public bool hasRealTimeLimit = true;
    
    [Header("Impacts")]
    public List<TaskImpact> impacts = new List<TaskImpact>();
    
    [Header("Agent Conversation")]
    public List<AgentMessage> agentMessages = new List<AgentMessage>();
    public List<AgentChoice> agentChoices = new List<AgentChoice>();
    public List<AgentNumericalInput> numericalInputs = new List<AgentNumericalInput>();
}