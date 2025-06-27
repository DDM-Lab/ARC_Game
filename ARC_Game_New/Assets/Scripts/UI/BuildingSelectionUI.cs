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
    
    [Header("Confirmation Panel")]
    public GameObject confirmationPanel;
    public TMPro.TextMeshProUGUI buildingNameText;
    public TMPro.TextMeshProUGUI buildingDescriptionText;
    public Button buildButton;
    public Button backButton;
    
    [Header("Building System")]
    public BuildingSystem buildingSystem;
    
    [Header("Stats UI")]
    public BuildingStatsUI buildingStatsUI;
    
    [Header("UI Positioning")]
    public Canvas uiCanvas;
    public float offsetFromSite = 100f; // Pixel offset from the site
    
    private Camera mainCamera;
    private Vector3 currentSitePosition;
    private float lastActivationTime;
    private BuildingType selectedBuildingType;
    
    void Start()
    {
        mainCamera = Camera.main;
        
        // Setup selection button listeners
        if (kitchenButton != null)
            kitchenButton.onClick.AddListener(() => OnBuildingTypeSelected(BuildingType.Kitchen));
        
        if (shelterButton != null)
            shelterButton.onClick.AddListener(() => OnBuildingTypeSelected(BuildingType.Shelter));
        
        if (caseworkButton != null)
            caseworkButton.onClick.AddListener(() => OnBuildingTypeSelected(BuildingType.CaseworkSite));
        
        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelSelected);
        
        // Setup confirmation button listeners
        if (buildButton != null)
            buildButton.onClick.AddListener(OnConfirmBuild);
            
        if (backButton != null)
            backButton.onClick.AddListener(OnBackToSelection);
        
        // Hide panels initially
        HideAllPanels();
        
        Debug.Log("BuildingSelectionUI initialized");
    }
    
    public void ShowSelectionUI(Vector3 worldPosition)
    {
        currentSitePosition = worldPosition;
        lastActivationTime = Time.time;
        
        // Show selection panel, hide confirmation panel
        if (selectionPanel != null)
            selectionPanel.SetActive(true);
            
        if (confirmationPanel != null)
            confirmationPanel.SetActive(false);
        
        // Show stats panel
        if (buildingStatsUI != null)
            buildingStatsUI.ShowStatsPanel();
        
        Debug.Log("Building selection UI panel activated");
    }
    
    public void HideAllPanels()
    {
        if (selectionPanel != null)
            selectionPanel.SetActive(false);
            
        if (confirmationPanel != null)
            confirmationPanel.SetActive(false);
        
        // Hide stats panel
        if (buildingStatsUI != null)
            buildingStatsUI.HideStatsPanel();
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
    
    void OnBuildingTypeSelected(BuildingType buildingType)
    {
        selectedBuildingType = buildingType;
        // if confirmation panel is already active, update info only
        if (confirmationPanel != null && confirmationPanel.activeInHierarchy)
        {
            UpdateBuildingInfo(buildingType);
        }else
        {
            ShowConfirmationPanel(buildingType);
        }
    }
    
    void ShowConfirmationPanel(BuildingType buildingType)
    {
        // Hide selection panel, show confirmation panel
        //if (selectionPanel != null)
        //    selectionPanel.SetActive(false);
            
        if (confirmationPanel != null)
            confirmationPanel.SetActive(true);
        
        // Update building info
        UpdateBuildingInfo(buildingType);
        
        Debug.Log($"Showing confirmation panel for: {buildingType}");
    }
    
    void UpdateBuildingInfo(BuildingType buildingType)
    {
        string buildingName = "";
        string buildingDescription = "";
        
        switch (buildingType)
        {
            case BuildingType.Kitchen:
                buildingName = "Kitchen";
                buildingDescription = "Provides food for survivors.\nCapacity: 20 people\nFunction: Food production and distribution";
                break;
            case BuildingType.Shelter:
                buildingName = "Shelter";
                buildingDescription = "Provides housing for survivors.\nCapacity: 15 people\nFunction: Safe accommodation and rest";
                break;
            case BuildingType.CaseworkSite:
                buildingName = "Casework Site";
                buildingDescription = "Handles administrative tasks.\nCapacity: 8 cases\nFunction: Case management and support services";
                break;
        }
        
        if (buildingNameText != null)
            buildingNameText.text = buildingName;
            
        if (buildingDescriptionText != null)
            buildingDescriptionText.text = buildingDescription;
    }
    
    void OnConfirmBuild()
    {
        Debug.Log($"Player confirmed building: {selectedBuildingType}");
        
        // Notify building system to actually build
        if (buildingSystem != null)
        {
            buildingSystem.OnBuildingTypeSelected(selectedBuildingType);
        }
        
        // Force update stats after building is created
        if (buildingStatsUI != null)
        {
            // Wait a frame then update stats to ensure building is created
            StartCoroutine(UpdateStatsAfterDelay());
        }
        
        // Hide all panels
        HideAllPanels();
    }
    
    System.Collections.IEnumerator UpdateStatsAfterDelay()
    {
        yield return new WaitForEndOfFrame();
        if (buildingStatsUI != null)
        {
            buildingStatsUI.ForceUpdateStats();
        }
    }

    void OnBackToSelection()
    {
        if (confirmationPanel != null)
        {
            confirmationPanel.SetActive(false);
        }
        // Go back to selection panel
            //ShowSelectionUI(currentSitePosition);
        }
    
    void OnCancelSelected()
    {
        Debug.Log("Player cancelled building selection");
        
        // Notify building system
        if (buildingSystem != null)
        {
            buildingSystem.CancelBuildingSelection();
        }
        
        // Hide all panels
        HideAllPanels();
    }
    
    // Handle clicking outside the panel to cancel
    void Update()
    {
        bool anyPanelActive = (selectionPanel != null && selectionPanel.activeInHierarchy) || 
                             (confirmationPanel != null && confirmationPanel.activeInHierarchy);
        
        if (anyPanelActive)
        {
            if (Input.GetMouseButtonDown(0))
            {
                // Wait one frame after UI becomes active to avoid immediate cancellation
                if (Time.time - lastActivationTime < 0.1f)
                    return;
                    
                // Check if click is outside any active panel
                Vector2 mousePosition = Input.mousePosition;
                bool clickedOutside = true;
                
                if (selectionPanel != null && selectionPanel.activeInHierarchy)
                {
                    RectTransform selectionRect = selectionPanel.GetComponent<RectTransform>();
                    if (RectTransformUtility.RectangleContainsScreenPoint(selectionRect, mousePosition, uiCanvas.worldCamera))
                        clickedOutside = false;
                }
                
                if (confirmationPanel != null && confirmationPanel.activeInHierarchy)
                {
                    RectTransform confirmationRect = confirmationPanel.GetComponent<RectTransform>();
                    if (RectTransformUtility.RectangleContainsScreenPoint(confirmationRect, mousePosition, uiCanvas.worldCamera))
                        clickedOutside = false;
                }
                
                if (clickedOutside)
                {
                    OnCancelSelected();
                }
            }
        }
    }
}