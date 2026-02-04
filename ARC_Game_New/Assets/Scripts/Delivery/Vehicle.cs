using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System;
using System.Linq;

public enum VehicleStatus
{
    Idle,
    Loading,
    InTransit,
    Unloading,
    Returning,
    Damaged  
}

public class Vehicle : MonoBehaviour
{
    [Header("Vehicle Configuration")]
    public int vehicleId;
    public string vehicleName = "Vehicle";
    public float moveSpeed = 5f;

    [Header("Cargo Configuration")]
    public int maxCargoCapacity = 10;
    public List<ResourceType> allowedCargoTypes = new List<ResourceType>();

    [Header("Visual Components")]
    public SpriteRenderer vehicleRenderer;
    public GameObject cargoIndicator; // Visual indicator of cargo

    [Header("Vehicle Direction")]
    public bool enableDirectionRotation = true;
    public float rotationSpeed = 10f; // turn speed
    public float defaultAngle = 180f; // default angle when idle

    [Header("Info Display")]
    public InfoDisplay infoDisplay;

    [Header("Flood Interaction")]
    public bool isDamaged = false;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showPathGizmos = true;

    [Header("Colors")]
    public Color idleColor = Color.white;
    public Color loadingColor = new Color(0.8f, 0.8f, 0.8f); // light gray
    public Color inTransitColor = Color.white;
    public Color unloadingColor = new Color(0.8f, 0.8f, 0.8f);
    public Color damagedColor = new Color(1f, 0.2f, 0.2f); // light red
    // Current state
    public VehicleStatus currentStatus = VehicleStatus.Idle;
    public List<Vector3> currentPath = new List<Vector3>();
    private int currentPathIndex = 0;
    private float pathProgress = 0f;

    // Cargo management
    public Dictionary<ResourceType, int> currentCargo = new Dictionary<ResourceType, int>();

    // Current delivery task
    public DeliveryTask currentTask;
    public Vector3 targetPosition;
    public MonoBehaviour sourceBuilding;
    public MonoBehaviour destinationBuilding;

    // Movement
    private Coroutine movementCoroutine;

    // Events
    public event Action<Vehicle, DeliveryTask> OnDeliveryCompleted;
    public event Action<Vehicle, VehicleStatus> OnStatusChanged;
    public event Action<Vehicle> OnCargoChanged;

    void Start()
    {
        InitializeVehicle();
        if (infoDisplay == null)
            infoDisplay = GetComponent<InfoDisplay>();

        UpdateInfoDisplay();

        // Register with UI overlay
        if (VehicleUIOverlay.Instance != null)
        {
            VehicleUIOverlay.Instance.RegisterVehicle(this);
        }
        
        // Ensure collider exists for click detection
        if (GetComponent<Collider2D>() == null)
        {
            CircleCollider2D collider = gameObject.AddComponent<CircleCollider2D>();
            collider.radius = 0.5f;
        }

    }

    public void UpdateInfoDisplay()
    {
        if (infoDisplay == null) return;

        string displayText = "";
        Color displayColor = Color.white;

        // Show cargo information
        int totalCargo = GetTotalCargo();
        if (totalCargo > 0)
        {
            ResourceType cargoType = GetPrimaryCargoType();
            string cargoIcon = cargoType == ResourceType.Population ? "👥" : "📦";
            displayText += $"{cargoIcon} {totalCargo}/{maxCargoCapacity}\n";
        }
        else
        {
            displayText += $"🚚 {totalCargo}/{maxCargoCapacity}\n";
        }

        // Show vehicle status
        switch (currentStatus)
        {
            case VehicleStatus.Idle:
                displayText += "Idle";
                displayColor = idleColor;
                break;
            case VehicleStatus.Loading:
                displayText += "Loading";
                displayColor = loadingColor;
                break;
            case VehicleStatus.InTransit:
                displayText += "In Transit";
                displayColor = inTransitColor;
                break;
            case VehicleStatus.Unloading:
                displayText += "Unloading";
                displayColor = unloadingColor;
                break;
        }

        infoDisplay.UpdateDisplay(displayText, displayColor);
    }

    public ResourceType GetPrimaryCargoType()
    {
        foreach (var kvp in currentCargo)
        {
            if (kvp.Value > 0)
                return kvp.Key;
        }
        return ResourceType.Population;
    }

    // Call UpdateInfoDisplay() whenever the status changes
    public void SetStatus(VehicleStatus newStatus)
    {
        if (currentStatus != newStatus)
        {
            currentStatus = newStatus;
            UpdateVisualState();
            UpdateInfoDisplay();
            OnStatusChanged?.Invoke(this, currentStatus);
        }
    }

    void InitializeVehicle()
    {
        // Initialize cargo dictionary
        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
        {
            currentCargo[type] = 0;
        }

        // Set default name if not set
        if (string.IsNullOrEmpty(vehicleName))
        {
            vehicleName = $"Vehicle {vehicleId}";
        }

        // Initialize visual state
        UpdateVisualState();

        Debug.Log($"Vehicle {vehicleName} initialized with capacity {maxCargoCapacity}");
    }

    /// <summary>
    /// Assign a delivery task to this vehicle
    /// </summary>
    public bool AssignDeliveryTask(DeliveryTask task)
    {
        if (currentStatus != VehicleStatus.Idle)
        {
            if (showDebugInfo)
                Debug.LogWarning($"Vehicle {vehicleName} is not idle - cannot assign new task");
            return false;
        }

        // Check if vehicle can handle this cargo type
        if (!allowedCargoTypes.Contains(task.cargoType))
        {
            if (showDebugInfo)
                Debug.LogWarning($"Vehicle {vehicleName} cannot carry {task.cargoType}");
            return false;
        }

        // Check if vehicle has enough capacity
        if (task.quantity > maxCargoCapacity)
        {
            if (showDebugInfo)
                Debug.LogWarning($"Vehicle {vehicleName} capacity ({maxCargoCapacity}) insufficient for task quantity ({task.quantity})");
            return false;
        }

        currentTask = task;
        sourceBuilding = task.sourceBuilding;
        destinationBuilding = task.destinationBuilding;

        // Start the delivery process
        StartCoroutine(ExecuteDeliveryTask());

        if (showDebugInfo)
            Debug.Log($"Vehicle {vehicleName} assigned delivery task: {task.quantity} {task.cargoType} from {sourceBuilding.name} to {destinationBuilding.name}");

        return true;
    }

    /// <summary>
    /// Execute the complete delivery task
    /// </summary>
    IEnumerator ExecuteDeliveryTask()
    {
        Debug.Log($"Vehicle {vehicleName} starting delivery task");

        // Step 1: Move to source building
        SetStatus(VehicleStatus.InTransit);
        Vector3 sourcePos = currentTask.GetSourceRoadConnection();
        Debug.Log($"Vehicle {vehicleName} moving to source: {sourcePos}");
        yield return StartCoroutine(MoveToPosition(sourcePos));

        // Step 2: Load cargo
        SetStatus(VehicleStatus.Loading);
        Debug.Log($"Vehicle {vehicleName} loading cargo");
        yield return StartCoroutine(LoadCargo());

        // Step 3: Move to destination building
        SetStatus(VehicleStatus.InTransit);
        Vector3 destPos = currentTask.GetDestinationRoadConnection();
        Debug.Log($"Vehicle {vehicleName} moving to destination: {destPos}");
        yield return StartCoroutine(MoveToPosition(destPos));

        // Step 4: Unload cargo
        SetStatus(VehicleStatus.Unloading);
        Debug.Log($"Vehicle {vehicleName} unloading cargo");
        yield return StartCoroutine(UnloadCargo());

        // Step 5: Complete delivery
        Debug.Log($"Vehicle {vehicleName} completing delivery");
        CompleteDelivery();
    }

    /// <summary>
    /// Move vehicle to target position using pathfinding
    /// </summary>
    IEnumerator MoveToPosition(Vector3 targetPos)
    {
        // Existing pathfinding code...
        PathfindingSystem pathfinder = FindObjectOfType<PathfindingSystem>();
        if (pathfinder != null)
        {
            // Use flood-aware pathfinding
            currentPath = pathfinder.FindFloodAwarePath(transform.position, targetPos);
        }
        else
        {
            currentPath = new List<Vector3> { transform.position, targetPos };
        }

        if (currentPath.Count == 0)
        {
            if (showDebugInfo)
                Debug.LogError($"Vehicle {vehicleName} could not find flood-free path to {targetPos}");
            yield break;
        }

        // Move along path with flood checking
        currentPathIndex = 0;
        while (currentPathIndex < currentPath.Count - 1)
        {
            Vector3 startPos = currentPath[currentPathIndex];
            Vector3 endPos = currentPath[currentPathIndex + 1];

            if (enableDirectionRotation)
            {
                UpdateVehicleDirection(startPos, endPos);
            }

            float journeyLength = Vector3.Distance(startPos, endPos);
            float journeyTime = journeyLength / moveSpeed;
            float elapsedTime = 0f;

            while (elapsedTime < journeyTime)
            {
                elapsedTime += Time.deltaTime;
                float fractionOfJourney = elapsedTime / journeyTime;
                transform.position = Vector3.Lerp(startPos, endPos, fractionOfJourney);

                // Check for flood collision during movement
                if (CheckForFloodCollision())
                {
                    StopVehicleDueToFlood();
                    yield break; // Stop movement immediately
                }

                pathProgress = (currentPathIndex + fractionOfJourney) / (currentPath.Count - 1);
                yield return null;
            }

            currentPathIndex++;
        }

        transform.position = currentPath[currentPath.Count - 1];
        pathProgress = 1f;
    }

    // Add flood collision detection
    bool CheckForFloodCollision()
    {
        if (FloodSystem.Instance == null) return false;

        return FloodSystem.Instance.IsFloodedAt(transform.position);
    }

    // Handle vehicle stopping due to flood
    public void StopVehicleDueToFlood()
    {
        isDamaged = true;
        SetStatus(VehicleStatus.Damaged);

        // Stop all movement
        StopAllCoroutines();

        // Trigger road blockage task
        TriggerRoadBlockageTask();

        // Trigger vehicle repair task
        TriggerVehicleRepairTask();

        if (showDebugInfo)
            Debug.Log($"Vehicle {vehicleName} stopped due to flood at position {transform.position}");
    }

    // Trigger emergency tasks
    void TriggerRoadBlockageTask()
    {
        if (currentTask != null)
        {
            GameTask relatedTask = FindRelatedGameTask();
            
            // Only handle delivery failure if we found a related game task
            if (relatedTask != null)
            {
                TaskSystem.Instance?.HandleDeliveryFailure(relatedTask);
            }
            
            // Always create road blockage task if we have a delivery task
            FloodTaskGenerator.Instance?.CreateRoadBlockageTask(this, currentTask);
        }
        else
        {
            if (showDebugInfo)
                Debug.Log($"Vehicle {vehicleName} blocked by flood but has no delivery task - skipping road blockage task");
        }
    }

    void TriggerVehicleRepairTask()
    {
        FloodTaskGenerator.Instance?.CreateVehicleRepairTask(this);
    }

    // Find the game task related to current delivery
    GameTask FindRelatedGameTask()
    {
        if (currentTask == null) return null;

        var activeTasks = TaskSystem.Instance.GetAllActiveTasks();
        return activeTasks.FirstOrDefault(t =>
            t.linkedDeliveryTaskIds != null &&
            t.linkedDeliveryTaskIds.Contains(currentTask.taskId));
    }

    // Add repair method
    public void RepairVehicle()
    {
        isDamaged = false;
        SetStatus(VehicleStatus.Idle);

        if (showDebugInfo)
            Debug.Log($"Vehicle {vehicleName} has been repaired");
    }

    // Update visual state for damaged status
    void UpdateVisualState()
    {
        if (vehicleRenderer == null) return;

        switch (currentStatus)
        {
            case VehicleStatus.Idle:
                vehicleRenderer.color = idleColor;
                break;
            case VehicleStatus.Loading:
                vehicleRenderer.color = loadingColor;
                break;
            case VehicleStatus.InTransit:
                vehicleRenderer.color = inTransitColor;
                break;
            case VehicleStatus.Unloading:
                vehicleRenderer.color = unloadingColor;
                break;
            case VehicleStatus.Returning:
                vehicleRenderer.color = inTransitColor;
                break;
            case VehicleStatus.Damaged:
                vehicleRenderer.color = damagedColor;
                break;
        }

        if (cargoIndicator != null)
        {
            bool hasCargo = GetTotalCargo() > 0;
            cargoIndicator.SetActive(hasCargo);
        }
    }

    /// <summary>
    /// Update vehicle rotation based on movement direction
    /// </summary>
    void UpdateVehicleDirection(Vector3 fromPos, Vector3 toPos)
    {
        Vector3 direction = (toPos - fromPos).normalized;

        if (direction != Vector3.zero)
        {
            // calculate target angle
            float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            // Adjust angle to match left-facing sprite (add 180 degree offset)
            targetAngle += defaultAngle;

            // Smoothly rotate to target angle
            Quaternion targetRotation = Quaternion.AngleAxis(targetAngle, Vector3.forward);
            //transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            transform.rotation = targetRotation; // turn instantly for simplicity
            if (showDebugInfo)
            {
                Debug.Log($"Vehicle {vehicleName} turning to angle: {targetAngle:F1}°");
            }
        }
    }

    /// <summary>
    /// Load cargo from source building
    /// </summary>
    IEnumerator LoadCargo()
    {
        if (currentTask == null || sourceBuilding == null)
            yield break;

        // Simulate loading time
        yield return new WaitForSeconds(1f);

        // Try to get resources from source building
        BuildingResourceStorage sourceStorage = GetBuildingResourceStorage(sourceBuilding);

        if (sourceStorage != null)
        {
            int actualLoaded = sourceStorage.RemoveResource(currentTask.cargoType, currentTask.quantity);
            currentCargo[currentTask.cargoType] = actualLoaded;

            if (showDebugInfo)
                Debug.Log($"Vehicle {vehicleName} loaded {actualLoaded} {currentTask.cargoType}");
        }
        else
        {
            if (showDebugInfo)
                Debug.LogError($"Vehicle {vehicleName} could not find resource storage at source building");
        }

        UpdateVisualState();
        OnCargoChanged?.Invoke(this);
    }

    /// <summary>
    /// Unload cargo at destination building
    /// </summary>
    IEnumerator UnloadCargo()
    {
        if (currentTask == null || destinationBuilding == null)
            yield break;

        // Simulate unloading time
        yield return new WaitForSeconds(1f);

        // Try to deliver resources to destination building
        BuildingResourceStorage destStorage = GetBuildingResourceStorage(destinationBuilding);

        if (destStorage != null)
        {
            int cargoAmount = currentCargo[currentTask.cargoType];
            int actualDelivered = destStorage.AddResource(currentTask.cargoType, cargoAmount);
            currentCargo[currentTask.cargoType] = 0;

            // NEW: Track client arrivals at shelters
            if (currentTask.cargoType == ResourceType.Population && ClientStayTracker.Instance != null && actualDelivered > 0)
            {
                Building sourceBuilding = currentTask.sourceBuilding.GetComponent<Building>();
                Building destBuilding = currentTask.destinationBuilding.GetComponent<Building>();
                PrebuiltBuilding sourcePrebuilt = currentTask.sourceBuilding.GetComponent<PrebuiltBuilding>();
                PrebuiltBuilding destPrebuilt = currentTask.destinationBuilding.GetComponent<PrebuiltBuilding>();
                
                // Case 1: Community to Shelter - Register new clients
                if (sourcePrebuilt != null && sourcePrebuilt.GetPrebuiltType() == PrebuiltBuildingType.Community &&
                    destBuilding != null && destBuilding.GetBuildingType() == BuildingType.Shelter)
                {
                    string groupName = $"Vehicle_{currentTask.taskId}_{sourcePrebuilt.name}_to_{destBuilding.name}";
                    ClientStayTracker.Instance.RegisterClientArrival(destBuilding, actualDelivered, groupName);
                }
                // Case 2: Shelter to Shelter - Move existing clients (implementation needed)
                else if (sourceBuilding != null && sourceBuilding.GetBuildingType() == BuildingType.Shelter &&
                        destBuilding != null && destBuilding.GetBuildingType() == BuildingType.Shelter)
                {
                    // For now, register as new arrivals (continue their stay duration)
                    string groupName = $"Vehicle_{currentTask.taskId}_{sourceBuilding.name}_to_{destBuilding.name}";
                    ClientStayTracker.Instance.RegisterClientArrival(destBuilding, actualDelivered, groupName);
                }
                // Case 3: Shelter to Casework - Remove clients
                else if (sourceBuilding != null && sourceBuilding.GetBuildingType() == BuildingType.Shelter &&
                        destBuilding != null && destBuilding.GetBuildingType() == BuildingType.CaseworkSite)
                {
                    int removed = ClientStayTracker.Instance.RemoveClientsByQuantity(sourceBuilding, actualDelivered);
                    if (showDebugInfo)
                        Debug.Log($"Removed {removed} clients from {sourceBuilding.name} for casework");
                }
            }

            if (showDebugInfo)
                Debug.Log($"Vehicle {vehicleName} delivered {actualDelivered} {currentTask.cargoType}");
        }
        else
        {
            if (showDebugInfo)
                Debug.LogError($"Vehicle {vehicleName} could not find resource storage at destination building");
        }

        UpdateVisualState();
        OnCargoChanged?.Invoke(this);
    }

    /// <summary>
    /// Get BuildingResourceStorage from either Building or PrebuiltBuilding
    /// </summary>
    BuildingResourceStorage GetBuildingResourceStorage(MonoBehaviour building)
    {
        // Try Building component first
        Building buildingComponent = building.GetComponent<Building>();
        if (buildingComponent != null)
        {
            return buildingComponent.GetComponent<BuildingResourceStorage>();
        }

        // Try PrebuiltBuilding component
        PrebuiltBuilding prebuiltBuilding = building.GetComponent<PrebuiltBuilding>();
        if (prebuiltBuilding != null)
        {
            return prebuiltBuilding.GetResourceStorage();
        }

        // Try direct BuildingResourceStorage component
        return building.GetComponent<BuildingResourceStorage>();
    }

    /// <summary>
    /// Complete the delivery and return to idle
    /// </summary>
    void CompleteDelivery()
    {
        DeliveryTask taskToComplete = currentTask;

        currentTask = null;
        sourceBuilding = null;
        destinationBuilding = null;
        currentPath.Clear();

        SetStatus(VehicleStatus.Idle);
        OnDeliveryCompleted?.Invoke(this, taskToComplete); // pass completed task to event directly

        if (showDebugInfo)
            Debug.Log($"Vehicle {vehicleName} completed delivery and returned to idle");
    }

    /// <summary>
    /// Get total cargo amount
    /// </summary>
    public int GetTotalCargo()
    {
        int total = 0;
        foreach (var kvp in currentCargo)
        {
            total += kvp.Value;
        }
        return total;
    }

    /// <summary>
    /// Get cargo amount of specific type
    /// </summary>
    public int GetCargoAmount(ResourceType type)
    {
        return currentCargo.ContainsKey(type) ? currentCargo[type] : 0;
    }

    /// <summary>
    /// Check if vehicle is available for new tasks
    /// </summary>
    public bool IsAvailable()
    {
        return currentStatus == VehicleStatus.Idle;
    }

    /// <summary>
    /// Get current progress along path (0-1)
    /// </summary>
    public float GetPathProgress()
    {
        return pathProgress;
    }

    /// <summary>
    /// Get current path for visualization
    /// </summary>
    public List<Vector3> GetCurrentPath()
    {
        return new List<Vector3>(currentPath);
    }

    /// <summary>
    /// Force stop current task and return to idle
    /// </summary>
    public void CancelCurrentTask()
    {
        if (movementCoroutine != null)
        {
            StopCoroutine(movementCoroutine);
        }

        StopAllCoroutines();
        CompleteDelivery();

        if (showDebugInfo)
            Debug.Log($"Vehicle {vehicleName} task cancelled");
    }

    // Gizmos for debugging
    void OnDrawGizmos()
    {
        if (!showPathGizmos || currentPath.Count == 0)
            return;

        // Draw current path
        Gizmos.color = Color.cyan;
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Gizmos.DrawLine(currentPath[i], currentPath[i + 1]);
        }

        // Draw current position on path
        if (currentPathIndex < currentPath.Count - 1)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(transform.position, 0.3f);
        }
    }
    
    void OnMouseEnter()
    {
        if (Time.timeScale != 0f) return; // Only when paused
        transform.localScale = Vector3.one * 1.1f;
    }

    void OnMouseExit()
    {
        transform.localScale = Vector3.one;
    }

    void OnMouseDown()
    {
        // Only allow clicks when game is paused
        if (Time.timeScale != 0f)
            return;
        
        if (VehicleInfoPanel.Instance != null)
        {
            VehicleInfoPanel.Instance.OnVehicleClicked(transform.position);
        }
    }
    
    void OnDestroy()
    {
        if (VehicleUIOverlay.Instance != null)
        {
            VehicleUIOverlay.Instance.UnregisterVehicle(this);
        }
    }

    // Getters
    public int GetVehicleId() => vehicleId;
    public string GetVehicleName() => vehicleName;
    public VehicleStatus GetCurrentStatus() => currentStatus;
    public int GetMaxCapacity() => maxCargoCapacity;
    public List<ResourceType> GetAllowedCargoTypes() => new List<ResourceType>(allowedCargoTypes);
    public DeliveryTask GetCurrentTask() => currentTask;

    [ContextMenu("Print Vehicle Status")]
    public void DebugPrintStatus()
    {
        Debug.Log($"=== {vehicleName} STATUS ===");
        Debug.Log($"Status: {currentStatus}");
        Debug.Log($"Position: {transform.position}");
        Debug.Log($"Cargo: {GetTotalCargo()}/{maxCargoCapacity}");

        if (currentTask != null)
        {
            Debug.Log($"Current Task: {currentTask.quantity} {currentTask.cargoType} from {currentTask.sourceBuilding.name} to {currentTask.destinationBuilding.name}");
        }
    }
    
    [ContextMenu("Test: Force Flood Stop")]
    public void TestForceFloodStop()
    {
        if (currentStatus == VehicleStatus.InTransit)
        {
            StopVehicleDueToFlood();
            Debug.Log($"Forced flood stop for vehicle {vehicleName}");
        }
        else
        {
            Debug.LogWarning($"Vehicle {vehicleName} is not in transit - cannot test flood stop");
        }
    }

    [ContextMenu("Test: Simulate Flood Encounter")]
    public void TestSimulateFloodEncounter()
    {
        // Create a fake delivery task for testing
        if (currentTask == null)
        {
            // Find buildings for fake task
            Building[] buildings = FindObjectsOfType<Building>();
            PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>();
            
            if (buildings.Length >= 2)
            {
                DeliveryTask fakeTask = new DeliveryTask(
                    buildings[0], buildings[1], 
                    ResourceType.FoodPacks, 5, 999);
                currentTask = fakeTask;
            }
            else if (buildings.Length >= 1 && prebuilts.Length >= 1)
            {
                DeliveryTask fakeTask = new DeliveryTask(
                    buildings[0], prebuilts[0], 
                    ResourceType.FoodPacks, 5, 999);
                currentTask = fakeTask;
            }
        }
        
        // Force flood encounter
        StopVehicleDueToFlood();
        Debug.Log($"Simulated flood encounter for vehicle {vehicleName}");
    }

    [ContextMenu("Test: Repair Vehicle")]
    public void TestRepairVehicle()
    {
        RepairVehicle();
        Debug.Log($"Repaired vehicle {vehicleName}");
    }
}