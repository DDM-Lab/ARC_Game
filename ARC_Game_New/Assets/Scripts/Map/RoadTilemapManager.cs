using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

public class RoadTilemapManager : MonoBehaviour
{
    [Header("Tilemap Components")]
    public Tilemap roadTilemap;
    public TilemapRenderer tilemapRenderer;
    
    [Header("Road Tiles")]
    public RuleTile roadRuleTile; // Your Road Rule Tile
    public TileBase intersectionTile; // Optional: separate intersection tile (can be null)
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    public Color debugRoadColor = Color.yellow;
    public Color debugIntersectionColor = Color.red;
    
    // Cached road positions for pathfinding
    private HashSet<Vector3Int> roadPositions = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> intersectionPositions = new HashSet<Vector3Int>();
    private bool isInitialized = false;
    
    void Start()
    {
        InitializeRoadSystem();
    }
    
    void InitializeRoadSystem()
    {
        if (roadTilemap == null)
        {
            Debug.LogError("Road Tilemap not assigned!");
            return;
        }
        
        if (roadRuleTile == null)
        {
            Debug.LogError("Road Rule Tile not assigned!");
            return;
        }
        
        // Cache all road positions for efficient pathfinding
        CacheRoadPositions();
        
        // Detect intersections dynamically
        DetectIntersections();
        
        isInitialized = true;
        
        Debug.Log($"Road system initialized with {roadPositions.Count} road tiles and {intersectionPositions.Count} intersections");
    }
    
    /// <summary>
    /// Cache all existing road tile positions for pathfinding
    /// </summary>
    void CacheRoadPositions()
    {
        roadPositions.Clear();
        
        // Get the bounds of the tilemap
        BoundsInt bounds = roadTilemap.cellBounds;
        
        // Iterate through all positions in the tilemap
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int position = new Vector3Int(x, y, 0);
                TileBase tile = roadTilemap.GetTile(position);
                
                // If there's a road tile at this position, cache it
                if (tile != null && IsRoadTile(tile))
                {
                    roadPositions.Add(position);
                }
            }
        }
    }
    
    /// <summary>
    /// Dynamically detect intersection positions based on road connections
    /// </summary>
    void DetectIntersections()
    {
        intersectionPositions.Clear();
        
        foreach (Vector3Int roadPos in roadPositions)
        {
            if (IsIntersection(roadPos))
            {
                intersectionPositions.Add(roadPos);
            }
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"Detected {intersectionPositions.Count} intersections automatically");
            foreach (Vector3Int intersection in intersectionPositions)
            {
                Debug.Log($"Intersection found at: {intersection}");
            }
        }
    }
    
    /// <summary>
    /// Check if a tile is a road tile (supports Rule Tiles)
    /// </summary>
    public bool IsRoadTile(TileBase tile)
    {
        // Check if it's the road Rule Tile
        if (tile == roadRuleTile)
            return true;
        
        // Check if it's a separate intersection tile (optional)
        if (intersectionTile != null && tile == intersectionTile)
            return true;
        
        return false;
    }
    
    /// <summary>
    /// Check if a road position is an intersection (has 3+ road neighbors)
    /// </summary>
    public bool IsIntersection(Vector3Int position)
    {
        if (!HasRoadAt(position))
            return false;
        
        List<Vector3Int> neighbors = GetRoadNeighbors(position);
        return neighbors.Count >= 3; // 3 or more connections = intersection
    }
    
    /// <summary>
    /// Get all intersection positions
    /// </summary>
    public HashSet<Vector3Int> GetAllIntersections()
    {
        if (!isInitialized)
        {
            CacheRoadPositions();
            DetectIntersections();
            isInitialized = true;
        }
        return new HashSet<Vector3Int>(intersectionPositions);
    }
    
    /// <summary>
    /// Check if a position has a road tile
    /// </summary>
    public bool HasRoadAt(Vector3Int position)
    {
        return roadPositions.Contains(position);
    }
    
    /// <summary>
    /// Get all road positions (for pathfinding)
    /// </summary>
    public HashSet<Vector3Int> GetAllRoadPositions()
    {
        if (!isInitialized)
        {
            CacheRoadPositions();
            isInitialized = true;
        }
        return new HashSet<Vector3Int>(roadPositions);
    }
    
    /// <summary>
    /// Get neighbors of a road position (4-directional)
    /// </summary>
    public List<Vector3Int> GetRoadNeighbors(Vector3Int position)
    {
        List<Vector3Int> neighbors = new List<Vector3Int>();
        
        // Check 4 directions (up, down, left, right)
        Vector3Int[] directions = {
            Vector3Int.up,
            Vector3Int.down,
            Vector3Int.left,
            Vector3Int.right
        };
        
        foreach (Vector3Int direction in directions)
        {
            Vector3Int neighborPos = position + direction;
            if (HasRoadAt(neighborPos))
            {
                neighbors.Add(neighborPos);
            }
        }
        
        return neighbors;
    }
    
    /// <summary>
    /// Convert world position to grid position
    /// </summary>
    public Vector3Int WorldToCell(Vector3 worldPosition)
    {
        return roadTilemap.WorldToCell(worldPosition);
    }
    
    /// <summary>
    /// Convert grid position to world position
    /// </summary>
    public Vector3 CellToWorld(Vector3Int cellPosition)
    {
        return roadTilemap.CellToWorld(cellPosition) + roadTilemap.tileAnchor;
    }
    
    /// <summary>
    /// Find the nearest road position to a world position (extended search)
    /// </summary>
    public Vector3Int FindNearestRoadPosition(Vector3 worldPosition)
    {
        Vector3Int centerCell = WorldToCell(worldPosition);
        Vector3Int nearestRoad = centerCell;
        float nearestDistance = float.MaxValue;
        
        // Search in expanding square pattern with larger radius
        for (int radius = 0; radius <= 10; radius++) // 增加搜索范围到10
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    // Only check the border of the current radius
                    if (radius > 0 && Mathf.Abs(x) < radius && Mathf.Abs(y) < radius)
                        continue;
                        
                    Vector3Int checkPos = centerCell + new Vector3Int(x, y, 0);
                    
                    if (HasRoadAt(checkPos))
                    {
                        float distance = Vector3Int.Distance(centerCell, checkPos);
                        if (distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            nearestRoad = checkPos;
                        }
                    }
                }
            }
            
            // If we found a road, return it
            if (nearestDistance < float.MaxValue)
            {
                //Debug.Log($"Found road at distance {nearestDistance} from {worldPosition}");
                break;
            }
        }
        
        if (nearestDistance == float.MaxValue)
        {
            Debug.LogWarning($"No road found within search radius for position {worldPosition}");
        }
        
        return nearestRoad;
    }
    
    /// <summary>
    /// Add a road tile at position (for runtime road building)
    /// </summary>
    public void AddRoadTile(Vector3Int position)
    {
        roadTilemap.SetTile(position, roadRuleTile);
        roadPositions.Add(position);
        
        // Re-detect intersections in the area
        RefreshIntersectionsAroundPosition(position);
        
        if (showDebugInfo)
            Debug.Log($"Added road tile at {position}");
    }
    
    /// <summary>
    /// Remove a road tile at position
    /// </summary>
    public void RemoveRoadTile(Vector3Int position)
    {
        roadTilemap.SetTile(position, null);
        roadPositions.Remove(position);
        intersectionPositions.Remove(position);
        
        // Re-detect intersections in the area
        RefreshIntersectionsAroundPosition(position);
        
        if (showDebugInfo)
            Debug.Log($"Removed road tile at {position}");
    }
    
    /// <summary>
    /// Refresh intersections around a specific position (when roads are added/removed)
    /// </summary>
    void RefreshIntersectionsAroundPosition(Vector3Int position)
    {
        // Check the position itself and all neighbors
        Vector3Int[] checkPositions = {
            position,
            position + Vector3Int.up,
            position + Vector3Int.down,
            position + Vector3Int.left,
            position + Vector3Int.right
        };
        
        foreach (Vector3Int checkPos in checkPositions)
        {
            if (HasRoadAt(checkPos))
            {
                if (IsIntersection(checkPos))
                {
                    intersectionPositions.Add(checkPos);
                }
                else
                {
                    intersectionPositions.Remove(checkPos);
                }
            }
        }
    }
    
    /// <summary>
    /// Refresh the road cache (call after manual tilemap changes)
    /// </summary>
    [ContextMenu("Refresh Road Cache")]
    public void RefreshRoadCache()
    {
        CacheRoadPositions();
        DetectIntersections();
        Debug.Log($"Road cache refreshed: {roadPositions.Count} road tiles, {intersectionPositions.Count} intersections");
    }
    
    /// <summary>
    /// Debug: Show all road positions and intersections in scene view
    /// </summary>
    void OnDrawGizmos()
    {
        if (!showDebugInfo || !isInitialized)
            return;
        
        // Draw regular roads
        Gizmos.color = debugRoadColor;
        foreach (Vector3Int roadPos in roadPositions)
        {
            if (!intersectionPositions.Contains(roadPos)) // Don't draw regular road color for intersections
            {
                Vector3 worldPos = CellToWorld(roadPos);
                Gizmos.DrawWireCube(worldPos, Vector3.one * 0.8f);
            }
        }
        
        // Draw intersections with different color
        Gizmos.color = debugIntersectionColor;
        foreach (Vector3Int intersectionPos in intersectionPositions)
        {
            Vector3 worldPos = CellToWorld(intersectionPos);
            Gizmos.DrawCube(worldPos, Vector3.one * 0.6f); // Solid cube for intersections
            
            // Draw connection lines to show why it's an intersection
            List<Vector3Int> neighbors = GetRoadNeighbors(intersectionPos);
            foreach (Vector3Int neighbor in neighbors)
            {
                Vector3 neighborWorld = CellToWorld(neighbor);
                Gizmos.DrawLine(worldPos, neighborWorld);
            }
        }
    }
    
    /// <summary>
    /// Get road system statistics for debugging
    /// </summary>
    public void PrintRoadStatistics()
    {
        Debug.Log($"=== ROAD SYSTEM STATISTICS ===");
        Debug.Log($"Total road tiles: {roadPositions.Count}");
        Debug.Log($"Intersections: {intersectionPositions.Count}");
        Debug.Log($"Regular roads: {roadPositions.Count - intersectionPositions.Count}");
        Debug.Log($"Tilemap bounds: {roadTilemap.cellBounds}");
        Debug.Log($"Initialized: {isInitialized}");
        
        // Show intersection details
        if (intersectionPositions.Count > 0)
        {
            Debug.Log("Intersection positions:");
            foreach (Vector3Int intersection in intersectionPositions)
            {
                int connectionCount = GetRoadNeighbors(intersection).Count;
                Debug.Log($"  {intersection} - {connectionCount} connections");
            }
        }
    }
    
    [ContextMenu("Print Road Statistics")]
    public void DebugPrintStats()
    {
        PrintRoadStatistics();
    }
}