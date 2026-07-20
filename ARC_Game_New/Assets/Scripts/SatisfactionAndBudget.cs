using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using System;

public class SatisfactionAndBudget : MonoBehaviour
{
    [Header("Satisfaction Settings")]
    [Range(0f, 100f)]
    public float currentSatisfaction = 50f;
    public float maxSatisfaction = 100f;
    public float minSatisfaction = 0f;
    
    [Header("Efficiency Settings")]
    public float currentEfficiency = 0f;

    [Header("Budget Settings")]
    public int currentBudget = 10000;
    public int maxBudget = 999999;
    public int minBudget = -999999;

    [Tooltip("When false (default), discretionary spending — construction, hiring, training, and costly task choices — is rejected before it executes if the budget can't cover it: agents cannot go into debt. Passive charges (motel upkeep, task-failure penalties) still apply and may drive the budget negative. When true, discretionary spending is allowed to go negative (for RL). Overridable at runtime via the ARC_ALLOW_NEGATIVE_BUDGET env var (1/true/yes).")]
    public bool allowNegativeBudget = false;

    // True once the initial budget/satisfaction from config (or the fallback) has been
    // applied to the live fields. Until then, currentBudget/currentSatisfaction still hold
    // the inspector defaults. Observers (e.g. the gym) should call EnsureConfigApplied()
    // before reading, so the first observation never reports the stale default.
    public bool ConfigApplied { get; private set; } = false;

    [Header("Amount Presets")]
    public float satisfactionSmallAmount = 5f;
    public float satisfactionMediumAmount = 15f;
    public float satisfactionLargeAmount = 30f;
    
    public int budgetSmallAmount = 500;
    public int budgetMediumAmount = 2000;
    public int budgetLargeAmount = 5000;
    
    [Header("Feedback Effects")]
    public BudgetSatisfactionFeedbackEffects feedbackEffects;
    
    [Header("UI References")]
    public Slider satisfactionSlider;
    public TextMeshProUGUI budgetText;
    public string budgetPrefix = "$";
    public TextMeshProUGUI satisfactionValueText;
    public Slider efficiencySlider;
    public TextMeshProUGUI efficiencyValueText;
    
    [Header("Debug")]
    public bool showDebugInfo = true;

    [Header("Config Loading")]
    public bool useExternalConfig = true;
    public GameConfigLoader configLoader;

    
    // Events for other systems to listen to
    public event Action<float> OnSatisfactionChanged;
    public event Action<int> OnBudgetChanged;
    
    // Singleton for easy access
    public static SatisfactionAndBudget Instance { get; private set; }
    
    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Runtime override of the no-debt policy (e.g. RL training). The serialized
            // field is the default; the env var, if set, wins. Accepts 1/true/yes (on)
            // and 0/false/no (off).
            string envNeg = System.Environment.GetEnvironmentVariable("ARC_ALLOW_NEGATIVE_BUDGET");
            if (!string.IsNullOrEmpty(envNeg))
            {
                string v = envNeg.Trim().ToLowerInvariant();
                if (v == "1" || v == "true" || v == "yes")
                    allowNegativeBudget = true;
                else if (v == "0" || v == "false" || v == "no")
                    allowNegativeBudget = false;
                Debug.Log($"[Budget] allowNegativeBudget overridden by ARC_ALLOW_NEGATIVE_BUDGET='{envNeg}' -> {allowNegativeBudget}");
            }
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        StartCoroutine(InitializeWithCentralConfig());
    }

    IEnumerator InitializeWithCentralConfig()
    {
        // Wait for config to load if using external config (GameConfigLoader is the
        // source of truth for the gym / benchmark / RL stack; falls back to inspector
        // values when absent). Deliberately kept over main-bugfixes' GameDataManager
        // path so headless config parity holds.
        if (useExternalConfig)
        {
            if (configLoader == null)
                configLoader = GameConfigLoader.Instance;

            if (configLoader != null)
            {
                // Wait for config to load (max 10 seconds)
                float waitTime = 0f;
                while (!configLoader.IsConfigLoaded() && waitTime < 10f)
                {
                    yield return new WaitForSeconds(0.1f);
                    waitTime += 0.1f;
                }

                // Apply loaded config (idempotent; shared with EnsureConfigApplied)
                EnsureConfigApplied();
                if (!ConfigApplied)
                    Debug.LogWarning("SatisfactionAndBudget: Config load timeout. Using inspector value.");
            }
            else
            {
                Debug.LogWarning("SatisfactionAndBudget: GameConfigLoader not found. Using inspector value.");
            }
        }

        // Mark applied regardless (no external config, missing loader, or load timeout all
        // fall back to the inspector values) so observers never wait forever.
        ConfigApplied = true;

        // Original Start() code continues here:
        InitializeValues();
        SetupFeedbackEffects();
        UpdateUI();

        if (satisfactionSlider != null)
        {
            satisfactionSlider.value = currentSatisfaction;
        }

        if (showDebugInfo)
            Debug.Log($"SatisfactionAndBudget initialized from DataManager - Budget: {currentBudget}, Sat: {currentSatisfaction}");
        GameLogPanel.Instance.LogMetricsChange($"Global Variables initialized - Satisfaction: {currentSatisfaction:F1}, Budget: {budgetPrefix}{currentBudget}");
    }

    /// <summary>
    /// Apply the initial budget/satisfaction from config to the live fields if it hasn't
    /// happened yet. Idempotent and safe to call from anywhere (e.g. the gym before building
    /// its first observation), so the first reported budget reflects the configured value
    /// rather than the stale inspector default. No-op once ConfigApplied is true. If external
    /// config is requested but not yet loaded, this leaves the fields untouched (the Start
    /// coroutine applies them once the load completes / times out).
    /// </summary>
    public void EnsureConfigApplied()
    {
        if (ConfigApplied) return;

        if (!useExternalConfig)
        {
            ConfigApplied = true;
            return;
        }

        if (configLoader == null)
            configLoader = GameConfigLoader.Instance;

        if (configLoader != null && configLoader.IsConfigLoaded())
        {
            currentBudget       = configLoader.GetInitialBudget();
            currentSatisfaction = configLoader.GetInitialSatisfaction();
            ConfigApplied       = true;
            if (showDebugInfo)
                Debug.Log($"SatisfactionAndBudget: Using config initialBudget = {currentBudget}; initialSatisfaction = {currentSatisfaction}");
        }
        // else: external config not ready yet — leave fields as-is; the coroutine will apply.
    }

    void SetupFeedbackEffects()
    {
        // Find feedback effects if not assigned
        if (feedbackEffects == null)
            feedbackEffects = FindObjectOfType<BudgetSatisfactionFeedbackEffects>();

        if (feedbackEffects != null)
        {
            feedbackEffects.SetUIReferences(satisfactionSlider, budgetText, satisfactionValueText);
            feedbackEffects.SetEfficiencyUIReferences(efficiencySlider, efficiencyValueText);
        }
    }
    
    void InitializeValues()
    {
        // Clamp initial values to valid ranges
        currentSatisfaction = Mathf.Clamp(currentSatisfaction, minSatisfaction, maxSatisfaction);
        currentBudget = Mathf.Clamp(currentBudget, minBudget, maxBudget);
        
        // Setup slider if available
        if (satisfactionSlider != null)
        {
            satisfactionSlider.minValue = minSatisfaction;
            satisfactionSlider.maxValue = maxSatisfaction;
        }
    }
    
    void UpdateUI()
    {
        // Don't update slider if feedback effects are handling it
        if (feedbackEffects == null && satisfactionSlider != null)
        {
            satisfactionSlider.value = currentSatisfaction;
        }

        if (feedbackEffects == null && efficiencySlider != null)
        {
            efficiencySlider.value = currentEfficiency;
        }

        // Always update budget text
        if (budgetText != null)
        {
            budgetText.text = budgetPrefix + currentBudget.ToString("N0");
        }

        UpdateSatisfactionValueText();
        UpdateEfficiencyValueText();
    }

    public void ForceRefreshUI()
    {
        if (satisfactionSlider != null)
            satisfactionSlider.value = currentSatisfaction;

        if (efficiencySlider != null)
            efficiencySlider.value = currentEfficiency;

        if (budgetText != null)
        {
            budgetText.text = budgetPrefix + currentBudget.ToString("N0");
        }

        UpdateSatisfactionValueText();
        UpdateEfficiencyValueText();
    }

    // ===== SATISFACTION METHODS =====

    /// <summary>
    /// Add satisfaction with custom description
    /// </summary>
    public void AddSatisfaction(float amount, string description = "")
    {
        float previousValue = currentSatisfaction;
        currentSatisfaction += amount;

        // Use default description if none provided
        if (string.IsNullOrEmpty(description))
        {
            description = GetDefaultSatisfactionDescription(amount);
        }

        // Record in history
        if (MetricsHistoryManager.Instance != null)
        {
            MetricsHistoryManager.Instance.RecordSatisfactionChange(amount, description);
        }

        // Show feedback effects
        if (feedbackEffects != null && Mathf.Abs(amount) > 0.01f)
        {
            feedbackEffects.ShowSatisfactionChange(previousValue, currentSatisfaction, maxSatisfaction);
        }

        // Update UI
        if (feedbackEffects == null)
        {
            UpdateUI();
        }
        else
        {
            if (budgetText != null)
                budgetText.text = budgetPrefix + currentBudget.ToString("N0");
        }

        // Always update satisfaction text regardless of feedback effects
        UpdateSatisfactionValueText();

        OnSatisfactionChanged?.Invoke(currentSatisfaction);

        if (showDebugInfo)
            Debug.Log($"Satisfaction: {previousValue:F1} → {currentSatisfaction:F1} (+{amount:F1}) - {description}");
        GameLogPanel.Instance.LogMetricsChange($"Satisfaction: {previousValue:F1} → {currentSatisfaction:F1} (+{amount:F1}) - {description}");
    }
    
    /// <summary>
    /// Get default description for satisfaction changes - EASY TO CUSTOMIZE
    /// </summary>
    private string GetDefaultSatisfactionDescription(float amount)
    {
        int currentRound = (GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentTimeSegment() : 1) + 1;
        
        if (amount > 0)
            return $"Round {currentRound} - Positive action";
        else if (amount < 0)
            return $"Round {currentRound} - Negative event";
        else
            return $"Round {currentRound}";
    }
    
    /// <summary>
    /// Remove specific amount from satisfaction
    /// </summary>
    public void RemoveSatisfaction(float amount, string description = "")
    {
        AddSatisfaction(-amount, description);
    }
    
    /// <summary>
    /// Add small amount to satisfaction
    /// </summary>
    public void AddSatisfactionSmall()
    {
        AddSatisfaction(satisfactionSmallAmount);
    }
    
    /// <summary>
    /// Add medium amount to satisfaction
    /// </summary>
    public void AddSatisfactionMedium()
    {
        AddSatisfaction(satisfactionMediumAmount);
    }
    
    /// <summary>
    /// Add large amount to satisfaction
    /// </summary>
    public void AddSatisfactionLarge()
    {
        AddSatisfaction(satisfactionLargeAmount);
    }
    
    /// <summary>
    /// Remove small amount from satisfaction
    /// </summary>
    public void RemoveSatisfactionSmall()
    {
        RemoveSatisfaction(satisfactionSmallAmount);
    }
    
    /// <summary>
    /// Remove medium amount from satisfaction
    /// </summary>
    public void RemoveSatisfactionMedium()
    {
        RemoveSatisfaction(satisfactionMediumAmount);
    }
    
    /// <summary>
    /// Remove large amount from satisfaction
    /// </summary>
    public void RemoveSatisfactionLarge()
    {
        RemoveSatisfaction(satisfactionLargeAmount);
    }

    /// <summary>
    /// Set satisfaction to specific value
    /// </summary>
    public void SetSatisfaction(float value)
    {
        float previousValue = currentSatisfaction;
        currentSatisfaction = Mathf.Clamp(value, minSatisfaction, maxSatisfaction);

        UpdateUI();
        OnSatisfactionChanged?.Invoke(currentSatisfaction);

        if (showDebugInfo)
            Debug.Log($"Satisfaction set: {previousValue:F1} → {currentSatisfaction:F1}");
        GameLogPanel.Instance.LogMetricsChange($"Satisfaction set: {previousValue:F1} → {currentSatisfaction:F1}");
    }

    void UpdateSatisfactionValueText()
    {
        if (satisfactionValueText != null)
            satisfactionValueText.text = $"{currentSatisfaction:F1}";
    }

    void UpdateEfficiencyValueText()
    {
        if (efficiencyValueText != null)
            efficiencyValueText.text = $"{currentEfficiency:F1}";
    }

    // ===== EFFICIENCY METHODS =====

    /// <summary>
    /// Add resource allocation efficiency score (called by DailyReportUI at end of day).
    /// </summary>
    public void AddEfficiency(float amount, string description = "")
    {
        float previousValue = currentEfficiency;
        currentEfficiency += amount;

        // Show feedback effects
        if (feedbackEffects != null && Mathf.Abs(amount) > 0.01f)
        {
            feedbackEffects.ShowEfficiencyChange(previousValue, currentEfficiency);
        }

        // Update text directly; slider is animated by feedback effects
        UpdateEfficiencyValueText();
        if (feedbackEffects == null && efficiencySlider != null)
            efficiencySlider.value = currentEfficiency;

        if (showDebugInfo)
            Debug.Log($"Efficiency: {previousValue:F1} → {currentEfficiency:F1} ({amount:+0.0;-0.0}) - {description}");
        GameLogPanel.Instance?.LogMetricsChange($"Efficiency: {previousValue:F1} → {currentEfficiency:F1} ({amount:+0.0;-0.0}) - {description}");
    }

    public float GetCurrentEfficiency() => currentEfficiency;

    // ===== BUDGET METHODS =====

    /// <summary>
    /// Add budget with custom description
    /// </summary>
    public void AddBudget(int amount, string description = "")
    {
        int previousValue = currentBudget;
        currentBudget = Mathf.Clamp(currentBudget + amount, minBudget, maxBudget);

        // Use default description if none provided
        if (string.IsNullOrEmpty(description))
        {
            description = GetDefaultBudgetDescription(amount);
        }

        // Record in history
        if (MetricsHistoryManager.Instance != null)
        {
            MetricsHistoryManager.Instance.RecordBudgetChange(amount, description);
        }

        // Show feedback effects
        if (feedbackEffects != null && amount != 0)
        {
            feedbackEffects.ShowBudgetChange(previousValue, currentBudget);
        }

        // Update UI
        if (budgetText != null)
        {
            budgetText.text = budgetPrefix + currentBudget.ToString("N0");
        }

        OnBudgetChanged?.Invoke(currentBudget);

        if (showDebugInfo)
            Debug.Log($"Budget: {budgetPrefix}{previousValue:N0} → {budgetPrefix}{currentBudget:N0} (+{budgetPrefix}{amount:N0}) - {description}");
        GameLogPanel.Instance.LogMetricsChange($"Budget: {budgetPrefix}{previousValue:N0} → {budgetPrefix}{currentBudget:N0} (+{budgetPrefix}{amount:N0}) - {description}");
    }

    /// <summary>
    /// Get default description for budget changes - EASY TO CUSTOMIZE
    /// </summary>
    private string GetDefaultBudgetDescription(int amount)
    {
        int currentRound = (GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentTimeSegment() : 1 ) + 1;
        
        if (amount > 0)
            return $"Round {currentRound} - Income";
        else if (amount < 0)
            return $"Round {currentRound} - Expense";
        else
            return $"Round {currentRound}";
    }
    
    /// <summary>
    /// Remove specific amount from budget
    /// </summary>
    public void RemoveBudget(int amount, string description = "")
    {
        AddBudget(-amount, description);
    }

    // ── Cumulative spend by category (for the cost-efficiency reward metric) ──
    public enum SpendCategory { Other, Food, Lodging, Worker, Casework }
    private int cumFoodSpend = 0, cumLodgingSpend = 0, cumWorkerSpend = 0, cumCaseworkSpend = 0;
    public int CumulativeFoodSpend => cumFoodSpend;
    public int CumulativeLodgingSpend => cumLodgingSpend;
    public int CumulativeWorkerSpend => cumWorkerSpend;
    public int CumulativeCaseworkSpend => cumCaseworkSpend;

    /// <summary>
    /// Spend attributed to a service category so Python can compute cost
    /// efficiency. Food = kitchen construction + food-choice costs; Lodging =
    /// shelter construction + motel charges + lodging-choice costs; Worker =
    /// request + training costs. Everything else stays Other (excluded).
    /// </summary>
    public void RemoveBudget(int amount, SpendCategory category, string description = "")
    {
        if (amount > 0)
        {
            if (category == SpendCategory.Food) cumFoodSpend += amount;
            else if (category == SpendCategory.Lodging) cumLodgingSpend += amount;
            else if (category == SpendCategory.Worker) cumWorkerSpend += amount;
            else if (category == SpendCategory.Casework) cumCaseworkSpend += amount;
        }
        AddBudget(-amount, description);
    }
    
    /// <summary>
    /// Add small amount to budget
    /// </summary>
    public void AddBudgetSmall()
    {
        AddBudget(budgetSmallAmount);
    }
    
    /// <summary>
    /// Add medium amount to budget
    /// </summary>
    public void AddBudgetMedium()
    {
        AddBudget(budgetMediumAmount);
    }
    
    /// <summary>
    /// Add large amount to budget
    /// </summary>
    public void AddBudgetLarge()
    {
        AddBudget(budgetLargeAmount);
    }
    
    /// <summary>
    /// Remove small amount from budget
    /// </summary>
    public void RemoveBudgetSmall()
    {
        RemoveBudget(budgetSmallAmount);
    }
    
    /// <summary>
    /// Remove medium amount from budget
    /// </summary>
    public void RemoveBudgetMedium()
    {
        RemoveBudget(budgetMediumAmount);
    }
    
    /// <summary>
    /// Remove large amount from budget
    /// </summary>
    public void RemoveBudgetLarge()
    {
        RemoveBudget(budgetLargeAmount);
    }

    /// <summary>
    /// Set budget to specific value
    /// </summary>
    public void SetBudget(int value)
    {
        int previousValue = currentBudget;
        currentBudget = Mathf.Clamp(value, minBudget, maxBudget);

        UpdateUI();
        OnBudgetChanged?.Invoke(currentBudget);

        if (showDebugInfo)
            Debug.Log($"Budget set: {budgetPrefix}{previousValue:N0} → {budgetPrefix}{currentBudget:N0}");
        GameLogPanel.Instance.LogMetricsChange($"Budget set: {budgetPrefix}{previousValue:N0} → {budgetPrefix}{currentBudget:N0}");
    }
    
    /// <summary>
    /// Check if budget is sufficient for a purchase
    /// </summary>
    public bool CanAfford(int cost)
    {
        return currentBudget >= cost;
    }

    /// <summary>
    /// Whether a discretionary spend of <paramref name="cost"/> is permitted right now.
    /// Honors the no-debt policy: when allowNegativeBudget is false, the spend is only
    /// permitted if the budget can cover it (CanAfford); when true, it is always permitted
    /// (the budget is allowed to go negative, e.g. for RL). This is the single gate that
    /// all discretionary spend sites (construction, hiring, training, costly task choices)
    /// should consult before charging. Passive charges (motel upkeep, task-failure
    /// penalties) must NOT use this gate — they always apply.
    /// </summary>
    public bool WouldAllowSpend(int cost)
    {
        return allowNegativeBudget || CanAfford(cost);
    }

    /// <summary>
    /// Try to spend budget (returns true if successful)
    /// </summary>
    public bool TrySpendBudget(int cost)
    {
        if (CanAfford(cost))
        {
            RemoveBudget(cost);
            return true;
        }
        
        if (showDebugInfo)
            Debug.LogWarning($"Cannot afford {budgetPrefix}{cost:N0} - Current budget: {budgetPrefix}{currentBudget:N0}");
        GameLogPanel.Instance.LogDebug($"Cannot afford {budgetPrefix}{cost:N0} - Current budget: {budgetPrefix}{currentBudget:N0}");
        return false;
    }
    
    // ===== GETTER METHODS =====
    
    public float GetSatisfaction()
    {
        return currentSatisfaction;
    }
    
    public float GetSatisfactionPercentage()
    {
        return (currentSatisfaction / maxSatisfaction) * 100f;
    }
    
    public int GetBudget()
    {
        return currentBudget;
    }
    
    public bool IsSatisfactionLow()
    {
        return currentSatisfaction < (maxSatisfaction * 0.3f); // Below 30%
    }
    
    public bool IsSatisfactionHigh()
    {
        return currentSatisfaction > (maxSatisfaction * 0.8f); // Above 80%
    }
    
    public bool IsBudgetLow()
    {
        return currentBudget < (budgetSmallAmount * 2); // Less than 2 small amounts
    }

    public float GetCurrentSatisfaction()
    {
        return currentSatisfaction;
    }

    public int GetCurrentBudget()
    {
        return currentBudget;
    }

    // ===== DEBUG METHODS =====

    [ContextMenu("Add Satisfaction Small")]
    public void DebugAddSatisfactionSmall()
    {
        AddSatisfactionSmall();
    }
    
    [ContextMenu("Remove Satisfaction Small")]
    public void DebugRemoveSatisfactionSmall()
    {
        RemoveSatisfactionSmall();
    }

    [ContextMenu("Add Satisfaction Medium")]
    public void DebugAddSatisfactionMedium()
    {
        AddSatisfactionMedium();
    }
    [ContextMenu("Remove Satisfaction Medium")]
    public void DebugRemoveSatisfactionMedium()
    {
        RemoveSatisfactionMedium();
    }
    [ContextMenu("Add Satisfaction Large")]
    public void DebugAddSatisfactionLarge()
    {
        AddSatisfactionLarge();
    }
    [ContextMenu("Remove Satisfaction Large")] 
    public void DebugRemoveSatisfactionLarge()
    {
        RemoveSatisfactionLarge();
    }

    [ContextMenu("Add Budget Small")]
    public void DebugAddBudgetSmall()
    {
        AddBudgetSmall();
    }
    
    [ContextMenu("Remove Budget Small")]
    public void DebugRemoveBudgetSmall()
    {
        RemoveBudgetSmall();
    }
    [ContextMenu("Add Budget Medium")]
    public void DebugAddBudgetMedium()
    {
        AddBudgetMedium();
    }
    [ContextMenu("Remove Budget Medium")]
    public void DebugRemoveBudgetMedium()
    {
        RemoveBudgetMedium();
    }
    [ContextMenu("Add Budget Large")]
    public void DebugAddBudgetLarge()
    {
        AddBudgetLarge();
    }
    [ContextMenu("Remove Budget Large")]
    public void DebugRemoveBudgetLarge()
    {
        RemoveBudgetLarge();
    }
    
    [ContextMenu("Print Current Values")]
    public void DebugPrintValues()
    {
        Debug.Log($"=== GLOBAL VARIABLES ===");
        Debug.Log($"Satisfaction: {currentSatisfaction:F1}/{maxSatisfaction} ({GetSatisfactionPercentage():F1}%)");
        Debug.Log($"Budget: {budgetPrefix}{currentBudget:N0}");
        Debug.Log($"Satisfaction Status: {(IsSatisfactionLow() ? "LOW" : IsSatisfactionHigh() ? "HIGH" : "NORMAL")}");
        Debug.Log($"Budget Status: {(IsBudgetLow() ? "LOW" : "NORMAL")}");
    }
}