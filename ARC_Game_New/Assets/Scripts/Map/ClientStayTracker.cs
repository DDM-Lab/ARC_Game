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
    public int clientsWithCaseworkNeed; // Clients need casework
    public int clientsWithoutCaseworkNeed; // Clients leave via timer
    //public MonoBehaviour currentShelter;
    public MonoBehaviour currentFacility; // Can be motel now
    public int arrivalRound;
    public float arrivalTime;
    public bool caseworkRequestGenerated = false;
    public bool isOverstaying = false;
    public int overstayRounds = 0;

    // Caseworkless departure params
    public int assignedDepartureRound;
    public bool hasDeparted = false;

    public ClientGroup(int id, string name, int count, MonoBehaviour facility, int round, float caseworkNeedProbabilityN, int minStayRounds, int maxStayRounds)
    {
        groupId = id;
        groupName = name;
        clientCount = count;
        //currentShelter = shelter;
        currentFacility = facility;
        arrivalRound = round;
        arrivalTime = Time.time;

        clientsWithCaseworkNeed = 0;
        for (int i = 0; i < count; i++)
        {
            if (UnityEngine.Random.value < (caseworkNeedProbabilityN / 100f))
            {
                clientsWithCaseworkNeed++;
            }
        }
        clientsWithoutCaseworkNeed = clientCount - clientsWithCaseworkNeed;

        int stayDurationRounds = UnityEngine.Random.Range(minStayRounds, maxStayRounds + 1);
        assignedDepartureRound = arrivalRound + stayDurationRounds;
    }
    
    public int GetRoundsInFacility(int currentRound)
    {
        return currentRound - arrivalRound;
    } 
}

[System.Serializable]
public class OverstayRecord
{
    public string clientGroupName;
    public string facilityName;
    public int roundsOverstayed;
    public int clientCount;
    public float recordedTime;
    
    public OverstayRecord(ClientGroup group, int currentRound, int threshold)
    {
        clientGroupName = group.groupName;
        facilityName = group.currentFacility?.name ?? "Unknown Facility";
        roundsOverstayed = group.GetRoundsInFacility(currentRound) - threshold; 
        clientCount = group.clientsWithCaseworkNeed;
        recordedTime = Time.time;
    }
}

public class ClientStayTracker : MonoBehaviour
{

    [Header("Casework Config Params")]
    [Tooltip("N%")]
    [Range(0f, 100f)]
    public float caseworkNeedProbability = 40f;

    [Tooltip("Base X% for casework generation at round Y=1")]
    [Range(0f, 100f)]
    public float baseCaseworkProbability = 10f;

    [Tooltip("Exponential growth factor G: P(Y) = X_0 * (G ^ (Y - 1))")]
    public float probabilityGrowthFactor = 1.5f;

    [Header("2. Caseworkless Departure Config Params")]
    [Tooltip("Minimum rounds stayed before caseworkless clients leave")]
    public int minStayRounds = 4;
    [Tooltip("Maximum rounds stayed before caseworkless clients leave")]
    public int maxStayRounds = 8;

    [Header("Client Tracking")]
    public List<ClientGroup> clientGroups = new List<ClientGroup>();
    public List<OverstayRecord> overstayRecords = new List<OverstayRecord>();
    
    [Header("Settings")]
    public int overstayThreshold = 8; // Rounds
    
    [Header("Task Generation")]
    public bool enableCaseworkTaskGeneration = true;
    public string caseworkTaskTitle = "Casework Request";
    public string caseworkTaskDescription = "Clients at {0} have been staying for {1} rounds and require casework assistance.";

    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Singleton
    public static ClientStayTracker Instance { get; private set; }
    
    // Events
    public event Action<ClientGroup> OnCaseworkRequested;
    public event Action<ClientGroup> OnClientOverstay;
    public event Action<OverstayRecord> OnOverstayRecorded;
    public event Action<ClientGroup> OnCaseworklessClientsDeparted;

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
        if (GlobalClock.Instance != null)
            GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;

        StartCoroutine(SubscribeToTaskEvents());

        if (showDebugInfo)
            Debug.Log("ClientStayTracker initialized");
    }

    System.Collections.IEnumerator SubscribeToTaskEvents()
    {
        while (TaskSystem.Instance == null)
            yield return null;

        TaskSystem.Instance.OnTaskExpired += OnCaseworkTaskFinished;
        TaskSystem.Instance.OnTaskCompleted += OnCaseworkTaskFinished;
    }

    void OnDestroy()
    {
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskExpired -= OnCaseworkTaskFinished;
            TaskSystem.Instance.OnTaskCompleted -= OnCaseworkTaskFinished;
        }
    }

    void OnCaseworkTaskFinished(GameTask task)
    {
        if (task.taskTag != TaskTag.BackToHome) return;

        string desc = task.description ?? "";
        const string marker = "|CLIENT_GROUP_ID:";
        int idx = desc.IndexOf(marker);
        if (idx < 0) return;

        if (!int.TryParse(desc.Substring(idx + marker.Length), out int groupId)) return;

        ClientGroup group = clientGroups.FirstOrDefault(g => g.groupId == groupId);
        if (group != null)
        {
            group.caseworkRequestGenerated = false;
            if (showDebugInfo)
                Debug.Log($"[ClientStayTracker] Casework request re-enabled for group {groupId} — task ended without resolution");
        }
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
    /// 

    public ClientGroup RegisterClientArrival(MonoBehaviour facility, int clientCount, string customName = null)
    {
        // assertion: only want shelters and motels! 
        Building building = facility.GetComponent<Building>();
        if (building != null)
        {
            var type = building.GetBuildingType();
            if (type != BuildingType.Shelter)
            {
                if (showDebugInfo)
                    Debug.Log($"[ClientStayTracker] Ignoring arrival at {facility.name} ({type}) — only Shelters/Motels may generate casework.");
                return null;
            }
        }
        PrebuiltBuilding prebuilt = facility.GetComponent<PrebuiltBuilding>();
        if (prebuilt != null && prebuilt.GetPrebuiltType() != PrebuiltBuildingType.Motel)
        {
            if (showDebugInfo)
                Debug.Log($"[ClientStayTracker] Ignoring arrival at {facility.name} — only Shelters/Motels may generate casework.");
            return null;
        }

        string groupName = customName ?? $"Group_{nextGroupId}";
        ClientGroup newGroup = new ClientGroup(
            nextGroupId++, 
            groupName, 
            clientCount, 
            facility, 
            currentRound,
            caseworkNeedProbability,
            minStayRounds,
            maxStayRounds);
        
        clientGroups.Add(newGroup);
        
        if (showDebugInfo)
            Debug.Log($"Registered {clientCount} clients at {facility.name} (Group: {groupName}, Round: {currentRound})");

        GameLogPanel.Instance.LogBuildingStatus($"Registered {clientCount} clients at {facility.name} (Group: {groupName}, Round: {currentRound})");
        DailyReportData.Instance?.RecordNewArrival(clientCount);

        return newGroup;
    }

    /// <summary>
    /// Evaluates casework generation and natural departures for all active groups
    /// </summary>
    void CheckClientStayDurations()
    {
        List<ClientGroup> groupsToRemove = new List<ClientGroup>();

        foreach (ClientGroup group in clientGroups.ToList())
        {
            int roundsInFacility = group.GetRoundsInFacility(currentRound);

            // Caseworkless Clients
            if (!group.hasDeparted && group.clientsWithoutCaseworkNeed>0 && currentRound>=group.assignedDepartureRound)
            {
                group.hasDeparted = true;
                TriggerNonCaseworkDeparture(group);
            }

            // Casework Clients
            if (group.clientsWithCaseworkNeed > 0 && !group.caseworkRequestGenerated && enableCaseworkTaskGeneration)
            {
                int Y = Mathf.Max(1, roundsInFacility); // rounds stayed 

                // X% = X_0 * (G ^ (Y - 1))
                float currentProbability = baseCaseworkProbability*Mathf.Pow(probabilityGrowthFactor, Y-1);
                currentProbability = Mathf.Clamp(currentProbability, 0f, 100f);
                if (UnityEngine.Random.value < (currentProbability/100f))
                {
                    GenerateCaseworkTask(group);
                    group.caseworkRequestGenerated = true;
                    OnCaseworkRequested?.Invoke(group);
                }
            }

            // Casework Clients Overstaying
            if (roundsInFacility > overstayThreshold && !group.isOverstaying && group.clientsWithCaseworkNeed>0)
            {
                group.isOverstaying = true;
                group.overstayRounds = roundsInFacility - overstayThreshold;
                OverstayRecord record = new OverstayRecord(group, currentRound, overstayThreshold);
                overstayRecords.Add(record);
                OnClientOverstay?.Invoke(group);
                OnOverstayRecorded?.Invoke(record);
            }

            if (group.clientCount <= 0 || (group.hasDeparted && group.clientsWithCaseworkNeed == 0))
            {
                groupsToRemove.Add(group);
            }
        }

        foreach (var group in groupsToRemove)
        {
            clientGroups.Remove(group);
        }
    }

    void TriggerNonCaseworkDeparture(ClientGroup group)
    {
        if (showDebugInfo)
            Debug.Log($"[ClientStayTracker] Group {group.groupName}: {group.clientsWithoutCaseworkNeed} clients without casework departed at round {currentRound}.");

        // Notify buildings to get rid of clients
        OnCaseworklessClientsDeparted?.Invoke(group);

        DailyReportData.Instance?.RecordDeparture(group.clientsWithoutCaseworkNeed);

        group.clientCount -= group.clientsWithoutCaseworkNeed;
        group.clientsWithoutCaseworkNeed = 0;
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
                Debug.Log($"Removed client group {group.groupName} from {group.currentFacility?.name}");

            GameLogPanel.Instance.LogBuildingStatus($"Removed client group {group.groupName} from {group.currentFacility?.name}");

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
                GameLogPanel.Instance.LogBuildingStatus($"Removed entire group {group.groupName} ({group.clientCount} clients) for casework");
            }
            else
            {
                // Partial removal from group
                group.clientCount -= remainingToRemove;

                int deductCasework = Mathf.Min(group.clientsWithCaseworkNeed, remainingToRemove);
                group.clientsWithCaseworkNeed -= deductCasework;

                int residual = remainingToRemove - deductCasework;
                group.clientsWithoutCaseworkNeed = Mathf.Max(0, group.clientsWithoutCaseworkNeed - residual);

                totalRemoved += remainingToRemove;

                if (showDebugInfo)
                    Debug.Log($"Partially removed {remainingToRemove} clients from group {group.groupName}");
                GameLogPanel.Instance.LogBuildingStatus($"Partially removed {remainingToRemove} clients from group {group.groupName}");
                remainingToRemove = 0;
            }
        }
        
        return totalRemoved;
    }


    /// <summary>
    /// Generate casework request task
    /// </summary>
    void GenerateCaseworkTask(ClientGroup group)
    {
        if (TaskSystem.Instance == null) return;

        string facilityDisplayName = GetFacilityDisplayName(group.currentFacility);
        string facilityName = group.currentFacility?.name ?? "Unknown Facility";
        int roundsInFacility = group.GetRoundsInFacility(currentRound);
        string description = string.Format(caseworkTaskDescription, facilityDisplayName, roundsInFacility);

        GameTask caseworkTask = TaskSystem.Instance.CreateTask(
            caseworkTaskTitle,
            TaskType.Advisory,
            facilityName,
            description);

        caseworkTask.facilityDisplayName = facilityDisplayName;
        caseworkTask.taskOfficer = TaskOfficer.LodgingMassCare;
        caseworkTask.taskTag = TaskTag.BackToHome;
        caseworkTask.roundsRemaining = 3;

        // only for clients w/ casework needs
        int caseworkClientCount = group.clientsWithCaseworkNeed;

        caseworkTask.impacts.Add(new TaskImpact(ImpactType.Clients, caseworkClientCount, false, "Clients Requesting Casework"));
        caseworkTask.impacts.Add(new TaskImpact(ImpactType.TotalTime, roundsInFacility, false, "Rounds in Facility"));

        caseworkTask.agentMessages.Add(new AgentMessage(
            $"{caseworkClientCount} clients at [facility_name] require casework assistance after {roundsInFacility} rounds."));
        caseworkTask.agentMessages.Add(new AgentMessage("How would you like to respond?"));

        AgentChoice sendToCasework = new AgentChoice(1,
            $"Send {caseworkClientCount} clients to a casework site (+10 satisfaction)");
        sendToCasework.triggersDelivery = true;
        sendToCasework.enableMultipleDeliveries = true;
        sendToCasework.multiDeliveryType = AgentChoice.MultiDeliveryType.SingleSourceMultiDest;
        sendToCasework.deliveryCargoType = ResourceType.Population;
        sendToCasework.deliveryQuantity = caseworkClientCount;
        sendToCasework.sourceType = DeliverySourceType.RequestingFacility;
        sendToCasework.destinationType = DeliveryDestinationType.SpecificBuilding;
        sendToCasework.destinationBuilding = BuildingType.CaseworkSite;
        sendToCasework.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, 10));
        caseworkTask.agentChoices.Add(sendToCasework);

        AgentChoice delay = new AgentChoice(2, "Ask them to wait longer (-10 satisfaction)");
        delay.triggersDelivery = false;
        delay.choiceImpacts.Add(new TaskImpact(ImpactType.Satisfaction, -10));
        caseworkTask.agentChoices.Add(delay);

        caseworkTask.description += $"|CLIENT_GROUP_ID:{group.groupId}";

        if (showDebugInfo)
            Debug.Log($"ClientStayTracker generated casework task for {caseworkClientCount} clients at {facilityDisplayName}");
        GameLogPanel.Instance.LogTaskEvent($"ClientStayTracker generated casework task for {caseworkClientCount} clients at {facilityDisplayName}");
    }

    string GetFacilityDisplayName(MonoBehaviour facility)
    {
        if (facility == null) return "Unknown Facility";
        Building b = facility.GetComponent<Building>();
        if (b != null) return b.GetDisplayName();
        return facility.name;
    }

    /// <summary>
    /// Get all client groups currently in a specific shelter
    /// </summary>
    public List<ClientGroup> GetClientsInShelter(MonoBehaviour facility)
    {
        return clientGroups.Where(g => g.currentFacility == facility).ToList();
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
        var facilityOverstays = overstayRecords.GroupBy(r => r.facilityName)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.clientCount));
        stats["OverstaysByFacility"] = facilityOverstays;
        
        return stats;
    }

    /// <summary>
    /// Clear overstay records
    /// </summary>
    public void ClearOverstayRecords()
    {
        overstayRecords.Clear();

        if (showDebugInfo)
            Debug.Log("Cleared all overstay records");
        GameLogPanel.Instance.LogBuildingStatus("Cleared all overstay records");
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
            int roundsInShelter = group.GetRoundsInFacility(currentRound);
            string status = group.isOverstaying ? "OVERSTAYING" : 
                           group.caseworkRequestGenerated ? "CASEWORK REQUESTED" :
                           roundsInShelter >= 8 ? "READY FOR CASEWORK" : "NORMAL";
            
            Debug.Log($"{group.groupName}: {group.clientCount} clients at {group.currentFacility?.name} " +
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