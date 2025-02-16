using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class TileHoverInfo : MonoBehaviour
{
    public Tilemap groundTilemap;   // Assign your Ground tilemap
    public Tilemap roadTilemap;     // Assign your Road tilemap
    public Tilemap forestTilemap; // Assign your Tree tilemap
    public Tilemap mountainTilemap; // Assign your Mountain tilemap
    public TextMeshProUGUI infoText; // Assign a UI Text to display terrain type

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
            Debug.Log("Game window is not focused");
            return;
        }

        // Check if hovering over UI to avoid conflicts
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            infoText.gameObject.SetActive(false);
            Debug.Log("Mouse is over UI");
            return;
        }

        // Get mouse position in screen space
        Vector3 mouseScreenPos = Input.mousePosition;
        // Validate screen position to avoid NaN issues
        if (mouseScreenPos.x < 0 || mouseScreenPos.y < 0 || 
            mouseScreenPos.x > Screen.width || mouseScreenPos.y > Screen.height)
        {
            infoText.gameObject.SetActive(false);
            //Debug.Log("Mouse is out of screen bounds");
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
        // Check tilemap layers in order of priority
        if (mountainTilemap.HasTile(cellPosition))
            return "Mountain\nCannot pass";
        if (forestTilemap.HasTile(cellPosition))
            return "Forests\nCannot pass";
        if (roadTilemap.HasTile(cellPosition))
            return "Road\nCan pass, speed up";
        if (groundTilemap.HasTile(cellPosition))
            return "Plain\nCan pass";

        return ""; // No tile found, return empty string
    }
}