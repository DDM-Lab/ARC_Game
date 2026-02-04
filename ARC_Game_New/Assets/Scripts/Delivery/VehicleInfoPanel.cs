using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class VehicleInfoPanel : MonoBehaviour
{
    [Header("Panel References")]
    public GameObject infoPanel;
    public Button closeButton;
    
    [Header("Info Display")]
    public TextMeshProUGUI vehicleIdText;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI sourceText;
    public TextMeshProUGUI destinationText;
    public TextMeshProUGUI cargoText;
    public TextMeshProUGUI etaText;
    public TextMeshProUGUI vehicleCountText; // Shows "Vehicle 2/3" when multiple at same position
    
    [Header("Route Visualization")]
    public PathHighlighter pathHighlighter;
    
    [Header("Click Detection")]
    public float clickRadius = 1f; // Radius to detect overlapping vehicles
    
    private Vehicle currentVehicle;
    private Vector3 lastClickPosition;
    private List<Vehicle> vehiclesAtClickPosition = new List<Vehicle>();
    private int currentVehicleIndex = 0;
    
    public static VehicleInfoPanel Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePanel);
        }
        
        if (infoPanel != null)
        {
            infoPanel.SetActive(false);
        }
        
        if (pathHighlighter == null)
        {
            pathHighlighter = FindObjectOfType<PathHighlighter>();
        }
    }
    
    /// <summary>
    /// Handle vehicle click - called from Vehicle.cs
    /// </summary>
    public void OnVehicleClicked(Vector3 clickPosition)
    {
        // Only allow clicks when game is paused
        if (Time.timeScale != 0f)
        {
            return;
        }
        
        // Check if clicking same position
        bool samePosition = Vector3.Distance(clickPosition, lastClickPosition) < 0.1f;
        
        if (!samePosition)
        {
            // New position - find all vehicles at this position
            lastClickPosition = clickPosition;
            vehiclesAtClickPosition = GetVehiclesAtPosition(clickPosition);
            currentVehicleIndex = 0;
            
            if (vehiclesAtClickPosition.Count == 0)
            {
                ClosePanel();
                return;
            }
        }
        else
        {
            // Same position - cycle to next vehicle
            if (vehiclesAtClickPosition.Count == 0)
            {
                ClosePanel();
                return;
            }
            
            currentVehicleIndex = (currentVehicleIndex + 1) % vehiclesAtClickPosition.Count;
        }
        
        // Show info for selected vehicle
        ShowVehicleInfo(vehiclesAtClickPosition[currentVehicleIndex]);
    }
    
    List<Vehicle> GetVehiclesAtPosition(Vector3 position)
    {
        List<Vehicle> vehicles = new List<Vehicle>();
        Vehicle[] allVehicles = FindObjectsOfType<Vehicle>();
        
        foreach (Vehicle vehicle in allVehicles)
        {
            if (Vector3.Distance(vehicle.transform.position, position) <= clickRadius)
            {
                vehicles.Add(vehicle);
            }
        }
        
        return vehicles;
    }
    
    /// <summary>
    /// Show vehicle information panel
    /// </summary>
    public void ShowVehicleInfo(Vehicle vehicle)
    {
        if (vehicle == null)
        {
            ClosePanel();
            return;
        }
        
        currentVehicle = vehicle;
        
        // Show panel
        if (infoPanel != null)
        {
            infoPanel.SetActive(true);
        }
        
        // Update all text fields
        UpdatePanelContent();
        
        // Show route visualization if vehicle has a path
        ShowRouteVisualization();
    }
    
    void UpdatePanelContent()
    {
        if (currentVehicle == null)
            return;
        
        // Vehicle ID
        if (vehicleIdText != null)
        {
            vehicleIdText.text = $"Vehicle #{currentVehicle.vehicleId}";
        }
        
        // Status
        if (statusText != null)
        {
            statusText.text = $"Status: {GetStatusText(currentVehicle.currentStatus)}";
        }
        
        // Source
        if (sourceText != null)
        {
            string sourceName = GetBuildingName(currentVehicle.sourceBuilding);
            sourceText.text = $"Source: {sourceName}";
        }
        
        // Destination
        if (destinationText != null)
        {
            string destName = GetBuildingName(currentVehicle.destinationBuilding);
            destinationText.text = $"Destination: {destName}";
        }
        
        // Cargo
        if (cargoText != null)
        {
            cargoText.text = GetCargoText();
        }
        
        // ETA
        if (etaText != null)
        {
            etaText.text = GetETAText();
        }
        
        // Vehicle count (if multiple at same position)
        if (vehicleCountText != null)
        {
            if (vehiclesAtClickPosition.Count > 1)
            {
                vehicleCountText.text = $"Vehicle {currentVehicleIndex + 1}/{vehiclesAtClickPosition.Count}";
                vehicleCountText.gameObject.SetActive(true);
            }
            else
            {
                vehicleCountText.gameObject.SetActive(false);
            }
        }
    }
    
    string GetStatusText(VehicleStatus status)
    {
        switch (status)
        {
            case VehicleStatus.Idle:
                return "Idle";
            case VehicleStatus.Loading:
                return "Loading";
            case VehicleStatus.InTransit:
                return "In Transit";
            case VehicleStatus.Unloading:
                return "Unloading";
            case VehicleStatus.Damaged:
                return "Damaged";
            default:
                return "Unknown";
        }
    }
    
    string GetBuildingName(MonoBehaviour building)
    {
        if (building == null)
            return "Unknown";
        
        // Try to get Building component
        Building buildingComponent = building.GetComponent<Building>();
        if (buildingComponent != null)
        {
            return $"{buildingComponent.GetBuildingType()} (Site {buildingComponent.GetOriginalSiteId()})";
        }
        
        return building.name;
    }
    
    string GetCargoText()
    {
        int totalCargo = currentVehicle.GetTotalCargo();
        
        if (totalCargo <= 0)
        {
            return "Cargo: Empty";
        }
        
        ResourceType cargoType = currentVehicle.GetPrimaryCargoType();
        string cargoTypeName = cargoType == ResourceType.Population ? "Clients" : "Food Packs";
        return $"Cargo: {cargoTypeName} ({totalCargo}/{currentVehicle.maxCargoCapacity})";
    }
    
    string GetETAText()
    {
        if (currentVehicle.currentStatus == VehicleStatus.Idle || 
            currentVehicle.currentStatus == VehicleStatus.Damaged)
        {
            return "ETA: N/A";
        }
        
        if (currentVehicle.currentPath == null || currentVehicle.currentPath.Count == 0)
        {
            return "ETA: Unknown";
        }
        
        // Calculate remaining distance
        float remainingDistance = CalculateRemainingDistance();
        
        // Calculate ETA based on speed
        float eta = remainingDistance / currentVehicle.moveSpeed;
        
        // Format ETA
        if (eta < 60f)
        {
            return $"ETA: {eta:F1} seconds";
        }
        else
        {
            float minutes = eta / 60f;
            return $"ETA: {minutes:F1} minutes";
        }
    }
    
    float CalculateRemainingDistance()
    {
        if (currentVehicle.currentPath == null || currentVehicle.currentPath.Count == 0)
            return 0f;
        
        float distance = 0f;
        
        // Find closest waypoint to vehicle
        int closestIndex = FindClosestPathIndex();
        
        // Distance from vehicle to closest waypoint
        distance += Vector3.Distance(currentVehicle.transform.position, currentVehicle.currentPath[closestIndex]);
        
        // Distance along remaining path
        for (int i = closestIndex; i < currentVehicle.currentPath.Count - 1; i++)
        {
            distance += Vector3.Distance(currentVehicle.currentPath[i], currentVehicle.currentPath[i + 1]);
        }
        
        return distance;
    }
    
    int FindClosestPathIndex()
    {
        if (currentVehicle.currentPath == null || currentVehicle.currentPath.Count == 0)
            return 0;
        
        int closestIndex = 0;
        float closestDistance = float.MaxValue;
        
        for (int i = 0; i < currentVehicle.currentPath.Count; i++)
        {
            float dist = Vector3.Distance(currentVehicle.transform.position, currentVehicle.currentPath[i]);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                closestIndex = i;
            }
        }
        
        return closestIndex;
    }
    
    void ShowRouteVisualization()
    {
        if (pathHighlighter == null || currentVehicle == null)
            return;
        
        pathHighlighter.ClearHighlights();
        
        // Show current path if available
        if (currentVehicle.currentPath != null && currentVehicle.currentPath.Count > 0)
        {
            // Create path from current position to destination
            List<Vector3> visualPath = new List<Vector3>();
            visualPath.Add(currentVehicle.transform.position); // Start from current position
            visualPath.AddRange(currentVehicle.currentPath); // Add remaining path
            
            pathHighlighter.HighlightPath(visualPath);
        }
    }
    
    public void ClosePanel()
    {
        if (infoPanel != null)
        {
            infoPanel.SetActive(false);
        }
        
        // Clear route visualization
        if (pathHighlighter != null)
        {
            pathHighlighter.ClearHighlights();
        }
        
        currentVehicle = null;
        vehiclesAtClickPosition.Clear();
        currentVehicleIndex = 0;
    }
    
    void Update()
    {
        // Auto-close if current vehicle is destroyed
        if (currentVehicle == null && infoPanel != null && infoPanel.activeSelf)
        {
            ClosePanel();
        }
        
        // Update content if panel is open
        if (infoPanel != null && infoPanel.activeSelf && currentVehicle != null)
        {
            UpdatePanelContent();
        }
    }
    
    public bool IsPanelOpen()
    {
        return infoPanel != null && infoPanel.activeSelf;
    }
}