using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GlobalWorkerButton : MonoBehaviour
{
    [Header("Button Components")]
    public Button workerButton;
    
    [Header("System References")]
    public GlobalWorkerManagementUI globalWorkerUI;
    public WorkerSystem workerSystem;
    
    [Header("Notification")]
    public GameObject notificationDot;
    public Color urgentNotificationColor = Color.red;
    public Color normalNotificationColor = Color.yellow;
    
    private Image buttonImage;
    
    void Start()
    {
        // Get components
        if (workerButton == null)
            workerButton = GetComponent<Button>();
        
        if (buttonImage == null)
            buttonImage = GetComponent<Image>();
        
        // Find system references if not assigned
        if (globalWorkerUI == null)
            globalWorkerUI = FindObjectOfType<GlobalWorkerManagementUI>();
        
        if (workerSystem == null)
            workerSystem = FindObjectOfType<WorkerSystem>();
        
        // Setup button listener
        if (workerButton != null)
        {
            workerButton.onClick.AddListener(OnWorkerButtonClicked);
        }
        
        // Subscribe to worker system events
        if (workerSystem != null)
        {
            workerSystem.OnWorkerStatsChanged += OnWorkerStatsChanged;
        }
        
        // Initialize display
        UpdateButtonDisplay();
        
        Debug.Log("Global Worker Button initialized");
    }
    
    void Update()
    {
        // Update display periodically
        if (Time.frameCount % 60 == 0) // Every 60 frames (~1 second at 60fps)
        {
            UpdateButtonDisplay();
        }
    }
    
    void OnWorkerButtonClicked()
    {
        if (globalWorkerUI != null)
        {
            globalWorkerUI.ToggleUI();
            Debug.Log("Global Worker Management UI toggled");
        }
        else
        {
            Debug.LogWarning("GlobalWorkerManagementUI reference not found!");
        }
    }
    
    void UpdateButtonDisplay()
    {
        UpdateNotificationState();
    }
    
    
    void UpdateNotificationState()
    {
        if (notificationDot == null || workerSystem == null) return;
        
        bool shouldShowNotification = false;
        Color notificationColor = normalNotificationColor;
        
        // Check for buildings needing workers
        BuildingSystem buildingSystem = FindObjectOfType<BuildingSystem>();
        if (buildingSystem != null)
        {
            var buildingsNeedingWorkers = buildingSystem.GetBuildingsNeedingWorkers();
            var availableWorkforce = workerSystem.GetTotalAvailableWorkforce();
            
            if (buildingsNeedingWorkers.Count > 0)
            {
                shouldShowNotification = true;
                
                // Urgent notification if we can assign workers but haven't
                if (availableWorkforce >= 4) // Minimum workforce needed
                {
                    notificationColor = urgentNotificationColor;
                }
                else
                {
                    notificationColor = normalNotificationColor;
                }
            }
        }
        
        // Show/hide notification
        notificationDot.SetActive(shouldShowNotification);
        
        // Set notification color
        if (shouldShowNotification)
        {
            Image notificationImage = notificationDot.GetComponent<Image>();
            if (notificationImage != null)
            {
                notificationImage.color = notificationColor;
            }
        }
    }
    
    void OnWorkerStatsChanged()
    {
        // Immediate update when worker stats change
        UpdateButtonDisplay();
    }

    void OnDestroy()
    {
        // Clean up event subscriptions
        if (workerSystem != null)
        {
            workerSystem.OnWorkerStatsChanged -= OnWorkerStatsChanged;
        }
    }
    
    // Public method to manually refresh display
    public void RefreshDisplay()
    {
        UpdateButtonDisplay();
    }
}