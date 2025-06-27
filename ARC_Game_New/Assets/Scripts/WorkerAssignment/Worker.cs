using UnityEngine;
using System;

public enum WorkerType
{
    Trained,    // Responsive worker - provides 2 workforce
    Untrained   // Event-based worker - provides 1 workforce
}

public enum TrainedWorkerStatus
{
    Working,     // Currently assigned to a building
    Free,        // Available for assignment
    NotArrived   // Will arrive later
}

public enum UntrainedWorkerStatus
{
    Working,     // Currently assigned to a building
    Free,        // Available for assignment
    Training     // Currently being trained (might become trained worker later)
}

[System.Serializable]
public class Worker
{
    [SerializeField] private int workerId;
    [SerializeField] private WorkerType workerType;
    [SerializeField] private TrainedWorkerStatus trainedStatus;
    [SerializeField] private UntrainedWorkerStatus untrainedStatus;
    [SerializeField] private int assignedBuildingId = -1; // -1 means not assigned
    
    // Events
    public event Action<Worker> OnStatusChanged;
    
    public Worker(int id, WorkerType type)
    {
        workerId = id;
        workerType = type;
        
        if (type == WorkerType.Trained)
        {
            trainedStatus = TrainedWorkerStatus.Free;
        }
        else
        {
            untrainedStatus = UntrainedWorkerStatus.Free;
        }
    }
    
    // Properties
    public int WorkerId => workerId;
    public WorkerType Type => workerType;
    public int WorkforceValue => workerType == WorkerType.Trained ? 2 : 1;
    public bool IsAvailable => GetCurrentStatus() == "Free";
    public bool IsWorking => GetCurrentStatus() == "Working";
    public int AssignedBuildingId => assignedBuildingId;
    
    // Status management
    public string GetCurrentStatus()
    {
        if (workerType == WorkerType.Trained)
        {
            return trainedStatus.ToString();
        }
        else
        {
            return untrainedStatus.ToString();
        }
    }
    
    public void SetTrainedStatus(TrainedWorkerStatus status)
    {
        if (workerType == WorkerType.Trained)
        {
            trainedStatus = status;
            OnStatusChanged?.Invoke(this);
        }
        else
        {
            Debug.LogWarning($"Cannot set trained status on untrained worker {workerId}");
        }
    }
    
    public void SetUntrainedStatus(UntrainedWorkerStatus status)
    {
        if (workerType == WorkerType.Untrained)
        {
            untrainedStatus = status;
            OnStatusChanged?.Invoke(this);
        }
        else
        {
            Debug.LogWarning($"Cannot set untrained status on trained worker {workerId}");
        }
    }
    
    // Assignment management
    public bool TryAssignToBuilding(int buildingId)
    {
        if (!IsAvailable)
        {
            Debug.LogWarning($"Worker {workerId} is not available for assignment (Current status: {GetCurrentStatus()})");
            return false;
        }
        
        assignedBuildingId = buildingId;
        
        // Set status to working
        if (workerType == WorkerType.Trained)
        {
            SetTrainedStatus(TrainedWorkerStatus.Working);
        }
        else
        {
            SetUntrainedStatus(UntrainedWorkerStatus.Working);
        }
        
        Debug.Log($"Worker {workerId} ({workerType}) assigned to building {buildingId}");
        return true;
    }
    
    public void ReleaseFromBuilding()
    {
        if (assignedBuildingId == -1)
        {
            Debug.LogWarning($"Worker {workerId} is not assigned to any building");
            return;
        }
        
        int previousBuildingId = assignedBuildingId;
        assignedBuildingId = -1;
        
        // Set status to free
        if (workerType == WorkerType.Trained)
        {
            SetTrainedStatus(TrainedWorkerStatus.Free);
        }
        else
        {
            SetUntrainedStatus(UntrainedWorkerStatus.Free);
        }
        
        Debug.Log($"Worker {workerId} ({workerType}) released from building {previousBuildingId}");
    }
    
    // Special status transitions
    public void StartTraining()
    {
        if (workerType == WorkerType.Untrained && untrainedStatus == UntrainedWorkerStatus.Free)
        {
            SetUntrainedStatus(UntrainedWorkerStatus.Training);
            Debug.Log($"Untrained worker {workerId} started training");
        }
        else
        {
            Debug.LogWarning($"Cannot start training for worker {workerId} (Type: {workerType}, Status: {GetCurrentStatus()})");
        }
    }
    
    public void ArriveAtSite()
    {
        if (workerType == WorkerType.Trained && trainedStatus == TrainedWorkerStatus.NotArrived)
        {
            SetTrainedStatus(TrainedWorkerStatus.Free);
            Debug.Log($"Trained worker {workerId} has arrived and is now available");
        }
        else
        {
            Debug.LogWarning($"Cannot mark arrival for worker {workerId} (Type: {workerType}, Status: {GetCurrentStatus()})");
        }
    }
    
    // Convert untrained to trained (for future training system)
    public Worker ConvertToTrained()
    {
        if (workerType == WorkerType.Untrained && untrainedStatus == UntrainedWorkerStatus.Training)
        {
            // Create new trained worker
            Worker trainedWorker = new Worker(workerId, WorkerType.Trained);
            trainedWorker.SetTrainedStatus(TrainedWorkerStatus.Free);
            
            Debug.Log($"Untrained worker {workerId} has been converted to trained worker");
            return trainedWorker;
        }
        else
        {
            Debug.LogWarning($"Cannot convert worker {workerId} to trained (Type: {workerType}, Status: {GetCurrentStatus()})");
            return null;
        }
    }
    
    // Debug info
    public override string ToString()
    {
        string assignment = assignedBuildingId == -1 ? "Unassigned" : $"Building {assignedBuildingId}";
        return $"Worker {workerId} ({workerType}) - Status: {GetCurrentStatus()}, Assignment: {assignment}, Workforce: {WorkforceValue}";
    }
}