// Task Item UI Component
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Task Item UI Component - Fixed version
public class TaskItemUI : MonoBehaviour
{
    [Header("Main Layout Components")]
    public Image agentIcon;
    public GameObject statLayout; // The vertical layout group
    public Button viewButton;
    [Header("Status Indicators")]
    public Image statusBackground; // Background for status color
    public Image incompleteIcon; // Icon for incomplete tasks
    
    [Header("Stat Layout Children - Auto-found")]
    [SerializeField] private TextMeshProUGUI taskDescriptionText;
    [SerializeField] private GameObject taskTypeLabelContainer; // Container for task type label
    [SerializeField] private TextMeshProUGUI taskTypeLabelText; // The actual text component
    [SerializeField] private TextMeshProUGUI additionalInfoText; // Additional info like "5 people"

    [Header("Agent Avatar Sprites")]
    public Sprite defaultOfficerSprite;
    public Sprite workforceServiceSprite;
    public Sprite lodgingMassCareSprite;
    public Sprite externalRelationshipSprite;
    public Sprite foodMassCareSprite;

    [Header("Status Colors")]
    public Color activeColor = Color.green;
    public Color inProgressColor = Color.yellow;
    public Color incompleteColor = Color.red;
    public Color expiredColor = Color.gray;
    public Color completedColor = Color.blue;
    
    [Header("Type Colors")]
    public Color emergencyColor = Color.red;
    public Color demandColor = Color.magenta;
    public Color advisoryColor = Color.blue;
    public Color alertColor = Color.black;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    private GameTask assignedTask;
    private TaskCenterUI parentUI;
    
    void Awake()
    {
        // Auto-find components in the stat layout
        FindStatLayoutComponents();
    }
    
    void FindStatLayoutComponents()
    {
        if (statLayout == null)
        {
            Debug.LogError("StatLayout is not assigned!");
            return;
        }
        
        // Find components by searching children
        Transform[] children = statLayout.GetComponentsInChildren<Transform>();
        
        foreach (Transform child in children)
        {
            // Find task description text (usually the first TextMeshProUGUI)
            if (taskDescriptionText == null && child.GetComponent<TextMeshProUGUI>() != null)
            {
                taskDescriptionText = child.GetComponent<TextMeshProUGUI>();
                if (showDebugInfo)
                    Debug.Log($"Found task description text: {child.name}");
                continue;
            }
            
            // Find task type label container and text
            if (child.name.ToLower().Contains("type") || child.name.ToLower().Contains("label"))
            {
                taskTypeLabelContainer = child.gameObject;
                taskTypeLabelText = child.GetComponent<TextMeshProUGUI>();
                
                // If this object doesn't have TextMeshProUGUI, look for it in children
                if (taskTypeLabelText == null)
                    taskTypeLabelText = child.GetComponentInChildren<TextMeshProUGUI>();
                
                if (showDebugInfo)
                    Debug.Log($"Found task type label: {child.name}");
                continue;
            }
        }
        
        // Find additional info text (usually the last TextMeshProUGUI that's not the description or type)
        TextMeshProUGUI[] allTexts = statLayout.GetComponentsInChildren<TextMeshProUGUI>();
        if (allTexts.Length > 2) // If we have more than 2 text components
        {
            additionalInfoText = allTexts[allTexts.Length - 1]; // Take the last one
            if (showDebugInfo)
                Debug.Log($"Found additional info text: {additionalInfoText.name}");
        }
        
        // If auto-finding failed, try by index
        if (taskDescriptionText == null || taskTypeLabelText == null)
        {
            TextMeshProUGUI[] texts = statLayout.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length >= 2)
            {
                if (taskDescriptionText == null) taskDescriptionText = texts[0];
                if (taskTypeLabelText == null) taskTypeLabelText = texts[1];
                if (texts.Length >= 3 && additionalInfoText == null) additionalInfoText = texts[2];
                
                if (showDebugInfo)
                    Debug.Log($"Auto-assigned components by index. Found {texts.Length} text components.");
            }
        }
    }
    
    public void Initialize(GameTask task, TaskCenterUI parent)
    {
        assignedTask = task;
        parentUI = parent;
        
        UpdateDisplay();
        
        // Setup view button
        if (viewButton != null)
        {
            viewButton.onClick.RemoveAllListeners();
            viewButton.onClick.AddListener(OnViewButtonClicked);
            
            if (showDebugInfo)
                Debug.Log($"View button setup for task: {task.taskTitle}");
        }
        else
        {
            Debug.LogError("View button is not assigned!");
        }
    }
    
    void UpdateDisplay()
    {
        if (assignedTask == null) return;
        
        // Update task description
        if (taskDescriptionText != null)
        {
            taskDescriptionText.text = assignedTask.taskTitle;
        }
        else
        {
            Debug.LogWarning("Task description text component not found!");
        }
        
        // Update task type label
        if (taskTypeLabelText != null)
        {
            taskTypeLabelText.text = assignedTask.taskType.ToString().ToUpper();
            UpdateTypeColor();
        }
        else
        {
            Debug.LogWarning("Task type label text component not found!");
        }

        // Update additional info
        if (additionalInfoText != null)
        {
            string additionalInfo = GenerateAdditionalInfo();
            additionalInfoText.text = additionalInfo;
            Debug.Log($"Generated additional info: {additionalInfo}");
        }
        
        // Update agent icon if available
        if (agentIcon != null && assignedTask != null)
        {
            agentIcon.sprite = GetOfficerAvatar(assignedTask.taskOfficer);
        }
        
        // Update status colors
        UpdateStatusIndicators();
        
        if (showDebugInfo)
            Debug.Log($"Updated display for task: {assignedTask.taskTitle} ({assignedTask.taskType})");
    }

    Sprite GetOfficerAvatar(TaskOfficer officer)
    {
        switch (officer)
        {
            case TaskOfficer.DisasterOfficer: return defaultOfficerSprite;
            case TaskOfficer.WorkforceService: return workforceServiceSprite;
            case TaskOfficer.LodgingMassCare: return lodgingMassCareSprite;
            case TaskOfficer.ExternalRelationship: return externalRelationshipSprite;
            case TaskOfficer.FoodMassCare: return foodMassCareSprite;
            default: return defaultOfficerSprite;
        }
    }

    
    string GenerateAdditionalInfo()
    {
        // Generate additional info based on task impacts
        string info = "";
        
        // Add affected facility
        if (!string.IsNullOrEmpty(assignedTask.affectedFacility))
        {
            info += assignedTask.affectedFacility;
        }
        
        // Add impact information
        if (assignedTask.impacts.Count > 0)
        {
            foreach (var impact in assignedTask.impacts)
            {
                if (impact.impactType == ImpactType.Clients && impact.value > 0)
                {
                    if (!string.IsNullOrEmpty(info)) info += " • ";
                    info += $"{impact.value} people";
                    break; // Only show the first client impact
                }
            }
        }
        
        // Add status info
        string status = GetTaskStatusText();
        if (!string.IsNullOrEmpty(status))
        {
            if (!string.IsNullOrEmpty(info)) info += " • ";
            info += status;
        }
        
        return info;
    }
    string GetTaskStatusText()
    {
        if (assignedTask.isExpired && assignedTask.status == TaskStatus.Active)
        {
            // just expired, not been processed by system yet
            return "EXPIRED";
        }
        
        switch (assignedTask.status)
        {
            case TaskStatus.Active:
                return "Active";
            case TaskStatus.InProgress:
                return "IN PROGRESS";
            case TaskStatus.Incomplete:
                return "INCOMPLETE";
            case TaskStatus.Expired:
                return "EXPIRED";
            case TaskStatus.Completed:
                return "COMPLETED";
            default:
                return "";
        }
    }
    
    void UpdateStatusIndicators()
    {
        Color statusColor = activeColor;
        bool showIncompleteIcon = false;
        
        switch (assignedTask.status)
        {
            case TaskStatus.Active:
                statusColor = activeColor;
                break;
            case TaskStatus.InProgress:
                statusColor = inProgressColor;
                break;
            case TaskStatus.Incomplete:
                statusColor = incompleteColor;
                showIncompleteIcon = true;
                break;
            case TaskStatus.Expired:
                statusColor = expiredColor;
                break;
            case TaskStatus.Completed:
                statusColor = completedColor;
                break;
        }
        
        // Apply status color to background
        if (statusBackground != null)
        {
            Color bgColor = statusColor;
            statusBackground.color = bgColor;
        }
        
        // Show/Hide Incomplete Icon
        if (incompleteIcon != null)
        {
            incompleteIcon.gameObject.SetActive(showIncompleteIcon);
        }
    }
    
    void UpdateTypeColor()
    {
        Color typeColor = advisoryColor;
        
        switch (assignedTask.taskType)
        {
            case TaskType.Emergency:
                typeColor = emergencyColor;
                break;
            case TaskType.Demand:
                typeColor = demandColor;
                break;
            case TaskType.Advisory:
                typeColor = advisoryColor;
                break;
            case TaskType.Alert:
                typeColor = alertColor;
                break;
        }
        
        // Apply type color to the task type label
        if (taskTypeLabelText != null)
        {
            taskTypeLabelText.color = typeColor;
        }
        
        // Also apply to the container background if it has an Image component
        if (taskTypeLabelContainer != null)
        {
            Image background = taskTypeLabelContainer.GetComponent<Image>();
            if (background != null)
            {
                Color bgColor = typeColor;
                bgColor.a = 0.3f; // Make it semi-transparent
                background.color = bgColor;
            }
        }
    }
    
    void OnViewButtonClicked()
    {
        if (showDebugInfo)
            Debug.Log($"View button clicked for task: {assignedTask?.taskTitle}");
        
        if (parentUI != null && assignedTask != null)
        {
            AudioManager.Instance.PlayClickSFX();
            parentUI.OnTaskItemClicked(assignedTask);
        }
        else
        {
            Debug.LogError("Cannot open task detail - missing parentUI or assignedTask");
        }
    }
    
    void Update()
    {
        // Update display periodically
        if (assignedTask != null)
        {
            UpdateDisplay();
        }
    }
    
    // Debug method to print component status
    [ContextMenu("Debug Component Status")]
    void DebugComponentStatus()
    {
        Debug.Log("=== TASK ITEM UI COMPONENT STATUS ===");
        Debug.Log($"Agent Icon: {(agentIcon != null ? "Found" : "Missing")}");
        Debug.Log($"Stat Layout: {(statLayout != null ? "Found" : "Missing")}");
        Debug.Log($"View Button: {(viewButton != null ? "Found" : "Missing")}");
        Debug.Log($"Task Description Text: {(taskDescriptionText != null ? "Found" : "Missing")}");
        Debug.Log($"Task Type Label Text: {(taskTypeLabelText != null ? "Found" : "Missing")}");
        Debug.Log($"Additional Info Text: {(additionalInfoText != null ? "Found" : "Missing")}");
        Debug.Log($"Assigned Task: {(assignedTask != null ? assignedTask.taskTitle : "None")}");
    }
}