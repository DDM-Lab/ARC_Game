using UnityEngine;
using System.Collections.Generic;
using System;

public class BuildingResourceStorage : MonoBehaviour
{
    [Header("Storage Configuration")]
    public List<ResourceCapacity> resourceCapacities = new List<ResourceCapacity>();
    
    [Header("Game Start Resource Settings")]
    public List<ResourceAmount> startingResources = new List<ResourceAmount>();
    
    [Header("Daily Food Settings")]
    public int startingFoodPacks = 0; // Separate food allocation per game start
    public bool enableFoodWaste = true;
    [Tooltip("If true, this storage's FoodPacks are topped up to full capacity at the start of each day (e.g. Kitchens), instead of the flat startingFoodPacks amount.")]
    public bool fillFoodToCapacityDaily = false;

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
    private int lastConsumptionRoundKey = int.MinValue;

    private int todayFoodPacksConsumed = 0;
    
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
            Debug.Log($"{gameObject.name} storage initialized to {capacity.maxCapacity} of {capacity.resourceType}");
            GameLogPanel.Instance.LogResourceChange($"{gameObject.name} storage initialized to {capacity.maxCapacity} of {capacity.resourceType}");
        }
        
        // Set daily starting resources
        SetStartingResources();
        FillFoodToCapacityIfConfigured();

        OnStorageUpdated?.Invoke();
    }
    
    void SetStartingResources()
    {
        // Set general game start resources (population, etc.)
        foreach (ResourceAmount resource in startingResources)
        {
            if (maxCapacities.ContainsKey(resource.type))
            {
                int actualAmount = Mathf.Min(resource.amount, maxCapacities[resource.type]);
                currentResources[resource.type] = actualAmount;

                if (showDebugInfo)
                    Debug.Log($"{gameObject.name} game start resource set to {actualAmount} {resource.type}");

                GameLogPanel.Instance.LogResourceChange($"{gameObject.name} game start resource is set to {actualAmount} {resource.type}");
            }
        }
        
        OnStorageUpdated?.Invoke();
    }

    void OnRoundChanged(int newRound)
    {
        if (newRound <= 4) // round 5 is daily report stage
        {
            HandlePopulationConsumptionCycle();
        }
    }

    void OnDayChanged(int newDay)
    {
        HandleDailyReset();
    }

    void HandlePopulationConsumptionCycle()
    {
        if (!enablePopulationBasedConsumption) return;

        roundsSinceLastConsumption++;

        // Only consume food every N rounds
        if (roundsSinceLastConsumption >= consumptionRoundInterval)
        {
            if (ConsumeFoodForPopulationOncePerRound($"after {consumptionRoundInterval} rounds"))
                roundsSinceLastConsumption = 0;
        }
        else
        {
            if (showDebugInfo)
                Debug.Log($"{gameObject.name} consumption cycle: {roundsSinceLastConsumption}/{consumptionRoundInterval} rounds");
            GameLogPanel.Instance.LogResourceChange($"{gameObject.name} consumption cycle: {roundsSinceLastConsumption}/{consumptionRoundInterval} rounds");
        }
    }

    /// <summary>
    /// Runs ConsumeFoodForPopulation at most once per round, no matter how many times it's
    /// requested — a single "deliver enough for two rounds" request can arrive as several
    /// separate vehicle drop-offs (destination stock split across kitchens/vehicle capacity),
    /// and each one calls AddResource. Without this guard every one of those arrivals would
    /// independently re-feed the whole population, consuming far more than one round's need.
    /// Returns true if consumption actually ran (so callers know whether to reset their own
    /// round-interval counters).
    /// </summary>
    bool ConsumeFoodForPopulationOncePerRound(string reasonSuffix)
    {
        if (GlobalClock.Instance == null)
        {
            ConsumeFoodForPopulation(reasonSuffix);
            return true;
        }

        int roundKey = GlobalClock.Instance.GetCurrentDay() * 100 + GlobalClock.Instance.GetCurrentTimeSegment();
        if (roundKey == lastConsumptionRoundKey) return false;

        lastConsumptionRoundKey = roundKey;
        ConsumeFoodForPopulation(reasonSuffix);
        return true;
    }

    /// <summary>
    /// Feeds everyone currently at this facility one consumption cycle's worth of food
    /// (population count × foodPerPersonPerNRounds), deducting from storage. This is the single
    /// place food is ever consumed — called both by the round-based timer above and immediately
    /// when a food delivery arrives (see AddResource), so there is exactly one consumption path.
    /// Go through ConsumeFoodForPopulationOncePerRound rather than calling this directly.
    /// </summary>
    void ConsumeFoodForPopulation(string reasonSuffix)
    {
        int totalPeopleToFeed = GetTotalPeopleCount();
        int foodNeeded = totalPeopleToFeed * foodPerPersonPerNRounds;

        if (foodNeeded <= 0) return;

        int foodConsumed = RemoveResource(ResourceType.FoodPacks, foodNeeded);
        if (DailyReportData.Instance != null)
            DailyReportData.Instance.RecordFoodConsumptionCumulative(foodConsumed, foodNeeded);
        todayFoodPacksConsumed += foodConsumed;

        if (showDebugInfo)
        {
            Debug.Log($"{gameObject.name} fed {totalPeopleToFeed} people {reasonSuffix}, consumed {foodConsumed}/{foodNeeded} meals");

            if (foodConsumed < foodNeeded)
                Debug.Log($"{gameObject.name} FOOD SHORTAGE: Need {foodNeeded}, only had {foodConsumed}");
        }

        GameLogPanel.Instance.LogResourceChange($"{gameObject.name} fed {totalPeopleToFeed} people {reasonSuffix}, consumed {foodConsumed}/{foodNeeded} meals");
        if (foodConsumed < foodNeeded)
            GameLogPanel.Instance.LogResourceChange($"{gameObject.name} FOOD SHORTAGE: Need {foodNeeded}, only had {foodConsumed}");
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

    public int GetFoodNeed()
    {
        int required = GetTotalPeopleCount() * foodPerPersonPerNRounds;
        int inStorage = GetResourceAmount(ResourceType.FoodPacks);
        return Mathf.Max(0, required - inStorage);
    }

    void HandleDailyReset()
    {
        if (enableFoodWaste)
        {

            todayFoodPacksConsumed = 0;

            // Remove all unused food at end of day
            int wastedFood = GetResourceAmount(ResourceType.FoodPacks);
            if (wastedFood > 0)
            {
                // Report to daily tracking
                DailyReportData.Instance.RecordFoodWasted(wastedFood);

                DailyReportData.Instance.RecordFoodWasteCumulative(wastedFood);

                
                RemoveResource(ResourceType.FoodPacks, wastedFood);

                if (showDebugInfo)
                    Debug.Log($"{gameObject.name} wasted {wastedFood} unused meals at end of day");
                GameLogPanel.Instance.LogResourceChange($"{gameObject.name} wasted {wastedFood} unused meals at end of day");
            }
        }
        
        // Start the new day fully stocked (e.g. Kitchens), or with a flat starting amount.
        if (fillFoodToCapacityDaily)
        {
            FillFoodToCapacityIfConfigured();
        }
        else if (startingFoodPacks > 0)
        {
            int actualAdded = AddResource(ResourceType.FoodPacks, startingFoodPacks);
            if (showDebugInfo)
                Debug.Log($"{gameObject.name} received {actualAdded} starting meals at start of day");
            GameLogPanel.Instance.LogResourceChange($"{gameObject.name} received {actualAdded} starting meals at start of day");
        }
    }

    /// <summary>
    /// Tops FoodPacks up to full capacity (used by storages with fillFoodToCapacityDaily,
    /// e.g. Kitchens) — called at initial game start and at each day's reset.
    /// </summary>
    void FillFoodToCapacityIfConfigured()
    {
        if (!fillFoodToCapacityDaily) return;

        Building building = GetComponent<Building>();
        if (building != null && !building.IsOperational())
        {
            if (showDebugInfo)
                Debug.Log($"{gameObject.name} not stocked - building not operational (Status: {building.GetCurrentStatus()})");
            return;
        }

        int missing = GetResourceCapacity(ResourceType.FoodPacks) - GetResourceAmount(ResourceType.FoodPacks);
        if (missing <= 0) return;

        int actualAdded = AddResource(ResourceType.FoodPacks, missing);
        if (showDebugInfo)
            Debug.Log($"{gameObject.name} stocked to full capacity: +{actualAdded} food packs");
        GameLogPanel.Instance.LogResourceChange($"{gameObject.name} stocked to full capacity: +{actualAdded} food packs");
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
        GameLogPanel.Instance.LogResourceChange($"{gameObject.name} received {actualAdded} {type} ({currentResources[type]}/{capacity})");

        // Clients eat as soon as food arrives, rather than waiting for the next round-based
        // consumption tick. Guarded to once per round (see ConsumeFoodForPopulationOncePerRound)
        // so a single request fulfilled via several separate vehicle drop-offs in the same round
        // doesn't feed everyone once per drop-off.
        if (type == ResourceType.FoodPacks && actualAdded > 0 && enablePopulationBasedConsumption)
        {
            if (ConsumeFoodForPopulationOncePerRound("immediately after delivery"))
                roundsSinceLastConsumption = 0;
        }

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
        GameLogPanel.Instance.LogResourceChange($"{gameObject.name} lost {actualRemoved} {type} ({currentResources[type]}/{maxCapacities[type]})");

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

    public int GetTodayFoodPacksConsumed() => todayFoodPacksConsumed;
    
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
        GameLogPanel.Instance.LogResourceChange($"Transferred {actualTransferred} {type} from {gameObject.name} to {targetStorage.gameObject.name}");
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

    public int GetMaxCapacity(ResourceType resourceType)
    {
        return maxCapacities[resourceType];
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
