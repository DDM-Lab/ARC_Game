using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System;

[System.Serializable]
public class MetricChangeEntry
{
    public float amount;
    public string description;
    public int round;
    public int day;
    public float timestamp;
    
    public MetricChangeEntry(float amt, string desc, int r, int d)
    {
        amount = amt;
        description = desc;
        round = r;
        day = d;
        timestamp = Time.time;
    }
}

[System.Serializable]
public class DailyMetricsHistory
{
    public int day;
    public List<MetricChangeEntry> satisfactionChanges = new List<MetricChangeEntry>();
    public List<MetricChangeEntry> budgetChanges = new List<MetricChangeEntry>();
}

public class MetricsHistoryManager : MonoBehaviour
{
    [Header("Merged Panel")]
    public RectTransform metricsPanel;
    public TextMeshProUGUI panelTitleText;
    public Button exitButton;
    
    [Header("Tab Buttons")]
    public Button satisfactionTabButton;
    public Button budgetTabButton;
    
    [Header("Scroll View (Shared)")]
    public ScrollRect metricsScrollView;
    public Transform metricsContent;
    
    [Header("Prefabs")]
    public GameObject metricEntryPrefab;
    
    [Header("Animation Settings")]
    public float expandedHeight = 400f;
    public float collapsedHeight = 0f;
    public float animationDuration = 0.3f;
    
    [Header("Colors")]
    public Color positiveColor = Color.green;
    public Color negativeColor = Color.red;
    public Color activeTabColor = Color.green;
    public Color inactiveTabColor = Color.white;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // History data storage
    private List<DailyMetricsHistory> allDaysHistory = new List<DailyMetricsHistory>();
    private DailyMetricsHistory currentDayHistory;
    
    // UI state
    private bool isPanelExpanded = false;
    private bool isAnimating = false;
    private bool isShowingSatisfaction = true; // Track which tab is active
    
    // Current game state
    private int currentRound = 1;
    private int currentDay = 1;
    
    // UI item tracking
    private List<GameObject> currentMetricItems = new List<GameObject>();
    
    public static MetricsHistoryManager Instance { get; private set; }
    
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
        SetupUI();
        InitializeHistory();
        SubscribeToEvents();
        
        // Set initial collapsed state
        if (metricsPanel != null)
            metricsPanel.sizeDelta = new Vector2(metricsPanel.sizeDelta.x, collapsedHeight);
    }
    
    void SetupUI()
    {
        if (satisfactionTabButton != null)
            satisfactionTabButton.onClick.AddListener(ShowSatisfactionTab);
            
        if (budgetTabButton != null)
            budgetTabButton.onClick.AddListener(ShowBudgetTab);
            
        if (exitButton != null)
            exitButton.onClick.AddListener(ClosePanel);
            
        UpdateTabColors();
    }
    
    void InitializeHistory()
    {
        currentDayHistory = new DailyMetricsHistory { day = currentDay };
        allDaysHistory.Add(currentDayHistory);
        
        if (showDebugInfo)
            Debug.Log("Metrics history initialized for Day 1");
    }
    
    void SubscribeToEvents()
    {
        if (SatisfactionAndBudget.Instance != null)
        {
            SatisfactionAndBudget.Instance.OnSatisfactionChanged += OnSatisfactionChanged;
            SatisfactionAndBudget.Instance.OnBudgetChanged += OnBudgetChanged;
        }
        
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;
            GlobalClock.Instance.OnDayChanged += OnDayChanged;
        }
    }
    
    public void ShowSatisfactionTab()
    {
        isShowingSatisfaction = true;
        
        // Update title
        if (panelTitleText != null)
            panelTitleText.text = "Satisfaction History";
        
        // Update tab colors
        UpdateTabColors();
        
        // Open panel if not already open
        if (!isPanelExpanded)
        {
            OpenPanel();
        }
        else
        {
            // Just refresh content
            RefreshCurrentTab();
        }
    }
    
    public void ShowBudgetTab()
    {
        isShowingSatisfaction = false;
        
        // Update title
        if (panelTitleText != null)
            panelTitleText.text = "Budget History";
        
        // Update tab colors
        UpdateTabColors();
        
        // Open panel if not already open
        if (!isPanelExpanded)
        {
            OpenPanel();
        }
        else
        {
            // Just refresh content
            RefreshCurrentTab();
        }
    }
    
    void UpdateTabColors()
    {
        if (satisfactionTabButton != null)
        {
            Image buttonImage = satisfactionTabButton.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = isShowingSatisfaction ? activeTabColor : inactiveTabColor;
        }
        
        if (budgetTabButton != null)
        {
            Image buttonImage = budgetTabButton.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = !isShowingSatisfaction ? activeTabColor : inactiveTabColor;
        }
    }
    
    void OpenPanel()
    {
        if (isAnimating) return;
        isPanelExpanded = true;
        RefreshCurrentTab();
        StartCoroutine(AnimatePanel(true));
    }
    
    void ClosePanel()
    {
        isPanelExpanded = false;
        
        // Immediate close without animation
        if (metricsPanel != null)
            metricsPanel.sizeDelta = new Vector2(metricsPanel.sizeDelta.x, collapsedHeight);
        
        ClearMetricItems();
        
        if (showDebugInfo)
            Debug.Log("Metrics panel closed immediately");
    }
    
    IEnumerator AnimatePanel(bool expand)
    {
        if (metricsPanel == null) yield break;
        
        isAnimating = true;
        
        float startHeight = metricsPanel.sizeDelta.y;
        float targetHeight = expand ? expandedHeight : collapsedHeight;
        float elapsed = 0f;
        
        while (elapsed < animationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / animationDuration);
            float easedT = Mathf.SmoothStep(0f, 1f, t);
            
            float currentHeight = Mathf.Lerp(startHeight, targetHeight, easedT);
            metricsPanel.sizeDelta = new Vector2(metricsPanel.sizeDelta.x, currentHeight);
            
            yield return null;
        }
        
        metricsPanel.sizeDelta = new Vector2(metricsPanel.sizeDelta.x, targetHeight);
        isAnimating = false;
    }
    
    void RefreshCurrentTab()
    {
        if (isShowingSatisfaction)
            RefreshSatisfactionHistory();
        else
            RefreshBudgetHistory();
    }
    
    void OnSatisfactionChanged(float newValue)
    {
        // Handled through RecordSatisfactionChange
    }
    
    void OnBudgetChanged(int newValue)
    {
        // Handled through RecordBudgetChange
    }
    
    public void RecordSatisfactionChange(float amount, string description)
    {
        if (currentDayHistory == null) return;
        
        MetricChangeEntry entry = new MetricChangeEntry(amount, description, currentRound, currentDay);
        currentDayHistory.satisfactionChanges.Add(entry);
        
        if (showDebugInfo)
            Debug.Log($"Recorded satisfaction change: {amount:F1} - {description}");
    }
    
    public void RecordBudgetChange(float amount, string description)
    {
        if (currentDayHistory == null) return;
        
        MetricChangeEntry entry = new MetricChangeEntry(amount, description, currentRound, currentDay);
        currentDayHistory.budgetChanges.Add(entry);
        
        if (showDebugInfo)
            Debug.Log($"Recorded budget change: {amount:F0} - {description}");
    }
    
    void RefreshSatisfactionHistory()
    {
        ClearMetricItems();
        
        if (currentDayHistory == null) return;
        
        // Show today's changes in reverse order (newest first)
        for (int i = currentDayHistory.satisfactionChanges.Count - 1; i >= 0; i--)
        {
            MetricChangeEntry entry = currentDayHistory.satisfactionChanges[i];
            CreateMetricEntryItem(entry, metricsContent, currentMetricItems);
        }
    }
    
    void RefreshBudgetHistory()
    {
        ClearMetricItems();
        
        if (currentDayHistory == null) return;
        
        // Show today's changes in reverse order (newest first)
        for (int i = currentDayHistory.budgetChanges.Count - 1; i >= 0; i--)
        {
            MetricChangeEntry entry = currentDayHistory.budgetChanges[i];
            CreateMetricEntryItem(entry, metricsContent, currentMetricItems);
        }
    }
    
    void CreateMetricEntryItem(MetricChangeEntry entry, Transform parent, List<GameObject> itemList)
    {
        if (metricEntryPrefab == null || parent == null) return;
        
        GameObject item = Instantiate(metricEntryPrefab, parent);
        
        // Get the three text components
        TextMeshProUGUI[] texts = item.GetComponentsInChildren<TextMeshProUGUI>();

        if (texts.Length >= 3)
        {
            // First text: day and round info
            texts[0].text = $"{entry.day}-{entry.round+1}";
            texts[0].color = Color.white;

            // Second text: description
            texts[1].text = entry.description;
            texts[1].color = Color.white;

            // Third text: amount with sign and color
            string sign = entry.amount >= 0 ? "+" : "";
            texts[2].text = $"{sign}{entry.amount:F0}";
            texts[2].color = entry.amount >= 0 ? positiveColor : negativeColor;
        }
        
        itemList.Add(item);
    }
    
    void ClearMetricItems()
    {
        foreach (GameObject item in currentMetricItems)
            if (item != null) Destroy(item);
        currentMetricItems.Clear();
    }
    
    void OnRoundChanged(int newRound)
    {
        currentRound = newRound;
    }
    
    void OnDayChanged(int newDay)
    {
        currentDay = newDay;
        
        // Create new day history
        currentDayHistory = new DailyMetricsHistory { day = currentDay };
        allDaysHistory.Add(currentDayHistory);
        
        if (showDebugInfo)
            Debug.Log($"Started tracking Day {currentDay} metrics history");
    }
    
    // Public API for accessing history data
    public List<DailyMetricsHistory> GetAllHistory()
    {
        return new List<DailyMetricsHistory>(allDaysHistory);
    }
    
    public DailyMetricsHistory GetDayHistory(int day)
    {
        return allDaysHistory.Find(h => h.day == day);
    }
    
    public DailyMetricsHistory GetCurrentDayHistory()
    {
        return currentDayHistory;
    }
    
    void OnDestroy()
    {
        if (SatisfactionAndBudget.Instance != null)
        {
            SatisfactionAndBudget.Instance.OnSatisfactionChanged -= OnSatisfactionChanged;
            SatisfactionAndBudget.Instance.OnBudgetChanged -= OnBudgetChanged;
        }
        
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged -= OnRoundChanged;
            GlobalClock.Instance.OnDayChanged -= OnDayChanged;
        }
    }
}