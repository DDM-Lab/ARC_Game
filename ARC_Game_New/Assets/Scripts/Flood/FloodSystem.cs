using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.Collections;
using System;

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
    
    [Header("Debug")]
    public bool showDebugGizmos = true;
    public Color floodGizmoColor = Color.blue;
    public Color riverGizmoColor = Color.cyan;
    
    // Flood state tracking
    private HashSet<Vector3Int> currentFloodTiles = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> riverTiles = new HashSet<Vector3Int>();
    private Queue<Vector3Int> floodExpansionQueue = new Queue<Vector3Int>();
    
    // Events
    public event Action<Vector3Int> OnFloodTileAdded;
    public event Action<Vector3Int> OnFloodTileRemoved;
    public event Action<int> OnFloodSizeChanged;
    
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
        
        // Find all river tiles as flood sources
        CacheRiverTiles();
        
        // Initialize flood from existing river tiles
        InitializeFloodFromRivers();
        
        if (floodParameters.enableDebugLogs)
            Debug.Log($"Flood System initialized with {riverTiles.Count} river tiles and {currentFloodTiles.Count} initial flood tiles");
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
                    //Debug.Log($"Found river tile at: {position}");
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
        // Update flood when each simulation round starts
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
        Debug.Log($"Current flood tiles count: {currentFloodTiles.Count}");
        
        if (WeatherSystem.Instance == null) 
        {
            Debug.LogError("WeatherSystem.Instance is null!");
            return;
        }
        
        WeatherType currentWeather = WeatherSystem.Instance.GetCurrentWeather();
        WeatherFloodData weatherData = GetWeatherFloodData(currentWeather);
        
        Debug.Log($"Current weather: {currentWeather}");
        Debug.Log($"Expansion rate: {weatherData.expansionRate}");
        Debug.Log($"Spread multiplier: {weatherData.spreadChanceMultiplier}");
        Debug.Log($"Shrinkage chance: {weatherData.shrinkageChance}");
        
        int floodCountBefore = currentFloodTiles.Count;
        
        // Handle flood expansion
        if (weatherData.expansionRate > 0)
        {
            Debug.Log("Attempting flood expansion...");
            ExpandFlood(weatherData);
        }
        else
        {
            Debug.Log("No flood expansion (expansion rate is 0)");
        }
        
        // Handle flood shrinkage
        if (weatherData.shrinkageChance > 0)
        {
            Debug.Log("Attempting flood shrinkage...");
            ShrinkFlood(weatherData);
        }
        else
        {
            Debug.Log("No flood shrinkage (shrinkage chance is 0)");
        }
        
        int floodCountAfter = currentFloodTiles.Count;
        Debug.Log($"Flood tiles before: {floodCountBefore}, after: {floodCountAfter}, change: {floodCountAfter - floodCountBefore}");
        
        // Trigger size changed event
        OnFloodSizeChanged?.Invoke(currentFloodTiles.Count);
        
        Debug.Log("=== FLOOD UPDATE COMPLETED ===");
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
        List<Vector3Int> tilesToRemove = new List<Vector3Int>();
        
        foreach (Vector3Int floodPos in currentFloodTiles)
        {
            // Don't shrink river tiles (permanent flood sources)
            if (riverTiles.Contains(floodPos))
                continue;
            
            float shrinkChance = weatherData.shrinkageChance + floodParameters.baseShrinkageChance;
            
            // Edge tiles have higher chance to shrink
            if (IsFloodEdgeTile(floodPos))
            {
                shrinkChance += floodParameters.edgeShrinkageBonus;
            }
            
            if (UnityEngine.Random.value < shrinkChance)
            {
                tilesToRemove.Add(floodPos);
            }
        }
        
        // Remove flood tiles
        foreach (Vector3Int pos in tilesToRemove)
        {
            RemoveFloodTile(pos);
        }
        
        if (floodParameters.enableDebugLogs && tilesToRemove.Count > 0)
            Debug.Log($"Flood shrinkage: Removed {tilesToRemove.Count} tiles");
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
        if (terrainBlockingTilemap == null || terrainBlockingTiles == null || terrainBlockingTiles.Length == 0)
            return false;
        
        TileBase tileAtPosition = terrainBlockingTilemap.GetTile(position);
        if (tileAtPosition == null)
            return false;
        
        foreach (TileBase blockingTile in terrainBlockingTiles)
        {
            if (tileAtPosition == blockingTile)
                return true;
        }
        
        return false;
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
}