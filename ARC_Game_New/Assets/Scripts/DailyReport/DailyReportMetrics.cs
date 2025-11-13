using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DailyReportMetrics
{
    [Header("Task Completion")]
    public int totalTasks;
    public int completedTasks;
    public int expiredTasks;
    public int totalDeliveryTasks;
    public int completedDeliveryTasks;
    
    [Header("Task Details by Type")]
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
    public int wastedFoodPacks; // Compatibility
    public int expiredFoodPacks; // Compatibility
    public int currentFoodInStorage;
    public float mealUsageRate;
    
    [Header("Population Metrics")]
    public int totalPopulation;
    public int newArrivals;
    public int departures;
    public int overstayingClientGroups;
    public int groupsOver48Hours;
    public float shelterOccupancyRate;
    public float shelterUtilizationRate; // Compatibility
    public int vacantShelterSlots;
    
    [Header("Worker Metrics")]
    public int totalWorkers;
    public int workingWorkers;
    public int idleWorkers;
    public int totalIdleWorkers; // Compatibility
    public int trainedWorkers;
    public int untrainedWorkers;
    public float idleWorkerRate;
    public int totalWorkersInvolved;
    public int tasksCompletedByWorkers;
    public int trainedWorkersInvolved;
    public int untrainedWorkersInvolved;
    
    [Header("Financial Metrics")]
    public float startingBudget;
    public float budgetSpent;
    public float endingBudget;
    public float budgetUsageRate;
    public float averageTaskCost;
    public float satisfactionChange;
    public int buildingsConstructed;

    public int newWorkersHired;
    public int workersInTraining;
    public int totalInfluencedResidents;

}

