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
    // Does this choice deliver in the SAME round, or queue a delivery that lands later?
    // The distinction decides whether a task is fulfilled at all: a deferred delivery can
    // arrive after its task has already resolved, at which point it is credited by the
    // late-delivery path with different capping rules. Non-LLM policies and the cora_sim
    // surrogate previously had to infer this from choiceText ("(immediate)", "Rapid
    // Response"), which is right for food and wrong for lodging.
    public bool immediateDelivery;
    public bool triggersDelivery;
    // Whether the choice can be carried out right now, by the same check that greys it out in
    // the player's task panel (TaskDetailUI.IsChoiceFeasibleFor): no space, no free vehicle,
    // every route flooded, no kitchen with food. A player cannot pick an unavailable choice;
    // without this an agent saw it as an ordinary option and had it refused on confirm.
    public bool feasible = true;
    public string unavailableReason;
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
    // Time-delayed effects the player sees in the Pending Actions panel (and a little more):
    // approved funding not yet paid, workers arriving or finishing training, construction still
    // under way. Without it an officer could see "2 in training" but never "ready in 1 day",
    // or approve funding and then lose track of it.
    public List<PendingEffect> pendingEffects;
    // A compact summary of each finished day's report, so an officer can explain what moved
    // the score instead of saying it cannot see the daily report.
    public List<DailyReportSummary> dailyReports;
    // The motel's per-person daily rate (MotelCostManager), so "cost per bed" is not a guess.
    public float motelCostPerPersonPerDay;
    // Which scenario this game is: map fingerprint and source, parameter source, RNG seed. A
    // benchmark or RL record carries it so results from different maps or parameter sheets are
    // never pooled by accident.
    public ScenarioInfo scenario;
}

[System.Serializable]
public class ScenarioInfo
{
    public string mapHash;       // GameConfigLoader.MapHash ("" when the built-in layout is used)
    public string mapStatus;     // loaded | default | unreachable | invalid
    public string mapUrl;
    public string paramSource;   // e.g. the CSV path, or "ARC_PARAM_CONFIG=..."
    public int seed = -1;        // GymServerManager.ActiveSeed; -1 = unseeded
}

[System.Serializable]
public class PendingEffect
{
    public string kind;          // funding | workers_arriving | training | construction
    public string description;   // human-readable label (funding source, worker type, building)
    public int amount;           // money, for funding
    public int quantity;         // workers, for workers_arriving / training
    public string target;        // building name, for construction
    public int roundsRemaining;  // -1 when the effect is scheduled by day instead
    public int daysRemaining;    // -1 when the effect is scheduled by round instead
}

[System.Serializable]
public class DailyReportSummary
{
    public int day;
    public int completedTasks, totalTasks, expiredTasks;
    public int foodProduced, foodDelivered, foodWasted;
    public float shelterOccupancyRate;
    public int idleWorkers;
    public float startingBudget, budgetSpent, budgetReceived, endingBudget;
    public float satisfactionChange;
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

    // ── Unity's own score, exported verbatim (the formula humans see) ─────────────────
    // reward_scoring.py reads these instead of re-implementing DailyReportData's math, so
    // the RL reward, the benchmark and the router all score exactly what the daily report
    // shows a human — and follow automatically if that formula changes. Component ratios
    // are the raw 0..1 S_*/C_* values; the totals are Unity's 0..1000 scale.
    public bool scoreAvailable;          // false before DailyReportData exists
    public float liveSatisfaction;       // SatisfactionAndBudget.currentSatisfaction (0..1000 scale)
    public float liveEfficiency;         // SatisfactionAndBudget.currentEfficiency   (0..1000 scale)
    public float sFood, sLodging, sWorkerUse, sWaste, sCasework;   // DailyReportData.S_*()
    public float cFood, cLodging, cWorker;                         // DailyReportData.C_*()  (higher = better)
    public float satisfactionComponentsTotal;   // ComputeFreshSatisfactionTotal()
    public float efficiencyComponentsTotal;     // ComputeFreshEfficiencyTotal()
    // The raw counters behind the ratios, so a run can be re-scored offline.
    public int foodPacksConsumed, foodPacksNeeded, foodPacksWasted;
    public int lodgingNightsConsumed, lodgingNightsNeeded;
    public int clientRoundsAwaitingCasework, clientsRequestedCasework;
    public int idleWorkerRounds, workingWorkerRounds, trainingWorkerRounds;
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
    public int roundsPerDay;     // so observers can count the rounds left without assuming 4
    public bool isGameOver;
}

[System.Serializable]
public class SatisfactionAndBudgetState
{
    public float efficiency;   // live efficiency (0..1000) — was absent, so no consumer could see it
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
    // People already on their way here (vehicle deliveries reserved + clients walking in). The
    // game counts these against free space when it decides whether to offer a shelter choice,
    // so a shelter can read 0/100 and still be full. Same functions the game uses.
    public int incomingPopulation;
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
public class PendingRelocation
{
    public int taskId;
    public string source;
    public string destination;
    public int quantity;
    public int roundsRemaining;
}

[System.Serializable]
public class Logistics
{
    public List<PendingRelocation> pendingRelocations;   // clients walking (main-bugfixes self-walk relocation)
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
    // Live value from BuildingSystem (serialized in MainScene, currently 2000) — NOT the 1000
    // default declared in BuildingSystem.cs. Do not restate this number in prompts or docs;
    // it is sent to the model each round as state.costs.build.
    public int buildingConstructionCost;
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
