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

    [Header("Worker Training Section")]
    public SectionElement workerTotal;
    public SectionElement workerStatus;
    public SectionElement workerTrainingBonusElement;
    public SectionElement idleWorker;
    // workerIdleRate removed - no longer used for satisfaction

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
    public TextMeshProUGUI incompleteExpiredTasksText;   // Was: totalInfluencedResidentsText
    public TextMeshProUGUI foodTaskRatioText;
    public TextMeshProUGUI lodgingTaskRatioText;
    public TextMeshProUGUI casesResolvedRatioText;       // Was: caseworkTaskRatioText
    public TextMeshProUGUI emergencyTaskRatioText;

    private DailyReportMetrics currentMetrics;

    // Default values
    private float currentSatisfaction = 50f;
    private float currentEfficiency = 80f;

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
        // Initialize all section elements
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
        InitializeSectionElement(workerTrainingBonusElement);
        InitializeSectionElement(idleWorker);

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
        if (element == null || element.layoutObject == null) return;

        // Add CanvasGroup if not assigned
        if (element.canvasGroup == null)
        {
            element.canvasGroup = element.layoutObject.GetComponent<CanvasGroup>();
            if (element.canvasGroup == null)
                element.canvasGroup = element.layoutObject.AddComponent<CanvasGroup>();
        }

        // Hide initially
        element.canvasGroup.alpha = 0f;
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    public void DisplayDailyReport(DailyReportMetrics metrics)
    {
        currentMetrics = metrics;
        currentDayDisplay.text = GlobalClock.Instance.currentDay.ToString();
        UpdateBottomPanels(metrics);
        
        // FIX PROBLEM 1: Save report BEFORE animation starts.
        // All calculation methods are deterministic based on currentMetrics,
        // so we pre-compute the final satisfaction/efficiency values.
        SaveCompletedReportToHistory();
        
        StartCoroutine(AnimateReportDisplay());
    }

    /// <summary>
    /// Display report immediately without animations (for historical reports and day button clicks).
    /// FIX PROBLEM 1: No longer tries to save - report is already saved at animation start.
    /// FIX PROBLEM 2: Uses float-formatted SetSectionValueFormatted for proper +25.5 display.
    /// </summary>
    public void DisplayDailyReportImmediate(DailyReportMetrics metrics, int dayNumber)
    {
        // Stop any running animations (safe - report was already saved at animation start)
        StopAllCoroutines();
        
        currentMetrics = metrics;
        
        // Set day display
        if (currentDayDisplay != null)
        {
            currentDayDisplay.text = dayNumber.ToString();
        }
        
        // Use stored final values from metrics
        currentSatisfaction = metrics.finalSatisfactionValue;
        currentEfficiency = metrics.finalEfficiencyValue;
        
        // Set all values from stored metrics (NO recalculation)
        UpdateBottomPanels(metrics);
        SetAllStoredSectionValues(metrics);
        SetFinalValuesFromMetrics(metrics);
        SetAllElementsVisible();
    }

    // =========================================================================
    // BOTTOM PANELS
    // =========================================================================

    public void UpdateBottomPanels(DailyReportMetrics metrics)
    {
        // What We Did Today section
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
        
        // Today's Data section
        // FIX PROBLEM 7: Show incomplete/expired tasks instead of "total influenced residents"
        if (incompleteExpiredTasksText != null)
            incompleteExpiredTasksText.text = metrics.incompleteExpiredTasks.ToString();
        
        if (foodTaskRatioText != null)
            foodTaskRatioText.text = $"{metrics.completedFoodTasks}/{metrics.totalFoodTasks}";
        
        if (lodgingTaskRatioText != null)
            lodgingTaskRatioText.text = $"{metrics.completedLodgingTasks}/{metrics.totalLodgingTasks}";
        
        // FIX PROBLEM 4: Cases resolved = Emergency + Demand only
        if (casesResolvedRatioText != null)
            casesResolvedRatioText.text = $"{metrics.completedCasesResolved}/{metrics.totalCasesResolvable}";
        
        if (emergencyTaskRatioText != null)
            emergencyTaskRatioText.text = $"{metrics.completedEmergencyTasks}/{metrics.totalEmergencyTasks}";
    }

    // =========================================================================
    // ANIMATION COROUTINES
    // =========================================================================

    IEnumerator AnimateReportDisplay()
    {
        // Note: SaveCompletedReportToHistory() already called BEFORE this starts

        // Step 1: Display satisfaction panel sections one by one
        yield return StartCoroutine(DisplaySatisfactionSections());

        // Step 2: Display efficiency panel sections one by one
        yield return StartCoroutine(DisplayEfficiencySections());

        // Step 3: Show final satisfaction changes
        yield return StartCoroutine(AnimateFinalSatisfactionChanges());

        // Step 4: Show final efficiency changes
        yield return StartCoroutine(AnimateFinalEfficiencyChanges());
        
        // No save needed here - already saved before animation started
    }

    IEnumerator DisplaySatisfactionSections()
    {
        // Food Delivery Section
        yield return StartCoroutine(AnimateSectionElement(foodDeliveryTotal, CalculateFoodSatisfactionTotal(), "Timely Food Delivery"));
        yield return StartCoroutine(AnimateSectionElement(foodDeliveryStatus, GenerateFoodDeliveryStatusText()));
        yield return StartCoroutine(AnimateSectionElement(foodCompletionBonus, CalculateFoodCompletionBonus(), "Task Completion Bonus:"));
        yield return StartCoroutine(AnimateSectionElement(foodOnTimeBonus, CalculateFoodOnTimeBonus(), "On-Time Bonus:"));
        yield return StartCoroutine(AnimateSectionElement(foodDelayScore, CalculateFoodDelayScore(), "Task Delay Score:"));

        // Lodging Section
        yield return StartCoroutine(AnimateSectionElement(lodgingTotal, CalculateLodgingSatisfactionTotal(), "Lodging Provided"));
        yield return StartCoroutine(AnimateSectionElement(lodgingStatus, GenerateLodgingStatusText()));
        yield return StartCoroutine(AnimateSectionElement(lodgingCompletionBonus, CalculateLodgingCompletionBonus(), "Task Completion Bonus:"));
        yield return StartCoroutine(AnimateSectionElement(lodgingOverstayPenalty, CalculateLodgingOverstayPenalty(), GenerateOverstayText()));

        // Worker Training Section (replaces old Worker Management)
        yield return StartCoroutine(AnimateSectionElement(workerTotal, CalculateWorkerSatisfactionTotal(), "Worker Contributions"));
        yield return StartCoroutine(AnimateSectionElement(workerStatus, GenerateWorkerTrainingStatusText()));
        yield return StartCoroutine(AnimateSectionElement(workerTrainingBonusElement, CalculateWorkerTrainingBonus(), $"Workers in Training: {currentMetrics.workersReceivingTraining}"));
        yield return StartCoroutine(AnimateSectionElement(idleWorker, GenerateWorkerUsageSummaryText()));
    }

    IEnumerator DisplayEfficiencySections()
    {
        // Food Utilization Section
        yield return StartCoroutine(AnimateSectionElement(foodUtilizationTotal, CalculateFoodUtilizationTotal(), "Food Utilization"));
        yield return StartCoroutine(AnimateSectionElement(foodUsageSummary, GenerateFoodUsageSummaryText()));
        yield return StartCoroutine(AnimateSectionElement(kitchenEfficiencyScore, CalculateKitchenEfficiencyScore(), "Kitchen Efficiency Score:"));

        // Shelter Utilization Section
        yield return StartCoroutine(AnimateSectionElement(shelterUtilizationTotal, CalculateShelterUtilizationTotal(), "Shelter Utilization"));
        yield return StartCoroutine(AnimateSectionElement(shelterUsageSummary, GenerateShelterUsageSummaryText()));
        yield return StartCoroutine(AnimateSectionElement(shelterEfficiencyScore, CalculateShelterEfficiencyScore(), "Shelter Efficiency Score:"));

        // Worker Utilization Section
        yield return StartCoroutine(AnimateSectionElement(workerUtilizationTotal, CalculateWorkerUtilizationTotal(), "Worker Utilization"));
        yield return StartCoroutine(AnimateSectionElement(workerUsageSummary, GenerateWorkerUsageSummaryText()));
        yield return StartCoroutine(AnimateSectionElement(workerEfficiencyScore, CalculateWorkerUtilizationScore(), "Worker Efficiency Score:"));

        // Budget Efficiency Section
        yield return StartCoroutine(AnimateSectionElement(budgetEfficiencyTotal, CalculateBudgetEfficiencyTotal(), "Budget Efficiency"));
        yield return StartCoroutine(AnimateSectionElement(budgetUsageSummary, GenerateBudgetUsageSummaryText()));
        yield return StartCoroutine(AnimateSectionElement(budgetEfficiencyScore, CalculateBudgetEfficiencyScore(), "Cost Efficiency Score:"));
    }

    IEnumerator AnimateSectionElement(SectionElement element, float numberValue, string labelValue)
    {
        if (element == null || element.layoutObject == null) yield break;

        // Update content
        if (element.numberText != null)
        {
            yield return StartCoroutine(AnimateNumberText(element.numberText, 0f, numberValue));
        }

        if (element.labelText != null)
        {
            element.labelText.text = labelValue;
        }

        // Fade in the entire layout
        yield return StartCoroutine(FadeInElement(element));
    }

    IEnumerator AnimateSectionElement(SectionElement element, string sentenceValue)
    {
        if (element == null || element.layoutObject == null) yield break;

        // Update sentence content
        if (element.sentenceText != null)
        {
            element.sentenceText.text = sentenceValue;
        }

        // Fade in the entire layout
        yield return StartCoroutine(FadeInElement(element));
    }

    IEnumerator FadeInElement(SectionElement element)
    {
        if (element == null || element.canvasGroup == null) yield break;

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

    /// <summary>
    /// Animate a number counting up with sign prefix and one decimal place.
    /// e.g. +25.5, -3.0, +0.0
    /// </summary>
    IEnumerator AnimateNumberText(TextMeshProUGUI numberText, float fromValue, float toValue)
    {
        float elapsed = 0f;
        while (elapsed < numberCountDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / numberCountDuration;
            float currentValue = Mathf.Lerp(fromValue, toValue, progress);

            // Format with + or - sign and one decimal place
            string sign = currentValue >= 0 ? "+" : "";
            numberText.text = $"{sign}{currentValue:F1}";
            numberText.color = currentValue >= 0 ? positiveChangeColor : negativeChangeColor;

            yield return null;
        }

        string finalSign = toValue >= 0 ? "+" : "";
        numberText.text = $"{finalSign}{toValue:F1}";
        numberText.color = toValue >= 0 ? positiveChangeColor : negativeChangeColor;
    }

    IEnumerator AnimateFinalSatisfactionChanges()
    {
        if (satisfactionAnimationSection == null) yield break;

        // Set initial values BEFORE fade in animation
        if (satisfactionValueText != null)
            satisfactionValueText.text = $"{currentSatisfaction:F1}%";
        if (satisfactionBar != null)
            satisfactionBar.value = currentSatisfaction / 100f;

        float satisfactionChange = CalculateSatisfactionScore();
        float newSatisfaction = Mathf.Clamp(currentSatisfaction + satisfactionChange, 0f, 100f);

        satisfactionAnimationSection.alpha = 0f;
        float elapsed = 0f;
        while (elapsed < satisfactionAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            satisfactionAnimationSection.alpha = Mathf.Lerp(0f, 1f, elapsed / satisfactionAnimationDuration);
            yield return null;
        }
        satisfactionAnimationSection.alpha = 1f;

        if (satisfactionChangeText != null)
        {
            string changeText = satisfactionChange >= 0 ? $"+{satisfactionChange:F1}" : $"{satisfactionChange:F1}";
            satisfactionChangeText.text = changeText;
            satisfactionChangeText.color = satisfactionChange >= 0 ? positiveChangeColor : negativeChangeColor;
        }

        if (satisfactionValueText != null && satisfactionBar != null)
        {
            yield return StartCoroutine(AnimateFinalValue(satisfactionValueText, satisfactionBar, currentSatisfaction, newSatisfaction));
        }

        currentSatisfaction = newSatisfaction;
    }

    IEnumerator AnimateFinalEfficiencyChanges()
    {
        if (efficiencyAnimationSection == null) yield break;

        // Set initial values BEFORE fade in animation  
        if (efficiencyValueText != null)
            efficiencyValueText.text = $"{currentEfficiency:F1}%";
        if (efficiencyBar != null)
            efficiencyBar.value = currentEfficiency / 100f;

        float efficiencyChange = CalculateEfficiencyScore();
        float newEfficiency = Mathf.Clamp(currentEfficiency + efficiencyChange, 0f, 100f);

        efficiencyAnimationSection.alpha = 0f;
        float elapsed = 0f;
        while (elapsed < satisfactionAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            efficiencyAnimationSection.alpha = Mathf.Lerp(0f, 1f, elapsed / satisfactionAnimationDuration);
            yield return null;
        }
        efficiencyAnimationSection.alpha = 1f;

        if (efficiencyChangeText != null)
        {
            string changeText = efficiencyChange >= 0 ? $"+{efficiencyChange:F1}" : $"{efficiencyChange:F1}";
            efficiencyChangeText.text = changeText;
            efficiencyChangeText.color = efficiencyChange >= 0 ? positiveChangeColor : negativeChangeColor;
        }

        if (efficiencyValueText != null && efficiencyBar != null)
        {
            yield return StartCoroutine(AnimateFinalValue(efficiencyValueText, efficiencyBar, currentEfficiency, newEfficiency));
        }

        currentEfficiency = newEfficiency;
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

    // =========================================================================
    // SAVE TO HISTORY
    // =========================================================================

    /// <summary>
    /// Save the completed report with all calculated scores to history.
    /// FIX PROBLEM 1: Called BEFORE animation starts (not after).
    /// Pre-computes finalSatisfactionValue and finalEfficiencyValue so interrupting
    /// the animation can never cause data loss or corrupted values.
    /// </summary>
    void SaveCompletedReportToHistory()
    {
        if (DailyReportData.Instance == null || currentMetrics == null)
            return;
        
        // Store all calculated score components in currentMetrics
        currentMetrics.foodCompletionBonus = CalculateFoodCompletionBonus();
        currentMetrics.foodOnTimeBonus = CalculateFoodOnTimeBonus();
        currentMetrics.foodDelayScore = CalculateFoodDelayScore();
        currentMetrics.lodgingCompletionBonus = CalculateLodgingCompletionBonus();
        currentMetrics.lodgingOverstayPenalty = CalculateLodgingOverstayPenalty();
        currentMetrics.workerTrainingBonus = CalculateWorkerTrainingBonus();
        
        currentMetrics.kitchenEfficiencyScore = CalculateKitchenEfficiencyScore();
        currentMetrics.shelterEfficiencyScore = CalculateShelterEfficiencyScore();
        currentMetrics.workerEfficiencyScore = CalculateWorkerUtilizationScore();
        currentMetrics.budgetEfficiencyScore = CalculateBudgetEfficiencyScore();
        
        // PRE-COMPUTE final values (what the animation WILL reach)
        // This way, even if animation is interrupted, stored values are correct.
        float satisfactionChange = CalculateSatisfactionScore();
        float efficiencyChange = CalculateEfficiencyScore();
        
        currentMetrics.finalSatisfactionValue = Mathf.Clamp(currentSatisfaction + satisfactionChange, 0f, 100f);
        currentMetrics.finalEfficiencyValue = Mathf.Clamp(currentEfficiency + efficiencyChange, 0f, 100f);
        currentMetrics.satisfactionChangeCalculated = satisfactionChange;
        
        // Also store aggregate totals
        currentMetrics.foodSatisfaction = CalculateFoodSatisfactionTotal();
        currentMetrics.lodgingSatisfaction = CalculateLodgingSatisfactionTotal();
        currentMetrics.workerSatisfaction = CalculateWorkerSatisfactionTotal();
        currentMetrics.foodEfficiency = CalculateKitchenEfficiencyScore();
        currentMetrics.shelterEfficiency = CalculateShelterEfficiencyScore();
        currentMetrics.workerEfficiency = CalculateWorkerUtilizationScore();
        currentMetrics.budgetEfficiency = CalculateBudgetEfficiencyScore();
        
        // Save to history
        int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;
        DailyReportData.Instance.SaveReportToHistory(currentDay, currentMetrics);
        
        Debug.Log($"Saved completed report for Day {currentDay} to history (pre-computed final sat={currentMetrics.finalSatisfactionValue:F1}, eff={currentMetrics.finalEfficiencyValue:F1})");
    }

    // =========================================================================
    // HISTORICAL REPORT DISPLAY (no animation)
    // =========================================================================

    /// <summary>
    /// Set all section values from STORED metrics (no recalculation).
    /// FIX PROBLEM 2: Uses SetSectionValueFormatted() to preserve +25.5 format.
    /// FIX PROBLEM 3: Uses workerTrainingBonus instead of workerTaskBonus/workerIdleRatePenalty.
    /// Also populates sentence/status texts for historical views.
    /// </summary>
    void SetAllStoredSectionValues(DailyReportMetrics metrics)
    {
        // Food Delivery Section - use stored bonuses
        SetSectionValueFormatted(foodDeliveryTotal,
            metrics.foodCompletionBonus + metrics.foodOnTimeBonus + metrics.foodDelayScore);
        SetSectionSentence(foodDeliveryStatus, GenerateStoredFoodDeliveryStatusText(metrics));
        SetSectionValueFormatted(foodCompletionBonus, metrics.foodCompletionBonus);
        SetSectionValueFormatted(foodOnTimeBonus, metrics.foodOnTimeBonus);
        SetSectionValueFormatted(foodDelayScore, metrics.foodDelayScore);
        
        // Lodging Section
        SetSectionValueFormatted(lodgingTotal,
            metrics.lodgingCompletionBonus + metrics.lodgingOverstayPenalty);
        SetSectionSentence(lodgingStatus, GenerateStoredLodgingStatusText(metrics));
        SetSectionValueFormatted(lodgingCompletionBonus, metrics.lodgingCompletionBonus);
        SetSectionValueFormatted(lodgingOverstayPenalty, metrics.lodgingOverstayPenalty);
        
        // Worker Training Section - FIX PROBLEM 3: uses workerTrainingBonus
        SetSectionValueFormatted(workerTotal, metrics.workerTrainingBonus);
        SetSectionSentence(workerStatus, GenerateStoredWorkerTrainingStatusText(metrics));
        SetSectionValueFormatted(workerTrainingBonusElement, metrics.workerTrainingBonus);
        
        // Efficiency Sections - use stored scores
        SetSectionValueFormatted(foodUtilizationTotal, metrics.kitchenEfficiencyScore);
        SetSectionSentence(foodUsageSummary, $"Meal usage rate: {metrics.mealUsageRate:F1}%");
        SetSectionValueFormatted(kitchenEfficiencyScore, metrics.kitchenEfficiencyScore);
        
        SetSectionValueFormatted(shelterUtilizationTotal, metrics.shelterEfficiencyScore);
        SetSectionSentence(shelterUsageSummary, $"Shelter utilization rate: {metrics.shelterUtilizationRate:F1}%");
        SetSectionValueFormatted(shelterEfficiencyScore, metrics.shelterEfficiencyScore);
        
        SetSectionValueFormatted(workerUtilizationTotal, metrics.workerEfficiencyScore);
        SetSectionSentence(workerUsageSummary, $"Worker utilization: {(100f - metrics.idleWorkerRate):F1}%");
        SetSectionValueFormatted(workerEfficiencyScore, metrics.workerEfficiencyScore);
        
        SetSectionValueFormatted(budgetEfficiencyTotal, metrics.budgetEfficiencyScore);
        SetSectionSentence(budgetUsageSummary, $"Budget usage: {metrics.budgetUsageRate:F1}%");
        SetSectionValueFormatted(budgetEfficiencyScore, metrics.budgetEfficiencyScore);
    }

    /// <summary>
    /// Set final satisfaction/efficiency from stored metrics (for historical view)
    /// </summary>
    void SetFinalValuesFromMetrics(DailyReportMetrics metrics)
    {
        // Satisfaction
        if (satisfactionValueText != null)
            satisfactionValueText.text = $"{metrics.finalSatisfactionValue:F1}%";
        
        if (satisfactionChangeText != null)
        {
            float change = metrics.satisfactionChangeCalculated;
            satisfactionChangeText.text = change >= 0 ? $"+{change:F1}" : $"{change:F1}";
            satisfactionChangeText.color = change >= 0 ? positiveChangeColor : negativeChangeColor;
        }
        
        if (satisfactionBar != null)
        {
            satisfactionBar.value = metrics.finalSatisfactionValue / 100f;
        }
        
        // Efficiency
        if (efficiencyValueText != null)
            efficiencyValueText.text = $"{metrics.finalEfficiencyValue:F1}%";
        
        if (efficiencyBar != null)
        {
            efficiencyBar.value = metrics.finalEfficiencyValue / 100f;
        }
        
        // Efficiency change (sum of efficiency components)
        if (efficiencyChangeText != null)
        {
            float effChange = metrics.kitchenEfficiencyScore + metrics.shelterEfficiencyScore + 
                            metrics.workerEfficiencyScore + metrics.budgetEfficiencyScore;
            efficiencyChangeText.text = effChange >= 0 ? $"+{effChange:F1}" : $"{effChange:F1}";
            efficiencyChangeText.color = effChange >= 0 ? positiveChangeColor : negativeChangeColor;
        }
    }

    // =========================================================================
    // SECTION VALUE HELPERS
    // =========================================================================

    /// <summary>
    /// FIX PROBLEM 2: Format a float value with sign and one decimal place.
    /// e.g. +25.5, -3.0, +0.0  (matches the animation format exactly)
    /// </summary>
    void SetSectionValueFormatted(SectionElement element, float value)
    {
        if (element == null || element.numberText == null) return;
        
        string sign = value >= 0 ? "+" : "";
        element.numberText.text = $"{sign}{value:F1}";
        
        // Set color based on positive/negative
        element.numberText.color = value >= 0 ? positiveChangeColor : negativeChangeColor;
        
        // Make visible
        if (element.canvasGroup != null)
            element.canvasGroup.alpha = 1f;
        if (element.layoutObject != null)
            element.layoutObject.SetActive(true);
    }

    /// <summary>
    /// Set sentence text on a section element and make it visible.
    /// Used for status/summary text lines in historical view.
    /// </summary>
    void SetSectionSentence(SectionElement element, string sentence)
    {
        if (element == null) return;
        
        if (element.sentenceText != null)
            element.sentenceText.text = sentence;
        
        if (element.canvasGroup != null)
            element.canvasGroup.alpha = 1f;
        if (element.layoutObject != null)
            element.layoutObject.SetActive(true);
    }

    /// <summary>
    /// Make all UI elements visible (used after SetAllStoredSectionValues)
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
        ShowSectionElement(workerTrainingBonusElement);
        
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

    // =========================================================================
    // SATISFACTION SCORE CALCULATIONS
    // =========================================================================

    // Satisfaction total calculations
    float CalculateFoodSatisfactionTotal()
    {
        return CalculateFoodCompletionBonus() + CalculateFoodOnTimeBonus() + CalculateFoodDelayScore();
    }

    float CalculateLodgingSatisfactionTotal()
    {
        return CalculateLodgingCompletionBonus() + CalculateLodgingOverstayPenalty();
    }

    /// <summary>
    /// FIX PROBLEM 3: Worker satisfaction now only uses training bonus.
    /// More workers in training = better satisfaction.
    /// </summary>
    float CalculateWorkerSatisfactionTotal()
    {
        return CalculateWorkerTrainingBonus();
    }

    // Efficiency total calculations
    float CalculateFoodUtilizationTotal() { return CalculateKitchenEfficiencyScore(); }
    float CalculateShelterUtilizationTotal() { return CalculateShelterEfficiencyScore(); }
    float CalculateWorkerUtilizationTotal() { return CalculateWorkerUtilizationScore(); }
    float CalculateBudgetEfficiencyTotal() { return CalculateBudgetEfficiencyScore(); }

    // Score calculation methods - Food Delivery
    float CalculateFoodCompletionBonus() { return currentMetrics.completedFoodTasks * 2f; }
    float CalculateFoodOnTimeBonus() { return (currentMetrics.completedFoodTasks - currentMetrics.expiredFoodDemandTasks) * 1.5f; }
    float CalculateFoodDelayScore() { return -currentMetrics.expiredFoodDemandTasks * 5f; }

    // Score calculation methods - Lodging
    float CalculateLodgingCompletionBonus() { return currentMetrics.completedLodgingTasks * 2f; }
    float CalculateLodgingOverstayPenalty() { return -currentMetrics.groupsOver48Hours * 5f; }

    // Score calculation methods - Worker Training (FIX PROBLEM 3)
    /// <summary>
    /// Worker Training Bonus = workersReceivingTraining * 3.0
    /// More workers in training = higher satisfaction bonus.
    /// Replaces old workerTaskBonus + workerIdleRatePenalty.
    /// </summary>
    float CalculateWorkerTrainingBonus() { return currentMetrics.workersReceivingTraining * 3f; }

    // =========================================================================
    // EFFICIENCY SCORE CALCULATIONS
    // Each formula: positive when performing well, negative when performing poorly.
    // Score ≈ 0 at 50% utilization (baseline). Range roughly -5 to +5 each.
    // =========================================================================
    
    /// <summary>
    /// Kitchen: Reward high meal usage rate, penalize expired food.
    /// +5 at 100% usage, 0 at 50%, -5 at 0%. Extra penalty for waste.
    /// </summary>
    float CalculateKitchenEfficiencyScore() 
    { 
        float usageReward = (currentMetrics.mealUsageRate - 50f) * 0.1f;
        float wastePenalty = -currentMetrics.expiredFoodPacks * 2f;
        return usageReward + wastePenalty;
    }
    
    /// <summary>
    /// Shelter: Reward high occupancy rate.
    /// +5 at 100% occupancy, 0 at 50%, -5 at 0%.
    /// </summary>
    float CalculateShelterEfficiencyScore() 
    { 
        return (currentMetrics.shelterOccupancyRate - 50f) * 0.1f;
    }
    
    /// <summary>
    /// Worker: Reward low idle rate (high utilization).
    /// +5 at 0% idle, 0 at 50% idle, -5 at 100% idle.
    /// </summary>
    float CalculateWorkerUtilizationScore() 
    { 
        float utilization = 100f - currentMetrics.idleWorkerRate;
        return (utilization - 50f) * 0.1f;
    }
    
    /// <summary>
    /// Budget: Reward conservative spending relative to daily allocation.
    /// +14 at 0% usage, 0 at 70%, -6 at 100%.
    /// </summary>
    float CalculateBudgetEfficiencyScore() 
    { 
        return (70f - currentMetrics.budgetUsageRate) * 0.2f; 
    }

    // Final score calculations
    float CalculateSatisfactionScore()
    {
        return CalculateFoodSatisfactionTotal() + CalculateLodgingSatisfactionTotal() + CalculateWorkerSatisfactionTotal();
    }

    float CalculateEfficiencyScore()
    {
        return CalculateFoodUtilizationTotal() + CalculateShelterUtilizationTotal() + CalculateWorkerUtilizationTotal() + CalculateBudgetEfficiencyTotal();
    }

    // =========================================================================
    // TEXT GENERATION METHODS
    // =========================================================================

    // --- Live text (used during animation with currentMetrics) ---

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

    /// <summary>
    /// FIX PROBLEM 3: Worker status now shows training info instead of task completion.
    /// </summary>
    string GenerateWorkerTrainingStatusText()
    {
        if (currentMetrics.workersReceivingTraining == 0)
            return "No workers currently in training.";
        return $"{currentMetrics.workersReceivingTraining} worker(s) currently receiving training.";
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

    // --- Stored text (used for historical view from metrics) ---

    string GenerateStoredFoodDeliveryStatusText(DailyReportMetrics metrics)
    {
        if (metrics.totalFoodTasks == 0)
            return "No food delivery tasks today.";
        return metrics.completedFoodTasks == metrics.totalFoodTasks ?
            "All food delivery tasks completed successfully." :
            $"Food delivery completion: {metrics.completedFoodTasks}/{metrics.totalFoodTasks} tasks completed.";
    }

    string GenerateStoredLodgingStatusText(DailyReportMetrics metrics)
    {
        if (metrics.totalLodgingTasks == 0)
            return "No lodging tasks today.";
        return metrics.completedLodgingTasks == metrics.totalLodgingTasks ?
            "All lodging tasks completed successfully." :
            $"Lodging completion: {metrics.completedLodgingTasks}/{metrics.totalLodgingTasks} tasks completed.";
    }

    string GenerateStoredWorkerTrainingStatusText(DailyReportMetrics metrics)
    {
        if (metrics.workersReceivingTraining == 0)
            return "No workers currently in training.";
        return $"{metrics.workersReceivingTraining} worker(s) currently receiving training.";
    }

    // =========================================================================
    // PUBLIC SETTERS
    // =========================================================================

    public void SetCurrentSatisfaction(float satisfaction) { currentSatisfaction = satisfaction; }
    public void SetCurrentEfficiency(float efficiency) { currentEfficiency = efficiency; }

    // =========================================================================
    // RESET
    // =========================================================================

    public void ResetAllElementsToHidden()
    {
        // Reset all satisfaction sections
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
        ResetSectionElement(workerTrainingBonusElement);

        // Reset all efficiency sections
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

        // Reset final animation sections
        if (satisfactionAnimationSection != null)
            satisfactionAnimationSection.alpha = 0f;
        if (efficiencyAnimationSection != null)
            efficiencyAnimationSection.alpha = 0f;
    }

    void ResetSectionElement(SectionElement element)
    {
        if (element != null && element.canvasGroup != null)
        {
            element.canvasGroup.alpha = 0f;
        }
    }
}