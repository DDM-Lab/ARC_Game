using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ResourceFlowManager : MonoBehaviour
{
    [Header("System References")]
    public DeliverySystem deliverySystem;
    public ResourceManager resourceManager;
    
    [Header("Flow Control")]
    public bool enableAutomaticFlow = false;
    public float flowCheckInterval = 5f; // Check resource flows every 5 seconds
    
    [Header("Flow Priorities")]
    public int foodDeliveryPriority = 3;
    public int populationTransportPriority = 2;
    public int returnHomePriority = 1;
    
    [Header("Emergency Thresholds")]
    public float criticalFoodRatio = 0.2f; // If food/population ratio drops below this, emergency delivery
    public int minPopulationPerTransport = 1;
    public int maxPopulationPerTransport = 5;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool logResourceFlows = true;
    
    // Game state tracking
    private float lastFlowCheck = 0f;
    private Dictionary<Building, float> lastFoodDeliveryTime = new Dictionary<Building, float>();
    private Dictionary<PrebuiltBuilding, float> lastPopulationTransport = new Dictionary<PrebuiltBuilding, float>();
    
    // Flow statistics
    private int totalFoodDeliveries = 0;
    private int totalPopulationTransports = 0;
    private int totalReturnHomeTransports = 0;
    
    void Start()
    {
        // Find system references if not assigned
        if (deliverySystem == null)
            deliverySystem = FindObjectOfType<DeliverySystem>();
        
        if (resourceManager == null)
            resourceManager = FindObjectOfType<ResourceManager>();
        
        // Subscribe to delivery events
        if (deliverySystem != null)
        {
            deliverySystem.OnTaskCompleted += OnDeliveryCompleted;
        }
        
        Debug.Log("Resource Flow Manager initialized");
    }
    
    void Update()
    {
        if (enableAutomaticFlow && Time.time - lastFlowCheck > flowCheckInterval)
        {
            AnalyzeAndManageResourceFlows();
            lastFlowCheck = Time.time;
        }
    }
    
    /// <summary>
    /// Main method to analyze and manage all resource flows
    /// </summary>
    void AnalyzeAndManageResourceFlows()
    {
        if (showDebugInfo)
            Debug.Log("=== ANALYZING RESOURCE FLOWS ===");
        
        // 1. Check critical food situations (highest priority)
        HandleCriticalFoodSituations();
        
        // 2. Manage regular food distribution
        ManageFoodDistribution();
        
        // 3. Handle population transportation from communities
        ManagePopulationTransportation();
        
        // 4. Handle return home requests (late game)
        ManageReturnHomeFlow();
        
        // 5. Balance shelter vs motel usage
        BalanceShelterMotelUsage();
        
        if (logResourceFlows)
            LogResourceFlowStatus();
    }
    
    /// <summary>
    /// Handle emergency food delivery to shelters with critical food shortages
    /// </summary>
    void HandleCriticalFoodSituations()
    {
        Building[] shelters = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        
        foreach (Building shelter in shelters)
        {
            BuildingResourceStorage storage = shelter.GetComponent<BuildingResourceStorage>();
            if (storage == null) continue;
            
            int population = storage.GetResourceAmount(ResourceType.Population);
            int foodPacks = storage.GetResourceAmount(ResourceType.FoodPacks);
            
            if (population > 0)
            {
                float foodRatio = (float)foodPacks / population;
                
                if (foodRatio < criticalFoodRatio)
                {
                    // Critical situation - request emergency food delivery
                    RequestEmergencyFoodDelivery(shelter, population - foodPacks);
                    
                    if (showDebugInfo)
                        Debug.LogWarning($"CRITICAL: {shelter.name} has food ratio {foodRatio:F2} (threshold: {criticalFoodRatio})");
                }
            }
        }
    }
    
    /// <summary>
    /// Request emergency food delivery with high priority
    /// </summary>
    void RequestEmergencyFoodDelivery(Building shelter, int urgentAmount)
    {
        Building bestKitchen = FindBestFoodSource(urgentAmount);
        
        if (bestKitchen != null)
        {
            DeliveryTask emergencyTask = deliverySystem.CreateDeliveryTask(
                bestKitchen, shelter, ResourceType.FoodPacks, urgentAmount, foodDeliveryPriority + 2);
            
            if (emergencyTask != null)
            {
                emergencyTask.isUrgent = true;
                
                if (showDebugInfo)
                    Debug.Log($"Emergency food delivery requested: {urgentAmount} food packs to {shelter.name}");
            }
        }
    }
    
    /// <summary>
    /// Manage regular food distribution to maintain healthy ratios
    /// </summary>
    void ManageFoodDistribution()
    {
        Building[] kitchens = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational()).ToArray();
        Building[] shelters = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        
        foreach (Building kitchen in kitchens)
        {
            BuildingResourceStorage kitchenStorage = kitchen.GetComponent<BuildingResourceStorage>();
            if (kitchenStorage == null) continue;
            
            int availableFoodPacks = kitchenStorage.GetResourceAmount(ResourceType.FoodPacks);
            if (availableFoodPacks < 3) continue; // Keep minimum stock
            
            // Find shelter that would benefit most from food delivery
            Building targetShelter = FindBestFoodDestination(shelters);
            
            if (targetShelter != null)
            {
                int deliveryAmount = Mathf.Min(availableFoodPacks - 2, 5); // Leave 2 in kitchen, max 5 per delivery
                
                deliverySystem.CreateDeliveryTask(kitchen, targetShelter, ResourceType.FoodPacks, deliveryAmount, foodDeliveryPriority);
                lastFoodDeliveryTime[kitchen] = Time.time;
                
                if (logResourceFlows)
                    Debug.Log($"Scheduled food delivery: {deliveryAmount} from {kitchen.name} to {targetShelter.name}");
            }
        }
    }
    
    /// <summary>
    /// Find the best kitchen to source food from
    /// </summary>
    Building FindBestFoodSource(int requiredAmount)
    {
        Building[] kitchens = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational()).ToArray();
        
        Building bestKitchen = null;
        int bestAvailableAmount = 0;
        
        foreach (Building kitchen in kitchens)
        {
            BuildingResourceStorage storage = kitchen.GetComponent<BuildingResourceStorage>();
            if (storage == null) continue;
            
            int availableFood = storage.GetResourceAmount(ResourceType.FoodPacks);
            
            if (availableFood >= requiredAmount && availableFood > bestAvailableAmount)
            {
                bestAvailableAmount = availableFood;
                bestKitchen = kitchen;
            }
        }
        
        return bestKitchen;
    }
    
    /// <summary>
    /// Find the shelter that would benefit most from food delivery
    /// </summary>
    Building FindBestFoodDestination(Building[] shelters)
    {
        Building bestShelter = null;
        float bestNeedScore = 0f;
        
        foreach (Building shelter in shelters)
        {
            BuildingResourceStorage storage = shelter.GetComponent<BuildingResourceStorage>();
            if (storage == null) continue;
            
            int population = storage.GetResourceAmount(ResourceType.Population);
            int foodPacks = storage.GetResourceAmount(ResourceType.FoodPacks);
            int foodCapacity = storage.GetResourceCapacity(ResourceType.FoodPacks);
            
            if (population > 0 && foodPacks < foodCapacity)
            {
                // Calculate need score based on population/food ratio and available space
                float foodRatio = population > 0 ? (float)foodPacks / population : 1f;
                float spaceRatio = (float)(foodCapacity - foodPacks) / foodCapacity;
                float needScore = (1f - foodRatio) * 10f + spaceRatio * 5f;
                
                if (needScore > bestNeedScore)
                {
                    bestNeedScore = needScore;
                    bestShelter = shelter;
                }
            }
        }
        
        return bestShelter;
    }
    
    /// <summary>
    /// Manage population transportation from communities to shelters/motels
    /// </summary>
    void ManagePopulationTransportation()
    {
        if (!enableAutomaticFlow) return;
        
        PrebuiltBuilding[] communities = FindObjectsOfType<PrebuiltBuilding>().Where(pb => pb.GetPrebuiltType() == PrebuiltBuildingType.Community).ToArray();
        
        foreach (PrebuiltBuilding community in communities)
        {
            if (community.GetCurrentPopulation() <= 0) continue;
            
            // Check if we recently transported from this community
            if (lastPopulationTransport.ContainsKey(community) && 
                Time.time - lastPopulationTransport[community] < 15f) // Wait 15 seconds between transports
                continue;
            
            // Find best destination for population
            MonoBehaviour destination = FindBestPopulationDestination();
            
            if (destination != null)
            {
                int transportAmount = Mathf.Min(
                    community.GetCurrentPopulation(), 
                    maxPopulationPerTransport,
                    GetAvailablePopulationSpace(destination)
                );
                
                if (transportAmount >= minPopulationPerTransport)
                {
                    deliverySystem.CreateDeliveryTask(community, destination, ResourceType.Population, transportAmount, populationTransportPriority);
                    lastPopulationTransport[community] = Time.time;
                    
                    if (logResourceFlows)
                        Debug.Log($"Scheduled population transport: {transportAmount} people from {community.GetBuildingName()} to {destination.name}");
                }
            }
        }
    }
    
    /// <summary>
    /// Find the best destination for population (prefer shelters over motels)
    /// </summary>
    MonoBehaviour FindBestPopulationDestination()
    {
        // First, try to find available shelter space
        Building[] shelters = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        
        foreach (Building shelter in shelters)
        {
            if (GetAvailablePopulationSpace(shelter) >= minPopulationPerTransport)
            {
                return shelter;
            }
        }
        
        // If no shelter space, try motels
        PrebuiltBuilding[] motels = FindObjectsOfType<PrebuiltBuilding>().Where(pb => pb.GetPrebuiltType() == PrebuiltBuildingType.Motel).ToArray();
        
        foreach (PrebuiltBuilding motel in motels)
        {
            if (motel.CanAcceptPopulation(minPopulationPerTransport))
            {
                return motel;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Get available population space in a building (supports both Building and PrebuiltBuilding)
    /// </summary>
    int GetAvailablePopulationSpace(MonoBehaviour building)
    {
        // Try Building first
        Building buildingComponent = building.GetComponent<Building>();
        if (buildingComponent != null)
        {
            BuildingResourceStorage storage = buildingComponent.GetComponent<BuildingResourceStorage>();
            if (storage != null)
            {
                return storage.GetAvailableSpace(ResourceType.Population);
            }
        }
        
        // Try PrebuiltBuilding
        PrebuiltBuilding prebuilt = building.GetComponent<PrebuiltBuilding>();
        if (prebuilt != null)
        {
            return prebuilt.GetPopulationCapacity() - prebuilt.GetCurrentPopulation();
        }
        
        return 0;
    }
    
    /// <summary>
    /// Handle return home transportation (late game)
    /// </summary>
    void ManageReturnHomeFlow()
    {
        // This would be triggered by casework sites processing return requests
        // For now, we'll implement basic logic
        
        Building[] caseworkSites = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.CaseworkSite && b.IsOperational()).ToArray();
        
        if (caseworkSites.Length == 0) return;
        
        // Find people in shelters/motels who might want to return home
        // This is simplified - in a real game, you'd have more complex logic for return requests
        
        Building[] shelters = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        PrebuiltBuilding[] communities = FindObjectsOfType<PrebuiltBuilding>().Where(pb => pb.GetPrebuiltType() == PrebuiltBuildingType.Community).ToArray();
        
        foreach (Building shelter in shelters)
        {
            BuildingResourceStorage storage = shelter.GetComponent<BuildingResourceStorage>();
            if (storage == null) continue;
            
            int population = storage.GetResourceAmount(ResourceType.Population);
            
            // Simple logic: if shelter has been occupied for a while, some people might want to return
            // In reality, this would be triggered by game events or player decisions
            if (population > 5 && Random.Range(0f, 1f) < 0.1f) // 10% chance
            {
                PrebuiltBuilding targetCommunity = FindCommunityWithSpace(communities);
                if (targetCommunity != null)
                {
                    int returnAmount = Mathf.Min(2, population); // Small groups return
                    deliverySystem.CreateDeliveryTask(shelter, targetCommunity, ResourceType.Population, returnAmount, returnHomePriority);
                    
                    if (logResourceFlows)
                        Debug.Log($"Scheduled return home transport: {returnAmount} people from {shelter.name} to {targetCommunity.GetBuildingName()}");
                }
            }
        }
    }
    
    /// <summary>
    /// Find a community with available space
    /// </summary>
    PrebuiltBuilding FindCommunityWithSpace(PrebuiltBuilding[] communities)
    {
        foreach (PrebuiltBuilding community in communities)
        {
            if (community.CanAcceptPopulation(1))
            {
                return community;
            }
        }
        return null;
    }
    
    /// <summary>
    /// Balance usage between shelters and motels
    /// </summary>
    void BalanceShelterMotelUsage()
    {
        // If motels are full but shelters have space, move people from motels to shelters
        PrebuiltBuilding[] motels = FindObjectsOfType<PrebuiltBuilding>().Where(pb => pb.GetPrebuiltType() == PrebuiltBuildingType.Motel).ToArray();
        Building[] shelters = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        
        foreach (PrebuiltBuilding motel in motels)
        {
            if (motel.GetCurrentPopulation() > 0)
            {
                Building availableShelter = shelters.FirstOrDefault(s => GetAvailablePopulationSpace(s) >= minPopulationPerTransport);
                
                if (availableShelter != null)
                {
                    int transferAmount = Mathf.Min(motel.GetCurrentPopulation(), GetAvailablePopulationSpace(availableShelter), 3);
                    
                    deliverySystem.CreateDeliveryTask(motel, availableShelter, ResourceType.Population, transferAmount, populationTransportPriority + 1);
                    
                    if (logResourceFlows)
                        Debug.Log($"Balancing: Moving {transferAmount} people from {motel.GetBuildingName()} to {availableShelter.name}");
                }
            }
        }
    }
    
    /// <summary>
    /// Handle delivery completion events
    /// </summary>
    void OnDeliveryCompleted(DeliveryTask task)
    {
        switch (task.cargoType)
        {
            case ResourceType.FoodPacks:
                totalFoodDeliveries++;
                break;
            case ResourceType.Population:
                totalPopulationTransports++;
                break;
        }
        
        if (logResourceFlows)
            Debug.Log($"Delivery completed: {task}");
    }
    
    /// <summary>
    /// Log current resource flow status
    /// </summary>
    void LogResourceFlowStatus()
    {
        if (!logResourceFlows) return;
        
        int totalPopulation = 0;
        int totalFoodPacks = 0;
        
        // Count resources in all buildings
        Building[] allBuildings = FindObjectsOfType<Building>();
        PrebuiltBuilding[] allPrebuiltBuildings = FindObjectsOfType<PrebuiltBuilding>();
        
        foreach (Building building in allBuildings)
        {
            BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();
            if (storage != null)
            {
                totalPopulation += storage.GetResourceAmount(ResourceType.Population);
                totalFoodPacks += storage.GetResourceAmount(ResourceType.FoodPacks);
            }
        }
        
        foreach (PrebuiltBuilding prebuilt in allPrebuiltBuildings)
        {
            totalPopulation += prebuilt.GetCurrentPopulation();
        }
        
        Debug.Log($"Resource Flow Status - Population: {totalPopulation}, Food Packs: {totalFoodPacks}, Deliveries: {totalFoodDeliveries} food, {totalPopulationTransports} population");
    }
    
    /// <summary>
    /// Force immediate resource flow analysis
    /// </summary>
    [ContextMenu("Analyze Resource Flows")]
    public void ForceAnalyzeResourceFlows()
    {
        AnalyzeAndManageResourceFlows();
    }
    
    /// <summary>
    /// Get resource flow statistics
    /// </summary>
    public ResourceFlowStatistics GetResourceFlowStatistics()
    {
        ResourceFlowStatistics stats = new ResourceFlowStatistics();
        
        stats.totalFoodDeliveries = totalFoodDeliveries;
        stats.totalPopulationTransports = totalPopulationTransports;
        stats.totalReturnHomeTransports = totalReturnHomeTransports;
        
        // Calculate current resource distribution
        Building[] allBuildings = FindObjectsOfType<Building>();
        PrebuiltBuilding[] allPrebuiltBuildings = FindObjectsOfType<PrebuiltBuilding>();
        
        foreach (Building building in allBuildings)
        {
            BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();
            if (storage != null)
            {
                stats.totalPopulationInBuildings += storage.GetResourceAmount(ResourceType.Population);
                stats.totalFoodPacksInBuildings += storage.GetResourceAmount(ResourceType.FoodPacks);
            }
        }
        
        foreach (PrebuiltBuilding prebuilt in allPrebuiltBuildings)
        {
            if (prebuilt.GetPrebuiltType() == PrebuiltBuildingType.Community)
            {
                stats.populationInCommunities += prebuilt.GetCurrentPopulation();
            }
            else if (prebuilt.GetPrebuiltType() == PrebuiltBuildingType.Motel)
            {
                stats.populationInMotels += prebuilt.GetCurrentPopulation();
            }
        }
        
        return stats;
    }
    
    [ContextMenu("Print Resource Flow Statistics")]
    public void DebugPrintResourceFlowStats()
    {
        ResourceFlowStatistics stats = GetResourceFlowStatistics();
        
        Debug.Log("=== RESOURCE FLOW STATISTICS ===");
        Debug.Log($"Total Deliveries - Food: {stats.totalFoodDeliveries}, Population: {stats.totalPopulationTransports}, Return Home: {stats.totalReturnHomeTransports}");
        Debug.Log($"Population Distribution - Communities: {stats.populationInCommunities}, Buildings: {stats.totalPopulationInBuildings}, Motels: {stats.populationInMotels}");
        Debug.Log($"Food Packs in Buildings: {stats.totalFoodPacksInBuildings}");
    }
}

[System.Serializable]
public class ResourceFlowStatistics
{
    public int totalFoodDeliveries;
    public int totalPopulationTransports;
    public int totalReturnHomeTransports;
    
    public int totalPopulationInBuildings;
    public int totalFoodPacksInBuildings;
    public int populationInCommunities;
    public int populationInMotels;
}