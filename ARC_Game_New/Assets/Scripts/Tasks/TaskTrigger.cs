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

[System.Serializable]
public class FloodTileTrigger : TaskTrigger
{
    [Header("Flood Condition")]
    public FloodConditionType conditionType = FloodConditionType.CurrentFloodTiles;
    public ComparisonType comparison = ComparisonType.ExactMatch;
    public int targetValue = 5;

    [Header("Change Detection")]
    public int previousFloodCount = 0; // Tracks previous count for change detection

    public enum FloodConditionType
    {
        CurrentFloodTiles,      // Current number of flood tiles
        FloodExpanded,          // Flood expanded by N tiles
        FloodShrank,            // Flood shrank by N tiles  
        FloodChanged            // Flood changed by N tiles (+ or -)
    }

    public enum ComparisonType
    {
        ExactMatch,     // == targetValue
        MoreThan,       // > targetValue
        LessThan,       // < targetValue
        AtLeast,        // >= targetValue
        AtMost          // <= targetValue
    }

    public override bool CheckCondition()
    {
        if (FloodSystem.Instance == null) return false;

        int currentFloodCount = FloodSystem.Instance.GetFloodTileCount();

        switch (conditionType)
        {
            case FloodConditionType.CurrentFloodTiles:
                return CheckComparison(currentFloodCount, targetValue);

            case FloodConditionType.FloodExpanded:
                int expansion = currentFloodCount - previousFloodCount;
                bool expandedMatch = expansion > 0 && CheckComparison(expansion, targetValue);
                previousFloodCount = currentFloodCount; // Update for next check
                return expandedMatch;

            case FloodConditionType.FloodShrank:
                int shrinkage = previousFloodCount - currentFloodCount;
                bool shrunkMatch = shrinkage > 0 && CheckComparison(shrinkage, targetValue);
                previousFloodCount = currentFloodCount;
                return shrunkMatch;

            case FloodConditionType.FloodChanged:
                int change = currentFloodCount - previousFloodCount;
                bool changedMatch = change != 0 && CheckComparison(Mathf.Abs(change), targetValue);
                previousFloodCount = currentFloodCount;
                return changedMatch;

            default:
                return false;
        }
    }

    bool CheckComparison(int actualValue, int targetValue)
    {
        switch (comparison)
        {
            case ComparisonType.ExactMatch:
                return actualValue == targetValue;
            case ComparisonType.MoreThan:
                return actualValue > targetValue;
            case ComparisonType.LessThan:
                return actualValue < targetValue;
            case ComparisonType.AtLeast:
                return actualValue >= targetValue;
            case ComparisonType.AtMost:
                return actualValue <= targetValue;
            default:
                return false;
        }
    }

    public override string GetDescription()
    {
        string comparisonText = GetComparisonText();

        switch (conditionType)
        {
            case FloodConditionType.CurrentFloodTiles:
                return $"Current flood tiles {comparisonText} {targetValue}";
            case FloodConditionType.FloodExpanded:
                return $"Flood expanded {comparisonText} {targetValue} tiles";
            case FloodConditionType.FloodShrank:
                return $"Flood shrank {comparisonText} {targetValue} tiles";
            case FloodConditionType.FloodChanged:
                return $"Flood changed {comparisonText} {targetValue} tiles";
            default:
                return "Unknown flood condition";
        }
    }

    string GetComparisonText()
    {
        switch (comparison)
        {
            case ComparisonType.ExactMatch: return "exactly";
            case ComparisonType.MoreThan: return "more than";
            case ComparisonType.LessThan: return "less than";
            case ComparisonType.AtLeast: return "at least";
            case ComparisonType.AtMost: return "at most";
            default: return "";
        }
    }
}


[System.Serializable]
public class FloodedFacilityTrigger : TaskTrigger
{
    [Header("Facility Flood Condition")]
    public FacilityFloodType facilityType = FacilityFloodType.AnyFacility;
    public ComparisonType comparison = ComparisonType.AtLeast;
    public int floodTileThreshold = 1;
    
    [Header("Specific Facility Types")]
    public BuildingType specificBuildingType = BuildingType.Shelter;
    public PrebuiltBuildingType specificPrebuiltType = PrebuiltBuildingType.Community;
    
    [Header("Flood Detection Range")]
    public int detectionRadius = 2; // How far from facility to check for flood
    
    public enum FacilityFloodType
    {
        AnyFacility,            // Any building or prebuilt
        AnyBuilding,            // Any constructed building
        AnyPrebuilt,            // Any prebuilt building
        SpecificBuildingType,   // Specific building type (e.g. Shelter)
        SpecificPrebuiltType    // Specific prebuilt type (e.g. Community)
    }
    
    public enum ComparisonType
    {
        ExactMatch,     // == threshold
        AtLeast,        // >= threshold
        MoreThan,       // > threshold
        LessThan,       // < threshold
        AtMost          // <= threshold
    }
    
    public override bool CheckCondition()
    {
        if (FloodSystem.Instance == null) return false;
        
        List<MonoBehaviour> targetFacilities = GetTargetFacilities();
        
        foreach (MonoBehaviour facility in targetFacilities)
        {
            int floodTilesNearFacility = CountFloodTilesNearFacility(facility);
            
            if (CheckComparison(floodTilesNearFacility, floodTileThreshold))
            {
                return true; // At least one facility meets the condition
            }
        }
        
        return false; // No facilities meet the condition
    }
    
    List<MonoBehaviour> GetTargetFacilities()
    {
        List<MonoBehaviour> facilities = new List<MonoBehaviour>();
        
        switch (facilityType)
        {
            case FacilityFloodType.AnyFacility:
                facilities.AddRange(Object.FindObjectsOfType<Building>().Cast<MonoBehaviour>());
                facilities.AddRange(Object.FindObjectsOfType<PrebuiltBuilding>().Cast<MonoBehaviour>());
                break;
                
            case FacilityFloodType.AnyBuilding:
                facilities.AddRange(Object.FindObjectsOfType<Building>().Cast<MonoBehaviour>());
                break;
                
            case FacilityFloodType.AnyPrebuilt:
                facilities.AddRange(Object.FindObjectsOfType<PrebuiltBuilding>().Cast<MonoBehaviour>());
                break;
                
            case FacilityFloodType.SpecificBuildingType:
                Building[] buildings = Object.FindObjectsOfType<Building>();
                facilities.AddRange(buildings.Where(b => b.GetBuildingType() == specificBuildingType).Cast<MonoBehaviour>());
                break;
                
            case FacilityFloodType.SpecificPrebuiltType:
                PrebuiltBuilding[] prebuilts = Object.FindObjectsOfType<PrebuiltBuilding>();
                facilities.AddRange(prebuilts.Where(p => p.GetPrebuiltType() == specificPrebuiltType).Cast<MonoBehaviour>());
                break;
        }
        
        return facilities;
    }
    
    int CountFloodTilesNearFacility(MonoBehaviour facility)
    {
        if (facility == null) return 0;
        
        Vector3 facilityWorldPos = facility.transform.position;
        int floodCount = 0;
        
        // Check flood tiles within detection radius
        for (int x = -detectionRadius; x <= detectionRadius; x++)
        {
            for (int y = -detectionRadius; y <= detectionRadius; y++)
            {
                Vector3 checkPos = facilityWorldPos + new Vector3(x, y, 0);
                
                if (FloodSystem.Instance.IsFloodedAt(checkPos))
                {
                    floodCount++;
                }
            }
        }
        
        return floodCount;
    }
    
    bool CheckComparison(int actualValue, int threshold)
    {
        switch (comparison)
        {
            case ComparisonType.ExactMatch:
                return actualValue == threshold;
            case ComparisonType.AtLeast:
                return actualValue >= threshold;
            case ComparisonType.MoreThan:
                return actualValue > threshold;
            case ComparisonType.LessThan:
                return actualValue < threshold;
            case ComparisonType.AtMost:
                return actualValue <= threshold;
            default:
                return false;
        }
    }
    
    public override string GetDescription()
    {
        string facilityText = GetFacilityTypeText();
        string comparisonText = GetComparisonText();
        
        return $"{facilityText} with {comparisonText} {floodTileThreshold} flood tiles nearby";
    }
    
    string GetFacilityTypeText()
    {
        switch (facilityType)
        {
            case FacilityFloodType.AnyFacility: return "Any facility";
            case FacilityFloodType.AnyBuilding: return "Any building";
            case FacilityFloodType.AnyPrebuilt: return "Any prebuilt";
            case FacilityFloodType.SpecificBuildingType: return $"{specificBuildingType}";
            case FacilityFloodType.SpecificPrebuiltType: return $"{specificPrebuiltType}";
            default: return "Unknown facility";
        }
    }
    
    string GetComparisonText()
    {
        switch (comparison)
        {
            case ComparisonType.ExactMatch: return "exactly";
            case ComparisonType.AtLeast: return "at least";
            case ComparisonType.MoreThan: return "more than";
            case ComparisonType.LessThan: return "less than";
            case ComparisonType.AtMost: return "at most";
            default: return "";
        }
    }
}