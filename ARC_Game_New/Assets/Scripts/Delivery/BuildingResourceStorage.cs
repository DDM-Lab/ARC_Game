using UnityEngine;
using System.Collections.Generic;
using System;

public class BuildingResourceStorage : MonoBehaviour
{
    [Header("Storage Configuration")]
    public List<ResourceCapacity> resourceCapacities = new List<ResourceCapacity>();
    
    [Header("Initial Resources")]
    public List<ResourceAmount> initialResources = new List<ResourceAmount>();
    
    [Header("Auto Production/Consumption")]
    public List<ResourceProduction> resourceProduction = new List<ResourceProduction>();
    public List<ResourceConsumption> resourceConsumption = new List<ResourceConsumption>();
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Current resource storage
    private Dictionary<ResourceType, int> currentResources = new Dictionary<ResourceType, int>();
    private Dictionary<ResourceType, int> maxCapacities = new Dictionary<ResourceType, int>();
    
    // Events
    public event Action<ResourceType, int, int> OnResourceChanged; // type, newAmount, capacity
    public event Action OnStorageUpdated;
    
    // Auto production/consumption
    private float lastProductionTime = 0f;
    private float lastConsumptionTime = 0f;
    
    void Start()
    {
        InitializeStorage();
    }
    
    void Update()
    {
        // Handle auto production and consumption
        HandleAutoProduction();
        HandleAutoConsumption();
    }
    
    void InitializeStorage()
    {
        // Initialize capacities
        foreach (ResourceCapacity capacity in resourceCapacities)
        {
            maxCapacities[capacity.resourceType] = capacity.maxCapacity;
            currentResources[capacity.resourceType] = 0;
        }
        
        // Set initial resources
        foreach (ResourceAmount resource in initialResources)
        {
            if (maxCapacities.ContainsKey(resource.type))
            {
                int actualAmount = Mathf.Min(resource.amount, maxCapacities[resource.type]);
                currentResources[resource.type] = actualAmount;
                
                if (showDebugInfo)
                    Debug.Log($"{gameObject.name} initialized with {actualAmount} {resource.type}");
            }
        }
        
        OnStorageUpdated?.Invoke();
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
    /// Handle automatic production
    /// </summary>
    void HandleAutoProduction()
    {
        if (resourceProduction.Count == 0) return;
        
        float currentTime = Time.time;
        
        foreach (ResourceProduction production in resourceProduction)
        {
            if (currentTime - lastProductionTime >= production.productionInterval)
            {
                // Check if we can produce (capacity available)
                if (CanAddResource(production.resourceType, production.amountPerInterval))
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
                        AddResource(production.resourceType, production.amountPerInterval);
                        
                        if (showDebugInfo)
                            Debug.Log($"{gameObject.name} produced {production.amountPerInterval} {production.resourceType}");
                    }
                }
                
                lastProductionTime = currentTime;
            }
        }
    }
    
    /// <summary>
    /// Handle automatic consumption
    /// </summary>
    void HandleAutoConsumption()
    {
        if (resourceConsumption.Count == 0) return;
        
        float currentTime = Time.time;
        
        foreach (ResourceConsumption consumption in resourceConsumption)
        {
            if (currentTime - lastConsumptionTime >= consumption.consumptionInterval)
            {
                // Check if we have the resource to consume
                if (HasResource(consumption.resourceType, consumption.amountPerInterval))
                {
                    RemoveResource(consumption.resourceType, consumption.amountPerInterval);
                    
                    if (showDebugInfo)
                        Debug.Log($"{gameObject.name} consumed {consumption.amountPerInterval} {consumption.resourceType}");
                }
                else if (showDebugInfo)
                {
                    Debug.LogWarning($"{gameObject.name} cannot consume {consumption.amountPerInterval} {consumption.resourceType} - insufficient resources");
                }
                
                lastConsumptionTime = currentTime;
            }
        }
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
    
    [ContextMenu("Print Resource Status")]
    public void DebugPrintStatus()
    {
        Debug.Log($"{gameObject.name} Resource Status: {GetResourceSummary()}");
    }
}

[System.Serializable]
public class ResourceCapacity
{
    public ResourceType resourceType;
    public int maxCapacity;
}

[System.Serializable]
public class ResourceProduction
{
    public ResourceType resourceType;
    public int amountPerInterval;
    public float productionInterval = 5f; // seconds
    public List<ResourceAmount> requiredResources = new List<ResourceAmount>(); // input requirements
}

[System.Serializable]
public class ResourceConsumption
{
    public ResourceType resourceType;
    public int amountPerInterval;
    public float consumptionInterval = 10f; // seconds
}