using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class BuildingListItem : MonoBehaviour
{
    [Header("UI Components")]
    public TextMeshProUGUI buildingNameText;
    public TextMeshProUGUI workerDescriptionText;
    public TextMeshProUGUI workforceNumberText;
    public Button manageButton;
    
    [Header("Visual States")]
    public Color normalBackgroundColor = Color.white;
    public Color operationalBackgroundColor = Color.green;
    public Color needsWorkerBackgroundColor = Color.yellow;
    public Color disabledBackgroundColor = Color.red;
    public Color constructionBackgroundColor = Color.blue;
    
    [Header("Workforce Display Colors")]
    public Color sufficientWorkforceColor = Color.green;
    public Color insufficientWorkforceColor = Color.red;
    public Color noWorkforceColor = Color.gray;

    [Header("Workforce Indicator")]
    public WorkforceIndicator workforceIndicator;

    private Building assignedBuilding;
    private GlobalWorkerManagementUI globalWorkerManagementUI;
    private Image backgroundImage;
    private WorkerSystem workerSystem;
    
    void Start()
    {
        // Get background image component
        backgroundImage = GetComponent<Image>();
        
        // Setup manage button listener
        if (manageButton != null)
        {
            manageButton.onClick.AddListener(OnManageButtonClicked);
        }
        
        // Find worker system
        workerSystem = FindObjectOfType<WorkerSystem>();
    }
    
    public void Initialize(Building building, GlobalWorkerManagementUI globalWorkerManagementUI)
    {
        this.assignedBuilding = building;
        this.globalWorkerManagementUI = globalWorkerManagementUI;
        
        // Update display immediately
        UpdateDisplay();
        
        // Set building name with numbering
        UpdateBuildingName();
    }
    
    void Update()
    {
        // Update display periodically to reflect real-time changes
        if (assignedBuilding != null)
        {
            UpdateDisplay();
        }
    }
    
    void UpdateBuildingName()
    {
        if (assignedBuilding == null) return;
        
        // Generate building name with numbering
        string buildingTypeName = GetBuildingTypeName();
        int buildingNumber = GetBuildingNumber();
        
        string displayName = $"{buildingTypeName} {buildingNumber}";
        UpdateTextSafe(buildingNameText, displayName);
    }
    
    string GetBuildingTypeName()
    {
        switch (assignedBuilding.GetBuildingType())
        {
            case BuildingType.Kitchen:
                return "Kitchen";
            case BuildingType.Shelter:
                return "Shelter";
            case BuildingType.CaseworkSite:
                return "Casework Site";
            default:
                return "Building";
        }
    }
    
    int GetBuildingNumber()
    {
        if (globalWorkerManagementUI == null) return 1;
        
        // Get all buildings of the same type and find this building's position
        var buildingsOfSameType = FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == assignedBuilding.GetBuildingType())
            .OrderBy(b => b.GetOriginalSiteId())
            .ToList();
        
        int index = buildingsOfSameType.FindIndex(b => b == assignedBuilding);
        return index + 1; // 1-based numbering
    }

    void UpdateDisplay()
    {
        if (assignedBuilding == null) return;

        // Update worker description
        UpdateWorkerDescription();

        // Update workforce number
        UpdateWorkforceNumber();

        // Update background color based on building status
        UpdateBackgroundColor();

        // Update manage button state
        UpdateManageButtonState();
        
        // Update workforce indicator
        if (workforceIndicator != null && workerSystem != null)
        {
            workforceIndicator.UpdateFromBuilding(assignedBuilding, workerSystem);
        }
    }
    
    void UpdateWorkerDescription()
    {
        if (workerSystem == null || assignedBuilding == null) return;
        
        List<Worker> assignedWorkers = workerSystem.GetWorkersByBuildingId(assignedBuilding.GetOriginalSiteId());
        
        if (assignedWorkers.Count == 0)
        {
            UpdateTextSafe(workerDescriptionText, "No workers assigned");
            return;
        }
        
        // Count trained and untrained workers
        int trainedCount = assignedWorkers.Count(w => w.Type == WorkerType.Trained);
        int untrainedCount = assignedWorkers.Count(w => w.Type == WorkerType.Untrained);
        
        string description = GenerateWorkerDescription(trainedCount, untrainedCount);
        UpdateTextSafe(workerDescriptionText, description);
    }
    
    string GenerateWorkerDescription(int trainedCount, int untrainedCount)
    {
        List<string> parts = new List<string>();
        
        if (trainedCount > 0)
        {
            string trainedText = trainedCount == 1 ? "one trained worker" : $"{trainedCount} trained workers";
            parts.Add(trainedText);
        }
        
        if (untrainedCount > 0)
        {
            string untrainedText = untrainedCount == 1 ? "one untrained volunteer" : $"{untrainedCount} untrained volunteers";
            parts.Add(untrainedText);
        }
        
        if (parts.Count == 0)
        {
            return "No workers assigned";
        }
        else if (parts.Count == 1)
        {
            return parts[0];
        }
        else
        {
            return string.Join(" and ", parts);
        }
    }
    
    void UpdateWorkforceNumber()
    {
        if (assignedBuilding == null) return;
        
        int currentWorkforce = assignedBuilding.GetAssignedWorkforce();
        int requiredWorkforce = assignedBuilding.GetRequiredWorkforce();
        
        string workforceText = $"{currentWorkforce}/{requiredWorkforce}";
        
        // Determine color based on workforce sufficiency
        Color workforceColor;
        if (currentWorkforce == 0)
        {
            workforceColor = noWorkforceColor;
        }
        else if (currentWorkforce >= requiredWorkforce)
        {
            workforceColor = sufficientWorkforceColor;
        }
        else
        {
            workforceColor = insufficientWorkforceColor;
        }
        
        UpdateTextWithColor(workforceNumberText, workforceText, workforceColor);
    }
    
    void UpdateBackgroundColor()
    {
        if (backgroundImage == null || assignedBuilding == null) return;
        
        Color backgroundColor;
        
        switch (assignedBuilding.GetCurrentStatus())
        {
            case BuildingStatus.UnderConstruction:
                backgroundColor = constructionBackgroundColor;
                break;
            case BuildingStatus.NeedWorker:
                backgroundColor = needsWorkerBackgroundColor;
                break;
            case BuildingStatus.InUse:
                backgroundColor = operationalBackgroundColor;
                break;
            case BuildingStatus.Disabled:
                backgroundColor = disabledBackgroundColor;
                break;
            default:
                backgroundColor = normalBackgroundColor;
                break;
        }
        
        // Apply transparency to make it subtle
        backgroundColor.a = 0.3f;
        backgroundImage.color = backgroundColor;
    }
    
    void UpdateManageButtonState()
    {
        if (manageButton == null || assignedBuilding == null) return;
        
        // Enable manage button for buildings that are not under construction
        bool shouldEnable = assignedBuilding.GetCurrentStatus() != BuildingStatus.UnderConstruction;
        manageButton.interactable = shouldEnable;
        
        // Update button text based on building status
        TextMeshProUGUI buttonText = manageButton.GetComponentInChildren<TextMeshProUGUI>();
        if (buttonText != null)
        {
            switch (assignedBuilding.GetCurrentStatus())
            {
                case BuildingStatus.UnderConstruction:
                    buttonText.text = "Building...";
                    break;
                case BuildingStatus.NeedWorker:
                    buttonText.text = "Assign";
                    break;
                case BuildingStatus.InUse:
                    buttonText.text = "Manage";
                    break;
                case BuildingStatus.Disabled:
                    buttonText.text = "Repair";
                    break;
                default:
                    buttonText.text = "Manage";
                    break;
            }
        }
    }
    
    void OnManageButtonClicked()
    {
        if (GlobalClock.Instance != null && GlobalClock.Instance.IsSimulationRunning())
        {
            Debug.Log("Cannot manage during simulation");
            return;
        }
        if (assignedBuilding != null && globalWorkerManagementUI != null)
        {
            AudioManager.Instance.PlayClickSFX();
            globalWorkerManagementUI.OnManageButtonClicked(assignedBuilding);
        }
        else
        {
            Debug.LogWarning("Cannot open manage UI - missing building or parent UI reference");
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
    
    // Public method to get the assigned building (for external access)
    public Building GetAssignedBuilding()
    {
        return assignedBuilding;
    }
    
    // Method to manually refresh display (can be called externally)
    public void RefreshDisplay()
    {
        UpdateDisplay();
    }
}