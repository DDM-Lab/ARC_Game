using UnityEngine;

public class DailyReportDiagnostic : MonoBehaviour
{
    [ContextMenu("Run Full Diagnostic")]
    public void RunFullDiagnostic()
    {
        Debug.Log("=== DAILY REPORT DIAGNOSTIC ===");
        
        // Check if DailyReportData exists
        if (DailyReportData.Instance == null)
        {
            Debug.LogError("DailyReportData.Instance is NULL! Create a GameObject with DailyReportData component!");
            return;
        }
        
        // Check system connections
        CheckSystemConnections();
        
        // Check current tasks
        CheckTaskSystem();
        
        // Check workers
        CheckWorkerSystem();
        
        // Check budget
        CheckBudgetSystem();
        
        // Generate and check metrics
        CheckGeneratedMetrics();
    }
    
    void CheckSystemConnections()
    {
        Debug.Log("--- SYSTEM CONNECTIONS ---");
        
        var data = DailyReportData.Instance;
        
        if (data.taskSystem != null)
            Debug.Log("✓ TaskSystem connected");
        else
            Debug.LogError("✗ TaskSystem is NULL - Tasks won't be tracked!");
            
        if (data.workerSystem != null)
            Debug.Log("✓ WorkerSystem connected");
        else
            Debug.LogError("✗ WorkerSystem is NULL - Workers won't be tracked!");
            
        if (data.budgetSystem != null)
            Debug.Log("✓ BudgetSystem connected");
        else
            Debug.LogError("✗ BudgetSystem is NULL - Budget won't be tracked!");
            
        if (data.deliverySystem != null)
            Debug.Log("✓ DeliverySystem connected");
        else
            Debug.LogWarning("✗ DeliverySystem is NULL - Deliveries won't be tracked");
    }
    
    void CheckTaskSystem()
    {
        Debug.Log("--- TASK SYSTEM ---");
        
        var taskSystem = TaskSystem.Instance;
        if (taskSystem == null)
        {
            Debug.LogError("TaskSystem.Instance is NULL!");
            return;
        }
        
        Debug.Log($"Active Tasks: {taskSystem.activeTasks.Count}");
        Debug.Log($"Completed Tasks: {taskSystem.completedTasks.Count}");
        
        // Check if events are subscribed
        var eventInfo = typeof(TaskSystem).GetField("OnTaskCompleted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (eventInfo != null)
        {
            var eventDelegate = eventInfo.GetValue(taskSystem) as System.Delegate;
            if (eventDelegate != null)
            {
                Debug.Log($"OnTaskCompleted has {eventDelegate.GetInvocationList().Length} subscribers");
            }
            else
            {
                Debug.LogWarning("OnTaskCompleted has NO subscribers!");
            }
        }
    }
    
    void CheckWorkerSystem()
    {
        Debug.Log("--- WORKER SYSTEM ---");
        
        var workerSystem = WorkerSystem.Instance;
        if (workerSystem == null)
        {
            Debug.LogError("WorkerSystem.Instance is NULL!");
            return;
        }
        
        var stats = workerSystem.GetWorkerStatistics();
        Debug.Log($"Total Workers: {stats.GetTotalWorkers()}");
        Debug.Log($"Trained: {stats.GetTotalTrained()} (Working: {stats.trainedWorking}, Free: {stats.trainedFree})");
        Debug.Log($"Untrained: {stats.GetTotalUntrained()} (Working: {stats.untrainedWorking}, Free: {stats.untrainedFree})");
        Debug.Log($"Idle Rate: {workerSystem.GetIdleWorkerPercentage():F1}%");
    }
    
    void CheckBudgetSystem()
    {
        Debug.Log("--- BUDGET SYSTEM ---");
        
        var budgetSystem = SatisfactionAndBudget.Instance;
        if (budgetSystem == null)
        {
            Debug.LogError("SatisfactionAndBudget.Instance is NULL!");
            return;
        }
        
        Debug.Log($"Current Budget: ${budgetSystem.GetCurrentBudget():F0}");
        Debug.Log($"Current Satisfaction: {budgetSystem.GetCurrentSatisfaction():F1}");
    }
    
    void CheckGeneratedMetrics()
    {
        Debug.Log("--- GENERATED METRICS ---");
        
        var metrics = DailyReportData.Instance.GenerateDailyReport();
        
        Debug.Log("Task Metrics:");
        Debug.Log($"  Total Tasks: {metrics.totalTasks}");
        Debug.Log($"  Completed: {metrics.completedTasks}");
        Debug.Log($"  Expired: {metrics.expiredTasks}");
        Debug.Log($"  Food Tasks: {metrics.totalFoodTasks} (Completed: {metrics.completedFoodTasks})");
        Debug.Log($"  Lodging Tasks: {metrics.totalLodgingTasks} (Completed: {metrics.completedLodgingTasks})");
        
        Debug.Log("Worker Metrics:");
        Debug.Log($"  Total Workers: {metrics.totalWorkers}");
        Debug.Log($"  Working: {metrics.workingWorkers}");
        Debug.Log($"  Idle: {metrics.idleWorkers} ({metrics.idleWorkerRate:F1}%)");
        Debug.Log($"  Workers Involved in Tasks: {metrics.totalWorkersInvolved}");
        Debug.Log($"  Tasks Completed by Workers: {metrics.tasksCompletedByWorkers}");
        
        Debug.Log("Budget Metrics:");
        Debug.Log($"  Starting: ${metrics.startingBudget:F0}");
        Debug.Log($"  Ending: ${metrics.endingBudget:F0}");
        Debug.Log($"  Spent: ${metrics.budgetSpent:F0}");
        Debug.Log($"  Usage Rate: {metrics.budgetUsageRate:F1}%");
        
        Debug.Log("Population Metrics:");
        Debug.Log($"  Total Population: {metrics.totalPopulation}");
        Debug.Log($"  Shelter Occupancy: {metrics.shelterOccupancyRate:F1}%");
        Debug.Log($"  Vacant Slots: {metrics.vacantShelterSlots}");
    }
    
    [ContextMenu("Test Food Task Detection")]
    public void TestFoodTaskDetection()
    {
        Debug.Log("--- TESTING FOOD TASK DETECTION ---");
        
        var taskSystem = TaskSystem.Instance;
        if (taskSystem == null) return;
        
        int foodTaskCount = 0;
        foreach (var task in taskSystem.activeTasks)
        {
            bool isFood = IsTaskRelatedToFood(task);
            Debug.Log($"Task: {task.taskTitle} - Is Food Task: {isFood}");
            if (isFood) foodTaskCount++;
        }
        
        foreach (var task in taskSystem.completedTasks)
        {
            bool isFood = IsTaskRelatedToFood(task);
            Debug.Log($"Completed Task: {task.taskTitle} - Is Food Task: {isFood}");
            if (isFood) foodTaskCount++;
        }
        
        Debug.Log($"Total Food Tasks Found: {foodTaskCount}");
    }
    
    bool IsTaskRelatedToFood(GameTask task)
    {
        if (task.taskTitle.ToLower().Contains("food")) return true;
        if (task.description.ToLower().Contains("food")) return true;
        return false;
    }
}