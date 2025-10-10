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

    [Header("Demo Only - Immediate Request Worker")]
    public bool enableImmediateRequest = false;
    public Button immediateRequestTrainedButton;
    public Button immediateRequestUntrainedButton;

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
    
    [Header("Training Group Item Prefab")]
    public GameObject trainingGroupItemPrefab;

    [Header("Close Button")]
    public Button closeButton;

    [Header("System References")]
    public WorkerSystem workerSystem;
    public BuildingSystem buildingSystem;
    public WorkerTrainingSystem workerTrainingSystem;

    [Header("Individual Building Manage UI")]
    public IndividualBuildingManageUI individualManageUI;

    [Header("Colors")]
    public Color positiveColor = new Color(0.2f, 0.6f, 0.2f);
    public Color negativeColor = new Color(0.6f, 0.2f, 0.2f);
    public Color neutralColor = new Color(0.6f, 0.4f, 0.2f);

    // Private variables
    private enum TabType
    {
        Shelter,
        Kitchen,
        Casework,
        DisasterAssessment,
        TrainCenter
    }
    
    private TabType currentSelectedTab = TabType.Shelter;
    private bool isUIOpen = false;
    private List<GameObject> currentListItems = new List<GameObject>();

    void Awake()
    {
        if (workerSystem == null)
            workerSystem = FindObjectOfType<WorkerSystem>();
        if (buildingSystem == null)
            buildingSystem = FindObjectOfType<BuildingSystem>();
        if (workerTrainingSystem == null)
            workerTrainingSystem = FindObjectOfType<WorkerTrainingSystem>();

        SetupButtonListeners();

        if (workerSystem != null)
            workerSystem.OnWorkerStatsChanged += OnWorkerStatsChanged;
    }

    void Start()
    {
        if (mainPanel != null && mainPanel.activeInHierarchy && !isUIOpen)
        {
            isUIOpen = true;
            UpdateWorkerStatsDisplay();
            UpdateRightPanel();
        }

        SetActiveTab(TabType.Shelter);
        Debug.Log("GlobalWorkerManagementUI Start() called, isUIOpen: " + isUIOpen);
    }

    void SetupButtonListeners()
    {
        if (shelterTabButton != null)
            shelterTabButton.onClick.AddListener(() => OnTabClicked(TabType.Shelter));

        if (kitchenTabButton != null)
            kitchenTabButton.onClick.AddListener(() => OnTabClicked(TabType.Kitchen));

        if (caseworkTabButton != null)
            caseworkTabButton.onClick.AddListener(() => OnTabClicked(TabType.Casework));
            
        if (disasterAssessmentTabButton != null)
            disasterAssessmentTabButton.onClick.AddListener(() => OnTabClicked(TabType.DisasterAssessment));
            
        if (trainCenterTabButton != null)
            trainCenterTabButton.onClick.AddListener(() => OnTabClicked(TabType.TrainCenter));

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClicked);

        if (immediateRequestTrainedButton != null && enableImmediateRequest)
            immediateRequestTrainedButton.onClick.AddListener(OnImmediateRequestTrainedButtonClicked);

        if (immediateRequestUntrainedButton != null && enableImmediateRequest)
            immediateRequestUntrainedButton.onClick.AddListener(OnImmediateRequestUntrainedButtonClicked);
    }

    public void ShowUI()
    {
        isUIOpen = true;

        if (mainPanel != null)
            mainPanel.SetActive(true);

        UpdateWorkerStatsDisplay();
        UpdateRightPanel();

        Debug.Log("Global Worker Management UI opened");
    }

    public void HideUI()
    {
        isUIOpen = false;

        if (mainPanel != null)
            mainPanel.SetActive(false);

        if (individualManageUI != null)
            individualManageUI.HideManageUI();

        Debug.Log("Global Worker Management UI closed");
    }

    void OnTabClicked(TabType tabType)
    {
        SetActiveTab(tabType);
        UpdateRightPanel();
        Debug.Log($"Switched to {tabType} tab");
    }

    void SetActiveTab(TabType tabType)
    {
        currentSelectedTab = tabType;
        UpdateTabButtonVisuals();
    }

    void UpdateTabButtonVisuals()
    {
        SetTabButtonColor(shelterTabButton, currentSelectedTab == TabType.Shelter ? activeTabColor : inactiveTabColor);
        SetTabButtonColor(kitchenTabButton, currentSelectedTab == TabType.Kitchen ? activeTabColor : inactiveTabColor);
        SetTabButtonColor(caseworkTabButton, currentSelectedTab == TabType.Casework ? activeTabColor : inactiveTabColor);
        SetTabButtonColor(disasterAssessmentTabButton, currentSelectedTab == TabType.DisasterAssessment ? activeTabColor : inactiveTabColor);
        SetTabButtonColor(trainCenterTabButton, currentSelectedTab == TabType.TrainCenter ? activeTabColor : inactiveTabColor);
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

        UpdateTextSafe(trainedWorkingText, stats.trainedWorking.ToString());
        UpdateTextSafe(trainedFreeText, stats.trainedFree.ToString());
        UpdateTextSafe(trainedNotArrivedText, stats.trainedNotArrived.ToString());
        UpdateTextSafe(trainedTotalText, stats.GetTotalTrained().ToString());

        UpdateTextSafe(untrainedWorkingText, stats.untrainedWorking.ToString());
        UpdateTextSafe(untrainedFreeText, stats.untrainedFree.ToString());
        UpdateTextSafe(untrainedTrainingText, stats.untrainedTraining.ToString());
        UpdateTextSafe(untrainedTotalText, stats.GetTotalUntrained().ToString());

        UpdateTextSafe(totalWorkersText, stats.GetTotalWorkers().ToString());
        UpdateTextSafe(availableWorkforceText, stats.GetAvailableWorkforce().ToString());
        UpdateTextSafe(totalWorkforceText, workerSystem.GetTotalWorkforce().ToString());

        ApplyWorkerStatsColors(stats);
    }

    void ApplyWorkerStatsColors(WorkerStatistics stats)
    {
        Color trainedFreeColor = stats.trainedFree > 0 ? positiveColor : neutralColor;
        Color untrainedFreeColor = stats.untrainedFree > 0 ? positiveColor : neutralColor;
        Color workingColor = neutralColor;
        Color notWorkingColor = neutralColor;

        UpdateTextWithColor(trainedFreeText, stats.trainedFree.ToString(), trainedFreeColor);
        UpdateTextWithColor(untrainedFreeText, stats.untrainedFree.ToString(), untrainedFreeColor);
        UpdateTextWithColor(trainedWorkingText, stats.trainedWorking.ToString(),
                           stats.trainedWorking > 0 ? workingColor : notWorkingColor);
        UpdateTextWithColor(untrainedWorkingText, stats.untrainedWorking.ToString(),
                           stats.untrainedWorking > 0 ? workingColor : notWorkingColor);

        UpdateTextWithColor(trainedNotArrivedText, stats.trainedNotArrived.ToString(),
                           stats.trainedNotArrived > 0 ? Color.yellow : Color.gray);
        UpdateTextWithColor(untrainedTrainingText, stats.untrainedTraining.ToString(),
                           stats.untrainedTraining > 0 ? Color.magenta : Color.gray);

        Color availableColor = stats.GetAvailableWorkforce() > 0 ? positiveColor : negativeColor;
        UpdateTextWithColor(availableWorkforceText, stats.GetAvailableWorkforce().ToString(), availableColor);
    }

    void UpdateRightPanel()
    {
        ClearList();

        switch (currentSelectedTab)
        {
            case TabType.Shelter:
                UpdateBuildingList(BuildingType.Shelter);
                break;
            case TabType.Kitchen:
                UpdateBuildingList(BuildingType.Kitchen);
                break;
            case TabType.Casework:
                UpdateBuildingList(BuildingType.CaseworkSite);
                break;
            case TabType.DisasterAssessment:
                // TODO: Implement disaster assessment panel
                Debug.Log("Disaster Assessment tab - not yet implemented");
                break;
            case TabType.TrainCenter:
                UpdateTrainingGroupList();
                break;
        }
    }

    void UpdateBuildingList(BuildingType buildingType)
    {
        if (buildingSystem == null || scrollViewContent == null) return;

        List<Building> buildings = GetBuildingsOfType(buildingType);

        foreach (Building building in buildings)
        {
            CreateBuildingListItem(building);
        }

        Debug.Log($"Updated building list for {buildingType}: {buildings.Count} buildings");
    }

    void UpdateTrainingGroupList()
    {
        if (workerTrainingSystem == null || scrollViewContent == null)
        {
            Debug.LogWarning("WorkerTrainingSystem or scroll content not found!");
            return;
        }

        List<WorkerTrainingSystem.TrainingTask> activeTrainings = workerTrainingSystem.GetActiveTrainingTasks();

        if (activeTrainings.Count == 0)
        {
            // Display nothing - just clear the list
            return;
        }

        // Sort: In-progress first, then completed, then by completion day
        activeTrainings = activeTrainings
            .OrderBy(t => t.isCompleted ? 1 : 0)  // In-progress (false=0) before completed (true=1)
            .ThenBy(t => t.completionDay)         // Earlier completion first
            .ToList();

        foreach (var training in activeTrainings)
        {
            CreateTrainingGroupItem(training);
        }

        Debug.Log($"Updated training group list: {activeTrainings.Count} groups");
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
        currentListItems.Add(item);

        BuildingListItem listItem = item.GetComponent<BuildingListItem>();
        if (listItem != null)
        {
            listItem.Initialize(building, this);
        }
    }

    void CreateTrainingGroupItem(WorkerTrainingSystem.TrainingTask training)
    {
        if (trainingGroupItemPrefab == null)
        {
            Debug.LogWarning("Training group item prefab not assigned!");
            return;
        }

        GameObject item = Instantiate(trainingGroupItemPrefab, scrollViewContent);
        currentListItems.Add(item);

        TrainingGroupItemUI groupItem = item.GetComponent<TrainingGroupItemUI>();
        if (groupItem != null)
        {
            groupItem.Initialize(training);
        }
    }

    void ClearList()
    {
        foreach (GameObject item in currentListItems)
        {
            if (item != null)
                Destroy(item);
        }
        currentListItems.Clear();
    }

    public void OnManageButtonClicked(Building building)
    {
        if (individualManageUI != null)
        {
            individualManageUI.ShowManageUI(building);
            Debug.Log($"Opening individual manage UI for {building.GetBuildingType()} at site {building.GetOriginalSiteId()}");
        }
    }

    void OnCloseButtonClicked()
    {
        HideUI();
    }

    public void OnWorkerStatsChanged()
    {
        if (isUIOpen)
        {
            UpdateWorkerStatsDisplay();
            UpdateRightPanel();
        }
    }

    void UpdateTextSafe(TextMeshProUGUI textComponent, string value)
    {
        if (textComponent != null)
            textComponent.text = value;
    }

    void UpdateTextWithColor(TextMeshProUGUI textComponent, string value, Color color)
    {
        if (textComponent != null)
        {
            textComponent.text = value;
            textComponent.color = color;
        }
    }

    void LateUpdate()
    {
        if (isUIOpen && Input.GetKeyDown(KeyCode.Escape))
            OnCloseButtonClicked();
    }

    void OnDestroy()
    {
        if (workerSystem != null)
            workerSystem.OnWorkerStatsChanged -= OnWorkerStatsChanged;
    }

    public bool IsUIOpen()
    {
        return isUIOpen;
    }

    public void ToggleUI()
    {
        if (isUIOpen)
            HideUI();
        else
            ShowUI();
    }

    void OnImmediateRequestTrainedButtonClicked()
    {
        if (workerSystem != null && enableImmediateRequest)
        {
            workerSystem.CreateTrainedWorker();
            Debug.Log("Immediate request for trained workers sent.");
        }
    }

    void OnImmediateRequestUntrainedButtonClicked()
    {
        if (workerSystem != null && enableImmediateRequest)
        {
            workerSystem.CreateUntrainedWorker();
            Debug.Log("Immediate request for untrained workers sent.");
        }
    }
}