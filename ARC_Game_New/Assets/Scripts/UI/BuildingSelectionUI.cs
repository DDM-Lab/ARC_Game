using UnityEngine;
using UnityEngine.UI;

public class BuildingSelectionUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject selectionPanel;
    public Button kitchenButton;
    public Button shelterButton;
    public Button caseworkButton;
    public Button cancelButton;
    
    [Header("Building System")]
    public BuildingSystem buildingSystem;
    
    [Header("UI Positioning")]
    public Canvas uiCanvas;
    public float offsetFromSite = 100f; // Pixel offset from the site
    
    private Camera mainCamera;
    private Vector3 currentSitePosition;
    private float lastActivationTime;
    
    void Start()
    {
        mainCamera = Camera.main;
        
        // Setup button listeners
        if (kitchenButton != null)
            kitchenButton.onClick.AddListener(() => OnBuildingSelected(BuildingType.Kitchen));
        
        if (shelterButton != null)
            shelterButton.onClick.AddListener(() => OnBuildingSelected(BuildingType.Shelter));
        
        if (caseworkButton != null)
            caseworkButton.onClick.AddListener(() => OnBuildingSelected(BuildingType.CaseworkSite));
        
        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelSelected);
        
        // Hide panel initially
        HideSelectionUI();
        
        Debug.Log("BuildingSelectionUI initialized");
    }
    
    public void ShowSelectionUI(Vector3 worldPosition)
    {
        currentSitePosition = worldPosition;
        lastActivationTime = Time.time; // Record activation time
        
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(true);
            Debug.Log("Building selection UI panel activated");
        }
        else
        {
            Debug.LogError("Selection panel is null!");
        }
    }
    
    public void HideSelectionUI()
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(false);
            Debug.Log("Building selection UI panel deactivated");
        }
    }
    
    void PositionUIPanel(Vector3 worldPosition)
    {
        if (uiCanvas == null || selectionPanel == null || mainCamera == null)
            return;
        
        // Convert world position to screen position
        Vector3 screenPosition = mainCamera.WorldToScreenPoint(worldPosition);
        
        // Add offset so UI doesn't overlap with the site
        screenPosition.y += offsetFromSite;
        
        // Convert screen position to canvas position
        Vector2 canvasPosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            uiCanvas.transform as RectTransform,
            screenPosition,
            uiCanvas.worldCamera,
            out canvasPosition
        );
        
        // Set the panel position
        RectTransform panelRect = selectionPanel.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            panelRect.localPosition = canvasPosition;
            
            // Ensure panel stays within canvas bounds
            ClampToCanvasBounds(panelRect);
        }
    }
    
    void ClampToCanvasBounds(RectTransform panelRect)
    {
        Vector3 localPos = panelRect.localPosition;
        Vector2 canvasSize = (uiCanvas.transform as RectTransform).sizeDelta;
        Vector2 panelSize = panelRect.sizeDelta;
        
        // Clamp X position
        float maxX = (canvasSize.x - panelSize.x) * 0.5f;
        float minX = -(canvasSize.x - panelSize.x) * 0.5f;
        localPos.x = Mathf.Clamp(localPos.x, minX, maxX);
        
        // Clamp Y position
        float maxY = (canvasSize.y - panelSize.y) * 0.5f;
        float minY = -(canvasSize.y - panelSize.y) * 0.5f;
        localPos.y = Mathf.Clamp(localPos.y, minY, maxY);
        
        panelRect.localPosition = localPos;
    }
    
    void OnBuildingSelected(BuildingType buildingType)
    {
        Debug.Log($"Player selected building type: {buildingType}");
        
        // Notify building system
        if (buildingSystem != null)
        {
            buildingSystem.OnBuildingTypeSelected(buildingType);
        }
        
        // Hide UI
        HideSelectionUI();
    }
    
    void OnCancelSelected()
    {
        Debug.Log("Player cancelled building selection");
        
        // Notify building system
        if (buildingSystem != null)
        {
            buildingSystem.CancelBuildingSelection();
        }
        
        // Hide UI
        HideSelectionUI();
    }
    
    // Handle clicking outside the panel to cancel
    void Update()
    {
        if (selectionPanel != null && selectionPanel.activeInHierarchy)
        {
            if (Input.GetMouseButtonDown(0))
            {
                // Wait one frame after UI becomes active to avoid immediate cancellation
                if (Time.time - lastActivationTime < 0.1f)
                    return;
                    
                // Check if click is outside the panel
                Vector2 mousePosition = Input.mousePosition;
                RectTransform panelRect = selectionPanel.GetComponent<RectTransform>();
                
                if (!RectTransformUtility.RectangleContainsScreenPoint(panelRect, mousePosition, uiCanvas.worldCamera))
                {
                    OnCancelSelected();
                }
            }
        }
    }
}