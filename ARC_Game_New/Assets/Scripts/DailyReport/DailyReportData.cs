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
    // private int todayFoodConsumed = 0; // Reserved for future use
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

    private int todayLodgingRequested = 0;
    private int todayLodgingSatisfied = 0;
    private int todayCaseworkRequestedNew = 0;
    private int todayCaseworkSatisfied = 0;

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
    private int todayCommunityFoodUsed = 0;
    private Dictionary<string, int> todayCommunityFoodDemandByFacility = new Dictionary<string, int>();
    private Dictionary<string, int> todayCommunityFoodUsedByFacility = new Dictionary<string, int>();

    private int todayFoodNeedStartOfDay = 0;
    private int todayFoodNeedRound3 = 0;



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
    private int cumulativeCaseworkAvailableRounds = 0;
    private HashSet<ClientGroup> caseworkRequestedGroups = new HashSet<ClientGroup>();

    // Mainly Cost-eff
    [Header("Cumulative - Cost-Efficiency Spend")] //record all
    private float cumulativeFoodSpend = 0f;
    private float cumulativeLodgingSpend = 0f;
    private float cumulativeWorkerRequestCost = 0f;
    private float cumulativeWorkerTrainingCost = 0f;
    //END NEW

    // Guards RecordInitialWorkerImputedCost() to once per game (see that method) — not part of
    // Snapshot, since a restore overwrites cumulativeWorkerRequestCost directly regardless of it.
    private bool initialWorkerCostImputed = false;

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
        RecordInitialWorkerImputedCost();
        LogCostEfficiencyMinimums();
        SyncWithExistingTasks();
    }

    /// <summary>
    /// Logs the cost-per-unit minimums C_Food/C_Lodging/C_Worker will score against this session,
    /// once at game start, so a session's log documents which formula/parameters produced its
    /// scores — without this, only the resulting score was visible (via
    /// DailyReportUI.LogDailyReportScoreFormulas' per-day lines), not what it was scored against.
    /// Reads GetFoodCostMin/GetLodgingCostMin/GetWorkerCostMin — the exact values the scores use —
    /// so this can never drift out of sync with the formulas themselves.
    /// </summary>
    void LogCostEfficiencyMinimums()
    {
        float? foodMin = GetFoodCostMin();
        float? lodgingMin = GetLodgingCostMin();
        float? workerMin = GetWorkerCostMin();

        static string Fmt(float? m) => m.HasValue ? $"${m.Value:F3}" : "undefined (missing config — that score returns max)";

        string message = $"Cost efficiency minimums for this session: food={Fmt(foodMin)}/pack, " +
                         $"lodging={Fmt(lodgingMin)}/night, worker={Fmt(workerMin)}/worker-round " +
                         $"(score = 0 at 50x minimum, 1.0 at or below minimum).";
        GameLogPanel.Instance?.LogMetricsChange(message);
        Debug.Log($"[DailyReportData] {message}");
    }

    /// <summary>
    /// C_Worker()'s cost-efficiency score charges every worker against the price of requesting
    /// one — but the free starting volunteers (GameDataManager.InitialTrainedVolunteerCount /
    /// InitialUntrainedVolunteerCount) were never charged at all, so a participant who never
    /// requests more workers works entirely on "free" labor and the score sits at its maximum for
    /// the whole game regardless of how those workers are used.
    ///
    /// Fix: treat the starting roster as if it had been requested at game start, at the same
    /// per-worker prices a real request would pay (trained x trainedWorkerCost + untrained x
    /// untrainedWorkerCost), added once to the SAME cumulative figure C_Worker() already reads
    /// (GetCumulativeWorkerRequestCost()) — no new formula field, no change to C_Worker() itself.
    /// Runs once per game (not once per scene load): a snapshot restore overwrites
    /// cumulativeWorkerRequestCost with the real saved value afterward, so this can't double-count.
    /// </summary>
    void RecordInitialWorkerImputedCost()
    {
        if (initialWorkerCostImputed) return;
        initialWorkerCostImputed = true;

        var gdm = GameDataManager.Instance;
        var wrs = FindObjectOfType<WorkerRequestSystem>();
        if (gdm == null || wrs == null)
        {
            Debug.LogWarning("[DailyReportData] Could not impute initial worker cost — GameDataManager or WorkerRequestSystem missing. Worker Cost Efficiency will under-count the starting roster.");
            return;
        }

        int trained = gdm.InitialTrainedVolunteerCount;
        int untrained = gdm.InitialUntrainedVolunteerCount;
        float imputedCost = trained * wrs.trainedWorkerCost + untrained * wrs.untrainedWorkerCost;
        if (imputedCost <= 0f) return;

        RecordWorkerRequestCostCumulative(imputedCost);
        // Deliberately NOT RecordWorkerRequestCostToday: that feeds the Day 1 spend receipt shown
        // to the player, and this isn't real money spent — only the score-facing cumulative figure
        // should see it.

        string reason = $"Score formula update: starting roster ({trained} trained, {untrained} untrained) " +
                        $"now charged as if requested at game start (${wrs.trainedWorkerCost}/trained, " +
                        $"${wrs.untrainedWorkerCost}/untrained) = ${imputedCost:F0}, added to cumulative worker " +
                        $"request cost so Worker Cost Efficiency no longer starts — and stays, if no more " +
                        $"workers are ever requested — pinned at its maximum for working the free roster.";
        GameLogPanel.Instance?.LogMetricsChange(reason);
        Debug.Log($"[DailyReportData] {reason}");
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
        {
            GlobalClock.Instance.OnDayChanged += OnDayChangedForLodgingNights;
            GlobalClock.Instance.OnTimeSegmentChanged += CaptureRound3FoodNeed; // NEW
        }
        //END NEW
    }

    //NEW
    void OnDestroy()
    {
        
        if (ClientStayTracker.Instance != null)
            ClientStayTracker.Instance.OnCaseworkRequested -= OnCaseworkRequested;
        if (GlobalClock.Instance != null)
        {
            GlobalClock.OnRoundEnd -= AccumulateRoundMetrics;
            GlobalClock.Instance.OnDayChanged -= OnDayChangedForLodgingNights;
            GlobalClock.Instance.OnTimeSegmentChanged -= CaptureRound3FoodNeed; // NEW
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

    //void RecordDayStartMetrics()
    //{
    //    if (budgetSystem != null)
    //    {
    //        dayStartBudget = budgetSystem.GetCurrentBudget();
    //        dayStartSatisfaction = budgetSystem.GetCurrentSatisfaction();
    //        dayStartEfficiency = budgetSystem.GetCurrentEfficiency();   
    //        dayStartBudgetRecorded = true;
    //        Debug.Log($"Recorded day start budget: {dayStartBudget}, satisfaction: {dayStartSatisfaction}, efficiency: {dayStartEfficiency}");
    //    }
    //    else
    //    {
    //        dayStartBudgetRecorded = false;
    //        Debug.LogWarning("budgetSystem null during RecordDayStartMetrics - will retry in GenerateDailyReport");
    //    }
    //    dayStartPopulation = CalculateTotalPopulation();
    //    todayFoodProduced = CalculateKitchenProductionCapacity();
    //}

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
        todayFoodNeedStartOfDay = SumShelterMotelFoodDemand(); // NEW
    }

    int SumShelterMotelFoodDemand()
    {
        int total = 0;
        foreach (var b in FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter))
        {
            var s = b.GetComponent<BuildingResourceStorage>();
            if (s == null) continue;
            int people = s.GetResourceAmount(ResourceType.Population) + (s.workersConsumeFoodToo ? b.GetAssignedWorkforce() : 0);
            total += people * s.foodPerPersonPerNRounds;
        }
        foreach (var pb in FindObjectsOfType<PrebuiltBuilding>().Where(p => p.GetPrebuiltType() == PrebuiltBuildingType.Motel))
        {
            var s = pb.GetResourceStorage();
            if (s == null) continue;
            total += s.GetResourceAmount(ResourceType.Population) * s.foodPerPersonPerNRounds;
        }
        return total;
    }

    void CaptureRound3FoodNeed(int newSegment)
    {
        if (newSegment == 2) // 0 idx
            todayFoodNeedRound3 = SumShelterMotelFoodDemand();
    }

    public int GetTodayFoodNeeded() => todayFoodNeedStartOfDay + todayFoodNeedRound3 + GetTodayCommunityFoodDemand();

    public int GetTodayFoodConsumedTotal()
    {
        int total = GetTodayCommunityFoodUsed();
        foreach (var s in FindObjectsOfType<BuildingResourceStorage>())
            total += s.GetTodayFoodPacksConsumed();
        return total;
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
        // todayFoodConsumed = 0; // Reserved for future use
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
        //todayCommunityFoodDemand = 0;
        todayCommunityFoodDemand = 0;
        todayCommunityFoodUsed = 0;
        todayCommunityFoodDemandByFacility.Clear();
        todayCommunityFoodUsedByFacility.Clear();
        todayFoodNeedRound3 = 0;

        todayLodgingRequested = 0;
        todayLodgingSatisfied = 0;

        todayCaseworkRequestedNew = 0;
        todayCaseworkSatisfied = 0;
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
                if (group.clientsWithCaseworkNeed > 0 && !group.hasDeparted && caseworkRequestedGroups.Contains(group))
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
        todayCaseworkRequestedNew += group.clientsWithCaseworkNeed; // NEW
        caseworkRequestedGroups.Add(group);
        var gdm = GameDataManager.Instance;
        if (gdm != null)
        {
            int totalRounds = gdm.InitialGameDays * gdm.InitialRoundsPerDay;
            int remainingRounds = Mathf.Max(0, totalRounds - cumulativeRoundsElapsed);
            cumulativeCaseworkAvailableRounds += remainingRounds * group.clientsWithCaseworkNeed;
        }
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


    public void RecordCommunityFoodDemand(int amount)
    {
        todayCommunityFoodDemand += amount;
        cumulativeCommunityFoodDemand += amount;
    }

    public void RecordCommunityFoodDemand(string facilityName, int amount)
    {
        RecordCommunityFoodDemand(amount); 
        if (string.IsNullOrEmpty(facilityName)) return;
        todayCommunityFoodDemandByFacility.TryGetValue(facilityName, out int existing);
        todayCommunityFoodDemandByFacility[facilityName] = existing + amount;
    }

    public void RecordCommunityFoodUsedToday(string facilityName, int amount)
    {
        todayCommunityFoodUsed += amount;
        if (string.IsNullOrEmpty(facilityName)) return;
        todayCommunityFoodUsedByFacility.TryGetValue(facilityName, out int existing);
        todayCommunityFoodUsedByFacility[facilityName] = existing + amount;
    }

    public int GetTodayCommunityFoodDemand() => todayCommunityFoodDemand;
    public int GetCumulativeCommunityFoodDemand() => cumulativeCommunityFoodDemand;
    public int GetTodayCommunityFoodUsed() => todayCommunityFoodUsed;

    public int GetTodayCommunityFoodDemandForFacility(string facilityName) =>
        todayCommunityFoodDemandByFacility.TryGetValue(facilityName, out int v) ? v : 0;

    public int GetTodayCommunityFoodUsedForFacility(string facilityName) =>
        todayCommunityFoodUsedByFacility.TryGetValue(facilityName, out int v) ? v : 0;

    public void RecordLodgingRequestedToday(int amount) => todayLodgingRequested += amount;
    public void RecordLodgingSatisfiedToday(int amount) => todayLodgingSatisfied += amount;
    public int GetTodayLodgingRequested() => todayLodgingRequested;
    public int GetTodayLodgingSatisfied() => todayLodgingSatisfied;

    public void RecordCaseworkSatisfiedToday(int amount) => todayCaseworkSatisfied += amount;
    public int GetTodayCaseworkRequestedNew() => todayCaseworkRequestedNew;
    public int GetTodayCaseworkSatisfied() => todayCaseworkSatisfied;

    

    public int GetCumulativeFoodPacksConsumedByClients() => cumulativeFoodPacksConsumedByClients;
    public int GetCumulativeFoodPacksNeededByClients() => cumulativeFoodPacksNeededByClients;
    public int GetCumulativeFoodPacksWasted() => cumulativeFoodPacksWasted;
    public int GetCumulativeIdleWorkerRounds() => cumulativeIdleWorkerRounds;
    public int GetCumulativeWorkingWorkerRounds() => cumulativeWorkingWorkerRounds;
    public int GetCumulativeTrainingWorkerRounds() => cumulativeTrainingWorkerRounds;
    public int GetCumulativeWorkerPoolRounds() => cumulativeWorkerPoolRounds;
    public int GetCumulativeClientRoundsAwaitingCasework() => cumulativeClientRoundsAwaitingCasework;
    public int GetCumulativeClientsRequestedCasework() => cumulativeClientsRequestedCasework;
    public int GetCumulativeCaseworkAvailableRounds() => cumulativeCaseworkAvailableRounds;
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

    public int GetCurrentUnassignedWorkers()
    {
        if (workerSystem == null) workerSystem = FindObjectOfType<WorkerSystem>();
        if (workerSystem == null) return 0;
        var stats = workerSystem.GetWorkerStatistics();
        return stats.trainedFree + stats.untrainedFree;
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
        if (needed <= 0) return 0f;
        return Mathf.Clamp01((float)d.GetCumulativeFoodPacksConsumedByClients() / needed);
    }

    public float S_Lodging()
    {
        var d = this;
        int needed = d.GetCumulativeLodgingNightsNeeded();
        if (needed <= 0) return 0f;
        return Mathf.Clamp01((float)d.GetCumulativeLodgingNightsConsumed() / needed);
    }

    //public float S_WorkerUse()
    //{
    //    var d = this;
    //    int roundsElapsed = d.GetCumulativeRoundsElapsed();
    //    if (roundsElapsed <= 0) return 0f;

    //    // PARITY BUILD (ledger D17): upstream's fixed assumed headcount, not our live
    //    // pool-rounds (BUG_REPORTS B23). With a pool far below the assumed size the ratios span
    //    // a sliver of their range and above it they exceed 1 -- our fix is right, and it is also
    //    // why this build reports no "Worker use progress" delta at all on day 1 where upstream
    //    // reports +63.3.
    //    //float denom = d.GetCumulativeWorkerPoolRounds();
    //    float denom = assumedTotalWorkerPoolSize * roundsElapsed;
    //    if (denom <= 0) return 0f;

    //    float idleRatio = Mathf.Clamp01(d.GetCumulativeIdleWorkerRounds() / denom);
    //    float workingRatio = Mathf.Clamp01(d.GetCumulativeWorkingWorkerRounds() / denom);
    //    float trainingRatio = Mathf.Clamp01(d.GetCumulativeTrainingWorkerRounds() / denom);

    //    const float wIdle = 1f / 3f, wWorking = 1f / 3f, wTraining = 1f / 3f;
    //    return Mathf.Clamp01((1f - idleRatio) * wIdle + (workingRatio * wWorking) + (trainingRatio * wTraining));
    //}

    public float S_WorkerUse()
    {
        var d = this;
        int idleRounds = d.GetCumulativeIdleWorkerRounds();
        int activatedRounds = idleRounds + d.GetCumulativeWorkingWorkerRounds() + d.GetCumulativeTrainingWorkerRounds();

        if (activatedRounds <= 0) return 0f;

        return Mathf.Clamp01(1f - ((float)idleRounds / activatedRounds));
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
    //public (float idle, float working, float training) GetWorkerSatisfactionComponents()
    //{
    //    var d = this;
    //    int roundsElapsed = d.GetCumulativeRoundsElapsed();
    //    if (roundsElapsed <= 0) return (0f, 0f, 0f);

    //    // Same denominator as S_WorkerUse (B23, live pool-rounds): this is the DISPLAYED
    //    // breakdown of that term, so a different normaliser would make the three parts fail
    //    // to add up to the worker score they are supposed to explain. The 1000 below is the
    //    // report's display scale and is correct here, unlike in ApplyDelta.
    //    float denom = Mathf.Max(1, d.GetCumulativeWorkerPoolRounds());
    //    float idleRatio = Mathf.Clamp01(d.GetCumulativeIdleWorkerRounds() / denom);
    //    float workingRatio = Mathf.Clamp01(d.GetCumulativeWorkingWorkerRounds() / denom);
    //    float trainingRatio = Mathf.Clamp01(d.GetCumulativeTrainingWorkerRounds() / denom);

    //    const float wIdle = 1f / 3f, wWorking = 1f / 3f, wTraining = 1f / 3f;
    //    const float wSat = 0.2f;

    //    float idleScore = (1f - idleRatio) * wIdle * wSat * 1000f;
    //    float workingScore = workingRatio * wWorking * wSat * 1000f;
    //    float trainingScore = trainingRatio * wTraining * wSat * 1000f;

    //    return (idleScore, workingScore, trainingScore);
    //}
    public (float idle, float working, float training) GetWorkerSatisfactionComponents()
    {
        var total = S_WorkerUse() * SAT_W * 1000f; 
        return (total, 0f, 0f);
    }

    [ContextMenu("Debug: Test S_WorkerUse Formula")]
    public void DebugTestWorkerUseFormula()
    {
        void Check(int idle, int working, int training, float expected)
        {
            int activated = idle + working + training;
            float result = activated <= 0 ? 0f : Mathf.Clamp01(1f - ((float)idle / activated));
            string pass = Mathf.Approximately(result, expected) ? "PASS" : "FAIL";
            Debug.Log($"[{pass}] idle={idle} working={working} training={training} => got={result:F4}, expected={expected:F4}");
        }

        Check(idle: 0, working: 100, training: 0, expected: 1.0f);   // no idling at all -> perfect score
        Check(idle: 100, working: 0, training: 0, expected: 0.0f);   // all idle -> worst score
        Check(idle: 50, working: 50, training: 0, expected: 0.5f);   // half idle -> midpoint
        Check(idle: 25, working: 50, training: 25, expected: 0.75f); // idle is 25% of activated -> 0.75
        Check(idle: 0, working: 0, training: 0, expected: 0.0f);     // nothing activated -> spec says 0, not divide-by-zero
    }

    public float S_Waste()
    {
        var d = this;
        int used = d.GetCumulativeFoodPacksConsumedByClients();
        int wasted = d.GetCumulativeFoodPacksWasted();
        int requested = used + wasted;

        if (requested <= 0) return 0f;
        return 1f-(float)wasted / requested;
    }

    //public float S_Casework()
    //{
    //    var d = this;
    //    int requested = d.GetCumulativeClientsRequestedCasework();
    //    if (requested <= 0 || GameDataManager.Instance == null) return 0f;
    //    int denom = GameDataManager.Instance.InitialGameDays * GameDataManager.Instance.InitialRoundsPerDay * requested;
    //    if (denom <= 0) return 0f;

    //    return Mathf.Clamp01(1f - ((float)d.GetCumulativeClientRoundsAwaitingCasework() / denom));
    //}

    public float S_Casework()
    {
        var d = this;
        if (d.cumulativeCaseworkAvailableRounds <= 0) return 0f;
        return Mathf.Clamp01(1f - ((float)d.GetCumulativeClientRoundsAwaitingCasework() / d.cumulativeCaseworkAvailableRounds));
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

        float? min = GetFoodCostMin();
        if (min == null) return 1f; // avoid divide-by-zero if capacity/min is misconfigured

        return Mathf.Clamp01(1f - (raw - min.Value) / (49f * min.Value));
    }

    /// <summary>
    /// Best-case cost per pack: one kitchen's construction cost spread over every pack it can
    /// produce. A kitchen produces InitialKitchenFoodCapacity packs a day (it refills to capacity
    /// daily) — NOT InitialKitchenCapacity, a retired setting that is no longer in the parameter
    /// sheet and silently fell back to 10, which made this minimum ~17x too high. Day 1 has no food
    /// service, so a kitchen only produces on the remaining days. Extracted from C_Food() so
    /// LogCostEfficiencyMinimums() logs the exact value the score uses — never a second copy that
    /// could drift from it.
    /// </summary>
    float? GetFoodCostMin()
    {
        var gdm = GameDataManager.Instance;
        var bs = FindObjectOfType<BuildingSystem>();
        if (gdm == null || bs == null) return null;

        int productiveDays = Mathf.Max(1, gdm.InitialGameDays - 1);
        int kitchenPacksPerDay = gdm.InitialKitchenFoodCapacity;
        if (kitchenPacksPerDay <= 0) return null;

        float min = (float)bs.kitchenConstructionCost / (kitchenPacksPerDay * productiveDays);
        return min > 0f ? min : (float?)null;
    }

    public float C_Lodging()
    {
        var d = this;
        float nightsConsumed = d.GetCumulativeLodgingNightsConsumed();
        if (nightsConsumed <= 0f) return 0f;

        float raw = d.GetCumulativeLodgingSpend() / nightsConsumed;

        float? min = GetLodgingCostMin();
        if (min == null) return 1f;

        return Mathf.Clamp01(1f - (raw - min.Value) / (49f * min.Value));
    }

    /// <summary>
    /// Best-case cost per night: one shelter's construction cost spread over every bed-night it can
    /// provide over the full game. Uses InitialGameDays - 1, not InitialGameDays: nightsConsumed
    /// (raw's denominator in C_Lodging(), incremented once per GlobalClock.OnDayChanged) can only
    /// ever register one sample per day BOUNDARY crossed — 1->2, 2->3, ... (days-1)->days — never a
    /// boundary before Day 1 or after the last day, so an 8-day game can produce at most 7 samples.
    /// Dividing by the full day count computed a minimum lower than shelters could actually reach,
    /// which unfairly lowered every lodging cost-efficiency score (a smaller min makes the same raw
    /// cost score worse, not better). See GetFoodCostMin() for why this is extracted.
    /// </summary>
    float? GetLodgingCostMin()
    {
        var gdm = GameDataManager.Instance;
        var bs = FindObjectOfType<BuildingSystem>();
        if (gdm == null || bs == null) return null;

        int days = Mathf.Max(1, gdm.InitialGameDays - 1);
        if (gdm.InitialShelterCapacity <= 0) return null;

        float min = (float)bs.shelterConstructionCost / (gdm.InitialShelterCapacity * days);
        return min > 0f ? min : (float?)null;
    }

    public float C_Worker()
    {
        var d = this;
        int workingRounds = d.GetCumulativeWorkingWorkerRounds();
        if (workingRounds <= 0) return 0f;

        float raw = (d.GetCumulativeWorkerTrainingCost() + d.GetCumulativeWorkerRequestCost()) / workingRounds;

        float? min = GetWorkerCostMin();
        if (min == null) return 1f; // can't define a minimum without the live price/schedule

        return Mathf.Clamp01(1f - (raw - min.Value) / (49f * min.Value));
    }

    /// <summary>
    /// Best-case cost per worker-ROUND: the untrained hire price spread over every round a worker
    /// could work. raw in C_Worker() is dollars per worker-round, so min must be too — it used to be
    /// the bare hire price ($300 per WORKER), ~30x too high, so raw always fell below min and the
    /// score was clamped to its maximum for nearly everyone. Day 1 has no worker service, so a
    /// worker only works the remaining days. The price is read live from the request system (the
    /// same value hires are charged at), not hard-coded. See GetFoodCostMin() for why this is
    /// extracted.
    /// </summary>
    float? GetWorkerCostMin()
    {
        var wrs = FindObjectOfType<WorkerRequestSystem>();
        var gdm = GameDataManager.Instance;
        if (wrs == null || gdm == null) return null;

        int productiveRounds = Mathf.Max(1, (gdm.InitialGameDays - 1) * gdm.InitialRoundsPerDay);
        float min = (float)wrs.untrainedWorkerCost / productiveRounds;
        return min > 0f ? min : (float?)null;
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
    // ── save / restore ────────────────────────────────────────────────────────────────
    //
    // THESE ACCUMULATORS DECIDE SATISFACTION, so they must round-trip. Recalc*Satisfaction
    // pushes the CHANGE in a component's score (newScore - applied) into the authoritative
    // field. If `applied*` comes back as 0 after a restore, the next Recalc pushes the whole
    // component score again as if it were new, and satisfaction jumps -- measured at 60
    // against the oracle's 40 on the trajectory test before this existed.
    //
    // The cumulative* counters are the INPUTS to those scores, so they have to come back too
    // or the scores themselves are computed from a blank history.
    [System.Serializable]
    public class Snapshot
    {
        public float dayStartBudget, dayStartSatisfaction, dayStartEfficiency;
        public int dayStartPopulation;

        public int roundsElapsed;
        public int foodPacksConsumedByClients, foodPacksNeededByClients, foodPacksWasted;
        public int communityFoodDemand;
        public int lodgingNightsConsumed, lodgingNightsNeeded;
        public int idleWorkerRounds, workingWorkerRounds, trainingWorkerRounds, workerPoolRounds;
        public int clientRoundsAwaitingCasework, clientsRequestedCasework, caseworkAvailableRounds;
        public float foodSpend, lodgingSpend, workerRequestCost, workerTrainingCost;

        public float appliedFoodSat, appliedLodgingSat, appliedWorkerSat, appliedCaseworkSat;
        public float appliedFoodEff, appliedLodgingEff, appliedWorkerEff;
    }

    public Snapshot CaptureState() => new Snapshot
    {
        dayStartBudget = dayStartBudget,
        dayStartSatisfaction = dayStartSatisfaction,
        dayStartEfficiency = dayStartEfficiency,
        dayStartPopulation = dayStartPopulation,
        roundsElapsed = cumulativeRoundsElapsed,
        foodPacksConsumedByClients = cumulativeFoodPacksConsumedByClients,
        foodPacksNeededByClients = cumulativeFoodPacksNeededByClients,
        foodPacksWasted = cumulativeFoodPacksWasted,
        communityFoodDemand = cumulativeCommunityFoodDemand,
        lodgingNightsConsumed = cumulativeLodgingNightsConsumed,
        lodgingNightsNeeded = cumulativeLodgingNightsNeeded,
        idleWorkerRounds = cumulativeIdleWorkerRounds,
        workingWorkerRounds = cumulativeWorkingWorkerRounds,
        trainingWorkerRounds = cumulativeTrainingWorkerRounds,
        workerPoolRounds = cumulativeWorkerPoolRounds,
        clientRoundsAwaitingCasework = cumulativeClientRoundsAwaitingCasework,
        clientsRequestedCasework = cumulativeClientsRequestedCasework,
        caseworkAvailableRounds = cumulativeCaseworkAvailableRounds,
        foodSpend = cumulativeFoodSpend,
        lodgingSpend = cumulativeLodgingSpend,
        workerRequestCost = cumulativeWorkerRequestCost,
        workerTrainingCost = cumulativeWorkerTrainingCost,
        appliedFoodSat = appliedFoodSat,
        appliedLodgingSat = appliedLodgingSat,
        appliedWorkerSat = appliedWorkerSat,
        appliedCaseworkSat = appliedCaseworkSat,
        appliedFoodEff = appliedFoodEff,
        appliedLodgingEff = appliedLodgingEff,
        appliedWorkerEff = appliedWorkerEff,
    };

    public void RestoreState(Snapshot s)
    {
        if (s == null) return;
        dayStartBudget = s.dayStartBudget;
        dayStartSatisfaction = s.dayStartSatisfaction;
        dayStartEfficiency = s.dayStartEfficiency;
        dayStartPopulation = s.dayStartPopulation;
        cumulativeRoundsElapsed = s.roundsElapsed;
        cumulativeFoodPacksConsumedByClients = s.foodPacksConsumedByClients;
        cumulativeFoodPacksNeededByClients = s.foodPacksNeededByClients;
        cumulativeFoodPacksWasted = s.foodPacksWasted;
        cumulativeCommunityFoodDemand = s.communityFoodDemand;
        cumulativeLodgingNightsConsumed = s.lodgingNightsConsumed;
        cumulativeLodgingNightsNeeded = s.lodgingNightsNeeded;
        cumulativeIdleWorkerRounds = s.idleWorkerRounds;
        cumulativeWorkingWorkerRounds = s.workingWorkerRounds;
        cumulativeTrainingWorkerRounds = s.trainingWorkerRounds;
        cumulativeWorkerPoolRounds = s.workerPoolRounds;
        cumulativeClientRoundsAwaitingCasework = s.clientRoundsAwaitingCasework;
        cumulativeClientsRequestedCasework = s.clientsRequestedCasework;
        cumulativeCaseworkAvailableRounds = s.caseworkAvailableRounds;
        cumulativeFoodSpend = s.foodSpend;
        cumulativeLodgingSpend = s.lodgingSpend;
        cumulativeWorkerRequestCost = s.workerRequestCost;
        cumulativeWorkerTrainingCost = s.workerTrainingCost;
        appliedFoodSat = s.appliedFoodSat;
        appliedLodgingSat = s.appliedLodgingSat;
        appliedWorkerSat = s.appliedWorkerSat;
        appliedCaseworkSat = s.appliedCaseworkSat;
        appliedFoodEff = s.appliedFoodEff;
        appliedLodgingEff = s.appliedLodgingEff;
        appliedWorkerEff = s.appliedWorkerEff;
    }

    private float appliedFoodSat, appliedLodgingSat, appliedWorkerSat, appliedCaseworkSat;
    private float appliedFoodEff, appliedLodgingEff, appliedWorkerEff;

    // PARITY BUILD (ledger D2): 1000f, matching upstream, NOT the correct 100f.
    // Our fix is right and upstream's value is wrong -- the five satisfaction weights sum to 1,
    // so components sum to SCORE_SCALE, and at 1000 one component's delta is ten times what it
    // should be against a field AddSatisfaction clamps to [0,100]. Version 2 reproduces the bug
    // deliberately: it exists to isolate "does the LLM code change the game", and carrying a
    // score fix into it would answer a different question.
    const float SCORE_SCALE = 1000f;
    const float SAT_W = 0.2f;
    const float EFF_W = 1f / 3f;

    /// <summary>
    /// Push the change in one component's score into the AUTHORITATIVE satisfaction/efficiency
    /// field, which is 0-100 (the human slider and the officer game_state both read it).
    ///
    /// SCALE. The callers below compute `S_x() * WEIGHT * SCORE_SCALE`. That must be 100, not
    /// the report's 1000: the five satisfaction weights sum to 1, so the components sum to
    /// SCORE_SCALE, and at 1000 a single component's delta is ten times what it should be.
    /// AddSatisfaction clamps to [0,100], so the symptom is not an absurd number but a metric
    /// pinned at 100 that then collapses -- a 50% food shortfall would cost 100 points instead
    /// of 10. The report's own 0-1000 display scaling is applied in DailyReportUI, separately.
    /// </summary>
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
        => ApplyDelta(ref appliedFoodSat, S_Food() * SAT_W * SCORE_SCALE, false, reason);

    public void RecalcLodgingSatisfaction(string reason = "Lodging progress")
        => ApplyDelta(ref appliedLodgingSat, S_Lodging() * SAT_W * SCORE_SCALE, false, reason);

    public void RecalcWorkerSatisfaction(string reason = "Worker use progress")
        => ApplyDelta(ref appliedWorkerSat, S_WorkerUse() * SAT_W * SCORE_SCALE, false, reason);

    public void RecalcCaseworkSatisfaction(string reason = "Casework progress")
        => ApplyDelta(ref appliedCaseworkSat, S_Casework() * SAT_W * SCORE_SCALE, false, reason);

    public void RecalcFoodEfficiency(string reason = "Food cost efficiency")
        => ApplyDelta(ref appliedFoodEff, C_Food() * EFF_W * SCORE_SCALE, true, reason);

    public void RecalcLodgingEfficiency(string reason = "Lodging cost efficiency")
        => ApplyDelta(ref appliedLodgingEff, C_Lodging() * EFF_W * SCORE_SCALE, true, reason);

    public void RecalcWorkerEfficiency(string reason = "Worker cost efficiency")
        => ApplyDelta(ref appliedWorkerEff, C_Worker() * EFF_W * SCORE_SCALE, true, reason);


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
        appliedFoodSat = S_Food() * SAT_W * SCORE_SCALE;
        appliedLodgingSat = S_Lodging() * SAT_W * SCORE_SCALE;
        appliedWorkerSat = S_WorkerUse() * SAT_W * SCORE_SCALE;
        appliedCaseworkSat = S_Casework() * SAT_W * SCORE_SCALE;

        appliedFoodEff = C_Food() * EFF_W * SCORE_SCALE;
        appliedLodgingEff = C_Lodging() * EFF_W * SCORE_SCALE;
        appliedWorkerEff = C_Worker() * EFF_W * SCORE_SCALE;
    }

    public float ComputeFreshSatisfactionTotal()
    => (S_Food() + S_Lodging() + S_WorkerUse() + S_Waste() + S_Casework()) * SAT_W * SCORE_SCALE;

    public float ComputeFreshEfficiencyTotal()
        => (C_Food() + C_Lodging() + C_Worker()) * EFF_W * SCORE_SCALE;
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

        metrics.lodgingRequestedToday = todayLodgingRequested;
        metrics.lodgingSatisfiedToday = todayLodgingSatisfied;
        metrics.caseworkRequestedTodayNew = todayCaseworkRequestedNew;
        metrics.caseworkSatisfiedToday = todayCaseworkSatisfied;
        metrics.workersUnassignedToday = GetCurrentUnassignedWorkers();
        metrics.foodNeededToday = GetTodayFoodNeeded();
        metrics.foodConsumedTotalToday = GetTodayFoodConsumedTotal();

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