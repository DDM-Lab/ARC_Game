using UnityEngine;
using System.Collections.Generic;

public class PathfindingTestController : MonoBehaviour
{
    [Header("System References")]
    public PathfindingSystem pathfindingSystem;
    public RoadTilemapManager roadManager;
    
    [Header("Test Settings")]
    public KeyCode testPathKey = KeyCode.P;
    public KeyCode clearPathKey = KeyCode.C;
    public KeyCode showRoadStatsKey = KeyCode.R;
    
    [Header("Manual Testing")]
    public Building startBuilding;
    public Building endBuilding;
    
    void Start()
    {
        // Find systems if not assigned
        if (pathfindingSystem == null)
            pathfindingSystem = FindObjectOfType<PathfindingSystem>();
        
        if (roadManager == null)
            roadManager = FindObjectOfType<RoadTilemapManager>();
        
        Debug.Log("PathfindingTestController initialized. Controls:");
        Debug.Log($"Press {testPathKey} to test random pathfinding");
        Debug.Log($"Press {clearPathKey} to clear current path");
        Debug.Log($"Press {showRoadStatsKey} to show road statistics");
    }
    
    void Update()
    {
        HandleInput();
    }
    
    void HandleInput()
    {
        // Test random pathfinding
        if (Input.GetKeyDown(testPathKey))
        {
            TestRandomPathfinding();
        }
        
        // Clear current path
        if (Input.GetKeyDown(clearPathKey))
        {
            ClearCurrentPath();
        }
        
        // Show road statistics
        if (Input.GetKeyDown(showRoadStatsKey))
        {
            ShowRoadStatistics();
        }
        
        // Mouse click pathfinding (for manual testing)
        if (Input.GetMouseButtonDown(1)) // Right click
        {
            HandleMousePathfinding();
        }
    }
    
    /// <summary>
    /// Test pathfinding between random buildings
    /// </summary>
    void TestRandomPathfinding()
    {
        if (pathfindingSystem == null)
        {
            Debug.LogError("PathfindingSystem not found!");
            return;
        }
        
        // Get all connected buildings
        Building[] allBuildings = FindObjectsOfType<Building>();
        List<Building> connectedBuildings = new List<Building>();
        
        foreach (Building building in allBuildings)
        {
            RoadConnection connection = building.GetComponent<RoadConnection>();
            if (connection != null && connection.IsConnectedToRoad)
            {
                connectedBuildings.Add(building);
            }
        }
        
        if (connectedBuildings.Count < 2)
        {
            Debug.LogWarning("Need at least 2 buildings connected to roads for pathfinding test");
            return;
        }
        
        // Pick random start and end buildings
        Building start = connectedBuildings[Random.Range(0, connectedBuildings.Count)];
        Building end = connectedBuildings[Random.Range(0, connectedBuildings.Count)];
        
        // Make sure they're different
        while (end == start && connectedBuildings.Count > 1)
        {
            end = connectedBuildings[Random.Range(0, connectedBuildings.Count)];
        }
        
        Debug.Log($"Testing path from {start.name} to {end.name}");
        pathfindingSystem.FindPathBetweenBuildings(start, end);
    }
    
    /// <summary>
    /// Test pathfinding between manually assigned buildings
    /// </summary>
    [ContextMenu("Test Manual Pathfinding")]
    void TestManualPathfinding()
    {
        if (startBuilding == null || endBuilding == null)
        {
            Debug.LogWarning("Please assign start and end buildings in the inspector");
            return;
        }
        
        if (pathfindingSystem == null)
        {
            Debug.LogError("PathfindingSystem not found!");
            return;
        }
        
        Debug.Log($"Testing manual path from {startBuilding.name} to {endBuilding.name}");
        pathfindingSystem.FindPathBetweenBuildings(startBuilding, endBuilding);
    }
    
    /// <summary>
    /// Handle mouse click pathfinding
    /// </summary>
    void HandleMousePathfinding()
    {
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0; // Ensure Z is 0 for 2D
        
        // Find nearest building to mouse position
        Building nearestBuilding = FindNearestBuilding(mouseWorldPos);
        
        if (nearestBuilding != null)
        {
            RoadConnection connection = nearestBuilding.GetComponent<RoadConnection>();
            if (connection != null && connection.IsConnectedToRoad)
            {
                // If we have a start building selected, find path to this building
                if (startBuilding != null && startBuilding != nearestBuilding)
                {
                    Debug.Log($"Finding path from {startBuilding.name} to {nearestBuilding.name}");
                    pathfindingSystem.FindPathBetweenBuildings(startBuilding, nearestBuilding);
                    startBuilding = null; // Reset for next selection
                }
                else
                {
                    // Set as start building
                    startBuilding = nearestBuilding;
                    Debug.Log($"Start building set to: {startBuilding.name}. Right-click another building to find path.");
                }
            }
            else
            {
                Debug.LogWarning($"Building {nearestBuilding.name} is not connected to road network");
            }
        }
    }
    
    /// <summary>
    /// Find the nearest building to a world position
    /// </summary>
    Building FindNearestBuilding(Vector3 worldPosition)
    {
        Building[] buildings = FindObjectsOfType<Building>();
        Building nearest = null;
        float shortestDistance = float.MaxValue;
        
        foreach (Building building in buildings)
        {
            float distance = Vector3.Distance(worldPosition, building.transform.position);
            if (distance < shortestDistance)
            {
                shortestDistance = distance;
                nearest = building;
            }
        }
        
        return nearest;
    }
    
    /// <summary>
    /// Clear the current path visualization
    /// </summary>
    void ClearCurrentPath()
    {
        if (pathfindingSystem != null)
        {
            pathfindingSystem.ClearPath();
            Debug.Log("Path cleared");
        }
    }
    
    /// <summary>
    /// Show road system statistics
    /// </summary>
    void ShowRoadStatistics()
    {
        if (roadManager != null)
        {
            roadManager.PrintRoadStatistics();
        }
        
        // Show building connection status for all types
        Building[] buildings = FindObjectsOfType<Building>();
        PrebuiltBuilding[] prebuiltBuildings = FindObjectsOfType<PrebuiltBuilding>();
        
        int connectedBuildings = 0;
        int totalBuildings = buildings.Length + prebuiltBuildings.Length;
        
        Debug.Log("=== BUILDING CONNECTION STATUS ===");
        
        // Check regular buildings
        foreach (Building building in buildings)
        {
            RoadConnection connection = building.GetComponent<RoadConnection>();
            if (connection != null)
            {
                if (connection.IsConnectedToRoad)
                {
                    connectedBuildings++;
                    Debug.Log($"{building.name}: CONNECTED to road at {connection.NearestRoadPosition}");
                }
                else
                {
                    Debug.Log($"{building.name}: NOT CONNECTED to road network");
                }
            }
            else
            {
                Debug.Log($"{building.name}: No RoadConnection component");
            }
        }
        
        // Check prebuilt buildings
        foreach (PrebuiltBuilding prebuilt in prebuiltBuildings)
        {
            RoadConnection connection = prebuilt.GetComponent<RoadConnection>();
            if (connection != null)
            {
                if (connection.IsConnectedToRoad)
                {
                    connectedBuildings++;
                    Debug.Log($"{prebuilt.name}: CONNECTED to road at {connection.NearestRoadPosition}");
                }
                else
                {
                    Debug.Log($"{prebuilt.name}: NOT CONNECTED to road network");
                }
            }
            else
            {
                Debug.Log($"{prebuilt.name}: No RoadConnection component");
            }
        }
        
        Debug.Log($"Total Buildings: {totalBuildings}, Connected: {connectedBuildings}");
    }
    
    /// <summary>
    /// Test all building connections
    /// </summary>
    [ContextMenu("Test All Building Connections")]
    public void TestAllBuildingConnections()
    {
        Building[] buildings = FindObjectsOfType<Building>();
        PrebuiltBuilding[] prebuiltBuildings = FindObjectsOfType<PrebuiltBuilding>();
        
        foreach (Building building in buildings)
        {
            RoadConnection connection = building.GetComponent<RoadConnection>();
            if (connection != null)
            {
                connection.ForceCheckConnection();
            }
        }
        
        foreach (PrebuiltBuilding prebuilt in prebuiltBuildings)
        {
            RoadConnection connection = prebuilt.GetComponent<RoadConnection>();
            if (connection != null)
            {
                connection.ForceCheckConnection();
            }
        }
    }
    
    /*void OnGUI()
    {
        // Simple on-screen instructions
        GUI.Label(new Rect(10, 10, 300, 20), $"Press {testPathKey} - Test Random Path");
        GUI.Label(new Rect(10, 30, 300, 20), $"Press {clearPathKey} - Clear Path");
        GUI.Label(new Rect(10, 50, 300, 20), $"Press {showRoadStatsKey} - Road Stats");
        GUI.Label(new Rect(10, 70, 300, 20), "Right Click - Select Buildings for Path");
        
        if (startBuilding != null)
        {
            GUI.Label(new Rect(10, 90, 300, 20), $"Start: {startBuilding.name} (right-click end building)");
        }
    }*/
}