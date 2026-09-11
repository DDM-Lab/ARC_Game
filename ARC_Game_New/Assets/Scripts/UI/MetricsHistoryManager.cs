using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Collections;
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
    public List<MetricChangeEntry> resourceEfficiencyChanges = new List<MetricChangeEntry>();
}

public enum MetricsTab
{
    Satisfaction,
    Budget,
    ResourceEfficiency
}

public class MetricsHistoryManager : MonoBehaviour
{
    [Header("Merged Panel")]
    public RectTransform metricsPanel;
    public TextMeshProUGUI panelTitleText;
    public Button exitButton;

    [Header("Modal Mask")]
    [Tooltip("Full-screen raycast-blocking image shown behind the panel while it's open, so other UI can't be clicked until Exit is pressed.")]
    public GameObject maskPanel;

    [Header("Tab Buttons")]
    public Button satisfactionTabButton;
    public Button budgetTabButton;
    public Button resourceEfficiencyTabButton;

    [Header("Scroll View (Shared)")]
    public ScrollRect metricsScrollView;
    public Transform metricsContent;

    [Header("Prefabs")]
    public GameObject metricEntryPrefab;

    [Header("Empty State")]
    [Tooltip("Shown when there are no budget entries for today; hidden as soon as there's at least one.")]
    public GameObject noEntriesText;

    [Header("Colors")]
    public Color positiveColor = Color.green;
    public Color negativeColor = Color.red;
    public Color activeTabColor = Color.green;
    public Color inactiveTabColor = Color.white;

    [Header("Live Score Sliders")]
    public Slider satisfactionTabSlider;
    public TextMeshProUGUI satisfactionTabValueText;

    public Slider resourceEfficiencyTabSlider;
    public TextMeshProUGUI resourceEfficiencyTabValueText;

    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // History data storage
    private List<DailyMetricsHistory> allDaysHistory = new List<DailyMetricsHistory>();
    private DailyMetricsHistory currentDayHistory;
    
    // UI state
    private bool isPanelExpanded = false;
    private MetricsTab currentTab = MetricsTab.Satisfaction; // Track which tab is active
    
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
        StartCoroutine(SyncInitialSliderValues());

        // Start closed - panel and mask both hidden
        if (metricsPanel != null)
            metricsPanel.gameObject.SetActive(false);

        if (maskPanel != null)
            maskPanel.SetActive(false);
    }
    
    void SetupUI()
    {
        if (satisfactionTabButton != null)
            satisfactionTabButton.onClick.AddListener(ShowSatisfactionTab);
            
        if (budgetTabButton != null)
            budgetTabButton.onClick.AddListener(ShowBudgetTab);

        if (resourceEfficiencyTabButton != null)
            resourceEfficiencyTabButton.onClick.AddListener(ShowResourceEfficiencyTab);

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

    //void SubscribeToEvents()
    //{
    //    if (SatisfactionAndBudget.Instance != null)
    //    {
    //        SatisfactionAndBudget.Instance.OnSatisfactionChanged += OnSatisfactionChanged;
    //        SatisfactionAndBudget.Instance.OnBudgetChanged += OnBudgetChanged;
    //    }

    //    if (GlobalClock.Instance != null)
    //    {
    //        GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;
    //        GlobalClock.Instance.OnDayChanged += OnDayChanged;
    //    }
    //}
    void SubscribeToEvents()
    {
        if (SatisfactionAndBudget.Instance != null)
        {
            SatisfactionAndBudget.Instance.OnSatisfactionChanged += OnSatisfactionChanged;
            SatisfactionAndBudget.Instance.OnBudgetChanged += OnBudgetChanged;
            SatisfactionAndBudget.Instance.OnEfficiencyChanged += OnEfficiencyChangedHandler; // NEW
        }

        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;
            GlobalClock.Instance.OnDayChanged += OnDayChanged;
        }
    }


    void OnDayChanged(int newDay)
    {
        currentDay = newDay;
        currentDayHistory = new DailyMetricsHistory { day = currentDay };
        allDaysHistory.Add(currentDayHistory);

        if (showDebugInfo)
            Debug.Log($"Metrics history initialized for Day {currentDay}");
    }

    public void ShowSatisfactionTab()
    {
        SwitchToTab(MetricsTab.Satisfaction, "Satisfaction History");
    }

    public void ShowBudgetTab()
    {
        SwitchToTab(MetricsTab.Budget, "Budget History");
    }

    public void ShowResourceEfficiencyTab()
    {
        SwitchToTab(MetricsTab.ResourceEfficiency, "Resource Efficiency History");
    }

    void SwitchToTab(MetricsTab tab, string title)
    {
        currentTab = tab;

        // Update title
        if (panelTitleText != null)
            panelTitleText.text = title;

        // Update tab colors
        UpdateTabColors();

        GameLogPanel.Instance?.LogUIInteraction($"Metrics panel: switched to {tab} tab | day={currentDay}");

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
                buttonImage.color = currentTab == MetricsTab.Satisfaction ? activeTabColor : inactiveTabColor;
        }

        if (budgetTabButton != null)
        {
            Image buttonImage = budgetTabButton.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = currentTab == MetricsTab.Budget ? activeTabColor : inactiveTabColor;
        }

        if (resourceEfficiencyTabButton != null)
        {
            Image buttonImage = resourceEfficiencyTabButton.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = currentTab == MetricsTab.ResourceEfficiency ? activeTabColor : inactiveTabColor;
        }
    }
    
    void OpenPanel()
    {
        isPanelExpanded = true;

        if (metricsPanel != null)
            metricsPanel.gameObject.SetActive(true);

        if (maskPanel != null)
            maskPanel.SetActive(true);

        RefreshCurrentTab();

        if (showDebugInfo)
            Debug.Log("Metrics panel opened (modal)");
    }

    void ClosePanel()
    {
        isPanelExpanded = false;

        if (metricsPanel != null)
            metricsPanel.gameObject.SetActive(false);

        if (maskPanel != null)
            maskPanel.SetActive(false);

        ClearMetricItems();
        GameLogPanel.Instance?.LogUIInteraction("Metrics panel closed");
        if (showDebugInfo)
            Debug.Log("Metrics panel closed");
    }
    
    void RefreshCurrentTab()
    {
        switch (currentTab)
        {
            case MetricsTab.Satisfaction:
                RefreshSatisfactionHistory();
                break;
            case MetricsTab.Budget:
                RefreshBudgetHistory();
                break;
            case MetricsTab.ResourceEfficiency:
                RefreshResourceEfficiencyHistory();
                break;
        }
    }

    //void OnSatisfactionChanged(float newValue)
    //{
    //    // Handled through RecordSatisfactionChange
    //}
    void OnSatisfactionChanged(float newValue)
    {
        UpdateSatisfactionTabSlider(newValue);
    }

    void OnEfficiencyChangedHandler(float newValue)
    {
        UpdateEfficiencyTabSlider(newValue);
    }

    void UpdateSatisfactionTabSlider(float value)
    {
        if (satisfactionTabSlider != null)
            satisfactionTabSlider.value = Mathf.Clamp01(value / 1000f);
        if (satisfactionTabValueText != null)
            satisfactionTabValueText.text = $"{value:F0}/1000";
    }

    void UpdateEfficiencyTabSlider(float value)
    {
        if (resourceEfficiencyTabSlider != null)
            resourceEfficiencyTabSlider.value = Mathf.Clamp01(value / 1000f);
        if (resourceEfficiencyTabValueText != null)
            resourceEfficiencyTabValueText.text = $"{value:F0}/1000";
    }

    IEnumerator SyncInitialSliderValues()
    {
        while (SatisfactionAndBudget.Instance == null)
            yield return null;

        UpdateSatisfactionTabSlider(SatisfactionAndBudget.Instance.GetCurrentSatisfaction());
        UpdateEfficiencyTabSlider(SatisfactionAndBudget.Instance.GetCurrentEfficiency());
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

    public void RecordResourceEfficiencyChange(float amount, string description)
    {
        if (currentDayHistory == null) return;

        MetricChangeEntry entry = new MetricChangeEntry(amount, description, currentRound, currentDay);
        currentDayHistory.resourceEfficiencyChanges.Add(entry);

        if (showDebugInfo)
            Debug.Log($"Recorded resource efficiency change: {amount:F1} - {description}");
    }
    
    void RefreshSatisfactionHistory()
    {
        ClearMetricItems();

        bool hasEntries = currentDayHistory != null && currentDayHistory.satisfactionChanges.Count > 0;
        if (noEntriesText != null)
            noEntriesText.SetActive(!hasEntries);

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

        bool hasEntries = currentDayHistory != null && currentDayHistory.budgetChanges.Count > 0;
        if (noEntriesText != null)
            noEntriesText.SetActive(!hasEntries);

        if (currentDayHistory == null) return;

        // Show today's changes in reverse order (newest first)
        for (int i = currentDayHistory.budgetChanges.Count - 1; i >= 0; i--)
        {
            MetricChangeEntry entry = currentDayHistory.budgetChanges[i];
            CreateMetricEntryItem(entry, metricsContent, currentMetricItems);
        }
    }

    void RefreshResourceEfficiencyHistory()
    {
        ClearMetricItems();

        bool hasEntries = currentDayHistory != null && currentDayHistory.resourceEfficiencyChanges.Count > 0;
        if (noEntriesText != null)
            noEntriesText.SetActive(!hasEntries);

        if (currentDayHistory == null) return;

        // Show today's changes in reverse order (newest first)
        for (int i = currentDayHistory.resourceEfficiencyChanges.Count - 1; i >= 0; i--)
        {
            MetricChangeEntry entry = currentDayHistory.resourceEfficiencyChanges[i];
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
            SatisfactionAndBudget.Instance.OnEfficiencyChanged -= OnEfficiencyChangedHandler; // NEW
        }

        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged -= OnRoundChanged;
            GlobalClock.Instance.OnDayChanged -= OnDayChanged;
        }
    }
}