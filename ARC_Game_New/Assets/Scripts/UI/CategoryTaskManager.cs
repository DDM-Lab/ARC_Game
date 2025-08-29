using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public enum TaskCategory
{
    Community,
    Kitchen,
    Shelter,
    Emergency
}

public class CategoryTaskManager : MonoBehaviour
{
    [Header("Category Buttons")]
    public Button communityButton;
    public Button kitchenButton;
    public Button shelterButton;
    public Button emergencyButton;
    
    [Header("Message Bubbles")]
    public GameObject communityBubble;
    public GameObject kitchenBubble;
    public GameObject shelterBubble;
    public GameObject emergencyBubble;
    public TextMeshProUGUI communityCountText;
    public TextMeshProUGUI kitchenCountText;
    public TextMeshProUGUI shelterCountText;
    public TextMeshProUGUI emergencyCountText;
    
    [Header("Button Colors")]
    public Color activeColor = Color.white;
    public Color disabledColor = Color.gray;

    [Header("Color Settings")]
    public Color brown = new Color(0.6f, 0.3f, 0.1f);
    public Color darkRed = new Color(0.6f, 0.1f, 0.1f);
    public Color darkOrange = new Color(0.6f, 0.4f, 0.1f);

    [Header("Task Panel")]
    public GameObject taskCategoryPanel;
    public TextMeshProUGUI categoryTitleText;
    public ScrollRect taskScrollView;
    public Transform taskListParent;
    public GameObject taskListItemPrefab;
    public Button closePanelButton;
    public float panelXOffset;

    private TaskCategory currentCategory;
    private List<GameObject> currentTaskItems = new List<GameObject>();
    private bool isPanelOpen = false;

    // Singleton
    public static CategoryTaskManager Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        SetupButtonListeners();
        
        if (taskCategoryPanel != null)
            taskCategoryPanel.SetActive(false);
            
        // Update button states initially
        UpdateButtonStates();
    }
    
    void Update()
    {
        // Update button states every frame to reflect current task status
        UpdateButtonStates();
        
        // Handle ESC key to close panel
        if (isPanelOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseCategoryPanel();
        }
    }
    
    void SetupButtonListeners()
    {
        if (communityButton != null)
            communityButton.onClick.AddListener(() => OnCategoryButtonClicked(TaskCategory.Community));
            
        if (kitchenButton != null)
            kitchenButton.onClick.AddListener(() => OnCategoryButtonClicked(TaskCategory.Kitchen));
            
        if (shelterButton != null)
            shelterButton.onClick.AddListener(() => OnCategoryButtonClicked(TaskCategory.Shelter));
            
        if (emergencyButton != null)
            emergencyButton.onClick.AddListener(() => OnCategoryButtonClicked(TaskCategory.Emergency));
            
        if (closePanelButton != null)
            closePanelButton.onClick.AddListener(CloseCategoryPanel);
    }
    
    void UpdateButtonStates()
    {
        if (TaskSystem.Instance == null) return;
        
        UpdateButtonState(communityButton, TaskCategory.Community, communityBubble, communityCountText);
        UpdateButtonState(kitchenButton, TaskCategory.Kitchen, kitchenBubble, kitchenCountText);
        UpdateButtonState(shelterButton, TaskCategory.Shelter, shelterBubble, shelterCountText);
        UpdateButtonState(emergencyButton, TaskCategory.Emergency, emergencyBubble, emergencyCountText);
    }
    
    void UpdateButtonState(Button button, TaskCategory category, GameObject bubble, TextMeshProUGUI countText)
    {
        if (button == null) return;
        
        List<GameTask> categoryTasks = GetTasksByCategory(category);
        int taskCount = categoryTasks.Count;
        bool hasTasks = taskCount > 0;
        
        // Update button interactability
        button.interactable = hasTasks;
        
        // Update button color
        Image buttonImage = button.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.color = hasTasks ? activeColor : disabledColor;
        }
        
        // Update message bubble
        if (bubble != null)
        {
            bubble.SetActive(hasTasks);
        }
        
        // Update count text
        if (countText != null && hasTasks)
        {
            countText.text = taskCount.ToString();
        }
    }
    
    void OnCategoryButtonClicked(TaskCategory category)
    {
        currentCategory = category;
        ShowCategoryPanel();
    }
    
    void ShowCategoryPanel()
    {
        if (taskCategoryPanel == null) return;
        
        // Position panel at button's X position
        PositionPanelAtButton();
        
        // Update panel title
        if (categoryTitleText != null)
        {
            categoryTitleText.text = GetCategoryDisplayName(currentCategory);
        }
        
        // Clear existing task items
        ClearTaskList();
        
        // Get and display tasks for this category
        List<GameTask> categoryTasks = GetTasksByCategory(currentCategory);
        
        foreach (GameTask task in categoryTasks)
        {
            CreateTaskListItem(task);
        }
        
        // Show panel
        taskCategoryPanel.SetActive(true);
        isPanelOpen = true;
        
        Debug.Log($"Opened {currentCategory} task panel with {categoryTasks.Count} tasks");
    }
    
    void PositionPanelAtButton()
    {
        Button currentButton = GetButtonForCategory(currentCategory);
        if (currentButton == null || taskCategoryPanel == null) return;
        
        RectTransform buttonRect = currentButton.GetComponent<RectTransform>();
        RectTransform panelRect = taskCategoryPanel.GetComponent<RectTransform>();

        if (buttonRect != null && panelRect != null)
        {
            // Get button position and use its X coordinate
            panelRect.anchoredPosition = new Vector2(buttonRect.anchoredPosition.x/4 + panelXOffset, panelRect.anchoredPosition.y);
        }
    }
    
    Button GetButtonForCategory(TaskCategory category)
    {
        switch (category)
        {
            case TaskCategory.Community: return communityButton;
            case TaskCategory.Kitchen: return kitchenButton;
            case TaskCategory.Shelter: return shelterButton;
            case TaskCategory.Emergency: return emergencyButton;
            default: return null;
        }
    }
    
    void CloseCategoryPanel()
    {
        if (taskCategoryPanel != null)
        {
            taskCategoryPanel.SetActive(false);
        }

        isPanelOpen = false;
        ClearTaskList();
        
        Debug.Log("Closed category task panel");
    }
    
    List<GameTask> GetTasksByCategory(TaskCategory category)
    {
        if (TaskSystem.Instance == null) return new List<GameTask>();
        
        var allTasks = TaskSystem.Instance.GetAllActiveTasks();
        var categoryTasks = new List<GameTask>();
        
        foreach (GameTask task in allTasks)
        {
            // Skip alert tasks
            if (task.taskType == TaskType.Alert)
                continue;

            // Skip global tasks
            if (task.isGlobalTask)
                continue;
                
            if (IsTaskInCategory(task, category))
            {
                categoryTasks.Add(task);
            }
        }
        
        return categoryTasks;
    }
    
    bool IsTaskInCategory(GameTask task, TaskCategory category)
    {
        switch (category)
        {
            case TaskCategory.Community:
                return IsTaskRelatedToFacilityType(task, "Community") || 
                       IsTaskRelatedToBuildingType(task, BuildingType.Community);
                       
            case TaskCategory.Kitchen:
                return IsTaskRelatedToFacilityType(task, "Kitchen") || 
                       IsTaskRelatedToBuildingType(task, BuildingType.Kitchen);
                       
            case TaskCategory.Shelter:
                return IsTaskRelatedToFacilityType(task, "Shelter") || 
                       IsTaskRelatedToBuildingType(task, BuildingType.Shelter);
                       
            case TaskCategory.Emergency:
                return task.taskType == TaskType.Emergency || 
                       task.taskTitle.ToLower().Contains("emergency") ||
                       task.roundsRemaining <= 1;
                       
            default:
                return false;
        }
    }
    
    bool IsTaskRelatedToFacilityType(GameTask task, string facilityType)
    {
        if (string.IsNullOrEmpty(task.affectedFacility)) return false;
        
        return task.affectedFacility.ToLower().Contains(facilityType.ToLower());
    }
    
    bool IsTaskRelatedToBuildingType(GameTask task, BuildingType buildingType)
    {
        // Check if task has delivery involving this building type
        if (task.linkedDeliveryTaskIds == null || task.linkedDeliveryTaskIds.Count == 0) 
            return false;
            
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return false;
        
        var activeTasks = deliverySystem.GetActiveTasks();
        
        foreach (var deliveryTask in activeTasks)
        {
            if (task.linkedDeliveryTaskIds.Contains(deliveryTask.taskId))
            {
                // Check if delivery involves this building type
                Building sourceBuilding = deliveryTask.sourceBuilding as Building;
                Building destBuilding = deliveryTask.destinationBuilding as Building;
                
                if ((sourceBuilding != null && sourceBuilding.GetBuildingType() == buildingType) ||
                    (destBuilding != null && destBuilding.GetBuildingType() == buildingType))
                {
                    return true;
                }
            }
        }
        
        return false;
    }
    
    void CreateTaskListItem(GameTask task)
    {
        if (taskListItemPrefab == null || taskListParent == null) return;
        
        GameObject item = Instantiate(taskListItemPrefab, taskListParent);
        currentTaskItems.Add(item);
        
        // Try to find StatLayout first
        Transform statLayout = item.transform.Find("Statlayout");
        Transform searchParent = statLayout != null ? statLayout : item.transform;
        
        // Find text components (search in StatLayout if it exists, otherwise in root)
        TextMeshProUGUI taskTitle = searchParent.Find("TaskTitle")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI taskType = searchParent.Find("TaskType")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI taskRounds = searchParent.Find("TaskRounds")?.GetComponent<TextMeshProUGUI>();
        
        // ViewButton should be in root level
        Button viewButton = item.transform.Find("ViewButton")?.GetComponent<Button>();
        
        // Debug log the prefab structure
        if (statLayout == null)
        {
            Debug.LogWarning($"StatLayout not found in {item.name}. Children: {string.Join(", ", GetChildNames(item.transform))}");
        }
        
        if (taskTitle != null)
        {
            taskTitle.text = task.taskTitle;
        }
        else
        {
            Debug.LogWarning("TaskTitle component not found");
        }
        
        if (taskType != null)
        {
            taskType.text = task.taskType.ToString();
            taskType.color = GetTaskTypeColor(task.taskType);
        }
        else
        {
            Debug.LogWarning("TaskType component not found");
        }
        
        if (taskRounds != null)
        {
            taskRounds.text = $"{task.roundsRemaining} rounds";
            taskRounds.color = GetRoundsColor(task.roundsRemaining);
        }
        else
        {
            Debug.LogWarning("TaskRounds component not found");
        }
        
        // Setup View button
        if (viewButton != null)
        {
            viewButton.onClick.RemoveAllListeners();
            viewButton.onClick.AddListener(() => OnViewTaskButtonClicked(task));
        }
        else
        {
            Debug.LogWarning("ViewButton not found");
        }
    }
    
    // Helper method to debug prefab structure
    string[] GetChildNames(Transform parent)
    {
        string[] names = new string[parent.childCount];
        for (int i = 0; i < parent.childCount; i++)
        {
            names[i] = parent.GetChild(i).name;
        }
        return names;
    }
    
    void OnViewTaskButtonClicked(GameTask task)
    {
        if (task == null) return;
        
        // Close this panel first
        CloseCategoryPanel();
        
        // Open the task in TaskDetailUI
        TaskDetailUI taskDetailUI = FindObjectOfType<TaskDetailUI>();
        if (taskDetailUI != null)
        {
            taskDetailUI.ShowTaskDetail(task);
            Debug.Log($"Opened task detail for: {task.taskTitle}");
        }
        else
        {
            // Fallback to TaskCenterUI
            TaskCenterUI taskCenterUI = FindObjectOfType<TaskCenterUI>();
            if (taskCenterUI != null)
            {
                taskCenterUI.OpenTaskCenter();
                Debug.Log($"Opened task center - locate task: {task.taskTitle}");
            }
        }
    }
    
    void ClearTaskList()
    {
        foreach (GameObject item in currentTaskItems)
        {
            if (item != null)
                Destroy(item);
        }
        currentTaskItems.Clear();
    }
    
    string GetCategoryDisplayName(TaskCategory category)
    {
        switch (category)
        {
            case TaskCategory.Community: return "Community Tasks";
            case TaskCategory.Kitchen: return "Kitchen Tasks";
            case TaskCategory.Shelter: return "Shelter Tasks";
            case TaskCategory.Emergency: return "Emergency Tasks";
            default: return "Tasks";
        }
    }
    
    Color GetTaskTypeColor(TaskType taskType)
    {
        switch (taskType)
        {
            case TaskType.Emergency: return darkRed;
            case TaskType.Demand: return brown;
            case TaskType.Advisory: return brown;
            default: return brown;
        }
    }
    
    Color GetRoundsColor(int rounds)
    {
        if (rounds <= 1) return darkRed;
        if (rounds <= 2) return darkOrange;
        return brown;
    }
    
    public bool IsPanelOpen()
    {
        return isPanelOpen;
    }

    public bool IsUIOpen()
    {
        return isPanelOpen;
    }

    // Debug method
    [ContextMenu("Debug Task Categories")]
    public void DebugTaskCategories()
    {
        Debug.Log("=== TASK CATEGORIES DEBUG ===");
        foreach (TaskCategory category in System.Enum.GetValues(typeof(TaskCategory)))
        {
            List<GameTask> tasks = GetTasksByCategory(category);
            Debug.Log($"{category}: {tasks.Count} tasks");
            foreach (GameTask task in tasks)
            {
                Debug.Log($"  - {task.taskTitle} ({task.taskType})");
            }
        }
    }
}