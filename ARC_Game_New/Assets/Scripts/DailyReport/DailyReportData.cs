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
        
        // Add all completed tasks as "completed today" (for current day)
        foreach (var task in taskSystem.completedTasks)
        {
            if (!processedTaskIds.Contains(task.taskId))
            {
                todayCompletedTasks.Add(task);
                processedTaskIds.Add(task.taskId);
                
                // Track costs
                foreach (var impact in task.impacts)
                {
                    if (impact.impactType == ImpactType.Budget && impact.value < 0)
                    {
                        todayTaskCosts += Mathf.Abs(impact.value);
                    }
                }
            }
        }
        
        Debug.Log($"After sync - Created: {todayCreatedTasks.Count}, Completed: {todayCompletedTasks.Count}");
    }
    
    void RecordDayStartMetrics()
    {
        if (budgetSystem != null)
        {
            dayStartBudget = budgetSystem.GetCurrentBudget();
            dayStartSatisfaction = budgetSystem.GetCurrentSatisfaction();
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
        // Track food production each round
        TrackFoodProduction();
    }
    
    void OnDayChanged(int newDay)
    {
        // Day is ending, generate report before reset
        currentDayNumber = newDay;
        
        // Reset daily tracking for new day
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
    
    /// <summary>
    /// Generate a snapshot of today's metrics from live game state.
    /// This produces BASE METRICS only. Calculated scores (satisfaction bonuses,
    /// efficiency penalties) are computed later by DailyReportUI during animation
    /// and stored via SaveReportToHistory().
    /// </summary>
    public DailyReportMetrics GenerateDailyReport()
    {
        // If we haven't synced yet, do it now
        if (todayCreatedTasks.Count == 0 && todayCompletedTasks.Count == 0 && taskSystem != null)
        {
            Debug.Log("No tasks tracked yet, syncing with TaskSystem...");
            SyncWithExistingTasks();
        }
        
        DailyReportMetrics metrics = new DailyReportMetrics();
        
        // === TASK METRICS ===
        // Combine all task lists and deduplicate by taskId, filtering out non-scoreable types
        var allRelevantTasks = new List<GameTask>();
        allRelevantTasks.AddRange(todayCreatedTasks);
        allRelevantTasks.AddRange(todayCompletedTasks);
        allRelevantTasks.AddRange(todayExpiredTasks);
        
        var filteredTasks = allRelevantTasks
            .Where(t => t.taskType != TaskType.Alert && t.taskType != TaskType.Other && t.taskType != TaskType.Advisory)
            .GroupBy(t => t.taskId)
            .Select(g => g.First())
            .ToList();

        metrics.totalTasks = filteredTasks.Count;
        metrics.completedTasks = todayCompletedTasks
            .Where(t => t.taskType != TaskType.Alert && t.taskType != TaskType.Other && t.taskType != TaskType.Advisory)
            .Count();
        metrics.expiredTasks = todayExpiredTasks
            .Where(t => t.taskType != TaskType.Alert && t.taskType != TaskType.Other && t.taskType != TaskType.Advisory)
            .Count();

        // Task type breakdown (food, lodging, casework, emergency)
        CalculateTaskTypeMetrics(metrics);
        
        // === DELIVERY METRICS ===
        if (deliverySystem != null)
        {
            var allDeliveryTasks = deliverySystem.GetCompletedTasks();
            todayCompletedDeliveries = allDeliveryTasks;
            metrics.totalDeliveryTasks = allDeliveryTasks.Count;
            metrics.completedDeliveryTasks = todayCompletedDeliveries.Count;
        }
        
        // === RESOURCE METRICS ===
        metrics.foodProduced = CalculateFoodProduced();
        metrics.foodDelivered = CalculateFoodDelivered();
        metrics.foodConsumed = CalculateFoodConsumed();
        metrics.foodWasted = todayFoodWasted;
        metrics.wastedFoodPacks = todayFoodWasted;
        metrics.expiredFoodPacks = todayExpiredFood;
        metrics.currentFoodInStorage = CalculateCurrentFoodStorage();
        
        // Meal usage rate: what percentage of produced food was actually used (not wasted)
        int totalMealsProduced = metrics.foodProduced;
        int totalMealsConsumed = totalMealsProduced - metrics.foodWasted;
        metrics.mealUsageRate = totalMealsProduced > 0 ? (float)totalMealsConsumed / totalMealsProduced * 100f : 100f;
        
        // === POPULATION METRICS ===
        metrics.totalPopulation = CalculateTotalPopulation();
        metrics.newArrivals = todayNewArrivals;
        metrics.departures = todayDepartures;
        metrics.overstayingClientGroups = CalculateOverstayingGroups();
        metrics.groupsOver48Hours = GetGroupsOver48Hours();
        metrics.shelterOccupancyRate = CalculateShelterOccupancy();
        metrics.shelterUtilizationRate = metrics.shelterOccupancyRate;
        metrics.vacantShelterSlots = CalculateVacantShelterSlots();
        
        // === WORKER METRICS ===
        CalculateWorkerMetrics(metrics);
        
        // === FINANCIAL METRICS ===
        if (budgetSystem != null)
        {
            metrics.startingBudget = dayStartBudget;
            metrics.endingBudget = budgetSystem.GetCurrentBudget();
            metrics.budgetSpent = dayStartBudget - metrics.endingBudget;
            metrics.averageTaskCost = todayCompletedTasks.Count > 0 ? todayTaskCosts / todayCompletedTasks.Count : 0f;
            metrics.budgetUsageRate = dailyBudgetAllocated > 0 ? todayTaskCosts / dailyBudgetAllocated * 100f : 0f;
            metrics.satisfactionChange = budgetSystem.GetCurrentSatisfaction() - dayStartSatisfaction;
        }
        
        metrics.buildingsConstructed = todayBuildingsConstructed;
        
        return metrics;
    }
    
    void CalculateTaskTypeMetrics(DailyReportMetrics metrics)
    {
        // Combine all tasks for the day
        var allTodayTasks = new List<GameTask>();
        allTodayTasks.AddRange(todayCreatedTasks);
        allTodayTasks.AddRange(todayCompletedTasks);
        allTodayTasks.AddRange(todayExpiredTasks);
        
        // Filter out non-scoreable types and deduplicate
        var uniqueTasks = allTodayTasks
            .Where(t => t.taskType != TaskType.Alert && t.taskType != TaskType.Other && t.taskType != TaskType.Advisory)
            .GroupBy(t => t.taskId)
            .Select(g => g.First())
            .ToList();
        
        // Food tasks (matched by keyword "food" in title/description)
        var foodTasks = uniqueTasks.Where(t => IsTaskRelatedToFood(t)).ToList();
        var completedFoodTasks = todayCompletedTasks
            .Where(t => t.taskType != TaskType.Alert && t.taskType != TaskType.Other && t.taskType != TaskType.Advisory && IsTaskRelatedToFood(t))
            .ToList();
        
        metrics.totalFoodTasks = foodTasks.Count;
        metrics.completedFoodTasks = completedFoodTasks.Count;
        
        // Food demand task expiry (specifically Demand type + food keyword)
        metrics.expiredFoodDemandTasks = todayExpiredTasks.Count(t => 
            t.taskType == TaskType.Demand && IsTaskRelatedToFood(t));
        
        // Lodging tasks (matched by keyword "relocation")
        var lodgingTasks = uniqueTasks.Where(t => IsTaskRelatedToLodging(t)).ToList();
        var completedLodgingTasks = todayCompletedTasks
            .Where(t => t.taskType != TaskType.Alert && t.taskType != TaskType.Other && t.taskType != TaskType.Advisory && IsTaskRelatedToLodging(t))
            .ToList();
        
        metrics.totalLodgingTasks = lodgingTasks.Count;
        metrics.completedLodgingTasks = completedLodgingTasks.Count;
        
        // Casework tasks (matched by keywords)
        var caseworkTasks = uniqueTasks.Where(t => IsTaskRelatedToCasework(t)).ToList();
        var completedCaseworkTasks = todayCompletedTasks
            .Where(t => t.taskType != TaskType.Alert && t.taskType != TaskType.Other && t.taskType != TaskType.Advisory && IsTaskRelatedToCasework(t))
            .ToList();
        metrics.totalCaseworkTasks = caseworkTasks.Count;
        metrics.completedCaseworkTasks = completedCaseworkTasks.Count;
        
        // Emergency tasks (matched by TaskType.Emergency)
        var emergencyTasks = uniqueTasks.Where(t => t.taskType == TaskType.Emergency).ToList();
        var completedEmergencyTasks = todayCompletedTasks
            .Where(t => t.taskType == TaskType.Emergency)
            .ToList();
        metrics.totalEmergencyTasks = emergencyTasks.Count;
        metrics.completedEmergencyTasks = completedEmergencyTasks.Count;
    }

    bool IsTaskRelatedToCasework(GameTask task)
    {
        if (task == null) return false;
        string taskText = (task.taskTitle + " " + task.description).ToLower();
        return taskText.Contains("casework") || taskText.Contains("case work") || 
            taskText.Contains("counseling") || taskText.Contains("social service");
    }
    
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
            
            // Worker involvement approximated from current working count
            metrics.totalWorkersInvolved = metrics.workingWorkers;
            metrics.tasksCompletedByWorkers = todayCompletedTasks.Count;
            
            // Estimate trained/untrained split based on current working ratio
            if (metrics.totalWorkersInvolved > 0)
            {
                int totalWorking = stats.trainedWorking + stats.untrainedWorking;
                float trainedRatio = totalWorking > 0 ? (float)stats.trainedWorking / totalWorking : 0f;
                metrics.trainedWorkersInvolved = Mathf.RoundToInt(metrics.totalWorkersInvolved * trainedRatio);
                metrics.untrainedWorkersInvolved = metrics.totalWorkersInvolved - metrics.trainedWorkersInvolved;
            }
        }
    }
    
    // === HELPER: Task Classification ===
    
    bool IsTaskRelatedToFood(GameTask task)
    {
        if (task == null) return false;
        string taskText = (task.taskTitle + " " + task.description).ToLower();
        return taskText.Contains("food");
    }
    
    bool IsTaskRelatedToLodging(GameTask task)
    {
        if (task == null) return false;
        string taskText = (task.taskTitle + " " + task.description).ToLower();
        return taskText.Contains("relocation");
    }
    
    // === RESOURCE CALCULATIONS ===
    
    /// <summary>
    /// Calculate total food produced today.
    /// Uses the tracked todayFoodProduced value (incremented per round per operational kitchen).
    /// 
    /// NOTE: Previously this also added current food in kitchen storage, which caused
    /// double-counting (production tracking + current storage aren't additive).
    /// FIXED: Now returns only the tracked production value.
    /// </summary>
    int CalculateFoodProduced()
    {
        // FIX: Only return tracked production, don't add current kitchen storage
        // The old code added storage on top of production tracking, which double-counts.
        return todayFoodProduced;
    }
    
    int CalculateFoodDelivered()
    {
        int delivered = 0;
        
        foreach (var delivery in todayCompletedDeliveries)
        {
            if (delivery.cargoType == ResourceType.FoodPacks)
            {
                delivered += delivery.quantity;
            }
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
            {
                totalFood += storage.GetResourceAmount(ResourceType.FoodPacks);
            }
        }
        
        PrebuiltBuilding[] prebuiltBuildings = FindObjectsOfType<PrebuiltBuilding>();
        foreach (var building in prebuiltBuildings)
        {
            var storage = building.GetComponent<BuildingResourceStorage>();
            if (storage != null)
            {
                totalFood += storage.GetResourceAmount(ResourceType.FoodPacks);
            }
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
            {
                totalPop += storage.GetResourceAmount(ResourceType.Population);
            }
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
            {
                return (int)stats["CurrentOverstayingGroups"];
            }
        }
        
        return 0;
    }
    
    int GetGroupsOver48Hours()
    {
        if (clientTracker != null)
        {
            var overstayRecords = clientTracker.GetOverstayStatistics();
            if (overstayRecords.ContainsKey("GroupsOver48Hours"))
            {
                return (int)overstayRecords["GroupsOver48Hours"];
            }
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
                int capacity = storage.GetMaxCapacity(ResourceType.Population);
                int occupied = storage.GetResourceAmount(ResourceType.Population);
                totalCapacity += capacity;
                totalOccupied += occupied;
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
            {
                totalVacant += storage.GetAvailableSpace(ResourceType.Population);
            }
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
    
    // === PUBLIC TRACKING METHODS (called by other systems) ===
    
    public void RecordFoodWasted(int amount)
    {
        todayFoodWasted += amount;
    }
    
    public void RecordExpiredFood(int amount)
    {
        todayExpiredFood += amount;
    }
    
    public void RecordNewArrival(int count = 1)
    {
        todayNewArrivals += count;
    }
    
    public void RecordDeparture(int count = 1)
    {
        todayDepartures += count;
    }
    
    public void RecordBuildingConstructed()
    {
        todayBuildingsConstructed++;
    }
    
    public void RecordDeliveryCompleted(DeliveryTask task)
    {
        todayCompletedDeliveries.Add(task);
        
        if (task.cargoType == ResourceType.FoodPacks)
        {
            todayFoodDelivered += task.quantity;
        }
    }
    
    public void RecordWastedFood(int amount)
    {
        todayFoodWasted += amount;
    }
    
    // === HISTORY STORAGE ===
    
    /// <summary>
    /// Save daily report to historical storage.
    /// Called by DailyReportUI after animation completes with all calculated scores populated.
    /// </summary>
    public void SaveReportToHistory(int day, DailyReportMetrics metrics)
    {
        historicalReports[day] = metrics;
        Debug.Log($"Saved report for Day {day} to history");
    }

    /// <summary>
    /// Get historical report for a specific day
    /// </summary>
    public DailyReportMetrics GetHistoricalReport(int day)
    {
        if (historicalReports.ContainsKey(day))
        {
            return historicalReports[day];
        }
        
        Debug.LogWarning($"No historical report found for Day {day}");
        return null;
    }

    // REMOVED: GetAllHistoricalReports() - "All Reports" feature removed

    /// <summary>
    /// Check if report exists for a day
    /// </summary>
    public bool HasReportForDay(int day)
    {
        return historicalReports.ContainsKey(day);
    }

    // Debug method
    [ContextMenu("Debug Current Tracking")]
    public void DebugCurrentTracking()
    {
        Debug.Log($"=== DAILY REPORT TRACKING ===");
        Debug.Log($"Day: {currentDayNumber}");
        Debug.Log($"Created Tasks: {todayCreatedTasks.Count}");
        Debug.Log($"Completed Tasks: {todayCompletedTasks.Count}");
        Debug.Log($"Expired Tasks: {todayExpiredTasks.Count}");
        Debug.Log($"Processed IDs: {processedTaskIds.Count}");
        Debug.Log($"Historical Reports Saved: {historicalReports.Count}");
        
        if (taskSystem != null)
        {
            Debug.Log($"TaskSystem - Active: {taskSystem.activeTasks.Count}, Completed: {taskSystem.completedTasks.Count}");
        }
    }
}