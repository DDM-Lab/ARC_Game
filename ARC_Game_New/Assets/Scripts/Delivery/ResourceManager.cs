using UnityEngine;
using System.Collections.Generic;
using System;

public class ResourceManager : MonoBehaviour
{
    [Header("Global Resource Tracking")]
    public bool trackGlobalResources = true;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Global resource tracking
    private Dictionary<ResourceType, int> globalResources = new Dictionary<ResourceType, int>();
    
    // Events for resource changes
    public event Action<ResourceType, int> OnGlobalResourceChanged;
    public event Action OnResourcesUpdated;
    
    // Singleton instance
    public static ResourceManager Instance { get; private set; }
    
    void Awake()
    {
        // Singleton pattern
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
        
        // Initialize global resources
        InitializeGlobalResources();
    }
    
    void InitializeGlobalResources()
    {
        foreach (ResourceType resourceType in Enum.GetValues(typeof(ResourceType)))
        {
            globalResources[resourceType] = 0;
        }
        
        Debug.Log("Resource Manager initialized");
    }
    
    /// <summary>
    /// Add resources to global pool
    /// </summary>
    public void AddGlobalResource(ResourceType type, int amount)
    {
        if (!trackGlobalResources) return;
        
        globalResources[type] += amount;
        OnGlobalResourceChanged?.Invoke(type, globalResources[type]);
        OnResourcesUpdated?.Invoke();
        
        if (showDebugInfo)
            Debug.Log($"Added {amount} {type} to global pool. Total: {globalResources[type]}");
    }
    
    /// <summary>
    /// Remove resources from global pool
    /// </summary>
    public bool RemoveGlobalResource(ResourceType type, int amount)
    {
        if (!trackGlobalResources) return true;
        
        if (globalResources[type] >= amount)
        {
            globalResources[type] -= amount;
            OnGlobalResourceChanged?.Invoke(type, globalResources[type]);
            OnResourcesUpdated?.Invoke();
            
            if (showDebugInfo)
                Debug.Log($"Removed {amount} {type} from global pool. Remaining: {globalResources[type]}");
            
            return true;
        }
        
        if (showDebugInfo)
            Debug.LogWarning($"Cannot remove {amount} {type} - only {globalResources[type]} available");
        
        return false;
    }
    
    /// <summary>
    /// Get global resource amount
    /// </summary>
    public int GetGlobalResource(ResourceType type)
    {
        return globalResources.ContainsKey(type) ? globalResources[type] : 0;
    }
    
    /// <summary>
    /// Calculate total resources across all buildings
    /// </summary>
    public Dictionary<ResourceType, int> CalculateTotalResources()
    {
        Dictionary<ResourceType, int> totals = new Dictionary<ResourceType, int>();
        
        // Initialize
        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
        {
            totals[type] = 0;
        }
        
        // Sum from all building storages
        BuildingResourceStorage[] storages = FindObjectsOfType<BuildingResourceStorage>();
        foreach (BuildingResourceStorage storage in storages)
        {
            foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
            {
                totals[type] += storage.GetResourceAmount(type);
            }
        }
        
        // Sum from all prebuilt buildings
        PrebuiltBuilding[] prebuiltBuildings = FindObjectsOfType<PrebuiltBuilding>();
        foreach (PrebuiltBuilding prebuilt in prebuiltBuildings)
        {
            if (prebuilt.GetResourceStorage() != null)
            {
                foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
                {
                    totals[type] += prebuilt.GetResourceStorage().GetResourceAmount(type);
                }
            }
        }
        
        return totals;
    }
    
    /// <summary>
    /// Get resource statistics for display
    /// </summary>
    public ResourceStatistics GetResourceStatistics()
    {
        ResourceStatistics stats = new ResourceStatistics();
        Dictionary<ResourceType, int> totals = CalculateTotalResources();
        
        stats.totalPopulation = totals[ResourceType.Population];
        stats.totalFoodPacks = totals[ResourceType.FoodPacks];
        
        if (trackGlobalResources)
        {
            stats.globalPopulation = globalResources[ResourceType.Population];
            stats.globalFoodPacks = globalResources[ResourceType.FoodPacks];
        }
        
        // Calculate distribution
        Building[] buildings = FindObjectsOfType<Building>();
        PrebuiltBuilding[] prebuiltBuildings = FindObjectsOfType<PrebuiltBuilding>();
        
        foreach (Building building in buildings)
        {
            BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();
            if (storage == null) continue;
            
            switch (building.GetBuildingType())
            {
                case BuildingType.Kitchen:
                    stats.foodInKitchens += storage.GetResourceAmount(ResourceType.FoodPacks);
                    break;
                case BuildingType.Shelter:
                    stats.populationInShelters += storage.GetResourceAmount(ResourceType.Population);
                    stats.foodInShelters += storage.GetResourceAmount(ResourceType.FoodPacks);
                    break;
                case BuildingType.CaseworkSite:
                    stats.populationInCasework += storage.GetResourceAmount(ResourceType.Population);
                    break;
            }
        }
        
        foreach (PrebuiltBuilding prebuilt in prebuiltBuildings)
        {
            switch (prebuilt.GetPrebuiltType())
            {
                case PrebuiltBuildingType.Community:
                    stats.populationInCommunities += prebuilt.GetCurrentPopulation();
                    break;
                case PrebuiltBuildingType.Motel:
                    stats.populationInMotels += prebuilt.GetCurrentPopulation();
                    break;
            }
        }
        
        return stats;
    }
    
    /// <summary>
    /// Print resource statistics for debugging
    /// </summary>
    public void PrintResourceStatistics()
    {
        ResourceStatistics stats = GetResourceStatistics();
        
        Debug.Log("=== RESOURCE STATISTICS ===");
        
        if (trackGlobalResources)
        {
            Debug.Log("Global Resources:");
            Debug.Log($"  Population: {stats.globalPopulation}");
            Debug.Log($"  FoodPacks: {stats.globalFoodPacks}");
        }
        
        Debug.Log("Total Resources in Buildings:");
        Debug.Log($"  Population: {stats.totalPopulation}");
        Debug.Log($"  FoodPacks: {stats.totalFoodPacks}");
        
        Debug.Log("Population Distribution:");
        Debug.Log($"  Communities: {stats.populationInCommunities}");
        Debug.Log($"  Shelters: {stats.populationInShelters}");
        Debug.Log($"  Motels: {stats.populationInMotels}");
        Debug.Log($"  Casework Sites: {stats.populationInCasework}");
        
        Debug.Log("Food Distribution:");
        Debug.Log($"  Kitchens: {stats.foodInKitchens}");
        Debug.Log($"  Shelters: {stats.foodInShelters}");
        
        // Building breakdown
        BuildingResourceStorage[] storages = FindObjectsOfType<BuildingResourceStorage>();
        Debug.Log($"Detailed breakdown across {storages.Length} buildings:");
        foreach (BuildingResourceStorage storage in storages)
        {
            Debug.Log($"  {storage.name}: {storage.GetResourceSummary()}");
        }
    }
    
    /// <summary>
    /// Check resource balance and warn about issues
    /// </summary>
    public void CheckResourceBalance()
    {
        ResourceStatistics stats = GetResourceStatistics();
        
        // Check food vs population ratio
        if (stats.populationInShelters > 0)
        {
            float foodRatio = (float)stats.foodInShelters / stats.populationInShelters;
            
            if (foodRatio < 0.5f)
            {
                Debug.LogWarning($"Low food ratio in shelters: {foodRatio:F2} (Population: {stats.populationInShelters}, Food: {stats.foodInShelters})");
            }
            else if (foodRatio > 2f)
            {
                Debug.Log($"Surplus food in shelters: {foodRatio:F2} ratio");
            }
        }
        
        // Check if communities are emptying
        if (stats.populationInCommunities < 5)
        {
            Debug.LogWarning($"Communities running low on population: {stats.populationInCommunities} remaining");
        }
        
        // Check motel usage
        if (stats.populationInMotels > stats.populationInShelters * 0.5f)
        {
            Debug.LogWarning($"High motel usage detected - consider building more shelters");
        }
    }
    
    [ContextMenu("Print Resource Statistics")]
    public void DebugPrintStats()
    {
        PrintResourceStatistics();
    }
    
    [ContextMenu("Check Resource Balance")]
    public void DebugCheckBalance()
    {
        CheckResourceBalance();
    }
    
    /// <summary>
    /// Get resource name for display
    /// </summary>
    public static string GetResourceDisplayName(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Population:
                return "People";
            case ResourceType.FoodPacks:
                return "Food Packs";
            default:
                return type.ToString();
        }
    }
    
    /// <summary>
    /// Get resource icon/color for UI display
    /// </summary>
    public static Color GetResourceColor(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Population:
                return Color.blue;
            case ResourceType.FoodPacks:
                return Color.yellow;
            default:
                return Color.white;
        }
    }
}

[System.Serializable]
public class ResourceStatistics
{
    [Header("Global Resources")]
    public int globalPopulation;
    public int globalFoodPacks;
    
    [Header("Total Resources")]
    public int totalPopulation;
    public int totalFoodPacks;
    
    [Header("Population Distribution")]
    public int populationInCommunities;
    public int populationInShelters;
    public int populationInMotels;
    public int populationInCasework;
    
    [Header("Food Distribution")]
    public int foodInKitchens;
    public int foodInShelters;
}