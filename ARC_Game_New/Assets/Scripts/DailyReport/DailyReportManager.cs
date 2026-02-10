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
    public int finalDay = 8;
    
    [Header("History Navigation")]
    public GameObject historyNavigationPanel;
    public Button[] dayButtons = new Button[7]; // Day 2-8 buttons
    
    // REMOVED: allReportsButton - "All Reports" feature removed
    // REMOVED: allReportsScrollView - "All Reports" feature removed
    
    [Header("Button States")]
    public Sprite selectedButtonSprite;
    public Sprite notSelectedButtonSprite;
    public Sprite inactiveButtonSprite;

    private int currentViewingDay = -1; // -1 = current day, 1-8 = specific historical day
    // REMOVED: isEndGameReached - only used by All Reports feature

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
        if (globalClock == null)
            globalClock = FindObjectOfType<GlobalClock>();
        
        if (globalClock != null)
        {
            globalClock.OnDayChanged += OnDayChangeAttempt;
        }
        
        if (nextDayButton != null)
        {
            nextDayButton.onClick.AddListener(OnNextDayButtonClicked);
        }
        
        if (panelCanvasGroup == null && dailyReportPanel != null)
        {
            panelCanvasGroup = dailyReportPanel.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
            {
                panelCanvasGroup = dailyReportPanel.AddComponent<CanvasGroup>();
            }
        }
        
        if (dailyReportPanel != null)
        {
            dailyReportPanel.SetActive(false);
        }
        
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
        if (isTransitioning)
        {
            Debug.Log($"Transition in progress - skipping day change for day {newDay}");
            return;
        }
        
        if (Time.unscaledTime - lastReportTime < reportCooldown)
        {
            Debug.Log($"Report cooldown active - skipping duplicate day change for day {newDay}");
            return;
        }
        
        if (newDay > 1)
        {
            ShowDailyReport();
            lastReportTime = Time.unscaledTime;
        }
    }
    
    public void ShowDailyReport()
    {
        if (dailyReportPanel == null || isTransitioning) return;
        
        // REMOVED: EnableAllReportsButton() call - "All Reports" feature removed
        
        // Pause the simulation
        if (globalClock != null)
        {
            globalClock.PauseSimulation();
        }
        
        StartCoroutine(FadeInReportWithData());
        
        Debug.Log("Daily report displayed - waiting for player to read and continue to next day");
        GameLogPanel.Instance.LogPlayerAction("Daily report displayed - waiting for player to read and continue to next day");
    }

    IEnumerator FadeInReportWithData()
    {
        // Reset all elements to hidden before fade in
        if (reportUI != null)
        {
            reportUI.ResetAllElementsToHidden();
        }
        
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
        
        // Generate and display report (skip Day 1 which has no data)
        if (currentDay > 1 && DailyReportData.Instance != null && reportUI != null)
        {
            var metrics = DailyReportData.Instance.GenerateDailyReport();
            reportUI.DisplayDailyReport(metrics);
        }
        
        // Mark the current day as selected in navigation
        UpdateDayButtonStates(currentDay);
        
        // Reset viewing day to current
        currentViewingDay = -1;
    }
    
    IEnumerator FadeInReport()
    {
        isTransitioning = true;
        isWaitingForNextDay = false;
        
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
            
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = true;
        }
        
        isTransitioning = false;
        isWaitingForNextDay = true;
        
        if (nextDayButton != null)
        {
            nextDayButton.interactable = true;
        }
    }
    
    IEnumerator FadeOutReport()
    {
        isTransitioning = true;
        isWaitingForNextDay = false;
        
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.interactable = false;
        }
        
        if (nextDayButton != null)
        {
            nextDayButton.interactable = false;
        }
        
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
            
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.blocksRaycasts = false;
        }

        if (day1MaskPanel != null)
        {
            day1MaskPanel.SetActive(false);
        }

        dailyReportPanel.SetActive(false);
        
        isTransitioning = false;
        
        Debug.Log("Daily report fade out completed");
    }
    
    void OnNextDayButtonClicked()
    {
        Debug.Log("=== NEXT DAY BUTTON DEBUG ===");
        Debug.Log($"isWaitingForNextDay: {isWaitingForNextDay}");
        Debug.Log($"isTransitioning: {isTransitioning}");
        
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
        yield return StartCoroutine(FadeOutReport());
        
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

    // ================================================================
    // HISTORY NAVIGATION (Day 2-8 buttons only)
    // ================================================================

    void SetupHistoryNavigation()
    {
        // Setup individual day buttons (Day 2 through Day 8)
        for (int i = 0; i < dayButtons.Length; i++)
        {
            int dayIndex = i + 2; // Day 2-8
            if (dayButtons[i] != null)
            {
                dayButtons[i].onClick.AddListener(() => OnDayButtonClicked(dayIndex));
            }
        }
        
        // REMOVED: allReportsButton setup - "All Reports" feature removed
        // REMOVED: allReportsScrollView hide - "All Reports" feature removed
    }

    // REMOVED: EnableAllReportsButton() - "All Reports" feature removed

    /// <summary>
    /// Show historical report for a specific day.
    /// Uses stored metrics from history - no recalculation.
    /// </summary>
    void OnDayButtonClicked(int day)
    {
        if (DailyReportData.Instance == null) return;
        
        if (!DailyReportData.Instance.HasReportForDay(day))
        {
            Debug.LogWarning($"No report available for Day {day}");
            return;
        }
        
        // REMOVED: allReportsScrollView hide - "All Reports" feature removed
        
        // Ensure the report UI is visible
        if (reportUI != null)
        {
            reportUI.gameObject.SetActive(true);
        }
        
        // Get the frozen historical report
        DailyReportMetrics metrics = DailyReportData.Instance.GetHistoricalReport(day);
        
        if (metrics != null && reportUI != null)
        {
            currentViewingDay = day;
            
            // Display immediately using stored values (no animation, no recalculation)
            reportUI.DisplayDailyReportImmediate(metrics, day);
            
            // Update which button appears selected
            UpdateDayButtonStates(day);
            
            Debug.Log($"Displaying historical report for Day {day}");
        }
    }

    /// <summary>
    /// Update button visual states: selected, available, or inactive.
    /// </summary>
    void UpdateDayButtonStates(int selectedDay)
    {
        int currentDay = globalClock != null ? globalClock.GetCurrentDay() : 1;
        
        for (int i = 0; i < dayButtons.Length; i++)
        {
            int dayIndex = i + 2; // Day 2-8
            Button btn = dayButtons[i];
            
            if (btn == null) continue;
            
            Image btnImage = btn.GetComponent<Image>();
            if (btnImage == null) continue;
            
            if (dayIndex == selectedDay)
            {
                // Currently viewing this day
                btnImage.sprite = selectedButtonSprite;
                btn.interactable = true;
            }
            else if (DailyReportData.Instance != null && DailyReportData.Instance.HasReportForDay(dayIndex))
            {
                // FIX: Check if report EXISTS for this day, not just if dayIndex <= currentDay.
                // This is more accurate since a report only exists after SaveReportToHistory is called.
                btnImage.sprite = notSelectedButtonSprite;
                btn.interactable = true;
            }
            else
            {
                // No report available (future day or not yet saved)
                btnImage.sprite = inactiveButtonSprite;
                btn.interactable = false;
            }
        }
    }

    // REMOVED: OnAllReportsButtonClicked() - "All Reports" feature removed
    // REMOVED: PopulateAllReports() - "All Reports" feature removed
    // REMOVED: CreateAllReportsItem() - "All Reports" feature removed
    
    // ================================================================
    // DEBUG / TESTING
    // ================================================================

    [ContextMenu("Test Show Daily Report")]
    public void TestShowDailyReport()
    {
        ShowDailyReport();
    }
    
    [ContextMenu("Skip Transition")]
    public void SkipTransition()
    {
        StopAllCoroutines();
        
        if (dailyReportPanel != null && dailyReportPanel.activeInHierarchy)
        {
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