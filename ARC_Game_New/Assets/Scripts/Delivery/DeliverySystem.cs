using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

[System.Serializable]
public class DeliveryTask
{
    public int taskId;
    public MonoBehaviour sourceBuilding; // Changed to MonoBehaviour to accept both Building and PrebuiltBuilding
    public MonoBehaviour destinationBuilding; // Changed to MonoBehaviour
    public ResourceType cargoType;
    public int quantity;
    public int priority = 1; // Higher number = higher priority
    public bool isUrgent = false;
    public float timeCreated;
    
    public DeliveryTask(MonoBehaviour source, MonoBehaviour destination, ResourceType cargo, int qty, int taskId)
    {
        this.taskId = taskId;
        this.sourceBuilding = source;
        this.destinationBuilding = destination;
        this.cargoType = cargo;
        this.quantity = qty;
        this.timeCreated = Time.time;
    }
    
    public override string ToString()
    {
        return $"Task {taskId}: {quantity} {cargoType} from {sourceBuilding.name} to {destinationBuilding.name}";
    }
    
    // Helper methods to get positions
    public Vector3 GetSourcePosition()
    {
        return sourceBuilding.transform.position;
    }
    
    public Vector3 GetDestinationPosition()
    {
        return destinationBuilding.transform.position;
    }
    
    // Helper methods to get road connection points
    public Vector3 GetSourceRoadConnection()
    {
        RoadConnection roadConnection = sourceBuilding.GetComponent<RoadConnection>();
        return roadConnection != null ? roadConnection.GetRoadConnectionPoint() : sourceBuilding.transform.position;
    }
    
    public Vector3 GetDestinationRoadConnection()
    {
        RoadConnection roadConnection = destinationBuilding.GetComponent<RoadConnection>();
        return roadConnection != null ? roadConnection.GetRoadConnectionPoint() : destinationBuilding.transform.position;
    }
}

public class DeliverySystem : MonoBehaviour
{
    [Header("Vehicle Management")]
    public List<Vehicle> availableVehicles = new List<Vehicle>();
    public GameObject vehiclePrefab; // For spawning new vehicles
    
    [Header("Task Management")]
    public int maxQueuedTasks = 50;
    public float taskAssignmentInterval = 1f; // Check for new assignments every second
    
    [Header("Auto-Task Generation")]
    public bool enableAutoTasks = false;
    public float autoTaskInterval = 10f; // Generate tasks every 10 seconds
    public bool prioritizeFoodDelivery = true;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Task management
    private Queue<DeliveryTask> pendingTasks = new Queue<DeliveryTask>();
    private List<DeliveryTask> activeTasks = new List<DeliveryTask>();
    private List<DeliveryTask> completedTasks = new List<DeliveryTask>();
    
    private int nextTaskId = 1;
    private float lastTaskAssignment = 0f;
    private float lastAutoTaskGeneration = 0f;
    
    // Events
    public event Action<DeliveryTask> OnTaskCreated;
    public event Action<DeliveryTask, Vehicle> OnTaskAssigned;
    public event Action<DeliveryTask> OnTaskCompleted;

    void Start()
    {
        // Find vehicles if not assigned
        if (availableVehicles.Count == 0)
        {
            Vehicle[] foundVehicles = FindObjectsOfType<Vehicle>();
            availableVehicles.AddRange(foundVehicles);
        }

        // Subscribe to vehicle events
        foreach (Vehicle vehicle in availableVehicles)
        {
            vehicle.OnDeliveryCompleted += OnVehicleDeliveryCompleted;
        }

        Debug.Log($"Delivery System initialized with {availableVehicles.Count} vehicles");
        
        pendingTasks.Clear();
    }
    
    void Update()
    {
        // Assign pending tasks to available vehicles
        if (Time.time - lastTaskAssignment > taskAssignmentInterval)
        {
            AssignPendingTasks();
            lastTaskAssignment = Time.time;
        }
        
        // Generate automatic tasks
        /*if (enableAutoTasks && Time.time - lastAutoTaskGeneration > autoTaskInterval)
        {
            GenerateAutoTasks();
            lastAutoTaskGeneration = Time.time;
        }*/
    }
    
    /// <summary>
    /// Create a new delivery task
    /// </summary>
    public DeliveryTask CreateDeliveryTask(MonoBehaviour source, MonoBehaviour destination, ResourceType cargoType, int quantity, int priority = 1)
    {
        if (source == null || destination == null)
        {
            Debug.LogError("Cannot create delivery task with null buildings");
            return null;
        }
        
        if (quantity <= 0)
        {
            Debug.LogError("Cannot create delivery task with zero or negative quantity");
            return null;
        }
        
        DeliveryTask newTask = new DeliveryTask(source, destination, cargoType, quantity, nextTaskId++);
        newTask.priority = priority;
        
        // Check if we have space in queue
        if (pendingTasks.Count >= maxQueuedTasks)
        {
            Debug.LogWarning("Delivery task queue is full - cannot add new task");
            return null;
        }
        
        pendingTasks.Enqueue(newTask);
        OnTaskCreated?.Invoke(newTask);
        
        if (showDebugInfo)
            Debug.Log($"Created delivery task: {newTask}");
        
        return newTask;
    }
    
    /// <summary>
    /// Assign pending tasks to available vehicles
    /// </summary>
    void AssignPendingTasks()
    {
        if (pendingTasks.Count == 0) return;
        
        // Get available vehicles
        List<Vehicle> availableVehicleList = availableVehicles.Where(v => v.IsAvailable()).ToList();
        
        if (availableVehicleList.Count == 0) return;
        
        // Sort pending tasks by priority
        List<DeliveryTask> sortedTasks = pendingTasks.OrderByDescending(t => t.priority).ThenBy(t => t.timeCreated).ToList();
        
        foreach (DeliveryTask task in sortedTasks)
        {
            if (availableVehicleList.Count == 0) break;
            
            // Find suitable vehicle for this task
            Vehicle suitableVehicle = FindSuitableVehicle(task, availableVehicleList);
            
            if (suitableVehicle != null)
            {
                // Assign task to vehicle
                if (suitableVehicle.AssignDeliveryTask(task))
                {
                    pendingTasks = new Queue<DeliveryTask>(pendingTasks.Where(t => t != task));
                    activeTasks.Add(task);
                    availableVehicleList.Remove(suitableVehicle);
                    
                    OnTaskAssigned?.Invoke(task, suitableVehicle);
                    
                    if (showDebugInfo)
                        Debug.Log($"Assigned {task} to vehicle {suitableVehicle.GetVehicleName()}");
                }
            }
        }
    }
    
    /// <summary>
    /// Find the most suitable vehicle for a task
    /// </summary>
    Vehicle FindSuitableVehicle(DeliveryTask task, List<Vehicle> availableVehicleList)
    {
        Vehicle bestVehicle = null;
        float bestScore = -1f;
        
        foreach (Vehicle vehicle in availableVehicleList)
        {
            // Check if vehicle can handle this cargo type
            if (!vehicle.GetAllowedCargoTypes().Contains(task.cargoType))
                continue;
            
            // Check if vehicle has enough capacity
            if (vehicle.GetMaxCapacity() < task.quantity)
                continue;
            
            // Calculate suitability score
            float score = CalculateVehicleSuitability(vehicle, task);
            
            if (score > bestScore)
            {
                bestScore = score;
                bestVehicle = vehicle;
            }
        }
        
        return bestVehicle;
    }
    
    /// <summary>
    /// Calculate how suitable a vehicle is for a task (higher is better)
    /// </summary>
    float CalculateVehicleSuitability(Vehicle vehicle, DeliveryTask task)
    {
        float score = 0f;
        
        // Distance factor (closer vehicles are better)
        float distanceToSource = Vector3.Distance(vehicle.transform.position, task.GetSourcePosition());
        score += 100f / (1f + distanceToSource); // Inverse distance
        
        // Capacity efficiency (vehicles with capacity closer to task quantity are better)
        float capacityEfficiency = (float)task.quantity / vehicle.GetMaxCapacity();
        score += capacityEfficiency * 50f;
        
        // Speed factor
        score += vehicle.moveSpeed * 10f;
        
        return score;
    }
    
    /// <summary>
    /// Handle vehicle delivery completion
    /// </summary>
    void OnVehicleDeliveryCompleted(Vehicle vehicle)
    {
        // Find and complete the task
        DeliveryTask completedTask = activeTasks.FirstOrDefault(t => t == vehicle.GetCurrentTask());
        
        if (completedTask != null)
        {
            activeTasks.Remove(completedTask);
            completedTasks.Add(completedTask);
            
            OnTaskCompleted?.Invoke(completedTask);
            
            if (showDebugInfo)
                Debug.Log($"Completed delivery task: {completedTask}");
        }
    }
    
    /// <summary>
    /// Generate automatic delivery tasks based on building needs
    /// </summary>
    void GenerateAutoTasks()
    {
        // Find buildings that need food
        if (prioritizeFoodDelivery)
        {
            GenerateFoodDeliveryTasks();
        }
        
        // Find communities with people who need transport to shelters
        GeneratePopulationTransportTasks();
    }
    
    /// <summary>
    /// Generate food delivery tasks from kitchens to shelters
    /// </summary>
    void GenerateFoodDeliveryTasks()
    {
        // Find kitchens with food packs
        Building[] kitchens = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Kitchen).ToArray();
        
        // Find shelters that need food
        Building[] shelters = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        
        foreach (Building kitchen in kitchens)
        {
            BuildingResourceStorage kitchenStorage = kitchen.GetComponent<BuildingResourceStorage>();
            if (kitchenStorage == null) continue;
            
            int availableFoodPacks = kitchenStorage.GetResourceAmount(ResourceType.FoodPacks);
            if (availableFoodPacks <= 0) continue;
            
            // Find shelter that needs food most urgently
            Building neediestShelter = null;
            int highestNeed = 0;
            
            foreach (Building shelter in shelters)
            {
                BuildingResourceStorage shelterStorage = shelter.GetComponent<BuildingResourceStorage>();
                if (shelterStorage == null) continue;
                
                int availableSpace = shelterStorage.GetAvailableSpace(ResourceType.FoodPacks);
                int currentPopulation = shelterStorage.GetResourceAmount(ResourceType.Population);
                
                // Calculate need based on population vs food ratio
                int need = currentPopulation - shelterStorage.GetResourceAmount(ResourceType.FoodPacks);
                
                if (need > highestNeed && availableSpace > 0)
                {
                    highestNeed = need;
                    neediestShelter = shelter;
                }
            }
            
            // Create delivery task if we found a needy shelter
            if (neediestShelter != null && highestNeed > 0)
            {
                int deliveryAmount = Mathf.Min(availableFoodPacks, highestNeed, 5); // Limit to 5 per delivery
                CreateDeliveryTask(kitchen, neediestShelter, ResourceType.FoodPacks, deliveryAmount, 2);
            }
        }
    }
    
    /// <summary>
    /// Generate population transport tasks from communities to shelters/motels
    /// </summary>
    void GeneratePopulationTransportTasks()
    {
        // Find communities with population
        PrebuiltBuilding[] communities = FindObjectsOfType<PrebuiltBuilding>().Where(pb => pb.GetPrebuiltType() == PrebuiltBuildingType.Community).ToArray();
        
        // Find shelters and motels with space
        Building[] shelters = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter).ToArray();
        PrebuiltBuilding[] motels = FindObjectsOfType<PrebuiltBuilding>().Where(pb => pb.GetPrebuiltType() == PrebuiltBuildingType.Motel).ToArray();
        
        foreach (PrebuiltBuilding community in communities)
        {
            if (community.GetCurrentPopulation() <= 0) continue;
            
            // Find best destination (prefer shelters over motels)
            MonoBehaviour bestDestination = null; // Changed from Building to MonoBehaviour
            int bestAvailableSpace = 0;
            
            // Check shelters first
            foreach (Building shelter in shelters)
            {
                BuildingResourceStorage storage = shelter.GetComponent<BuildingResourceStorage>();
                if (storage == null) continue;
                
                int availableSpace = storage.GetAvailableSpace(ResourceType.Population);
                if (availableSpace > bestAvailableSpace)
                {
                    bestAvailableSpace = availableSpace;
                    bestDestination = shelter; // Building inherits from MonoBehaviour
                }
            }
            
            // Check motels if no shelter space found
            if (bestDestination == null)
            {
                foreach (PrebuiltBuilding motel in motels)
                {
                    int availableSpace = motel.GetPopulationCapacity() - motel.GetCurrentPopulation();
                    if (availableSpace > bestAvailableSpace)
                    {
                        bestAvailableSpace = availableSpace;
                        bestDestination = motel; // PrebuiltBuilding inherits from MonoBehaviour
                    }
                }
            }
            
            // Create transport task if we found space
            if (bestDestination != null && bestAvailableSpace > 0)
            {
                int transportAmount = Mathf.Min(community.GetCurrentPopulation(), bestAvailableSpace, 3); // Limit to 3 people per trip
                CreateDeliveryTask(community, bestDestination, ResourceType.Population, transportAmount, 3);
            }
        }
    }
    
    /// <summary>
    /// Add a new vehicle to the fleet
    /// </summary>
    public void AddVehicle(Vehicle vehicle)
    {
        if (!availableVehicles.Contains(vehicle))
        {
            availableVehicles.Add(vehicle);
            vehicle.OnDeliveryCompleted += OnVehicleDeliveryCompleted;
            
            if (showDebugInfo)
                Debug.Log($"Added vehicle {vehicle.GetVehicleName()} to delivery fleet");
        }
    }
    
    /// <summary>
    /// Remove a vehicle from the fleet
    /// </summary>
    public void RemoveVehicle(Vehicle vehicle)
    {
        if (availableVehicles.Contains(vehicle))
        {
            availableVehicles.Remove(vehicle);
            vehicle.OnDeliveryCompleted -= OnVehicleDeliveryCompleted;
            
            // Cancel current task if this vehicle was working
            if (!vehicle.IsAvailable())
            {
                vehicle.CancelCurrentTask();
            }
            
            if (showDebugInfo)
                Debug.Log($"Removed vehicle {vehicle.GetVehicleName()} from delivery fleet");
        }
    }
    
    /// <summary>
    /// Get delivery system statistics
    /// </summary>
    public DeliveryStatistics GetDeliveryStatistics()
    {
        DeliveryStatistics stats = new DeliveryStatistics();
        
        stats.totalVehicles = availableVehicles.Count;
        stats.availableVehicles = availableVehicles.Count(v => v.IsAvailable());
        stats.busyVehicles = stats.totalVehicles - stats.availableVehicles;
        
        stats.pendingTasks = pendingTasks.Count;
        stats.activeTasks = activeTasks.Count;
        stats.completedTasks = completedTasks.Count;
        
        return stats;
    }
    
    /// <summary>
    /// Get all active delivery tasks (for UI display)
    /// </summary>
    public List<DeliveryTask> GetActiveTasks()
    {
        return new List<DeliveryTask>(activeTasks);
    }
    
    /// <summary>
    /// Print delivery system statistics
    /// </summary>
    public void PrintDeliveryStatistics()
    {
        DeliveryStatistics stats = GetDeliveryStatistics();

        Debug.Log("=== DELIVERY SYSTEM STATISTICS ===");
        Debug.Log($"Vehicles: {stats.availableVehicles}/{stats.totalVehicles} available");
        Debug.Log($"Tasks: {stats.pendingTasks} pending, {stats.activeTasks} active, {stats.completedTasks} completed");

        if (showDebugInfo)
        {
            Debug.Log("Active Tasks:");
            foreach (DeliveryTask task in activeTasks)
            {
                Debug.Log($"  {task}");
            }
        }
    }
    
    [ContextMenu("Print Delivery Statistics")]
    public void DebugPrintStats()
    {
        PrintDeliveryStatistics();
    }
    
    [ContextMenu("Generate Test Task")]
    public void GenerateTestTask()
    {
        GenerateAutoTasks();
    }
}

[System.Serializable]
public class DeliveryStatistics
{
    public int totalVehicles;
    public int availableVehicles;
    public int busyVehicles;
    public int pendingTasks;
    public int activeTasks;
    public int completedTasks;
}