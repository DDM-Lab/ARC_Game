// TaskTrigger.cs - Base class for all trigger conditions

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

[System.Serializable]
public abstract class TaskTrigger
{
    public abstract bool CheckCondition();
    public abstract string GetDescription();
}

[System.Serializable]
public class RoundTrigger : TaskTrigger
{
    [Header("Round Condition")]
    public int targetRound = 1;
    public bool exactMatch = true; // true = equals, false = greater than or equal
    
    public override bool CheckCondition()
    {
        if (GlobalClock.Instance == null) return false;
        
        int currentRound = GlobalClock.Instance.GetCurrentTimeSegment();
        return exactMatch ? currentRound == targetRound : currentRound >= targetRound;
    }
    
    public override string GetDescription()
    {
        return exactMatch ? $"Round == {targetRound}" : $"Round >= {targetRound}";
    }
}

[System.Serializable]
public class PopulationTrigger : TaskTrigger
{
    [Header("Population Condition")]
    public BuildingType facilityType = BuildingType.Shelter;
    public int populationThreshold = 0;
    public PopulationCondition condition = PopulationCondition.Empty;
    
    public enum PopulationCondition
    {
        Empty,      // population == 0
        Full,       // population >= capacity
        HasPeople,  // population > 0
        LessThan,   // population < threshold
        MoreThan    // population > threshold
    }
    
    public override bool CheckCondition()
    {
        // Find facilities of specified type
        Building[] buildings = Object.FindObjectsOfType<Building>();
        
        foreach (Building building in buildings)
        {
            if (building.GetBuildingType() == facilityType && building.IsOperational())
            {
                BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();
                if (storage == null) continue;
                
                int currentPopulation = storage.GetResourceAmount(ResourceType.Population);
                int capacity = storage.GetResourceCapacity(ResourceType.Population);
                
                switch (condition)
                {
                    case PopulationCondition.Empty:
                        if (currentPopulation == 0) return true;
                        break;
                    case PopulationCondition.Full:
                        if (currentPopulation >= capacity) return true;
                        break;
                    case PopulationCondition.HasPeople:
                        if (currentPopulation > 0) return true;
                        break;
                    case PopulationCondition.LessThan:
                        if (currentPopulation < populationThreshold) return true;
                        break;
                    case PopulationCondition.MoreThan:
                        if (currentPopulation > populationThreshold) return true;
                        break;
                }
            }
        }
        
        return false;
    }
    
    public override string GetDescription()
    {
        return $"{facilityType} population {condition}" + 
               (condition == PopulationCondition.LessThan || condition == PopulationCondition.MoreThan 
                ? $" {populationThreshold}" : "");
    }
}

[System.Serializable]
public class ResourceTrigger : TaskTrigger
{
    [Header("Resource Condition")]
    public BuildingType facilityType = BuildingType.Kitchen;
    public ResourceType resourceType = ResourceType.FoodPacks;
    public int resourceThreshold = 0;
    public ResourceCondition condition = ResourceCondition.Empty;
    
    public enum ResourceCondition
    {
        Empty,      // resource == 0
        Full,       // resource >= capacity
        LessThan,   // resource < threshold
        MoreThan    // resource > threshold
    }
    
    public override bool CheckCondition()
    {
        Building[] buildings = Object.FindObjectsOfType<Building>();
        
        foreach (Building building in buildings)
        {
            if (building.GetBuildingType() == facilityType && building.IsOperational())
            {
                BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();
                if (storage == null) continue;
                
                int currentResource = storage.GetResourceAmount(resourceType);
                int capacity = storage.GetResourceCapacity(resourceType);
                
                switch (condition)
                {
                    case ResourceCondition.Empty:
                        if (currentResource == 0) return true;
                        break;
                    case ResourceCondition.Full:
                        if (currentResource >= capacity) return true;
                        break;
                    case ResourceCondition.LessThan:
                        if (currentResource < resourceThreshold) return true;
                        break;
                    case ResourceCondition.MoreThan:
                        if (currentResource > resourceThreshold) return true;
                        break;
                }
            }
        }
        
        return false;
    }
    
    public override string GetDescription()
    {
        return $"{facilityType} {resourceType} {condition}" +
               (condition == ResourceCondition.LessThan || condition == ResourceCondition.MoreThan 
                ? $" {resourceThreshold}" : "");
    }
}

[System.Serializable]
public class ProbabilityTrigger : TaskTrigger
{
    [Header("Probability Condition")]
    [Range(0f, 1f)]
    public float probability = 0.5f; // 50% chance
    
    public override bool CheckCondition()
    {
        return Random.Range(0f, 1f) < probability;
    }
    
    public override string GetDescription()
    {
        return $"{probability * 100:F0}% chance";
    }
}

[System.Serializable]
public class FloodTrigger : TaskTrigger
{
    [Header("Flood Condition")]
    public FloodCondition condition = FloodCondition.FloodExists;
    public int floodTileThreshold = 5;
    
    public enum FloodCondition
    {
        FloodExists,        // Any flood tiles exist
        FloodAboveThreshold, // Flood tiles > threshold
        NoFloodTiles        // No flood tiles exist
    }
    
    public override bool CheckCondition()
    {
        if (FloodSystem.Instance == null) return false;
        
        int currentFloodTiles = FloodSystem.Instance.GetFloodTileCount();
        
        switch (condition)
        {
            case FloodCondition.FloodExists:
                return currentFloodTiles > 0;
            case FloodCondition.FloodAboveThreshold:
                return currentFloodTiles > floodTileThreshold;
            case FloodCondition.NoFloodTiles:
                return currentFloodTiles == 0;
            default:
                return false;
        }
    }
    
    public override string GetDescription()
    {
        switch (condition)
        {
            case FloodCondition.FloodExists:
                return "Flood tiles exist";
            case FloodCondition.FloodAboveThreshold:
                return $"Flood tiles > {floodTileThreshold}";
            case FloodCondition.NoFloodTiles:
                return "No flood tiles";
            default:
                return "Unknown flood condition";
        }
    }
}

[System.Serializable]
public class VehicleDamagedTrigger : TaskTrigger
{
    [Header("Vehicle Damage Condition")]
    public int minimumDamagedVehicles = 1;
    
    public override bool CheckCondition()
    {
        Vehicle[] vehicles = Object.FindObjectsOfType<Vehicle>();
        int damagedCount = 0;
        
        foreach (Vehicle vehicle in vehicles)
        {
            if (vehicle.GetCurrentStatus() == VehicleStatus.Damaged)
            {
                damagedCount++;
            }
        }
        
        return damagedCount >= minimumDamagedVehicles;
    }
    
    public override string GetDescription()
    {
        return $"At least {minimumDamagedVehicles} vehicles damaged";
    }
}

[System.Serializable]
public class FloodBlockedRouteTrigger : TaskTrigger
{
    [Header("Route Blocking")]
    public BuildingType sourceType = BuildingType.Kitchen;
    public BuildingType destinationType = BuildingType.Shelter;
    
    public override bool CheckCondition()
    {
        if (FloodSystem.Instance == null) return false;
        
        // Find buildings of specified types
        Building[] sources = Object.FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == sourceType && b.IsOperational()).ToArray();
        Building[] destinations = Object.FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == destinationType && b.IsOperational()).ToArray();
        
        // Check if any routes are blocked
        foreach (Building source in sources)
        {
            foreach (Building dest in destinations)
            {
                if (!FloodSystem.Instance.IsRouteClearOfFlood(source.transform.position, dest.transform.position))
                {
                    return true; // At least one route is blocked
                }
            }
        }
        
        return false; // All routes are clear
    }
    
    public override string GetDescription()
    {
        return $"Flood blocking {sourceType} to {destinationType} routes";
    }
}