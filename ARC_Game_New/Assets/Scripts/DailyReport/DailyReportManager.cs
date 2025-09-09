using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class DailyReportManager : MonoBehaviour
{
    [Header("Daily Report UI")]
    public GameObject dailyReportPanel;
    public Button nextDayButton;
    public CanvasGroup panelCanvasGroup;
    public DailyReportUI reportUI;
    
    [Header("Transition Settings")]
    public float fadeInDuration = 0.5f;
    public float fadeOutDuration = 0.3f;
    public AnimationCurve fadeInCurve = new AnimationCurve(new Keyframe(0, 0, 0, 2), new Keyframe(1, 1, 0, 0));
    public AnimationCurve fadeOutCurve = new AnimationCurve(new Keyframe(0, 0, 0, 0), new Keyframe(1, 1, 2, 0));
    
    [Header("System References")]
    public GlobalClock globalClock;
    
    private bool isWaitingForNextDay = false;
    private bool isTransitioning = false;
    
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
        
        // Setup canvas group if not assigned
        if (panelCanvasGroup == null && dailyReportPanel != null)
        {
            panelCanvasGroup = dailyReportPanel.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
            {
                panelCanvasGroup = dailyReportPanel.AddComponent<CanvasGroup>();
            }
        }
        
        // Hide report panel initially
        if (dailyReportPanel != null)
        {
            dailyReportPanel.SetActive(false);
        }
        
        // Initialize canvas group
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }
    }
    
    void OnDayChangeAttempt(int newDay)
    {
        // Prevent showing report during transitions
        if (isTransitioning)
        {
            Debug.Log($"Transition in progress - skipping day change for day {newDay}");
            return;
        }
        
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
        if (dailyReportPanel == null || isTransitioning) return;
        
        // Pause the simulation
        if (globalClock != null)
        {
            globalClock.PauseSimulation();
        }
        
        // Start fade in transition
        StartCoroutine(FadeInReportWithData());
        
        Debug.Log("Daily report displayed - waiting for player to continue");
        ToastManager.ShowToast("Daily report generated", ToastType.Info, true);
    }

    IEnumerator FadeInReportWithData()
    {
        // Reset all elements to hidden AFTER panel is activated but BEFORE fade in
        if (reportUI != null)
        {
            reportUI.ResetAllElementsToHidden();
        }
        
        // Do the existing fade in animation first
        yield return StartCoroutine(FadeInReport());
        
        // THEN generate and display report data
        if (DailyReportData.Instance != null && reportUI != null)
        {
            var metrics = DailyReportData.Instance.GenerateDailyReport();
            reportUI.DisplayDailyReport(metrics);
        }
    }
    
    IEnumerator FadeInReport()
    {
        isTransitioning = true;
        isWaitingForNextDay = false; // Disable button until fade completes
        
        // Show panel but invisible
        dailyReportPanel.SetActive(true);
        
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = true;
            
            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = elapsed / fadeInDuration;
                float curveValue = fadeInCurve.Evaluate(progress);
                
                panelCanvasGroup.alpha = curveValue;
                
                yield return null;
            }
            
            // Ensure final state
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = true;
        }
        
        isTransitioning = false;
        isWaitingForNextDay = true; // Enable button interaction
        
        // Enable next day button
        if (nextDayButton != null)
        {
            nextDayButton.interactable = true;
        }
    }
    
    IEnumerator FadeOutReport()
    {
        isTransitioning = true;
        isWaitingForNextDay = false;
        
        // Disable interactions immediately
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.interactable = false;
        }
        
        if (nextDayButton != null)
        {
            nextDayButton.interactable = false;
        }
        
        // Fade out animation
        if (panelCanvasGroup != null)
        {
            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = elapsed / fadeOutDuration;
                float curveValue = fadeOutCurve.Evaluate(progress);
                
                panelCanvasGroup.alpha = 1f - curveValue;
                
                yield return null;
            }
            
            // Ensure final state
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.blocksRaycasts = false;
        }
        
        // Hide panel completely
        dailyReportPanel.SetActive(false);
        
        isTransitioning = false;
        
        Debug.Log("Daily report fade out completed");
    }
    
    void OnNextDayButtonClicked()
    {
        Debug.Log("=== NEXT DAY BUTTON DEBUG ===");
        Debug.Log($"isWaitingForNextDay: {isWaitingForNextDay}");
        Debug.Log($"isTransitioning: {isTransitioning}");
        
        // Prevent action during transitions
        if (isTransitioning)
        {
            Debug.Log("BLOCKED - transition in progress");
            return;
        }
        
        if (!isWaitingForNextDay) 
        {
            Debug.Log("EARLY RETURN - not waiting for next day");
            return;
        }
        
        if (dailyReportPanel == null)
        {
            Debug.Log("ERROR: dailyReportPanel is null!");
            return;
        }
        
        // Start fade out transition
        StartCoroutine(FadeOutAndProceed());
    }
    
    IEnumerator FadeOutAndProceed()
    {
        // Fade out the panel
        yield return StartCoroutine(FadeOutReport());
        
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
    
    public bool IsTransitioning()
    {
        return isTransitioning;
    }
    
    // Method to force show report for testing
    [ContextMenu("Test Show Daily Report")]
    public void TestShowDailyReport()
    {
        ShowDailyReport();
    }
    
    // Method to skip current transition (for debugging)
    [ContextMenu("Skip Transition")]
    public void SkipTransition()
    {
        StopAllCoroutines();
        
        if (dailyReportPanel != null && dailyReportPanel.activeInHierarchy)
        {
            // Complete fade out immediately
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = 0f;
                panelCanvasGroup.interactable = false;
                panelCanvasGroup.blocksRaycasts = false;
            }
            
            dailyReportPanel.SetActive(false);
            
            if (globalClock != null)
            {
                globalClock.ResumeSimulation();
                globalClock.ProceedToNextDay();
            }
        }
        
        isTransitioning = false;
        isWaitingForNextDay = false;
        
        Debug.Log("Transition skipped");
    }
}