using UnityEngine;
using System.Collections.Generic;
using System.Linq;


[System.Serializable]
public class DeliveryTimeEstimate
{
    public bool pathExists;
    public int roadTileCount;
    public float totalDistance;
    public float estimatedTimeSeconds;
    public bool isFloodBlocked;
    
    public string GetFormattedTime()
    {
        if (!pathExists) return "No route available";
        
        int minutes = Mathf.FloorToInt(estimatedTimeSeconds / 60);
        int seconds = Mathf.FloorToInt(estimatedTimeSeconds % 60);
        return $"{minutes:D2}:{seconds:D2}";
    }
    
    public string GetSummary()
    {
        if (!pathExists)
        {
            return isFloodBlocked ? "Route blocked by flood" : "No route available";
        }
        
        return $"{roadTileCount} tiles, {GetFormattedTime()}";
    }
}

[System.Serializable]
public class PathAnalysis
{
    public bool normalPathExists;
    public int normalPathLength;
    public bool floodAwarePathExists;
    public int floodAwarePathLength;
    public bool isFloodAffected;
    public bool hasAlternativeRoute;
    public int routeLengthDifference;
}

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

        if (roadManager == null)
        {
            Debug.LogError("RoadTilemapManager not found in PathfindingSystem!");
            GameLogPanel.Instance.LogError("RoadTilemapManager not found in PathfindingSystem!");
            return;
        }

        if (pathVisualizer == null)
            pathVisualizer = FindObjectOfType<PathVisualizer>();
            
        if (pathVisualizer == null)
        {
            Debug.LogWarning("PathVisualizer not found! Path visualization will be disabled.");
            GameLogPanel.Instance.LogError("PathVisualizer not found! Path visualization will be disabled.");
        }
    }
    
    /// <summary>
    /// Find path between two world positions
    /// </summary>
    public List<Vector3> FindPath(Vector3 startWorld, Vector3 endWorld)
    {
        if (roadManager == null)
        {
            Debug.LogError("RoadManager not found!");
            GameLogPanel.Instance.LogError("RoadManager not found in PathfindingSystem!");
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
            Debug.LogWarning($"Start position {startPos} is not on a road. Pathfinding failed. Nearest road to start is at {roadManager.FindNearestRoadPosition(roadManager.CellToWorld(startPos))}.");
            GameLogPanel.Instance.LogError($"Start position {startPos} is not on a road. Pathfinding failed. Nearest road to start is at {roadManager.FindNearestRoadPosition(roadManager.CellToWorld(startPos))}.");
            return new List<Vector3Int>();
        }
        
        if (!roadManager.HasRoadAt(endPos))
        {
            Debug.LogWarning($"End position {endPos} is not on a road. Pathfinding failed. Nearest road to end is at {roadManager.FindNearestRoadPosition(roadManager.CellToWorld(endPos))}.");
            GameLogPanel.Instance.LogError($"End position {endPos} is not on a road. Pathfinding failed. Nearest road to end is at {roadManager.FindNearestRoadPosition(roadManager.CellToWorld(endPos))}.");
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
                    Debug.Log($"Pathfinding succeeded in {iterations} iterations");
                }
                //GameLogPanel.Instance.LogDebug($"Pathfinding succeeded in {iterations} iterations for {startPos} to {endPos}.");
                //GameLogPanel.Instance.LogEnvironmentChange($"Path from {startPos} to {endPos} found.");
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
            Debug.Log($"No path found after {iterations} iterations");
        }

        //GameLogPanel.Instance.LogDebug($"No path found after {iterations} iterations for {startPos} to {endPos}.");
        //GameLogPanel.Instance.LogEnvironmentChange($"Path from {startPos} to {endPos} not found.");
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
            GameLogPanel.Instance.LogError("RoadManager not found in PathfindingSystem!");
            return new List<Vector3>();
        }
        
        // Convert world positions to grid positions
        Vector3Int startGrid = roadManager.FindNearestRoadPosition(startWorld);
        Vector3Int endGrid = roadManager.FindNearestRoadPosition(endWorld);
        
        //if (showDebugInfo)
        //    Debug.Log($"Flood-aware pathfinding: {startWorld} -> {endWorld} started.");

        // Find path using flood-aware A*
        List<Vector3Int> gridPath = FindPathAStarFloodAware(startGrid, endGrid);
        
        // Convert grid path to world positions
        List<Vector3> worldPath = new List<Vector3>();
        foreach (Vector3Int gridPos in gridPath)
        {
            worldPath.Add(roadManager.CellToWorld(gridPos));
        }
        
        if (showDebugInfo)
        {
            if (worldPath.Count > 0)
                Debug.Log($"Found flood-aware path with {worldPath.Count} waypoints from {startWorld} to {endWorld}");
            else
                Debug.Log($"No flood-free path available from {startWorld} to {endWorld}");
        }

        // Visualize the path
        /*if (worldPath.Count > 0)
        {
            GameLogPanel.Instance.LogEnvironmentChange($"Flood-aware path found with {worldPath.Count} waypoints from {startWorld} to {endWorld}.");
        }
        else
        {
            GameLogPanel.Instance.LogEnvironmentChange($"No flood-free path available from {startWorld} to {endWorld}.");
        }*/
        
        return worldPath;
    }


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
            
            // Must have road AND not be flooded
            if (roadManager.HasRoadAt(neighborPos))
            {
                Vector3 worldPos = roadManager.CellToWorld(neighborPos);
                bool isFlooded = FloodSystem.Instance != null && FloodSystem.Instance.IsFloodedAt(worldPos);
                
                // Only add if not flooded - treat flooded tiles as if they don't exist
                if (!isFlooded)
                {
                    neighbors.Add(neighborPos);
                }
            }
        }
        
        return neighbors;
    }

    /// <summary>
    /// Estimate delivery time for a path
    /// </summary>
    public DeliveryTimeEstimate EstimateDeliveryTime(Vector3 startPos, Vector3 endPos, float vehicleSpeed = 5f)
    {
        List<Vector3> path = FindFloodAwarePath(startPos, endPos);
        
        DeliveryTimeEstimate estimate = new DeliveryTimeEstimate();
        estimate.pathExists = path.Count > 0;
        
        if (!estimate.pathExists)
        {
            estimate.roadTileCount = 0;
            estimate.estimatedTimeSeconds = -1f; // Invalid
            estimate.isFloodBlocked = !FloodSystem.Instance.IsRouteClearOfFlood(startPos, endPos);
            return estimate;
        }
        
        // Calculate path distance and tiles
        estimate.roadTileCount = path.Count - 1; // Number of segments
        estimate.totalDistance = CalculatePathDistance(path);
        estimate.estimatedTimeSeconds = estimate.totalDistance / vehicleSpeed;
        
        // Add loading/unloading time
        estimate.estimatedTimeSeconds += 2f; // 1 second each for loading and unloading
        
        // Check if path avoids flood
        estimate.isFloodBlocked = false;
        
        if (showDebugInfo)
        {
            Debug.Log($"Delivery estimate: {estimate.roadTileCount} tiles, {estimate.totalDistance:F1} distance, {estimate.estimatedTimeSeconds:F1} seconds");
        }
        //GameLogPanel.Instance.LogEnvironmentChange($"Delivery estimate: {estimate.roadTileCount} tiles, {estimate.totalDistance:F1} distance, {estimate.estimatedTimeSeconds:F1} seconds from {startPos} to {endPos}.");
        
        return estimate;
    }

    /// <summary>
    /// Calculate total distance of a path
    /// </summary>
    float CalculatePathDistance(List<Vector3> path)
    {
        if (path.Count < 2) return 0f;
        
        float totalDistance = 0f;
        for (int i = 0; i < path.Count - 1; i++)
        {
            totalDistance += Vector3.Distance(path[i], path[i + 1]);
        }
        
        return totalDistance;
    }

    /// <summary>
    /// Get detailed path analysis
    /// </summary>
    public PathAnalysis AnalyzePath(Vector3 startPos, Vector3 endPos)
    {
        PathAnalysis analysis = new PathAnalysis();
        
        // Try normal pathfinding first
        List<Vector3> normalPath = FindPath(startPos, endPos);
        analysis.normalPathExists = normalPath.Count > 0;
        analysis.normalPathLength = normalPath.Count - 1;
        
        // Try flood-aware pathfinding
        List<Vector3> floodPath = FindFloodAwarePath(startPos, endPos);
        analysis.floodAwarePathExists = floodPath.Count > 0;
        analysis.floodAwarePathLength = floodPath.Count - 1;
        
        // Check if flood is blocking
        analysis.isFloodAffected = analysis.normalPathExists && !analysis.floodAwarePathExists;
        analysis.hasAlternativeRoute = !analysis.normalPathExists && analysis.floodAwarePathExists;
        analysis.routeLengthDifference = analysis.floodAwarePathLength - analysis.normalPathLength;
        
        if (showDebugInfo)
        {
            Debug.Log($"Path Analysis: Normal={analysis.normalPathLength} tiles, Flood-aware={analysis.floodAwarePathLength} tiles, Difference={analysis.routeLengthDifference}");
        }
        return analysis;
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
            GameLogPanel.Instance.LogError("Start or end position is not on a road in flood-aware pathfinding!");
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
                    Debug.Log($"Flood-aware path found in {iterations} iterations from {startPos} to {endPos}");
                //GameLogPanel.Instance.LogDebug($"Flood-aware path found in {iterations} iterations for {startPos} to {endPos}.");
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
        //GameLogPanel.Instance.LogDebug($"No flood-free path found after {iterations} iterations for {startPos} to {endPos}.");
        return new List<Vector3Int>();
    }

    public bool CanCreateDeliveryWithEstimate(MonoBehaviour source, MonoBehaviour destination, out DeliveryTimeEstimate estimate)
    {
        estimate = new DeliveryTimeEstimate();
        
        if (source == null || destination == null)
        {
            estimate.pathExists = false;
            return false;
        }
        
        PathfindingSystem pathfinder = FindObjectOfType<PathfindingSystem>();
        if (pathfinder == null)
        {
            estimate.pathExists = false;
            return false;
        }
        
        Vector3 sourcePos = source.transform.position;
        Vector3 destPos = destination.transform.position;
        
        estimate = pathfinder.EstimateDeliveryTime(sourcePos, destPos);
        
        if (showDebugInfo && estimate.pathExists)
        {
            Debug.Log($"Delivery estimate from {source.name} to {destination.name}: {estimate.GetSummary()}");
        }
        if (estimate.pathExists)
        {
            GameLogPanel.Instance.LogEnvironmentChange($"Delivery estimate from {source.name} to {destination.name}: {estimate.GetSummary()}");
        }
        return estimate.pathExists;
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

    [ContextMenu("Test: Analyze Current Paths")]
    public void TestAnalyzeCurrentPaths()
    {
        Building[] buildings = FindObjectsOfType<Building>();
        if (buildings.Length < 2)
        {
            Debug.LogWarning("Need at least 2 buildings for path analysis");
            return;
        }

        Vector3 start = buildings[0].transform.position;
        Vector3 end = buildings[1].transform.position;

        PathAnalysis analysis = AnalyzePath(start, end);
        DeliveryTimeEstimate estimate = EstimateDeliveryTime(start, end);

        Debug.Log($"=== PATH ANALYSIS ===");
        Debug.Log($"From: {buildings[0].name} To: {buildings[1].name}");
        Debug.Log($"Normal path: {(analysis.normalPathExists ? analysis.normalPathLength + " tiles" : "None")}");
        Debug.Log($"Flood-aware path: {(analysis.floodAwarePathExists ? analysis.floodAwarePathLength + " tiles" : "None")}");
        Debug.Log($"Delivery estimate: {estimate.GetSummary()}");
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