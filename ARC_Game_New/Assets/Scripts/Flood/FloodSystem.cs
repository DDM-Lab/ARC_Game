using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.Collections;
using System;
using System.Linq;

public class FloodSystem : MonoBehaviour
{
    [Header("Tilemap References")]
    public Tilemap groundTilemap;
    public Tilemap floodTilemap;
    public Tilemap terrainBlockingTilemap; // Optional: forests, mountains

    [Header("Tiles")]
    public RuleTile riverRuleTile;
    public RuleTile floodRuleTile;
    public TileBase[] terrainBlockingTiles; // Array of tiles that block flood spread

    [Header("Flood Configuration")]
    public FloodParameters floodParameters;

    [Header("Change Tracking")]
    private int previousFloodCount = 0;
    private int floodChangeThisRound = 0;

    [Header("Debug")]
    public bool showDebugGizmos = true;
    public Color floodGizmoColor = Color.blue;
    public Color riverGizmoColor = Color.cyan;

    // Flood state tracking
    private HashSet<Vector3Int> currentFloodTiles = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> riverTiles = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> blockedPositions = new HashSet<Vector3Int>(); // Positions affected by blocking tiles
    private Queue<Vector3Int> floodExpansionQueue = new Queue<Vector3Int>();
    private WeatherType lastWeatherType = WeatherType.Sunny;

    // Events
    public event Action<Vector3Int> OnFloodTileAdded;
    public event Action<Vector3Int> OnFloodTileRemoved;
    public event Action<int> OnFloodSizeChanged;
    public event System.Action<int> OnFloodExpanded; // Parameter: tiles expanded
    public event System.Action<int> OnFloodShrank;   // Parameter: tiles shrank
    public event System.Action<int> OnFloodChanged;  // Parameter: net change

    // Singleton for easy access
    public static FloodSystem Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        InitializeFloodSystem();
        SubscribeToEvents();
    }

    void InitializeFloodSystem()
    {
        if (!ValidateReferences())
            return;

        // Find all river tiles as potential flood sources
        CacheRiverTiles();

        // Cache blocking positions
        CacheBlockingPositions();

        // Start with no flood - only spawn when it rains
        currentFloodTiles.Clear();
        floodTilemap.SetTilesBlock(floodTilemap.cellBounds, new TileBase[floodTilemap.cellBounds.size.x * floodTilemap.cellBounds.size.y]);

        if (floodParameters.enableDebugLogs)
            Debug.Log($"Flood System initialized with {riverTiles.Count} river tiles, {blockedPositions.Count} blocked positions. Starting with no flood.");
    }

    bool ValidateReferences()
    {
        if (groundTilemap == null)
        {
            Debug.LogError("FloodSystem: Ground Tilemap not assigned!");
            return false;
        }

        if (floodTilemap == null)
        {
            Debug.LogError("FloodSystem: Flood Tilemap not assigned!");
            return false;
        }

        if (riverRuleTile == null)
        {
            Debug.LogError("FloodSystem: River Rule Tile not assigned!");
            return false;
        }

        if (floodRuleTile == null)
        {
            Debug.LogError("FloodSystem: Flood Rule Tile not assigned!");
            return false;
        }

        if (floodParameters == null)
        {
            Debug.LogError("FloodSystem: Flood Parameters not assigned!");
            return false;
        }

        return true;
    }

    void CacheBlockingPositions()
    {
        blockedPositions.Clear();

        if (terrainBlockingTilemap == null || terrainBlockingTiles == null || terrainBlockingTiles.Length == 0)
        {
            if (floodParameters.enableDebugLogs)
                Debug.Log("No terrain blocking configured");
            return;
        }

        BoundsInt bounds = terrainBlockingTilemap.cellBounds;
        Debug.Log($"=== CACHING BLOCKING POSITIONS ===");
        Debug.Log($"Terrain blocking tilemap bounds: {bounds}");
        Debug.Log($"Blocking radius: {floodParameters.blockingRadius}");

        int blockingTileCount = 0;

        // Find all blocking tiles first
        List<Vector3Int> blockingTilePositions = new List<Vector3Int>();
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int position = new Vector3Int(x, y, 0);
                TileBase tile = terrainBlockingTilemap.GetTile(position);

                if (tile != null && IsBlockingTile(tile))
                {
                    blockingTilePositions.Add(position);
                    blockingTileCount++;
                }
            }
        }

        // For each blocking tile, mark positions within radius as blocked
        foreach (Vector3Int blockingPos in blockingTilePositions)
        {
            for (int dx = -floodParameters.blockingRadius; dx <= floodParameters.blockingRadius; dx++)
            {
                for (int dy = -floodParameters.blockingRadius; dy <= floodParameters.blockingRadius; dy++)
                {
                    Vector3Int affectedPos = blockingPos + new Vector3Int(dx, dy, 0);
                    blockedPositions.Add(affectedPos);
                }
            }
        }

        Debug.Log($"Found {blockingTileCount} blocking tiles, affecting {blockedPositions.Count} positions total");
    }

    bool IsBlockingTile(TileBase tile)
    {
        foreach (TileBase blockingTile in terrainBlockingTiles)
        {
            if (tile == blockingTile)
                return true;
        }
        return false;
    }

    void CacheRiverTiles()
    {
        riverTiles.Clear();
        BoundsInt bounds = groundTilemap.cellBounds;

        Debug.Log($"=== CACHING RIVER TILES ===");
        Debug.Log($"Ground tilemap bounds: {bounds}");
        Debug.Log($"River rule tile assigned: {riverRuleTile != null}");

        int foundRivers = 0;
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int position = new Vector3Int(x, y, 0);
                TileBase tile = groundTilemap.GetTile(position);

                if (tile == riverRuleTile)
                {
                    riverTiles.Add(position);
                    foundRivers++;
                }
            }
        }

        Debug.Log($"Cached {riverTiles.Count} river tiles total");

        if (riverTiles.Count == 0)
        {
            Debug.LogWarning("No river tiles found! Check if riverRuleTile matches tiles in groundTilemap");
        }
    }

    void InitializeFloodFromRivers()
    {
        // Start flood at all river positions
        foreach (Vector3Int riverPos in riverTiles)
        {
            AddFloodTile(riverPos, false); // Don't trigger events during initialization
        }

        // Trigger size changed event after initialization
        OnFloodSizeChanged?.Invoke(currentFloodTiles.Count);
    }

    void SubscribeToEvents()
    {
        // Subscribe to simulation events for flood updates
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnSimulationStarted += OnSimulationStarted;
            // Also listen to time segment changes for additional updates
            GlobalClock.Instance.OnTimeSegmentChanged += OnTimeSegmentChanged;
        }
    }

    void OnSimulationStarted()
    {
        // Update flood when each simulation round ends
        UpdateFlood();
    }

    void OnTimeSegmentChanged(int timeSegment)
    {
        // Optional: Additional flood update on time segment changes
        // This ensures flood updates even if simulation events aren't working
        if (floodParameters.enableDebugLogs)
            Debug.Log($"Time segment changed to {timeSegment}, updating flood");
    }

    void UpdateFlood()
    {
        Debug.Log("=== FLOOD UPDATE STARTED ===");
        int floodCountBefore = currentFloodTiles.Count;
        Debug.Log($"Current flood tiles count: {floodCountBefore}");
        
        if (WeatherSystem.Instance == null) 
        {
            Debug.LogError("WeatherSystem.Instance is null!");
            return;
        }
        
        WeatherType currentWeather = WeatherSystem.Instance.GetCurrentWeather();
        WeatherFloodData weatherData = GetWeatherFloodData(currentWeather);
        float rainIntensity = WeatherSystem.Instance.GetRainIntensity();
        
        Debug.Log($"Current weather: {currentWeather}, Rain intensity: {rainIntensity}");
        
        bool wasRaining = WeatherSystem.Instance.IsRaining();
        bool weatherChanged = lastWeatherType != currentWeather;
        lastWeatherType = currentWeather;
        
        if (rainIntensity >= floodParameters.minimumRainForSpawning)
        {
            if (currentFloodTiles.Count == 0 || (weatherChanged && rainIntensity > 0))
            {
                Debug.Log("Spawning flood due to rain");
                SpawnFloodFromRain(rainIntensity);
            }
        }
        
        if (weatherData.expansionRate > 0 && currentFloodTiles.Count > 0)
        {
            Debug.Log("Attempting flood expansion...");
            ExpandFlood(weatherData);
        }
        
        if (rainIntensity < floodParameters.minimumRainForSpawning)
        {
            Debug.Log("Rain stopped - flood will shrink/disappear");
            ShrinkFlood(weatherData);
        }
        else if (weatherData.shrinkageChance > 0)
        {
            Debug.Log("Attempting flood shrinkage...");
            ShrinkFlood(weatherData);
        }
        
        int floodCountAfter = currentFloodTiles.Count;
        int floodChange = floodCountAfter - floodCountBefore;
        
        Debug.Log($"Flood tiles before: {floodCountBefore}, after: {floodCountAfter}, change: {floodChange}");
        
        // NEW: Track and trigger flood change events
        if (floodChange > 0)
        {
            OnFloodExpanded?.Invoke(floodChange);
            Debug.Log($"🌊 Flood expanded by {floodChange} tiles");
        }
        else if (floodChange < 0)
        {
            OnFloodShrank?.Invoke(-floodChange);
            Debug.Log($"🌅 Flood shrank by {-floodChange} tiles");
        }
        
        if (floodChange != 0)
        {
            OnFloodChanged?.Invoke(floodChange);
        }
        
        // Update previous count for triggers
        previousFloodCount = floodCountBefore;
        floodChangeThisRound = floodChange;
        
        OnFloodSizeChanged?.Invoke(currentFloodTiles.Count);
        
        Debug.Log("=== FLOOD UPDATE COMPLETED ===");
    }

    // Add getter methods for triggers
    public int GetPreviousFloodCount() => previousFloodCount;
    public int GetFloodChangeThisRound() => floodChangeThisRound;


    void SpawnFloodFromRain(float rainIntensity)
    {
        Debug.Log($"--- SPAWNING FLOOD FROM RAIN (intensity: {rainIntensity}) ---");

        float spawnChance = floodParameters.floodSpawnChance +
                           (rainIntensity * floodParameters.rainIntensitySpawnBonus);

        Debug.Log($"Flood spawn chance: {spawnChance:F2}");

        int spawned = 0;
        foreach (Vector3Int riverPos in riverTiles)
        {
            if (UnityEngine.Random.value < spawnChance)
            {
                AddFloodTile(riverPos, false);
                spawned++;
            }
        }

        Debug.Log($"Spawned flood at {spawned}/{riverTiles.Count} river positions");

        // Trigger events after spawning
        if (spawned > 0)
        {
            OnFloodSizeChanged?.Invoke(currentFloodTiles.Count);
        }
    }

    WeatherFloodData GetWeatherFloodData(WeatherType weather)
    {
        foreach (var data in floodParameters.weatherFloodRates)
        {
            if (data.weatherType == weather)
                return data;
        }

        // Fallback to sunny weather data
        return floodParameters.weatherFloodRates[0];
    }

    void ExpandFlood(WeatherFloodData weatherData)
    {
        Debug.Log($"--- FLOOD EXPANSION START ---");
        int tilesToExpand = Mathf.RoundToInt(weatherData.expansionRate);
        Debug.Log($"Tiles to expand: {tilesToExpand}");

        List<Vector3Int> expansionCandidates = new List<Vector3Int>();

        // Get all possible expansion positions
        Debug.Log($"Checking expansion from {currentFloodTiles.Count} current flood tiles");
        foreach (Vector3Int floodPos in currentFloodTiles)
        {
            List<Vector3Int> neighbors = GetAdjacentPositions(floodPos);
            foreach (Vector3Int neighbor in neighbors)
            {
                if (CanFloodSpreadTo(neighbor, weatherData))
                {
                    expansionCandidates.Add(neighbor);
                    Debug.Log($"Added expansion candidate: {neighbor}");
                }
            }
        }

        // Remove duplicates
        int candidatesBeforeDedup = expansionCandidates.Count;
        expansionCandidates = new List<Vector3Int>(new HashSet<Vector3Int>(expansionCandidates));
        Debug.Log($"Expansion candidates: {candidatesBeforeDedup} -> {expansionCandidates.Count} (after dedup)");

        // Expand flood to random candidates
        int actualExpansions = 0;
        for (int i = 0; i < tilesToExpand && expansionCandidates.Count > 0; i++)
        {
            int randomIndex = UnityEngine.Random.Range(0, expansionCandidates.Count);
            Vector3Int expandTo = expansionCandidates[randomIndex];

            Debug.Log($"Expanding to: {expandTo}");
            AddFloodTile(expandTo);
            actualExpansions++;
            expansionCandidates.RemoveAt(randomIndex);
        }

        Debug.Log($"Actual expansions: {actualExpansions}/{tilesToExpand}");

        // Handle random expansion beyond normal spread
        HandleRandomExpansion(weatherData);

        Debug.Log($"--- FLOOD EXPANSION END ---");
    }

    void HandleRandomExpansion(WeatherFloodData weatherData)
    {
        if (UnityEngine.Random.value > floodParameters.randomExpansionChance)
            return;

        // Pick a random flood tile as expansion source
        if (currentFloodTiles.Count == 0) return;

        Vector3Int[] floodArray = new Vector3Int[currentFloodTiles.Count];
        currentFloodTiles.CopyTo(floodArray);
        Vector3Int sourcePos = floodArray[UnityEngine.Random.Range(0, floodArray.Length)];

        // Try to expand in a random direction up to max distance
        Vector3Int[] directions = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        Vector3Int direction = directions[UnityEngine.Random.Range(0, directions.Length)];

        int expansionDistance = UnityEngine.Random.Range(1, floodParameters.maxRandomExpansionDistance + 1);
        Vector3Int targetPos = sourcePos + (direction * expansionDistance);

        if (CanFloodSpreadTo(targetPos, weatherData))
        {
            AddFloodTile(targetPos);

            if (floodParameters.enableDebugLogs)
                Debug.Log($"Random flood expansion: {sourcePos} -> {targetPos} (distance: {expansionDistance})");
        }
    }

    void ShrinkFlood(WeatherFloodData weatherData)
    {
        Debug.Log($"--- FLOOD SHRINKAGE START ---");
        List<Vector3Int> tilesToRemove = new List<Vector3Int>();

        // Get rain intensity to determine shrinkage behavior
        float rainIntensity = WeatherSystem.Instance.GetRainIntensity();

        // If no rain, dramatically increase shrinkage (flood dries up)
        float shrinkChance = weatherData.shrinkageChance + floodParameters.baseShrinkageChance;
        if (rainIntensity < floodParameters.minimumRainForSpawning)
        {
            shrinkChance += 0.8f; // Heavy shrinkage when not raining
            Debug.Log("No rain - applying heavy shrinkage");
        }

        foreach (Vector3Int floodPos in currentFloodTiles)
        {
            float tileShrinkChance = shrinkChance;

            // Edge tiles have higher chance to shrink
            if (IsFloodEdgeTile(floodPos))
            {
                tileShrinkChance += floodParameters.edgeShrinkageBonus;
            }

            if (UnityEngine.Random.value < tileShrinkChance)
            {
                tilesToRemove.Add(floodPos);
            }
        }

        // Remove flood tiles
        foreach (Vector3Int pos in tilesToRemove)
        {
            RemoveFloodTile(pos);
        }

        Debug.Log($"Flood shrinkage: Removed {tilesToRemove.Count} tiles (shrink chance: {shrinkChance:F2})");
        Debug.Log($"--- FLOOD SHRINKAGE END ---");
    }

    bool CanFloodSpreadTo(Vector3Int position, WeatherFloodData weatherData)
    {
        Debug.Log($"    Checking if flood can spread to {position}");

        // Already flooded
        if (currentFloodTiles.Contains(position))
        {
            Debug.Log($"    -> NO: Already flooded");
            return false;
        }

        // Check if position is within tilemap bounds
        if (!groundTilemap.cellBounds.Contains(position))
        {
            Debug.Log($"    -> NO: Outside tilemap bounds {groundTilemap.cellBounds}");
            return false;
        }

        // Check for terrain blocking
        if (IsTerrainBlocked(position))
        {
            // Apply terrain block multiplier to spread chance
            float blockedSpreadChance = floodParameters.baseSpreadChance *
                                       weatherData.spreadChanceMultiplier *
                                       floodParameters.terrainBlockMultiplier;

            bool canSpread = UnityEngine.Random.value < blockedSpreadChance;
            Debug.Log($"    -> Terrain blocked. Spread chance: {blockedSpreadChance:F2}, result: {canSpread}");
            return canSpread;
        }

        // Check if it's land (can flood over land)
        TileBase groundTile = groundTilemap.GetTile(position);
        Debug.Log($"    -> Ground tile at position: {(groundTile != null ? groundTile.name : "null")}");

        if (groundTile != null && groundTile != riverRuleTile)
        {
            // Apply land spread multiplier
            float landSpreadChance = floodParameters.baseSpreadChance *
                                   weatherData.spreadChanceMultiplier *
                                   floodParameters.landSpreadMultiplier;

            bool canSpread = UnityEngine.Random.value < landSpreadChance;
            Debug.Log($"    -> Land tile. Spread chance: {landSpreadChance:F2}, result: {canSpread}");
            return canSpread;
        }

        // Default spread chance for river tiles or empty spaces
        float normalSpreadChance = floodParameters.baseSpreadChance * weatherData.spreadChanceMultiplier;
        bool canSpreadNormal = UnityEngine.Random.value < normalSpreadChance;
        Debug.Log($"    -> River/empty. Spread chance: {normalSpreadChance:F2}, result: {canSpreadNormal}");
        return canSpreadNormal;
    }

    bool IsTerrainBlocked(Vector3Int position)
    {
        return blockedPositions.Contains(position);
    }

    bool IsFloodEdgeTile(Vector3Int position)
    {
        List<Vector3Int> neighbors = GetAdjacentPositions(position);

        foreach (Vector3Int neighbor in neighbors)
        {
            if (!currentFloodTiles.Contains(neighbor))
                return true;
        }

        return false;
    }

    List<Vector3Int> GetAdjacentPositions(Vector3Int center)
    {
        return new List<Vector3Int>
        {
            center + Vector3Int.up,
            center + Vector3Int.down,
            center + Vector3Int.left,
            center + Vector3Int.right
        };
    }

    void AddFloodTile(Vector3Int position, bool triggerEvents = true)
    {
        if (currentFloodTiles.Contains(position))
            return;

        currentFloodTiles.Add(position);
        floodTilemap.SetTile(position, floodRuleTile);

        if (triggerEvents)
        {
            OnFloodTileAdded?.Invoke(position);

            if (floodParameters.enableDebugLogs)
                Debug.Log($"Flood tile added at {position}");
        }
    }

    void RemoveFloodTile(Vector3Int position)
    {
        if (!currentFloodTiles.Contains(position))
            return;

        currentFloodTiles.Remove(position);
        floodTilemap.SetTile(position, null);

        OnFloodTileRemoved?.Invoke(position);

        if (floodParameters.enableDebugLogs)
            Debug.Log($"Flood tile removed at {position}");
    }

    // Public query methods
    public bool IsFloodedAt(Vector3Int position)
    {
        return currentFloodTiles.Contains(position);
    }

    public bool IsFloodedAt(Vector3 worldPosition)
    {
        Vector3Int gridPos = floodTilemap.WorldToCell(worldPosition);
        return IsFloodedAt(gridPos);
    }

    public HashSet<Vector3Int> GetAllFloodPositions()
    {
        return new HashSet<Vector3Int>(currentFloodTiles);
    }

    public HashSet<Vector3Int> GetAllRiverPositions()
    {
        return new HashSet<Vector3Int>(riverTiles);
    }

    public int GetFloodTileCount()
    {
        return currentFloodTiles.Count;
    }

    public List<Vector3Int> GetFloodedBuildingPositions(List<Vector3> buildingPositions)
    {
        List<Vector3Int> floodedBuildings = new List<Vector3Int>();

        foreach (Vector3 buildingPos in buildingPositions)
        {
            Vector3Int gridPos = floodTilemap.WorldToCell(buildingPos);
            if (IsFloodedAt(gridPos))
            {
                floodedBuildings.Add(gridPos);
            }
        }

        return floodedBuildings;
    }

    public bool IsRoadFlooded(Vector3Int roadPosition)
    {
        return IsFloodedAt(roadPosition);
    }

    /// <summary>
    /// Check if a path is clear of flood tiles
    /// </summary>
    public bool IsPathClearOfFlood(List<Vector3> worldPath)
    {
        if (worldPath == null || worldPath.Count == 0) return true;

        foreach (Vector3 worldPos in worldPath)
        {
            if (IsFloodedAt(worldPos))
            {
                if (floodParameters.enableDebugLogs)
                    Debug.Log($"Flood detected on path at position: {worldPos}");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Check if route between two points is flood-free
    /// </summary>
    public bool IsRouteClearOfFlood(Vector3 startPos, Vector3 endPos)
    {
        PathfindingSystem pathfinder = FindObjectOfType<PathfindingSystem>();
        if (pathfinder == null) return true;

        List<Vector3> path = pathfinder.FindPath(startPos, endPos);
        return IsPathClearOfFlood(path);
    }

    /// <summary>
    /// Find alternative flood-free route (if possible)
    /// </summary>
    public List<Vector3> FindFloodFreeRoute(Vector3 startPos, Vector3 endPos)
    {
        PathfindingSystem pathfinder = FindObjectOfType<PathfindingSystem>();
        if (pathfinder == null) return new List<Vector3>();

        // Use modified pathfinding that avoids flood tiles
        return pathfinder.FindFloodAwarePath(startPos, endPos);
    }

    // Manual flood control methods (for testing/debugging)
    [ContextMenu("Force Flood Update")]
    public void ForceFloodUpdate()
    {
        UpdateFlood();
    }

    [ContextMenu("Clear All Flood")]
    public void ClearAllFlood()
    {
        Debug.Log("=== CLEARING ALL FLOOD ===");
        Debug.Log($"Current flood tiles before clear: {currentFloodTiles.Count}");

        List<Vector3Int> tilesToRemove = new List<Vector3Int>(currentFloodTiles);
        Debug.Log($"Tiles to remove: {tilesToRemove.Count}");

        int removedCount = 0;
        foreach (Vector3Int pos in tilesToRemove)
        {
            // Keep river tiles flooded
            if (!riverTiles.Contains(pos))
            {
                Debug.Log($"Removing flood tile at: {pos}");
                RemoveFloodTile(pos);
                removedCount++;
            }
            else
            {
                Debug.Log($"Keeping river tile flooded at: {pos}");
            }
        }

        Debug.Log($"Removed {removedCount} flood tiles, {currentFloodTiles.Count} tiles remaining");
        OnFloodSizeChanged?.Invoke(currentFloodTiles.Count);
        Debug.Log("=== FLOOD CLEAR COMPLETED ===");
    }

    [ContextMenu("Reset Flood to Rivers")]
    public void ResetFloodToRivers()
    {
        // Clear all flood tiles
        foreach (Vector3Int pos in currentFloodTiles)
        {
            floodTilemap.SetTile(pos, null);
        }
        currentFloodTiles.Clear();

        // Re-initialize from rivers
        InitializeFloodFromRivers();

        Debug.Log("Flood reset to river positions only");
    }

    [ContextMenu("Add Flood Tiles at Left Part of Map")]
    public void AddFloodTilesAtLeftPart()
    {
        for (int x = -5; x <= -1; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                Vector3Int floodPos = new Vector3Int(x, y, 0);
                AddFloodTile(floodPos);
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        // Draw flood tiles
        Gizmos.color = floodGizmoColor;
        foreach (Vector3Int floodPos in currentFloodTiles)
        {
            Vector3 worldPos = floodTilemap.CellToWorld(floodPos) + floodTilemap.tileAnchor;
            Gizmos.DrawWireCube(worldPos, Vector3.one * 0.8f);
        }

        // Draw river tiles
        Gizmos.color = riverGizmoColor;
        foreach (Vector3Int riverPos in riverTiles)
        {
            Vector3 worldPos = groundTilemap.CellToWorld(riverPos) + groundTilemap.tileAnchor;
            Gizmos.DrawWireCube(worldPos, Vector3.one * 0.6f);
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnSimulationStarted -= OnSimulationStarted;
            GlobalClock.Instance.OnTimeSegmentChanged -= OnTimeSegmentChanged;
        }
    }
    
    [ContextMenu("Test: Create Flood at Vehicle Position")]
    public void TestCreateFloodAtVehicle()
    {
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
        if (vehicles.Length == 0)
        {
            Debug.LogWarning("No vehicles found to test flood blocking");
            return;
        }
        
        // Get first vehicle that's moving
        Vehicle targetVehicle = null;
        foreach (Vehicle vehicle in vehicles)
        {
            if (vehicle.GetCurrentStatus() == VehicleStatus.InTransit)
            {
                targetVehicle = vehicle;
                break;
            }
        }
        
        // If no moving vehicle, use first available
        if (targetVehicle == null)
            targetVehicle = vehicles[0];
        
        Vector3 vehiclePos = targetVehicle.transform.position;
        Vector3Int floodPos = floodTilemap.WorldToCell(vehiclePos);
        
        // Create flood at vehicle position
        AddFloodTile(floodPos);
        
        Debug.Log($"Created flood tile at {vehiclePos} to block vehicle {targetVehicle.GetVehicleName()}");
    }

    [ContextMenu("Test: Create Flood Path Between Buildings")]
    public void TestCreateFloodPath()
    {
        // Fix the LINQ syntax - FindObjectsOfType returns an array, not a single object
        Building kitchen = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Kitchen).FirstOrDefault();
        Building shelter = FindObjectsOfType<Building>().Where(b => b.GetBuildingType() == BuildingType.Shelter).FirstOrDefault();
        
        if (kitchen == null || shelter == null)
        {
            Debug.LogWarning("Need at least one kitchen and one shelter to test flood path");
            return;
        }
        
        // Create flood tiles between them
        Vector3 startPos = kitchen.transform.position;
        Vector3 endPos = shelter.transform.position;
        Vector3 midPoint = (startPos + endPos) / 2;
        
        // Create a line of flood tiles in the middle
        for (int i = -2; i <= 2; i++)
        {
            Vector3 floodWorldPos = midPoint + new Vector3(i * 2, 0, 0);
            Vector3Int floodGridPos = floodTilemap.WorldToCell(floodWorldPos);
            AddFloodTile(floodGridPos);
        }
        
        Debug.Log($"Created flood path between {kitchen.name} and {shelter.name}");
    }

    [ContextMenu("Test: Force Vehicle Damage")]
    public void TestForceVehicleDamage()
    {
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
        if (vehicles.Length == 0)
        {
            Debug.LogWarning("No vehicles found to damage");
            return;
        }
        
        Vehicle targetVehicle = vehicles[0];
        
        // Force damage the vehicle
        targetVehicle.isDamaged = true;
        targetVehicle.SetStatus(VehicleStatus.Damaged);
        
        // Trigger repair task
        if (FloodTaskGenerator.Instance != null)
        {
            FloodTaskGenerator.Instance.CreateVehicleRepairTask(targetVehicle);
        }
        
        Debug.Log($"Forced damage on vehicle {targetVehicle.GetVehicleName()} and created repair task");
    }
}