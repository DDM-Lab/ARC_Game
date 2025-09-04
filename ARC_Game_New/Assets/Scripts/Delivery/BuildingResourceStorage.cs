using UnityEngine;
using System.Collections.Generic;
using System;

public class BuildingResourceStorage : MonoBehaviour
{
    [Header("Storage Configuration")]
    public List<ResourceCapacity> resourceCapacities = new List<ResourceCapacity>();
    
    [Header("Daily Reset Settings")]
    public List<ResourceAmount> dailyStartingResources = new List<ResourceAmount>();
    public bool enableDailyFoodWaste = true;
    public int dailyStartingFoodPacks = 0; // Separate food allocation per day
    
    [Header("Round-Based Production")]
    public List<RoundResourceProduction> roundProduction = new List<RoundResourceProduction>();
    
    [Header("Population-Based Consumption")]
    public bool enablePopulationBasedConsumption = true;
    public int foodPerPersonPerNRounds  = 1;
    public int consumptionRoundInterval = 4; // Consume food every N rounds
    public bool workersConsumeFoodToo = true;

    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Current resource storage
    private Dictionary<ResourceType, int> currentResources = new Dictionary<ResourceType, int>();
    private Dictionary<ResourceType, int> maxCapacities = new Dictionary<ResourceType, int>();
    
    // Events
    public event Action<ResourceType, int, int> OnResourceChanged; // type, newAmount, capacity
    public event Action OnStorageUpdated;

    private int roundsSinceLastConsumption = 0;
    
    void Start()
    {
        InitializeStorage();
        SubscribeToEvents();
    }
    
    void SubscribeToEvents()
    {
        // Subscribe to round changes for production and consumption
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;
            GlobalClock.Instance.OnDayChanged += OnDayChanged;
        }
    }
    
    void InitializeStorage()
    {
        // Initialize capacities
        foreach (ResourceCapacity capacity in resourceCapacities)
        {
            maxCapacities[capacity.resourceType] = capacity.maxCapacity;
            currentResources[capacity.resourceType] = 0;
        }
        
        // Set daily starting resources
        SetDailyStartingResources();
        
        OnStorageUpdated?.Invoke();
    }
    
    void SetDailyStartingResources()
    {
        // Set general daily starting resources (population, etc.)
        foreach (ResourceAmount resource in dailyStartingResources)
        {
            if (maxCapacities.ContainsKey(resource.type))
            {
                int actualAmount = Mathf.Min(resource.amount, maxCapacities[resource.type]);
                currentResources[resource.type] = actualAmount;
                
                if (showDebugInfo)
                    Debug.Log($"{gameObject.name} daily reset to {actualAmount} {resource.type}");
            }
        }
        
        // Separately handle daily food allocation
        if (dailyStartingFoodPacks > 0 && maxCapacities.ContainsKey(ResourceType.FoodPacks))
        {
            int actualFood = Mathf.Min(dailyStartingFoodPacks, maxCapacities[ResourceType.FoodPacks]);
            currentResources[ResourceType.FoodPacks] = actualFood;
            
            if (showDebugInfo)
                Debug.Log($"{gameObject.name} received daily food allocation: {actualFood} food packs");
        }
        
        OnStorageUpdated?.Invoke();
    }

    void OnRoundChanged(int newRound)
    {
        HandleRoundProduction();
        HandlePopulationConsumptionCycle();
    }
    
    void OnDayChanged(int newDay)
    {
        HandleDailyReset();
    }
    
    void HandleRoundProduction()
    {
        // Check if this building is operational before producing
        Building building = GetComponent<Building>();
        if (building != null && !building.IsOperational())
        {
            if (showDebugInfo)
                Debug.Log($"{gameObject.name} cannot produce - building not operational (Status: {building.GetCurrentStatus()})");
            return;
        }

        foreach (RoundResourceProduction production in roundProduction)
        {
            // Check if we can produce (capacity available)
            if (CanAddResource(production.resourceType, production.amountPerRound))
            {
                // Check if we have required input resources
                bool canProduce = true;
                foreach (ResourceAmount requirement in production.requiredResources)
                {
                    if (!HasResource(requirement.type, requirement.amount))
                    {
                        canProduce = false;
                        break;
                    }
                }

                if (canProduce)
                {
                    // Consume input resources
                    foreach (ResourceAmount requirement in production.requiredResources)
                    {
                        RemoveResource(requirement.type, requirement.amount);
                    }

                    // Produce output resource
                    AddResource(production.resourceType, production.amountPerRound);

                    if (showDebugInfo)
                        Debug.Log($"{gameObject.name} produced {production.amountPerRound} {production.resourceType} this round");
                }
                else if (showDebugInfo)
                {
                    Debug.LogWarning($"{gameObject.name} cannot produce {production.resourceType} - missing required resources");
                }
            }
            else if (showDebugInfo)
            {
                Debug.LogWarning($"{gameObject.name} cannot produce {production.resourceType} - storage full");
            }
        }
    }
    
    void HandlePopulationConsumptionCycle()
    {
        if (!enablePopulationBasedConsumption) return;
        
        roundsSinceLastConsumption++;
        
        // Only consume food every N rounds
        if (roundsSinceLastConsumption >= consumptionRoundInterval)
        {
            int totalPeopleToFeed = GetTotalPeopleCount();
            int foodNeeded = totalPeopleToFeed * foodPerPersonPerNRounds;
            
            if (foodNeeded > 0)
            {
                int foodConsumed = RemoveResource(ResourceType.FoodPacks, foodNeeded);
                
                if (showDebugInfo)
                {
                    Debug.Log($"{gameObject.name} fed {totalPeopleToFeed} people after {consumptionRoundInterval} rounds, consumed {foodConsumed}/{foodNeeded} food packs");
                    
                    if (foodConsumed < foodNeeded)
                    {
                        Debug.LogWarning($"{gameObject.name} FOOD SHORTAGE: Need {foodNeeded}, only had {foodConsumed}");
                    }
                }
            }
            
            // Reset counter
            roundsSinceLastConsumption = 0;
        }
        else if (showDebugInfo)
        {
            Debug.Log($"{gameObject.name} consumption cycle: {roundsSinceLastConsumption}/{consumptionRoundInterval} rounds");
        }
    }
    
    int GetTotalPeopleCount()
    {
        int totalPeople = 0;
        
        // Count population
        totalPeople += GetResourceAmount(ResourceType.Population);
        
        // Count workers if they consume food too
        if (workersConsumeFoodToo)
        {
            Building building = GetComponent<Building>();
            if (building != null)
            {
                totalPeople += building.GetAssignedWorkforce();
            }
        }
        
        return totalPeople;
    }
    
    void HandleDailyReset()
    {
        if (enableDailyFoodWaste)
        {
            // Remove all unused food at end of day
            int wastedFood = GetResourceAmount(ResourceType.FoodPacks);
            if (wastedFood > 0)
            {
                RemoveResource(ResourceType.FoodPacks, wastedFood);
                
                if (showDebugInfo)
                    Debug.Log($"{gameObject.name} wasted {wastedFood} unused food packs at end of day");
            }
        }
        
        // Reset to daily starting resources
        SetDailyStartingResources();
    }
    
    /// <summary>
    /// Add resources to storage
    /// </summary>
    public int AddResource(ResourceType type, int amount)
    {
        if (!maxCapacities.ContainsKey(type))
        {
            if (showDebugInfo)
                Debug.LogWarning($"{gameObject.name} cannot store {type}");
            return 0;
        }
        
        int currentAmount = currentResources[type];
        int capacity = maxCapacities[type];
        int spaceAvailable = capacity - currentAmount;
        int actualAdded = Mathf.Min(amount, spaceAvailable);
        
        currentResources[type] = currentAmount + actualAdded;
        
        OnResourceChanged?.Invoke(type, currentResources[type], capacity);
        OnStorageUpdated?.Invoke();
        
        if (showDebugInfo && actualAdded > 0)
            Debug.Log($"{gameObject.name} received {actualAdded} {type} ({currentResources[type]}/{capacity})");
        
        return actualAdded;
    }
    
    /// <summary>
    /// Remove resources from storage
    /// </summary>
    public int RemoveResource(ResourceType type, int amount)
    {
        if (!currentResources.ContainsKey(type))
            return 0;
        
        int currentAmount = currentResources[type];
        int actualRemoved = Mathf.Min(amount, currentAmount);
        
        currentResources[type] = currentAmount - actualRemoved;
        
        OnResourceChanged?.Invoke(type, currentResources[type], maxCapacities[type]);
        OnStorageUpdated?.Invoke();
        
        if (showDebugInfo && actualRemoved > 0)
            Debug.Log($"{gameObject.name} lost {actualRemoved} {type} ({currentResources[type]}/{maxCapacities[type]})");
        
        return actualRemoved;
    }
    
    /// <summary>
    /// Check if can add specific amount of resource
    /// </summary>
    public bool CanAddResource(ResourceType type, int amount)
    {
        if (!maxCapacities.ContainsKey(type))
            return false;
        
        int currentAmount = currentResources.ContainsKey(type) ? currentResources[type] : 0;
        int capacity = maxCapacities[type];
        
        return (currentAmount + amount) <= capacity;
    }
    
    /// <summary>
    /// Check if has specific amount of resource
    /// </summary>
    public bool HasResource(ResourceType type, int amount)
    {
        if (!currentResources.ContainsKey(type))
            return false;
        
        return currentResources[type] >= amount;
    }
    
    /// <summary>
    /// Get current resource amount
    /// </summary>
    public int GetResourceAmount(ResourceType type)
    {
        return currentResources.ContainsKey(type) ? currentResources[type] : 0;
    }
    
    /// <summary>
    /// Get resource capacity
    /// </summary>
    public int GetResourceCapacity(ResourceType type)
    {
        return maxCapacities.ContainsKey(type) ? maxCapacities[type] : 0;
    }
    
    /// <summary>
    /// Get available space for resource
    /// </summary>
    public int GetAvailableSpace(ResourceType type)
    {
        if (!maxCapacities.ContainsKey(type))
            return 0;
        
        int currentAmount = currentResources.ContainsKey(type) ? currentResources[type] : 0;
        return maxCapacities[type] - currentAmount;
    }
    
    /// <summary>
    /// Check if storage is full for a resource type
    /// </summary>
    public bool IsResourceFull(ResourceType type)
    {
        return GetAvailableSpace(type) <= 0;
    }
    
    /// <summary>
    /// Check if storage is empty for a resource type
    /// </summary>
    public bool IsResourceEmpty(ResourceType type)
    {
        return GetResourceAmount(type) <= 0;
    }
    
    /// <summary>
    /// Transfer resources to another storage
    /// </summary>
    public int TransferResourceTo(BuildingResourceStorage targetStorage, ResourceType type, int amount)
    {
        if (targetStorage == null || !HasResource(type, amount))
            return 0;
        
        int actualTransferred = targetStorage.AddResource(type, amount);
        RemoveResource(type, actualTransferred);
        
        if (showDebugInfo && actualTransferred > 0)
            Debug.Log($"Transferred {actualTransferred} {type} from {gameObject.name} to {targetStorage.gameObject.name}");
        
        return actualTransferred;
    }
    
    /// <summary>
    /// Get resource summary for debugging
    /// </summary>
    public string GetResourceSummary()
    {
        List<string> summary = new List<string>();
        
        foreach (var kvp in currentResources)
        {
            int capacity = maxCapacities.ContainsKey(kvp.Key) ? maxCapacities[kvp.Key] : 0;
            summary.Add($"{kvp.Key}: {kvp.Value}/{capacity}");
        }
        
        return string.Join(", ", summary);
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged -= OnRoundChanged;
            GlobalClock.Instance.OnDayChanged -= OnDayChanged;
        }
    }
    

    
    [ContextMenu("Force Daily Reset")]
    public void DebugForceDailyReset()
    {
        HandleDailyReset();
    }
}

[System.Serializable]
public class ResourceCapacity
{
    public ResourceType resourceType;
    public int maxCapacity;
}

[System.Serializable]
public class RoundResourceProduction
{
    public ResourceType resourceType;
    public int amountPerRound;
    public List<ResourceAmount> requiredResources = new List<ResourceAmount>(); // input requirements
}

[System.Serializable]
public class ResourceAmount
{
    public ResourceType type;
    public int amount;
}

public enum ResourceType
{
    Population,
    FoodPacks
}
