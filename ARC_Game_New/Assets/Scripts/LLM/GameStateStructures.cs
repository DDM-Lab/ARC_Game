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
    public string stableTaskId;
    public string taskTitle;
    public string taskDescription;
    public string taskType;
    public string affectedFacility;
    public int roundsRemaining;
    // Choices the task offers, if any (bare minimum: id + text). Select one via
    // the gym 'select_task_choice' action / the UI. Empty for non-choice tasks.
    public List<TaskChoiceBrief> choices;
}

[System.Serializable]
public class TaskChoiceBrief
{
    public int choiceId;
    public string choiceText;
    // Sparse: only the choice's non-zero impacts (e.g. Budget +5000, Satisfaction +10,
    // Budget -2000 cost). Always populated in the payload; the Python observation layer
    // decides whether to surface it to the model (ablation toggle).
    public List<ChoiceImpactBrief> impacts;
    // Structured delivery destination — avoids choiceText parsing in non-LLM policies.
    // "Motel" | "Shelter" | "CaseworkSite" | "Kitchen" | ... for delivery choices; null otherwise.
    public string destinationCategory;
    // Expected delivery quantity (people for relocation, food units, etc.).
    // Only meaningful when destinationCategory is non-null.
    public int deliveryQuantity;
}

[System.Serializable]
public class ChoiceImpactBrief
{
    public string type;   // ImpactType name (Budget, Satisfaction, Clients, ...)
    public int value;     // signed: positive = gain (e.g. funding), negative = cost
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
    public RewardMetrics rewardMetrics;
}

// Raw cumulative quantities for the (Python-side) reward function. Unity reports
// facts only; scoring/weighting/clamping happens in Python.
[System.Serializable]
public class RewardMetrics
{
    // Needs-met (Food/Lodging Demand/Emergency tasks): fulfilled / resolved
    public int foodResolved;
    public int foodFulfilled;
    public int lodgingResolved;
    public int lodgingFulfilled;
    // Casework / return-home (people who requested casework vs people actually processed home)
    public int caseworkRequested;
    public int caseworkProcessed;
    // Worker allocation summed across rounds (person-rounds)
    public long cumWorkingWorkers;
    public long cumTrainingWorkers;
    public long cumIdleWorkers;
    public int roundsCompleted;
    public int daysCompleted;
    public int totalWorkers;        // current present workforce
    // Cumulative spend by service category
    public int foodSpend;
    public int lodgingSpend;
    public int workerSpend;
    public int caseworkSpend;
}

[System.Serializable]
public class SessionInfo
{
    public int currentDay;
    public int currentRound;
    public string currentGameTime;
    public float simulationSpeed;
    public bool isPaused;
    // Finite-horizon terminal signal for the gym: the game ends after finalDay's last
    // round (EndGamePanel shows at Day finalDay, Round 4). isGameOver lets the Python
    // env terminate the episode there instead of advancing into meaningless Day 9+.
    public int finalDay;
    public bool isGameOver;
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
