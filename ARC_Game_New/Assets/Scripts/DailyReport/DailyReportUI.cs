using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class SectionElement
{
    public GameObject layoutObject;
    public TextMeshProUGUI numberText;
    public TextMeshProUGUI labelText;
    public TextMeshProUGUI sentenceText;
    public CanvasGroup canvasGroup;
}

public class DailyReportUI : MonoBehaviour
{
    [Header("Systems References")]
    public DeliverySystem deliverySystem;

    [Header("Satisfaction Panel Sections")]
    public TextMeshProUGUI currentDayDisplay;
    [Header("Food Delivery Section")]
    public SectionElement foodDeliveryTotal;
    public SectionElement foodDeliveryStatus;
    public SectionElement foodCompletionBonus;
    public SectionElement foodOnTimeBonus;
    public SectionElement foodDelayScore;

    [Header("Lodging Section")]
    public SectionElement lodgingTotal;
    public SectionElement lodgingStatus;
    public SectionElement lodgingCompletionBonus;
    public SectionElement lodgingOverstayPenalty;

    [Header("Worker Section")]
    public SectionElement workerTotal;
    public SectionElement workerStatus;
    public SectionElement workerTaskBonus;
    public SectionElement workerIdleRate;

    [Header("Efficiency Panel Sections")]
    [Header("Food Utilization Section")]
    public SectionElement foodUtilizationTotal;
    public SectionElement foodUsageSummary;
    public SectionElement kitchenEfficiencyScore;

    [Header("Shelter Utilization Section")]
    public SectionElement shelterUtilizationTotal;
    public SectionElement shelterUsageSummary;
    public SectionElement shelterEfficiencyScore;

    [Header("Worker Utilization Section")]
    public SectionElement workerUtilizationTotal;
    public SectionElement workerUsageSummary;
    public SectionElement workerEfficiencyScore;

    [Header("Budget Efficiency Section")]
    public SectionElement budgetEfficiencyTotal;
    public SectionElement budgetUsageSummary;
    public SectionElement budgetEfficiencyScore;

    [Header("Colors")]
    public Color positiveChangeColor = new Color(70f / 255f, 149f / 255f, 67f / 255f); //#469543
    public Color negativeChangeColor = new Color(222f / 255f, 83f / 255f, 48f / 255f); //#DE5330

    [Header("Final Animation Sections")]
    [Tooltip("Section that shows overall satisfaction percentage, change amount, and animated progress bar")]
    public TextMeshProUGUI satisfactionValueText;
    [Tooltip("Text showing satisfaction change like '+5.2' or '-3.1'")]
    public TextMeshProUGUI satisfactionChangeText;
    [Tooltip("Animated progress bar for satisfaction level")]
    public Slider satisfactionBar;
    [Tooltip("CanvasGroup for the entire satisfaction summary section")]
    public CanvasGroup satisfactionAnimationSection;

    [Tooltip("Section that shows overall efficiency percentage, change amount, and animated progress bar")]
    public TextMeshProUGUI efficiencyValueText;
    [Tooltip("Text showing efficiency change like '+2.8' or '-1.5'")]
    public TextMeshProUGUI efficiencyChangeText;
    [Tooltip("Animated progress bar for efficiency level")]
    public Slider efficiencyBar;
    [Tooltip("CanvasGroup for the entire efficiency summary section")]
    public CanvasGroup efficiencyAnimationSection;

    [Header("Animation Settings")]
    public float elementAnimationDelay = 0.4f;
    public float elementFadeInDuration = 0.3f;
    public float numberCountDuration = 0.8f;
    public float satisfactionAnimationDuration = 1f;
    public float barAnimationDuration = 1.5f;

    [Header("Bottom Panel - What We Did Today")]
    public TextMeshProUGUI tasksCompletedText;
    public TextMeshProUGUI facilitiesConstructedText;
    public TextMeshProUGUI moneySpentText;
    public TextMeshProUGUI workersHiredText;
    public TextMeshProUGUI workersTrainedText;

    [Header("Bottom Panel - Today's Data")]
    public TextMeshProUGUI totalInfluencedResidentsText;
    public TextMeshProUGUI foodTaskRatioText;
    public TextMeshProUGUI lodgingTaskRatioText;
    public TextMeshProUGUI caseworkTaskRatioText;
    public TextMeshProUGUI emergencyTaskRatioText;

    private DailyReportMetrics currentMetrics;

    // Running satisfaction/efficiency values that persist across days
    private float currentSatisfaction = 50f;
    private float currentEfficiency = 80f;
    
    // Track whether we're currently animating (to prevent save corruption)
    private bool isAnimating = false;

    void Start()
    {
        InitializeElements();

        // Hide final animation sections initially
        if (satisfactionAnimationSection != null)
            satisfactionAnimationSection.alpha = 0f;
        if (efficiencyAnimationSection != null)
            efficiencyAnimationSection.alpha = 0f;
    }

    void InitializeElements()
    {
        InitializeSectionElement(foodDeliveryTotal);
        InitializeSectionElement(foodDeliveryStatus);
        InitializeSectionElement(foodCompletionBonus);
        InitializeSectionElement(foodOnTimeBonus);
        InitializeSectionElement(foodDelayScore);

        InitializeSectionElement(lodgingTotal);
        InitializeSectionElement(lodgingStatus);
        InitializeSectionElement(lodgingCompletionBonus);
        InitializeSectionElement(lodgingOverstayPenalty);

        InitializeSectionElement(workerTotal);
        InitializeSectionElement(workerStatus);
        InitializeSectionElement(workerTaskBonus);
        InitializeSectionElement(workerIdleRate);

        InitializeSectionElement(foodUtilizationTotal);
        InitializeSectionElement(foodUsageSummary);
        InitializeSectionElement(kitchenEfficiencyScore);

        InitializeSectionElement(shelterUtilizationTotal);
        InitializeSectionElement(shelterUsageSummary);
        InitializeSectionElement(shelterEfficiencyScore);

        InitializeSectionElement(workerUtilizationTotal);
        InitializeSectionElement(workerUsageSummary);
        InitializeSectionElement(workerEfficiencyScore);

        InitializeSectionElement(budgetEfficiencyTotal);
        InitializeSectionElement(budgetUsageSummary);
        InitializeSectionElement(budgetEfficiencyScore);
    }

    void InitializeSectionElement(SectionElement element)
    {
        if (element.layoutObject == null) return;

        if (element.canvasGroup == null)
        {
            element.canvasGroup = element.layoutObject.GetComponent<CanvasGroup>();
            if (element.canvasGroup == null)
                element.canvasGroup = element.layoutObject.AddComponent<CanvasGroup>();
        }

        element.canvasGroup.alpha = 0f;
    }

    // ================================================================
    // PUBLIC ENTRY POINTS
    // ================================================================

    /// <summary>
    /// Display a new daily report WITH animations. Called for the current day's report.
    /// After all animations complete, calculated scores are saved to history.
    /// </summary>
    public void DisplayDailyReport(DailyReportMetrics metrics)
    {
        currentMetrics = metrics;
        currentDayDisplay.text = GlobalClock.Instance.currentDay.ToString();
        UpdateBottomPanels(metrics);
        StartCoroutine(AnimateReportDisplay());
    }

    /// <summary>
    /// Display a historical report INSTANTLY without animations.
    /// Uses ONLY stored calculated values from metrics - NO recalculation.
    /// 
    /// FIX: Removed the premature SaveCompletedReportToHistory() call that was here.
    /// The old code would save whatever was in currentMetrics to history tagged with
    /// the CURRENT GlobalClock day, which corrupted data when browsing historical reports
    /// (e.g., viewing Day 2 then clicking Day 3 would save Day 2's data as the current day).
    /// </summary>
    public void DisplayDailyReportImmediate(DailyReportMetrics metrics, int dayNumber)
    {
        // Stop any running animations (they may be mid-flight for current day)
        StopAllCoroutines();
        isAnimating = false;
        
        currentMetrics = metrics;
        
        // Set day display
        if (currentDayDisplay != null)
        {
            currentDayDisplay.text = dayNumber.ToString();
        }
        
        // Use stored final values - don't overwrite running satisfaction/efficiency
        // for historical views, just display what was stored
        float displaySatisfaction = metrics.finalSatisfactionValue;
        float displayEfficiency = metrics.finalEfficiencyValue;
        
        // Populate ALL sections from stored data (no recalculation)
        UpdateBottomPanels(metrics);
        SetAllStoredSectionValues(metrics);
        SetFinalValuesFromStoredMetrics(metrics, displaySatisfaction, displayEfficiency);
        SetAllElementsVisible();
    }

    // ================================================================
    // BOTTOM PANELS (raw metric display, same for live and historical)
    // ================================================================

    public void UpdateBottomPanels(DailyReportMetrics metrics)
    {
        // What We Did Today
        if (tasksCompletedText != null)
            tasksCompletedText.text = metrics.completedTasks.ToString();
        
        if (facilitiesConstructedText != null)
            facilitiesConstructedText.text = metrics.buildingsConstructed.ToString();
        
        if (moneySpentText != null)
            moneySpentText.text = $"${metrics.budgetSpent:F0}";
        
        if (workersHiredText != null)
            workersHiredText.text = metrics.newWorkersHired.ToString();
        
        if (workersTrainedText != null)
            workersTrainedText.text = metrics.workersInTraining.ToString();
        
        // Today's Data - task type ratios
        if (totalInfluencedResidentsText != null)
            totalInfluencedResidentsText.text = metrics.totalInfluencedResidents.ToString();
        
        if (foodTaskRatioText != null)
            foodTaskRatioText.text = $"{metrics.completedFoodTasks}/{metrics.totalFoodTasks}";
        
        if (lodgingTaskRatioText != null)
            lodgingTaskRatioText.text = $"{metrics.completedLodgingTasks}/{metrics.totalLodgingTasks}";
        
        if (caseworkTaskRatioText != null)
            caseworkTaskRatioText.text = $"{metrics.completedCaseworkTasks}/{metrics.totalCaseworkTasks}";
        
        if (emergencyTaskRatioText != null)
            emergencyTaskRatioText.text = $"{metrics.completedEmergencyTasks}/{metrics.totalEmergencyTasks}";
    }

    // ================================================================
    // ANIMATED DISPLAY (current day report)
    // ================================================================

    IEnumerator AnimateReportDisplay()
    {
        isAnimating = true;
        
        // Step 1: Animate satisfaction panel sections one by one
        yield return StartCoroutine(DisplaySatisfactionSections());

        // Step 2: Animate efficiency panel sections one by one
        yield return StartCoroutine(DisplayEfficiencySections());

        // Step 3: Show final satisfaction score with animated bar
        yield return StartCoroutine(AnimateFinalSatisfactionChanges());

        // Step 4: Show final efficiency score with animated bar
        yield return StartCoroutine(AnimateFinalEfficiencyChanges());
        
        // Step 5: SAVE all calculated values to history AFTER everything is done
        isAnimating = false;
        SaveCompletedReportToHistory();
    }

    /// <summary>
    /// Save the current report with all calculated scores to history.
    /// Called ONLY after animations complete for the current day.
    /// </summary>
    void SaveCompletedReportToHistory()
    {
        if (DailyReportData.Instance == null || currentMetrics == null)
            return;
        
        // Store all individual score components that were displayed during animation
        currentMetrics.foodCompletionBonus = CalculateFoodCompletionBonus();
        currentMetrics.foodOnTimeBonus = CalculateFoodOnTimeBonus();
        currentMetrics.foodDelayScore = CalculateFoodDelayScore();
        currentMetrics.lodgingCompletionBonus = CalculateLodgingCompletionBonus();
        currentMetrics.lodgingOverstayPenalty = CalculateLodgingOverstayPenalty();
        currentMetrics.workerTaskBonus = CalculateWorkerTaskBonus();
        currentMetrics.workerIdleRatePenalty = CalculateWorkerIdleRateDisplay();
        
        currentMetrics.kitchenEfficiencyScore = CalculateKitchenEfficiencyScore();
        currentMetrics.shelterEfficiencyScore = CalculateShelterEfficiencyScore();
        currentMetrics.workerEfficiencyScore = CalculateWorkerUtilizationScore();
        currentMetrics.budgetEfficiencyScore = CalculateBudgetEfficiencyScore();
        
        // Store aggregate satisfaction/efficiency breakdown totals
        currentMetrics.foodSatisfaction = CalculateFoodSatisfactionTotal();
        currentMetrics.lodgingSatisfaction = CalculateLodgingSatisfactionTotal();
        currentMetrics.workerSatisfaction = CalculateWorkerSatisfactionTotal();
        currentMetrics.foodEfficiency = currentMetrics.kitchenEfficiencyScore;
        currentMetrics.shelterEfficiency = currentMetrics.shelterEfficiencyScore;
        currentMetrics.workerEfficiency = currentMetrics.workerEfficiencyScore;
        currentMetrics.budgetEfficiency = currentMetrics.budgetEfficiencyScore;
        
        // Store final animated values
        currentMetrics.finalSatisfactionValue = currentSatisfaction;
        currentMetrics.finalEfficiencyValue = currentEfficiency;
        currentMetrics.satisfactionChangeCalculated = CalculateSatisfactionScore();
        
        // Save to history keyed by current day
        int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;
        DailyReportData.Instance.SaveReportToHistory(currentDay, currentMetrics);
        
        Debug.Log($"Saved completed report for Day {currentDay} | Satisfaction: {currentSatisfaction:F1}% | Efficiency: {currentEfficiency:F1}%");
    }

    // ================================================================
    // SATISFACTION ANIMATION SECTIONS
    // ================================================================

    IEnumerator DisplaySatisfactionSections()
    {
        // --- Food Delivery Section ---
        yield return StartCoroutine(AnimateSectionElement(foodDeliveryTotal, CalculateFoodSatisfactionTotal(), "Timely Food Delivery"));
        yield return StartCoroutine(AnimateSectionElement(foodDeliveryStatus, GenerateFoodDeliveryStatusText()));
        yield return StartCoroutine(AnimateSectionElement(foodCompletionBonus, CalculateFoodCompletionBonus(), "Task Completion Bonus:"));
        yield return StartCoroutine(AnimateSectionElement(foodOnTimeBonus, CalculateFoodOnTimeBonus(), "On-Time Bonus:"));
        yield return StartCoroutine(AnimateSectionElement(foodDelayScore, CalculateFoodDelayScore(), "Task Delay Score:"));

        // --- Lodging Section ---
        yield return StartCoroutine(AnimateSectionElement(lodgingTotal, CalculateLodgingSatisfactionTotal(), "Lodging Services"));
        yield return StartCoroutine(AnimateSectionElement(lodgingStatus, GenerateLodgingStatusText()));
        yield return StartCoroutine(AnimateSectionElement(lodgingCompletionBonus, CalculateLodgingCompletionBonus(), "Task Completion Bonus:"));
        yield return StartCoroutine(AnimateSectionElement(lodgingOverstayPenalty, CalculateLodgingOverstayPenalty(), GenerateOverstayText()));

        // --- Worker Section ---
        yield return StartCoroutine(AnimateSectionElement(workerTotal, CalculateWorkerSatisfactionTotal(), "Worker Management"));
        yield return StartCoroutine(AnimateSectionElement(workerStatus, GenerateWorkerStatusText()));
        yield return StartCoroutine(AnimateSectionElement(workerTaskBonus, CalculateWorkerTaskBonus(), "Worker Task Bonus:"));
        yield return StartCoroutine(AnimateSectionElement(workerIdleRate, CalculateWorkerIdleRateDisplay(), $"Idle Workers: {currentMetrics.idleWorkerRate:F1}%"));
    }

    // ================================================================
    // EFFICIENCY ANIMATION SECTIONS
    // ================================================================

    IEnumerator DisplayEfficiencySections()
    {
        // --- Food Utilization ---
        yield return StartCoroutine(AnimateSectionElement(foodUtilizationTotal, CalculateFoodUtilizationTotal(), "Food Utilization"));
        yield return StartCoroutine(AnimateSectionElement(foodUsageSummary, GenerateFoodUsageSummaryText()));
        yield return StartCoroutine(AnimateSectionElement(kitchenEfficiencyScore, CalculateKitchenEfficiencyScore(), "Kitchen Efficiency Score:"));

        // --- Shelter Utilization ---
        yield return StartCoroutine(AnimateSectionElement(shelterUtilizationTotal, CalculateShelterUtilizationTotal(), "Shelter Utilization"));
        yield return StartCoroutine(AnimateSectionElement(shelterUsageSummary, GenerateShelterUsageSummaryText()));
        yield return StartCoroutine(AnimateSectionElement(shelterEfficiencyScore, CalculateShelterEfficiencyScore(), "Shelter Efficiency Score:"));

        // --- Worker Utilization ---
        yield return StartCoroutine(AnimateSectionElement(workerUtilizationTotal, CalculateWorkerUtilizationTotal(), "Worker Utilization"));
        yield return StartCoroutine(AnimateSectionElement(workerUsageSummary, GenerateWorkerUsageSummaryText()));
        yield return StartCoroutine(AnimateSectionElement(workerEfficiencyScore, CalculateWorkerUtilizationScore(), "Worker Efficiency Score:"));

        // --- Budget Efficiency ---
        yield return StartCoroutine(AnimateSectionElement(budgetEfficiencyTotal, CalculateBudgetEfficiencyTotal(), "Budget Efficiency"));
        yield return StartCoroutine(AnimateSectionElement(budgetUsageSummary, GenerateBudgetUsageSummaryText()));
        yield return StartCoroutine(AnimateSectionElement(budgetEfficiencyScore, CalculateBudgetEfficiencyScore(), "Cost Efficiency Score:"));
    }

    // ================================================================
    // FINAL SCORE ANIMATIONS (progress bars + value text)
    // ================================================================

    IEnumerator AnimateFinalSatisfactionChanges()
    {
        if (satisfactionAnimationSection == null) yield break;

        // Set initial values BEFORE fade in
        if (satisfactionValueText != null)
            satisfactionValueText.text = $"{currentSatisfaction:F1}%";
        if (satisfactionBar != null)
            satisfactionBar.value = currentSatisfaction / 100f;

        float satisfactionChange = CalculateSatisfactionScore();
        float newSatisfaction = Mathf.Clamp(currentSatisfaction + satisfactionChange, 0f, 100f);

        // Fade in the satisfaction summary section
        satisfactionAnimationSection.alpha = 0f;
        float elapsed = 0f;
        while (elapsed < satisfactionAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            satisfactionAnimationSection.alpha = Mathf.Lerp(0f, 1f, elapsed / satisfactionAnimationDuration);
            yield return null;
        }
        satisfactionAnimationSection.alpha = 1f;

        // Show change text
        if (satisfactionChangeText != null)
        {
            string changeText = satisfactionChange >= 0 ? $"+{satisfactionChange:F1}" : $"{satisfactionChange:F1}";
            satisfactionChangeText.text = changeText;
            satisfactionChangeText.color = satisfactionChange >= 0 ? positiveChangeColor : negativeChangeColor;
        }

        // Animate the bar and value from current to new
        if (satisfactionValueText != null && satisfactionBar != null)
        {
            yield return StartCoroutine(AnimateFinalValue(satisfactionValueText, satisfactionBar, currentSatisfaction, newSatisfaction));
        }

        // Update running value for next day
        currentSatisfaction = newSatisfaction;
    }

    IEnumerator AnimateFinalEfficiencyChanges()
    {
        if (efficiencyAnimationSection == null) yield break;

        // Set initial values BEFORE fade in
        if (efficiencyValueText != null)
            efficiencyValueText.text = $"{currentEfficiency:F1}%";
        if (efficiencyBar != null)
            efficiencyBar.value = currentEfficiency / 100f;

        float efficiencyChange = CalculateEfficiencyScore();
        float newEfficiency = Mathf.Clamp(currentEfficiency + efficiencyChange, 0f, 100f);

        // Fade in the efficiency summary section
        efficiencyAnimationSection.alpha = 0f;
        float elapsed = 0f;
        while (elapsed < satisfactionAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            efficiencyAnimationSection.alpha = Mathf.Lerp(0f, 1f, elapsed / satisfactionAnimationDuration);
            yield return null;
        }
        efficiencyAnimationSection.alpha = 1f;

        // Show change text
        if (efficiencyChangeText != null)
        {
            string changeText = efficiencyChange >= 0 ? $"+{efficiencyChange:F1}" : $"{efficiencyChange:F1}";
            efficiencyChangeText.text = changeText;
            efficiencyChangeText.color = efficiencyChange >= 0 ? positiveChangeColor : negativeChangeColor;
        }

        // Animate the bar and value
        if (efficiencyValueText != null && efficiencyBar != null)
        {
            yield return StartCoroutine(AnimateFinalValue(efficiencyValueText, efficiencyBar, currentEfficiency, newEfficiency));
        }

        // Update running value for next day
        currentEfficiency = newEfficiency;
    }

    // ================================================================
    // ANIMATION HELPERS
    // ================================================================

    /// <summary>
    /// Animate a section element with a numeric value and label.
    /// Used for score rows (bonuses, penalties, totals).
    /// </summary>
    IEnumerator AnimateSectionElement(SectionElement element, float numberValue, string labelValue)
    {
        if (element.layoutObject == null) yield break;

        if (element.numberText != null)
        {
            yield return StartCoroutine(AnimateNumberText(element.numberText, 0f, numberValue));
        }

        if (element.labelText != null)
        {
            element.labelText.text = labelValue;
        }

        yield return StartCoroutine(FadeInElement(element));
    }

    /// <summary>
    /// Animate a section element with sentence text only.
    /// Used for status/summary description rows.
    /// </summary>
    IEnumerator AnimateSectionElement(SectionElement element, string sentenceValue)
    {
        if (element.layoutObject == null) yield break;

        if (element.sentenceText != null)
        {
            element.sentenceText.text = sentenceValue;
        }

        yield return StartCoroutine(FadeInElement(element));
    }

    IEnumerator FadeInElement(SectionElement element)
    {
        if (element.canvasGroup == null) yield break;

        float elapsed = 0f;
        while (elapsed < elementFadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            element.canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / elementFadeInDuration);
            yield return null;
        }

        element.canvasGroup.alpha = 1f;
        yield return new WaitForSecondsRealtime(elementAnimationDelay);
    }

    IEnumerator AnimateNumberText(TextMeshProUGUI numberText, float fromValue, float toValue)
    {
        float elapsed = 0f;
        while (elapsed < numberCountDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / numberCountDuration;
            float currentValue = Mathf.Lerp(fromValue, toValue, progress);

            string sign = currentValue >= 0 ? "+" : "";
            numberText.text = $"{sign}{currentValue:F1}";
            numberText.color = currentValue >= 0 ? positiveChangeColor : negativeChangeColor;

            yield return null;
        }

        string finalSign = toValue >= 0 ? "+" : "";
        numberText.text = $"{finalSign}{toValue:F1}";
        numberText.color = toValue >= 0 ? positiveChangeColor : negativeChangeColor;
    }

    IEnumerator AnimateFinalValue(TextMeshProUGUI valueText, Slider valueBar, float fromValue, float toValue)
    {
        valueBar.value = fromValue / 100f;

        float elapsed = 0f;
        while (elapsed < barAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / barAnimationDuration;
            float currentValue = Mathf.Lerp(fromValue, toValue, progress);

            valueText.text = $"{currentValue:F1}%";
            valueBar.value = currentValue / 100f;

            yield return null;
        }

        valueText.text = $"{toValue:F1}%";
        valueBar.value = toValue / 100f;
    }

    // ================================================================
    // SCORE CALCULATION METHODS
    // These compute scores from currentMetrics base data.
    // Used during animation AND when saving to history.
    // ================================================================

    // --- Satisfaction: Food Delivery ---
    
    /// <summary>+2 points per completed food task</summary>
    float CalculateFoodCompletionBonus() { return currentMetrics.completedFoodTasks * 2f; }
    
    /// <summary>
    /// +1.5 points per food task completed without expiring.
    /// FIX: Changed from (totalFoodTasks - expired) to (completedFoodTasks - expired).
    /// The old formula gave on-time bonus for tasks that were still active/pending,
    /// which inflated the score. Only completed tasks should count as "on time".
    /// </summary>
    float CalculateFoodOnTimeBonus() 
    { 
        // FIX: Use completedFoodTasks as the base, not totalFoodTasks
        // Only tasks that were actually completed can be considered "on time"
        int onTimeTasks = Mathf.Max(0, currentMetrics.completedFoodTasks - currentMetrics.expiredFoodDemandTasks);
        return onTimeTasks * 1.5f; 
    }
    
    /// <summary>-5 points per expired food demand task</summary>
    float CalculateFoodDelayScore() { return -currentMetrics.expiredFoodDemandTasks * 5f; }

    // --- Satisfaction: Lodging ---
    
    /// <summary>+2 points per completed lodging task</summary>
    float CalculateLodgingCompletionBonus() { return currentMetrics.completedLodgingTasks * 2f; }
    
    /// <summary>-5 points per group that stayed over 48 hours</summary>
    float CalculateLodgingOverstayPenalty() { return -currentMetrics.groupsOver48Hours * 5f; }

    // --- Satisfaction: Worker ---
    
    /// <summary>+1.5 points per task completed by workers</summary>
    float CalculateWorkerTaskBonus() { return currentMetrics.tasksCompletedByWorkers * 1.5f; }
    
    /// <summary>-0.1 points per percentage point of idle worker rate</summary>
    float CalculateWorkerIdleRateDisplay() { return -currentMetrics.idleWorkerRate * 0.1f; }

    // --- Efficiency: Kitchen ---
    
    /// <summary>-2 points per expired food pack (penalty for food waste)</summary>
    float CalculateKitchenEfficiencyScore() { return -currentMetrics.expiredFoodPacks * 2f; }
    
    // --- Efficiency: Shelter ---
    
    /// <summary>-0.5 points per vacant shelter slot (penalty for unused capacity)</summary>
    float CalculateShelterEfficiencyScore() { return -currentMetrics.vacantShelterSlots * 0.5f; }
    
    // --- Efficiency: Worker ---
    
    /// <summary>-1.5 points per idle worker</summary>
    float CalculateWorkerUtilizationScore() { return -currentMetrics.totalIdleWorkers * 1.5f; }
    
    // --- Efficiency: Budget ---
    
    /// <summary>
    /// Budget efficiency based on 70% ideal usage.
    /// Positive if under 70% budget used, negative if over.
    /// Formula: (70 - budgetUsageRate) * 0.2
    /// </summary>
    float CalculateBudgetEfficiencyScore() { return (70f - currentMetrics.budgetUsageRate) * 0.2f; }

    // --- Aggregate Totals ---
    
    float CalculateFoodSatisfactionTotal()
    {
        return CalculateFoodCompletionBonus() + CalculateFoodOnTimeBonus() + CalculateFoodDelayScore();
    }

    float CalculateLodgingSatisfactionTotal()
    {
        return CalculateLodgingCompletionBonus() + CalculateLodgingOverstayPenalty();
    }

    float CalculateWorkerSatisfactionTotal()
    {
        return CalculateWorkerTaskBonus() + CalculateWorkerIdleRateDisplay();
    }

    float CalculateFoodUtilizationTotal()
    {
        return CalculateKitchenEfficiencyScore();
    }

    float CalculateShelterUtilizationTotal()
    {
        return CalculateShelterEfficiencyScore();
    }

    float CalculateWorkerUtilizationTotal()
    {
        return CalculateWorkerUtilizationScore();
    }

    float CalculateBudgetEfficiencyTotal()
    {
        return CalculateBudgetEfficiencyScore();
    }

    /// <summary>Total satisfaction change = sum of all three satisfaction category totals</summary>
    float CalculateSatisfactionScore()
    {
        return CalculateFoodSatisfactionTotal() + CalculateLodgingSatisfactionTotal() + CalculateWorkerSatisfactionTotal();
    }

    /// <summary>Total efficiency change = sum of all four efficiency scores</summary>
    float CalculateEfficiencyScore()
    {
        return CalculateFoodUtilizationTotal() + CalculateShelterUtilizationTotal() + CalculateWorkerUtilizationTotal() + CalculateBudgetEfficiencyTotal();
    }

    // ================================================================
    // TEXT GENERATION (for sentence/status elements)
    // ================================================================

    string GenerateFoodDeliveryStatusText()
    {
        if (currentMetrics.totalFoodTasks == 0)
            return "No food delivery tasks today.";
        return currentMetrics.completedFoodTasks == currentMetrics.totalFoodTasks ?
            "All food delivery tasks completed successfully." :
            $"Food delivery completion: {currentMetrics.completedFoodTasks}/{currentMetrics.totalFoodTasks} tasks completed.";
    }

    string GenerateLodgingStatusText()
    {
        if (currentMetrics.totalLodgingTasks == 0)
            return "No lodging tasks today.";
        return currentMetrics.completedLodgingTasks == currentMetrics.totalLodgingTasks ?
            "All lodging tasks completed successfully." :
            $"Lodging completion: {currentMetrics.completedLodgingTasks}/{currentMetrics.totalLodgingTasks} tasks completed.";
    }

    string GenerateWorkerStatusText()
    {
        return $"{currentMetrics.tasksCompletedByWorkers} tasks completed by {currentMetrics.totalWorkersInvolved} workers ({currentMetrics.trainedWorkersInvolved} trained, {currentMetrics.untrainedWorkersInvolved} untrained).";
    }

    string GenerateOverstayText()
    {
        if (currentMetrics.groupsOver48Hours == 0)
            return "No groups overstayed beyond 48 hours.";
        return $"{currentMetrics.groupsOver48Hours} group(s) stayed over 48 hours";
    }

    string GenerateFoodUsageSummaryText()
    {
        return $"Meal usage rate: {currentMetrics.mealUsageRate:F1}%";
    }

    string GenerateShelterUsageSummaryText()
    {
        return $"Shelter utilization rate: {currentMetrics.shelterUtilizationRate:F1}%";
    }

    string GenerateWorkerUsageSummaryText()
    {
        return $"Worker utilization: {(100f - currentMetrics.idleWorkerRate):F1}%";
    }

    string GenerateBudgetUsageSummaryText()
    {
        return $"Budget usage: {currentMetrics.budgetUsageRate:F1}%";
    }

    // ================================================================
    // IMMEDIATE DISPLAY HELPERS (for historical reports)
    // ================================================================

    /// <summary>
    /// Set ALL section values from STORED metrics without any recalculation.
    /// This includes both numeric score elements AND sentence/status text elements.
    /// 
    /// FIX: The old version only set numeric values and skipped sentence texts,
    /// leaving stale text from the previous report visible.
    /// Now also populates all sentence/status text elements from stored metrics.
    /// </summary>
    void SetAllStoredSectionValues(DailyReportMetrics metrics)
    {
        // === SATISFACTION SECTIONS ===
        
        // Food Delivery - numeric scores from stored bonuses
        SetSectionValue(foodDeliveryTotal, Mathf.RoundToInt(
            metrics.foodCompletionBonus + metrics.foodOnTimeBonus + metrics.foodDelayScore));
        SetSectionValue(foodCompletionBonus, Mathf.RoundToInt(metrics.foodCompletionBonus));
        SetSectionValue(foodOnTimeBonus, Mathf.RoundToInt(metrics.foodOnTimeBonus));
        SetSectionValue(foodDelayScore, Mathf.RoundToInt(metrics.foodDelayScore));
        
        // FIX: Also set the sentence text for food delivery status
        SetSectionSentence(foodDeliveryStatus, GenerateFoodDeliveryStatusText());
        
        // Lodging - numeric scores from stored bonuses
        SetSectionValue(lodgingTotal, Mathf.RoundToInt(
            metrics.lodgingCompletionBonus + metrics.lodgingOverstayPenalty));
        SetSectionValue(lodgingCompletionBonus, Mathf.RoundToInt(metrics.lodgingCompletionBonus));
        SetSectionValue(lodgingOverstayPenalty, Mathf.RoundToInt(metrics.lodgingOverstayPenalty));
        
        // FIX: Also set sentence texts for lodging
        SetSectionSentence(lodgingStatus, GenerateLodgingStatusText());
        
        // Worker - numeric scores from stored bonuses
        SetSectionValue(workerTotal, Mathf.RoundToInt(
            metrics.workerTaskBonus + metrics.workerIdleRatePenalty));
        SetSectionValue(workerTaskBonus, Mathf.RoundToInt(metrics.workerTaskBonus));
        SetSectionValue(workerIdleRate, Mathf.RoundToInt(metrics.workerIdleRatePenalty));
        
        // FIX: Also set sentence texts for worker
        SetSectionSentence(workerStatus, GenerateWorkerStatusText());
        
        // === EFFICIENCY SECTIONS ===
        
        // Food Utilization
        SetSectionValue(foodUtilizationTotal, Mathf.RoundToInt(metrics.kitchenEfficiencyScore));
        SetSectionValue(kitchenEfficiencyScore, Mathf.RoundToInt(metrics.kitchenEfficiencyScore));
        SetSectionSentence(foodUsageSummary, GenerateFoodUsageSummaryText());
        
        // Shelter Utilization
        SetSectionValue(shelterUtilizationTotal, Mathf.RoundToInt(metrics.shelterEfficiencyScore));
        SetSectionValue(shelterEfficiencyScore, Mathf.RoundToInt(metrics.shelterEfficiencyScore));
        SetSectionSentence(shelterUsageSummary, GenerateShelterUsageSummaryText());
        
        // Worker Utilization
        SetSectionValue(workerUtilizationTotal, Mathf.RoundToInt(metrics.workerEfficiencyScore));
        SetSectionValue(workerEfficiencyScore, Mathf.RoundToInt(metrics.workerEfficiencyScore));
        SetSectionSentence(workerUsageSummary, GenerateWorkerUsageSummaryText());
        
        // Budget Efficiency
        SetSectionValue(budgetEfficiencyTotal, Mathf.RoundToInt(metrics.budgetEfficiencyScore));
        SetSectionValue(budgetEfficiencyScore, Mathf.RoundToInt(metrics.budgetEfficiencyScore));
        SetSectionSentence(budgetUsageSummary, GenerateBudgetUsageSummaryText());
    }

    /// <summary>
    /// Set final satisfaction/efficiency values from stored metrics.
    /// Displays the stored final values and change amounts without recalculation.
    /// </summary>
    void SetFinalValuesFromStoredMetrics(DailyReportMetrics metrics, float satisfaction, float efficiency)
    {
        // Satisfaction
        if (satisfactionValueText != null)
            satisfactionValueText.text = $"{satisfaction:F1}%";
        
        if (satisfactionChangeText != null)
        {
            float change = metrics.satisfactionChangeCalculated;
            satisfactionChangeText.text = change >= 0 ? $"+{change:F1}" : $"{change:F1}";
            satisfactionChangeText.color = change >= 0 ? positiveChangeColor : negativeChangeColor;
        }
        
        if (satisfactionBar != null)
        {
            satisfactionBar.value = satisfaction / 100f;
        }
        
        // Efficiency
        if (efficiencyValueText != null)
            efficiencyValueText.text = $"{efficiency:F1}%";
        
        if (efficiencyBar != null)
        {
            efficiencyBar.value = efficiency / 100f;
        }
        
        // Efficiency change from stored components
        if (efficiencyChangeText != null)
        {
            float effChange = metrics.kitchenEfficiencyScore + metrics.shelterEfficiencyScore + 
                            metrics.workerEfficiencyScore + metrics.budgetEfficiencyScore;
            efficiencyChangeText.text = effChange >= 0 ? $"+{effChange:F1}" : $"{effChange:F1}";
            efficiencyChangeText.color = effChange >= 0 ? positiveChangeColor : negativeChangeColor;
        }
    }

    /// <summary>
    /// Helper to set a section element's numeric value directly (no animation).
    /// Makes the element visible.
    /// </summary>
    void SetSectionValue(SectionElement element, int value, string suffix = "")
    {
        if (element == null || element.numberText == null) return;
        
        element.numberText.text = value.ToString() + suffix;
        element.numberText.color = value >= 0 ? positiveChangeColor : negativeChangeColor;
        
        if (element.canvasGroup != null)
            element.canvasGroup.alpha = 1f;
        if (element.layoutObject != null)
            element.layoutObject.SetActive(true);
    }

    /// <summary>
    /// Helper to set a section element's sentence text directly (no animation).
    /// Makes the element visible.
    /// FIX: New method - the old code had no way to set sentence texts for historical display.
    /// </summary>
    void SetSectionSentence(SectionElement element, string text)
    {
        if (element == null) return;
        
        if (element.sentenceText != null)
            element.sentenceText.text = text;
        
        if (element.canvasGroup != null)
            element.canvasGroup.alpha = 1f;
        if (element.layoutObject != null)
            element.layoutObject.SetActive(true);
    }

    /// <summary>
    /// Make all UI elements visible (called after setting values for historical display).
    /// </summary>
    void SetAllElementsVisible()
    {
        // Show final animation sections
        if (satisfactionAnimationSection != null)
        {
            satisfactionAnimationSection.alpha = 1f;
            satisfactionAnimationSection.gameObject.SetActive(true);
        }
        
        if (efficiencyAnimationSection != null)
        {
            efficiencyAnimationSection.alpha = 1f;
            efficiencyAnimationSection.gameObject.SetActive(true);
        }
        
        // Show all section elements
        ShowSectionElement(foodDeliveryTotal);
        ShowSectionElement(foodDeliveryStatus);
        ShowSectionElement(foodCompletionBonus);
        ShowSectionElement(foodOnTimeBonus);
        ShowSectionElement(foodDelayScore);
        
        ShowSectionElement(lodgingTotal);
        ShowSectionElement(lodgingStatus);
        ShowSectionElement(lodgingCompletionBonus);
        ShowSectionElement(lodgingOverstayPenalty);
        
        ShowSectionElement(workerTotal);
        ShowSectionElement(workerStatus);
        ShowSectionElement(workerTaskBonus);
        ShowSectionElement(workerIdleRate);
        
        ShowSectionElement(foodUtilizationTotal);
        ShowSectionElement(foodUsageSummary);
        ShowSectionElement(kitchenEfficiencyScore);
        
        ShowSectionElement(shelterUtilizationTotal);
        ShowSectionElement(shelterUsageSummary);
        ShowSectionElement(shelterEfficiencyScore);
        
        ShowSectionElement(workerUtilizationTotal);
        ShowSectionElement(workerUsageSummary);
        ShowSectionElement(workerEfficiencyScore);
        
        ShowSectionElement(budgetEfficiencyTotal);
        ShowSectionElement(budgetUsageSummary);
        ShowSectionElement(budgetEfficiencyScore);
    }

    void ShowSectionElement(SectionElement element)
    {
        if (element == null) return;
        
        if (element.canvasGroup != null)
            element.canvasGroup.alpha = 1f;
        if (element.layoutObject != null)
            element.layoutObject.SetActive(true);
    }

    // ================================================================
    // RESET & PUBLIC SETTERS
    // ================================================================

    public void SetCurrentSatisfaction(float satisfaction) { currentSatisfaction = satisfaction; }
    public void SetCurrentEfficiency(float efficiency) { currentEfficiency = efficiency; }

    public void ResetAllElementsToHidden()
    {
        ResetSectionElement(foodDeliveryTotal);
        ResetSectionElement(foodDeliveryStatus);
        ResetSectionElement(foodCompletionBonus);
        ResetSectionElement(foodOnTimeBonus);
        ResetSectionElement(foodDelayScore);

        ResetSectionElement(lodgingTotal);
        ResetSectionElement(lodgingStatus);
        ResetSectionElement(lodgingCompletionBonus);
        ResetSectionElement(lodgingOverstayPenalty);

        ResetSectionElement(workerTotal);
        ResetSectionElement(workerStatus);
        ResetSectionElement(workerTaskBonus);
        ResetSectionElement(workerIdleRate);

        ResetSectionElement(foodUtilizationTotal);
        ResetSectionElement(foodUsageSummary);
        ResetSectionElement(kitchenEfficiencyScore);

        ResetSectionElement(shelterUtilizationTotal);
        ResetSectionElement(shelterUsageSummary);
        ResetSectionElement(shelterEfficiencyScore);

        ResetSectionElement(workerUtilizationTotal);
        ResetSectionElement(workerUsageSummary);
        ResetSectionElement(workerEfficiencyScore);

        ResetSectionElement(budgetEfficiencyTotal);
        ResetSectionElement(budgetUsageSummary);
        ResetSectionElement(budgetEfficiencyScore);

        if (satisfactionAnimationSection != null)
            satisfactionAnimationSection.alpha = 0f;
        if (efficiencyAnimationSection != null)
            efficiencyAnimationSection.alpha = 0f;
    }

    void ResetSectionElement(SectionElement element)
    {
        if (element.canvasGroup != null)
        {
            element.canvasGroup.alpha = 0f;
        }
    }

    // ================================================================
    // HELPER DATA METHODS
    // ================================================================

    int GetNewWorkersHired()
    {
        if (WorkerSystem.Instance != null)
        {
            return WorkerSystem.Instance.GetNewWorkersHiredToday();
        }
        return 0;
    }

    int GetWorkersInTraining()
    {
        if (WorkerSystem.Instance != null)
        {
            var stats = WorkerSystem.Instance.GetWorkerStatistics();
            return stats.untrainedTraining;
        }
        return 0;
    }

    int CalculateTotalInfluencedResidents()
    {
        int totalInfluenced = 0;
        
        if (deliverySystem != null)
        {
            var completedDeliveries = deliverySystem.GetCompletedTasks();
            foreach (var delivery in completedDeliveries)
            {
                if (delivery.cargoType == ResourceType.Population)
                {
                    totalInfluenced += delivery.quantity;
                }
            }
        }
        
        return totalInfluenced;
    }

    // REMOVED: CalculateCaseworkTaskMetrics() - dead code, DailyReportData handles this
    // REMOVED: CalculateEmergencyTaskMetrics() - dead code, DailyReportData handles this
    // REMOVED: IsTaskRelatedToCasework() - dead code, DailyReportData handles this
    // REMOVED: SaveReportWithCalculatedScores() - dead code, replaced by SaveCompletedReportToHistory
    // REMOVED: CopyBaseMetrics() - dead code, was only used by SaveReportWithCalculatedScores
    // REMOVED: SetAllSectionValuesFromMetrics() - dead code duplicate of SetAllStoredSectionValues
    // REMOVED: SetFinalValuesDirectly() - dead code duplicate of SetFinalValuesFromStoredMetrics
    // REMOVED: SetAllSectionValuesDirectly() - dead code duplicate of SetAllStoredSectionValues
}