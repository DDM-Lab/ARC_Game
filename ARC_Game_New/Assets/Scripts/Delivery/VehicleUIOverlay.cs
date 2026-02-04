using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class VehicleUIOverlay : MonoBehaviour
{
    [Header("UI Settings")]
    public Vector2 uiOffset = new Vector2(0, 50f); // Offset above vehicle
    
    [Header("UI Prefab")]
    public GameObject vehicleOverlayPrefab; // Prefab with cargo and status text
    
    [Header("UI References")]
    public Transform uiContainer; // Container in Canvas
    public Camera mainCamera;
    
    [Header("Debug")]
    public bool showDebugInfo = false;
    
    // Dictionary to track vehicle-to-UI mapping
    private Dictionary<Vehicle, GameObject> vehicleUIMap = new Dictionary<Vehicle, GameObject>();
    
    // Singleton
    public static VehicleUIOverlay Instance { get; private set; }
    
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
        if (mainCamera == null)
            mainCamera = Camera.main;
        
        if (uiContainer == null)
        {
            Debug.LogError("VehicleUIOverlay: UI Container not assigned!");
        }
    }
    
    /// <summary>
    /// Called when a new vehicle is created
    /// </summary>
    public void RegisterVehicle(Vehicle vehicle)
    {
        if (vehicle == null || uiContainer == null) return;
        
        // Don't create duplicate overlays
        if (vehicleUIMap.ContainsKey(vehicle))
        {
            if (showDebugInfo)
                Debug.LogWarning($"Vehicle {vehicle.vehicleId} already has UI overlay");
            return;
        }
        
        GameObject uiOverlay = CreateUIOverlay(vehicle);
        
        if (uiOverlay != null)
        {
            vehicleUIMap[vehicle] = uiOverlay;
            UpdateUIPosition(vehicle, uiOverlay);
            UpdateOverlayContent(vehicle, uiOverlay);
            
            // Subscribe to vehicle events
            vehicle.OnStatusChanged += (v, status) => OnVehicleStatusChanged(vehicle);
            vehicle.OnCargoChanged += (v) => OnVehicleCargoChanged(vehicle);
            
            if (showDebugInfo)
                Debug.Log($"Created UI overlay for Vehicle {vehicle.vehicleId}");
        }
    }
    
    /// <summary>
    /// Called when a vehicle is destroyed
    /// </summary>
    public void UnregisterVehicle(Vehicle vehicle)
    {
        if (vehicle == null) return;
        
        if (vehicleUIMap.ContainsKey(vehicle))
        {
            Destroy(vehicleUIMap[vehicle]);
            vehicleUIMap.Remove(vehicle);
            
            if (showDebugInfo)
                Debug.Log($"Removed UI overlay for Vehicle {vehicle.vehicleId}");
        }
    }
    
    GameObject CreateUIOverlay(Vehicle vehicle)
    {
        if (vehicleOverlayPrefab == null)
        {
            Debug.LogError("VehicleUIOverlay: vehicleOverlayPrefab not assigned!");
            return null;
        }
        
        GameObject uiOverlay = Instantiate(vehicleOverlayPrefab, uiContainer);
        
        RectTransform rt = uiOverlay.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
        }
        
        uiOverlay.name = $"VehicleOverlay_{vehicle.vehicleId}";
        
        return uiOverlay;
    }
    
    void UpdateUIPosition(Vehicle vehicle, GameObject uiOverlay)
    {
        if (vehicle == null || uiOverlay == null || mainCamera == null)
            return;
        
        Vector3 worldPos = vehicle.transform.position;
        Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPos);
        
        RectTransform rt = uiOverlay.GetComponent<RectTransform>();
        if (rt != null)
        {
            // Convert screen to canvas position
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                uiContainer.GetComponent<RectTransform>(),
                screenPos,
                mainCamera, // Use null for Screen Space - Overlay canvas
                out Vector2 localPoint
            );
            
            rt.anchoredPosition = localPoint + uiOffset;
        }
    }
    
    void UpdateOverlayContent(Vehicle vehicle, GameObject uiOverlay)
    {
        if (vehicle == null || uiOverlay == null)
            return;
        
        // Find status text
        Transform statusTextTransform = uiOverlay.transform.Find("StatusText");
        if (statusTextTransform != null)
        {
            TextMeshProUGUI statusText = statusTextTransform.GetComponent<TextMeshProUGUI>();
            if (statusText != null)
            {
                statusText.text = GetStatusText(vehicle);
            }
        }
        
        // Find cargo text
        Transform cargoTextTransform = uiOverlay.transform.Find("CargoText");
        if (cargoTextTransform != null)
        {
            TextMeshProUGUI cargoText = cargoTextTransform.GetComponent<TextMeshProUGUI>();
            if (cargoText != null)
            {
                cargoText.text = GetCargoText(vehicle);
            }
        }
    }
    
    string GetStatusText(Vehicle vehicle)
    {
        switch (vehicle.currentStatus)
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
    
    string GetCargoText(Vehicle vehicle)
    {
        int totalCargo = vehicle.GetTotalCargo();
        
        if (totalCargo <= 0)
        {
            return "Empty";
        }
        
        ResourceType cargoType = vehicle.GetPrimaryCargoType();
        string cargoTypeName = cargoType == ResourceType.Population ? "Clients" : "Food";
        return $"{cargoTypeName}: {totalCargo}/{vehicle.maxCargoCapacity}";
    }
    
    void OnVehicleStatusChanged(Vehicle vehicle)
    {
        if (vehicleUIMap.ContainsKey(vehicle))
        {
            UpdateOverlayContent(vehicle, vehicleUIMap[vehicle]);
        }
    }
    
    void OnVehicleCargoChanged(Vehicle vehicle)
    {
        if (vehicleUIMap.ContainsKey(vehicle))
        {
            UpdateOverlayContent(vehicle, vehicleUIMap[vehicle]);
        }
    }
    
    void Update()
    {
        // Update all UI positions each frame
        foreach (var kvp in vehicleUIMap)
        {
            if (kvp.Key != null && kvp.Value != null)
            {
                UpdateUIPosition(kvp.Key, kvp.Value);
            }
        }
    }
    
    public GameObject GetUIOverlayForVehicle(Vehicle vehicle)
    {
        if (vehicleUIMap.ContainsKey(vehicle))
        {
            return vehicleUIMap[vehicle];
        }
        return null;
    }
}
