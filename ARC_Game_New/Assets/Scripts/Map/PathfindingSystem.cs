using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PathfindingSystem : MonoBehaviour
{
    [Header("System References")]
    public RoadTilemapManager roadManager;
    public PathVisualizer pathVisualizer;
    
    [Header("Pathfinding Settings")]
    public float straightCost = 1f;
    public float diagonalCost = 1.4f; // √2 ≈ 1.414
    public float intersectionCost = 1.2f; // Slightly higher cost for intersections
    public bool allowDiagonalMovement = false;
    public bool preferIntersections = false; // Whether to prefer paths through intersections
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showSearchedNodes = false;
    
    private Dictionary<Vector3Int, PathNode> searchedNodes;
    
    void Start()
    {
        // Find references if not assigned
        if (roadManager == null)
            roadManager = FindObjectOfType<RoadTilemapManager>();
        
        if (pathVisualizer == null)
            pathVisualizer = FindObjectOfType<PathVisualizer>();
    }
    
    /// <summary>
    /// Find path between two world positions
    /// </summary>
    public List<Vector3> FindPath(Vector3 startWorld, Vector3 endWorld)
    {
        if (roadManager == null)
        {
            Debug.LogError("RoadManager not found!");
            return new List<Vector3>();
        }
        
        // Convert world positions to grid positions
        Vector3Int startGrid = roadManager.FindNearestRoadPosition(startWorld);
        Vector3Int endGrid = roadManager.FindNearestRoadPosition(endWorld);
        
        if (showDebugInfo)
        {
            Debug.Log($"Pathfinding: {startWorld} -> {endWorld}");
            Debug.Log($"Grid positions: {startGrid} -> {endGrid}");
        }
        
        // Find path using A*
        List<Vector3Int> gridPath = FindPathAStar(startGrid, endGrid);
        
        // Convert grid path to world positions
        List<Vector3> worldPath = new List<Vector3>();
        foreach (Vector3Int gridPos in gridPath)
        {
            worldPath.Add(roadManager.CellToWorld(gridPos));
        }
        
        // Visualize the path
        if (pathVisualizer != null && worldPath.Count > 0)
        {
            pathVisualizer.ShowPath(worldPath);
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"Path found with {worldPath.Count} waypoints");
        }
        
        return worldPath;
    }
    
    /// <summary>
    /// Find path between two buildings
    /// </summary>
    public List<Vector3> FindPathBetweenBuildings(Building startBuilding, Building endBuilding)
    {
        if (startBuilding == null || endBuilding == null)
        {
            Debug.LogWarning("Cannot find path: one or both buildings are null");
            return new List<Vector3>();
        }
        
        Vector3 startPos = startBuilding.transform.position;
        Vector3 endPos = endBuilding.transform.position;
        
        if (showDebugInfo)
        {
            Debug.Log($"Finding path from {startBuilding.GetBuildingType()} to {endBuilding.GetBuildingType()}");
        }
        
        return FindPath(startPos, endPos);
    }
    
    /// <summary>
    /// A* pathfinding algorithm implementation
    /// </summary>
    List<Vector3Int> FindPathAStar(Vector3Int startPos, Vector3Int endPos)
    {
        searchedNodes = new Dictionary<Vector3Int, PathNode>();
        
        // Check if start and end positions are valid
        if (!roadManager.HasRoadAt(startPos))
        {
            Debug.LogWarning($"Start position {startPos} is not on a road!");
            return new List<Vector3Int>();
        }
        
        if (!roadManager.HasRoadAt(endPos))
        {
            Debug.LogWarning($"End position {endPos} is not on a road!");
            return new List<Vector3Int>();
        }
        
        // Initialize open and closed lists
        List<PathNode> openList = new List<PathNode>();
        HashSet<Vector3Int> closedList = new HashSet<Vector3Int>();
        
        // Create start node
        PathNode startNode = new PathNode(startPos, null, 0, CalculateHeuristic(startPos, endPos));
        openList.Add(startNode);
        searchedNodes[startPos] = startNode;
        
        int iterations = 0;
        int maxIterations = 1000; // Prevent infinite loops
        
        while (openList.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            
            // Find node with lowest F cost
            PathNode currentNode = openList.OrderBy(n => n.FCost).ThenBy(n => n.HCost).First();
            
            // Remove from open list and add to closed list
            openList.Remove(currentNode);
            closedList.Add(currentNode.position);
            
            // Check if we reached the destination
            if (currentNode.position == endPos)
            {
                if (showDebugInfo)
                {
                    Debug.Log($"Path found in {iterations} iterations");
                }
                
                return ReconstructPath(currentNode);
            }
            
            // Check all neighbors
            List<Vector3Int> neighbors = GetNeighbors(currentNode.position);
            
            foreach (Vector3Int neighborPos in neighbors)
            {
                // Skip if in closed list
                if (closedList.Contains(neighborPos))
                    continue;
                
                // Calculate new G cost
                float newGCost = currentNode.GCost + CalculateDistance(currentNode.position, neighborPos);
                
                // Check if this path to neighbor is better
                PathNode neighborNode = null;
                bool isInOpenList = searchedNodes.TryGetValue(neighborPos, out neighborNode);
                
                if (!isInOpenList || newGCost < neighborNode.GCost)
                {
                    if (neighborNode == null)
                    {
                        neighborNode = new PathNode(neighborPos, currentNode, newGCost, CalculateHeuristic(neighborPos, endPos));
                        searchedNodes[neighborPos] = neighborNode;
                    }
                    else
                    {
                        neighborNode.parent = currentNode;
                        neighborNode.GCost = newGCost;
                        neighborNode.HCost = CalculateHeuristic(neighborPos, endPos);
                    }
                    
                    if (!isInOpenList)
                    {
                        openList.Add(neighborNode);
                    }
                }
            }
        }
        
        if (showDebugInfo)
        {
            Debug.LogWarning($"No path found after {iterations} iterations");
        }
        
        return new List<Vector3Int>(); // No path found
    }
    
    /// <summary>
    /// Get valid neighbors for pathfinding
    /// </summary>
    List<Vector3Int> GetNeighbors(Vector3Int position)
    {
        List<Vector3Int> neighbors = new List<Vector3Int>();
        
        // 4-directional movement
        Vector3Int[] directions = {
            Vector3Int.up,
            Vector3Int.down,
            Vector3Int.left,
            Vector3Int.right
        };
        
        // Add diagonal directions if allowed
        if (allowDiagonalMovement)
        {
            Vector3Int[] diagonalDirections = {
                new Vector3Int(1, 1, 0),   // up-right
                new Vector3Int(-1, 1, 0),  // up-left
                new Vector3Int(1, -1, 0),  // down-right
                new Vector3Int(-1, -1, 0)  // down-left
            };
            
            directions = directions.Concat(diagonalDirections).ToArray();
        }
        
        foreach (Vector3Int direction in directions)
        {
            Vector3Int neighborPos = position + direction;
            
            if (roadManager.HasRoadAt(neighborPos))
            {
                neighbors.Add(neighborPos);
            }
        }
        
        return neighbors;
    }
    
    /// <summary>
    /// Calculate distance between two positions (with intersection awareness)
    /// </summary>
    float CalculateDistance(Vector3Int from, Vector3Int to)
    {
        Vector3Int diff = to - from;
        float baseCost;
        
        // Manhattan distance for 4-directional movement
        if (!allowDiagonalMovement)
        {
            baseCost = (Mathf.Abs(diff.x) + Mathf.Abs(diff.y)) * straightCost;
        }
        else
        {
            // Diagonal distance for 8-directional movement
            int xDistance = Mathf.Abs(diff.x);
            int yDistance = Mathf.Abs(diff.y);
            int remaining = Mathf.Abs(xDistance - yDistance);
            
            baseCost = Mathf.Min(xDistance, yDistance) * diagonalCost + remaining * straightCost;
        }
        
        // Apply intersection cost modifier
        if (roadManager.IsIntersection(to))
        {
            if (preferIntersections)
            {
                baseCost *= 0.9f; // Reduce cost if we prefer intersections
            }
            else
            {
                baseCost *= intersectionCost; // Increase cost for intersections
            }
        }
        
        return baseCost;
    }
    
    /// <summary>
    /// Calculate heuristic (estimated cost to goal)
    /// </summary>
    float CalculateHeuristic(Vector3Int from, Vector3Int to)
    {
        return CalculateDistance(from, to);
    }
    
    /// <summary>
    /// Reconstruct path from end node to start
    /// </summary>
    List<Vector3Int> ReconstructPath(PathNode endNode)
    {
        List<Vector3Int> path = new List<Vector3Int>();
        PathNode currentNode = endNode;
        
        while (currentNode != null)
        {
            path.Add(currentNode.position);
            currentNode = currentNode.parent;
        }
        
        path.Reverse();
        return path;
    }
    
    /// <summary>
    /// Clear the current path visualization
    /// </summary>
    public void ClearPath()
    {
        if (pathVisualizer != null)
        {
            pathVisualizer.ClearPath();
        }
    }

    /// <summary>
    /// Find path that avoids flood tiles
    /// </summary>
    public List<Vector3> FindFloodAwarePath(Vector3 startWorld, Vector3 endWorld)
    {
        if (roadManager == null)
        {
            Debug.LogError("RoadManager not found!");
            return new List<Vector3>();
        }
        
        // Convert world positions to grid positions
        Vector3Int startGrid = roadManager.FindNearestRoadPosition(startWorld);
        Vector3Int endGrid = roadManager.FindNearestRoadPosition(endWorld);
        
        if (showDebugInfo)
            Debug.Log($"Flood-aware pathfinding: {startWorld} -> {endWorld}");
        
        // Find path using flood-aware A*
        List<Vector3Int> gridPath = FindPathAStarFloodAware(startGrid, endGrid);
        
        // Convert grid path to world positions
        List<Vector3> worldPath = new List<Vector3>();
        foreach (Vector3Int gridPos in gridPath)
        {
            worldPath.Add(roadManager.CellToWorld(gridPos));
        }
        
        return worldPath;
    }

    /// <summary>
    /// A* pathfinding that avoids flood tiles
    /// </summary>
    List<Vector3Int> FindPathAStarFloodAware(Vector3Int startPos, Vector3Int endPos)
    {
        // Similar to FindPathAStar but with flood checking
        searchedNodes = new Dictionary<Vector3Int, PathNode>();
        
        if (!roadManager.HasRoadAt(startPos) || !roadManager.HasRoadAt(endPos))
        {
            Debug.LogWarning("Start or end position is not on a road!");
            return new List<Vector3Int>();
        }
        
        List<PathNode> openList = new List<PathNode>();
        HashSet<Vector3Int> closedList = new HashSet<Vector3Int>();
        
        PathNode startNode = new PathNode(startPos, null, 0, CalculateHeuristic(startPos, endPos));
        openList.Add(startNode);
        searchedNodes[startPos] = startNode;
        
        int iterations = 0;
        int maxIterations = 1000;
        
        while (openList.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            
            PathNode currentNode = openList.OrderBy(n => n.FCost).ThenBy(n => n.HCost).First();
            openList.Remove(currentNode);
            closedList.Add(currentNode.position);
            
            if (currentNode.position == endPos)
            {
                if (showDebugInfo)
                    Debug.Log($"Flood-aware path found in {iterations} iterations");
                return ReconstructPath(currentNode);
            }
            
            List<Vector3Int> neighbors = GetFloodAwareNeighbors(currentNode.position);
            
            foreach (Vector3Int neighborPos in neighbors)
            {
                if (closedList.Contains(neighborPos))
                    continue;
                
                float newGCost = currentNode.GCost + CalculateDistance(currentNode.position, neighborPos);
                
                PathNode neighborNode = null;
                bool isInOpenList = searchedNodes.TryGetValue(neighborPos, out neighborNode);
                
                if (!isInOpenList || newGCost < neighborNode.GCost)
                {
                    if (neighborNode == null)
                    {
                        neighborNode = new PathNode(neighborPos, currentNode, newGCost, CalculateHeuristic(neighborPos, endPos));
                        searchedNodes[neighborPos] = neighborNode;
                    }
                    else
                    {
                        neighborNode.parent = currentNode;
                        neighborNode.GCost = newGCost;
                        neighborNode.HCost = CalculateHeuristic(neighborPos, endPos);
                    }
                    
                    if (!isInOpenList)
                    {
                        openList.Add(neighborNode);
                    }
                }
            }
        }
        
        Debug.LogWarning($"No flood-free path found after {iterations} iterations");
        return new List<Vector3Int>();
    }

    /// <summary>
    /// Get neighbors that avoid flood tiles
    /// </summary>
    List<Vector3Int> GetFloodAwareNeighbors(Vector3Int position)
    {
        List<Vector3Int> neighbors = new List<Vector3Int>();
        
        Vector3Int[] directions = {
            Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
        };
        
        if (allowDiagonalMovement)
        {
            Vector3Int[] diagonalDirections = {
                new Vector3Int(1, 1, 0), new Vector3Int(-1, 1, 0),
                new Vector3Int(1, -1, 0), new Vector3Int(-1, -1, 0)
            };
            directions = directions.Concat(diagonalDirections).ToArray();
        }
        
        foreach (Vector3Int direction in directions)
        {
            Vector3Int neighborPos = position + direction;
            
            if (roadManager.HasRoadAt(neighborPos))
            {
                // Check if this position is flooded
                Vector3 worldPos = roadManager.CellToWorld(neighborPos);
                bool isFlooded = FloodSystem.Instance != null && FloodSystem.Instance.IsFloodedAt(worldPos);
                
                if (!isFlooded)
                {
                    neighbors.Add(neighborPos);
                }
            }
        }
        
        return neighbors;
    }
    
    /// <summary>
    /// Test pathfinding between two random buildings
    /// </summary>
    [ContextMenu("Test Random Path")]
    public void TestRandomPath()
    {
        Building[] buildings = FindObjectsOfType<Building>();
        
        if (buildings.Length < 2)
        {
            Debug.LogWarning("Need at least 2 buildings to test pathfinding");
            return;
        }
        
        Building startBuilding = buildings[Random.Range(0, buildings.Length)];
        Building endBuilding = buildings[Random.Range(0, buildings.Length)];
        
        // Make sure we don't pick the same building
        while (endBuilding == startBuilding)
        {
            endBuilding = buildings[Random.Range(0, buildings.Length)];
        }
        
        FindPathBetweenBuildings(startBuilding, endBuilding);
    }
    
    /// <summary>
    /// Test and display intersection detection
    /// </summary>
    [ContextMenu("Test Intersection Detection")]
    public void TestIntersectionDetection()
    {
        if (roadManager == null)
        {
            Debug.LogError("RoadManager not found!");
            return;
        }
        
        HashSet<Vector3Int> intersections = roadManager.GetAllIntersections();
        
        Debug.Log($"=== INTERSECTION DETECTION TEST ===");
        Debug.Log($"Found {intersections.Count} intersections:");
        
        foreach (Vector3Int intersection in intersections)
        {
            List<Vector3Int> neighbors = roadManager.GetRoadNeighbors(intersection);
            Vector3 worldPos = roadManager.CellToWorld(intersection);
            
            Debug.Log($"Intersection at {intersection} (World: {worldPos}) - {neighbors.Count} connections");
            
            // List all connections
            foreach (Vector3Int neighbor in neighbors)
            {
                Vector3Int direction = neighbor - intersection;
                string directionName = GetDirectionName(direction);
                Debug.Log($"  Connected to {neighbor} ({directionName})");
            }
        }
    }
    
    /// <summary>
    /// Get readable direction name for debugging
    /// </summary>
    string GetDirectionName(Vector3Int direction)
    {
        if (direction == Vector3Int.up) return "North";
        if (direction == Vector3Int.down) return "South";
        if (direction == Vector3Int.left) return "West";
        if (direction == Vector3Int.right) return "East";
        if (direction == new Vector3Int(1, 1, 0)) return "Northeast";
        if (direction == new Vector3Int(-1, 1, 0)) return "Northwest";
        if (direction == new Vector3Int(1, -1, 0)) return "Southeast";
        if (direction == new Vector3Int(-1, -1, 0)) return "Southwest";
        return direction.ToString();
    }
    
    /// <summary>
    /// Debug gizmos for searched nodes
    /// </summary>
    void OnDrawGizmos()
    {
        if (!showSearchedNodes || searchedNodes == null)
            return;
        
        foreach (var kvp in searchedNodes)
        {
            Vector3 worldPos = roadManager.CellToWorld(kvp.Key);
            
            // Color based on node type
            if (kvp.Value.parent == null)
            {
                Gizmos.color = Color.green; // Start node
            }
            else
            {
                Gizmos.color = Color.blue; // Searched node
            }
            
            Gizmos.DrawSphere(worldPos, 0.2f);
        }
    }
}

/// <summary>
/// Node class for A* pathfinding
/// </summary>
public class PathNode
{
    public Vector3Int position;
    public PathNode parent;
    public float GCost; // Distance from start node
    public float HCost; // Heuristic distance to end node
    public float FCost => GCost + HCost; // Total cost
    
    public PathNode(Vector3Int position, PathNode parent, float gCost, float hCost)
    {
        this.position = position;
        this.parent = parent;
        this.GCost = gCost;
        this.HCost = hCost;
    }
}