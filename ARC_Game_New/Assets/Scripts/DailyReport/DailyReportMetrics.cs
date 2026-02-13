using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// ============================================================================
/// DAILY REPORT METRICS - Data container for all daily report values
/// ============================================================================
///
/// METRIC FLOW:
///   BASE METRICS (populated by DailyReportData.GenerateDailyReport())
///     -> Factual counts from the day: tasks, food, population, workers, budget
///
///   CALCULATED SCORES (populated by DailyReportUI.SaveCompletedReportToHistory())
///     -> Derived satisfaction/efficiency bonuses and penalties
///     -> Final animated satisfaction/efficiency values
///
/// SATISFACTION SCORE FORMULA:
///   Food Satisfaction:
///     foodCompletionBonus    = completedFoodTasks * 2.0
///     foodOnTimeBonus        = (completedFoodTasks - expiredFoodDemandTasks) * 1.5
///     foodDelayScore         = -expiredFoodDemandTasks * 5.0
///
///   Lodging Satisfaction:
///     lodgingCompletionBonus = completedLodgingTasks * 2.0
///     lodgingOverstayPenalty = -groupsOver48Hours * 5.0
///
///   Worker Training Satisfaction:
///     workerTrainingBonus    = workersReceivingTraining * 3.0
///
///   Total Satisfaction Change = Food + Lodging + Worker Training
///   Final Satisfaction = Clamp(previous + change, 0, 100)
///
/// EFFICIENCY SCORE FORMULA:
///   Kitchen Efficiency  = -expiredFoodPacks * 2.0
///   Shelter Efficiency  = -vacantShelterSlots * 0.5
///   Worker Efficiency   = -idleWorkers * 1.5
///   Budget Efficiency   = (70 - budgetUsageRate) * 0.2
///
///   Total Efficiency Change = sum of all four
///   Final Efficiency = Clamp(previous + change, 0, 100)
/// ============================================================================

[System.Serializable]
public class DailyReportMetrics
{
    // =========================================================================
    // BASE METRICS - populated by DailyReportData.GenerateDailyReport()
    // =========================================================================

    [Header("Task Completion")]
    public int totalTasks;          // All Emergency+Demand tasks today (deduplicated)
    public int completedTasks;      // Tasks with TaskStatus.Completed ONLY
    public int expiredTasks;        // Tasks with TaskStatus.Expired or Incomplete
    public int totalDeliveryTasks;
    public int completedDeliveryTasks;
    
    [Header("Task Details by Tag (uses TaskTag)")]
    public int totalFoodTasks;
    public int completedFoodTasks;
    public int expiredFoodDemandTasks;
    public int totalLodgingTasks;
    public int completedLodgingTasks;
    
    [Header("Resource Metrics")]
    public int foodProduced;
    public int foodDelivered;
    public int foodConsumed;
    public int foodWasted;
    public int wastedFoodPacks;     // Compatibility alias for foodWasted
    public int expiredFoodPacks;    // Compatibility alias for expired food
    public int currentFoodInStorage;
    public float mealUsageRate;
    
    [Header("Population Metrics")]
    public int totalPopulation;
    public int newArrivals;
    public int departures;
    public int overstayingClientGroups;
    public int groupsOver48Hours;
    public float shelterOccupancyRate;
    public float shelterUtilizationRate; // Compatibility alias
    public int vacantShelterSlots;
    
    [Header("Worker Metrics")]
    public int totalWorkers;
    public int workingWorkers;
    public int idleWorkers;
    public int trainedWorkers;
    public int untrainedWorkers;
    public float idleWorkerRate;
    public int totalWorkersInvolved;
    public int tasksCompletedByWorkers;
    public int trainedWorkersInvolved;
    public int untrainedWorkersInvolved;
    /// <summary>
    /// Number of untrained workers currently in training status.
    /// Used for Worker Training satisfaction bonus.
    /// </summary>
    public int workersReceivingTraining;
    
    [Header("Financial Metrics")]
    public float startingBudget;
    public float budgetSpent;
    public float endingBudget;
    public float budgetUsageRate;
    public float averageTaskCost;
    public float satisfactionChange;
    public int buildingsConstructed;

    [Header("Satisfaction Breakdown (stored after calculation)")]
    public float foodSatisfaction;
    public float lodgingSatisfaction;
    public float workerSatisfaction;

    [Header("Efficiency Breakdown (stored after calculation)")]
    public float foodEfficiency;
    public float shelterEfficiency;
    public float workerEfficiency;
    public float budgetEfficiency;

    [Header("Bottom Panel - What We Did Today")]
    public int newWorkersHired;
    public int workersInTraining;
    
    [Header("Bottom Panel - Today's Data")]
    /// <summary>
    /// Count of Emergency+Demand tasks that ended as Incomplete or Expired.
    /// Replaces old "totalInfluencedResidents" which was never populated correctly.
    /// </summary>
    public int incompleteExpiredTasks;

    [Header("Task Type Breakdown - Cases Resolved (Emergency+Demand only)")]
    /// <summary>
    /// Total Emergency+Demand tasks for the day. Used for "Cases Resolved" stat.
    /// Advisory/Alert/Other tasks are excluded.
    /// </summary>
    public int totalCasesResolvable;
    /// <summary>
    /// Completed Emergency+Demand tasks (TaskStatus.Completed only).
    /// </summary>
    public int completedCasesResolved;
    public int totalEmergencyTasks;
    public int completedEmergencyTasks;

    [Header("Calculated Scores (stored after animations)")]
    public float finalSatisfactionValue;
    public float finalEfficiencyValue;
    public float satisfactionChangeCalculated; // The ACTUAL change from animations

    // Individual satisfaction components
    public float foodCompletionBonus;
    public float foodOnTimeBonus;
    public float foodDelayScore;
    public float lodgingCompletionBonus;
    public float lodgingOverstayPenalty;
    /// <summary>
    /// Worker Training satisfaction bonus = workersReceivingTraining * 3.0
    /// Replaces old workerTaskBonus/workerIdleRatePenalty.
    /// </summary>
    public float workerTrainingBonus;

    // Individual efficiency components
    public float kitchenEfficiencyScore;
    public float shelterEfficiencyScore;
    public float workerEfficiencyScore;
    public float budgetEfficiencyScore;

} 