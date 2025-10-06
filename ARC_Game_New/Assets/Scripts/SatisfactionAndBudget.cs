using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class SatisfactionAndBudget : MonoBehaviour
{
    [Header("Satisfaction Settings")]
    [Range(0f, 100f)]
    public float currentSatisfaction = 50f;
    public float maxSatisfaction = 100f;
    public float minSatisfaction = 0f;
    
    [Header("Budget Settings")]
    public int currentBudget = 10000;
    public int maxBudget = 999999;
    public int minBudget = 0;
    
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
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
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
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        InitializeValues();
        SetupFeedbackEffects();
        UpdateUI();

        if (showDebugInfo)
            Debug.Log($"Global Variables initialized - Satisfaction: {currentSatisfaction:F1}, Budget: {budgetPrefix}{currentBudget}");
        GameLogPanel.Instance.LogMetricsChange($"Global Variables initialized - Satisfaction: {currentSatisfaction:F1}, Budget: {budgetPrefix}{currentBudget}");
    }
    
    void SetupFeedbackEffects()
    {
        // Find feedback effects if not assigned
        if (feedbackEffects == null)
            feedbackEffects = FindObjectOfType<BudgetSatisfactionFeedbackEffects>();
        
        // Set UI references for feedback effects
        if (feedbackEffects != null)
        {
            feedbackEffects.SetUIReferences(satisfactionSlider, budgetText);
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
        
        // Always update budget text
        if (budgetText != null)
        {
            budgetText.text = budgetPrefix + currentBudget.ToString("N0");
        }
    }

    // ===== SATISFACTION METHODS =====

    /// <summary>
    /// Add specific amount to satisfaction
    /// </summary>
    public void AddSatisfaction(float amount)
    {
        float previousValue = currentSatisfaction;
        currentSatisfaction = Mathf.Clamp(currentSatisfaction + amount, minSatisfaction, maxSatisfaction);

        // Record this change in history
        if (MetricsHistoryManager.Instance != null)
        {
            string description = $"Round {(GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentTimeSegment() : 1)}";
            MetricsHistoryManager.Instance.RecordSatisfactionChange(amount, description);
        }

        // Show feedback effects
        if (feedbackEffects != null && Mathf.Abs(amount) > 0.01f)
        {
            feedbackEffects.ShowSatisfactionChange(previousValue, currentSatisfaction, maxSatisfaction);
        }

        // Update UI (but don't update slider directly if using feedback effects)
        if (feedbackEffects == null)
        {
            UpdateUI();
        }
        else
        {
            // Only update budget text, slider will be animated by feedback effects
            if (budgetText != null)
            {
                budgetText.text = budgetPrefix + currentBudget.ToString("N0");
            }
        }

        OnSatisfactionChanged?.Invoke(currentSatisfaction);

        if (showDebugInfo)
            Debug.Log($"Satisfaction: {previousValue:F1} → {currentSatisfaction:F1} (+{amount:F1})");
        GameLogPanel.Instance.LogMetricsChange($"Satisfaction: {previousValue:F1} → {currentSatisfaction:F1} (+{amount:F1})");
    }
    
    /// <summary>
    /// Remove specific amount from satisfaction
    /// </summary>
    public void RemoveSatisfaction(float amount)
    {
        AddSatisfaction(-amount);
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

    // ===== BUDGET METHODS =====

    /// <summary>
    /// Add specific amount to budget
    /// </summary>
    public void AddBudget(int amount)
    {
        int previousValue = currentBudget;
        currentBudget = Mathf.Clamp(currentBudget + amount, minBudget, maxBudget);
        // Record this change in history
        if (MetricsHistoryManager.Instance != null)
        {
            string description = $"Round {(GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentTimeSegment() : 1)}";
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
            Debug.Log($"Budget: {budgetPrefix}{previousValue:N0} → {budgetPrefix}{currentBudget:N0} (+{budgetPrefix}{amount:N0})");
        GameLogPanel.Instance.LogMetricsChange($"Budget: {budgetPrefix}{previousValue:N0} → {budgetPrefix}{currentBudget:N0} (+{budgetPrefix}{amount:N0})");
    }
    
    /// <summary>
    /// Remove specific amount from budget
    /// </summary>
    public void RemoveBudget(int amount)
    {
        AddBudget(-amount);
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

    public float GetCurrentBudget()
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