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
    public GameObject buildingInfoPanel;// Assign a UI Panel for building info
    public TextMeshProUGUI buildingInfoText; // Assign a UI Text to display building info
    public FloodManager floodManager; // Reference to FloodManager
    public bool Hover_Text_Debug = true; // Set to true to enable debug logs
    private CityBuilderCore.Building selectedBuilding; // Store the selected building
    public float baseOrthographicSize = 5f; // Default orthographic size
    public float basePanelScale = 1f;       // Default panel scale at baseOrthographicSize
    public float defaultPanelScaleFactor = 2.0f; // Default scale factor for the panel
    private Vector3 originalPanelScale;
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
        originalPanelScale = buildingInfoPanel.transform.localScale * defaultPanelScaleFactor;
        DebugBuildings();
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

        if (Input.GetMouseButtonDown(0))  // Left-click
        {
            TrySelectBuilding();
        }

        // Update building info panel position if active
        if (buildingInfoPanel.activeSelf && selectedBuilding != null)
        {

            PositionInfoPanelAboveBuilding(selectedBuilding);
            ScalePanelBasedOnZoom(); // Scale the panel after positioning it
        
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
            new Vector3Int(tilePosition.x - 1, tilePosition.y - 1, tilePosition.z),
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

    private void TrySelectBuilding()
    {
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0;
        Vector2 mouseWorld2D = new Vector2(mouseWorldPos.x, mouseWorldPos.y);

        DebugLog($"[TileHoverInfo] Mouse world position: {mouseWorld2D}");

        // Use a simpler raycast first
        RaycastHit2D hit = Physics2D.Raycast(mouseWorld2D, Vector2.zero);

        if (hit.collider != null)
        {
            DebugLog($"[TileHoverInfo] Raycast hit: {hit.collider.gameObject.name}");

            // First try to get Building directly from hit object
            var building = hit.collider.GetComponent<CityBuilderCore.Building>();
            
            // If not found, try parent
            if (building == null)
            {
                building = hit.collider.GetComponentInParent<CityBuilderCore.Building>();
            }

            if (building != null)
            {
                DebugLog($"[TileHoverInfo] Building detected: {building.name}");
                selectedBuilding = building;
                ShowBuildingInfo(building);
                return;
            }
            else
            {
                DebugLog($"[TileHoverInfo] No Building component found on hit object or parents");
            }
        }
        else
        {
            DebugLog($"[TileHoverInfo] Raycast did not hit any collider");
        }

        // Hide panel if no building was hit
        buildingInfoPanel.SetActive(false);
        selectedBuilding = null;
    }

    private void ShowBuildingInfo(CityBuilderCore.Building building)
    {
        // Build info text
        string info = $"<b>{building.name}</b>\nPosition: {building.Point}\n";
        
        // Add component info as before...
        var storage = building.GetBuildingComponent<CityBuilderCore.IStorageComponent>();
        if (storage != null)
        {
            foreach (var item in storage.Storage.GetItemQuantities())
            {
                info += $"{item.Item.Key}: {item.Quantity}\n";
            }
        }

        var community = building.GetComponent<CommunityLogic>();
        if (community != null)
        {
            info += $"Flooded: {community.CheckFlooded()}\n";
        }

        var shelter = building.GetComponent<ShelterLogic>();
        if (shelter != null)
        {
            info += $"Shelter component active.\n";
        }

        // Set the text and activate the panel
        buildingInfoText.text = info;
        buildingInfoPanel.SetActive(true);
        
        // Position the panel above the building
        PositionInfoPanelAboveBuilding(building);
    }

    private void PositionInfoPanelAboveBuilding(CityBuilderCore.Building building)
    {
        Collider2D collider = building.GetComponentInChildren<Collider2D>();
        if (collider == null) return;
        
        // Get the center of the building
        Vector3 center = collider.bounds.center;
        
        // Base vertical offset
        float baseOffset = collider.bounds.extents.y;
        
        // Get zoom factor (inverse relationship - larger value when zoomed out)
        float currentZoom = mainCamera.orthographicSize;
        float referenceZoom = 5.0f; // Reference zoom level
        
        // Calculate position above the building
        Vector3 worldPos = center + new Vector3(0, baseOffset, 0);
        
        // Convert to screen position
        Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPos);
        
        // Get the panel's RectTransform
        RectTransform panelRect = buildingInfoPanel.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            // Set the pivot to be at the bottom center
            panelRect.pivot = new Vector2(0.5f, 0);
            
            // For screen space overlay canvas
            if (buildingInfoPanel.transform.parent.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay)
            {
                panelRect.position = screenPos;
                
                // Scale the panel based on zoom level
                // Use inverse relationship for scaling too
                // When zoomed in, panel should be larger
                // When zoomed out, panel should be smaller
                float scaleFactor = Mathf.Clamp(referenceZoom / currentZoom, 0.5f, 2.0f);
                
                // Apply the scale
                buildingInfoPanel.transform.localScale = originalPanelScale * scaleFactor;
            }
        }
    }

    // Debugging function to check buildings and colliders
    private void DebugBuildings()
    {
        var buildings = FindObjectsOfType<CityBuilderCore.Building>();
        Debug.Log($"Found {buildings.Length} buildings in scene");
        
        foreach (var building in buildings)
        {
            var colliders = building.GetComponentsInChildren<Collider2D>();
            Debug.Log($"Building '{building.name}' has {colliders.Length} colliders");
        }
    }


    private void AdjustPanelToStayOnScreen(RectTransform panelRect)
    {
        // Get panel dimensions
        Vector2 panelSize = panelRect.rect.size;
        Vector2 screenSize = new Vector2(Screen.width, Screen.height);
        
        // Current position
        Vector3 position = panelRect.position;
        
        // Adjust horizontally if needed
        if (position.x + panelSize.x/2 > screenSize.x)
            position.x = screenSize.x - panelSize.x/2;
        else if (position.x - panelSize.x/2 < 0)
            position.x = panelSize.x/2;
            
        // Adjust vertically if needed
        if (position.y + panelSize.y/2 > screenSize.y)
            position.y = screenSize.y - panelSize.y/2;
        else if (position.y - panelSize.y/2 < 0)
            position.y = panelSize.y/2;
        
        // Apply adjusted position
        panelRect.position = position;
    }
    void OnDrawGizmos()
    {
        // Draw a small sphere at each building position
        var buildings = FindObjectsOfType<CityBuilderCore.Building>();
        foreach (var building in buildings)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(building.transform.position, 0.5f);
            
            // Draw collider bounds
            var colliders = building.GetComponentsInChildren<Collider2D>();
            Gizmos.color = Color.green;
            foreach (var collider in colliders)
            {
                Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
            }
        }
    }
    private void ScalePanelBasedOnZoom()
    {
        if (mainCamera.orthographic)
        {
            // Calculate scale factor based on orthographic size
            float currentOrthoSize = mainCamera.orthographicSize;
            float zoomRatio = baseOrthographicSize / currentOrthoSize;
            
            // Apply scale - when zoomed in (smaller ortho size), scale will be larger
            Vector3 newScale = originalPanelScale * zoomRatio * basePanelScale;
            buildingInfoPanel.transform.localScale = newScale;
            
            // Optional: Limit the min/max scale to prevent too big/small
            float minScale = 0.5f;
            float maxScale = 2.0f;
            if (newScale.x < minScale)
                buildingInfoPanel.transform.localScale = new Vector3(minScale, minScale, minScale);
            else if (newScale.x > maxScale)
                buildingInfoPanel.transform.localScale = new Vector3(maxScale, maxScale, maxScale);
        }
    }
    private void DebugLog(string message)
    {
        if (Hover_Text_Debug)
            Debug.Log(message);
    }
}