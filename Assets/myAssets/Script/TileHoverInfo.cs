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
    public TextMeshProUGUI infoText; // Assign a UI Text to display terrain type

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

        if (!string.IsNullOrEmpty(terrainType))
        {
            infoText.text = "Terrain: " + terrainType;
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
            return "Mountain\nCannot pass\n x: " + x + "  y: " + y;
        if (forestTilemap.HasTile(cellPosition))
            return "Forests\nCannot pass\n x: " + x + "  y: " + y;
        if (roadTilemap.HasTile(cellPosition))
            return "Road\nCan pass, speed up\n x: " + x + "  y: " + y;
        if (riverTilemap.HasTile(cellPosition))
            return "River\nCannot pass\n x: " + x + "  y: " + y;
        if (groundTilemap.HasTile(cellPosition))
            return "Plain\nCan pass\n x: " + x + "  y: " + y;

        return ""; // No tile found, return empty string
    }
    private void DebugLog(string message)
    {
        if (Hover_Text_Debug)
            Debug.Log(message);
    }
}