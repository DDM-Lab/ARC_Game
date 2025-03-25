using System.Collections.Generic; 
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class TileHoverInfo : MonoBehaviour
{
    public Tilemap groundTilemap;   // Assign your Ground tilemap
    public Tilemap riverTilemap;     // Assign your River tilemap
    public Tilemap roadTilemap;     // Assign your Road tilemap
    public Tilemap forestTilemap; // Assign your Tree tilemap
    public Tilemap mountainTilemap; // Assign your Mountain tilemap
    public Tilemap floodedTilemap; // Assign your Flooded tilemap
    public TextMeshProUGUI infoText; // Assign a UI Text to display terrain type
    public FloodManager floodManager; // Reference to FloodManager
    public bool Hover_Text_Debug = false; // Set to true to enable debug logs
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        // Skip update if the game window is out of focus
        if (!Application.isFocused)
        {
            infoText.gameObject.SetActive(false);
            DebugLog("Game window is not focused");
            return;
        }

        // Check if hovering over UI to avoid conflicts
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            infoText.gameObject.SetActive(false);
            DebugLog("Mouse is over UI");
            return;
        }

        // Get mouse position in screen space
        Vector3 mouseScreenPos = Input.mousePosition;
        // Validate screen position to avoid NaN issues
        if (mouseScreenPos.x < 0 || mouseScreenPos.y < 0 || 
            mouseScreenPos.x > Screen.width || mouseScreenPos.y > Screen.height)
        {
            infoText.gameObject.SetActive(false);
            DebugLog("Mouse is out of screen bounds");
            return;
        }

        // Convert screen position to world position
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(mouseScreenPos);
        Vector3Int gridPosition = groundTilemap.WorldToCell(mouseWorldPos);
        
        string terrainType = GetTerrainType(gridPosition);
        string floodInfo = GetFloodInfo(gridPosition);

        if (!string.IsNullOrEmpty(terrainType) || !string.IsNullOrEmpty(floodInfo))
        {
            infoText.text = terrainType + floodInfo;
            infoText.gameObject.SetActive(true);
        }
        else
        {
            infoText.gameObject.SetActive(false);
        }
    }

    string GetTerrainType(Vector3Int cellPosition)
    {
        string x = cellPosition.x.ToString();
        string y = cellPosition.y.ToString();
        
        // Check tilemap layers in order of priority
        if (mountainTilemap.HasTile(cellPosition))
            return $"Mountain\nCannot pass\n x: {x}  y: {y}\n";
        if (forestTilemap.HasTile(cellPosition))
            return $"Forests\nCannot pass\n x: {x}  y: {y}\n";
        if (roadTilemap.HasTile(cellPosition))
            return $"Road\nCan pass, speed up\n x: {x}  y: {y}\n";
        if (riverTilemap.HasTile(cellPosition))
            return $"River\nCannot pass\n x: {x}  y: {y}\n";
        if (groundTilemap.HasTile(cellPosition))
            return $"Plain\nCan pass\n x: {x}  y: {y}\n";

        return ""; // No tile found
    }

    string GetFloodInfo(Vector3Int cellPosition)
    {
        if (floodedTilemap.HasTile(cellPosition) || IsAdjacentToFloodedTile(cellPosition))
        {
            var (recedeChance, spreadChance, floodChance) = floodManager.GetFloodChances(cellPosition);
            int waterBodySize = floodManager.waterBodySizes.ContainsKey(cellPosition) ? floodManager.waterBodySizes[cellPosition] : 1;
            bool isLargeWaterBody = floodManager.largeWaterBodies.Contains(cellPosition);
            bool isSafeZone = !floodManager.IsFarFromMountainsOrForests(cellPosition, 5);

            string waterBodyStatus = isLargeWaterBody ? "Large Water Body\n" : "";
            string safeStatus = isSafeZone ? "Protected by Terrain\n" : "";

            return $"{waterBodyStatus}{safeStatus} Recede: {recedeChance * 100:F1}%\n Spread: {spreadChance * 100:F1}%\n Flood: {floodChance * 100:F1}%\n";
        }
        return "";
    }


    private bool IsAdjacentToFloodedTile(Vector3Int tilePosition)
    {
        List<Vector3Int> neighbors = new List<Vector3Int>
        {
            new Vector3Int(tilePosition.x + 1, tilePosition.y, tilePosition.z),
            new Vector3Int(tilePosition.x - 1, tilePosition.y, tilePosition.z),
            new Vector3Int(tilePosition.x, tilePosition.y + 1, tilePosition.z),
            new Vector3Int(tilePosition.x, tilePosition.y - 1, tilePosition.z),
            new Vector3Int(tilePosition.x + 1, tilePosition.y + 1, tilePosition.z),
            new Vector3Int(tilePosition.x - 1, tilePosition.y + 1, tilePosition.z),
            new Vector3Int(tilePosition.x - 1, tilePosition.y + 1, tilePosition.z),
            new Vector3Int(tilePosition.x + 1, tilePosition.y - 1, tilePosition.z)
        };

        foreach (Vector3Int neighbor in neighbors)
        {
            if (floodedTilemap.HasTile(neighbor))
                return true;
        }
        return false;
    }
    private void DebugLog(string message)
    {
        if (Hover_Text_Debug)
            Debug.Log(message);
    }
}