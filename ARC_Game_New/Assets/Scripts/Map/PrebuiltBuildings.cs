using UnityEngine;
using System.Collections;
using System.Linq;
public enum PrebuiltBuildingType
{
    Community,
    Motel
}

public class PrebuiltBuilding : MonoBehaviour
{
    [Header("Prebuilt Building Information")]
    [SerializeField] private PrebuiltBuildingType prebuiltType;
    [SerializeField] private int buildingId;
    [SerializeField] private string buildingName;

    [Header("Visual Components")]
    public SpriteRenderer buildingRenderer;

    [Header("System References")]
    public BuildingResourceStorage resourceStorage;
    public RoadConnection roadConnection;
    [Header("Info Display")]
    public InfoDisplay infoDisplay;

    // Building functionality
    private bool isInitialized = false;

    void Start()
    {
        InitializePrebuiltBuilding();

        if (infoDisplay == null)
            infoDisplay = GetComponent<InfoDisplay>();

        // make sure the display is initialized after all components are ready
        StartCoroutine(InitializeInfoDisplayAfterFrame());
    }

    IEnumerator InitializeInfoDisplayAfterFrame()
    {
        // Wait for a frame to ensure all components are initialized
        yield return new WaitForEndOfFrame();
        UpdateInfoDisplay();
    }

    public void UpdateInfoDisplay()
    {
        if (infoDisplay == null) return;

        string displayText = "";
        Color displayColor = Color.white;

        int currentPop = GetCurrentPopulation();
        int maxPop = GetPopulationCapacity();
        displayText += $"👥 {currentPop}/{maxPop}\n";

        string taskStatus = GetTransportTaskStatus();
        if (!string.IsNullOrEmpty(taskStatus))
        {
            displayText += taskStatus;
            displayColor = Color.yellow;
        }
        else
        {
            displayText += GetBuildingStatusText();
            displayColor = GetStatusColor();
        }

        infoDisplay.UpdateDisplay(displayText, displayColor);
    }

    string GetTransportTaskStatus()
    {
        // check if there are any active delivery tasks involving this building
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem != null)
        {
            var activeTasks = deliverySystem.GetActiveTasks();

            foreach (var task in activeTasks)
            {
                if (task.sourceBuilding == this)
                    return "📤 Sending";
                if (task.destinationBuilding == this)
                    return "📥 Receiving";
            }
        }
        return "";
    }

    string GetBuildingStatusText()
    {
        switch (prebuiltType)
        {
            case PrebuiltBuildingType.Community:
                return GetCurrentPopulation() > 0 ? "Has People" : "Vacant";
            case PrebuiltBuildingType.Motel:
                return GetCurrentPopulation() > 0 ? "Has People" : "Vacant";
            default:
                return "Normal";
        }
    }

    Color GetStatusColor()
    {
        int currentPop = GetCurrentPopulation();
        int maxPop = GetPopulationCapacity();

        if (currentPop == 0) return Color.gray;
        if (currentPop >= maxPop) return Color.red;
        return Color.green;
    }

    void InitializePrebuiltBuilding()
    {
        if (isInitialized) return;

        // Get components if not assigned
        if (buildingRenderer == null)
            buildingRenderer = GetComponent<SpriteRenderer>();

        if (resourceStorage == null)
            resourceStorage = GetComponent<BuildingResourceStorage>();

        if (roadConnection == null)
            roadConnection = GetComponent<RoadConnection>();

        // Set default name if not set
        if (string.IsNullOrEmpty(buildingName))
        {
            buildingName = $"{prebuiltType} {buildingId}";
        }

        // Configure based on building type
        ConfigureBuildingType();

        // Subscribe to resource changes for UI updates
        if (resourceStorage != null)
        {
            resourceStorage.OnResourceChanged += OnResourceChanged;
            resourceStorage.OnStorageUpdated += OnStorageUpdated;
        }

        isInitialized = true;

        Debug.Log($"Prebuilt building initialized: {buildingName} ({prebuiltType})");
    }

    void ConfigureBuildingType()
    {
        switch (prebuiltType)
        {
            case PrebuiltBuildingType.Community:
                ConfigureCommunity();
                break;
            case PrebuiltBuildingType.Motel:
                ConfigureMotel();
                break;
        }
    }

    void ConfigureCommunity()
    {
        // Communities are population sources and destinations
        // They start with population and can receive people back

        if (resourceStorage != null)
        {
            // Ensure community can store population
            // Initial population should be set in the inspector via initialResources
        }

        // Communities typically don't require road connection validation
        // as they are starting points, but keep it for consistency
        if (roadConnection != null)
        {
            roadConnection.requiresRoadConnection = true;
        }
    }

    void ConfigureMotel()
    {
        // Motels are temporary population storage when shelters are full
        // They don't produce anything but can house people

        if (resourceStorage != null)
        {
            // Motels should be able to store population
            // Capacity should be set in the inspector
        }

        if (roadConnection != null)
        {
            roadConnection.requiresRoadConnection = true;
        }
    }

    void OnResourceChanged(ResourceType type, int newAmount, int capacity)
    {
        // Update visual indicators when resources change
        UpdateVisualState();
        UpdateInfoDisplay();

    }

    void OnStorageUpdated()
    {
        UpdateVisualState();
    }

    void UpdateVisualState()
    {
        if (buildingRenderer == null || resourceStorage == null) return;

        // Visual feedback based on occupancy/resources
        switch (prebuiltType)
        {
            case PrebuiltBuildingType.Community:
                UpdateCommunityVisuals();
                break;
            case PrebuiltBuildingType.Motel:
                UpdateMotelVisuals();
                break;
        }
    }

    void UpdateCommunityVisuals()
    {
        int population = resourceStorage.GetResourceAmount(ResourceType.Population);
        int capacity = resourceStorage.GetResourceCapacity(ResourceType.Population);

        if (population == 0)
        {
            buildingRenderer.color = Color.gray; // Empty community
        }
        else if (population >= capacity)
        {
            buildingRenderer.color = Color.yellow; // Full community
        }
        else
        {
            buildingRenderer.color = Color.green; // Active community
        }
    }

    void UpdateMotelVisuals()
    {
        int population = resourceStorage.GetResourceAmount(ResourceType.Population);
        int capacity = resourceStorage.GetResourceCapacity(ResourceType.Population);

        if (population == 0)
        {
            buildingRenderer.color = Color.white; // Empty motel
        }
        else if (population >= capacity)
        {
            buildingRenderer.color = Color.red; // Full motel
        }
        else
        {
            buildingRenderer.color = Color.cyan; // Occupied motel
        }
    }

    /// <summary>
    /// Check if building can accept more population
    /// </summary>
    public bool CanAcceptPopulation(int amount)
    {
        if (resourceStorage == null) return false;
        return resourceStorage.CanAddResource(ResourceType.Population, amount);
    }

    /// <summary>
    /// Check if building has population to give
    /// </summary>
    public bool HasPopulation(int amount)
    {
        if (resourceStorage == null) return false;
        return resourceStorage.HasResource(ResourceType.Population, amount);
    }

    /// <summary>
    /// Add population to this building
    /// </summary>
    public int AddPopulation(int amount)
    {
        if (resourceStorage == null) return 0;
        return resourceStorage.AddResource(ResourceType.Population, amount);
    }

    /// <summary>
    /// Remove population from this building
    /// </summary>
    public int RemovePopulation(int amount)
    {
        if (resourceStorage == null) return 0;
        return resourceStorage.RemoveResource(ResourceType.Population, amount);
    }

    /// <summary>
    /// Get current population
    /// </summary>
    public int GetCurrentPopulation()
    {
        if (resourceStorage == null) return 0;
        return resourceStorage.GetResourceAmount(ResourceType.Population);
    }

    /// <summary>
    /// Get population capacity
    /// </summary>
    public int GetPopulationCapacity()
    {
        if (resourceStorage == null) return 0;
        return resourceStorage.GetResourceCapacity(ResourceType.Population);
    }

    /// <summary>
    /// Check if this building is connected to road network
    /// </summary>
    public bool IsConnectedToRoads()
    {
        if (roadConnection == null) return true; // Assume connected if no road connection component
        return roadConnection.IsConnectedToRoad;
    }

    /// <summary>
    /// Get road connection point for transportation
    /// </summary>
    public Vector3 GetRoadConnectionPoint()
    {
        if (roadConnection == null) return transform.position;
        return roadConnection.GetRoadConnectionPoint();
    }

    // Mouse interaction for debugging
    void OnMouseDown()
    {
        if (resourceStorage != null)
        {
            Debug.Log($"{buildingName} clicked - {resourceStorage.GetResourceSummary()}");
        }
    }

    // Getters
    public PrebuiltBuildingType GetPrebuiltType() => prebuiltType;
    public int GetBuildingId() => buildingId;
    public string GetBuildingName() => buildingName;
    public BuildingResourceStorage GetResourceStorage() => resourceStorage;

    /// <summary>
    /// Get BuildingType equivalent for compatibility with existing systems
    /// </summary>
    public BuildingType GetBuildingType()
    {
        switch (prebuiltType)
        {
            case PrebuiltBuildingType.Community:
                return BuildingType.Community;
            case PrebuiltBuildingType.Motel:
                return BuildingType.Motel;
            default:
                return BuildingType.Community; // Default fallback
        }
    }

    // Public methods for external systems
    public void SetBuildingId(int id)
    {
        buildingId = id;
        if (string.IsNullOrEmpty(buildingName))
        {
            buildingName = $"{prebuiltType} {buildingId}";
        }
    }

    public void SetBuildingName(string name)
    {
        buildingName = name;
    }

    [ContextMenu("Print Building Status")]
    public void DebugPrintStatus()
    {
        Debug.Log($"=== {buildingName} STATUS ===");
        Debug.Log($"Type: {prebuiltType}");
        Debug.Log($"ID: {buildingId}");
        Debug.Log($"Road Connected: {IsConnectedToRoads()}");

        if (resourceStorage != null)
        {
            Debug.Log($"Resources: {resourceStorage.GetResourceSummary()}");
        }
    }
    
    [Header("Manual Task Debug")]
    public bool enableManualTasks = true;

    [ContextMenu("Manual: Send People to Shelter")]
    public void DebugSendPeopleToShelter()
    {
        if (!enableManualTasks || GetCurrentPopulation() <= 0) return;
        
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return;
        
        // Find a shelter with space
        Building[] shelters = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        Building targetShelter = null;
        
        foreach (Building shelter in shelters)
        {
            BuildingResourceStorage storage = shelter.GetComponent<BuildingResourceStorage>();
            if (storage != null && storage.GetAvailableSpace(ResourceType.Population) > 0)
            {
                targetShelter = shelter;
                break;
            }
        }
        
        if (targetShelter != null)
        {
            int sendAmount = Mathf.Min(GetCurrentPopulation(), 3); // Send max 3 people
            deliverySystem.CreateDeliveryTask(this, targetShelter, ResourceType.Population, sendAmount, 5);
            Debug.Log($"{GetBuildingName()} sending {sendAmount} people to {targetShelter.name}");
        }
        else
        {
            Debug.LogWarning($"{GetBuildingName()} cannot send people - no shelters with space available");
        }
    }

    [ContextMenu("Manual: Send People to Motel")]
    public void DebugSendPeopleToMotel()
    {
        if (!enableManualTasks || GetCurrentPopulation() <= 0) return;
        
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return;
        
        // Find a motel with space
        PrebuiltBuilding[] motels = FindObjectsOfType<PrebuiltBuilding>().Where(pb => pb.GetPrebuiltType() == PrebuiltBuildingType.Motel).ToArray();
        PrebuiltBuilding targetMotel = null;
        
        foreach (PrebuiltBuilding motel in motels)
        {
            if (motel.CanAcceptPopulation(1))
            {
                targetMotel = motel;
                break;
            }
        }
        
        if (targetMotel != null)
        {
            int sendAmount = Mathf.Min(GetCurrentPopulation(), 2); // Send max 2 people to motel
            deliverySystem.CreateDeliveryTask(this, targetMotel, ResourceType.Population, sendAmount, 5);
            Debug.Log($"{GetBuildingName()} sending {sendAmount} people to {targetMotel.GetBuildingName()}");
        }
        else
        {
            Debug.LogWarning($"{GetBuildingName()} cannot send people - no motels with space available");
        }
    }

    [ContextMenu("Manual: Accept People from Community")]
    public void DebugAcceptPeopleFromCommunity()
    {
        if (!enableManualTasks || !CanAcceptPopulation(1)) return;
        
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return;
        
        // Find a community with people
        PrebuiltBuilding[] communities = FindObjectsOfType<PrebuiltBuilding>().Where(pb => pb.GetPrebuiltType() == PrebuiltBuildingType.Community && pb != this).ToArray();
        PrebuiltBuilding sourceCommunity = null;
        
        foreach (PrebuiltBuilding community in communities)
        {
            if (community.GetCurrentPopulation() > 0)
            {
                sourceCommunity = community;
                break;
            }
        }
        
        if (sourceCommunity != null)
        {
            int availableSpace = GetPopulationCapacity() - GetCurrentPopulation();
            int requestAmount = Mathf.Min(3, availableSpace, sourceCommunity.GetCurrentPopulation());
            deliverySystem.CreateDeliveryTask(sourceCommunity, this, ResourceType.Population, requestAmount, 5);
            Debug.Log($"{GetBuildingName()} requesting {requestAmount} people from {sourceCommunity.GetBuildingName()}");
        }
        else
        {
            Debug.LogWarning($"{GetBuildingName()} cannot accept people - no communities with people available");
        }
    }

    [ContextMenu("Manual: Print Building Status")]
    public void DebugPrintPrebuiltStatus()
    {
        Debug.Log($"=== {GetBuildingName()} DEBUG STATUS ===");
        Debug.Log($"Building Type: {GetPrebuiltType()}");
        Debug.Log($"Population: {GetCurrentPopulation()}/{GetPopulationCapacity()}");
        Debug.Log($"Can Accept More: {CanAcceptPopulation(1)}");
        
        if (resourceStorage != null)
        {
            Debug.Log($"Resources: {resourceStorage.GetResourceSummary()}");
        }
        
        RoadConnection roadConn = GetComponent<RoadConnection>();
        if (roadConn != null)
        {
            Debug.Log($"Road Connected: {roadConn.IsConnectedToRoad}");
        }
    }
}