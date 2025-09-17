using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class IndividualBuildingManageUI : MonoBehaviour
{
    [Header("Main Panel")]
    public GameObject managePanel;

    [Header("Building Info")]
    public TextMeshProUGUI buildingTitleText;
    public TextMeshProUGUI buildingStatusText;
    public TextMeshProUGUI requiredWorkforceText;

    [Header("Current Workforce Display")]
    public TextMeshProUGUI currentWorkforceText;
    public TextMeshProUGUI trainedWorkersText;
    public TextMeshProUGUI untrainedWorkersText;

    [Header("Trained Worker Controls")]
    public Button addTrainedButton;
    public Button removeTrainedButton;
    public TextMeshProUGUI trainedCountText;

    [Header("Untrained Worker Controls")]
    public Button addUntrainedButton;
    public Button removeUntrainedButton;
    public TextMeshProUGUI untrainedCountText;

    [Header("Action Buttons")]
    public Button applyChangesButton;
    public Button cancelButton;
    public Button closeButton;

    [Header("Feedback")]
    public TextMeshProUGUI feedbackText;
    public Color successColor = Color.green;
    public Color errorColor = Color.red;
    public Color warningColor = Color.yellow;

    [Header("Interaction")]
    public bool enableClick = true;
    public Building parentBuilding; // Reference to the building this indicator belongs to


    [Header("System References")]
    public WorkerSystem workerSystem;
    public GlobalWorkerManagementUI globalWorkerUI;

    private Building currentBuilding;
    private bool isUIOpen = false;

    // Temporary assignment tracking (before applying changes)
    private int tempTrainedWorkers = 0;
    private int tempUntrainedWorkers = 0;
    private int originalTrainedWorkers = 0;
    private int originalUntrainedWorkers = 0;

    void Start()
    {
        // Find worker system if not assigned
        if (workerSystem == null)
            workerSystem = FindObjectOfType<WorkerSystem>();

        // Setup button listeners
        SetupButtonListeners();

        // Don't call HideManageUI() here - let the initial state be handled by the Inspector
        // The panel should be set to inactive in the Inspector by default
        // Otherwise, Start() will be called when ShowManageUI() is called, and therefore hide the UI
    }

    void SetupButtonListeners()
    {
        if (addTrainedButton != null)
            addTrainedButton.onClick.AddListener(() => ModifyWorkerCount(WorkerType.Trained, 1));

        if (removeTrainedButton != null)
            removeTrainedButton.onClick.AddListener(() => ModifyWorkerCount(WorkerType.Trained, -1));

        if (addUntrainedButton != null)
            addUntrainedButton.onClick.AddListener(() => ModifyWorkerCount(WorkerType.Untrained, 1));

        if (removeUntrainedButton != null)
            removeUntrainedButton.onClick.AddListener(() => ModifyWorkerCount(WorkerType.Untrained, -1));

        if (applyChangesButton != null)
            applyChangesButton.onClick.AddListener(OnApplyChangesClicked);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelClicked);

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);
    }

    public void ShowManageUI(Building building)
    {

        if (GlobalClock.Instance != null && !GlobalClock.Instance.CanPlayerInteract())
        {
            Debug.Log("Cannot open manage UI - simulation is running");
            return;
        }
        currentBuilding = building;
        isUIOpen = true;

        if (managePanel != null)
            managePanel.SetActive(true);

        // Initialize UI with current building data
        InitializeUI();

        Debug.Log($"Individual manage UI opened for {building.GetBuildingType()} at site {building.GetOriginalSiteId()}");
    }

    public void HideManageUI()
    {
        isUIOpen = false;
        currentBuilding = null;

        if (managePanel != null)
            managePanel.SetActive(false);

        ClearFeedback();

        Debug.Log("Individual manage UI closed");
    }

    void InitializeUI()
    {
        if (currentBuilding == null || workerSystem == null) return;

        // Update building info
        UpdateBuildingInfo();

        // Get current worker assignments
        List<Worker> currentWorkers = workerSystem.GetWorkersByBuildingId(currentBuilding.GetOriginalSiteId());
        originalTrainedWorkers = currentWorkers.Count(w => w.Type == WorkerType.Trained);
        originalUntrainedWorkers = currentWorkers.Count(w => w.Type == WorkerType.Untrained);

        // Initialize temporary values
        tempTrainedWorkers = originalTrainedWorkers;
        tempUntrainedWorkers = originalUntrainedWorkers;

        // Update all displays
        UpdateCurrentWorkforceDisplay();
        UpdateWorkerControls();
        UpdateButtonStates();
        ClearFeedback();
    }

    void UpdateBuildingInfo()
    {
        if (currentBuilding == null) return;

        // Building title
        string buildingName = $"{currentBuilding.GetBuildingType()} {GetBuildingNumber()}";
        UpdateTextSafe(buildingTitleText, buildingName);

        // Building status
        string statusText = $"Site {currentBuilding.GetOriginalSiteId()} - {currentBuilding.GetCurrentStatus()}";
        UpdateTextSafe(buildingStatusText, statusText);

        // Required workforce
        UpdateTextSafe(requiredWorkforceText, $"Required Workforce: {currentBuilding.GetRequiredWorkforce()}");
    }

    int GetBuildingNumber()
    {
        // Get all buildings of the same type and find this building's position
        var buildingsOfSameType = FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == currentBuilding.GetBuildingType())
            .OrderBy(b => b.GetOriginalSiteId())
            .ToList();

        int index = buildingsOfSameType.FindIndex(b => b == currentBuilding);
        return index + 1; // 1-based numbering
    }

    void UpdateCurrentWorkforceDisplay()
    {
        if (currentBuilding == null) return;

        int currentWorkforce = currentBuilding.GetAssignedWorkforce();
        int requiredWorkforce = currentBuilding.GetRequiredWorkforce();

        // Current workforce with color coding
        Color workforceColor = currentWorkforce >= requiredWorkforce ? Color.green : Color.red;
        UpdateTextWithColor(currentWorkforceText, $"Current: {currentWorkforce}/{requiredWorkforce}", workforceColor);

        // Individual worker counts
        /*List<Worker> currentWorkers = workerSystem.GetWorkersByBuildingId(currentBuilding.GetOriginalSiteId());
        int trainedCount = currentWorkers.Count(w => w.Type == WorkerType.Trained);
        int untrainedCount = currentWorkers.Count(w => w.Type == WorkerType.Untrained);*/

        WorkerStatistics stats = workerSystem.GetWorkerStatistics();
        int trainedCount = stats.trainedFree;
        int untrainedCount = stats.untrainedFree;

        UpdateTextSafe(trainedWorkersText, $"{trainedCount}");
        UpdateTextSafe(untrainedWorkersText, $"{untrainedCount}");
    }

    void UpdateWorkerControls()
    {
        // Update the temporary worker count displays
        UpdateTextSafe(trainedCountText, tempTrainedWorkers.ToString());
        UpdateTextSafe(untrainedCountText, tempUntrainedWorkers.ToString());

        // Calculate projected workforce
        int projectedWorkforce = (tempTrainedWorkers * 2) + tempUntrainedWorkers;
        bool isValidAssignment = projectedWorkforce >= currentBuilding.GetRequiredWorkforce();

        // Show projected workforce in feedback
        if (HasChanges())
        {
            string projectedText = $"Projected Workforce: {projectedWorkforce}/{currentBuilding.GetRequiredWorkforce()}";
            Color feedbackColor = isValidAssignment ? successColor : warningColor;
            UpdateTextWithColor(feedbackText, projectedText, feedbackColor);
        }
    }

    void UpdateButtonStates()
    {
        if (workerSystem == null) return;

        WorkerStatistics stats = workerSystem.GetWorkerStatistics();

        // Add buttons - enable if we have available workers
        if (addTrainedButton != null)
            addTrainedButton.interactable = stats.trainedFree > 0;

        if (addUntrainedButton != null)
            addUntrainedButton.interactable = stats.untrainedFree > 0;

        // Remove buttons - enable if we have workers assigned to this building
        if (removeTrainedButton != null)
            removeTrainedButton.interactable = tempTrainedWorkers > 0;

        if (removeUntrainedButton != null)
            removeUntrainedButton.interactable = tempUntrainedWorkers > 0;

        // Apply button - enable if there are changes and assignment is valid
        if (applyChangesButton != null)
        {
            bool hasChanges = HasChanges();
            int projectedWorkforce = (tempTrainedWorkers * 2) + tempUntrainedWorkers;
            bool isValidWorkforce = projectedWorkforce >= currentBuilding.GetRequiredWorkforce() ||
                                   currentBuilding.GetCurrentStatus() == BuildingStatus.Disabled;

            applyChangesButton.interactable = hasChanges && isValidWorkforce;
        }
    }

    void ModifyWorkerCount(WorkerType workerType, int change)
    {
        if (workerType == WorkerType.Trained)
        {
            tempTrainedWorkers = Mathf.Max(0, tempTrainedWorkers + change);
        }
        else
        {
            tempUntrainedWorkers = Mathf.Max(0, tempUntrainedWorkers + change);
        }

        UpdateWorkerControls();
        UpdateButtonStates();

        Debug.Log($"Modified {workerType} worker count by {change}. New temporary counts: Trained={tempTrainedWorkers}, Untrained={tempUntrainedWorkers}");
        GameLogPanel.Instance.LogPlayerAction($"Modified {workerType} worker count by {change} for building {currentBuilding.GetBuildingType()} at site {currentBuilding.GetOriginalSiteId()}");
    }

    bool HasChanges()
    {
        return tempTrainedWorkers != originalTrainedWorkers || tempUntrainedWorkers != originalUntrainedWorkers;
    }

    void OnApplyChangesClicked()
    {
        if (currentBuilding == null || workerSystem == null)
        {
            ShowFeedback("Error: Missing building or worker system reference", errorColor);
            return;
        }

        if (!HasChanges())
        {
            ShowFeedback("No changes to apply", warningColor);
            return;
        }

        // Validate workforce requirements
        int projectedWorkforce = (tempTrainedWorkers * 2) + tempUntrainedWorkers;
        bool meetsRequirement = projectedWorkforce >= currentBuilding.GetRequiredWorkforce();

        if (!meetsRequirement && currentBuilding.GetCurrentStatus() != BuildingStatus.Disabled)
        {
            ShowFeedback($"Insufficient workforce! Need {currentBuilding.GetRequiredWorkforce()}, projected: {projectedWorkforce}", errorColor);
            return;
        }

        // Apply the changes
        bool success = ApplyWorkerChanges();

        if (success)
        {
            ShowFeedback("Worker assignment updated successfully!", successColor);

            // Update original values to reflect new state
            originalTrainedWorkers = tempTrainedWorkers;
            originalUntrainedWorkers = tempUntrainedWorkers;

            // Update building status if necessary
            if (meetsRequirement && currentBuilding.NeedsWorker())
            {
                currentBuilding.AssignWorker();
            }
            else if (!meetsRequirement && currentBuilding.IsOperational())
            {
                currentBuilding.DisableBuilding();
            }

            // Refresh displays
            currentBuilding.UpdateWorkforceDisplay();
            UpdateCurrentWorkforceDisplay();
            UpdateButtonStates();
        }
        else
        {
            ShowFeedback("Failed to update worker assignment", errorColor);
        }
    }

    bool ApplyWorkerChanges()
    {
        try
        {
            // First, release all current workers from this building
            workerSystem.ReleaseWorkersFromBuilding(currentBuilding.GetOriginalSiteId());

            // Then assign the new workers
            List<Worker> availableWorkers = workerSystem.GetAvailableWorkers();
            List<Worker> workersToAssign = new List<Worker>();

            // Select trained workers first
            var availableTrained = availableWorkers.Where(w => w.Type == WorkerType.Trained).Take(tempTrainedWorkers);
            workersToAssign.AddRange(availableTrained);

            // Then select untrained workers
            var availableUntrained = availableWorkers.Where(w => w.Type == WorkerType.Untrained).Take(tempUntrainedWorkers);
            workersToAssign.AddRange(availableUntrained);

            // Verify we have enough workers
            int actualTrained = workersToAssign.Count(w => w.Type == WorkerType.Trained);
            int actualUntrained = workersToAssign.Count(w => w.Type == WorkerType.Untrained);

            if (actualTrained < tempTrainedWorkers || actualUntrained < tempUntrainedWorkers)
            {
                ShowFeedback("Not enough available workers of the requested types", errorColor);
                return false;
            }

            // Assign workers to the building
            foreach (Worker worker in workersToAssign)
            {
                worker.TryAssignToBuilding(currentBuilding.GetOriginalSiteId());
            }
            GameLogPanel.Instance.LogPlayerAction($"Assigned {actualTrained} trained and {actualUntrained} untrained workers to building {currentBuilding.GetBuildingType()} at site {currentBuilding.GetOriginalSiteId()}");
            Debug.Log($"Successfully assigned {actualTrained} trained and {actualUntrained} untrained workers to building {currentBuilding.GetOriginalSiteId()}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error applying worker changes: {e.Message}");
            return false;
        }
    }

    void OnCancelClicked()
    {
        // Reset temporary values to original
        tempTrainedWorkers = originalTrainedWorkers;
        tempUntrainedWorkers = originalUntrainedWorkers;

        UpdateWorkerControls();
        UpdateButtonStates();
        ClearFeedback();

        Debug.Log("Worker assignment changes cancelled");
    }

    void OnCloseClicked()
    {
        HideManageUI();
    }

    void ShowFeedback(string message, Color color)
    {
        UpdateTextWithColor(feedbackText, message, color);

        // Auto-clear feedback after a few seconds
        CancelInvoke(nameof(ClearFeedback));
        Invoke(nameof(ClearFeedback), 3f);
    }

    void ClearFeedback()
    {
        UpdateTextSafe(feedbackText, "");
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

    // Handle ESC key to close
    void Update()
    {
        if (isUIOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            OnCloseClicked();
        }

        // Update button states periodically to reflect worker availability changes
        if (isUIOpen && Time.frameCount % 30 == 0) // Every 30 frames (~0.5 seconds at 60fps)
        {
            UpdateButtonStates();
        }
    }

    // Public methods for external access
    public bool IsUIOpen()
    {
        return isUIOpen;
    }

    public Building GetCurrentBuilding()
    {
        return currentBuilding;
    }

}