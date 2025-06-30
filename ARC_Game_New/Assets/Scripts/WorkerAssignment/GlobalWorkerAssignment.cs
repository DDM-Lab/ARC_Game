using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class GlobalWorkerManagementUI : MonoBehaviour
{
    [Header("Main UI Panel")]
    public GameObject mainPanel;
    
    [Header("Left Side - Worker Stats Panel")]
    public GameObject leftStatsPanel;
    
    [Header("Trained Worker Stats")]
    public TextMeshProUGUI trainedWorkingText;
    public TextMeshProUGUI trainedFreeText;
    public TextMeshProUGUI trainedNotArrivedText;
    public TextMeshProUGUI trainedTotalText;
    
    [Header("Untrained Worker Stats")]
    public TextMeshProUGUI untrainedWorkingText;
    public TextMeshProUGUI untrainedFreeText;
    public TextMeshProUGUI untrainedTrainingText;
    public TextMeshProUGUI untrainedTotalText;
    
    [Header("Summary Stats")]
    public TextMeshProUGUI totalWorkersText;
    public TextMeshProUGUI availableWorkforceText;
    public TextMeshProUGUI totalWorkforceText;
    
    [Header("Right Side - Building Management Panel")]
    public GameObject rightBuildingPanel;
    
    [Header("Tab Buttons")]
    public Button shelterTabButton;
    public Button kitchenTabButton;
    public Button caseworkTabButton;
    public Button disasterAssessmentTabButton;
    public Button trainCenterTabButton;
    
    [Header("Tab Visual States")]
    public Color activeTabColor = Color.white;
    public Color inactiveTabColor = Color.gray;
    
    [Header("Scroll View")]
    public ScrollRect buildingScrollView;
    public Transform scrollViewContent;
    
    [Header("Building List Item Prefab")]
    public GameObject buildingListItemPrefab;
    
    [Header("Close Button")]
    public Button closeButton;
    
    [Header("System References")]
    public WorkerSystem workerSystem;
    public BuildingSystem buildingSystem;
    
    [Header("Individual Building Manage UI")]
    public IndividualBuildingManageUI individualManageUI;
    
    // Private variables
    private BuildingType currentSelectedTab = BuildingType.Shelter;
    private bool isUIOpen = false;
    private List<GameObject> currentBuildingItems = new List<GameObject>();
    
    void Awake()
    {
        // Find system references if not assigned
        if (workerSystem == null)
            workerSystem = FindObjectOfType<WorkerSystem>();
        if (buildingSystem == null)
            buildingSystem = FindObjectOfType<BuildingSystem>();
        
        // Setup button listeners
        SetupButtonListeners();
        
        // Subscribe to worker system events
        if (workerSystem != null)
            workerSystem.OnWorkerStatsChanged += OnWorkerStatsChanged;
    }

    void Start()
    {
        // Only ensure proper initial state if the panel is already active
        // Don't force hide if something else activated it
        if (mainPanel != null && mainPanel.activeInHierarchy && !isUIOpen)
        {
            // Panel is active but our state says it shouldn't be - fix the state
            isUIOpen = true;
            UpdateWorkerStatsDisplay();
            UpdateBuildingList();
        }
        
        // Initialize UI state
        SetActiveTab(BuildingType.Shelter);
        
        Debug.Log("GlobalWorkerManagementUI Start() called, isUIOpen: " + isUIOpen);
    }
    
    void SetupButtonListeners()
    {
        if (shelterTabButton != null)
            shelterTabButton.onClick.AddListener(() => OnTabClicked(BuildingType.Shelter));
            
        if (kitchenTabButton != null)
            kitchenTabButton.onClick.AddListener(() => OnTabClicked(BuildingType.Kitchen));
            
        if (caseworkTabButton != null)
            caseworkTabButton.onClick.AddListener(() => OnTabClicked(BuildingType.CaseworkSite));
            
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClicked);
    }
    
    public void ShowUI()
    {
        isUIOpen = true;
        
        if (mainPanel != null)
            mainPanel.SetActive(true);
        
        // Immediate update when opening
        UpdateWorkerStatsDisplay();
        UpdateBuildingList();
        
        Debug.Log("Global Worker Management UI opened");
    }
    
    public void HideUI()
    {
        isUIOpen = false;
        
        if (mainPanel != null)
            mainPanel.SetActive(false);
        
        // Also close individual manage UI if open
        if (individualManageUI != null)
            individualManageUI.HideManageUI();
        
        Debug.Log("Global Worker Management UI closed");
    }
    
    void OnTabClicked(BuildingType tabType)
    {
        SetActiveTab(tabType);
        UpdateBuildingList();
        
        Debug.Log($"Switched to {tabType} tab");
    }
    
    void SetActiveTab(BuildingType tabType)
    {
        currentSelectedTab = tabType;
        
        // Update tab button visuals
        UpdateTabButtonVisuals();
    }
    
    void UpdateTabButtonVisuals()
    {
        // Reset all tabs to inactive color
        SetTabButtonColor(shelterTabButton, inactiveTabColor);
        SetTabButtonColor(kitchenTabButton, inactiveTabColor);
        SetTabButtonColor(caseworkTabButton, inactiveTabColor);
        SetTabButtonColor(disasterAssessmentTabButton, inactiveTabColor);
        SetTabButtonColor(trainCenterTabButton, inactiveTabColor);

        // Set active tab color
        switch (currentSelectedTab)
        {
            case BuildingType.Shelter:
                SetTabButtonColor(shelterTabButton, activeTabColor);
                break;
            case BuildingType.Kitchen:
                SetTabButtonColor(kitchenTabButton, activeTabColor);
                break;
            case BuildingType.CaseworkSite:
                SetTabButtonColor(caseworkTabButton, activeTabColor);
                break;
        }
    }
    
    void SetTabButtonColor(Button button, Color color)
    {
        if (button != null)
        {
            Image buttonImage = button.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = color;
        }
    }
    
    void UpdateWorkerStatsDisplay()
    {
        if (workerSystem == null) return;
        
        WorkerStatistics stats = workerSystem.GetWorkerStatistics();
        
        // Update trained worker stats
        UpdateTextSafe(trainedWorkingText, stats.trainedWorking.ToString());
        UpdateTextSafe(trainedFreeText, stats.trainedFree.ToString());
        UpdateTextSafe(trainedNotArrivedText, stats.trainedNotArrived.ToString());
        UpdateTextSafe(trainedTotalText, stats.GetTotalTrained().ToString());
        
        // Update untrained worker stats
        UpdateTextSafe(untrainedWorkingText, stats.untrainedWorking.ToString());
        UpdateTextSafe(untrainedFreeText, stats.untrainedFree.ToString());
        UpdateTextSafe(untrainedTrainingText, stats.untrainedTraining.ToString());
        UpdateTextSafe(untrainedTotalText, stats.GetTotalUntrained().ToString());
        
        // Update summary stats
        UpdateTextSafe(totalWorkersText, stats.GetTotalWorkers().ToString());
        UpdateTextSafe(availableWorkforceText, stats.GetAvailableWorkforce().ToString());
        UpdateTextSafe(totalWorkforceText, workerSystem.GetTotalWorkforce().ToString());
        
        // Apply color coding
        ApplyWorkerStatsColors(stats);
    }
    
    void ApplyWorkerStatsColors(WorkerStatistics stats)
    {
        // Color free workers green if available
        Color trainedFreeColor = stats.trainedFree > 0 ? Color.green : Color.gray;
        Color untrainedFreeColor = stats.untrainedFree > 0 ? Color.green : Color.gray;
        
        // Color working workers blue
        Color workingColor = Color.cyan;
        Color notWorkingColor = Color.gray;
        
        UpdateTextWithColor(trainedFreeText, stats.trainedFree.ToString(), trainedFreeColor);
        UpdateTextWithColor(untrainedFreeText, stats.untrainedFree.ToString(), untrainedFreeColor);
        UpdateTextWithColor(trainedWorkingText, stats.trainedWorking.ToString(), 
                           stats.trainedWorking > 0 ? workingColor : notWorkingColor);
        UpdateTextWithColor(untrainedWorkingText, stats.untrainedWorking.ToString(), 
                           stats.untrainedWorking > 0 ? workingColor : notWorkingColor);
        
        // Color special statuses
        UpdateTextWithColor(trainedNotArrivedText, stats.trainedNotArrived.ToString(), 
                           stats.trainedNotArrived > 0 ? Color.yellow : Color.gray);
        UpdateTextWithColor(untrainedTrainingText, stats.untrainedTraining.ToString(), 
                           stats.untrainedTraining > 0 ? Color.magenta : Color.gray);
        
        // Color available workforce based on total
        Color availableColor = stats.GetAvailableWorkforce() > 0 ? Color.green : Color.red;
        UpdateTextWithColor(availableWorkforceText, stats.GetAvailableWorkforce().ToString(), availableColor);
    }
    
    void UpdateBuildingList()
    {
        if (buildingSystem == null || scrollViewContent == null) return;
        
        // Clear existing items
        ClearBuildingList();
        
        // Get buildings of current selected type
        List<Building> buildings = GetBuildingsOfType(currentSelectedTab);
        
        // Create items for each building
        foreach (Building building in buildings)
        {
            CreateBuildingListItem(building);
        }
        
        Debug.Log($"Updated building list for {currentSelectedTab}: {buildings.Count} buildings");
    }
    
    List<Building> GetBuildingsOfType(BuildingType buildingType)
    {
        if (buildingSystem == null) return new List<Building>();
        
        List<Building> allBuildings = buildingSystem.GetAllBuildings();
        return allBuildings.Where(b => b.GetBuildingType() == buildingType).ToList();
    }
    
    void CreateBuildingListItem(Building building)
    {
        if (buildingListItemPrefab == null) return;
        
        GameObject item = Instantiate(buildingListItemPrefab, scrollViewContent);
        currentBuildingItems.Add(item);
        
        // Configure the building list item
        BuildingListItem listItem = item.GetComponent<BuildingListItem>();
        if (listItem != null)
        {
            listItem.Initialize(building, this);
        }
        else
        {
            Debug.LogWarning("BuildingListItem component not found on prefab!");
        }
    }
    
    void ClearBuildingList()
    {
        foreach (GameObject item in currentBuildingItems)
        {
            if (item != null)
                Destroy(item);
        }
        currentBuildingItems.Clear();
    }
    
    public void OnManageButtonClicked(Building building)
    {
        if (individualManageUI != null)
        {
            individualManageUI.ShowManageUI(building);
            Debug.Log($"Opening individual manage UI for {building.GetBuildingType()} at site {building.GetOriginalSiteId()}");
        }
        else
        {
            Debug.LogWarning("IndividualBuildingManageUI reference not set!");
        }
    }
    
    void OnCloseButtonClicked()
    {
        HideUI();
    }
    
    void OnWorkerStatsChanged()
    {
        // Real-time update when worker stats change
        if (isUIOpen)
        {
            UpdateWorkerStatsDisplay();
            // Also refresh building list as workforce assignments might have changed
            UpdateBuildingList();
        }
    }
    
    void UpdateTextSafe(TextMeshProUGUI textComponent, string value)
    {
        if (textComponent != null)
        {
            textComponent.text = value;
        }
    }
    
    void UpdateTextWithColor(TextMeshProUGUI textComponent, string value, Color color)
    {
        if (textComponent != null)
        {
            textComponent.text = value;
            textComponent.color = color;
        }
    }
    
    // Handle ESC key to close UI
    void LateUpdate()
    {
        if (isUIOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            OnCloseButtonClicked();
        }
    }
    
    void OnDestroy()
    {
        // Clean up event subscriptions
        if (workerSystem != null)
            workerSystem.OnWorkerStatsChanged -= OnWorkerStatsChanged;
    }
    
    // Public methods for external access
    public bool IsUIOpen()
    {
        return isUIOpen;
    }
    
    public BuildingType GetCurrentSelectedTab()
    {
        return currentSelectedTab;
    }
    
    // Method to be called by the global worker button on the map
    public void ToggleUI()
    {
        if (isUIOpen)
            HideUI();
        else
            ShowUI();
    }
}