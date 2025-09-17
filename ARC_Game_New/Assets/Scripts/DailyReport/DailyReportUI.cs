using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

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
    [Header("Satisfaction Panel Sections")]
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

    public void DisplayDailyReport(DailyReportMetrics metrics)
    {
        currentMetrics = metrics;
        StartCoroutine(AnimateReportDisplay());
    }

    IEnumerator AnimateReportDisplay()
    {
        // Step 1: Display satisfaction panel sections one by one
        yield return StartCoroutine(DisplaySatisfactionSections());

        // Step 2: Display efficiency panel sections one by one
        yield return StartCoroutine(DisplayEfficiencySections());

        // Step 3: Show final satisfaction changes
        yield return StartCoroutine(AnimateFinalSatisfactionChanges());

        // Step 4: Show final efficiency changes
        yield return StartCoroutine(AnimateFinalEfficiencyChanges());
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
        yield return StartCoroutine(AnimateSectionElement(lodgingTotal, CalculateLodgingSatisfactionTotal(), "Lodging Services"));
        yield return StartCoroutine(AnimateSectionElement(lodgingStatus, GenerateLodgingStatusText()));
        yield return StartCoroutine(AnimateSectionElement(lodgingCompletionBonus, CalculateLodgingCompletionBonus(), "Task Completion Bonus:"));
        yield return StartCoroutine(AnimateSectionElement(lodgingOverstayPenalty, CalculateLodgingOverstayPenalty(), GenerateOverstayText()));

        // Worker Section
        yield return StartCoroutine(AnimateSectionElement(workerTotal, CalculateWorkerSatisfactionTotal(), "Worker Management"));
        yield return StartCoroutine(AnimateSectionElement(workerStatus, GenerateWorkerStatusText()));
        yield return StartCoroutine(AnimateSectionElement(workerTaskBonus, CalculateWorkerTaskBonus(), "Worker Task Bonus:"));
        yield return StartCoroutine(AnimateSectionElement(workerIdleRate, CalculateWorkerIdleRateDisplay(), $"Idle Workers: {currentMetrics.idleWorkerRate:F1}%"));
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
        if (element.layoutObject == null) yield break;

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
        if (element.layoutObject == null) yield break;

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

            // Format with + or - sign and appropriate color
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

    // Satisfaction total calculations
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

    // Efficiency total calculations
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

    // Text generation methods
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

    // Score calculation methods - Food Delivery
    float CalculateFoodCompletionBonus() { return currentMetrics.completedFoodTasks * 2f; }
    float CalculateFoodOnTimeBonus() { return (currentMetrics.totalFoodTasks - currentMetrics.expiredFoodDemandTasks) * 1.5f; }
    float CalculateFoodDelayScore() { return -currentMetrics.expiredFoodDemandTasks * 5f; }

    // Score calculation methods - Lodging
    float CalculateLodgingCompletionBonus() { return currentMetrics.completedLodgingTasks * 2f; }
    float CalculateLodgingOverstayPenalty() { return -currentMetrics.groupsOver48Hours * 5f; }

    // Score calculation methods - Worker
    float CalculateWorkerTaskBonus() { return currentMetrics.tasksCompletedByWorkers * 1.5f; }
    float CalculateWorkerIdleRateDisplay() { return -currentMetrics.idleWorkerRate * 0.1f; }

    // Score calculation methods - Efficiency
    float CalculateKitchenEfficiencyScore() { return -currentMetrics.expiredFoodPacks * 2f; }
    float CalculateShelterEfficiencyScore() { return -currentMetrics.vacantShelterSlots * 0.5f; }
    float CalculateWorkerUtilizationScore() { return -currentMetrics.totalIdleWorkers * 1.5f; }
    float CalculateBudgetEfficiencyScore() { return (70f - currentMetrics.budgetUsageRate) * 0.2f; }

    // Final score calculations
    float CalculateSatisfactionScore()
    {
        return CalculateFoodSatisfactionTotal() + CalculateLodgingSatisfactionTotal() + CalculateWorkerSatisfactionTotal();
    }

    float CalculateEfficiencyScore()
    {
        return CalculateFoodUtilizationTotal() + CalculateShelterUtilizationTotal() + CalculateWorkerUtilizationTotal() + CalculateBudgetEfficiencyTotal();
    }

    public void SetCurrentSatisfaction(float satisfaction) { currentSatisfaction = satisfaction; }
    public void SetCurrentEfficiency(float efficiency) { currentEfficiency = efficiency; }

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
        ResetSectionElement(workerTaskBonus);
        ResetSectionElement(workerIdleRate);

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
        if (element.canvasGroup != null)
        {
            element.canvasGroup.alpha = 0f;
        }
    }

}