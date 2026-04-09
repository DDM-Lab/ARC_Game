using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

public class TaskCenterNotification : MonoBehaviour
{
    [Header("Button References")] 
    public Button taskCenterButton;
    public TaskCenterUI taskCenterUI;
    
    [Header("Notification Components")]
    public GameObject notificationDot;
    public TextMeshProUGUI taskCountText;
    
    [Header("Settings")]
    public bool showNotification = true;

    [Header("Notification Colors")]
    public Color normalColor = Color.white;
    
    private Image notificationImage;
    private TaskSystem taskSystem;

    
    void Awake()
    {
        // Get components
        if (taskCenterButton == null)
            taskCenterButton = GetComponent<Button>();
        
        if (notificationDot != null)
            notificationImage = notificationDot.GetComponent<Image>();
        
        // Setup button listener
        if (taskCenterButton != null)
        {
            taskCenterButton.onClick.AddListener(OnTaskCenterButtonClicked);
        }
    }

    void Start()
    {
        // Find system references
        if (taskCenterUI == null)
            taskCenterUI = FindObjectOfType<TaskCenterUI>();
        
        taskSystem = TaskSystem.Instance;
        
        // Subscribe to task system events
        if (taskSystem != null)
        {
            taskSystem.OnTaskCreated += OnTaskChanged;
            taskSystem.OnTaskCompleted += OnTaskChanged;
            taskSystem.OnTaskExpired += OnTaskChanged;
        }
        
        // Initialize display
        UpdateNotificationDisplay();
        
        Debug.Log("Task Center Notification initialized");
    }
    
    void Update()
    {
        // Update display periodically
        if (Time.frameCount % 30 == 0) // Every 30 frames (~0.5 seconds at 60fps)
        {
            UpdateNotificationDisplay();
        }
    }
    
    void OnTaskCenterButtonClicked()
    {
        if (taskCenterUI != null)
        {
            taskCenterUI.ToggleTaskCenter();
            Debug.Log("Task Center UI toggled via notification button");
        }
    }
    
    void UpdateNotificationDisplay()
    {
        if (taskSystem == null) return;
        
        var activeTasks = taskSystem.GetAllActiveNonAlertTasks().Where(t => t.status == TaskStatus.Active).ToList();
        int activeTaskCount = activeTasks.Count;
        
        // Update task count text
        if (taskCountText != null)
        {
            bool shouldShow = showNotification && activeTaskCount > 0;
            taskCountText.gameObject.SetActive(shouldShow);
            if (shouldShow)
                taskCountText.text = activeTaskCount.ToString();
        }
        
        if (notificationDot != null)
        {
            bool shouldShow = showNotification && activeTaskCount > 0;
            notificationDot.SetActive(shouldShow);

            if (shouldShow && notificationImage != null)
                notificationImage.color = normalColor;
        }
    }
    
    void OnTaskChanged(GameTask task)
    {
        // Immediate update when tasks change
        UpdateNotificationDisplay();
    }

    void OnDestroy()
    {
        // Clean up event subscriptions
        if (taskSystem != null)
        {
            taskSystem.OnTaskCreated -= OnTaskChanged;
            taskSystem.OnTaskCompleted -= OnTaskChanged;
            taskSystem.OnTaskExpired -= OnTaskChanged;
        }
    }
    
    // Public method to manually refresh
    public void RefreshNotification()
    {
        UpdateNotificationDisplay();
    }
    
    // Get current active task count for external access
    public int GetActiveTaskCount()
    {
        return taskSystem != null ? taskSystem.GetAllActiveTasks().Count : 0;
    }
}