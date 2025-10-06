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
    [Header("Panel References")]
    public RectTransform satisfactionPanel;
    public RectTransform budgetPanel;
    public Button satisfactionExpandButton;
    public Button budgetExpandButton;
    
    [Header("Scroll Views")]
    public ScrollRect satisfactionScrollView;
    public Transform satisfactionContent;
    public ScrollRect budgetScrollView;
    public Transform budgetContent;
    
    [Header("Prefabs")]
    public GameObject metricEntryPrefab;
    
    [Header("Animation Settings")]
    public float expandedHeight = 400f;
    public float collapsedHeight = 0f;
    public float animationDuration = 0.3f;
    
    [Header("Colors")]
    public Color positiveColor = Color.green;
    public Color negativeColor = Color.red;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // History data storage
    private List<DailyMetricsHistory> allDaysHistory = new List<DailyMetricsHistory>();
    private DailyMetricsHistory currentDayHistory;
    
    // UI state
    private bool isSatisfactionExpanded = false;
    private bool isBudgetExpanded = false;
    private bool isAnimating = false;
    
    // Current game state
    private int currentRound = 1;
    private int currentDay = 1;
    
    // UI item tracking
    private List<GameObject> currentSatisfactionItems = new List<GameObject>();
    private List<GameObject> currentBudgetItems = new List<GameObject>();
    
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
        if (satisfactionPanel != null)
            satisfactionPanel.sizeDelta = new Vector2(satisfactionPanel.sizeDelta.x, collapsedHeight);
        if (budgetPanel != null)
            budgetPanel.sizeDelta = new Vector2(budgetPanel.sizeDelta.x, collapsedHeight);
    }
    
    void SetupUI()
    {
        if (satisfactionExpandButton != null)
            satisfactionExpandButton.onClick.AddListener(ToggleSatisfactionPanel);
            
        if (budgetExpandButton != null)
            budgetExpandButton.onClick.AddListener(ToggleBudgetPanel);
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
    
    void Update()
    {
        HandleClickOutside();
    }
    
    void HandleClickOutside()
    {
        if (!isSatisfactionExpanded && !isBudgetExpanded) return;
        
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Input.mousePosition;
            bool clickedSatisfactionButton = false;
            bool clickedBudgetButton = false;
            
            // Check if clicked on expand buttons
            if (satisfactionExpandButton != null)
            {
                RectTransform buttonRect = satisfactionExpandButton.GetComponent<RectTransform>();
                clickedSatisfactionButton = RectTransformUtility.RectangleContainsScreenPoint(
                    buttonRect, mousePos, null);
            }
            
            if (budgetExpandButton != null)
            {
                RectTransform buttonRect = budgetExpandButton.GetComponent<RectTransform>();
                clickedBudgetButton = RectTransformUtility.RectangleContainsScreenPoint(
                    buttonRect, mousePos, null);
            }
            
            // Check if clicked inside panels
            bool clickedInsideSatisfaction = false;
            bool clickedInsideBudget = false;
            
            if (isSatisfactionExpanded && satisfactionPanel != null)
            {
                clickedInsideSatisfaction = RectTransformUtility.RectangleContainsScreenPoint(
                    satisfactionPanel, mousePos, null);
            }
            
            if (isBudgetExpanded && budgetPanel != null)
            {
                clickedInsideBudget = RectTransformUtility.RectangleContainsScreenPoint(
                    budgetPanel, mousePos, null);
            }
            
            // Close panels if clicked outside
            if (isSatisfactionExpanded && !clickedInsideSatisfaction && !clickedSatisfactionButton)
            {
                ToggleSatisfactionPanel();
            }
            
            if (isBudgetExpanded && !clickedInsideBudget && !clickedBudgetButton)
            {
                ToggleBudgetPanel();
            }
        }
    }
    
    void ToggleSatisfactionPanel()
    {
        if (isAnimating) return;
        isSatisfactionExpanded = !isSatisfactionExpanded;
        
        if (isSatisfactionExpanded)
            RefreshSatisfactionHistory();
            
        StartCoroutine(AnimatePanel(satisfactionPanel, isSatisfactionExpanded));
    }
    
    void ToggleBudgetPanel()
    {
        if (isAnimating) return;
        isBudgetExpanded = !isBudgetExpanded;
        
        if (isBudgetExpanded)
            RefreshBudgetHistory();
            
        StartCoroutine(AnimatePanel(budgetPanel, isBudgetExpanded));
    }
    
    IEnumerator AnimatePanel(RectTransform panel, bool expand)
    {
        if (panel == null) yield break;
        
        isAnimating = true;
        
        float startHeight = panel.sizeDelta.y;
        float targetHeight = expand ? expandedHeight : collapsedHeight;
        float elapsed = 0f;
        
        while (elapsed < animationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / animationDuration);
            float easedT = Mathf.SmoothStep(0f, 1f, t);
            
            float currentHeight = Mathf.Lerp(startHeight, targetHeight, easedT);
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, currentHeight);
            
            yield return null;
        }
        
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, targetHeight);
        isAnimating = false;
    }
    
    void OnSatisfactionChanged(float newValue)
    {
        // This gets called AFTER the change, so we need to track the delta
        // We'll handle this through the existing AddSatisfaction/RemoveSatisfaction methods
    }
    
    void OnBudgetChanged(int newValue)
    {
        // Same as satisfaction
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
        ClearSatisfactionItems();
        
        if (currentDayHistory == null) return;
        
        // Show today's changes in reverse order (newest first)
        for (int i = currentDayHistory.satisfactionChanges.Count - 1; i >= 0; i--)
        {
            MetricChangeEntry entry = currentDayHistory.satisfactionChanges[i];
            CreateMetricEntryItem(entry, satisfactionContent, currentSatisfactionItems);
        }
    }
    
    void RefreshBudgetHistory()
    {
        ClearBudgetItems();
        
        if (currentDayHistory == null) return;
        
        // Show today's changes in reverse order (newest first)
        for (int i = currentDayHistory.budgetChanges.Count - 1; i >= 0; i--)
        {
            MetricChangeEntry entry = currentDayHistory.budgetChanges[i];
            CreateMetricEntryItem(entry, budgetContent, currentBudgetItems);
        }
    }
    
    void CreateMetricEntryItem(MetricChangeEntry entry, Transform parent, List<GameObject> itemList)
    {
        if (metricEntryPrefab == null || parent == null) return;
        
        GameObject item = Instantiate(metricEntryPrefab, parent);
        
        // Get the two text components
        TextMeshProUGUI[] texts = item.GetComponentsInChildren<TextMeshProUGUI>();
        
        if (texts.Length >= 2)
        {
            // First text: amount with color
            TextMeshProUGUI amountText = texts[0];
            string sign = entry.amount >= 0 ? "+" : "";
            amountText.text = $"{sign}{entry.amount:F0}";
            amountText.color = entry.amount >= 0 ? positiveColor : negativeColor;
            
            // Second text: description
            TextMeshProUGUI descText = texts[1];
            descText.text = entry.description;
            descText.color = Color.white;
        }
        
        itemList.Add(item);
    }
    
    void ClearSatisfactionItems()
    {
        foreach (GameObject item in currentSatisfactionItems)
            if (item != null) Destroy(item);
        currentSatisfactionItems.Clear();
    }
    
    void ClearBudgetItems()
    {
        foreach (GameObject item in currentBudgetItems)
            if (item != null) Destroy(item);
        currentBudgetItems.Clear();
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