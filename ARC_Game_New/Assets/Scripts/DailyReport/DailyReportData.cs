using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class DailyReportData : MonoBehaviour
{
    [Header("System References")]
    public TaskSystem taskSystem;
    public WorkerSystem workerSystem;
    public DeliverySystem deliverySystem;
    public SatisfactionAndBudget budgetSystem;
    public ClientStayTracker clientTracker;
    
    [Header("Daily Budget")]
    public float dailyBudgetAllocated = 3000f;
    
    [Header("Daily Tracking")]
    private float dayStartBudget;
    private float dayStartSatisfaction;
    private int dayStartPopulation;
    private int currentDayNumber = 1;
    private bool dayStartBudgetRecorded = false;

    [Header("Historical Reports")]
    private Dictionary<int, DailyReportMetrics> historicalReports = new Dictionary<int, DailyReportMetrics>();
    
    // Daily data tracking
    private List<GameTask> todayCompletedTasks = new List<GameTask>();
    private List<GameTask> todayExpiredTasks = new List<GameTask>();
    private List<GameTask> todayCreatedTasks = new List<GameTask>();
    private List<DeliveryTask> todayCompletedDeliveries = new List<DeliveryTask>();
    private int todayFoodProduced = 0;
    private int todayFoodDelivered = 0;
    private int todayFoodConsumed = 0;
    private int todayFoodWasted = 0;
    private int todayExpiredFood = 0;
    private int todayNewArrivals = 0;
    private int todayDepartures = 0;
    private int todayBuildingsConstructed = 0;
    private float todayTaskCosts = 0f;
    
    // Track what we've already processed
    private HashSet<int> processedTaskIds = new HashSet<int>();
    
    // Singleton
    public static DailyReportData Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        FindSystemReferences();
        SubscribeToEvents();
        RecordDayStartMetrics();
        
        // Sync with existing data
        SyncWithExistingTasks();
    }
    
    void FindSystemReferences()
    {
        if (taskSystem == null)
            taskSystem = FindObjectOfType<TaskSystem>();
        if (workerSystem == null)
            workerSystem = FindObjectOfType<WorkerSystem>();
        if (deliverySystem == null)
            deliverySystem = FindObjectOfType<DeliverySystem>();
        if (budgetSystem == null)
            budgetSystem = FindObjectOfType<SatisfactionAndBudget>();
        if (clientTracker == null)
            clientTracker = FindObjectOfType<ClientStayTracker>();
    }
    
    void SubscribeToEvents()
    {
        // Task events
        if (taskSystem != null)
        {
            taskSystem.OnTaskCompleted += OnTaskCompleted;
            taskSystem.OnTaskExpired += OnTaskExpired;
            taskSystem.OnTaskCreated += OnTaskCreated;
        }
        
        // Global clock events
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnDayChanged += OnDayChanged;
            GlobalClock.Instance.OnSimulationStarted += OnRoundStarted;
        }
    }
    
    /// <summary>
    /// Sync with tasks that already exist when we start.
    /// Only count TaskStatus.Completed as completed. Expired/Incomplete go to expired list.
    /// </summary>
    void SyncWithExistingTasks()
    {
        if (taskSystem == null) return;
        
        Debug.Log($"Syncing with existing tasks - Active: {taskSystem.activeTasks.Count}, Completed: {taskSystem.completedTasks.Count}");
        
        // Get current day from GlobalClock
        if (GlobalClock.Instance != null)
        {
            currentDayNumber = GlobalClock.Instance.GetCurrentDay();
        }
        
        // Add all active tasks as "created today"
        foreach (var task in taskSystem.activeTasks)
        {
            if (!processedTaskIds.Contains(task.taskId))
            {
                todayCreatedTasks.Add(task);
                processedTaskIds.Add(task.taskId);
            }
        }
        
        // Properly categorize tasks from completedTasks list by their actual status
        foreach (var task in taskSystem.completedTasks)
        {
            if (!processedTaskIds.Contains(task.taskId))
            {
                if (task.status == TaskStatus.Completed)
                {
                    todayCompletedTasks.Add(task);
                    
                    // Track costs for completed tasks
                    foreach (var impact in task.impacts)
                    {
                        if (impact.impactType == ImpactType.Budget && impact.value < 0)
                        {
                            todayTaskCosts += Mathf.Abs(impact.value);
                        }
                    }
                }
                else if (task.status == TaskStatus.Expired || task.status == TaskStatus.Incomplete)
                {
                    todayExpiredTasks.Add(task);
                }
                
                processedTaskIds.Add(task.taskId);
            }
        }
        
        Debug.Log($"After sync - Created: {todayCreatedTasks.Count}, Completed: {todayCompletedTasks.Count}, Expired: {todayExpiredTasks.Count}");
    }
    
    void RecordDayStartMetrics()
    {
        if (budgetSystem != null)
        {
            dayStartBudget = budgetSystem.GetCurrentBudget();
            dayStartSatisfaction = budgetSystem.GetCurrentSatisfaction();
            dayStartBudgetRecorded = true;
            Debug.Log($"Recorded day start budget: {dayStartBudget}, satisfaction: {dayStartSatisfaction}");
        }
        else
        {
            dayStartBudgetRecorded = false;
            Debug.LogWarning("budgetSystem null during RecordDayStartMetrics - will retry in GenerateDailyReport");
        }
        
        dayStartPopulation = CalculateTotalPopulation();
    }
    
    void OnTaskCompleted(GameTask task)
    {
        if (!processedTaskIds.Contains(task.taskId))
        {
            todayCompletedTasks.Add(task);
            processedTaskIds.Add(task.taskId);
            
            // Track task costs
            foreach (var impact in task.impacts)
            {
                if (impact.impactType == ImpactType.Budget && impact.value < 0)
                {
                    todayTaskCosts += Mathf.Abs(impact.value);
                }
            }
        }
    }
    
    void OnTaskExpired(GameTask task)
    {
        if (!processedTaskIds.Contains(task.taskId))
        {
            todayExpiredTasks.Add(task);
            processedTaskIds.Add(task.taskId);
        }
    }
    
    void OnTaskCreated(GameTask task)
    {
        if (!processedTaskIds.Contains(task.taskId))
        {
            todayCreatedTasks.Add(task);
            processedTaskIds.Add(task.taskId);
        }
    }
    
    void OnRoundStarted()
    {
        TrackFoodProduction();
    }
    
    void OnDayChanged(int newDay)
    {
        currentDayNumber = newDay;
        ResetDailyTracking();
        RecordDayStartMetrics();
    }
    
    void ResetDailyTracking()
    {
        todayCompletedTasks.Clear();
        todayExpiredTasks.Clear();
        todayCreatedTasks.Clear();
        todayCompletedDeliveries.Clear();
        processedTaskIds.Clear();
        todayFoodProduced = 0;
        todayFoodDelivered = 0;
        todayFoodConsumed = 0;
        todayFoodWasted = 0;
        todayExpiredFood = 0;
        todayNewArrivals = 0;
        todayDepartures = 0;
        todayBuildingsConstructed = 0;
        todayTaskCosts = 0f;
    }
    
    // =========================================================================
    // GENERATE DAILY REPORT
    // =========================================================================
    
    public DailyReportMetrics GenerateDailyReport()
    {
        // Lazy re-find system references if any are null
        FindSystemReferences();
        
        // If dayStartBudget was never recorded (budgetSystem was null at Start),
        // record it now. Won't capture spending before this point, but prevents
        // all future reports from showing $0.
        if (!dayStartBudgetRecorded && budgetSystem != null)
        {
            dayStartBudget = budgetSystem.GetCurrentBudget();
            dayStartSatisfaction = budgetSystem.GetCurrentSatisfaction();
            dayStartBudgetRecorded = true;
            Debug.Log($"Late-recorded day start budget: {dayStartBudget}");
        }
        
        // If we haven't synced yet, do it now
        if (todayCreatedTasks.Count == 0 && todayCompletedTasks.Count == 0 && taskSystem != null)
        {
            Debug.Log("No tasks tracked yet, syncing with TaskSystem...");
            SyncWithExistingTasks();
        }
        
        DailyReportMetrics metrics = new DailyReportMetrics();
        
        // =====================================================================
        // TASK STATISTICS
        // =====================================================================
        var allRelevantTasks = new List<GameTask>();
        allRelevantTasks.AddRange(todayCreatedTasks);
        allRelevantTasks.AddRange(todayCompletedTasks);
        allRelevantTasks.AddRange(todayExpiredTasks);
        
        // Filter out Alert/Other/Advisory, deduplicate by taskId
        var filteredTasks = allRelevantTasks
            .Where(t => t.taskType != TaskType.Alert && t.taskType != TaskType.Other && t.taskType != TaskType.Advisory)
            .GroupBy(t => t.taskId)
            .Select(g => g.First())
            .ToList();

        metrics.totalTasks = filteredTasks.Count;
        
        metrics.completedTasks = todayCompletedTasks
            .Where(t => t.taskType != TaskType.Alert && t.taskType != TaskType.Other && t.taskType != TaskType.Advisory)
            .Where(t => t.status == TaskStatus.Completed)
            .Count();
            
        metrics.expiredTasks = todayExpiredTasks
            .Where(t => t.taskType != TaskType.Alert && t.taskType != TaskType.Other && t.taskType != TaskType.Advisory)
            .Count();

        // Task type breakdown (uses TaskTag with keyword fallback)
        CalculateTaskTypeMetrics(metrics, filteredTasks);
        
        // =====================================================================
        // DELIVERY METRICS
        // =====================================================================
        if (deliverySystem != null)
        {
            var allDeliveryTasks = deliverySystem.GetCompletedTasks();
            todayCompletedDeliveries = allDeliveryTasks;
            metrics.totalDeliveryTasks = allDeliveryTasks.Count;
            metrics.completedDeliveryTasks = todayCompletedDeliveries.Count;
        }
        
        // =====================================================================
        // RESOURCE METRICS
        // =====================================================================
        metrics.foodProduced = CalculateFoodProduced();
        metrics.foodDelivered = CalculateFoodDelivered();
        metrics.foodConsumed = CalculateFoodConsumed();
        metrics.foodWasted = todayFoodWasted;
        metrics.wastedFoodPacks = todayFoodWasted;
        metrics.expiredFoodPacks = todayExpiredFood;
        metrics.currentFoodInStorage = CalculateCurrentFoodStorage();
        
        int totalMealsProduced = metrics.foodProduced;
        int totalMealsConsumed = totalMealsProduced - metrics.foodWasted;
        metrics.mealUsageRate = totalMealsProduced > 0 ? (float)totalMealsConsumed / totalMealsProduced * 100f : 100f;
        
        // =====================================================================
        // POPULATION METRICS
        // =====================================================================
        metrics.totalPopulation = CalculateTotalPopulation();
        metrics.newArrivals = todayNewArrivals;
        metrics.departures = todayDepartures;
        metrics.overstayingClientGroups = CalculateOverstayingGroups();
        metrics.groupsOver48Hours = GetGroupsOver48Hours();
        metrics.shelterOccupancyRate = CalculateShelterOccupancy();
        metrics.shelterUtilizationRate = metrics.shelterOccupancyRate;
        metrics.vacantShelterSlots = CalculateVacantShelterSlots();
        
        // =====================================================================
        // WORKER METRICS
        // =====================================================================
        CalculateWorkerMetrics(metrics);
        
        // =====================================================================
        // FINANCIAL METRICS
        // FIX: budgetUsageRate now uses actual budgetSpent, not just task impact costs.
        // This captures ALL spending including building construction, deliveries, etc.
        // =====================================================================
        if (budgetSystem != null)
        {
            float currentBudget = budgetSystem.GetCurrentBudget();
            
            metrics.startingBudget = dayStartBudget;
            metrics.endingBudget = currentBudget;
            metrics.budgetSpent = Mathf.Max(0f, dayStartBudget - currentBudget);
            metrics.averageTaskCost = todayCompletedTasks.Count > 0 ? todayTaskCosts / todayCompletedTasks.Count : 0f;
            
            // FIX: Use actual budgetSpent for usage rate (not todayTaskCosts which
            // only tracks task impact costs and misses building costs, delivery costs, etc.)
            metrics.budgetUsageRate = dailyBudgetAllocated > 0 
                ? metrics.budgetSpent / dailyBudgetAllocated * 100f 
                : 0f;
            
            metrics.satisfactionChange = budgetSystem.GetCurrentSatisfaction() - dayStartSatisfaction;
            
            Debug.Log($"Budget: start={dayStartBudget}, end={currentBudget}, spent={metrics.budgetSpent}, usageRate={metrics.budgetUsageRate:F1}%");
        }
        else
        {
            Debug.LogWarning("budgetSystem is NULL - all financial metrics will be 0");
        }
        
        metrics.buildingsConstructed = todayBuildingsConstructed;
        
        // =====================================================================
        // BOTTOM PANEL - "What We Did Today"
        // =====================================================================
        if (workerSystem != null)
        {
            metrics.newWorkersHired = workerSystem.GetNewWorkersHiredToday();
            var workerStats = workerSystem.GetWorkerStatistics();
            metrics.workersInTraining = workerStats.untrainedTraining;
        }
        
        // =====================================================================
        // BOTTOM PANEL - Incomplete/Expired Tasks
        // =====================================================================
        metrics.incompleteExpiredTasks = todayExpiredTasks
            .Where(t => t.taskType == TaskType.Emergency || t.taskType == TaskType.Demand)
            .Count();
        metrics.incompleteExpiredTasks += todayCompletedTasks
            .Where(t => (t.taskType == TaskType.Emergency || t.taskType == TaskType.Demand) 
                     && t.status == TaskStatus.Incomplete)
            .Count();
        
        return metrics;
    }
    
    // =========================================================================
    // TASK CLASSIFICATION
    // Uses TaskTag when set; falls back to keyword matching for TaskTag.None.
    // This ensures tasks created programmatically via CreateTask() still get
    // classified, while TaskData-based tasks use explicit tags.
    // =========================================================================
    
    void CalculateTaskTypeMetrics(DailyReportMetrics metrics, List<GameTask> uniqueTasks)
    {
        // FOOD TASKS
        var foodTasks = uniqueTasks.Where(t => IsTaskFood(t)).ToList();
        var completedFoodTasks = todayCompletedTasks
            .Where(t => IsTaskFood(t) && t.status == TaskStatus.Completed)
            .ToList();
        
        metrics.totalFoodTasks = foodTasks.Count;
        metrics.completedFoodTasks = completedFoodTasks.Count;
        
        metrics.expiredFoodDemandTasks = todayExpiredTasks.Count(t => 
            t.taskType == TaskType.Demand && IsTaskFood(t));
        
        // LODGING TASKS
        var lodgingTasks = uniqueTasks.Where(t => IsTaskLodging(t)).ToList();
        var completedLodgingTasks = todayCompletedTasks
            .Where(t => IsTaskLodging(t) && t.status == TaskStatus.Completed)
            .ToList();
        
        metrics.totalLodgingTasks = lodgingTasks.Count;
        metrics.completedLodgingTasks = completedLodgingTasks.Count;
        
        // CASES RESOLVED - Emergency + Demand only
        var casesResolvable = uniqueTasks
            .Where(t => t.taskType == TaskType.Emergency || t.taskType == TaskType.Demand)
            .ToList();
        var casesResolved = todayCompletedTasks
            .Where(t => (t.taskType == TaskType.Emergency || t.taskType == TaskType.Demand)
                     && t.status == TaskStatus.Completed)
            .ToList();
        metrics.totalCasesResolvable = casesResolvable.Count;
        metrics.completedCasesResolved = casesResolved.Count;
        
        // EMERGENCY TASKS
        var emergencyTasks = uniqueTasks.Where(t => t.taskType == TaskType.Emergency).ToList();
        var completedEmergencyTasks = todayCompletedTasks
            .Where(t => t.taskType == TaskType.Emergency && t.status == TaskStatus.Completed)
            .ToList();
        metrics.totalEmergencyTasks = emergencyTasks.Count;
        metrics.completedEmergencyTasks = completedEmergencyTasks.Count;
    }
    
    /// <summary>
    /// Check if task is food-related. Uses TaskTag first; falls back to keyword if tag is None.
    /// </summary>
    bool IsTaskFood(GameTask task)
    {
        if (task == null) return false;
        
        // Explicit tag takes priority
        if (task.taskTag == TaskTag.Food) return true;
        if (task.taskTag != TaskTag.None) return false; // Has a different tag
        
        // Fallback: keyword matching for programmatically-created tasks
        string taskText = (task.taskTitle + " " + task.description).ToLower();
        return taskText.Contains("food");
    }
    
    /// <summary>
    /// Check if task is lodging-related. Uses TaskTag first; falls back to keyword if tag is None.
    /// </summary>
    bool IsTaskLodging(GameTask task)
    {
        if (task == null) return false;
        
        // Explicit tag takes priority
        if (task.taskTag == TaskTag.Lodging) return true;
        if (task.taskTag != TaskTag.None) return false; // Has a different tag
        
        // Fallback: keyword matching for programmatically-created tasks
        string taskText = (task.taskTitle + " " + task.description).ToLower();
        return taskText.Contains("relocation") || taskText.Contains("shelter") || taskText.Contains("housing");
    }
    
    // =========================================================================
    // WORKER METRICS
    // =========================================================================
    
    void CalculateWorkerMetrics(DailyReportMetrics metrics)
    {
        if (workerSystem != null)
        {
            var stats = workerSystem.GetWorkerStatistics();
            metrics.totalWorkers = stats.GetTotalWorkers();
            metrics.workingWorkers = stats.trainedWorking + stats.untrainedWorking;
            metrics.idleWorkers = stats.trainedFree + stats.untrainedFree;
            metrics.totalIdleWorkers = metrics.idleWorkers;
            metrics.trainedWorkers = stats.GetTotalTrained();
            metrics.untrainedWorkers = stats.GetTotalUntrained();
            metrics.idleWorkerRate = workerSystem.GetIdleWorkerPercentage();
            
            // Workers receiving training (for satisfaction metric)
            metrics.workersReceivingTraining = stats.untrainedTraining;
            
            // Worker involvement (kept for compatibility with DailyReportDiagnostic)
            metrics.totalWorkersInvolved = metrics.workingWorkers;
            metrics.tasksCompletedByWorkers = todayCompletedTasks.Count;
            
            if (metrics.totalWorkersInvolved > 0)
            {
                float trainedRatio = (float)stats.trainedWorking / Mathf.Max(stats.trainedWorking + stats.untrainedWorking, 1);
                metrics.trainedWorkersInvolved = Mathf.RoundToInt(metrics.totalWorkersInvolved * trainedRatio);
                metrics.untrainedWorkersInvolved = metrics.totalWorkersInvolved - metrics.trainedWorkersInvolved;
            }
        }
    }
    
    // =========================================================================
    // CALCULATION HELPERS
    // =========================================================================
    
    int CalculateFoodProduced()
    {
        return todayFoodProduced;
    }
    
    int CalculateFoodDelivered()
    {
        int delivered = 0;
        foreach (var delivery in todayCompletedDeliveries)
        {
            if (delivery.cargoType == ResourceType.FoodPacks)
                delivered += delivery.quantity;
        }
        return delivered;
    }
    
    int CalculateFoodConsumed()
    {
        return todayFoodProduced - CalculateCurrentFoodStorage() - todayFoodWasted;
    }
    
    int CalculateCurrentFoodStorage()
    {
        int totalFood = 0;
        
        Building[] allBuildings = FindObjectsOfType<Building>();
        foreach (var building in allBuildings)
        {
            var storage = building.GetComponent<BuildingResourceStorage>();
            if (storage != null)
                totalFood += storage.GetResourceAmount(ResourceType.FoodPacks);
        }
        
        PrebuiltBuilding[] prebuiltBuildings = FindObjectsOfType<PrebuiltBuilding>();
        foreach (var building in prebuiltBuildings)
        {
            var storage = building.GetComponent<BuildingResourceStorage>();
            if (storage != null)
                totalFood += storage.GetResourceAmount(ResourceType.FoodPacks);
        }
        
        return totalFood;
    }
    
    int CalculateTotalPopulation()
    {
        int totalPop = 0;
        
        Building[] shelters = FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Shelter)
            .ToArray();
        
        foreach (var shelter in shelters)
        {
            var storage = shelter.GetComponent<BuildingResourceStorage>();
            if (storage != null)
                totalPop += storage.GetResourceAmount(ResourceType.Population);
        }
        
        PrebuiltBuilding[] communities = FindObjectsOfType<PrebuiltBuilding>()
            .Where(p => p.GetPrebuiltType() == PrebuiltBuildingType.Community)
            .ToArray();
        
        foreach (var community in communities)
        {
            totalPop += community.GetCurrentPopulation();
        }
        
        return totalPop;
    }
    
    int CalculateOverstayingGroups()
    {
        if (clientTracker != null)
        {
            var stats = clientTracker.GetOverstayStatistics();
            if (stats.ContainsKey("CurrentOverstayingGroups"))
                return (int)stats["CurrentOverstayingGroups"];
        }
        return 0;
    }
    
    int GetGroupsOver48Hours()
    {
        if (clientTracker != null)
        {
            var overstayRecords = clientTracker.GetOverstayStatistics();
            if (overstayRecords.ContainsKey("GroupsOver48Hours"))
                return (int)overstayRecords["GroupsOver48Hours"];
        }
        return 0;
    }
    
    float CalculateShelterOccupancy()
    {
        int totalCapacity = 0;
        int totalOccupied = 0;
        
        Building[] shelters = FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Shelter)
            .ToArray();
        
        foreach (var shelter in shelters)
        {
            var storage = shelter.GetComponent<BuildingResourceStorage>();
            if (storage != null)
            {
                totalCapacity += storage.GetMaxCapacity(ResourceType.Population);
                totalOccupied += storage.GetResourceAmount(ResourceType.Population);
            }
        }
        
        return totalCapacity > 0 ? (float)totalOccupied / totalCapacity * 100f : 0f;
    }
    
    int CalculateVacantShelterSlots()
    {
        int totalVacant = 0;
        
        Building[] shelters = FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Shelter)
            .ToArray();
        
        foreach (var shelter in shelters)
        {
            var storage = shelter.GetComponent<BuildingResourceStorage>();
            if (storage != null)
                totalVacant += storage.GetAvailableSpace(ResourceType.Population);
        }
        
        return totalVacant;
    }
    
    void TrackFoodProduction()
    {
        Building[] kitchens = FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational())
            .ToArray();
        
        foreach (var kitchen in kitchens)
        {
            todayFoodProduced += 10; // Base production per round per kitchen
        }
    }
    
    // =========================================================================
    // PUBLIC TRACKING METHODS
    // =========================================================================
    
    public void RecordFoodWasted(int amount) { todayFoodWasted += amount; }
    public void RecordExpiredFood(int amount) { todayExpiredFood += amount; }
    public void RecordNewArrival(int count = 1) { todayNewArrivals += count; }
    public void RecordDeparture(int count = 1) { todayDepartures += count; }
    public void RecordBuildingConstructed() { todayBuildingsConstructed++; }
    
    public void RecordDeliveryCompleted(DeliveryTask task)
    {
        todayCompletedDeliveries.Add(task);
        if (task.cargoType == ResourceType.FoodPacks)
            todayFoodDelivered += task.quantity;
    }
    
    public void RecordWastedFood(int amount) { todayFoodWasted += amount; }
    
    // =========================================================================
    // HISTORICAL REPORT STORAGE
    // =========================================================================
    
    public void SaveReportToHistory(int day, DailyReportMetrics metrics)
    {
        historicalReports[day] = metrics;
        Debug.Log($"Saved report for Day {day} to history");
    }

    public DailyReportMetrics GetHistoricalReport(int day)
    {
        if (historicalReports.ContainsKey(day))
            return historicalReports[day];
        
        Debug.LogWarning($"No historical report found for Day {day}");
        return null;
    }

    public bool HasReportForDay(int day)
    {
        return historicalReports.ContainsKey(day);
    }

    [ContextMenu("Debug Current Tracking")]
    public void DebugCurrentTracking()
    {
        Debug.Log($"=== DAILY REPORT TRACKING ===");
        Debug.Log($"Day: {currentDayNumber}");
        Debug.Log($"Created Tasks: {todayCreatedTasks.Count}");
        Debug.Log($"Completed Tasks: {todayCompletedTasks.Count}");
        Debug.Log($"Expired Tasks: {todayExpiredTasks.Count}");
        Debug.Log($"Processed IDs: {processedTaskIds.Count}");
        Debug.Log($"dayStartBudget: {dayStartBudget} (recorded: {dayStartBudgetRecorded})");
        Debug.Log($"todayTaskCosts: {todayTaskCosts}");
        Debug.Log($"todayBuildingsConstructed: {todayBuildingsConstructed}");
        
        if (budgetSystem != null)
            Debug.Log($"Current budget: {budgetSystem.GetCurrentBudget()}, Spent: {dayStartBudget - budgetSystem.GetCurrentBudget()}");
        else
            Debug.LogWarning("budgetSystem is NULL");
        
        if (taskSystem != null)
            Debug.Log($"TaskSystem - Active: {taskSystem.activeTasks.Count}, Completed: {taskSystem.completedTasks.Count}");
    }
}