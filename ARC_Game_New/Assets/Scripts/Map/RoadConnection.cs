using UnityEngine;
using System.Collections.Generic;

public class RoadConnection : MonoBehaviour
{
    [Header("Connection Settings")]
    public float connectionRadius = 2f; // How close to road the building needs to be
    public bool requiresRoadConnection = true; // Whether this building needs road access
    
    [Header("Visual Debug")]
    public bool showConnectionStatus = true;
    public Color connectedColor = Color.green;
    public Color disconnectedColor = Color.red;
    public float gizmoSize = 0.5f;
    
    private RoadTilemapManager roadManager;
    private bool isConnectedToRoad = false;
    private Vector3Int nearestRoadPosition;
    private float lastCheckTime = 0f;
    private float checkInterval = 1f; // Check connection every second
    
    void Start()
    {
        // Find road manager
        roadManager = FindObjectOfType<RoadTilemapManager>();
        
        if (roadManager == null)
        {
            Debug.LogWarning($"RoadTilemapManager not found for {gameObject.name}");
            GameLogPanel.Instance.LogError($"RoadTilemapManager not found for {gameObject.name} in RoadConnection script.");
            return;
        }
        
        // Initial connection check
        CheckRoadConnection();
    }
    
    void Update()
    {
        // Periodic connection check (for performance)
        if (Time.time - lastCheckTime > checkInterval)
        {
            CheckRoadConnection();
            lastCheckTime = Time.time;
        }
    }
    
    /// <summary>
    /// Check if this building is connected to the road network (using pathfinding logic)
    /// </summary>
    public void CheckRoadConnection()
    {
        if (roadManager == null)
            return;
        
        Vector3 buildingPosition = transform.position;
        bool wasConnected = isConnectedToRoad;
        
        // Use pathfinding system's road finding logic
        Vector3Int nearestRoad = roadManager.FindNearestRoadPosition(buildingPosition);
        
        // If a road was found, we're connected
        isConnectedToRoad = roadManager.HasRoadAt(nearestRoad);
        nearestRoadPosition = nearestRoad;
        
        // Calculate distance for display purposes
        if (isConnectedToRoad)
        {
            Vector3 nearestRoadWorld = roadManager.CellToWorld(nearestRoad);
            float distance = Vector3.Distance(buildingPosition, nearestRoadWorld);
            
            if (wasConnected != isConnectedToRoad)
            {
                Debug.Log($"{gameObject.name} connected to road at {nearestRoadPosition} (distance: {distance:F2})");
            }
        }
        else if (wasConnected)
        {
            GameLogPanel.Instance.LogError($"{gameObject.name} was connected and now disconnected from road connections.");
            Debug.Log($"{gameObject.name} disconnected from road network");
        }
    }
    
    /// <summary>
    /// Get the road connection point for pathfinding
    /// </summary>
    public Vector3 GetRoadConnectionPoint()
    {
        if (!isConnectedToRoad)
        {
            GameLogPanel.Instance.LogError($"{gameObject.name} is not connected to road network.");
            Debug.LogWarning($"{gameObject.name} is not connected to road network");
            return transform.position;
        }
        
        return roadManager.CellToWorld(nearestRoadPosition);
    }
    
    /// <summary>
    /// Check if building can be used for operations (requires road connection)
    /// </summary>
    public bool CanOperate()
    {
        if (!requiresRoadConnection)
            return true;
        
        return isConnectedToRoad;
    }
    
    /// <summary>
    /// Find all adjacent road positions
    /// </summary>
    public List<Vector3Int> GetAdjacentRoadPositions()
    {
        List<Vector3Int> adjacentRoads = new List<Vector3Int>();
        
        if (roadManager == null)
            return adjacentRoads;
        
        Vector3Int buildingGridPos = roadManager.WorldToCell(transform.position);
        
        // Check positions around the building
        for (int x = -Mathf.CeilToInt(connectionRadius); x <= Mathf.CeilToInt(connectionRadius); x++)
        {
            for (int y = -Mathf.CeilToInt(connectionRadius); y <= Mathf.CeilToInt(connectionRadius); y++)
            {
                Vector3Int checkPos = buildingGridPos + new Vector3Int(x, y, 0);
                
                if (roadManager.HasRoadAt(checkPos))
                {
                    Vector3 roadWorldPos = roadManager.CellToWorld(checkPos);
                    float distance = Vector3.Distance(transform.position, roadWorldPos);
                    
                    if (distance <= connectionRadius)
                    {
                        adjacentRoads.Add(checkPos);
                    }
                }
            }
        }
        
        return adjacentRoads;
    }
    
    /// <summary>
    /// Get the best road connection point (closest to building)
    /// </summary>
    public Vector3Int GetBestRoadConnection()
    {
        List<Vector3Int> adjacentRoads = GetAdjacentRoadPositions();
        
        if (adjacentRoads.Count == 0)
        {
            return nearestRoadPosition; // Fallback to nearest road
        }
        
        Vector3Int bestConnection = adjacentRoads[0];
        float shortestDistance = Vector3.Distance(transform.position, roadManager.CellToWorld(bestConnection));
        
        foreach (Vector3Int roadPos in adjacentRoads)
        {
            float distance = Vector3.Distance(transform.position, roadManager.CellToWorld(roadPos));
            if (distance < shortestDistance)
            {
                shortestDistance = distance;
                bestConnection = roadPos;
            }
        }
        
        return bestConnection;
    }
    
    /// <summary>
    /// Force immediate connection check
    /// </summary>
    [ContextMenu("Check Road Connection")]
    public void ForceCheckConnection()
    {
        CheckRoadConnection();
        Debug.Log($"{gameObject.name} - Connected: {isConnectedToRoad}, Nearest Road: {nearestRoadPosition}");
    }
    
    /// <summary>
    /// Debug visualization
    /// </summary>
    void OnDrawGizmos()
    {
        if (!showConnectionStatus)
            return;
        
        // Draw connection status
        Gizmos.color = isConnectedToRoad ? connectedColor : disconnectedColor;
        Gizmos.DrawWireSphere(transform.position, gizmoSize);
        
        // Draw connection radius
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, connectionRadius);
        
        // Draw line to nearest road
        if (roadManager != null && Application.isPlaying)
        {
            Vector3 nearestRoadWorld = roadManager.CellToWorld(nearestRoadPosition);
            
            Gizmos.color = isConnectedToRoad ? connectedColor : disconnectedColor;
            Gizmos.DrawLine(transform.position, nearestRoadWorld);
            
            // Draw nearest road position
            Gizmos.DrawWireCube(nearestRoadWorld, Vector3.one * 0.8f);
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (roadManager == null)
            return;
        
        // Show all adjacent roads when selected
        List<Vector3Int> adjacentRoads = GetAdjacentRoadPositions();
        
        Gizmos.color = Color.cyan;
        foreach (Vector3Int roadPos in adjacentRoads)
        {
            Vector3 worldPos = roadManager.CellToWorld(roadPos);
            Gizmos.DrawCube(worldPos, Vector3.one * 0.6f);
        }
    }
    
    // Public getters
    public bool IsConnectedToRoad => isConnectedToRoad;
    public Vector3Int NearestRoadPosition => nearestRoadPosition;
    public float ConnectionRadius => connectionRadius;
}