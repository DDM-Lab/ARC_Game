using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

public class WorkerSystem : MonoBehaviour
{
    [Header("Initial Worker Configuration")]
    [SerializeField] private int initialTrainedWorkers = 3;
    [SerializeField] private int initialUntrainedWorkers = 5;
    
    [Header("Worker Assignment UI")]
    public GlobalWorkerManagementUI globalWorkerManagementUI;
    
    // Worker storage
    private List<Worker> allWorkers = new List<Worker>();
    private int nextWorkerId = 1;
    
    // Events
    public event Action OnWorkerStatsChanged;

    // Singleton instance
    public static WorkerSystem Instance { get; private set; }

    // Daily Hired Workers Tracking
    private int newWorkersHiredToday = 0;
    private Dictionary <int, int> newWorkersHiredEachDay = new Dictionary<int, int>();
    private bool isInitializing = true; // Don't count initial pool as "hired today"

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
        InitializeWorkerPool();

        // =====================================================
        // FIX: REMOVED OnDayChanged subscription for reset.
        //
        // WHAT WAS WRONG: ResetDailyHiredWorkersCount was subscribed
        // to OnDayChanged, but WorkerRequestSystem (arrivals) and
        // WorkerTrainingSystem (training completions) also subscribe
        // to OnDayChanged and CREATE workers during their handlers.
        // C# doesn't guarantee handler execution order across
        // different MonoBehaviours.
        //
        // This caused unpredictable behavior:
        // - If reset fires BEFORE arrivals → new arrivals count
        //   toward the new day (ok but inconsistent)
        // - If reset fires AFTER arrivals → new arrivals increment
        //   the counter then get immediately zeroed (data loss)
        // - Training completions also created workers via
        //   CreateTrainedWorker which incremented the counter
        //
        // The result was hired counts that accumulated across days
        // and included training completions as "hires".
        //
        // WHY THIS FIXES IT: Reset is now called explicitly by
        // DailyReportData.PrepareForNewDay() at a controlled time:
        //   1. Report reads newWorkersHiredToday (correct value)
        //   2. Player clicks "Next Day"
        //   3. PrepareForNewDay() → SaveAndResetDailyHiredCount()
        //   4. OnDayChanged fires → arrivals/training create workers
        //      → these correctly count toward the NEW day
        //
        // OLD CODE:
        // if (GlobalClock.Instance != null)
        //     GlobalClock.Instance.OnDayChanged += ResetDailyHiredWorkersCount;
        // =====================================================
    }

    void OnDestroy()
    {
        // FIX: Removed OnDayChanged unsubscribe (no longer subscribed)
    }
    
    void InitializeWorkerPool()
    {
        // Create initial trained workers
        for (int i = 0; i < initialTrainedWorkers; i++)
        {
            CreateTrainedWorker();
        }
        
        // Create initial untrained workers
        for (int i = 0; i < initialUntrainedWorkers; i++)
        {
            CreateUntrainedWorker();
        }
        GameLogPanel.Instance.LogWorkerAction($"Initialized worker pool with {initialTrainedWorkers} trained and {initialUntrainedWorkers} untrained workers.");
        Debug.Log($"Worker pool initialized with {initialTrainedWorkers} trained and {initialUntrainedWorkers} untrained workers");
        PrintWorkerStatistics();
        isInitializing = false; // Future calls to Create*Worker now count as hires
    }
    
    // =========================================================================
    // WORKER CREATION
    // =========================================================================
    
    /// <summary>
    /// Create a trained worker and add to the system.
    /// 
    /// FIX: Added 'countAsNewHire' parameter (default true).
    /// WorkerTrainingSystem passes false for training upgrades.
    /// WorkerRequestSystem uses default true for genuine new hires.
    /// </summary>
    public Worker CreateTrainedWorker(TrainedWorkerStatus initialStatus = TrainedWorkerStatus.Free, bool countAsNewHire = true)
    {
        Worker worker = new Worker(nextWorkerId++, WorkerType.Trained);
        worker.SetTrainedStatus(initialStatus);
        worker.OnStatusChanged += OnWorkerStatusChanged;
        
        allWorkers.Add(worker);
        OnWorkerStatsChanged?.Invoke();
        
        if (!isInitializing && countAsNewHire)
            IncrementNewWorkersHired();
        
        GameLogPanel.Instance.LogWorkerAction($"Created trained worker: {worker}");
        Debug.Log($"Created trained worker: {worker} (countAsNewHire={countAsNewHire})");
        return worker;
    }
    
    /// <summary>
    /// Create an untrained worker and add to the system.
    /// FIX: Added 'countAsNewHire' parameter for consistency.
    /// </summary>
    public Worker CreateUntrainedWorker(UntrainedWorkerStatus initialStatus = UntrainedWorkerStatus.Free, bool countAsNewHire = true)
    {
        Worker worker = new Worker(nextWorkerId++, WorkerType.Untrained);
        worker.SetUntrainedStatus(initialStatus);
        worker.OnStatusChanged += OnWorkerStatusChanged;
        
        allWorkers.Add(worker);
        OnWorkerStatsChanged?.Invoke();
        
        if (!isInitializing && countAsNewHire)
            IncrementNewWorkersHired();
        
        GameLogPanel.Instance.LogWorkerAction($"Created untrained worker: worker.Id");
        Debug.Log($"Created untrained worker: {worker} (countAsNewHire={countAsNewHire})");
        return worker;
    }
    
    // Worker status change handler
    void OnWorkerStatusChanged(Worker worker)
    {
        OnWorkerStatsChanged?.Invoke();
        Debug.Log($"Worker status changed: {worker}");
    }
    
    // Worker assignment methods
    public bool TryAssignWorkersToBuilding(int buildingId, int requiredWorkforce = 4)
    {
        List<Worker> availableWorkers = GetAvailableWorkers();
        List<Worker> selectedWorkers = new List<Worker>();
        int totalWorkforce = 0;
        
        var trainedWorkers = availableWorkers.Where(w => w.Type == WorkerType.Trained).ToList();
        var untrainedWorkers = availableWorkers.Where(w => w.Type == WorkerType.Untrained).ToList();
        
        foreach (Worker worker in trainedWorkers)
        {
            if (totalWorkforce >= requiredWorkforce) break;
            selectedWorkers.Add(worker);
            totalWorkforce += worker.WorkforceValue;
        }
        
        foreach (Worker worker in untrainedWorkers)
        {
            if (totalWorkforce >= requiredWorkforce) break;
            selectedWorkers.Add(worker);
            totalWorkforce += worker.WorkforceValue;
        }
        
        if (totalWorkforce < requiredWorkforce)
        {
            Debug.LogWarning($"Not enough available workforce for building {buildingId}. Required: {requiredWorkforce}, Available: {totalWorkforce}");
            return false;
        }
        
        foreach (Worker worker in selectedWorkers)
        {
            worker.TryAssignToBuilding(buildingId);
        }
        
        Debug.Log($"Assigned {selectedWorkers.Count} workers (workforce: {totalWorkforce}) to building {buildingId}");
        return true;
    }

    public void ReleaseWorkersFromBuilding(int buildingId)
    {
        List<Worker> buildingWorkers = GetWorkersByBuildingId(buildingId);

        foreach (Worker worker in buildingWorkers)
        {
            worker.ReleaseFromBuilding();
        }
        GameLogPanel.Instance.LogWorkerAction($"Released {buildingWorkers.Count} workers from building {buildingId}");
        Debug.Log($"Released {buildingWorkers.Count} workers from building {buildingId}");
    }
    
    // Worker retrieval methods
    public List<Worker> GetAllWorkers() { return new List<Worker>(allWorkers); }
    public List<Worker> GetAvailableWorkers() { return allWorkers.Where(w => w.IsAvailable).ToList(); }
    public List<Worker> GetWorkingWorkers() { return allWorkers.Where(w => w.IsWorking).ToList(); }
    public List<Worker> GetWorkersByType(WorkerType type) { return allWorkers.Where(w => w.Type == type).ToList(); }
    public List<Worker> GetWorkersByBuildingId(int buildingId) { return allWorkers.Where(w => w.AssignedBuildingId == buildingId).ToList(); }
    
    // Statistics methods
    public WorkerStatistics GetWorkerStatistics()
    {
        WorkerStatistics stats = new WorkerStatistics();
        
        foreach (Worker worker in allWorkers)
        {
            if (worker.Type == WorkerType.Trained)
            {
                switch (worker.GetCurrentStatus())
                {
                    case "Working": stats.trainedWorking++; break;
                    case "Free": stats.trainedFree++; break;
                    case "NotArrived": stats.trainedNotArrived++; break;
                }
            }
            else
            {
                switch (worker.GetCurrentStatus())
                {
                    case "Working": stats.untrainedWorking++; break;
                    case "Free": stats.untrainedFree++; break;
                    case "NotArrived": stats.untrainedNotArrived++; break;
                    case "Training": stats.untrainedTraining++; break;
                }
            }
        }
        
        return stats;
    }
    
    public int GetTotalAvailableWorkforce() { return GetAvailableWorkers().Sum(w => w.WorkforceValue); }
    public int GetTotalWorkforce() { return allWorkers.Sum(w => w.WorkforceValue); }
    public int GetTrainedWorkersCount() { return GetWorkersByType(WorkerType.Trained).Count; }
    public int GetUntrainedWorkersCount() { return GetWorkersByType(WorkerType.Untrained).Count; }
    public int GetAvailableUntrainedWorkers() { return GetWorkersByType(WorkerType.Untrained).Count(w => w.IsAvailable); }
    public int GetAvailableTrainedWorkers() { return GetWorkersByType(WorkerType.Trained).Count(w => w.IsAvailable); }

    public float GetIdleWorkerPercentage()
    {
        int totalWorkers = allWorkers.Count;
        int idleWorkers = allWorkers.Count(w => w.IsAvailable);
        return totalWorkers > 0 ? (float)idleWorkers / totalWorkers * 100 : 0;
    }

    public void ReturnWorkersFromBuilding(int buildingId, int workforceAmount=4)
    {
        List<Worker> buildingWorkers = GetWorkersByBuildingId(buildingId);
        
        if (buildingWorkers.Count == 0)
        {
            Debug.LogWarning($"No workers found assigned to building {buildingId}");
            return;
        }
        
        int workforceReturned = 0;
        int workersReleased = 0;
        
        foreach (Worker worker in buildingWorkers)
        {
            worker.ReleaseFromBuilding();
            workforceReturned += worker.WorkforceValue;
            workersReleased++;
        }
        
        OnWorkerStatsChanged?.Invoke();
        
        Debug.Log($"Returned {workersReleased} workers (workforce: {workforceReturned}) from building {buildingId} to available pool");
        GameLogPanel.Instance.LogWorkerAction($"Returned {workersReleased} workers (workforce: {workforceReturned}) from building {buildingId}");
    }

    public void PrintWorkerStatistics()
    {
        WorkerStatistics stats = GetWorkerStatistics();
        Debug.Log("=== WORKER STATISTICS ===");
        Debug.Log($"Trained Workers - Working: {stats.trainedWorking}, Free: {stats.trainedFree}, Not Arrived: {stats.trainedNotArrived}");
        Debug.Log($"Untrained Workers - Working: {stats.untrainedWorking}, Free: {stats.untrainedFree}, Training: {stats.untrainedTraining}");
        Debug.Log($"Total Workers: {allWorkers.Count}, Available Workforce: {GetTotalAvailableWorkforce()}, Total Workforce: {GetTotalWorkforce()}");
    }

    public void RemoveWorker(Worker worker)
    {
        if (allWorkers.Contains(worker))
        {
            worker.OnStatusChanged -= OnWorkerStatusChanged;
            allWorkers.Remove(worker);
            OnWorkerStatsChanged?.Invoke();
        }
    }
    
    public void IncrementNewWorkersHired()
    {
        newWorkersHiredToday++;
        Debug.Log($"[WorkerSystem] newWorkersHiredToday incremented to {newWorkersHiredToday}");
    }
    
    public int GetNewWorkersHiredToday()
    {
        return newWorkersHiredToday;
    }
    
    public int GetNewWorkersHiredOnDay(int Day)
    {
        if (newWorkersHiredEachDay.ContainsKey(Day))
            return newWorkersHiredEachDay[Day];
        return 0;
    }
    
    /// <summary>
    /// FIX: Explicit save-and-reset, called by DailyReportData.PrepareForNewDay().
    /// Replaces the old OnDayChanged-based ResetDailyHiredWorkersCount.
    /// Guaranteed to run AFTER report reads the data, BEFORE new day starts.
    /// </summary>
    public void SaveAndResetDailyHiredCount(int completedDay)
    {
        Debug.Log($"[WorkerSystem] Saving hired count for Day {completedDay}: {newWorkersHiredToday}. Resetting to 0.");
        newWorkersHiredEachDay[completedDay] = newWorkersHiredToday;
        newWorkersHiredToday = 0;
    }
    
    [ContextMenu("Add Trained Worker")]
    public void AddTrainedWorkerForTesting() { CreateTrainedWorker(); }
    
    [ContextMenu("Add Untrained Worker")]
    public void AddUntrainedWorkerForTesting() { CreateUntrainedWorker(); }
    
    [ContextMenu("Print Worker Stats")]
    public void PrintStatsForTesting() { PrintWorkerStatistics(); }
}

[System.Serializable]
public class WorkerStatistics
{
    [Header("Trained Workers")]
    public int trainedWorking = 0;
    public int trainedFree = 0;
    public int trainedNotArrived = 0;
    
    [Header("Untrained Workers")]
    public int untrainedWorking = 0;
    public int untrainedFree = 0;
    public int untrainedTraining = 0;
    public int untrainedNotArrived = 0;
    
    public int GetTotalTrained() { return trainedWorking + trainedFree + trainedNotArrived; }
    public int GetTotalUntrained() { return untrainedWorking + untrainedFree + untrainedTraining; }
    public int GetTotalWorkers() { return GetTotalTrained() + GetTotalUntrained(); }
    public int GetAvailableWorkforce() { return (trainedFree * 2) + untrainedFree; }
}