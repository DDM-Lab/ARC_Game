using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

[System.Serializable]
public class ClientGroup
{
    public int groupId;
    public string groupName;
    public int clientCount;
    public MonoBehaviour currentShelter;
    public int arrivalRound;
    public float arrivalTime;
    public bool caseworkRequestGenerated = false;
    public bool isOverstaying = false;
    public int overstayRounds = 0;
    
    public ClientGroup(int id, string name, int count, MonoBehaviour shelter, int round)
    {
        groupId = id;
        groupName = name;
        clientCount = count;
        currentShelter = shelter;
        arrivalRound = round;
        arrivalTime = Time.time;
    }
    
    public int GetRoundsInShelter(int currentRound)
    {
        return currentRound - arrivalRound;
    }
    
    public bool ShouldRequestCasework(int currentRound)
    {
        return GetRoundsInShelter(currentRound) >= 8 && !caseworkRequestGenerated;
    }
    
    public bool IsOverstaying(int currentRound)
    {
        return GetRoundsInShelter(currentRound) > 8;
    }
}

[System.Serializable]
public class OverstayRecord
{
    public string clientGroupName;
    public string shelterName;
    public int roundsOverstayed;
    public int clientCount;
    public float recordedTime;
    
    public OverstayRecord(ClientGroup group, int currentRound)
    {
        clientGroupName = group.groupName;
        shelterName = group.currentShelter?.name ?? "Unknown Shelter";
        roundsOverstayed = group.GetRoundsInShelter(currentRound) - 8; // Rounds beyond 10
        clientCount = group.clientCount;
        recordedTime = Time.time;
    }
}

public class ClientStayTracker : MonoBehaviour
{
    [Header("Client Tracking")]
    public List<ClientGroup> clientGroups = new List<ClientGroup>();
    public List<OverstayRecord> overstayRecords = new List<OverstayRecord>();
    
    [Header("Settings")]
    public int caseworkRequestThreshold = 8; // Rounds
    public int overstayThreshold = 8; // Rounds
    
    [Header("Task Generation")]
    public bool enableCaseworkTaskGeneration = true;
    public string caseworkTaskTitle = "Casework Request";
    public string caseworkTaskDescription = "Clients have been in shelter for {0} rounds and are requesting casework assistance.";
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Singleton
    public static ClientStayTracker Instance { get; private set; }
    
    // Events
    public event Action<ClientGroup> OnCaseworkRequested;
    public event Action<ClientGroup> OnClientOverstay;
    public event Action<OverstayRecord> OnOverstayRecorded;
    
    private int nextGroupId = 1;
    private int currentRound = 0;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        // Subscribe to round changes
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;
        }
        
        if (showDebugInfo)
            Debug.Log("ClientStayTracker initialized");
    }

    void OnRoundChanged(int newRound)
    {
        currentRound = GlobalClock.Instance.GetCurrentTimeSegment() + (GlobalClock.Instance.GetCurrentDay() - 1) * 4;
        CheckClientStayDurations();
        
        if (showDebugInfo)
            Debug.Log($"Round {currentRound}: Checking {clientGroups.Count} client groups");
    }

    /// <summary>
    /// Register clients arriving at a shelter
    /// </summary>
    public ClientGroup RegisterClientArrival(MonoBehaviour shelter, int clientCount, string customName = null)
    {
        string groupName = customName ?? $"Group_{nextGroupId}";
        ClientGroup newGroup = new ClientGroup(nextGroupId++, groupName, clientCount, shelter, currentRound);
        
        clientGroups.Add(newGroup);
        
        if (showDebugInfo)
            Debug.Log($"Registered {clientCount} clients at {shelter.name} (Group: {groupName}, Round: {currentRound})");
        
        return newGroup;
    }

    /// <summary>
    /// Remove clients from shelter (e.g., when they leave for casework or permanent housing)
    /// </summary>
    public bool RemoveClientGroup(int groupId)
    {
        ClientGroup group = clientGroups.FirstOrDefault(g => g.groupId == groupId);
        if (group != null)
        {
            clientGroups.Remove(group);
            
            if (showDebugInfo)
                Debug.Log($"Removed client group {group.groupName} from {group.currentShelter?.name}");
            
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Remove clients by shelter and quantity (for casework departures)
    /// </summary>
    public int RemoveClientsByQuantity(MonoBehaviour shelter, int quantity)
    {
        List<ClientGroup> shelterGroups = GetClientsInShelter(shelter);
        int remainingToRemove = quantity;
        int totalRemoved = 0;
        
        foreach (ClientGroup group in shelterGroups.ToList())
        {
            if (remainingToRemove <= 0) break;
            
            if (group.clientCount <= remainingToRemove)
            {
                // Remove entire group
                remainingToRemove -= group.clientCount;
                totalRemoved += group.clientCount;
                clientGroups.Remove(group);
                
                if (showDebugInfo)
                    Debug.Log($"Removed entire group {group.groupName} ({group.clientCount} clients) for casework");
            }
            else
            {
                // Partial removal from group
                group.clientCount -= remainingToRemove;
                totalRemoved += remainingToRemove;
                
                if (showDebugInfo)
                    Debug.Log($"Partially removed {remainingToRemove} clients from group {group.groupName}");
                
                remainingToRemove = 0;
            }
        }
        
        return totalRemoved;
    }


    /// <summary>
    /// Move client group to different shelter
    /// </summary>
    public bool MoveClientGroup(int groupId, MonoBehaviour newShelter)
    {
        ClientGroup group = clientGroups.FirstOrDefault(g => g.groupId == groupId);
        if (group != null)
        {
            MonoBehaviour oldShelter = group.currentShelter;
            group.currentShelter = newShelter;
            // Note: We don't reset arrival time - they carry their stay duration

            if (showDebugInfo)
                Debug.Log($"Moved {group.groupName} from {oldShelter?.name} to {newShelter.name}");

            return true;
        }

        return false;
    }

    /// <summary>
    /// Check all client groups for casework requests and overstays
    /// </summary>
    void CheckClientStayDurations()
    {
        List<ClientGroup> groupsToRemove = new List<ClientGroup>();
        
        foreach (ClientGroup group in clientGroups.ToList()) // ToList to avoid modification during iteration
        {
            // Check for casework request
            if (group.ShouldRequestCasework(currentRound) && enableCaseworkTaskGeneration)
            {
                GenerateCaseworkTask(group);
                group.caseworkRequestGenerated = true;
                OnCaseworkRequested?.Invoke(group);
            }
            
            // Check for overstay
            if (group.IsOverstaying(currentRound) && !group.isOverstaying)
            {
                group.isOverstaying = true;
                group.overstayRounds = group.GetRoundsInShelter(currentRound) - overstayThreshold;
                
                // Record overstay
                OverstayRecord record = new OverstayRecord(group, currentRound);
                overstayRecords.Add(record);
                
                OnClientOverstay?.Invoke(group);
                OnOverstayRecorded?.Invoke(record);
                
                if (showDebugInfo)
                    Debug.Log($"OVERSTAY: {group.groupName} at {group.currentShelter?.name} - {group.GetRoundsInShelter(currentRound)} rounds");
            }
            
            // Update overstay rounds for already overstaying groups
            if (group.isOverstaying)
            {
                group.overstayRounds = group.GetRoundsInShelter(currentRound) - overstayThreshold;
            }
        }
    }

    /// <summary>
    /// Generate casework request task
    /// </summary>
    void GenerateCaseworkTask(ClientGroup group)
    {
        if (TaskSystem.Instance == null) return;
        
        string description = string.Format(caseworkTaskDescription, group.GetRoundsInShelter(currentRound));
        string facilityName = group.currentShelter?.name ?? "Unknown Shelter";
        
        GameTask caseworkTask = TaskSystem.Instance.CreateTask(
            caseworkTaskTitle, 
            TaskType.Advisory, 
            facilityName, 
            description);
        
        // Add task details
        caseworkTask.impacts.Add(new TaskImpact(ImpactType.Clients, group.clientCount, false, "Clients Requesting Casework"));
        caseworkTask.impacts.Add(new TaskImpact(ImpactType.TotalTime, group.GetRoundsInShelter(currentRound), false, "Rounds in Shelter"));
        
        // Add agent messages
        caseworkTask.agentMessages.Add(new AgentMessage($"We have {group.clientCount} clients who have been in {facilityName} for {group.GetRoundsInShelter(currentRound)} rounds."));
        caseworkTask.agentMessages.Add(new AgentMessage("They are requesting casework assistance to find permanent housing."));
        caseworkTask.agentMessages.Add(new AgentMessage("How would you like to respond?"));
        
        // Add choices
        AgentChoice sendToCasework = new AgentChoice(1, $"Send {group.groupName} to casework site");
        sendToCasework.triggersDelivery = true;
        sendToCasework.enableMultipleDeliveries = true;
        sendToCasework.multiDeliveryType = AgentChoice.MultiDeliveryType.SingleSourceMultiDest; // enable multi delivery
        sendToCasework.deliveryCargoType = ResourceType.Population;
        sendToCasework.deliveryQuantity = group.clientCount;
        sendToCasework.sourceType = DeliverySourceType.RequestingFacility;
        sendToCasework.destinationType = DeliveryDestinationType.SpecificBuilding;
        sendToCasework.destinationBuilding = BuildingType.CaseworkSite; // Assuming you have this building type
        sendToCasework.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 10));
        caseworkTask.agentChoices.Add(sendToCasework);
        
        AgentChoice delay = new AgentChoice(2, "Ask them to wait longer");
        delay.triggersDelivery = false;
        delay.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -5));
        caseworkTask.agentChoices.Add(delay);
        
        // Store reference to client group in task description for removal after completion
        caseworkTask.description += $"|CLIENT_GROUP_ID:{group.groupId}";
        
        if (showDebugInfo)
            Debug.Log($"Generated casework task for {group.groupName} at {facilityName}");
    }

    /// <summary>
    /// Get all client groups currently in a specific shelter
    /// </summary>
    public List<ClientGroup> GetClientsInShelter(MonoBehaviour shelter)
    {
        return clientGroups.Where(g => g.currentShelter == shelter).ToList();
    }

    /// <summary>
    /// Get overstay statistics for reporting
    /// </summary>
    public Dictionary<string, object> GetOverstayStatistics()
    {
        var stats = new Dictionary<string, object>();
        
        stats["TotalOverstayRecords"] = overstayRecords.Count;
        stats["TotalOverstayingClients"] = overstayRecords.Sum(r => r.clientCount);
        stats["CurrentOverstayingGroups"] = clientGroups.Count(g => g.isOverstaying);
        stats["AverageOverstayRounds"] = overstayRecords.Count > 0 ? overstayRecords.Average(r => r.roundsOverstayed) : 0;
        
        // Group by shelter
        var shelterOverstays = overstayRecords.GroupBy(r => r.shelterName)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.clientCount));
        stats["OverstaysByShelter"] = shelterOverstays;
        
        return stats;
    }

    /// <summary>
    /// Clear overstay records (e.g., at end of day)
    /// </summary>
    public void ClearOverstayRecords()
    {
        overstayRecords.Clear();
        
        if (showDebugInfo)
            Debug.Log("Cleared overstay records");
    }

    /// <summary>
    /// Get current round for external access
    /// </summary>
    public int GetCurrentRound()
    {
        return currentRound;
    }

    // Debug methods
    [ContextMenu("Show Client Status")]
    public void ShowClientStatus()
    {
        Debug.Log("=== CLIENT STATUS ===");
        Debug.Log($"Current Round: {currentRound}");
        Debug.Log($"Active Client Groups: {clientGroups.Count}");
        
        foreach (ClientGroup group in clientGroups)
        {
            int roundsInShelter = group.GetRoundsInShelter(currentRound);
            string status = group.isOverstaying ? "OVERSTAYING" : 
                           group.caseworkRequestGenerated ? "CASEWORK REQUESTED" :
                           roundsInShelter >= 8 ? "READY FOR CASEWORK" : "NORMAL";
            
            Debug.Log($"{group.groupName}: {group.clientCount} clients at {group.currentShelter?.name} " +
                     $"({roundsInShelter} rounds) - {status}");
        }
        
        Debug.Log($"Total Overstay Records: {overstayRecords.Count}");
    }

    [ContextMenu("Test Add Clients to Shelter")]
    public void TestAddClients()
    {
        Building[] shelters = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        if (shelters.Length > 0)
        {
            RegisterClientArrival(shelters[0], 3, "Test Family");
            Debug.Log($"Added test clients to {shelters[0].name}");
        }
    }
}