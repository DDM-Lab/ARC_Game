using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Tilemaps;

public class PathfindingTestController : MonoBehaviour
{
    [Header("System References")]
    public PathfindingSystem pathfindingSystem;
    public RoadTilemapManager roadManager;

    [Header("Test Settings")]
    public KeyCode testPathKey = KeyCode.P;
    public KeyCode testFloodPathKey = KeyCode.F; // NEW: Test flood-aware path
    public KeyCode clearPathKey = KeyCode.C;
    public KeyCode showRoadStatsKey = KeyCode.R;

    [Header("Manual Testing - Buildings")]
    public Building startBuilding;
    public Building endBuilding;

    [Header("Manual Testing - Prebuilt Buildings")]
    public PrebuiltBuilding startPrebuilt;
    public PrebuiltBuilding endPrebuilt;

    [Header("Manual Testing - Mixed")]
    public MonoBehaviour startFacility; // Can be either Building or PrebuiltBuilding
    public MonoBehaviour endFacility;   // Can be either Building or PrebuiltBuilding

    void Start()
    {
        // Find systems if not assigned
        if (pathfindingSystem == null)
            pathfindingSystem = FindObjectOfType<PathfindingSystem>();

        if (roadManager == null)
            roadManager = FindObjectOfType<RoadTilemapManager>();

        Debug.Log("PathfindingTestController initialized. Controls:");
        Debug.Log($"Press {testPathKey} to test random pathfinding");
        Debug.Log($"Press {testFloodPathKey} to test flood-aware pathfinding");
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

        // Test flood-aware pathfinding
        if (Input.GetKeyDown(testFloodPathKey))
        {
            TestRandomFloodAwarePathfinding();
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
    /// Test flood-aware pathfinding between random facilities
    /// </summary>
    void TestRandomFloodAwarePathfinding()
    {
        if (pathfindingSystem == null)
        {
            Debug.LogError("PathfindingSystem not found!");
            return;
        }

        // Get all connected facilities (both buildings and prebuilt)
        List<MonoBehaviour> connectedFacilities = GetAllConnectedFacilities();

        if (connectedFacilities.Count < 2)
        {
            Debug.LogWarning("Need at least 2 facilities connected to roads for pathfinding test");
            return;
        }

        // Pick random start and end facilities
        MonoBehaviour start = connectedFacilities[Random.Range(0, connectedFacilities.Count)];
        MonoBehaviour end = connectedFacilities[Random.Range(0, connectedFacilities.Count)];

        // Make sure they're different
        while (end == start && connectedFacilities.Count > 1)
        {
            end = connectedFacilities[Random.Range(0, connectedFacilities.Count)];
        }

        Debug.Log($"Testing FLOOD-AWARE path from {start.name} to {end.name}");

        // Test both normal and flood-aware paths
        List<Vector3> normalPath = pathfindingSystem.FindPath(start.transform.position, end.transform.position);
        List<Vector3> floodPath = pathfindingSystem.FindFloodAwarePath(start.transform.position, end.transform.position);

        Debug.Log($"Normal path: {normalPath.Count} waypoints");
        Debug.Log($"Flood-aware path: {floodPath.Count} waypoints");

        // Get time estimation
        DeliveryTimeEstimate estimate = pathfindingSystem.EstimateDeliveryTime(start.transform.position, end.transform.position);
        Debug.Log($"Delivery estimate: {estimate.GetSummary()}");
    }

    /// <summary>
    /// Test pathfinding between random buildings (original method)
    /// </summary>
    void TestRandomPathfinding()
    {
        if (pathfindingSystem == null)
        {
            Debug.LogError("PathfindingSystem not found!");
            return;
        }

        List<MonoBehaviour> connectedFacilities = GetAllConnectedFacilities();

        if (connectedFacilities.Count < 2)
        {
            Debug.LogWarning("Need at least 2 facilities connected to roads for pathfinding test");
            return;
        }

        MonoBehaviour start = connectedFacilities[Random.Range(0, connectedFacilities.Count)];
        MonoBehaviour end = connectedFacilities[Random.Range(0, connectedFacilities.Count)];

        while (end == start && connectedFacilities.Count > 1)
        {
            end = connectedFacilities[Random.Range(0, connectedFacilities.Count)];
        }

        Debug.Log($"Testing NORMAL path from {start.name} to {end.name}");
        pathfindingSystem.FindPath(start.transform.position, end.transform.position);
    }

    /// <summary>
    /// Get all facilities (buildings and prebuilt) connected to roads
    /// </summary>
    List<MonoBehaviour> GetAllConnectedFacilities()
    {
        List<MonoBehaviour> connectedFacilities = new List<MonoBehaviour>();

        // Check regular buildings
        Building[] allBuildings = FindObjectsOfType<Building>();
        foreach (Building building in allBuildings)
        {
            RoadConnection connection = building.GetComponent<RoadConnection>();
            if (connection != null && connection.IsConnectedToRoad)
            {
                connectedFacilities.Add(building);
            }
        }

        // Check prebuilt buildings
        PrebuiltBuilding[] allPrebuilts = FindObjectsOfType<PrebuiltBuilding>();
        foreach (PrebuiltBuilding prebuilt in allPrebuilts)
        {
            RoadConnection connection = prebuilt.GetComponent<RoadConnection>();
            if (connection != null && connection.IsConnectedToRoad)
            {
                connectedFacilities.Add(prebuilt);
            }
        }

        return connectedFacilities;
    }

    /// <summary>
    /// Test pathfinding between manually assigned facilities (supports all types)
    /// </summary>
    [ContextMenu("Test Manual Pathfinding (Buildings)")]
    void TestManualBuildingPathfinding()
    {
        if (startBuilding == null || endBuilding == null)
        {
            Debug.LogWarning("Please assign start and end buildings in the inspector");
            return;
        }

        TestPathBetweenFacilities(startBuilding, endBuilding);
    }

    [ContextMenu("Test Manual Pathfinding (Prebuilt)")]
    void TestManualPrebuiltPathfinding()
    {
        if (startPrebuilt == null || endPrebuilt == null)
        {
            Debug.LogWarning("Please assign start and end prebuilt buildings in the inspector");
            return;
        }

        TestPathBetweenFacilities(startPrebuilt, endPrebuilt);
    }

    [ContextMenu("Test Manual Pathfinding (Mixed)")]
    void TestManualMixedPathfinding()
    {
        if (startFacility == null || endFacility == null)
        {
            Debug.LogWarning("Please assign start and end facilities in the inspector");
            return;
        }

        TestPathBetweenFacilities(startFacility, endFacility);
    }

    /// <summary>
    /// Test pathfinding between two facilities with full analysis
    /// </summary>
    void TestPathBetweenFacilities(MonoBehaviour start, MonoBehaviour end)
    {
        if (pathfindingSystem == null)
        {
            Debug.LogError("PathfindingSystem not found!");
            return;
        }

        Debug.Log($"=== PATHFINDING TEST: {start.name} → {end.name} ===");

        Vector3 startPos = start.transform.position;
        Vector3 endPos = end.transform.position;

        // Test normal pathfinding
        List<Vector3> normalPath = pathfindingSystem.FindPath(startPos, endPos);
        Debug.Log($"Normal path: {(normalPath.Count > 0 ? normalPath.Count + " waypoints" : "NO PATH")}");

        // Test flood-aware pathfinding
        List<Vector3> floodPath = pathfindingSystem.FindFloodAwarePath(startPos, endPos);
        Debug.Log($"Flood-aware path: {(floodPath.Count > 0 ? floodPath.Count + " waypoints" : "NO PATH")}");

        // Get detailed analysis
        PathAnalysis analysis = pathfindingSystem.AnalyzePath(startPos, endPos);
        Debug.Log($"Path Analysis:");
        Debug.Log($"  - Normal path affected by flood: {analysis.isFloodAffected}");
        Debug.Log($"  - Has alternative route: {analysis.hasAlternativeRoute}");
        Debug.Log($"  - Route length difference: {analysis.routeLengthDifference} tiles");

        // Get time estimation
        DeliveryTimeEstimate estimate = pathfindingSystem.EstimateDeliveryTime(startPos, endPos);
        Debug.Log($"Delivery Estimate: {estimate.GetSummary()}");

        // Show the path (will show flood-aware path if available)
        if (floodPath.Count > 0)
        {
            pathfindingSystem.FindFloodAwarePath(startPos, endPos); // This will visualize the path
            Debug.Log("Showing flood-aware path visualization");
        }
        else if (normalPath.Count > 0)
        {
            pathfindingSystem.FindPath(startPos, endPos); // This will visualize the path
            Debug.Log("Showing normal path visualization (flood-aware not available)");
        }
    }

    /// <summary>
    /// Handle mouse click pathfinding (updated for all facility types)
    /// </summary>
    void HandleMousePathfinding()
    {
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0;

        // Find nearest facility (building or prebuilt) to mouse position
        MonoBehaviour nearestFacility = FindNearestFacility(mouseWorldPos);

        if (nearestFacility != null)
        {
            RoadConnection connection = nearestFacility.GetComponent<RoadConnection>();
            if (connection != null && connection.IsConnectedToRoad)
            {
                // If we have a start facility selected, find path to this facility
                if (startFacility != null && startFacility != nearestFacility)
                {
                    Debug.Log($"Finding flood-aware path from {startFacility.name} to {nearestFacility.name}");
                    TestPathBetweenFacilities(startFacility, nearestFacility);
                    startFacility = null; // Reset for next selection
                }
                else
                {
                    // Set as start facility
                    startFacility = nearestFacility;
                    string facilityType = nearestFacility is Building ? "Building" : "PrebuiltBuilding";
                    Debug.Log($"Start facility set to: {startFacility.name} ({facilityType}). Right-click another facility to find path.");
                }
            }
            else
            {
                Debug.LogWarning($"Facility {nearestFacility.name} is not connected to road network");
            }
        }
    }

    /// <summary>
    /// Find the nearest facility (building or prebuilt) to a world position
    /// </summary>
    MonoBehaviour FindNearestFacility(Vector3 worldPosition)
    {
        List<MonoBehaviour> allFacilities = new List<MonoBehaviour>();

        // Add all buildings
        Building[] buildings = FindObjectsOfType<Building>();
        foreach (Building building in buildings)
        {
            allFacilities.Add(building);
        }

        // Add all prebuilt buildings
        PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>();
        foreach (PrebuiltBuilding prebuilt in prebuilts)
        {
            allFacilities.Add(prebuilt);
        }

        MonoBehaviour nearest = null;
        float shortestDistance = float.MaxValue;

        foreach (MonoBehaviour facility in allFacilities)
        {
            float distance = Vector3.Distance(worldPosition, facility.transform.position);
            if (distance < shortestDistance)
            {
                shortestDistance = distance;
                nearest = facility;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Test delivery route options (normal vs flood-aware)
    /// </summary>
    [ContextMenu("Test Delivery Route Options")]
    public void TestDeliveryRouteOptions()
    {
        List<MonoBehaviour> facilities = GetAllConnectedFacilities();

        if (facilities.Count < 2)
        {
            Debug.LogWarning("Need at least 2 connected facilities");
            return;
        }

        MonoBehaviour start = facilities[0];
        MonoBehaviour end = facilities[1];

        Debug.Log($"=== DELIVERY ROUTE OPTIONS TEST ===");
        Debug.Log($"From: {start.name} To: {end.name}");

        // Check if normal route is available
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem != null)
        {
            DeliveryTimeEstimate estimate;
            bool canDeliver = deliverySystem.CanCreateDeliveryWithEstimate(start, end, out estimate);

            Debug.Log($"Can create delivery: {canDeliver}");
            if (canDeliver)
            {
                Debug.Log($"Route estimate: {estimate.GetSummary()}");
            }
            else
            {
                Debug.Log($"Route blocked: {(estimate.isFloodBlocked ? "Flood blocking" : "No path available")}");
            }
        }

        // Show path analysis
        PathAnalysis analysis = pathfindingSystem.AnalyzePath(start.transform.position, end.transform.position);

        if (analysis.isFloodAffected && analysis.hasAlternativeRoute)
        {
            Debug.Log("AGENT WOULD SAY: 'The fastest route is blocked by flood, but I found an alternative route that takes longer.'");
        }
        else if (analysis.isFloodAffected && !analysis.hasAlternativeRoute)
        {
            Debug.Log("AGENT WOULD SAY: 'The route is completely blocked by flood. We need to wait for it to recede or use emergency alternatives.'");
        }
        else
        {
            Debug.Log("AGENT WOULD SAY: 'Route is clear for delivery.'");
        }
    }

    // Rest of the existing methods stay the same...
    void ClearCurrentPath()
    {
        if (pathfindingSystem != null)
        {
            pathfindingSystem.ClearPath();
            Debug.Log("Path cleared");
        }
    }

    void ShowRoadStatistics()
    {
        if (roadManager != null)
        {
            roadManager.PrintRoadStatistics();
        }

        List<MonoBehaviour> connectedFacilities = GetAllConnectedFacilities();
        int totalFacilities = FindObjectsOfType<Building>().Length + FindObjectsOfType<PrebuiltBuilding>().Length;

        Debug.Log($"=== FACILITY CONNECTION STATUS ===");
        Debug.Log($"Total Facilities: {totalFacilities}, Connected: {connectedFacilities.Count}");

        foreach (MonoBehaviour facility in connectedFacilities)
        {
            RoadConnection connection = facility.GetComponent<RoadConnection>();
            string facilityType = facility is Building ? ((Building)facility).GetBuildingType().ToString() :
                                                      ((PrebuiltBuilding)facility).GetPrebuiltType().ToString();
            Debug.Log($"{facility.name} ({facilityType}): CONNECTED at {connection.NearestRoadPosition}");
        }
    }

    [ContextMenu("Test All Facility Connections")]
    public void TestAllFacilityConnections()
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

        Debug.Log("Forced connection check on all facilities");
    }
}