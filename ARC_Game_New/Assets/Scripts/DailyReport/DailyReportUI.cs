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

    [Header("Food Waste Section")]
    public SectionElement wasteTotal;
    public SectionElement wasteStatus;

    [Header("Casework Section")]
    public SectionElement caseworkTotal;
    public SectionElement caseworkStatus;

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

    //NEW
    [Header("Assumed Based on Scores Doc")]
    public int assumedTotalWorkerPoolSize = 200;
    public int maxBudget = 999999;
    //END NEW

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
    public TextMeshProUGUI moneyReceivedText;
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
    private float currentEfficiency = 0f;

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
        // // Initialize all section elements
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

        InitializeSectionElement(wasteTotal);
        InitializeSectionElement(wasteStatus);

        InitializeSectionElement(caseworkTotal);
        InitializeSectionElement(caseworkStatus);

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

        // Sync running totals from authoritative source before computing this day's delta
        if (SatisfactionAndBudget.Instance != null)
        {
            currentSatisfaction = SatisfactionAndBudget.Instance.GetCurrentSatisfaction();
            currentEfficiency = SatisfactionAndBudget.Instance.GetCurrentEfficiency();
        }

        UpdateBottomPanels(metrics);

        SaveCompletedReportToHistory();
        
        StartCoroutine(AnimateReportDisplay());
    }

    /// <summary>
    /// Display report immediately without animations (for historical reports and day button clicks).
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

        if (moneyReceivedText != null)
            moneyReceivedText.text = $"${metrics.budgetReceived:F0}";

        
        if (workersHiredText != null)
            workersHiredText.text = metrics.newWorkersHired.ToString();
        
        if (workersTrainedText != null)
            workersTrainedText.text = metrics.workersInTraining.ToString();
        
        // Today's Data section
        if (incompleteExpiredTasksText != null)
            incompleteExpiredTasksText.text = metrics.incompleteExpiredTasks.ToString();
        
        if (foodTaskRatioText != null)
            foodTaskRatioText.text = $"{metrics.completedFoodTasks}/{metrics.totalFoodTasks}";
        
        if (lodgingTaskRatioText != null)
            lodgingTaskRatioText.text = $"{metrics.completedLodgingTasks}/{metrics.totalLodgingTasks}";
        
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
        yield return StartCoroutine(AnimateSectionElement(foodDeliveryTotal, currentMetrics.satFoodScore, "Food Satisfaction"));
        yield return StartCoroutine(AnimateSectionElement(foodDeliveryStatus,
            $"{currentMetrics.cumFoodPacksConsumedByClients}/{currentMetrics.cumFoodPacksNeededByClients} net food packs consumed by clients."));

        yield return StartCoroutine(AnimateSectionElement(lodgingTotal, currentMetrics.satLodgingScore, "Lodging Satisfaction"));
        yield return StartCoroutine(AnimateSectionElement(lodgingStatus,
            $"{currentMetrics.cumLodgingNightsConsumed}/{currentMetrics.cumLodgingNightsNeeded} lodging-nights consumed by clients (cumulative)."));

        yield return StartCoroutine(AnimateSectionElement(workerTotal, currentMetrics.satWorkerScore, "Worker Satisfaction"));
        yield return StartCoroutine(AnimateSectionElement(workerStatus,
            $"Net Idle Rounds: {currentMetrics.cumIdleWorkerRounds} | Net Working Rounds: {currentMetrics.cumWorkingWorkerRounds} | Net Training Rounds: {currentMetrics.cumTrainingWorkerRounds}"));

        yield return StartCoroutine(AnimateSectionElement(wasteTotal, currentMetrics.satWasteScore, "Food Waste Penalty"));
        yield return StartCoroutine(AnimateSectionElement(wasteStatus,
            $"{currentMetrics.cumFoodPacksWasted} of {currentMetrics.cumFoodPacksConsumedByClients + currentMetrics.cumFoodPacksWasted} food packs requested went to waste (cumulative)."));

        yield return StartCoroutine(AnimateSectionElement(caseworkTotal, currentMetrics.satCaseworkScore, "Casework Satisfaction"));
        yield return StartCoroutine(AnimateSectionElement(caseworkStatus,
            $"{currentMetrics.cumClientRoundsAwaitingCasework} net rounds clients awaited casework, out of {currentMetrics.cumClientsRequestedCasework} clients who requested it."));
    }

    IEnumerator DisplayEfficiencySections()
    {
        yield return StartCoroutine(AnimateSectionElement(foodUtilizationTotal, currentMetrics.costFoodScore, "Food Cost Efficiency"));
        yield return StartCoroutine(AnimateSectionElement(foodUsageSummary,
            $"${currentMetrics.cumFoodSpend:F0} spent, {currentMetrics.cumFoodPacksConsumedByClients} net meals consumed."));
       

        yield return StartCoroutine(AnimateSectionElement(shelterUtilizationTotal, currentMetrics.costLodgingScore, "Lodging Cost Efficiency"));
        yield return StartCoroutine(AnimateSectionElement(shelterUsageSummary,
            $"${currentMetrics.cumLodgingSpend:F0} spent, {currentMetrics.cumLodgingNightsConsumed} net nights used."));


        yield return StartCoroutine(AnimateSectionElement(workerUtilizationTotal, currentMetrics.costWorkerScore, "Worker Cost Efficiency"));
        yield return StartCoroutine(AnimateSectionElement(workerUsageSummary,
            $"${(currentMetrics.cumWorkerRequestCost + currentMetrics.cumWorkerTrainingCost):F0} spent over {currentMetrics.cumWorkingWorkerRounds} networking rounds."));
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

        if (satisfactionValueText != null) satisfactionValueText.text = $"{currentSatisfaction:F1}";
        if (satisfactionBar != null) satisfactionBar.value = currentSatisfaction / 10000f;

        float satisfactionChange = currentMetrics.satisfactionChangeCalculated;
        float newSatisfaction = currentMetrics.finalSatisfactionValue;

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
            yield return StartCoroutine(AnimateFinalValue(satisfactionValueText, satisfactionBar, currentSatisfaction, newSatisfaction));

        currentSatisfaction = newSatisfaction;
    }

    IEnumerator AnimateFinalEfficiencyChanges()
    {
        if (efficiencyAnimationSection == null) yield break;

        if (efficiencyValueText != null) efficiencyValueText.text = $"{currentEfficiency:F1}";
        if (efficiencyBar != null) efficiencyBar.value = currentEfficiency / 10000f;

        float efficiencyChange = currentMetrics.costEfficiencyChangeCalculated;
        float newEfficiency = currentMetrics.finalEfficiencyValue;

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
            yield return StartCoroutine(AnimateFinalValue(efficiencyValueText, efficiencyBar, currentEfficiency, newEfficiency));

        currentEfficiency = newEfficiency;
    }

    IEnumerator AnimateFinalValue(TextMeshProUGUI valueText, Slider valueBar, float fromValue, float toValue)
    {
        valueBar.value = fromValue / 10000f;

        float elapsed = 0f;
        while (elapsed < barAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / barAnimationDuration;
            float currentValue = Mathf.Lerp(fromValue, toValue, progress);

            valueText.text = $"{currentValue:F1}";
            valueBar.value = currentValue / 10000f;

            yield return null;
        }

        valueText.text = $"{toValue:F1}";
        valueBar.value = toValue / 10000f;
    }

    // =========================================================================
    // SAVE TO HISTORY
    // =========================================================================

    /// <summary>
    /// Save the completed report with all calculated scores to history.
    /// Pre-computes finalSatisfactionValue and finalEfficiencyValue so interrupting
    /// the animation can never cause data loss or corrupted values.
    /// </summary>
    void SaveCompletedReportToHistory()
    {
        //NEW
        if (DailyReportData.Instance == null || currentMetrics == null)
            return;

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

        float newSatisfaction = CalculateLiveSatisfactionScore() * 10000f; 
        float newEfficiency = CalculateLiveCostEfficiencyScore() * 10000f;
        float sFood = S_Food(), sLodging = S_Lodging(), sWorker = S_WorkerUse(), sWaste = S_Waste(), sCasework = S_Casework();

        const float wSat = 0.2f;
        currentMetrics.satFoodScore     = sFood     * wSat * 10000f;
        currentMetrics.satLodgingScore  = sLodging  * wSat * 10000f;
        currentMetrics.satWorkerScore   = sWorker   * wSat * 10000f;
        currentMetrics.satWasteScore    = sWaste    * wSat * 10000f;
        currentMetrics.satCaseworkScore = sCasework * wSat * 10000f;

        float cFood = C_Food(), cLodging = C_Lodging(), cWorker = C_Worker();
        const float wCost = 1f / 3f;
        currentMetrics.costFoodScore    = cFood    * wCost * 10000f;
        currentMetrics.costLodgingScore = cLodging * wCost * 10000f;
        currentMetrics.costWorkerScore  = cWorker  * wCost * 10000f;

        currentMetrics.costEfficiencyChangeCalculated = newEfficiency - currentEfficiency;

        currentMetrics.satisfactionChangeCalculated = newSatisfaction - currentSatisfaction;
        currentMetrics.finalSatisfactionValue = newSatisfaction;
        currentMetrics.finalEfficiencyValue = newEfficiency;
        var d = DailyReportData.Instance;

        currentMetrics.cumFoodPacksConsumedByClients = d.GetCumulativeFoodPacksConsumedByClients();
        currentMetrics.cumFoodPacksNeededByClients = d.GetCumulativeFoodPacksNeededByClients();
        currentMetrics.cumFoodPacksWasted = d.GetCumulativeFoodPacksWasted(); 

        currentMetrics.cumIdleWorkerRounds = d.GetCumulativeIdleWorkerRounds();
        currentMetrics.cumWorkingWorkerRounds = d.GetCumulativeWorkingWorkerRounds();
        currentMetrics.cumTrainingWorkerRounds = d.GetCumulativeTrainingWorkerRounds();

        currentMetrics.cumClientRoundsAwaitingCasework = d.GetCumulativeClientRoundsAwaitingCasework();
        currentMetrics.cumClientsRequestedCasework = d.GetCumulativeClientsRequestedCasework();

        currentMetrics.cumLodgingNightsConsumed = d.GetCumulativeLodgingNightsConsumed();
        currentMetrics.cumLodgingNightsNeeded = d.GetCumulativeLodgingNightsNeeded();

        currentMetrics.cumFoodSpend = d.GetCumulativeFoodSpend();
        currentMetrics.cumLodgingSpend = d.GetCumulativeLodgingSpend();
        currentMetrics.cumWorkerRequestCost = d.GetCumulativeWorkerRequestCost();
        currentMetrics.cumWorkerTrainingCost = d.GetCumulativeWorkerTrainingCost();

        currentMetrics.liveSatisfactionScore = newSatisfaction / 10000f; 
        currentMetrics.liveCostEfficiencyScore = newEfficiency / 10000f;

        int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;

        SatisfactionAndBudget.Instance?.AddSatisfaction(currentMetrics.satisfactionChangeCalculated, $"Day {currentDay} report");
        SatisfactionAndBudget.Instance?.AddEfficiency(newEfficiency - currentEfficiency, $"Day {currentDay} efficiency");


        currentMetrics.foodSatisfaction = CalculateFoodSatisfactionTotal();
        currentMetrics.lodgingSatisfaction = CalculateLodgingSatisfactionTotal();
        currentMetrics.workerSatisfaction = CalculateWorkerSatisfactionTotal();
        currentMetrics.foodEfficiency = CalculateKitchenEfficiencyScore();
        currentMetrics.shelterEfficiency = CalculateShelterEfficiencyScore();
        currentMetrics.workerEfficiency = CalculateWorkerUtilizationTotal();
        currentMetrics.budgetEfficiency = CalculateBudgetEfficiencyScore();

        // Save to history
        DailyReportData.Instance.SaveReportToHistory(currentDay, currentMetrics);
        //END NEW

        // ── Log all metrics and scores ──────────────────────────────
        int day = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;

        // Task summary
        GameLogPanel.Instance?.LogMetricsChange(
            $"DAILY_REPORT | day={day}" +
            $" | tasks_total={currentMetrics.totalTasks}" +
            $" | tasks_completed={currentMetrics.completedTasks}" +
            $" | tasks_expired={currentMetrics.expiredTasks}" +
            $" | food_tasks={currentMetrics.completedFoodTasks}/{currentMetrics.totalFoodTasks}" +
            $" | lodging_tasks={currentMetrics.completedLodgingTasks}/{currentMetrics.totalLodgingTasks}" +
            $" | emergency_tasks={currentMetrics.completedEmergencyTasks}/{currentMetrics.totalEmergencyTasks}" +
            $" | cases_resolved={currentMetrics.completedCasesResolved}/{currentMetrics.totalCasesResolvable}");

        // Resource & population
        GameLogPanel.Instance?.LogMetricsChange(
            $"DAILY_RESOURCES | day={day}" +
            $" | food_produced={currentMetrics.foodProduced}" +
            $" | food_delivered={currentMetrics.foodDelivered}" +
            $" | food_in_storage={currentMetrics.currentFoodInStorage}" +
            $" | food_wasted={currentMetrics.foodWasted}" +
            $" | population={currentMetrics.totalPopulation}" +
            $" | shelter_occupancy={currentMetrics.shelterOccupancyRate:F1}%" +
            $" | vacant_slots={currentMetrics.vacantShelterSlots}" +
            $" | overstay_groups={currentMetrics.groupsOver48Hours}");

        // Workers & budget
        GameLogPanel.Instance?.LogMetricsChange(
            $"DAILY_WORKERS_BUDGET | day={day}" +
            $" | workers_total={currentMetrics.totalWorkers}" +
            $" | workers_idle={currentMetrics.idleWorkers}" +
            $" | idle_rate={currentMetrics.idleWorkerRate:F1}%" +
            $" | workers_in_training={currentMetrics.workersReceivingTraining}" +
            $" | workers_hired={currentMetrics.newWorkersHired}" +
            $" | budget_start={currentMetrics.startingBudget:F0}" +
            $" | budget_spent={currentMetrics.budgetSpent:F0}" +
            $" | budget_end={currentMetrics.endingBudget:F0}" +
            $" | budget_usage={currentMetrics.budgetUsageRate:F1}%");

        // Satisfaction score breakdown
        GameLogPanel.Instance?.LogMetricsChange(
            $"SATISFACTION_SCORES | day={day}" +
            $" | food_completion_bonus={currentMetrics.foodCompletionBonus:F1}" +
            $" | food_ontime_bonus={currentMetrics.foodOnTimeBonus:F1}" +
            $" | food_delay_score={currentMetrics.foodDelayScore:F1}" +
            $" | lodging_completion_bonus={currentMetrics.lodgingCompletionBonus:F1}" +
            $" | lodging_overstay_penalty={currentMetrics.lodgingOverstayPenalty:F1}" +
            $" | worker_training_bonus={currentMetrics.workerTrainingBonus:F1}" +
            $" | satisfaction_change={currentMetrics.satisfactionChangeCalculated:F1}" +
            $" | satisfaction_final={currentMetrics.finalSatisfactionValue:F1}");

        // Efficiency score breakdown
        GameLogPanel.Instance?.LogMetricsChange(
            $"EFFICIENCY_SCORES | day={day}" +
            $" | kitchen_efficiency={currentMetrics.kitchenEfficiencyScore:F1}" +
            $" | shelter_efficiency={currentMetrics.shelterEfficiencyScore:F1}" +
            $" | worker_efficiency={currentMetrics.workerEfficiencyScore:F1}" +
            $" | budget_efficiency={currentMetrics.budgetEfficiencyScore:F1}" +
            $" | efficiency_final={currentMetrics.finalEfficiencyValue:F1}");
        // ─────────────────────────────────────────────────────────────────
        
        Debug.Log($"Saved completed report for Day {currentDay} to history (pre-computed final sat={currentMetrics.finalSatisfactionValue:F1}, eff={currentMetrics.finalEfficiencyValue:F1})");
    }

    // =========================================================================
    // HISTORICAL REPORT DISPLAY (no animation)
    // =========================================================================

    /// <summary>
    /// Set all section values from STORED metrics (no recalculation).
    /// Also populates sentence/status texts for historical views.
    /// </summary>

    void SetAllStoredSectionValues(DailyReportMetrics metrics)
    {
        SetSectionValueFormatted(foodDeliveryTotal, metrics.satFoodScore);
        // SetSectionSentence(foodDeliveryStatus, $"{metrics.cumFoodPacksConsumedByClients}/{metrics.cumFoodPacksNeededByClients} food packs delivered to clients (cumulative).");
        SetSectionSentence(foodDeliveryStatus, $"{metrics.cumFoodPacksConsumedByClients}/{metrics.cumFoodPacksNeededByClients} food packs consumed by clients (cumulative).");

        SetSectionValueFormatted(lodgingTotal, metrics.satLodgingScore);
        // SetSectionSentence(lodgingStatus, $"{metrics.cumLodgingNightsConsumed}/{metrics.cumLodgingNightsNeeded} lodging-nights provided (cumulative).");
        SetSectionSentence(lodgingStatus, $"{metrics.cumLodgingNightsConsumed}/{metrics.cumLodgingNightsNeeded} lodging-nights consumed by clients (cumulative).");

        SetSectionValueFormatted(workerTotal, metrics.satWorkerScore);

        SetSectionValueFormatted(wasteTotal, metrics.satWasteScore);
        // SetSectionSentence(wasteStatus, $"{metrics.cumFoodPacksWasted} food pack(s) wasted (cumulative).");
        SetSectionSentence(wasteStatus, $"{metrics.cumFoodPacksWasted} of {metrics.cumFoodPacksConsumedByClients + metrics.cumFoodPacksWasted} food packs requested went to waste (cumulative).");

        SetSectionValueFormatted(caseworkTotal, metrics.satCaseworkScore);
        // SetSectionSentence(caseworkStatus, $"{metrics.cumClientRoundsAwaitingCasework} client-rounds still awaiting casework.");
        SetSectionSentence(caseworkStatus, $"{metrics.cumClientRoundsAwaitingCasework} client-rounds still awaiting casework, out of {metrics.cumClientsRequestedCasework} clients who requested it.");

        SetSectionValueFormatted(foodUtilizationTotal, metrics.costFoodScore);
        SetSectionSentence(foodUsageSummary, $"${metrics.cumFoodSpend:F0} spent, {metrics.cumFoodPacksConsumedByClients} packs consumed.");
        SetSectionValueFormatted(kitchenEfficiencyScore, metrics.costFoodScore);

        SetSectionValueFormatted(shelterUtilizationTotal, metrics.costLodgingScore);
        SetSectionSentence(shelterUsageSummary, $"${metrics.cumLodgingSpend:F0} spent, {metrics.cumLodgingNightsConsumed} nights used.");
        SetSectionValueFormatted(shelterEfficiencyScore, metrics.costLodgingScore);

        SetSectionValueFormatted(workerUtilizationTotal, metrics.costWorkerScore);
        // SetSectionSentence(workerUsageSummary, $"${(metrics.cumWorkerRequestCost + metrics.cumWorkerTrainingCost):F0} spent over {metrics.cumWorkingWorkerRounds} working-rounds.");
        SetSectionSentence(workerStatus, $"Idle: {metrics.cumIdleWorkerRounds} | Working: {metrics.cumWorkingWorkerRounds} | Training: {metrics.cumTrainingWorkerRounds}");
        SetSectionValueFormatted(workerEfficiencyScore, metrics.costWorkerScore);

        float totalCostEff = metrics.costFoodScore + metrics.costLodgingScore + metrics.costWorkerScore;
        SetSectionValueFormatted(budgetEfficiencyTotal, totalCostEff);
        if (budgetUsageSummary?.layoutObject != null) budgetUsageSummary.layoutObject.SetActive(false);
        if (budgetEfficiencyScore?.layoutObject != null) budgetEfficiencyScore.layoutObject.SetActive(false);
    }

    /// <summary>
    /// Set final satisfaction/efficiency from stored metrics (for historical view)
    /// </summary>
    void SetFinalValuesFromMetrics(DailyReportMetrics metrics)
    {
        // Satisfaction
        if (satisfactionValueText != null)
            satisfactionValueText.text = $"{metrics.finalSatisfactionValue:F1}";
        
        if (satisfactionChangeText != null)
        {
            float change = metrics.satisfactionChangeCalculated;
            satisfactionChangeText.text = change >= 0 ? $"+{change:F1}" : $"{change:F1}";
            satisfactionChangeText.color = change >= 0 ? positiveChangeColor : negativeChangeColor;
        }
        
        if (satisfactionBar != null)
        {
            satisfactionBar.value = metrics.finalSatisfactionValue / 10000f;
        }
        
        // Efficiency
        if (efficiencyValueText != null)
            efficiencyValueText.text = $"{metrics.finalEfficiencyValue:F1}";
        
        if (efficiencyBar != null)
        {
            efficiencyBar.value = metrics.finalEfficiencyValue / 10000f;
        }
        
        // Efficiency change (sum of efficiency components)
        if (efficiencyChangeText != null)
        {
            float effChange = metrics.costEfficiencyChangeCalculated; 
            efficiencyChangeText.text = effChange >= 0 ? $"+{effChange:F1}" : $"{effChange:F1}";
            efficiencyChangeText.color = effChange >= 0 ? positiveChangeColor : negativeChangeColor;
        }
    }

    // =========================================================================
    // SECTION VALUE HELPERS
    // =========================================================================

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
    /// 
    void SetAllElementsVisible()
    {
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

        ShowSectionElement(wasteTotal);
        ShowSectionElement(wasteStatus);

        ShowSectionElement(caseworkTotal);
        ShowSectionElement(caseworkStatus);

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

    float CalculateWorkerSatisfactionTotal()
    {
        return CalculateWorkerTrainingBonus() + CalculateWorkerUtilizationScore();
    }

    // Efficiency total calculations
    float CalculateFoodUtilizationTotal() { return CalculateKitchenEfficiencyScore(); }
    float CalculateShelterUtilizationTotal() { return CalculateShelterEfficiencyScore(); }
    float CalculateWorkerUtilizationTotal() { return CalculateWorkerSatisfactionTotal(); }
    float CalculateBudgetEfficiencyTotal() { return CalculateBudgetEfficiencyScore(); }

    // Score calculation methods - Food Delivery
    float CalculateFoodCompletionBonus() { return currentMetrics.completedFoodTasks * 2f; }
    float CalculateFoodOnTimeBonus() { return (currentMetrics.completedFoodTasks - currentMetrics.expiredFoodDemandTasks) * 1.5f; }
    float CalculateFoodDelayScore() { return -currentMetrics.expiredFoodDemandTasks * 5f; }

    // Score calculation methods - Lodging
    float CalculateLodgingCompletionBonus() { return currentMetrics.completedLodgingTasks * 2f; }
    float CalculateLodgingOverstayPenalty() { return -currentMetrics.groupsOver48Hours * 5f; }

    // Score calculation methods - Worker Training
    /// <summary>
    /// Worker Training Bonus = workersReceivingTraining * 3.0
    /// More workers in training = higher satisfaction bonus.
    /// Replaces old workerTaskBonus + workerIdleRatePenalty.
    /// </summary>
    float CalculateWorkerTrainingBonus() { return currentMetrics.workersReceivingTraining * 3f; }

    //NEW
    // =========================================================================
    // satisfaction subscores (cummulative)
    // =========================================================================

    float S_Food()
    {
        var d = DailyReportData.Instance;
        int needed = d.GetCumulativeFoodPacksNeededByClients();
        if (needed <= 0) return 1f;
        return Mathf.Clamp01((float)d.GetCumulativeFoodPacksConsumedByClients() / needed);
    }

    float S_Lodging()
    {
        var d = DailyReportData.Instance;
        int needed = d.GetCumulativeLodgingNightsNeeded();
        if (needed <= 0) return 1f;
        return Mathf.Clamp01((float)d.GetCumulativeLodgingNightsConsumed() / needed);
    }

    float S_WorkerUse()
    {
        var d = DailyReportData.Instance;
        int roundsElapsed = d.GetCumulativeRoundsElapsed();
        if (roundsElapsed <= 0) return 0f;

        float denom = assumedTotalWorkerPoolSize * roundsElapsed;

        float idleRatio = d.GetCumulativeIdleWorkerRounds() / denom;
        float workingRatio = d.GetCumulativeWorkingWorkerRounds() / denom;
        float trainingRatio = d.GetCumulativeTrainingWorkerRounds() / denom;

        const float wIdle = 1f / 3f, wWorking = 1f / 3f, wTraining = 1f / 3f;
        return (1f - idleRatio) * wIdle + (workingRatio * wWorking) + (trainingRatio * wTraining);
    }

    float S_Waste()
    {
        var d = DailyReportData.Instance;
        int used = d.GetCumulativeFoodPacksConsumedByClients();
        int wasted = d.GetCumulativeFoodPacksWasted();
        int requested = used + wasted; 

        if (requested <= 0) return 1f; 
        return (float)wasted / requested; 
    }

    float S_Casework()
    {
        var d = DailyReportData.Instance;
        int requested = d.GetCumulativeClientsRequestedCasework();
        if (requested <= 0 || GameDataManager.Instance == null) return 1f;
        int denom = GameDataManager.Instance.InitialGameDays * GameDataManager.Instance.InitialRoundsPerDay * requested;
        if (denom <= 0) return 1f;

        return Mathf.Clamp01(1f - ((float)d.GetCumulativeClientRoundsAwaitingCasework() / denom));
    }

    float CalculateLiveSatisfactionScore()
    {
        const float wFood = 0.2f, wLodging = 0.2f, wWorker = 0.2f, wWaste = 0.2f, wCasework = 0.2f;
        return S_Food() * wFood + S_Lodging() * wLodging + S_WorkerUse() * wWorker
             + S_Waste() * wWaste + S_Casework() * wCasework;
    }

    //END NEW

    // =========================================================================
    // EFFICIENCY SCORE CALCULATIONS
    // Each formula: positive when performing well, negative when performing poorly.
    // Score ≈ 0 at 50% utilization (baseline). Range roughly -5 to +5 each.
    // =========================================================================

    /// <summary>
    /// NEW FORMULA: Penalize meals left in storage at end of day.
    /// These packs WILL become waste when the day advances.
    /// 0 packs in storage = +5.0 (perfect, no waste)
    /// 10 packs = 0 (baseline)
    /// 20+ packs = -5.0 (heavy waste)
    /// </summary>
    float CalculateKitchenEfficiencyScore() 
    { 
        float foodInStorage = currentMetrics.currentFoodInStorage;
        return 5.0f - (foodInStorage * 0.05f);
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

//NEW
// =========================================================================
// cost-eff new scores
// =========================================================================

    float C_Food()
    {
        var d = DailyReportData.Instance;
        int consumed = d.GetCumulativeFoodPacksConsumedByClients();
        if (consumed <= 0) return 0f;

        float raw = d.GetCumulativeFoodSpend() / consumed;

        var gdm = GameDataManager.Instance;
        var bs = FindObjectOfType<BuildingSystem>();
        int mapSpots = bs != null ? bs.RegisteredSites.Count : 0; 
        int days = gdm.InitialGameDays;
        float totalBudget = maxBudget;

        float min = (float)bs.kitchenConstructionCost / (gdm.InitialKitchenCapacity * days);
        float max = Mathf.Max(bs.kitchenConstructionCost * mapSpots * days, totalBudget);

        return Mathf.Clamp01(1f - (raw - min) / (max - min));
    }

    float C_Lodging()
    {
        var d = DailyReportData.Instance;
        var gdm = GameDataManager.Instance;

        float nightsConsumed = d.GetCumulativeLodgingNightsConsumed(); 
        if (nightsConsumed <= 0f) return 0f;

        float raw = d.GetCumulativeLodgingSpend() / nightsConsumed;

        var bs = FindObjectOfType<BuildingSystem>();
        int mapSpots = bs != null ? bs.RegisteredSites.Count : 0; 
        int days = gdm.InitialGameDays;
        float totalBudget = maxBudget;

        float min = (float)bs.shelterConstructionCost / (gdm.InitialShelterCapacity * days);
        float max = Mathf.Max(bs.shelterConstructionCost * mapSpots * days, totalBudget);

        return Mathf.Clamp01(1f - (raw - min) / (max - min));
    }

    float C_Worker()
    {
        var d = DailyReportData.Instance;
        int workingRounds = d.GetCumulativeWorkingWorkerRounds();
        if (workingRounds <= 0) return 0f;

        float raw = (d.GetCumulativeWorkerTrainingCost() + d.GetCumulativeWorkerRequestCost()) / workingRounds;

        var gdm = GameDataManager.Instance;
        var wrs = FindObjectOfType<WorkerRequestSystem>();
        var wts = FindObjectOfType<WorkerTrainingSystem>();
        float untrainedCost = wrs != null ? wrs.untrainedWorkerCost : 100f;
        float trainedCost   = wrs != null ? wrs.trainedWorkerCost   : 100f;
        float trainingCost = wts != null ? wts.trainingCostPerWorker : 100f;
        float min = untrainedCost;
        float totalBudget = maxBudget;
        float maxCostWorkforceUnit = Mathf.Max(untrainedCost, Mathf.Max(trainedCost/2f, (untrainedCost+trainingCost)/2f));
        float max = Mathf.Max(assumedTotalWorkerPoolSize*maxCostWorkforceUnit, totalBudget);

        return Mathf.Clamp01(1f - (raw - min) / (max - min));
    }

    float CalculateLiveCostEfficiencyScore()
    {
        const float wFood = 1f / 3f, wLodging = 1f / 3f, wWorker = 1f / 3f;
        return C_Food() * wFood + C_Lodging() * wLodging + C_Worker() * wWorker;
    }
    //END NEW

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

    /// <summary>
    /// These packs represent upcoming waste when the day advances.
    /// </summary>
    string GenerateFoodUsageSummaryText()
    {
        int foodInStorage = currentMetrics.currentFoodInStorage;
        if (foodInStorage == 0)
            return "No meals remaining in storage. No waste!";
        return $"{foodInStorage} meal(s) in storage will go to waste.";
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

        ResetSectionElement(wasteTotal);
        ResetSectionElement(wasteStatus);

        ResetSectionElement(caseworkTotal);
        ResetSectionElement(caseworkStatus);

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
        if (element != null && element.canvasGroup != null)
        {
            element.canvasGroup.alpha = 0f;
        }
    }
}