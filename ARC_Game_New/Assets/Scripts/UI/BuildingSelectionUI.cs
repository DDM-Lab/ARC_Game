using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BuildingSelectionUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject selectionPanel;
    public TextMeshProUGUI selectSitePromptText;
    public Button kitchenButton;
    public Button shelterButton;
    public Button caseworkButton;
    public Button cancelButton;
    
    [Header("Confirmation Panel")]
    public GameObject confirmationPanel;
    public TextMeshProUGUI buildingNameText;
    public TextMeshProUGUI buildingDescriptionText;
    public Button buildButton;
    public Button backButton;
    
    [Header("Building System")]
    public BuildingSystem buildingSystem;
    
    [Header("Stats UI")]
    public BuildingStatsUI buildingStatsUI;

    [Header("System References")]
    private bool isUIOpen = false;
    
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

        selectSitePromptText.gameObject.SetActive(false);
        
        Debug.Log("BuildingSelectionUI initialized");
    }
    
    public void ShowSelectionUI(Vector3 worldPosition)
    {
        currentSitePosition = worldPosition;
        lastActivationTime = Time.time;
        isUIOpen = true;
        
        // Only show panels if there's a selected site from BuildingSystem
        BuildingSystem buildingSystem = FindObjectOfType<BuildingSystem>();
        bool hasSiteSelected = buildingSystem != null && buildingSystem.HasSelectedSite();
        
        if (hasSiteSelected)
        {
            selectSitePromptText.gameObject.SetActive(false);

            // Show selection panel
            if (selectionPanel != null)
                selectionPanel.SetActive(true);
                
            if (confirmationPanel != null)
                confirmationPanel.SetActive(false);
            
            if (buildingStatsUI != null)
                buildingStatsUI.ShowStatsPanel();
        }
        else
        {
            selectSitePromptText.gameObject.SetActive(true);
            // Don't show other panels if no site selected
            if (buildingStatsUI != null)
                buildingStatsUI.ShowStatsPanel();
        }
    }

    public void HideAllPanels()
    {
        isUIOpen = false;

        if (selectionPanel != null)
            selectionPanel.SetActive(false);

        if (confirmationPanel != null)
            confirmationPanel.SetActive(false);

        if (buildingStatsUI != null)
            buildingStatsUI.HideStatsPanel();
            
        selectSitePromptText.gameObject.SetActive(false);
    }
    public void ToggleUI(Vector3 worldPosition)
    {
        if (isUIOpen)
        {
            OnCancelSelected();
        }
        else
        {
            ShowSelectionUI(worldPosition);
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
                buildingDescription = "Provides food for clients.\nCapacity: 10 food packs\n" + "<color=#00FF00>Time Needed: 1 Round</color>\n" +
                                        $"<color=#00FF00>Construction Cost: ${buildingSystem.kitchenConstructionCost}</color>";
                break;
            case BuildingType.Shelter:
                buildingName = "Shelter";
                buildingDescription = "Provides housing for clients.\nCapacity: 10 people\n" + "<color=#00FF00>Time Needed: 1 Round</color>\n" +
                                        $"<color=#00FF00>Construction Cost: ${buildingSystem.shelterConstructionCost}</color>";
                break;
            case BuildingType.CaseworkSite:
                buildingName = "Casework Site";
                buildingDescription = "Handles administrative tasks.\nCapacity: 40 cases\n" + "<color=#00FF00>Time Needed: 1 Round</color>\n" +
                                        $"<color=#00FF00>Construction Cost: ${buildingSystem.caseworkSiteConstructionCost}</color>";
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

    public bool IsUIOpen()
    {
        return isUIOpen;
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