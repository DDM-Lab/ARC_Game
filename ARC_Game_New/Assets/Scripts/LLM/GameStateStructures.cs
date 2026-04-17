using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Game state serialization classes for LLM integration
/// These structures enable sending comprehensive game state to the LLM server
/// </summary>

[System.Serializable]
public class TaskContext
{
    public int taskId;
    public string taskTitle;
    public string taskDescription;
    public string taskType;
    public string affectedFacility;
    public int roundsRemaining;
}

[System.Serializable]
public class GameStatePayload
{
    public SessionInfo sessionInfo;
    public SatisfactionAndBudgetState satisfactionAndBudget;
    public TaskContext taskContext;
    public List<TaskContext> allActiveTasks;
    public MapState mapState;
    public EnvironmentalConditions environmentalConditions;
    public DistributedResources distributedResources;
    public Logistics logistics;
    public DailyMetrics dailyMetrics;
    public WorkforceState workforceState;
    public ConstructionState constructionState;
}

[System.Serializable]
public class SessionInfo
{
    public int currentDay;
    public int currentRound;
    public string currentGameTime;
    public float simulationSpeed;
    public bool isPaused;
}

[System.Serializable]
public class SatisfactionAndBudgetState
{
    public int satisfaction;
    public int budget;
}

[System.Serializable]
public class MapState
{
    public List<FacilityState> facilities;
    public List<VehicleState> vehicles;
    public int totalPopulation;
    public FloodState floodState;
    public List<AbandonedSiteState> abandonedSites;
}

[System.Serializable]
public class FacilityState
{
    public string facilityName;
    public string facilityType; // "Building" or "Prebuilt"
    public string buildingType; // Kitchen, Shelter, etc.
    public bool isOperational;
    public ResourceInventory resources;
    public int currentPopulation;
    public int populationCapacity;
    public Vector3Serializable position;
    public string buildingStatus; // UnderConstruction, NeedWorker, InUse, Disabled
    public int assignedWorkforce; // Current workforce assigned
    public int requiredWorkforce; // Usually 4
    public int originalSiteId; // ID of the abandoned site this building was built on
}

[System.Serializable]
public class ResourceInventory
{
    public int foodPacks;
    public int foodPacksCapacity;
    public int population;
    public int populationCapacity;
    public int untrainedWorkers;
    public int trainedWorkers;
}

[System.Serializable]
public class VehicleState
{
    public string vehicleName;
    public string vehicleStatus; // Available, InTransit, Damaged
    public int currentCapacity;
    public int maxCapacity;
    public string currentCargo;
    public string currentTask; // Description of active delivery
}

[System.Serializable]
public class FloodState
{
    public bool isActive;
    public int affectedRoads;
    public List<string> blockedRoutes;
    public float waterLevel;
}

[System.Serializable]
public class EnvironmentalConditions
{
    public string weatherCondition; // Clear, Rain, Storm
    public bool isFlooding;
    public int damagedVehicles;
    public int blockedRoads;
}

[System.Serializable]
public class DistributedResources
{
    public int totalFoodDistributed;
    public int totalPopulationRelocated;
    public int activeDeliveryTasks;
    public int completedDeliveryTasks;
    public int failedDeliveryTasks;
}

[System.Serializable]
public class Logistics
{
    public int availableVehicles;
    public int vehiclesInTransit;
    public int damagedVehicles;
    public List<ActiveDelivery> activeDeliveries;
}

[System.Serializable]
public class ActiveDelivery
{
    public int deliveryId;
    public string cargoType;
    public int quantity;
    public string source;
    public string destination;
    public string status;
    public float progress; // 0.0 to 1.0
}

[System.Serializable]
public class DailyMetrics
{
    public int currentSatisfaction;
    public int currentBudget;
    public int tasksCompleted;
    public int tasksExpired;
    public int tasksIncomplete;
    public int activeTasks;
}

/// <summary>
/// Helper class to serialize Vector3 (since Unity's Vector3 doesn't serialize well to JSON)
/// </summary>
[System.Serializable]
public class Vector3Serializable
{
    public float x;
    public float y;
    public float z;

    public Vector3Serializable(Vector3 vector)
    {
        x = vector.x;
        y = vector.y;
        z = vector.z;
    }

    public Vector3 ToVector3()
    {
        return new Vector3(x, y, z);
    }
}

/// <summary>
/// LLM-generated task content response structure
/// </summary>
[System.Serializable]
public class LLMTaskContentResponse
{
    public bool success;
    public string error;
    public LLMTaskContent result;
    public float inference_time;
    public string timestamp;
}

[System.Serializable]
public class LLMTaskContent
{
    public int taskId;
    public List<string> messages;
    public List<LLMAgentChoice> choices;
    public List<LLMNumericalInput> numericalInputs;
}

[System.Serializable]
public class LLMAgentChoice
{
    public int choiceId;
    public string choiceText;
    public string agentReasoning;
    public float confidence;
    public List<LLMImpact> impacts;
    public LLMDelivery delivery;
}

[System.Serializable]
public class LLMImpact
{
    public string type; // "Satisfaction", "Budget", "FoodPacks", etc.
    public int value;
}

[System.Serializable]
public class LLMDelivery
{
    public bool triggers;
    public string cargoType; // "FoodPacks", "Population"
    public int quantity;
    public string sourceType; // "AutoFind", "SpecificBuilding", etc.
    public string destinationType;
}

[System.Serializable]
public class LLMNumericalInput
{
    public int inputId;
    public string inputLabel;
    public string inputType; // "Budget", "Clients", "Workers", "FoodPacks"
    public int minValue;
    public int maxValue;
    public int defaultValue;
    public int stepSize;
}

/// <summary>
/// Complete worker system state for action generation
/// </summary>
[System.Serializable]
public class WorkforceState
{
    public int freeTrainedWorkers;
    public int freeUntrainedWorkers;
    public int workingTrainedWorkers;
    public int workingUntrainedWorkers;
    public int trainedWorkersNotArrived;
    public int untrainedWorkersNotArrived;
    public int untrainedWorkersInTraining;
    public int totalTrainedWorkers;
    public int totalUntrainedWorkers;
    public int totalAvailableWorkforce; // Trained * 2 + Untrained * 1
    public int totalWorkforceCapacity;
    public int untrainedWorkerCost; // $100
    public int trainedWorkerCost; // $500
    public int trainingCostPerWorker; // $50
    public int trainingDurationDays; // 3 days
    public int newWorkersHiredToday; // Daily limit tracking
}

/// <summary>
/// Building construction and site state
/// </summary>
[System.Serializable]
public class ConstructionState
{
    public List<AbandonedSiteState> availableSites;
    public List<string> buildingsUnderConstruction;
    public List<string> buildingsNeedingWorkers;
    public int buildingConstructionCost; // $1000
    public float constructionTimeDays;
    public float deconstructionTimeDays;
}

/// <summary>
/// Available construction site information
/// </summary>
[System.Serializable]
public class AbandonedSiteState
{
    public int siteId;
    public string siteName;
    public bool isAvailable;
    public Vector3Serializable position;
}
