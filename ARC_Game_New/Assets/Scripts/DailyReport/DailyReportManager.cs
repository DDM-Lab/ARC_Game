using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

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

    [Header("Day 1 Special")]
    public GameObject day1MaskPanel;
    [Header("Game End Settings")]
    public int finalDay = 8; // Game ends after this day
    
    [Header("History Navigation")]
    public GameObject historyNavigationPanel;
    public Button[] dayButtons = new Button[7]; // Day 2-8 buttons
    
    [Header("Button States")]
    public Sprite selectedButtonSprite;
    public Sprite notSelectedButtonSprite;
    public Sprite inactiveButtonSprite;

    private int currentViewingDay = -1; // -1 = current day, 1-8 = specific day

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

        SetupHistoryNavigation();
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
    
    public void ShowDailyReport()
    {
        if (dailyReportPanel == null || isTransitioning) return;
        
        // Pause the simulation
        if (globalClock != null)
        {
            globalClock.PauseSimulation();
        }
        
        // Start fade in transition
        StartCoroutine(FadeInReportWithData());
        
        Debug.Log("Daily report displayed - waiting for player to read and continue to next day");
        GameLogPanel.Instance.LogPlayerAction("Daily report displayed - waiting for player to read and continue to next day");
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

        // Show mask for Day 1, hide for other days
        int currentDay = globalClock != null ? globalClock.GetCurrentDay() : 1;
        if (day1MaskPanel != null)
        {
            day1MaskPanel.SetActive(currentDay == 1);
        }

        // Hide next day button if game ended
        if (currentDay >= finalDay && nextDayButton != null)
        {
            nextDayButton.gameObject.SetActive(false);
        }
        
        // Only generate report data if NOT Day 1
        if (currentDay > 1 && DailyReportData.Instance != null && reportUI != null)
        {
            var metrics = DailyReportData.Instance.GenerateDailyReport();
            reportUI.DisplayDailyReport(metrics);
        }
        
        // Update button states
        UpdateDayButtonStates(currentDay);
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

        if (day1MaskPanel != null)
        {
            day1MaskPanel.SetActive(false);
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

        // Show confirmation popup when clicking next day
        ConfirmationPopup.Instance.ShowPopup(
            message: "Are you sure you want to proceed to the next day?",
            onConfirm: () => {
                StartCoroutine(FadeOutAndProceed());
            },
            title: "Proceed to Next Day?"
        );
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
        GameLogPanel.Instance.LogPlayerAction("Player finished reading daily report. Now proceeding to next day");
    }
    
    public bool IsWaitingForNextDay()
    {
        return isWaitingForNextDay;
    }
    
    public bool IsTransitioning()
    {
        return isTransitioning;
    }

    // =========================================================================
    // HISTORY NAVIGATION
    // =========================================================================

    void SetupHistoryNavigation()
    {
        // Setup individual day buttons (Day 2-8)
        for (int i = 0; i < dayButtons.Length; i++)
        {
            int dayIndex = i + 2;
            if (dayButtons[i] != null)
            {
                dayButtons[i].onClick.AddListener(() => OnDayButtonClicked(dayIndex));
            }
        }
    }

    /// <summary>
    /// Show report for a specific historical day.
    /// </summary>
    void OnDayButtonClicked(int day)
    {
        if (DailyReportData.Instance == null) return;
        
        if (!DailyReportData.Instance.HasReportForDay(day))
        {
            Debug.LogWarning($"No report available for Day {day}");
            return;
        }
        
        // Show single report UI
        if (reportUI != null)
        {
            reportUI.gameObject.SetActive(true);
        }
        
        // Get historical report
        DailyReportMetrics metrics = DailyReportData.Instance.GetHistoricalReport(day);
        
        if (metrics != null && reportUI != null)
        {
            currentViewingDay = day;
            
            // USE IMMEDIATE DISPLAY - NO ANIMATIONS
            reportUI.DisplayDailyReportImmediate(metrics, day);
            
            // UPDATE BUTTON STATES
            UpdateDayButtonStates(day);
            
            Debug.Log($"Displaying historical report for Day {day}");
        }
    }

    /// <summary>
    /// FIX PROBLEM 1: Now checks HasReportForDay() instead of just day number comparison.
    /// This ensures buttons are only enabled for days that actually have saved data.
    /// </summary>
    void UpdateDayButtonStates(int selectedDay)
    {
        for (int i = 0; i < dayButtons.Length; i++)
        {
            int dayIndex = i + 2; // Day 2-8
            Button btn = dayButtons[i];
            
            if (btn == null) continue;
            
            Image btnImage = btn.GetComponent<Image>();
            if (btnImage == null) continue;
            
            // Selected (currently viewing)
            if (dayIndex == selectedDay)
            {
                btnImage.sprite = selectedButtonSprite;
                btn.interactable = true;
            }
            // Has saved report data (available to view)
            else if (DailyReportData.Instance != null && DailyReportData.Instance.HasReportForDay(dayIndex))
            {
                btnImage.sprite = notSelectedButtonSprite;
                btn.interactable = true;
            }
            // No data yet (inactive)
            else
            {
                btnImage.sprite = inactiveButtonSprite;
                btn.interactable = false;
            }
        }
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