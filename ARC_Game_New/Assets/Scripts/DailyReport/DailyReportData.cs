using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class DailyReportMetrics
{
    [Header("Task Completion")]
    public int totalFoodTasks;
    public int completedFoodTasks;
    public int expiredFoodDemandTasks;
    public int totalLodgingTasks;
    public int completedLodgingTasks;
    
    [Header("Population Metrics")]
    public int overstayingClientGroups;
    public int groupsOver48Hours; // New field for overstay tracking
    
    [Header("Worker Metrics")]
    public int totalWorkersInvolved;
    public int tasksCompletedByWorkers;
    public int trainedWorkersInvolved;
    public int untrainedWorkersInvolved;
    public float idleWorkerRate;
    
    [Header("Resource Efficiency")]
    public int totalIdleWorkers;
    public int wastedFoodPacks;
    public int expiredFoodPacks; // New field for expired food tracking
    public float mealUsageRate;
    public int vacantShelterSlots;
    public float shelterUtilizationRate; // New field for shelter utilization
    public float budgetUsageRate;
    public float averageTaskCost;
}

public class DailyReportData : MonoBehaviour
{
    [Header("System References")]
    public TaskSystem taskSystem;
    public WorkerSystem workerSystem;
    public ClientStayTracker clientTracker;
    
    [Header("Daily Tracking")]
    public float dailyBudgetAllocated = 3000f; // Default daily budget
    
    // Daily tracking data
    private List<GameTask> todayCompletedTasks = new List<GameTask>();
    private List<GameTask> todayExpiredTasks = new List<GameTask>();
    private float todayTaskCosts = 0f;
    private int todayWastedFood = 0;
    private int todayExpiredFood = 0;
    
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
    }
    
    void FindSystemReferences()
    {
        if (taskSystem == null)
            taskSystem = FindObjectOfType<TaskSystem>();
        if (workerSystem == null)
            workerSystem = FindObjectOfType<WorkerSystem>();
        if (clientTracker == null)
            clientTracker = FindObjectOfType<ClientStayTracker>();
    }
    
    void SubscribeToEvents()
    {
        if (taskSystem != null)
        {
            taskSystem.OnTaskCompleted += OnTaskCompleted;
            taskSystem.OnTaskExpired += OnTaskExpired;
        }
        
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnDayChanged += OnDayChanged;
        }
    }
    
    void OnTaskCompleted(GameTask task)
    {
        todayCompletedTasks.Add(task);
        
        // Track task costs
        foreach (var impact in task.impacts)
        {
            if (impact.impactType == ImpactType.Budget && impact.value < 0)
            {
                todayTaskCosts += Mathf.Abs(impact.value);
            }
        }
    }
    
    void OnTaskExpired(GameTask task)
    {
        todayExpiredTasks.Add(task);
    }
    
    void OnDayChanged(int newDay)
    {
        // Reset daily tracking for new day
        todayCompletedTasks.Clear();
        todayExpiredTasks.Clear();
        todayTaskCosts = 0f;
        todayWastedFood = 0;
        todayExpiredFood = 0;
    }
    
    public DailyReportMetrics GenerateDailyReport()
    {
        DailyReportMetrics metrics = new DailyReportMetrics();
        
        // Task completion metrics
        CalculateTaskMetrics(metrics);
        
        // Worker contribution metrics
        CalculateWorkerMetrics(metrics);
        
        // Resource efficiency metrics
        CalculateResourceEfficiencyMetrics(metrics);
        
        return metrics;
    }
    
    void CalculateTaskMetrics(DailyReportMetrics metrics)
    {
        // Food delivery tasks
        var foodTasks = todayCompletedTasks.Where(t => IsTaskRelatedToFood(t)).ToList();
        var allFoodTasks = GetAllFoodTasksToday();
        
        metrics.totalFoodTasks = allFoodTasks.Count;
        metrics.completedFoodTasks = foodTasks.Count;
        
        // Food demand task expiry
        metrics.expiredFoodDemandTasks = todayExpiredTasks.Count(t => 
            t.taskType == TaskType.Demand && IsTaskRelatedToFood(t));
        
        // Lodging tasks (population/shelter related)
        var lodgingTasks = todayCompletedTasks.Where(t => IsTaskRelatedToLodging(t)).ToList();
        var allLodgingTasks = GetAllLodgingTasksToday();
        
        metrics.totalLodgingTasks = allLodgingTasks.Count;
        metrics.completedLodgingTasks = lodgingTasks.Count;
    }
    
    void CalculateWorkerMetrics(DailyReportMetrics metrics)
    {
        if (workerSystem == null) return;
        
        // Get workers involved in completed tasks
        HashSet<int> workersInvolved = new HashSet<int>();
        int trainedCount = 0;
        int untrainedCount = 0;
        
        foreach (var task in todayCompletedTasks)
        {
            // Get buildings involved in this task and their workers
            var involvedBuildings = GetBuildingsInvolvedInTask(task);
            foreach (var building in involvedBuildings)
            {
                var buildingWorkers = workerSystem.GetWorkersByBuildingId(building.GetOriginalSiteId());
                foreach (var worker in buildingWorkers)
                {
                    if (workersInvolved.Add(worker.WorkerId)) // Add returns true if new
                    {
                        if (worker.Type == WorkerType.Trained)
                            trainedCount++;
                        else
                            untrainedCount++;
                    }
                }
            }
        }
        
        metrics.totalWorkersInvolved = workersInvolved.Count;
        metrics.tasksCompletedByWorkers = todayCompletedTasks.Count;
        metrics.trainedWorkersInvolved = trainedCount;
        metrics.untrainedWorkersInvolved = untrainedCount;
        
        // Idle worker rate
        var allWorkers = workerSystem.GetAllWorkers();
        int availableWorkers = allWorkers.Count(w => w.IsAvailable);
        metrics.idleWorkerRate = allWorkers.Count > 0 ? (float)availableWorkers / allWorkers.Count * 100f : 0f;
    }
    
    void CalculateResourceEfficiencyMetrics(DailyReportMetrics metrics)
    {
        // Idle workers
        if (workerSystem != null)
        {
            var allWorkers = workerSystem.GetAllWorkers();
            metrics.totalIdleWorkers = allWorkers.Count(w => w.IsAvailable);
        }
        
        // Wasted food packs
        metrics.wastedFoodPacks = CalculateWastedFood();
        
        // Expired food packs
        metrics.expiredFoodPacks = todayExpiredFood;
        
        // Meal usage rate
        int totalMealsProduced = CalculateTotalMealsProduced();
        int totalMealsConsumed = totalMealsProduced - metrics.wastedFoodPacks;
        metrics.mealUsageRate = totalMealsProduced > 0 ? (float)totalMealsConsumed / totalMealsProduced * 100f : 100f;
        
        // Vacant shelter slots
        metrics.vacantShelterSlots = CalculateVacantShelterSlots();
        
        // Shelter utilization rate
        metrics.shelterUtilizationRate = CalculateShelterUtilizationRate();
        
        // Overstaying groups
        metrics.overstayingClientGroups = GetOverstayingClientGroups();
        metrics.groupsOver48Hours = GetGroupsOver48Hours();
        
        // Budget usage
        metrics.averageTaskCost = todayCompletedTasks.Count > 0 ? todayTaskCosts / todayCompletedTasks.Count : 0f;
        metrics.budgetUsageRate = dailyBudgetAllocated > 0 ? todayTaskCosts / dailyBudgetAllocated * 100f : 0f;
    }
    
    // Helper methods
    bool IsTaskRelatedToFood(GameTask task)
    {
        if (task.taskTitle.ToLower().Contains("food")) return true;
        if (task.description.ToLower().Contains("food")) return true;
        
        // Check if task involves food delivery
        if (task.linkedDeliveryTaskIds != null)
        {
            DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
            if (deliverySystem != null)
            {
                var deliveryTasks = deliverySystem.GetCompletedTasks();
                return deliveryTasks.Any(dt => 
                    task.linkedDeliveryTaskIds.Contains(dt.taskId) && 
                    dt.cargoType == ResourceType.FoodPacks);
            }
        }
        
        return false;
    }
    
    bool IsTaskRelatedToLodging(GameTask task)
    {
        string[] lodgingKeywords = { "shelter", "housing", "lodging", "accommodation", "population", "relocation" };
        string taskText = (task.taskTitle + " " + task.description).ToLower();
        
        return lodgingKeywords.Any(keyword => taskText.Contains(keyword));
    }
    
    List<GameTask> GetAllFoodTasksToday()
    {
        var allTasks = new List<GameTask>();
        allTasks.AddRange(todayCompletedTasks);
        allTasks.AddRange(todayExpiredTasks);
        
        return allTasks.Where(t => IsTaskRelatedToFood(t)).ToList();
    }
    
    List<GameTask> GetAllLodgingTasksToday()
    {
        var allTasks = new List<GameTask>();
        allTasks.AddRange(todayCompletedTasks);
        allTasks.AddRange(todayExpiredTasks);
        
        return allTasks.Where(t => IsTaskRelatedToLodging(t)).ToList();
    }
    
    List<Building> GetBuildingsInvolvedInTask(GameTask task)
    {
        List<Building> buildings = new List<Building>();
        
        // Get building from affected facility
        if (!string.IsNullOrEmpty(task.affectedFacility))
        {
            Building building = FindObjectsOfType<Building>()
                .FirstOrDefault(b => b.name.Contains(task.affectedFacility));
            if (building != null)
                buildings.Add(building);
        }
        
        return buildings;
    }
    
    int CalculateWastedFood()
    {
        return todayWastedFood;
    }
    
    int CalculateTotalMealsProduced()
    {
        // Calculate total food production for the day
        Building[] kitchens = FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational())
            .ToArray();
        
        int totalProduced = 0;
        foreach (var kitchen in kitchens)
        {
            var storage = kitchen.GetComponent<BuildingResourceStorage>();
            if (storage != null)
            {
                // Estimate production based on rounds and production rate
                // This should ideally be tracked throughout the day
                totalProduced += 10 * 4; // Assuming 10 per round * 4 rounds
            }
        }
        
        return totalProduced;
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
    
    float CalculateShelterUtilizationRate()
    {
        Building[] shelters = FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Shelter)
            .ToArray();
        
        int totalCapacity = 0;
        int totalOccupied = 0;
        
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
    
    public int GetOverstayingClientGroups()
    {
        if (clientTracker == null) return 0;
        
        var overstayRecords = clientTracker.GetOverstayStatistics();
        if (overstayRecords.ContainsKey("CurrentOverstayingGroups"))
        {
            return (int)overstayRecords["CurrentOverstayingGroups"];
        }
        
        return 0;
    }
    
    int GetGroupsOver48Hours()
    {
        if (clientTracker == null) return 0;
        
        var overstayRecords = clientTracker.GetOverstayStatistics();
        if (overstayRecords.ContainsKey("GroupsOver48Hours"))
        {
            return (int)overstayRecords["GroupsOver48Hours"];
        }
        
        return 0;
    }
    
    // Public method to add wasted food tracking
    public void RecordWastedFood(int amount)
    {
        todayWastedFood += amount;
    }
    
    // Public method to add expired food tracking
    public void RecordExpiredFood(int amount)
    {
        todayExpiredFood += amount;
    }
    
    [ContextMenu("Generate Test Report")]
    public void GenerateTestReport()
    {
        var metrics = GenerateDailyReport();
        
        Debug.Log("=== DAILY REPORT TEST ===");
        Debug.Log($"Food Tasks: {metrics.completedFoodTasks}/{metrics.totalFoodTasks}");
        Debug.Log($"Expired Food Demands: {metrics.expiredFoodDemandTasks}");
        Debug.Log($"Lodging Tasks: {metrics.completedLodgingTasks}/{metrics.totalLodgingTasks}");
        Debug.Log($"Overstaying Groups: {metrics.overstayingClientGroups}");
        Debug.Log($"Groups Over 48h: {metrics.groupsOver48Hours}");
        Debug.Log($"Worker Contribution: {metrics.tasksCompletedByWorkers} tasks by {metrics.totalWorkersInvolved} workers");
        Debug.Log($"Idle Worker Rate: {metrics.idleWorkerRate:F1}%");
        Debug.Log($"Wasted Food: {metrics.wastedFoodPacks}");
        Debug.Log($"Expired Food: {metrics.expiredFoodPacks}");
        Debug.Log($"Meal Usage Rate: {metrics.mealUsageRate:F1}%");
        Debug.Log($"Shelter Utilization: {metrics.shelterUtilizationRate:F1}%");
    }
}