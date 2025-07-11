using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System;

public enum VehicleStatus
{
    Idle,
    Loading,
    InTransit,
    Unloading,
    Returning
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
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showPathGizmos = true;
    
    // Current state
    private VehicleStatus currentStatus = VehicleStatus.Idle;
    private List<Vector3> currentPath = new List<Vector3>();
    private int currentPathIndex = 0;
    private float pathProgress = 0f;
    
    // Cargo management
    private Dictionary<ResourceType, int> currentCargo = new Dictionary<ResourceType, int>();
    
    // Current delivery task
    private DeliveryTask currentTask;
    private Vector3 targetPosition;
    private MonoBehaviour sourceBuilding;
    private MonoBehaviour destinationBuilding;
    
    // Movement
    private Coroutine movementCoroutine;
    
    // Events
    public event Action<Vehicle> OnDeliveryCompleted;
    public event Action<Vehicle, VehicleStatus> OnStatusChanged;
    public event Action<Vehicle> OnCargoChanged;

    void Start()
    {
        InitializeVehicle();
        if (infoDisplay == null)
            infoDisplay = GetComponent<InfoDisplay>();
    
        UpdateInfoDisplay();
        
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
                displayColor = Color.white;
                break;
            case VehicleStatus.Loading:
                displayText += "Loading";
                displayColor = Color.yellow;
                break;
            case VehicleStatus.InTransit:
                displayText += "In Transit";
                displayColor = Color.green;
                break;
            case VehicleStatus.Unloading:
                displayText += "Unloading";
                displayColor = Color.cyan;
                break;
        }
        
        infoDisplay.UpdateDisplay(displayText, displayColor);
    }

    ResourceType GetPrimaryCargoType()
    {
        foreach (var kvp in currentCargo)
        {
            if (kvp.Value > 0)
                return kvp.Key;
        }
        return ResourceType.Population;
    }

    // Call UpdateInfoDisplay() whenever the status changes
    void SetStatus(VehicleStatus newStatus)
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
        // Step 1: Move to source building
        SetStatus(VehicleStatus.InTransit);
        Vector3 sourcePos = currentTask.GetSourceRoadConnection();
        yield return StartCoroutine(MoveToPosition(sourcePos));
        
        // Step 2: Load cargo
        SetStatus(VehicleStatus.Loading);
        yield return StartCoroutine(LoadCargo());
        
        // Step 3: Move to destination building
        SetStatus(VehicleStatus.InTransit);
        Vector3 destPos = currentTask.GetDestinationRoadConnection();
        yield return StartCoroutine(MoveToPosition(destPos));
        
        // Step 4: Unload cargo
        SetStatus(VehicleStatus.Unloading);
        yield return StartCoroutine(UnloadCargo());
        
        // Step 5: Complete delivery
        CompleteDelivery();
    }
    
    /// <summary>
    /// Move vehicle to target position using pathfinding
    /// </summary>
    IEnumerator MoveToPosition(Vector3 targetPos)
    {
        // Find path using pathfinding system
        PathfindingSystem pathfinder = FindObjectOfType<PathfindingSystem>();
        if (pathfinder != null)
        {
            currentPath = pathfinder.FindPath(transform.position, targetPos);
        }
        else
        {
            // Fallback: direct movement
            currentPath = new List<Vector3> { transform.position, targetPos };
        }
        
        if (currentPath.Count == 0)
        {
            if (showDebugInfo)
                Debug.LogError($"Vehicle {vehicleName} could not find path to {targetPos}");
            yield break;
        }
        
        // Move along the path
        currentPathIndex = 0;
        while (currentPathIndex < currentPath.Count - 1)
        {
            Vector3 startPos = currentPath[currentPathIndex];
            Vector3 endPos = currentPath[currentPathIndex + 1];
            
            // compute direction and rotation
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

                // Update direction during movement (smoother)
                if (enableDirectionRotation && elapsedTime < journeyTime * 0.1f) // Only rotate at the start of the segment
                {
                    UpdateVehicleDirection(startPos, endPos);
                }
                
                pathProgress = (currentPathIndex + fractionOfJourney) / (currentPath.Count - 1);
                yield return null;
            }
            
            currentPathIndex++;
        }
        
        // Ensure we're exactly at the target
        transform.position = currentPath[currentPath.Count - 1];
        pathProgress = 1f;
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
        currentTask = null;
        sourceBuilding = null;
        destinationBuilding = null;
        currentPath.Clear();
        
        SetStatus(VehicleStatus.Idle);
        OnDeliveryCompleted?.Invoke(this);
        
        if (showDebugInfo)
            Debug.Log($"Vehicle {vehicleName} completed delivery and returned to idle");
    }
    
    /// <summary>
    /// Update visual appearance based on status and cargo
    /// </summary>
    void UpdateVisualState()
    {
        if (vehicleRenderer == null) return;
        
        // Color based on status
        switch (currentStatus)
        {
            case VehicleStatus.Idle:
                vehicleRenderer.color = Color.white;
                break;
            case VehicleStatus.Loading:
                vehicleRenderer.color = Color.yellow;
                break;
            case VehicleStatus.InTransit:
                vehicleRenderer.color = Color.green;
                break;
            case VehicleStatus.Unloading:
                vehicleRenderer.color = Color.blue;
                break;
            case VehicleStatus.Returning:
                vehicleRenderer.color = Color.cyan;
                break;
        }
        
        // Show/hide cargo indicator
        if (cargoIndicator != null)
        {
            bool hasCargo = GetTotalCargo() > 0;
            cargoIndicator.SetActive(hasCargo);
        }
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
}