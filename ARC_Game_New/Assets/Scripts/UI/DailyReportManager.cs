using UnityEngine;
using UnityEngine.UI;

public class DailyReportManager : MonoBehaviour
{
    [Header("Daily Report UI")]
    public GameObject dailyReportPanel;
    public Button nextDayButton;
    
    [Header("System References")]
    public GlobalClock globalClock;
    
    private bool isWaitingForNextDay = false;
    
    // Singleton
    public static DailyReportManager Instance { get; private set; }
    private float lastReportTime = 0f;
    private float reportCooldown = 1f;
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
        // Find GlobalClock if not assigned
        if (globalClock == null)
            globalClock = FindObjectOfType<GlobalClock>();
        
        // Subscribe to day change events
        if (globalClock != null)
        {
            globalClock.OnDayChanged += OnDayChangeAttempt;
        }
        
        // Setup next day button
        if (nextDayButton != null)
        {
            nextDayButton.onClick.AddListener(OnNextDayButtonClicked);
        }
        
        // Hide report panel initially
        if (dailyReportPanel != null)
        {
            dailyReportPanel.SetActive(false);
        }
    }
    
    void OnDayChangeAttempt(int newDay)
    {
        // Add cooldown to prevent duplicate reports
        if (Time.unscaledTime - lastReportTime < reportCooldown)
        {
            Debug.Log($"Report cooldown active - skipping duplicate day change for day {newDay}");
            return;
        }
        
        // Show daily report at the end of day (when transitioning to next day)
        if (newDay > 1) // Don't show report before first day starts
        {
            ShowDailyReport();
            lastReportTime = Time.unscaledTime;
        }
    }
    
    void ShowDailyReport()
    {
        if (dailyReportPanel == null) return;
        
        // Pause the simulation
        if (globalClock != null)
        {
            globalClock.PauseSimulation();
        }
        
        // Show the report panel
        dailyReportPanel.SetActive(true);
        isWaitingForNextDay = true;
        
        Debug.Log("Daily report displayed - waiting for player to continue");
        ToastManager.ShowToast("Daily report generated", ToastType.Info, true);
    }
    
    void OnNextDayButtonClicked()
    {
        Debug.Log("=== NEXT DAY BUTTON DEBUG ===");
        Debug.Log($"isWaitingForNextDay: {isWaitingForNextDay}");
        Debug.Log($"dailyReportPanel null: {dailyReportPanel == null}");
        
        if (!isWaitingForNextDay) 
        {
            Debug.Log("EARLY RETURN - not waiting for next day");
            return;
        }
        
        if (dailyReportPanel != null)
        {
            Debug.Log($"Panel activeInHierarchy BEFORE: {dailyReportPanel.activeInHierarchy}");
            Debug.Log($"Panel activeSelf BEFORE: {dailyReportPanel.activeSelf}");
            
            dailyReportPanel.SetActive(false);
            
            Debug.Log($"Panel activeInHierarchy AFTER: {dailyReportPanel.activeInHierarchy}");
            Debug.Log($"Panel activeSelf AFTER: {dailyReportPanel.activeSelf}");
        }
        else
        {
            Debug.Log("ERROR: dailyReportPanel is null!");
        }
        
        isWaitingForNextDay = false;
        
        // Resume simulation and allow day transition
        if (globalClock != null)
        {
            globalClock.ResumeSimulation();
            globalClock.ProceedToNextDay();
        }
        
        Debug.Log("Player confirmed - proceeding to next day");
        ToastManager.ShowToast("Proceeding to next day", ToastType.Success, true);
    }
    
    public bool IsWaitingForNextDay()
    {
        return isWaitingForNextDay;
    }
    
    // Method to force show report for testing
    [ContextMenu("Test Show Daily Report")]
    public void TestShowDailyReport()
    {
        ShowDailyReport();
    }
}