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

        // Subscribe to day change event
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnDayChanged += ResetDailyHiredWorkersCount;
        }
    }

    void OnDestroy()
    {
        // ALWAYS unsubscribe when destroyed to prevent memory leaks
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnDayChanged -= ResetDailyHiredWorkersCount;
        }
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
    }
    
    // Worker creation methods
    public Worker CreateTrainedWorker(TrainedWorkerStatus initialStatus = TrainedWorkerStatus.Free)
    {
        Worker worker = new Worker(nextWorkerId++, WorkerType.Trained);
        worker.SetTrainedStatus(initialStatus);
        worker.OnStatusChanged += OnWorkerStatusChanged;
        
        allWorkers.Add(worker);
        OnWorkerStatsChanged?.Invoke();
        GameLogPanel.Instance.LogWorkerAction($"Created trained worker: {worker}");
        Debug.Log($"Created trained worker: {worker}");
        return worker;
    }
    
    public Worker CreateUntrainedWorker(UntrainedWorkerStatus initialStatus = UntrainedWorkerStatus.Free)
    {
        Worker worker = new Worker(nextWorkerId++, WorkerType.Untrained);
        worker.SetUntrainedStatus(initialStatus);
        worker.OnStatusChanged += OnWorkerStatusChanged;
        
        allWorkers.Add(worker);
        OnWorkerStatsChanged?.Invoke();
        GameLogPanel.Instance.LogWorkerAction($"Created untrained worker: worker.Id");
        Debug.Log($"Created untrained worker: {worker}");
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
        
        // Greedy selection: prioritize trained workers first
        var trainedWorkers = availableWorkers.Where(w => w.Type == WorkerType.Trained).ToList();
        var untrainedWorkers = availableWorkers.Where(w => w.Type == WorkerType.Untrained).ToList();
        
        // First, use trained workers
        foreach (Worker worker in trainedWorkers)
        {
            if (totalWorkforce >= requiredWorkforce) break;
            
            selectedWorkers.Add(worker);
            totalWorkforce += worker.WorkforceValue;
        }
        
        // Then, use untrained workers if needed
        foreach (Worker worker in untrainedWorkers)
        {
            if (totalWorkforce >= requiredWorkforce) break;
            
            selectedWorkers.Add(worker);
            totalWorkforce += worker.WorkforceValue;
        }
        
        // Check if we have enough workforce
        if (totalWorkforce < requiredWorkforce)
        {
            Debug.LogWarning($"Not enough available workforce for building {buildingId}. Required: {requiredWorkforce}, Available: {totalWorkforce}");
            return false;
        }
        
        // Assign selected workers
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
    public List<Worker> GetAllWorkers()
    {
        return new List<Worker>(allWorkers);
    }
    
    public List<Worker> GetAvailableWorkers()
    {
        return allWorkers.Where(w => w.IsAvailable).ToList();
    }
    
    public List<Worker> GetWorkingWorkers()
    {
        return allWorkers.Where(w => w.IsWorking).ToList();
    }
    
    public List<Worker> GetWorkersByType(WorkerType type)
    {
        return allWorkers.Where(w => w.Type == type).ToList();
    }
    
    public List<Worker> GetWorkersByBuildingId(int buildingId)
    {
        return allWorkers.Where(w => w.AssignedBuildingId == buildingId).ToList();
    }
    
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
                    case "Working":
                        stats.trainedWorking++;
                        break;
                    case "Free":
                        stats.trainedFree++;
                        break;
                    case "NotArrived":
                        stats.trainedNotArrived++;
                        break;
                }
            }
            else // Untrained
            {
                switch (worker.GetCurrentStatus())
                {
                    case "Working":
                        stats.untrainedWorking++;
                        break;
                    case "Free":
                        stats.untrainedFree++;
                        break;
                    case "NotArrived":
                        stats.untrainedNotArrived++;
                        break;
                    case "Training":
                        stats.untrainedTraining++;
                        break;
                }
            }
        }
        
        return stats;
    }
    
    public int GetTotalAvailableWorkforce()
    {
        return GetAvailableWorkers().Sum(w => w.WorkforceValue);
    }
    
    public int GetTotalWorkforce()
    {
        return allWorkers.Sum(w => w.WorkforceValue);
    }
    
    public int GetTrainedWorkersCount()
    {
        return GetWorkersByType(WorkerType.Trained).Count;
    }

    public int GetUntrainedWorkersCount()
    {
        return GetWorkersByType(WorkerType.Untrained).Count;
    }

    public int GetAvailableUntrainedWorkers()
    {
        return GetWorkersByType(WorkerType.Untrained).Count(w => w.IsAvailable);
    }

    public int GetAvailableTrainedWorkers()
    {
        return GetWorkersByType(WorkerType.Trained).Count(w => w.IsAvailable);
    }

    public float GetIdleWorkerPercentage()
    {
        int totalWorkers = allWorkers.Count;
        int idleWorkers = allWorkers.Count(w => w.IsAvailable);

        return totalWorkers > 0 ? (float)idleWorkers / totalWorkers * 100 : 0;
    }

    /// <summary>
    /// Return workers from a building back to the available pool
    /// </summary>
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

    // Debug and testing methods
    public void PrintWorkerStatistics()
    {
        WorkerStatistics stats = GetWorkerStatistics();

        Debug.Log("=== WORKER STATISTICS ===");
        Debug.Log($"Trained Workers - Working: {stats.trainedWorking}, Free: {stats.trainedFree}, Not Arrived: {stats.trainedNotArrived}");
        Debug.Log($"Untrained Workers - Working: {stats.untrainedWorking}, Free: {stats.untrainedFree}, Training: {stats.untrainedTraining}");
        Debug.Log($"Total Workers: {allWorkers.Count}, Available Workforce: {GetTotalAvailableWorkforce()}, Total Workforce: {GetTotalWorkforce()}");
    }

    /// <summary>
    /// Remove a worker from the system (for training conversion)
    /// </summary>
    public void RemoveWorker(Worker worker)
    {
        if (allWorkers.Contains(worker))
        {
            // Unsubscribe from events
            worker.OnStatusChanged -= OnWorkerStatusChanged;

            // Remove from list
            allWorkers.Remove(worker);

            OnWorkerStatsChanged?.Invoke();
        }
    }
    public void IncrementNewWorkersHired()
    {
        newWorkersHiredToday++;
    }
    public int GetNewWorkersHiredToday()
    {
        return newWorkersHiredToday;
    }
    public void ResetDailyHiredWorkersCount(int newDay)
    {
        Debug.Log($"Day changed to {newDay}, resetting worker count");
        newWorkersHiredToday = 0;
    }
    
    // Test methods for demonstration
    [ContextMenu("Add Trained Worker")]
    public void AddTrainedWorkerForTesting()
    {
        CreateTrainedWorker();
    }
    
    [ContextMenu("Add Untrained Worker")]
    public void AddUntrainedWorkerForTesting()
    {
        CreateUntrainedWorker();
    }
    
    [ContextMenu("Print Worker Stats")]
    public void PrintStatsForTesting()
    {
        PrintWorkerStatistics();
    }
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
    
    public int GetTotalTrained()
    {
        return trainedWorking + trainedFree + trainedNotArrived;
    }
    
    public int GetTotalUntrained()
    {
        return untrainedWorking + untrainedFree + untrainedTraining;
    }
    
    public int GetTotalWorkers()
    {
        return GetTotalTrained() + GetTotalUntrained();
    }

    public int GetAvailableWorkforce()
    {
        return (trainedFree * 2) + untrainedFree; // Trained = 2 workforce, Untrained = 1
    }

}