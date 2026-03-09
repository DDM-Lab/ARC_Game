using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

public class PathHighlighter : MonoBehaviour
{
    [Header("References")]
    public Tilemap roadTilemap;
    public RoadTilemapManager roadManager;
    
    [Header("Highlight Settings")]
    public Color highlightColor = new Color(0f, 1f, 1f, 0.6f); // Cyan with transparency
    
    [Header("Debug")]
    public bool showDebugInfo = false;
    
    private Dictionary<Vector3Int, Color> originalTileColors = new Dictionary<Vector3Int, Color>();
    private List<Vector3Int> currentHighlightedTiles = new List<Vector3Int>();
    
    public static PathHighlighter Instance { get; private set; }
    
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
        if (roadManager == null)
        {
            roadManager = FindObjectOfType<RoadTilemapManager>();
        }
        
        if (roadManager != null && roadTilemap == null)
        {
            roadTilemap = roadManager.roadTilemap;
        }
        
        if (roadTilemap == null)
        {
            Debug.LogError("PathHighlighter: No road tilemap found!");
        }
    }
    
    /// <summary>
    /// Highlight a path by converting world positions to tile positions
    /// </summary>
    public void HighlightPath(List<Vector3> worldPath)
    {
        if (roadTilemap == null || roadManager == null)
        {
            Debug.LogError("PathHighlighter: Missing tilemap or road manager!");
            return;
        }
        
        if (worldPath == null || worldPath.Count == 0)
        {
            if (showDebugInfo)
                Debug.LogWarning("PathHighlighter: Empty path provided");
            return;
        }
        
        // Clear previous highlights
        ClearHighlights();
        
        // Convert world path to tile positions and highlight
        foreach (Vector3 worldPos in worldPath)
        {
            Vector3Int tilePos = roadManager.WorldToCell(worldPos);
            
            if (roadManager.HasRoadAt(tilePos) && !currentHighlightedTiles.Contains(tilePos))
            {
                HighlightTile(tilePos);
            }
        }
        
        if (showDebugInfo)
            Debug.Log($"PathHighlighter: Highlighted {currentHighlightedTiles.Count} tiles from {worldPath.Count} waypoints");
    }
    
    /// <summary>
    /// Highlight a single tile
    /// </summary>
    void HighlightTile(Vector3Int tilePos)
    {
        // Store original color if not already stored
        if (!originalTileColors.ContainsKey(tilePos))
        {
            Color originalColor = roadTilemap.GetColor(tilePos);
            originalTileColors[tilePos] = originalColor;
        }
        
        // Set highlight color
        roadTilemap.SetColor(tilePos, highlightColor);
        currentHighlightedTiles.Add(tilePos);
    }
    
    /// <summary>
    /// Clear all highlights and restore original colors
    /// </summary>
    public void ClearHighlights()
    {
        if (roadTilemap == null)
            return;
        
        // Restore original colors
        foreach (Vector3Int tilePos in currentHighlightedTiles)
        {
            if (originalTileColors.ContainsKey(tilePos))
            {
                roadTilemap.SetColor(tilePos, originalTileColors[tilePos]);
            }
            else
            {
                roadTilemap.SetColor(tilePos, Color.white);
            }
        }
        
        currentHighlightedTiles.Clear();
        originalTileColors.Clear();
        
        if (showDebugInfo)
            Debug.Log("PathHighlighter: Cleared highlights");
    }
    
    public bool IsPathHighlighted()
    {
        return currentHighlightedTiles.Count > 0;
    }
    
    public int GetHighlightedTileCount()
    {
        return currentHighlightedTiles.Count;
    }
}
