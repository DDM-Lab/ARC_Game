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
    
    [Header("Daily Tracking")]
    private float dayStartBudget;
    private float dayStartSatisfaction;
    private float dayStartEfficiency;   
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
    private float todayBudgetReceived = 0f;

    // receipt vars
    private float todayKitchenOpenCost = 0f;
    private float todayShelterOpenCost = 0f;
    private float todayCaseworkOpenCost = 0f;
    private float todayFastFoodCost = 0f;
    private float todayTransportCost = 0f;
    private float todayLodgingCost = 0f;
    private float todayWorkerRequestCost = 0f;
    private float todayWorkerTrainingCost = 0f;

    private int todayWorkersReleased = 0;

    [Header("Score Assumptions")]
    public int assumedTotalWorkerPoolSize = 40;
    public float maxEmergencyFunding = 600000f;

    // Track what we've already processed
    private HashSet<int> processedTaskIds = new HashSet<int>();

    //NEW
    // =========================================================================
    // Cummulative Tracked Vars
    // =========================================================================

    // Mainly Scores
    [Header("Overall metrics")]
    private int cumulativeRoundsElapsed = 0;  //here

    [Header("Cumulative Food Satisfaction + Food Waste")]
    private int cumulativeFoodPacksConsumedByClients = 0;  // record
    private int cumulativeFoodPacksNeededByClients = 0;    // record
    private int cumulativeFoodPacksWasted = 0;            // record

    // Communities have no consumption rate — their demand is the sum of food-request task
    // quantities generated for them, tracked separately from the consumption-rate "clients" above.
    private int todayCommunityFoodDemand = 0;
    private int cumulativeCommunityFoodDemand = 0;



    [Header("Cumulative Lodging")]
    private int cumulativeLodgingNightsConsumed = 0; //here
    private int cumulativeLodgingNightsNeeded = 0;  //here


    [Header("Cumulative Worker Use")]
    private int cumulativeIdleWorkerRounds = 0; //here
    private int cumulativeWorkingWorkerRounds = 0; //here
    private int cumulativeTrainingWorkerRounds = 0; //here
    private int cumulativeWorkerPoolRounds = 0; 
    

    [Header("Cumulative - Casework")]
    private int cumulativeClientRoundsAwaitingCasework = 0; // here
    private int cumulativeClientsRequestedCasework = 0; //here

    // Mainly Cost-eff
    [Header("Cumulative - Cost-Efficiency Spend")] //record all
    private float cumulativeFoodSpend = 0f;
    private float cumulativeLodgingSpend = 0f;
    private float cumulativeWorkerRequestCost = 0f;
    private float cumulativeWorkerTrainingCost = 0f;
    //END NEW

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
        SyncWithExistingTasks();
    }
    
    void FindSystemReferences()
    {
        if (taskSystem == null) taskSystem = FindObjectOfType<TaskSystem>();
        if (workerSystem == null) workerSystem = FindObjectOfType<WorkerSystem>();
        if (deliverySystem == null) deliverySystem = FindObjectOfType<DeliverySystem>();
        if (budgetSystem == null) budgetSystem = FindObjectOfType<SatisfactionAndBudget>();
        if (clientTracker == null) clientTracker = FindObjectOfType<ClientStayTracker>();
    }

    public void RecordBudgetReceived(float amount)
    {
        todayBudgetReceived += amount;
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
        
        //NEW
        GlobalClock.OnRoundEnd += AccumulateRoundMetrics;

        if (ClientStayTracker.Instance != null)
            ClientStayTracker.Instance.OnCaseworkRequested += OnCaseworkRequested;

        if (GlobalClock.Instance != null)
            GlobalClock.Instance.OnDayChanged += OnDayChangedForLodgingNights;
        //END NEW
    }

    //NEW
    void OnDestroy()
    {
        
        if (ClientStayTracker.Instance != null)
            ClientStayTracker.Instance.OnCaseworkRequested -= OnCaseworkRequested;
        if (GlobalClock.Instance != null) {
            GlobalClock.OnRoundEnd -= AccumulateRoundMetrics;
            GlobalClock.Instance.OnDayChanged -= OnDayChangedForLodgingNights;
        }
            
    }
    //END NEW


    void SyncWithExistingTasks()
    {
        if (taskSystem == null) return;
        
        Debug.Log($"Syncing with existing tasks - Active: {taskSystem.activeTasks.Count}, Completed: {taskSystem.completedTasks.Count}");
        
        if (GlobalClock.Instance != null)
            currentDayNumber = GlobalClock.Instance.GetCurrentDay();
        
        foreach (var task in taskSystem.activeTasks)
        {
            if (!processedTaskIds.Contains(task.taskId))
            {
                todayCreatedTasks.Add(task);
                processedTaskIds.Add(task.taskId);
            }
        }
        
        foreach (var task in taskSystem.completedTasks)
        {
            if (!processedTaskIds.Contains(task.taskId))
            {
                if (task.status == TaskStatus.Completed)
                {
                    todayCompletedTasks.Add(task);
                    foreach (var impact in task.impacts)
                    {
                        if (impact.impactType == ImpactType.Budget && impact.value < 0)
                            todayTaskCosts += Mathf.Abs(impact.value);
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
            dayStartEfficiency = budgetSystem.GetCurrentEfficiency();   
            dayStartBudgetRecorded = true;
            Debug.Log($"Recorded day start budget: {dayStartBudget}, satisfaction: {dayStartSatisfaction}, efficiency: {dayStartEfficiency}");
        }
        else
        {
            dayStartBudgetRecorded = false;
            Debug.LogWarning("budgetSystem null during RecordDayStartMetrics - will retry in GenerateDailyReport");
        }
        dayStartPopulation = CalculateTotalPopulation();
        todayFoodProduced = CalculateKitchenProductionCapacity();
    }

    public float GetDayStartSatisfaction() => dayStartSatisfaction;
    public float GetDayStartEfficiency() => dayStartEfficiency;



    void OnTaskCompleted(GameTask task)
    {
        if (!todayCompletedTasks.Any(t => t.taskId == task.taskId))
        {
            todayCompletedTasks.Add(task);
            foreach (var impact in task.impacts)
            {
                if (impact.impactType == ImpactType.Budget && impact.value < 0)
                    todayTaskCosts += Mathf.Abs(impact.value);
            }
        }
    }

    void OnTaskExpired(GameTask task)
    {
        if (!todayExpiredTasks.Any(t => t.taskId == task.taskId))
            todayExpiredTasks.Add(task);
    }
    
    void OnTaskCreated(GameTask task)
    {
        if (!processedTaskIds.Contains(task.taskId))
        {
            todayCreatedTasks.Add(task);
            processedTaskIds.Add(task.taskId);
        }
    }
    
    public void PrepareForNewDay()
    {
        Debug.Log($"PrepareForNewDay called - resetting tracking for new day (was day {currentDayNumber})");
    
        if (workerSystem == null)
            workerSystem = FindObjectOfType<WorkerSystem>();
        
        if (workerSystem != null)
        {
            workerSystem.SaveAndResetDailyHiredCount(currentDayNumber);
        }
        
        // Update day number
        if (GlobalClock.Instance != null)
            currentDayNumber = GlobalClock.Instance.GetCurrentDay() + 1;
        
        ResetDailyTracking();
        RecordDayStartMetrics();
        
        Debug.Log($"Daily tracking reset complete - ready for day {currentDayNumber}");
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
        todayBudgetReceived = 0f;

        //receipt
        todayKitchenOpenCost = 0f;
        todayShelterOpenCost = 0f;
        todayCaseworkOpenCost = 0f;
        todayFastFoodCost = 0f;
        todayTransportCost = 0f;
        todayLodgingCost = 0f;
        todayWorkerRequestCost = 0f;
        todayWorkerTrainingCost = 0f;

        todayWorkersReleased = 0;
        todayCommunityFoodDemand = 0;
    }

    //NEW
    // =========================================================================
    // CUMULATIVE ROUND-LEVEL ACCUMULATION
    // =========================================================================
    void AccumulateRoundMetrics()
    {
        if (workerSystem == null) workerSystem = FindObjectOfType<WorkerSystem>();
        if (workerSystem != null)
        {
            var stats = workerSystem.GetWorkerStatistics();
            int idle = stats.trainedFree + stats.untrainedFree;
            int working = stats.trainedWorking + stats.untrainedWorking;
            int training = stats.untrainedTraining;
            int total = stats.GetTotalWorkers();

            cumulativeIdleWorkerRounds += idle;
            cumulativeWorkingWorkerRounds += working;
            cumulativeTrainingWorkerRounds += training;
            cumulativeWorkerPoolRounds += total;
        }

        cumulativeRoundsElapsed++;

        if (ClientStayTracker.Instance != null)
        {
            foreach (var group in ClientStayTracker.Instance.clientGroups)
            {
                if (group.clientsWithCaseworkNeed > 0 && !group.hasDeparted)
                    cumulativeClientRoundsAwaitingCasework += group.clientsWithCaseworkNeed;
            }
        }

        RecalcWorkerSatisfaction();
        RecalcWorkerEfficiency();   
        RecalcCaseworkSatisfaction();
    }

    void OnCaseworkRequested(ClientGroup group)
    {
        cumulativeClientsRequestedCasework += group.clientsWithCaseworkNeed;
        RecalcCaseworkSatisfaction();   
    }

    void OnDayChangedForLodgingNights(int newDay)
    {
        int housedTonight = 0;
        if (ClientStayTracker.Instance != null)
        {
            housedTonight = ClientStayTracker.Instance.clientGroups.Sum(g => g.clientCount);
            cumulativeLodgingNightsConsumed += housedTonight;
        }

        if (taskSystem != null)
        {
            int neededTonight = taskSystem.activeTasks
                .Where(t => t.taskTag == TaskTag.Lodging)
                .SelectMany(t => t.impacts)
                .Where(i => i.impactType == ImpactType.Clients)
                .Sum(i => i.value);

            cumulativeLodgingNightsNeeded += housedTonight + neededTonight;
        }

        RecalcLodgingSatisfaction();
        RecalcLodgingEfficiency();
    }

    public void RecordFoodConsumptionCumulative(int consumed, int needed)
    {
        cumulativeFoodPacksConsumedByClients += consumed;
        cumulativeFoodPacksNeededByClients += needed;
        RecalcFoodSatisfaction();
        RecalcFoodEfficiency();
    }

    public void RecordFoodWasteCumulative(int amount)
    {
        cumulativeFoodPacksWasted += amount;
    }


    public void RecordFoodSpendCumulative(float amount)
    {
        cumulativeFoodSpend += amount;
        RecalcFoodEfficiency();
    }

    public void RecordLodgingSpendCumulative(float amount)
    {
        cumulativeLodgingSpend += amount;
        RecalcLodgingEfficiency();

    }

    public void RecordWorkerRequestCostCumulative(float amount)
    {
        cumulativeWorkerRequestCost += amount;
        RecalcWorkerEfficiency();
    }

    public void RecordWorkerTrainingCostCumulative(float amount)
    {
        cumulativeWorkerTrainingCost += amount;
        RecalcWorkerEfficiency();
    }

    /// <summary>
    /// Called once when a food-request task is generated for a facility with no consumption rate
    /// (e.g. Community) — see TaskSystem.CreateTaskFromDatabase. Not touched by fulfillment,
    /// multi-delivery, or later rounds, so each task counts toward today's demand exactly once.
    /// </summary>
    public void RecordCommunityFoodDemand(int amount)
    {
        todayCommunityFoodDemand += amount;
        cumulativeCommunityFoodDemand += amount;
    }

    public int GetTodayCommunityFoodDemand() => todayCommunityFoodDemand;
    public int GetCumulativeCommunityFoodDemand() => cumulativeCommunityFoodDemand;

    public int GetCumulativeFoodPacksConsumedByClients() => cumulativeFoodPacksConsumedByClients;
    public int GetCumulativeFoodPacksNeededByClients() => cumulativeFoodPacksNeededByClients;
    public int GetCumulativeFoodPacksWasted() => cumulativeFoodPacksWasted;
    public int GetCumulativeIdleWorkerRounds() => cumulativeIdleWorkerRounds;
    public int GetCumulativeWorkingWorkerRounds() => cumulativeWorkingWorkerRounds;
    public int GetCumulativeTrainingWorkerRounds() => cumulativeTrainingWorkerRounds;
    public int GetCumulativeWorkerPoolRounds() => cumulativeWorkerPoolRounds;
    public int GetCumulativeClientRoundsAwaitingCasework() => cumulativeClientRoundsAwaitingCasework;
    public int GetCumulativeClientsRequestedCasework() => cumulativeClientsRequestedCasework;
    public float GetCumulativeFoodSpend() => cumulativeFoodSpend;
    public float GetCumulativeLodgingSpend() => cumulativeLodgingSpend;
    public float GetCumulativeWorkerRequestCost() => cumulativeWorkerRequestCost;
    public float GetCumulativeWorkerTrainingCost() => cumulativeWorkerTrainingCost;

    public int GetCumulativeLodgingNightsConsumed() => cumulativeLodgingNightsConsumed;
    public int GetCumulativeLodgingNightsNeeded() => cumulativeLodgingNightsNeeded;   
    public int GetCumulativeRoundsElapsed() => cumulativeRoundsElapsed;

    // receipt 
    public void RecordKitchenOpenCostToday(float amt) => todayKitchenOpenCost += amt;
    public void RecordShelterOpenCostToday(float amt) => todayShelterOpenCost += amt;
    public void RecordCaseworkOpenCostToday(float amt) => todayCaseworkOpenCost += amt;
    public void RecordFastFoodSpendToday(float amt) => todayFastFoodCost += amt;
    public void RecordTransportCostToday(float amt) => todayTransportCost += amt;
    public void RecordLodgingCostToday(float amt) => todayLodgingCost += amt;
    public void RecordWorkerRequestCostToday(float amt) => todayWorkerRequestCost += amt;
    public void RecordWorkerTrainingCostToday(float amt) => todayWorkerTrainingCost += amt;

    public float GetTodayKitchenOpenCost() => todayKitchenOpenCost;
    public float GetTodayShelterOpenCost() => todayShelterOpenCost;
    public float GetTodayCaseworkOpenCost() => todayCaseworkOpenCost;
    public float GetTodayFastFoodCost() => todayFastFoodCost;
    public float GetTodayTransportCost() => todayTransportCost;
    public float GetTodayLodgingCost() => todayLodgingCost;
    public float GetTodayWorkerRequestCost() => todayWorkerRequestCost;
    public float GetTodayWorkerTrainingCost() => todayWorkerTrainingCost;
    //END NEW

    // new 
    public void RecordWorkersReleasedToday(int count) => todayWorkersReleased += count;
    public int GetTodayWorkersReleased() => todayWorkersReleased;

    public int GetCurrentWorkingWorkers()
    {
        if (workerSystem == null) workerSystem = FindObjectOfType<WorkerSystem>();
        if (workerSystem == null) return 0;
        var stats = workerSystem.GetWorkerStatistics();
        return stats.trainedWorking + stats.untrainedWorking;
    }

    public int GetCurrentWaitingWorkers()
    {
        if (workerSystem == null) workerSystem = FindObjectOfType<WorkerSystem>();
        if (workerSystem == null) return 0;
        var stats = workerSystem.GetWorkerStatistics();
        return stats.trainedNotArrived + stats.untrainedNotArrived;
    }

    public int GetCurrentTrainingWorkers()
    {
        if (workerSystem == null) workerSystem = FindObjectOfType<WorkerSystem>();
        if (workerSystem == null) return 0;
        var stats = workerSystem.GetWorkerStatistics();
        return stats.untrainedTraining;
    }

    public int GetCurrentPopulationNeedingLodging()
    {
        int total = 0;
        PrebuiltBuilding[] communities = FindObjectsOfType<PrebuiltBuilding>()
            .Where(pb => pb.GetPrebuiltType() == PrebuiltBuildingType.Community).ToArray();
        foreach (var c in communities)
            total += c.GetCurrentPopulation();
        return total;
    }

    public int GetCurrentClientsNeedingCasework()
    {
        if (ClientStayTracker.Instance == null) return 0;
        return ClientStayTracker.Instance.clientGroups.Sum(g => g.clientsWithCaseworkNeed);
    }

    public int GetCurrentFoodPacksInTransit()
    {
        if (DeliverySystem.Instance == null) return 0;
        return DeliverySystem.Instance.GetActiveTasks()
            .Where(t => t.cargoType == ResourceType.FoodPacks)
            .Sum(t => t.quantity);
    }

    public int GetCurrentPeopleInTransitToCasework()
    {
        // Clients walk to casework on their own (no Vehicle/DeliveryTask involved) — count them separately.
        int walking = ClientRelocationHandler.Instance != null
            ? ClientRelocationHandler.Instance.GetPendingQuantityToBuildingType(BuildingType.CaseworkSite)
            : 0;

        int vehicled = 0;
        if (DeliverySystem.Instance != null)
        {
            vehicled = DeliverySystem.Instance.GetActiveTasks()
                .Where(t => t.cargoType == ResourceType.Population)
                .Where(t =>
                {
                    Building b = t.destinationBuilding as Building;
                    return b != null && b.GetBuildingType() == BuildingType.CaseworkSite;
                })
                .Sum(t => t.quantity);
        }

        return walking + vehicled;
    }

    float GetMaxPossibleBudget()
    {
        var gdm = GameDataManager.Instance;
        if (gdm == null)
        {
            return maxEmergencyFunding;
        }
        else
        {
            int days = gdm != null ? gdm.InitialGameDays : 1;
            return gdm.InitialBudget + (gdm.InitialDailyBudgetAddition * (days-1)) + (maxEmergencyFunding*(days-1));
        }
            
    }

    // end new

    // score move //

    public float S_Food()
    {
        var d = this;
        int needed = d.GetCumulativeFoodPacksNeededByClients();
        if (needed <= 0) return 1f;
        return Mathf.Clamp01((float)d.GetCumulativeFoodPacksConsumedByClients() / needed);
    }

    public float S_Lodging()
    {
        var d = this;
        int needed = d.GetCumulativeLodgingNightsNeeded();
        if (needed <= 0) return 1f;
        return Mathf.Clamp01((float)d.GetCumulativeLodgingNightsConsumed() / needed);
    }

    //public float S_WorkerUse()
    //{
    //    var d = this;
    //    int roundsElapsed = d.GetCumulativeRoundsElapsed();
    //    if (roundsElapsed <= 0) return 0f;

    //    float denom = assumedTotalWorkerPoolSize * roundsElapsed;

    //    float idleRatio = d.GetCumulativeIdleWorkerRounds() / denom;
    //    float workingRatio = d.GetCumulativeWorkingWorkerRounds() / denom;
    //    float trainingRatio = d.GetCumulativeTrainingWorkerRounds() / denom;

    //    const float wIdle = 1f / 3f, wWorking = 1f / 3f, wTraining = 1f / 3f;
    //    return (1f - idleRatio) * wIdle + (workingRatio * wWorking) + (trainingRatio * wTraining);
    //}
    public float S_WorkerUse()
    {
        var d = this;
        int roundsElapsed = d.GetCumulativeRoundsElapsed();
        if (roundsElapsed <= 0) return 0f;

        float denom = assumedTotalWorkerPoolSize * roundsElapsed;

        float idleRatio = Mathf.Clamp01(d.GetCumulativeIdleWorkerRounds() / denom);
        float workingRatio = Mathf.Clamp01(d.GetCumulativeWorkingWorkerRounds() / denom);
        float trainingRatio = Mathf.Clamp01(d.GetCumulativeTrainingWorkerRounds() / denom);

        const float wIdle = 1f / 3f, wWorking = 1f / 3f, wTraining = 1f / 3f;
        return Mathf.Clamp01((1f - idleRatio) * wIdle + (workingRatio * wWorking) + (trainingRatio * wTraining));
    }

    // Get worker satisfaction components (idle, working, training) for display in UI
    //public (float idle, float working, float training) GetWorkerSatisfactionComponents()
    //{
    //    var d = this;
    //    int roundsElapsed = d.GetCumulativeRoundsElapsed();
    //    if (roundsElapsed <= 0) return (0f, 0f, 0f);

    //    float denom = assumedTotalWorkerPoolSize * roundsElapsed;
    //    float idleRatio = d.GetCumulativeIdleWorkerRounds() / denom;
    //    float workingRatio = d.GetCumulativeWorkingWorkerRounds() / denom;
    //    float trainingRatio = d.GetCumulativeTrainingWorkerRounds() / denom;

    //    const float wIdle = 1f / 3f, wWorking = 1f / 3f, wTraining = 1f / 3f;
    //    const float wSat = 0.2f;

    //    float idleScore = (1f - idleRatio) * wIdle * wSat * 1000f;
    //    float workingScore = workingRatio * wWorking * wSat * 1000f;
    //    float trainingScore = trainingRatio * wTraining * wSat * 1000f;

    //    return (idleScore, workingScore, trainingScore);
    //}
    public (float idle, float working, float training) GetWorkerSatisfactionComponents()
    {
        var d = this;
        int roundsElapsed = d.GetCumulativeRoundsElapsed();
        if (roundsElapsed <= 0) return (0f, 0f, 0f);

        float denom = assumedTotalWorkerPoolSize * roundsElapsed;
        float idleRatio = Mathf.Clamp01(d.GetCumulativeIdleWorkerRounds() / denom);
        float workingRatio = Mathf.Clamp01(d.GetCumulativeWorkingWorkerRounds() / denom);
        float trainingRatio = Mathf.Clamp01(d.GetCumulativeTrainingWorkerRounds() / denom);

        const float wIdle = 1f / 3f, wWorking = 1f / 3f, wTraining = 1f / 3f;
        const float wSat = 0.2f;

        float idleScore = (1f - idleRatio) * wIdle * wSat * 1000f;
        float workingScore = workingRatio * wWorking * wSat * 1000f;
        float trainingScore = trainingRatio * wTraining * wSat * 1000f;

        return (idleScore, workingScore, trainingScore);
    }

    public float S_Waste()
    {
        var d = this;
        int used = d.GetCumulativeFoodPacksConsumedByClients();
        int wasted = d.GetCumulativeFoodPacksWasted();
        int requested = used + wasted;

        if (requested <= 0) return 1f;
        return (float)wasted / requested;
    }

    public float S_Casework()
    {
        var d = this;
        int requested = d.GetCumulativeClientsRequestedCasework();
        if (requested <= 0 || GameDataManager.Instance == null) return 1f;
        int denom = GameDataManager.Instance.InitialGameDays * GameDataManager.Instance.InitialRoundsPerDay * requested;
        if (denom <= 0) return 1f;

        return Mathf.Clamp01(1f - ((float)d.GetCumulativeClientRoundsAwaitingCasework() / denom));
    }

    public float CalculateLiveSatisfactionScore()
    {
        const float wFood = 0.2f, wLodging = 0.2f, wWorker = 0.2f, wWaste = 0.2f, wCasework = 0.2f;
        return S_Food() * wFood + S_Lodging() * wLodging + S_WorkerUse() * wWorker
             + S_Waste() * wWaste + S_Casework() * wCasework;
    }



    // =========================================================================
    // cost-eff new scores
    // =========================================================================
    public float C_Food()
    {
        var d = this;
        int consumed = d.GetCumulativeFoodPacksConsumedByClients();
        if (consumed <= 0) return 0f;

        float raw = d.GetCumulativeFoodSpend() / consumed;

        var gdm = GameDataManager.Instance;
        var bs = FindObjectOfType<BuildingSystem>();
        int days = gdm.InitialGameDays;

        float min = (float)bs.kitchenConstructionCost / (gdm.InitialKitchenCapacity * days);
        if (min <= 0f) return 1f; // avoid divide-by-zero if min is misconfigured

        return Mathf.Clamp01(1f - (raw - min) / (49f * min));
    }

    public float C_Lodging()
    {
        var d = this;
        var gdm = GameDataManager.Instance;

        float nightsConsumed = d.GetCumulativeLodgingNightsConsumed();
        if (nightsConsumed <= 0f) return 0f;

        float raw = d.GetCumulativeLodgingSpend() / nightsConsumed;

        var bs = FindObjectOfType<BuildingSystem>();
        int days = gdm.InitialGameDays;

        float min = (float)bs.shelterConstructionCost / (gdm.InitialShelterCapacity * days);
        if (min <= 0f) return 1f;

        return Mathf.Clamp01(1f - (raw - min) / (49f * min));
    }

    public float C_Worker()
    {
        var d = this;
        int workingRounds = d.GetCumulativeWorkingWorkerRounds();
        if (workingRounds <= 0) return 0f;

        float raw = (d.GetCumulativeWorkerTrainingCost() + d.GetCumulativeWorkerRequestCost()) / workingRounds;

        var wrs = FindObjectOfType<WorkerRequestSystem>();
        float untrainedCost = wrs != null ? wrs.untrainedWorkerCost : 100f;

        float min = untrainedCost;
        if (min <= 0f) return 1f;

        return Mathf.Clamp01(1f - (raw - min) / (49f * min));
    }
    //public float C_Food()
    //{
    //    var d = DailyReportData.Instance;
    //    int consumed = d.GetCumulativeFoodPacksConsumedByClients();
    //    if (consumed <= 0) return 0f;

    //    float raw = d.GetCumulativeFoodSpend() / consumed;

    //    var gdm = GameDataManager.Instance;
    //    var bs = FindObjectOfType<BuildingSystem>();
    //    int mapSpots = bs != null ? bs.RegisteredSites.Count : 0;
    //    int days = gdm.InitialGameDays;
    //    float totalBudget = GetMaxPossibleBudget();

    //    float min = (float)bs.kitchenConstructionCost / (gdm.InitialKitchenCapacity * days);
    //    float max = Mathf.Max(bs.kitchenConstructionCost * mapSpots * days, totalBudget);

    //    return Mathf.Clamp01(1f - (raw - min) / (max - min));
    //}

    //public float C_Lodging()
    //{
    //    var d = DailyReportData.Instance;
    //    var gdm = GameDataManager.Instance;

    //    float nightsConsumed = d.GetCumulativeLodgingNightsConsumed();
    //    if (nightsConsumed <= 0f) return 0f;

    //    float raw = d.GetCumulativeLodgingSpend() / nightsConsumed;

    //    var bs = FindObjectOfType<BuildingSystem>();
    //    int mapSpots = bs != null ? bs.RegisteredSites.Count : 0;
    //    int days = gdm.InitialGameDays;
    //    float totalBudget = GetMaxPossibleBudget();

    //    float min = (float)bs.shelterConstructionCost / (gdm.InitialShelterCapacity * days);
    //    float max = Mathf.Max(bs.shelterConstructionCost * mapSpots * days, totalBudget);

    //    return Mathf.Clamp01(1f - (raw - min) / (max - min));
    //}

    //public float C_Worker()
    //{
    //    var d = DailyReportData.Instance;
    //    int workingRounds = d.GetCumulativeWorkingWorkerRounds();
    //    if (workingRounds <= 0) return 0f;

    //    float raw = (d.GetCumulativeWorkerTrainingCost() + d.GetCumulativeWorkerRequestCost()) / workingRounds;

    //    var gdm = GameDataManager.Instance;
    //    var wrs = FindObjectOfType<WorkerRequestSystem>();
    //    var wts = FindObjectOfType<WorkerTrainingSystem>();
    //    float untrainedCost = wrs != null ? wrs.untrainedWorkerCost : 100f;
    //    float trainedCost = wrs != null ? wrs.trainedWorkerCost : 100f;
    //    float trainingCost = wts != null ? wts.trainingCostPerWorker : 100f;
    //    float min = untrainedCost;
    //    float totalBudget = GetMaxPossibleBudget();
    //    float maxCostWorkforceUnit = Mathf.Max(untrainedCost, Mathf.Max(trainedCost / 2f, (untrainedCost + trainingCost) / 2f));
    //    float max = Mathf.Max(assumedTotalWorkerPoolSize * maxCostWorkforceUnit, totalBudget);

    //    return Mathf.Clamp01(1f - (raw - min) / (max - min));
    //}

    public float CalculateLiveCostEfficiencyScore()
    {
        const float wFood = 1f / 3f, wLodging = 1f / 3f, wWorker = 1f / 3f;
        return C_Food() * wFood + C_Lodging() * wLodging + C_Worker() * wWorker;
    }


    // How much of each component's score has already been pushed into
    // SatisfactionAndBudget's running total. Waste is deliberately excluded —
    // it's still applied once, at end of day, in DailyReportUI.
    private float appliedFoodSat, appliedLodgingSat, appliedWorkerSat, appliedCaseworkSat;
    private float appliedFoodEff, appliedLodgingEff, appliedWorkerEff;

    const float SAT_W = 0.2f;
    const float EFF_W = 1f / 3f;

    void ApplyDelta(ref float applied, float newScore, bool isEfficiency, string reason)
    {
        float delta = newScore - applied;
        if (Mathf.Abs(delta) < 0.001f) return;
        applied = newScore;
        if (isEfficiency)
            SatisfactionAndBudget.Instance?.AddEfficiency(delta, reason);
        else
            SatisfactionAndBudget.Instance?.AddSatisfaction(delta, reason);
    }

    public void RecalcFoodSatisfaction(string reason = "Food delivery progress")
        => ApplyDelta(ref appliedFoodSat, S_Food() * SAT_W * 1000f, false, reason);

    public void RecalcLodgingSatisfaction(string reason = "Lodging progress")
        => ApplyDelta(ref appliedLodgingSat, S_Lodging() * SAT_W * 1000f, false, reason);

    public void RecalcWorkerSatisfaction(string reason = "Worker use progress")
        => ApplyDelta(ref appliedWorkerSat, S_WorkerUse() * SAT_W * 1000f, false, reason);

    public void RecalcCaseworkSatisfaction(string reason = "Casework progress")
        => ApplyDelta(ref appliedCaseworkSat, S_Casework() * SAT_W * 1000f, false, reason);

    public void RecalcFoodEfficiency(string reason = "Food cost efficiency")
        => ApplyDelta(ref appliedFoodEff, C_Food() * EFF_W * 1000f, true, reason);

    public void RecalcLodgingEfficiency(string reason = "Lodging cost efficiency")
        => ApplyDelta(ref appliedLodgingEff, C_Lodging() * EFF_W * 1000f, true, reason);

    public void RecalcWorkerEfficiency(string reason = "Worker cost efficiency")
        => ApplyDelta(ref appliedWorkerEff, C_Worker() * EFF_W * 1000f, true, reason);


    // Read what's already been applied, for DailyReportUI's end-of-day display
    public float GetAppliedFoodSat() => appliedFoodSat;
    public float GetAppliedLodgingSat() => appliedLodgingSat;
    public float GetAppliedWorkerSat() => appliedWorkerSat;
    public float GetAppliedCaseworkSat() => appliedCaseworkSat;
    public float GetAppliedFoodEff() => appliedFoodEff;
    public float GetAppliedLodgingEff() => appliedLodgingEff;
    public float GetAppliedWorkerEff() => appliedWorkerEff;

    public void SyncAppliedScoresToFresh()
    {
        appliedFoodSat = S_Food() * SAT_W * 1000f;
        appliedLodgingSat = S_Lodging() * SAT_W * 1000f;
        appliedWorkerSat = S_WorkerUse() * SAT_W * 1000f;
        appliedCaseworkSat = S_Casework() * SAT_W * 1000f;

        appliedFoodEff = C_Food() * EFF_W * 1000f;
        appliedLodgingEff = C_Lodging() * EFF_W * 1000f;
        appliedWorkerEff = C_Worker() * EFF_W * 1000f;
    }

    public float ComputeFreshSatisfactionTotal()
    => (S_Food() + S_Lodging() + S_WorkerUse() + S_Waste() + S_Casework()) * SAT_W * 1000f;

    public float ComputeFreshEfficiencyTotal()
        => (C_Food() + C_Lodging() + C_Worker()) * EFF_W * 1000f;
    //END NEW

    //score move end //

    // =========================================================================
    // GENERATE DAILY REPORT
    // =========================================================================

    public DailyReportMetrics GenerateDailyReport()
    {
        FindSystemReferences();
        
        if (!dayStartBudgetRecorded && budgetSystem != null)
        {
            dayStartBudget = budgetSystem.GetCurrentBudget();
            dayStartSatisfaction = budgetSystem.GetCurrentSatisfaction();
            dayStartEfficiency = budgetSystem.GetCurrentEfficiency();   // NEW0
            dayStartBudgetRecorded = true;
            Debug.Log($"Late-recorded day start budget: {dayStartBudget}");
        }
        
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

        metrics.foodProduced = CalculateFoodProduced();
        metrics.foodDelivered = CalculateFoodDelivered();
        metrics.foodConsumed = CalculateFoodConsumed();
        metrics.foodWasted = todayFoodWasted;
        metrics.wastedFoodPacks = todayFoodWasted;
        metrics.communityFoodDemand = todayCommunityFoodDemand;
        metrics.expiredFoodPacks = todayExpiredFood;
        metrics.currentFoodInStorage = CalculateCurrentFoodStorage();
        
        // FIX: mealUsageRate now represents meals in storage (upcoming waste).
        // Store the raw count so DailyReportUI can display it directly.
        // The efficiency score calculation in DailyReportUI uses this value.
        metrics.mealUsageRate = metrics.currentFoodInStorage;
        
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
        // =====================================================================
        if (budgetSystem != null)
        {
            float currentBudget = budgetSystem.GetCurrentBudget();
            
            metrics.startingBudget = dayStartBudget;
            metrics.endingBudget = currentBudget;
            metrics.budgetReceived   = todayBudgetReceived;
            metrics.budgetSpent      = Mathf.Max(0f, (dayStartBudget + todayBudgetReceived) - currentBudget);
            float totalAvailable = dayStartBudget + todayBudgetReceived;
            metrics.budgetUsageRate = totalAvailable > 0
                ? metrics.budgetSpent / totalAvailable * 100f
                : 0f;
            
            metrics.satisfactionChange = budgetSystem.GetCurrentSatisfaction() - dayStartSatisfaction;
            
            Debug.Log($"Budget: start={dayStartBudget}, end={currentBudget}, spent={metrics.budgetSpent}, usageRate={metrics.budgetUsageRate:F1}%");
        }
        else
        {
            Debug.LogWarning("budgetSystem is NULL - all financial metrics will be 0");
        }
        
        metrics.buildingsConstructed = todayBuildingsConstructed;
        //receipt
        metrics.todayKitchenOpenCost = todayKitchenOpenCost;
        metrics.todayShelterOpenCost = todayShelterOpenCost;
        metrics.todayCaseworkOpenCost = todayCaseworkOpenCost;
        metrics.todayFastFoodCost = todayFastFoodCost;
        metrics.todayTransportCost = todayTransportCost;
        metrics.todayLodgingCost = todayLodgingCost;
        metrics.todayWorkerRequestCost = todayWorkerRequestCost;
        metrics.todayWorkerTrainingCost = todayWorkerTrainingCost;

        float trackedExpenseSum = metrics.todayKitchenOpenCost + metrics.todayShelterOpenCost
                                + metrics.todayCaseworkOpenCost + metrics.todayFastFoodCost
                                + metrics.todayTransportCost + metrics.todayLodgingCost
                                + metrics.todayWorkerRequestCost + metrics.todayWorkerTrainingCost;
        metrics.todayOtherExpenses = Mathf.Max(0f, metrics.budgetSpent - trackedExpenseSum);
        
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
        
        Debug.Log($"[DailyReport] Workers hired today: {metrics.newWorkersHired}, Workers in training: {metrics.workersInTraining}, Food in storage: {metrics.currentFoodInStorage}");
        
        return metrics;
    }
    
    // =========================================================================
    // TASK CLASSIFICATION
    // =========================================================================
    
    void CalculateTaskTypeMetrics(DailyReportMetrics metrics, List<GameTask> uniqueTasks)
    {
        var foodTasks = uniqueTasks.Where(t => IsTaskFood(t)).ToList();
        var completedFoodTasks = todayCompletedTasks
            .Where(t => IsTaskFood(t) && t.status == TaskStatus.Completed).ToList();
        
        metrics.totalFoodTasks = foodTasks.Count;
        metrics.completedFoodTasks = completedFoodTasks.Count;
        metrics.expiredFoodDemandTasks = todayExpiredTasks.Count(t => t.taskType == TaskType.Demand && IsTaskFood(t));
        
        var lodgingTasks = uniqueTasks.Where(t => IsTaskLodging(t)).ToList();
        var completedLodgingTasks = todayCompletedTasks
            .Where(t => IsTaskLodging(t) && t.status == TaskStatus.Completed).ToList();
        
        metrics.totalLodgingTasks = lodgingTasks.Count;
        metrics.completedLodgingTasks = completedLodgingTasks.Count;
        
        var casesResolvable = uniqueTasks
            .Where(t => t.taskType == TaskType.Emergency || t.taskType == TaskType.Demand).ToList();
        var casesResolved = todayCompletedTasks
            .Where(t => (t.taskType == TaskType.Emergency || t.taskType == TaskType.Demand) && t.status == TaskStatus.Completed).ToList();
        metrics.totalCasesResolvable = casesResolvable.Count;
        metrics.completedCasesResolved = casesResolved.Count;
        
        var emergencyTasks = uniqueTasks.Where(t => t.taskType == TaskType.Emergency).ToList();
        var completedEmergencyTasks = todayCompletedTasks
            .Where(t => t.taskType == TaskType.Emergency && t.status == TaskStatus.Completed).ToList();
        metrics.totalEmergencyTasks = emergencyTasks.Count;
        metrics.completedEmergencyTasks = completedEmergencyTasks.Count;
    }
    
    bool IsTaskFood(GameTask task)
    {
        if (task == null) return false;
        if (task.taskTag == TaskTag.Food) return true;
        if (task.taskTag != TaskTag.None) return false;
        return false;
    }
    
    bool IsTaskLodging(GameTask task)
    {
        if (task == null) return false;
        if (task.taskTag == TaskTag.Lodging) return true;
        if (task.taskTag != TaskTag.None) return false;
        return false;
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
            metrics.trainedWorkers = stats.GetTotalTrained();
            metrics.untrainedWorkers = stats.GetTotalUntrained();
            metrics.idleWorkerRate = workerSystem.GetIdleWorkerPercentage();
            metrics.workersReceivingTraining = stats.untrainedTraining;
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
    
    int CalculateFoodProduced() { return todayFoodProduced; }
    
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
            .Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        foreach (var shelter in shelters)
        {
            var storage = shelter.GetComponent<BuildingResourceStorage>();
            if (storage != null)
                totalPop += storage.GetResourceAmount(ResourceType.Population);
        }
        
        PrebuiltBuilding[] communities = FindObjectsOfType<PrebuiltBuilding>()
            .Where(p => p.GetPrebuiltType() == PrebuiltBuildingType.Community).ToArray();
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
            .Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
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
            .Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        foreach (var shelter in shelters)
        {
            var storage = shelter.GetComponent<BuildingResourceStorage>();
            if (storage != null)
                totalVacant += storage.GetAvailableSpace(ResourceType.Population);
        }
        return totalVacant;
    }
    
    /// <summary>
    /// Kitchens produce their full FoodPacks capacity exactly once, at the start of the day
    /// (see BuildingResourceStorage.fillFoodToCapacityDaily). So "today's production" is simply
    /// the capacity of every kitchen that was operational at that moment.
    /// </summary>
    int CalculateKitchenProductionCapacity()
    {
        return FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational())
            .Sum(b => b.GetComponent<BuildingResourceStorage>()?.GetResourceCapacity(ResourceType.FoodPacks) ?? 0);
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
        Debug.Log($"Food in storage: {CalculateCurrentFoodStorage()}");
        
        if (budgetSystem != null)
            Debug.Log($"Current budget: {budgetSystem.GetCurrentBudget()}, Spent: {dayStartBudget - budgetSystem.GetCurrentBudget()}");
        
        if (workerSystem != null)
            Debug.Log($"Workers hired today: {workerSystem.GetNewWorkersHiredToday()}");
        
        if (taskSystem != null)
            Debug.Log($"TaskSystem - Active: {taskSystem.activeTasks.Count}, Completed: {taskSystem.completedTasks.Count}");
    }
}