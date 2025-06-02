using System;
using System.Collections.Generic;
using CityBuilderCore;
using UnityEngine;

/// <summary>
/// Manages worker assignment and allocation across facilities
/// </summary>
public class WorkerManager : MonoBehaviour
{
    [Header("Worker Configuration")]
    [SerializeField] private int totalWorkersPerRound = 20;
    [SerializeField] private int currentlyAssignedWorkers = 0;
    
    [Header("Worker Types")]
    [SerializeField] private List<WorkerType> workerTypes = new List<WorkerType>();
    
    // Worker assignment tracking
    private Dictionary<string, FacilityWorkerAssignment> _facilityAssignments = new Dictionary<string, FacilityWorkerAssignment>();
    private Dictionary<string, int> _workerTypeAvailable = new Dictionary<string, int>();
    
    // Events
    public event Action<string, int> OnWorkersAssigned; // facilityId, workerCount
    public event Action<string, int> OnWorkersRemoved; // facilityId, workerCount
    public event Action OnWorkerAssignmentChanged;
    
    public int TotalWorkers => totalWorkersPerRound;
    public int AssignedWorkers => currentlyAssignedWorkers;
    public int AvailableWorkers => totalWorkersPerRound - currentlyAssignedWorkers;
    
    private void Start()
    {
        InitializeWorkerTypes();
        ResetWorkerAssignments();
    }
    
    /// <summary>
    /// Initialize available worker types and their counts
    /// </summary>
    private void InitializeWorkerTypes()
    {
        _workerTypeAvailable.Clear();
        
        // If no worker types defined, create default general workers
        if (workerTypes.Count == 0)
        {
            workerTypes.Add(new WorkerType 
            { 
                typeName = "General Worker", 
                maxCount = totalWorkersPerRound,
                skillLevel = 1
            });
        }
        
        foreach (var workerType in workerTypes)
        {
            _workerTypeAvailable[workerType.typeName] = workerType.maxCount;
        }
        
        Debug.Log($"[WorkerManager] Initialized {workerTypes.Count} worker types");
    }
    
    /// <summary>
    /// Reset all worker assignments for a new round
    /// </summary>
    public void ResetWorkerAssignments()
    {
        _facilityAssignments.Clear();
        currentlyAssignedWorkers = 0;
        InitializeWorkerTypes();
        
        Debug.Log("[WorkerManager] Reset worker assignments for new round");
        OnWorkerAssignmentChanged?.Invoke();
    }
    
    /// <summary>
    /// Set absolute worker count for a facility (used by UI)
    /// </summary>
    public bool SetWorkersForFacility(string facilityId, int targetWorkerCount, string workerType = "General Worker")
    {
        // Get current assignment
        int currentWorkers = GetTotalAssignedWorkers(facilityId);
        
        if (targetWorkerCount == currentWorkers)
            return true; // No change needed
        
        if (targetWorkerCount > currentWorkers)
        {
            // Add workers
            int workersToAdd = targetWorkerCount - currentWorkers;
            return AssignWorkersToFacility(facilityId, workersToAdd, workerType);
        }
        else if (targetWorkerCount < currentWorkers)
        {
            // Remove workers
            int workersToRemove = currentWorkers - targetWorkerCount;
            return RemoveWorkersFromFacility(facilityId, workersToRemove, workerType);
        }
        
        return true;
    }
    
    /// <summary>
    /// Get current worker count for a facility (used by UI)
    /// </summary>
    public int GetCurrentWorkerCount(string facilityId)
    {
        return GetTotalAssignedWorkers(facilityId);
    }
    
    /// <summary>
    /// Assign workers to a specific facility
    /// </summary>
    public bool AssignWorkersToFacility(string facilityId, int workerCount, string workerType = "General Worker")
    {
        // Check if we have enough available workers
        if (AvailableWorkers < workerCount)
        {
            Debug.LogWarning($"[WorkerManager] Not enough available workers. Requested: {workerCount}, Available: {AvailableWorkers}");
            return false;
        }
        
        // Check if worker type is available
        if (!_workerTypeAvailable.ContainsKey(workerType) || _workerTypeAvailable[workerType] < workerCount)
        {
            Debug.LogWarning($"[WorkerManager] Not enough {workerType} workers available");
            return false;
        }
        
        // Get the facility from BuildingSystem
        var facility = GetFacilityById(facilityId);
        if (facility == null)
        {
            Debug.LogWarning($"[WorkerManager] Facility {facilityId} not found");
            // For testing purposes, allow assignment even if facility not found
            // return false;
        }
        
        // Check if facility can accept workers
        if (facility != null && !CanFacilityAcceptWorkers(facility, workerCount))
        {
            Debug.LogWarning($"[WorkerManager] Facility {facilityId} cannot accept {workerCount} workers");
            return false;
        }
        
        // Create or update facility assignment
        if (!_facilityAssignments.ContainsKey(facilityId))
        {
            _facilityAssignments[facilityId] = new FacilityWorkerAssignment
            {
                facilityId = facilityId,
                assignedWorkers = new Dictionary<string, int>()
            };
        }
        
        var assignment = _facilityAssignments[facilityId];
        
        // Add workers to assignment
        if (!assignment.assignedWorkers.ContainsKey(workerType))
            assignment.assignedWorkers[workerType] = 0;
            
        assignment.assignedWorkers[workerType] += workerCount;
        
        // Update counters
        currentlyAssignedWorkers += workerCount;
        _workerTypeAvailable[workerType] -= workerCount;
        
        // Notify facility of worker assignment
        if (facility != null)
        {
            NotifyFacilityOfWorkerAssignment(facility, workerCount, workerType);
        }
        
        Debug.Log($"[WorkerManager] Assigned {workerCount} {workerType} workers to {facilityId}");
        
        // Trigger events
        OnWorkersAssigned?.Invoke(facilityId, workerCount);
        OnWorkerAssignmentChanged?.Invoke();
        
        return true;
    }
    
    /// <summary>
    /// Remove workers from a facility
    /// </summary>
    public bool RemoveWorkersFromFacility(string facilityId, int workerCount, string workerType = "General Worker")
    {
        if (!_facilityAssignments.ContainsKey(facilityId))
        {
            Debug.LogWarning($"[WorkerManager] No workers assigned to facility {facilityId}");
            return false;
        }
        
        var assignment = _facilityAssignments[facilityId];
        
        if (!assignment.assignedWorkers.ContainsKey(workerType) || 
            assignment.assignedWorkers[workerType] < workerCount)
        {
            Debug.LogWarning($"[WorkerManager] Not enough {workerType} workers assigned to {facilityId}");
            return false;
        }
        
        // Remove workers
        assignment.assignedWorkers[workerType] -= workerCount;
        currentlyAssignedWorkers -= workerCount;
        _workerTypeAvailable[workerType] += workerCount;
        
        // Clean up empty assignments
        if (assignment.assignedWorkers[workerType] == 0)
            assignment.assignedWorkers.Remove(workerType);
            
        if (assignment.assignedWorkers.Count == 0)
            _facilityAssignments.Remove(facilityId);
        
        Debug.Log($"[WorkerManager] Removed {workerCount} {workerType} workers from {facilityId}");
        
        // Trigger events
        OnWorkersRemoved?.Invoke(facilityId, workerCount);
        OnWorkerAssignmentChanged?.Invoke();
        
        return true;
    }
    
    /// <summary>
    /// Get worker assignment for a specific facility
    /// </summary>
    public FacilityWorkerAssignment GetFacilityAssignment(string facilityId)
    {
        return _facilityAssignments.TryGetValue(facilityId, out var assignment) ? assignment : null;
    }
    
    /// <summary>
    /// Check if a facility is properly staffed
    /// </summary>
    public bool IsFacilityStaffed(string facilityId)
    {
        var facility = GetFacilityById(facilityId);
        if (facility == null) 
        {
            // For testing, assume facility is staffed if workers are assigned
            return GetTotalAssignedWorkers(facilityId) > 0;
        }
        
        var assignment = GetFacilityAssignment(facilityId);
        if (assignment == null) return false;
        
        // Check if facility has minimum required workers
        return GetTotalAssignedWorkers(facilityId) >= GetMinimumWorkersRequired(facility);
    }
    
    /// <summary>
    /// Get total workers assigned to a facility
    /// </summary>
    public int GetTotalAssignedWorkers(string facilityId)
    {
        var assignment = GetFacilityAssignment(facilityId);
        if (assignment == null) return 0;
        
        int total = 0;
        foreach (var workerCount in assignment.assignedWorkers.Values)
        {
            total += workerCount;
        }
        return total;
    }
    
    /// <summary>
    /// Get available workers of a specific type
    /// </summary>
    public int GetAvailableWorkersByType(string workerType)
    {
        return _workerTypeAvailable.TryGetValue(workerType, out var count) ? count : 0;
    }
    
    /// <summary>
    /// Get all facility assignments
    /// </summary>
    public IReadOnlyDictionary<string, FacilityWorkerAssignment> GetAllAssignments()
    {
        return _facilityAssignments;
    }
    
    /// <summary>
    /// Get all facilities that need workers
    /// </summary>
    public List<Building> GetFacilitiesNeedingWorkers()
    {
        List<Building> facilitiesNeedingWorkers = new List<Building>();
        
        if (BuildingSystem.Instance == null) return facilitiesNeedingWorkers;
        
        var allFacilities = BuildingSystem.Instance.GetAllFacilities();
        
        foreach (var facility in allFacilities)
        {
            if (facility != null)
            {
                int currentWorkers = GetTotalAssignedWorkers(facility.GetInstanceID().ToString());
                int requiredWorkers = GetMinimumWorkersRequired(facility);
                
                if (currentWorkers < requiredWorkers)
                {
                    facilitiesNeedingWorkers.Add(facility);
                }
            }
        }
        
        return facilitiesNeedingWorkers;
    }
    
    /// <summary>
    /// Check if all facilities are properly staffed
    /// </summary>
    public bool AreAllFacilitiesStaffed()
    {
        if (BuildingSystem.Instance == null) return true;
        
        var allFacilities = BuildingSystem.Instance.GetAllFacilities();
        
        foreach (var facility in allFacilities)
        {
            if (facility != null)
            {
                string facilityId = facility.GetInstanceID().ToString();
                if (!IsFacilityStaffed(facilityId))
                {
                    return false;
                }
            }
        }
        
        return true;
    }
    
    // Helper methods
    private Building GetFacilityById(string facilityId)
    {
        if (BuildingSystem.Instance == null) return null;
        
        return BuildingSystem.Instance.GetFacilityById(facilityId);
    }
    
    private bool CanFacilityAcceptWorkers(Building facility, int workerCount)
    {
        // Check if building has worker assignment capability
        var workerAssignable = facility.GetComponent<IWorkerAssignable>();
        if (workerAssignable != null)
        {
            return workerAssignable.CanAcceptWorkers(workerCount);
        }
        
        // Default implementation - most buildings can accept 1-5 workers
        int currentWorkers = GetTotalAssignedWorkers(facility.GetInstanceID().ToString());
        int maxWorkers = GetMaximumWorkersAllowed(facility);
        
        return (currentWorkers + workerCount) <= maxWorkers;
    }
    
    private int GetMinimumWorkersRequired(Building facility)
    {
        // Check if building has worker assignment capability
        var workerAssignable = facility.GetComponent<IWorkerAssignable>();
        if (workerAssignable != null)
        {
            return workerAssignable.GetMinimumWorkersRequired();
        }
        
        // Default implementation based on facility type
        if (facility.name.ToLower().Contains("shelter"))
            return 2;
        else if (facility.name.ToLower().Contains("kitchen"))
            return 2;
        else if (facility.name.ToLower().Contains("community"))
            return 1;
        else
            return 1;
    }
    
    private int GetMaximumWorkersAllowed(Building facility)
    {
        // Default implementation based on facility type
        if (facility.name.ToLower().Contains("shelter"))
            return 4;
        else if (facility.name.ToLower().Contains("kitchen"))
            return 3;
        else if (facility.name.ToLower().Contains("community"))
            return 2;
        else
            return 3;
    }
    
    private void NotifyFacilityOfWorkerAssignment(Building facility, int workerCount, string workerType)
    {
        // Notify facility components of worker assignment
        var facilityComponents = facility.GetComponents<MonoBehaviour>();
        
        foreach (var component in facilityComponents)
        {
            if (component is IWorkerAssignable assignable)
            {
                assignable.OnWorkersAssigned(workerCount, workerType);
            }
        }
    }
    
    /// <summary>
    /// Get worker assignment statistics
    /// </summary>
    public WorkerStatistics GetWorkerStatistics()
    {
        return new WorkerStatistics
        {
            totalWorkers = totalWorkersPerRound,
            assignedWorkers = currentlyAssignedWorkers,
            availableWorkers = AvailableWorkers,
            facilitiesStaffed = GetStaffedFacilitiesCount(),
            totalFacilities = GetTotalFacilitiesCount(),
            assignmentRate = totalWorkersPerRound > 0 ? (float)currentlyAssignedWorkers / totalWorkersPerRound : 0f
        };
    }
    
    private int GetStaffedFacilitiesCount()
    {
        int count = 0;
        if (BuildingSystem.Instance != null)
        {
            var allFacilities = BuildingSystem.Instance.GetAllFacilities();
            foreach (var facility in allFacilities)
            {
                if (facility != null && IsFacilityStaffed(facility.GetInstanceID().ToString()))
                {
                    count++;
                }
            }
        }
        return count;
    }
    
    private int GetTotalFacilitiesCount()
    {
        if (BuildingSystem.Instance != null)
        {
            return BuildingSystem.Instance.GetAllFacilities().Count;
        }
        return 0;
    }
}

/// <summary>
/// Data structure for worker assignments to facilities
/// </summary>
[System.Serializable]
public class FacilityWorkerAssignment
{
    public string facilityId;
    public Dictionary<string, int> assignedWorkers = new Dictionary<string, int>();
    public DateTime assignmentTime = DateTime.Now;
}

/// <summary>
/// Worker type configuration
/// </summary>
[System.Serializable]
public class WorkerType
{
    public string typeName = "General Worker";
    public int maxCount = 10;
    public int skillLevel = 1;
    public float efficiency = 1.0f;
    public List<string> specializations = new List<string>();
}

/// <summary>
/// Worker statistics data structure
/// </summary>
[System.Serializable]
public class WorkerStatistics
{
    public int totalWorkers;
    public int assignedWorkers;
    public int availableWorkers;
    public int facilitiesStaffed;
    public int totalFacilities;
    public float assignmentRate;
}

/// <summary>
/// Interface for facilities that can receive worker assignments
/// </summary>
public interface IWorkerAssignable
{
    void OnWorkersAssigned(int workerCount, string workerType);
    void OnWorkersRemoved(int workerCount, string workerType);
    bool CanAcceptWorkers(int workerCount);
    int GetMinimumWorkersRequired();
}
