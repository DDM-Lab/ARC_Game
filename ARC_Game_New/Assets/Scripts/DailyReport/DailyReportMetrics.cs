using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data class storing all metrics for a single day's report.
/// 
/// === METRIC FLOW ===
/// 1. BASE METRICS: Populated by DailyReportData.GenerateDailyReport() from live game state
/// 2. CALCULATED SCORES: Populated by DailyReportUI.SaveCompletedReportToHistory() AFTER animations finish
/// 3. HISTORICAL: Once saved, these values are frozen and used for historical display (no recalculation)
///
/// === SATISFACTION SCORE FORMULA ===
/// Food Satisfaction = foodCompletionBonus + foodOnTimeBonus + foodDelayScore
///   - foodCompletionBonus = completedFoodTasks * 2.0
///   - foodOnTimeBonus     = (completedFoodTasks - expiredFoodDemandTasks) * 1.5  [FIXED: was using totalFoodTasks]
///   - foodDelayScore      = -expiredFoodDemandTasks * 5.0
///
/// Lodging Satisfaction = lodgingCompletionBonus + lodgingOverstayPenalty
///   - lodgingCompletionBonus = completedLodgingTasks * 2.0
///   - lodgingOverstayPenalty = -groupsOver48Hours * 5.0
///
/// Worker Satisfaction = workerTaskBonus + workerIdleRatePenalty
///   - workerTaskBonus       = tasksCompletedByWorkers * 1.5
///   - workerIdleRatePenalty  = -idleWorkerRate * 0.1
///
/// Total Satisfaction Change = Food + Lodging + Worker satisfaction totals
/// Final Satisfaction = Clamp(previousSatisfaction + change, 0, 100)
///
/// === EFFICIENCY SCORE FORMULA ===
/// Kitchen Efficiency  = -expiredFoodPacks * 2.0     (penalty for wasted food)
/// Shelter Efficiency  = -vacantShelterSlots * 0.5   (penalty for empty beds)
/// Worker Efficiency   = -idleWorkers * 1.5          (penalty for idle workers)
/// Budget Efficiency   = (70 - budgetUsageRate) * 0.2 (positive if under 70% usage)
///
/// Total Efficiency Change = sum of all four efficiency scores
/// Final Efficiency = Clamp(previousEfficiency + change, 0, 100)
/// </summary>
[System.Serializable]
public class DailyReportMetrics
{
    // ============================================================
    // BASE METRICS - Populated by DailyReportData.GenerateDailyReport()
    // ============================================================

    [Header("Task Completion")]
    public int totalTasks;              // All unique tasks today (excluding Alert/Other/Advisory)
    public int completedTasks;          // Tasks completed today
    public int expiredTasks;            // Tasks that expired today
    public int totalDeliveryTasks;      // Delivery tasks from DeliverySystem
    public int completedDeliveryTasks;  // Completed deliveries
    
    [Header("Task Details by Type")]
    public int totalFoodTasks;          // Tasks with "food" in title/description
    public int completedFoodTasks;      // Completed food tasks
    public int expiredFoodDemandTasks;  // Expired food demand tasks (used for delay penalty)
    public int totalLodgingTasks;       // Tasks with "relocation" in title/description
    public int completedLodgingTasks;   // Completed lodging tasks
    
    [Header("Resource Metrics")]
    public int foodProduced;            // Food produced today (kitchen count * base per round)
    public int foodDelivered;           // Food delivered via DeliverySystem
    public int foodConsumed;            // Calculated: produced - storage - wasted
    public int foodWasted;              // Manually tracked via RecordFoodWasted()
    public int wastedFoodPacks;         // Same as foodWasted (compatibility alias)
    public int expiredFoodPacks;        // Manually tracked via RecordExpiredFood()
    public int currentFoodInStorage;    // Current food across all buildings
    public float mealUsageRate;         // (produced - wasted) / produced * 100
    
    [Header("Population Metrics")]
    public int totalPopulation;         // Current pop in shelters + communities
    public int newArrivals;             // Tracked via RecordNewArrival()
    public int departures;              // Tracked via RecordDeparture()
    public int overstayingClientGroups; // From ClientStayTracker
    public int groupsOver48Hours;       // From ClientStayTracker (used for penalty)
    public float shelterOccupancyRate;  // occupied / capacity * 100
    public float shelterUtilizationRate;// Same as shelterOccupancyRate (compatibility alias)
    public int vacantShelterSlots;      // Empty shelter beds (used for efficiency penalty)
    
    [Header("Worker Metrics")]
    public int totalWorkers;            // From WorkerSystem stats
    public int workingWorkers;          // trainedWorking + untrainedWorking
    public int idleWorkers;             // trainedFree + untrainedFree
    public int totalIdleWorkers;        // Same as idleWorkers (compatibility alias)
    public int trainedWorkers;          // Total trained workers
    public int untrainedWorkers;        // Total untrained workers
    public float idleWorkerRate;        // From WorkerSystem.GetIdleWorkerPercentage()
    public int totalWorkersInvolved;    // = workingWorkers (approximation)
    public int tasksCompletedByWorkers; // = todayCompletedTasks.Count
    public int trainedWorkersInvolved;  // Estimated from trained/untrained working ratio
    public int untrainedWorkersInvolved;// Remainder
    
    [Header("Financial Metrics")]
    public float startingBudget;        // Budget at day start
    public float budgetSpent;           // startingBudget - endingBudget
    public float endingBudget;          // Current budget at report time
    public float budgetUsageRate;       // taskCosts / dailyBudgetAllocated * 100
    public float averageTaskCost;       // taskCosts / completedTasks
    public float satisfactionChange;    // From SatisfactionAndBudget system (live tracking)
    public int buildingsConstructed;    // Tracked via RecordBuildingConstructed()

    [Header("Satisfaction Breakdown")]
    public float foodSatisfaction;      // Sum of food satisfaction components
    public float lodgingSatisfaction;   // Sum of lodging satisfaction components
    public float workerSatisfaction;    // Sum of worker satisfaction components

    [Header("Efficiency Breakdown")]
    public float foodEfficiency;        // Kitchen efficiency score
    public float shelterEfficiency;     // Shelter efficiency score
    public float workerEfficiency;      // Worker efficiency score
    public float budgetEfficiency;      // Budget efficiency score

    public int newWorkersHired;         // From WorkerSystem.GetNewWorkersHiredToday()
    public int workersInTraining;       // From WorkerSystem stats
    public int totalInfluencedResidents;// Population moved via deliveries

    [Header("Task Type Breakdown - Casework/Emergency")]
    public int totalCaseworkTasks;      // Tasks with casework keywords
    public int completedCaseworkTasks;  // Completed casework tasks
    public int totalEmergencyTasks;     // TaskType.Emergency tasks
    public int completedEmergencyTasks; // Completed emergency tasks

    // ============================================================
    // CALCULATED SCORES - Set by DailyReportUI AFTER animation completes
    // These are the values displayed during animation, frozen for history
    // ============================================================

    [Header("Calculated Scores (stored after animations)")]
    public float finalSatisfactionValue;        // The satisfaction % after applying change
    public float finalEfficiencyValue;          // The efficiency % after applying change
    public float satisfactionChangeCalculated;  // The total satisfaction change from this day

    // Individual satisfaction components (what was displayed in each row)
    public float foodCompletionBonus;       // completedFoodTasks * 2
    public float foodOnTimeBonus;           // (completedFoodTasks - expiredFoodDemandTasks) * 1.5
    public float foodDelayScore;            // -expiredFoodDemandTasks * 5
    public float lodgingCompletionBonus;    // completedLodgingTasks * 2
    public float lodgingOverstayPenalty;    // -groupsOver48Hours * 5
    public float workerTaskBonus;           // tasksCompletedByWorkers * 1.5
    public float workerIdleRatePenalty;     // -idleWorkerRate * 0.1

    // Individual efficiency components (what was displayed in each row)
    public float kitchenEfficiencyScore;    // -expiredFoodPacks * 2
    public float shelterEfficiencyScore;    // -vacantShelterSlots * 0.5
    public float workerEfficiencyScore;     // -idleWorkers * 1.5
    public float budgetEfficiencyScore;     // (70 - budgetUsageRate) * 0.2
}