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
    public SectionElement workerWorkingElement;
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

    [Header("Receipt - Today's Expenses")]
    public SectionElement receiptKitchen;
    public SectionElement receiptShelter;
    public SectionElement receiptCasework;
    public SectionElement receiptFastFood;
    public SectionElement receiptTransport;
    public SectionElement receiptLodging;
    public SectionElement receiptWorkerRequest;
    public SectionElement receiptWorkerTraining;
    public SectionElement receiptWorkerReleased;
    public SectionElement receiptOther;
    public SectionElement receiptTotal;

    [Header("Current Budget (Live)")]
    public TextMeshProUGUI currentBudgetDisplayText;

    [Header("Live Status - Food")]
    public SectionElement liveFoodInTransit;
    public SectionElement liveKitchenProduction;
    public SectionElement liveFoodWaste;

    [Header("Live Status - Lodging")]
    public SectionElement liveNeedLodging;

    [Header("Live Status - Workers")]
    public SectionElement liveWorkersWorking;
    public SectionElement liveWorkersWaiting;
    public SectionElement liveWorkersTraining;
    public SectionElement liveWorkersReleasedToday;

    [Header("Live Status - Casework")]
    public SectionElement liveNeedCasework;
    public SectionElement liveInTransitToCasework;

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
        InitializeSectionElement(workerTotal);
        // worker satisfaction subscores
        InitializeSectionElement(workerStatus);
        InitializeSectionElement(idleWorker);
        InitializeSectionElement(workerWorkingElement);
        InitializeSectionElement(workerTrainingBonusElement);

        InitializeSectionElement(budgetEfficiencyTotal);
        InitializeSectionElement(budgetUsageSummary);
        InitializeSectionElement(budgetEfficiencyScore);
        
        //receipt
        InitializeSectionElement(receiptKitchen);
        InitializeSectionElement(receiptShelter);
        InitializeSectionElement(receiptCasework);
        InitializeSectionElement(receiptFastFood);
        InitializeSectionElement(receiptTransport);
        InitializeSectionElement(receiptLodging);
        InitializeSectionElement(receiptWorkerRequest);
        InitializeSectionElement(receiptWorkerTraining);
        InitializeSectionElement(receiptWorkerReleased);
        InitializeSectionElement(receiptOther);
        InitializeSectionElement(receiptTotal);

        // curr bottom stats
        InitializeSectionElement(liveFoodInTransit);
        InitializeSectionElement(liveKitchenProduction);
        InitializeSectionElement(liveFoodWaste);
        InitializeSectionElement(liveNeedLodging);
        InitializeSectionElement(liveWorkersWorking);
        InitializeSectionElement(liveWorkersWaiting);
        InitializeSectionElement(liveWorkersTraining);
        InitializeSectionElement(liveWorkersReleasedToday);
        InitializeSectionElement(liveNeedCasework);
        InitializeSectionElement(liveInTransitToCasework);
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


        if (DailyReportData.Instance != null)
        {
            currentSatisfaction = DailyReportData.Instance.GetDayStartSatisfaction();
            currentEfficiency = DailyReportData.Instance.GetDayStartEfficiency();
        }

        UpdateBottomPanels(metrics);
        SaveCompletedReportToHistory();

        LogDailyReportAsDisplayed();
        LogDailyReportScoreFormulas();
        BuildingStatusTableUI.Instance?.LogTableContents(GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1);

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

        // Start the satisfaction/efficiency score-change animation immediately, in
        // parallel with the rest of the report, instead of waiting for every other
        // section to finish first.
        Coroutine satisfactionAnim = StartCoroutine(AnimateFinalSatisfactionChanges());
        Coroutine efficiencyAnim = StartCoroutine(AnimateFinalEfficiencyChanges());

        // Step 1: Display satisfaction panel sections one by one
        yield return StartCoroutine(DisplaySatisfactionSections());

        // Step 2: Display efficiency panel sections one by one
        yield return StartCoroutine(DisplayEfficiencySections());

        // receipt
        yield return StartCoroutine(DisplayReceiptSection());

        // bottom curr status
        yield return StartCoroutine(DisplayLiveStatusSection());

        // Make sure the score-change animation has actually finished before
        // revealing the building status table.
        yield return satisfactionAnim;
        yield return efficiencyAnim;

        // No save needed here - already saved before animation started
        // (Data logging already happened synchronously in DisplayDailyReport(),
        // before this coroutine was even started — see LogDailyReportAsDisplayed()
        // and LogDailyReportScoreFormulas(). Everything below here is purely visual.)

        // Reveal the building status table now that the report's own content is fully shown
        BuildingStatusTableUI.Instance?.ShowTable();
    }

    // =========================================================================
    // GAME LOG — mirrors what the report UI will display (not a separately-computed stat dump)
    // =========================================================================

    /// <summary>
    /// Records the Daily Report the same instant the player enters it — computed
    /// directly from currentMetrics/DailyReportData using the exact same values and
    /// format strings the animation coroutines below use, rather than waiting for
    /// those coroutines to run and reading the result off the UI. This must stay
    /// callable synchronously (no waiting on animation), since on Day 8 the log is
    /// sent to the server immediately after DisplayDailyReport() returns.
    /// Order matches the on-screen sequence: DisplaySatisfactionSections,
    /// DisplayEfficiencySections, DisplayReceiptSection, DisplayLiveStatusSection,
    /// AnimateFinalSatisfactionChanges, AnimateFinalEfficiencyChanges.
    /// </summary>
    void LogDailyReportAsDisplayed()
    {
        if (currentMetrics == null) return;
        var d = DailyReportData.Instance;
        if (d == null) return;

        int day = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;
        int seq = 0;

        void Row(string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            seq++;
            GameLogPanel.Instance?.LogMetricsChange($"DAILY_REPORT_UI | day={day} | #{seq} | {label}: {value}");
        }

        // Matches AnimateNumberText's final text: sign + whole number
        string Score(float v) => (v >= 0 ? "+" : "") + v.ToString("F0");
        // Matches AnimateCostNumberText's final text
        string Cost(float v) => $"${v:F0}";
        // Matches AnimateLiveNumberText's final text
        string Live(int v) => $"{v}";

        var m = currentMetrics;

        // --- Bottom Panel: What We Did Today ---
        Row("Tasks Completed", m.completedTasks.ToString());
        Row("Facilities Constructed", m.buildingsConstructed.ToString());
        Row("Money Spent", $"${m.budgetSpent:F0}");
        Row("Money Received", $"${m.budgetReceived:F0}");
        Row("Workers Hired", m.newWorkersHired.ToString());
        Row("Workers Trained", m.workersInTraining.ToString());

        // --- Bottom Panel: Today's Data ---
        Row("Incomplete/Expired Tasks", m.incompleteExpiredTasks.ToString());
        Row("Food Task Ratio", $"{m.completedFoodTasks}/{m.totalFoodTasks}");
        Row("Lodging Task Ratio", $"{m.completedLodgingTasks}/{m.totalLodgingTasks}");
        Row("Cases Resolved Ratio", $"{m.completedCasesResolved}/{m.totalCasesResolvable}");
        Row("Emergency Task Ratio", $"{m.completedEmergencyTasks}/{m.totalEmergencyTasks}");

        // --- Satisfaction Panel (order matches DisplaySatisfactionSections) ---
        Row("Food Satisfaction", Score(m.satFoodScore));
        Row("Food Delivery Status", $"{m.cumFoodPacksConsumedByClients}/{m.cumFoodPacksNeededByClients} food packs consumed by clients (cumulative).");
        Row("Lodging Satisfaction", Score(m.satLodgingScore));
        Row("Lodging Status", $"{m.cumLodgingNightsConsumed}/{m.cumLodgingNightsNeeded} lodging-nights consumed by clients (cumulative).");
        Row("Worker Use Satisfaction", Score(m.satWorkerScore));
        Row("Worker Status", $"Idle: {m.cumIdleWorkerRounds} | Working: {m.cumWorkingWorkerRounds} | Training: {m.cumTrainingWorkerRounds}");
        Row("Idle", Score(m.workerIdleSatScore));
        Row("Working", Score(m.workerWorkingSatScore));
        Row("Training", Score(m.workerTrainingSatScore));
        Row("Food Waste Penalty", Score(m.satWasteScore));
        Row("Waste Status", $"{m.cumFoodPacksWasted} of {m.cumFoodPacksConsumedByClients + m.cumFoodPacksWasted} food packs requested went to waste (cumulative).");
        Row("Casework Satisfaction", Score(m.satCaseworkScore));
        Row("Casework Status", $"{m.cumClientRoundsAwaitingCasework} client-rounds still awaiting casework, out of {m.cumClientsRequestedCasework} clients who requested it.");

        // --- Efficiency Panel (order matches DisplayEfficiencySections) ---
        Row("Food Cost Efficiency", Score(m.costFoodScore));
        Row("Food Usage Summary", $"${m.cumFoodSpend:F0} spent, {m.cumFoodPacksConsumedByClients} packs consumed (cumulative).");
        Row("Lodging Cost Efficiency", Score(m.costLodgingScore));
        Row("Lodging Usage Summary", $"${m.cumLodgingSpend:F0} spent, {m.cumLodgingNightsConsumed} nights used (cumulative).");
        Row("Worker Cost Efficiency", Score(m.costWorkerScore));
        Row("Worker Usage Summary", $"${(m.cumWorkerRequestCost + m.cumWorkerTrainingCost):F0} spent over {m.cumWorkingWorkerRounds} working-rounds.");

        // --- Receipt (order matches DisplayReceiptSection) ---
        Row("Opened Kitchen", Cost(m.todayKitchenOpenCost));
        Row("Opened Shelter", Cost(m.todayShelterOpenCost));
        Row("Opened Casework", Cost(m.todayCaseworkOpenCost));
        Row("Fast Food Delivery", Cost(m.todayFastFoodCost));
        Row("Transport of People", Cost(m.todayTransportCost));
        Row("Motel / Lodging", Cost(m.todayLodgingCost));
        Row("Requested Workers", Cost(m.todayWorkerRequestCost));
        Row("Worker Training", Cost(m.todayWorkerTrainingCost));
        Row("Released Workers", Cost(0f));
        Row("Other", Cost(m.todayOtherExpenses));
        Row("Total", Cost(m.budgetSpent));

        // --- Live Status (order matches DisplayLiveStatusSection) ---
        int currentBudget = SatisfactionAndBudget.Instance != null ? SatisfactionAndBudget.Instance.GetCurrentBudget() : 0;
        Row("Current Budget", $"${currentBudget:N0}");
        Row("Food packs in transit", Live(d.GetCurrentFoodPacksInTransit()));
        Row("Kitchen production", Live(m.foodProduced));
        Row("Food waste", Live(m.foodWasted));
        Row("Need lodging", Live(d.GetCurrentPopulationNeedingLodging()));
        Row("Working", Live(d.GetCurrentWorkingWorkers()));
        Row("Waiting", Live(d.GetCurrentWaitingWorkers()));
        Row("Training", Live(d.GetCurrentTrainingWorkers()));
        Row("Released today", Live(d.GetTodayWorkersReleased()));
        Row("Need casework", Live(d.GetCurrentClientsNeedingCasework()));
        Row("In transit to casework", Live(d.GetCurrentPeopleInTransitToCasework()));

        // --- Final Summary (order matches AnimateFinalSatisfactionChanges / AnimateFinalEfficiencyChanges) ---
        Row("Overall Satisfaction", $"{m.finalSatisfactionValue:F0}/1000");
        Row("Satisfaction Change", Score(m.satisfactionChangeCalculated));
        Row("Overall Efficiency", $"{m.finalEfficiencyValue:F0}/1000");
        Row("Efficiency Change", Score(m.costEfficiencyChangeCalculated));
    }

    /// <summary>
    /// Logs the raw statistics and formulas behind every satisfaction/efficiency
    /// component for the day, including scores that aren't currently rendered
    /// anywhere on screen (e.g. the per-category Kitchen/Shelter/Worker/Budget
    /// Efficiency Scores). This is deliberately separate from
    /// LogDailyReportAsDisplayed() — that method is a screen transcript, this one
    /// is the "how we got this number" breakdown, read straight from currentMetrics
    /// (already fully populated by SaveCompletedReportToHistory() before this runs).
    /// </summary>
    void LogDailyReportScoreFormulas()
    {
        if (currentMetrics == null) return;
        var d = DailyReportData.Instance;
        if (d == null) return;

        int day = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;

        void F(string text) => GameLogPanel.Instance?.LogMetricsChange($"DAILY_REPORT_FORMULA | day={day} | {text}");

        // ── Satisfaction subscores (cumulative ratios, each weighted 20% of 1000) ──
        int foodConsumed = d.GetCumulativeFoodPacksConsumedByClients();
        int foodNeeded = d.GetCumulativeFoodPacksNeededByClients();
        F($"Food Satisfaction = (food packs consumed / needed) x 20% weight x 1000 | consumed={foodConsumed}, needed={foodNeeded} => score={currentMetrics.satFoodScore:F1}");

        int lodgingConsumed = d.GetCumulativeLodgingNightsConsumed();
        int lodgingNeeded = d.GetCumulativeLodgingNightsNeeded();
        F($"Lodging Satisfaction = (lodging-nights consumed / needed) x 20% weight x 1000 | consumed={lodgingConsumed}, needed={lodgingNeeded} => score={currentMetrics.satLodgingScore:F1}");

        int idleRounds = d.GetCumulativeIdleWorkerRounds();
        int workingRounds = d.GetCumulativeWorkingWorkerRounds();
        int trainingRounds = d.GetCumulativeTrainingWorkerRounds();
        int roundsElapsed = d.GetCumulativeRoundsElapsed();
        F($"Worker Use Satisfaction = (1-idle_ratio)/3 + working_ratio/3 + training_ratio/3, ratios vs {assumedTotalWorkerPoolSize} workers x {roundsElapsed} rounds elapsed, x 20% weight x 1000" +
          $" | idle_rounds={idleRounds}, working_rounds={workingRounds}, training_rounds={trainingRounds}" +
          $" => idle_sub={currentMetrics.workerIdleSatScore:F1}, working_sub={currentMetrics.workerWorkingSatScore:F1}, training_sub={currentMetrics.workerTrainingSatScore:F1}, total={currentMetrics.satWorkerScore:F1}");

        int wasted = d.GetCumulativeFoodPacksWasted();
        F($"Food Waste Penalty = (food packs wasted / (consumed+wasted)) x 20% weight x 1000 | wasted={wasted}, consumed={foodConsumed} => score={currentMetrics.satWasteScore:F1}");

        int caseworkAwaiting = d.GetCumulativeClientRoundsAwaitingCasework();
        int caseworkRequested = d.GetCumulativeClientsRequestedCasework();
        F($"Casework Satisfaction = (1 - client-rounds awaiting / total possible rounds) x 20% weight x 1000 | awaiting={caseworkAwaiting}, requested={caseworkRequested} => score={currentMetrics.satCaseworkScore:F1}");

        float satTotal = currentMetrics.satFoodScore + currentMetrics.satLodgingScore + currentMetrics.satWorkerScore + currentMetrics.satWasteScore + currentMetrics.satCaseworkScore;
        F($"Satisfaction Total (this calculation) = Food+Lodging+Worker+Waste+Casework = {currentMetrics.satFoodScore:F1}+{currentMetrics.satLodgingScore:F1}+{currentMetrics.satWorkerScore:F1}+{currentMetrics.satWasteScore:F1}+{currentMetrics.satCaseworkScore:F1} = {satTotal:F1}");

        // ── Cost-efficiency subscores (cumulative $/unit, each weighted 1/3 of 1000) ──
        float foodSpend = d.GetCumulativeFoodSpend();
        F($"Food Cost Efficiency = normalized($ spent / packs consumed) x 1/3 weight x 1000 | spent=${foodSpend:F0}, consumed={foodConsumed} => score={currentMetrics.costFoodScore:F1}");

        float lodgingSpend = d.GetCumulativeLodgingSpend();
        F($"Lodging Cost Efficiency = normalized($ spent / nights consumed) x 1/3 weight x 1000 | spent=${lodgingSpend:F0}, nights consumed={lodgingConsumed} => score={currentMetrics.costLodgingScore:F1}");

        float workerReqCost = d.GetCumulativeWorkerRequestCost();
        float workerTrainCost = d.GetCumulativeWorkerTrainingCost();
        F($"Worker Cost Efficiency = normalized($ spent / working-rounds) x 1/3 weight x 1000 | request cost=${workerReqCost:F0}, training cost=${workerTrainCost:F0}, working_rounds={workingRounds} => score={currentMetrics.costWorkerScore:F1}");

        float costTotal = currentMetrics.costFoodScore + currentMetrics.costLodgingScore + currentMetrics.costWorkerScore;
        F($"Cost Efficiency Total (this calculation) = Food+Lodging+Worker = {currentMetrics.costFoodScore:F1}+{currentMetrics.costLodgingScore:F1}+{currentMetrics.costWorkerScore:F1} = {costTotal:F1}");

        // ── Per-category Efficiency Scores — today-only (not cumulative), not currently rendered on screen ──
        F($"Kitchen Efficiency Score = 5.0 - (food packs in storage x 0.05) | food_in_storage={currentMetrics.currentFoodInStorage} => score={currentMetrics.kitchenEfficiencyScore:F1}");
        F($"Shelter Efficiency Score = (shelter occupancy rate - 50) x 0.1 | occupancy_rate={currentMetrics.shelterOccupancyRate:F1}% => score={currentMetrics.shelterEfficiencyScore:F1}");
        F($"Worker Efficiency Score = ((100 - idle worker rate) - 50) x 0.1 | idle_rate={currentMetrics.idleWorkerRate:F1}% => score={currentMetrics.workerEfficiencyScore:F1}");
        F($"Budget Efficiency Score = (70 - budget usage rate) x 0.2 | budget_usage={currentMetrics.budgetUsageRate:F1}% => score={currentMetrics.budgetEfficiencyScore:F1}");
        float categoryEffTotal = currentMetrics.kitchenEfficiencyScore + currentMetrics.shelterEfficiencyScore + currentMetrics.workerEfficiencyScore + currentMetrics.budgetEfficiencyScore;
        F($"Per-Category Efficiency Total (today only) = Kitchen+Shelter+Worker+Budget = {currentMetrics.kitchenEfficiencyScore:F1}+{currentMetrics.shelterEfficiencyScore:F1}+{currentMetrics.workerEfficiencyScore:F1}+{currentMetrics.budgetEfficiencyScore:F1} = {categoryEffTotal:F1}");

        // ── Final rollups ──
        float prevSatisfaction = currentMetrics.finalSatisfactionValue - currentMetrics.satisfactionChangeCalculated;
        F($"Satisfaction Change = new_running_total - previous_running_total = {currentMetrics.finalSatisfactionValue:F1} - {prevSatisfaction:F1} = {currentMetrics.satisfactionChangeCalculated:F1}");

        float prevEfficiency = currentMetrics.finalEfficiencyValue - currentMetrics.costEfficiencyChangeCalculated;
        F($"Efficiency Change = new_running_total - previous_running_total = {currentMetrics.finalEfficiencyValue:F1} - {prevEfficiency:F1} = {currentMetrics.costEfficiencyChangeCalculated:F1}");
    }

    IEnumerator DisplaySatisfactionSections()
    {
        yield return StartCoroutine(AnimateSectionElement(foodDeliveryTotal, currentMetrics.satFoodScore, "Food Satisfaction"));
        yield return StartCoroutine(AnimateSectionElement(foodDeliveryStatus,
            $"{currentMetrics.cumFoodPacksConsumedByClients}/{currentMetrics.cumFoodPacksNeededByClients} food packs consumed by clients (cumulative)."));

        yield return StartCoroutine(AnimateSectionElement(lodgingTotal, currentMetrics.satLodgingScore, "Lodging Satisfaction"));
        yield return StartCoroutine(AnimateSectionElement(lodgingStatus,
            $"{currentMetrics.cumLodgingNightsConsumed}/{currentMetrics.cumLodgingNightsNeeded} lodging-nights consumed by clients (cumulative)."));

        yield return StartCoroutine(AnimateSectionElement(workerTotal, currentMetrics.satWorkerScore, "Worker Use Satisfaction"));
        yield return StartCoroutine(AnimateSectionElement(workerStatus,
            $"Idle: {currentMetrics.cumIdleWorkerRounds} | Working: {currentMetrics.cumWorkingWorkerRounds} | Training: {currentMetrics.cumTrainingWorkerRounds}"));
        // worker subscores
        yield return StartCoroutine(AnimateSectionElement(idleWorker, currentMetrics.workerIdleSatScore, "Idle"));
        yield return StartCoroutine(AnimateSectionElement(workerWorkingElement, currentMetrics.workerWorkingSatScore, "Working"));
        yield return StartCoroutine(AnimateSectionElement(workerTrainingBonusElement, currentMetrics.workerTrainingSatScore, "Training"));

        yield return StartCoroutine(AnimateSectionElement(wasteTotal, currentMetrics.satWasteScore, "Food Waste Penalty"));
        yield return StartCoroutine(AnimateSectionElement(wasteStatus,
            $"{currentMetrics.cumFoodPacksWasted} of {currentMetrics.cumFoodPacksConsumedByClients + currentMetrics.cumFoodPacksWasted} food packs requested went to waste (cumulative)."));

        yield return StartCoroutine(AnimateSectionElement(caseworkTotal, currentMetrics.satCaseworkScore, "Casework Satisfaction"));
        yield return StartCoroutine(AnimateSectionElement(caseworkStatus,
            $"{currentMetrics.cumClientRoundsAwaitingCasework} client-rounds still awaiting casework, out of {currentMetrics.cumClientsRequestedCasework} clients who requested it."));
    }

    IEnumerator DisplayEfficiencySections()
    {
        yield return StartCoroutine(AnimateSectionElement(foodUtilizationTotal, currentMetrics.costFoodScore, "Food Cost Efficiency"));
        yield return StartCoroutine(AnimateSectionElement(foodUsageSummary,
            $"${currentMetrics.cumFoodSpend:F0} spent, {currentMetrics.cumFoodPacksConsumedByClients} packs consumed (cumulative)."));
       

        yield return StartCoroutine(AnimateSectionElement(shelterUtilizationTotal, currentMetrics.costLodgingScore, "Lodging Cost Efficiency"));
        yield return StartCoroutine(AnimateSectionElement(shelterUsageSummary,
            $"${currentMetrics.cumLodgingSpend:F0} spent, {currentMetrics.cumLodgingNightsConsumed} nights used (cumulative)."));


        yield return StartCoroutine(AnimateSectionElement(workerUtilizationTotal, currentMetrics.costWorkerScore, "Worker Cost Efficiency"));
        yield return StartCoroutine(AnimateSectionElement(workerUsageSummary,
            $"${(currentMetrics.cumWorkerRequestCost + currentMetrics.cumWorkerTrainingCost):F0} spent over {currentMetrics.cumWorkingWorkerRounds} working-rounds."));
    }

    //receipt
    IEnumerator DisplayReceiptSection()
    {
        yield return StartCoroutine(AnimateReceiptElement(receiptKitchen, currentMetrics.todayKitchenOpenCost, "Opened Kitchen"));
        yield return StartCoroutine(AnimateReceiptElement(receiptShelter, currentMetrics.todayShelterOpenCost, "Opened Shelter"));
        yield return StartCoroutine(AnimateReceiptElement(receiptCasework, currentMetrics.todayCaseworkOpenCost, "Opened Casework"));
        yield return StartCoroutine(AnimateReceiptElement(receiptFastFood, currentMetrics.todayFastFoodCost, "Fast Food Delivery"));
        yield return StartCoroutine(AnimateReceiptElement(receiptTransport, currentMetrics.todayTransportCost, "Transport of People"));
        yield return StartCoroutine(AnimateReceiptElement(receiptLodging, currentMetrics.todayLodgingCost, "Motel / Lodging"));
        yield return StartCoroutine(AnimateReceiptElement(receiptWorkerRequest, currentMetrics.todayWorkerRequestCost, "Requested Workers"));
        yield return StartCoroutine(AnimateReceiptElement(receiptWorkerTraining, currentMetrics.todayWorkerTrainingCost, "Worker Training"));
        yield return StartCoroutine(AnimateReceiptElement(receiptWorkerReleased, 0f, "Released Workers"));
        yield return StartCoroutine(AnimateReceiptElement(receiptOther, currentMetrics.todayOtherExpenses, "Other"));
        yield return StartCoroutine(AnimateReceiptElement(receiptTotal, currentMetrics.budgetSpent, "Total"));
    }

    IEnumerator AnimateReceiptElement(SectionElement element, float value, string labelValue)
    {
        if (element == null || element.layoutObject == null) yield break;

        if (element.numberText != null)
        {
            yield return StartCoroutine(AnimateCostNumberText(element.numberText, 0f, value));
        }

        if (element.labelText != null)
        {
            element.labelText.text = labelValue;
        }

        yield return StartCoroutine(FadeInElement(element));
    }

    /// <summary>
    /// Counts up a dollar value with no +/- sign, always in the neutral text color.
    /// Used for the "Today's Expenses" receipt, where every line is a cost, not a delta.
    /// </summary>
    IEnumerator AnimateCostNumberText(TextMeshProUGUI numberText, float fromValue, float toValue)
    {
        float elapsed = 0f;
        while (elapsed < numberCountDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / numberCountDuration;
            float currentValue = Mathf.Lerp(fromValue, toValue, progress);
            numberText.text = $"${currentValue:F0}";
            yield return null;
        }
        numberText.text = $"${toValue:F0}";
    }
    //end receipt

    //new bottom curr status
    IEnumerator DisplayLiveStatusSection()
    {
        UpdateCurrentBudgetText();

        var d = DailyReportData.Instance;
        if (d == null) yield break;

        yield return StartCoroutine(AnimateLiveElement(liveFoodInTransit, d.GetCurrentFoodPacksInTransit(), "Food packs in transit"));
        yield return StartCoroutine(AnimateLiveElement(liveKitchenProduction, currentMetrics.foodProduced, "Kitchen production"));
        yield return StartCoroutine(AnimateLiveElement(liveFoodWaste, currentMetrics.foodWasted, "Food waste"));

        yield return StartCoroutine(AnimateLiveElement(liveNeedLodging, d.GetCurrentPopulationNeedingLodging(), "Need lodging"));

        yield return StartCoroutine(AnimateLiveElement(liveWorkersWorking, d.GetCurrentWorkingWorkers(), "Working"));
        yield return StartCoroutine(AnimateLiveElement(liveWorkersWaiting, d.GetCurrentWaitingWorkers(), "Waiting"));
        yield return StartCoroutine(AnimateLiveElement(liveWorkersTraining, d.GetCurrentTrainingWorkers(), "Training"));
        yield return StartCoroutine(AnimateLiveElement(liveWorkersReleasedToday, d.GetTodayWorkersReleased(), "Released today"));

        yield return StartCoroutine(AnimateLiveElement(liveNeedCasework, d.GetCurrentClientsNeedingCasework(), "Need casework"));
        yield return StartCoroutine(AnimateLiveElement(liveInTransitToCasework, d.GetCurrentPeopleInTransitToCasework(), "In transit to casework"));
    }

    IEnumerator AnimateLiveElement(SectionElement element, int value, string labelValue)
    {
        if (element == null || element.layoutObject == null) yield break;

        if (element.numberText != null)
        {
            yield return StartCoroutine(AnimateLiveNumberText(element.numberText, 0f, value));
        }

        if (element.labelText != null)
        {
            element.labelText.text = labelValue;
        }

        yield return StartCoroutine(FadeInElement(element));
    }

    /// <summary>
    /// Counts up a plain integer with no +/- sign and no dollar sign.
    /// Used for the live status panel (worker counts, food pack counts, etc).
    /// </summary>
    IEnumerator AnimateLiveNumberText(TextMeshProUGUI numberText, float fromValue, float toValue)
    {
        float elapsed = 0f;
        while (elapsed < numberCountDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / numberCountDuration;
            float currentValue = Mathf.Lerp(fromValue, toValue, progress);
            numberText.text = $"{currentValue:F0}";
            yield return null;
        }
        numberText.text = $"{toValue:F0}";
    }

    void UpdateCurrentBudgetText()
    {
        if (currentBudgetDisplayText == null) return;
        int budget = SatisfactionAndBudget.Instance != null ? SatisfactionAndBudget.Instance.GetCurrentBudget() : 0;
        currentBudgetDisplayText.text = $"${budget:N0}";
    }
    // end bottom curr status
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
            // numberText.text = $"{sign}{currentValue:F1}";
            numberText.text = $"{sign}{currentValue:F0}";
            numberText.color = currentValue >= 0 ? positiveChangeColor : negativeChangeColor;

            yield return null;
        }

        string finalSign = toValue >= 0 ? "+" : "";
        // numberText.text = $"{finalSign}{toValue:F1}";
        numberText.text = $"{finalSign}{toValue:F0}";
        numberText.color = toValue >= 0 ? positiveChangeColor : negativeChangeColor;
    }

    IEnumerator AnimateFinalSatisfactionChanges()
    {
        if (satisfactionAnimationSection == null) yield break;

        // if (satisfactionValueText != null) satisfactionValueText.text = $"{currentSatisfaction:F1}";
         if (satisfactionValueText != null) satisfactionValueText.text = $"{currentSatisfaction:F0}/1000";
        if (satisfactionBar != null) satisfactionBar.value = currentSatisfaction / 1000f;

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
            // string changeText = satisfactionChange >= 0 ? $"+{satisfactionChange:F1}" : $"{satisfactionChange:F1}";
            string changeText = satisfactionChange >= 0 ? $"+{satisfactionChange:F0}" : $"{satisfactionChange:F0}";
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

        // if (efficiencyValueText != null) efficiencyValueText.text = $"{currentEfficiency:F1}";
        if (efficiencyValueText != null) efficiencyValueText.text = $"{currentEfficiency:F0}/1000";
        if (efficiencyBar != null) efficiencyBar.value = currentEfficiency / 1000f;

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
            // string changeText = efficiencyChange >= 0 ? $"+{efficiencyChange:F1}" : $"{efficiencyChange:F1}";
            string changeText = efficiencyChange >= 0 ? $"+{efficiencyChange:F0}" : $"{efficiencyChange:F0}";
            efficiencyChangeText.text = changeText;
            efficiencyChangeText.color = efficiencyChange >= 0 ? positiveChangeColor : negativeChangeColor;
        }

        if (efficiencyValueText != null && efficiencyBar != null)
            yield return StartCoroutine(AnimateFinalValue(efficiencyValueText, efficiencyBar, currentEfficiency, newEfficiency));

        currentEfficiency = newEfficiency;
    }

    IEnumerator AnimateFinalValue(TextMeshProUGUI valueText, Slider valueBar, float fromValue, float toValue)
    {
        valueBar.value = fromValue / 1000f;

        float elapsed = 0f;
        while (elapsed < barAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / barAnimationDuration;
            float currentValue = Mathf.Lerp(fromValue, toValue, progress);

            // valueText.text = $"{currentValue:F1}";
            valueText.text = $"{currentValue:F0}/1000";
            valueBar.value = currentValue / 1000f;

            yield return null;
        }

        // valueText.text = $"{toValue:F1}";
        valueText.text = $"{toValue:F0}/1000";
        valueBar.value = toValue / 1000f;
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
        if (DailyReportData.Instance == null || currentMetrics == null)
            return;

        var d = DailyReportData.Instance;

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

        int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;

        float freshSat = d.ComputeFreshSatisfactionTotal();
        float freshEff = d.ComputeFreshEfficiencyTotal();

        float satBefore = SatisfactionAndBudget.Instance.GetCurrentSatisfaction();
        float effBefore = SatisfactionAndBudget.Instance.GetCurrentEfficiency();

        SatisfactionAndBudget.Instance?.AddSatisfaction(freshSat - satBefore, $"Day {currentDay} report (waste + reconciliation)");
        SatisfactionAndBudget.Instance?.AddEfficiency(freshEff - effBefore, $"Day {currentDay} report (reconciliation)");

        d.SyncAppliedScoresToFresh();


        currentMetrics.satFoodScore = d.S_Food() * 0.2f * 1000f;
        currentMetrics.satLodgingScore = d.S_Lodging() * 0.2f * 1000f;
        currentMetrics.satWorkerScore = d.S_WorkerUse() * 0.2f * 1000f;
        currentMetrics.satWasteScore = d.S_Waste() * 0.2f * 1000f;
        currentMetrics.satCaseworkScore = d.S_Casework() * 0.2f * 1000f;

        var (idleScore, workingScore, trainingScore) = d.GetWorkerSatisfactionComponents();
        currentMetrics.workerIdleSatScore = idleScore;
        currentMetrics.workerWorkingSatScore = workingScore;
        currentMetrics.workerTrainingSatScore = trainingScore;

        currentMetrics.costFoodScore = d.C_Food() * (1f / 3f) * 1000f;
        currentMetrics.costLodgingScore = d.C_Lodging() * (1f / 3f) * 1000f;
        currentMetrics.costWorkerScore = d.C_Worker() * (1f / 3f) * 1000f;

        currentMetrics.finalSatisfactionValue = SatisfactionAndBudget.Instance.GetCurrentSatisfaction();
        currentMetrics.finalEfficiencyValue = SatisfactionAndBudget.Instance.GetCurrentEfficiency();   
        currentMetrics.satisfactionChangeCalculated = currentMetrics.finalSatisfactionValue - currentSatisfaction;
        currentMetrics.costEfficiencyChangeCalculated = currentMetrics.finalEfficiencyValue - currentEfficiency;

        currentMetrics.liveSatisfactionScore = currentMetrics.finalSatisfactionValue / 1000f;
        currentMetrics.liveCostEfficiencyScore = currentMetrics.finalEfficiencyValue / 1000f;

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

        currentMetrics.foodSatisfaction = CalculateFoodSatisfactionTotal();
        currentMetrics.lodgingSatisfaction = CalculateLodgingSatisfactionTotal();
        currentMetrics.workerSatisfaction = CalculateWorkerSatisfactionTotal();
        currentMetrics.foodEfficiency = CalculateKitchenEfficiencyScore();
        currentMetrics.shelterEfficiency = CalculateShelterEfficiencyScore();
        currentMetrics.workerEfficiency = CalculateWorkerUtilizationTotal();
        currentMetrics.budgetEfficiency = CalculateBudgetEfficiencyScore();

        d.SaveReportToHistory(currentDay, currentMetrics);

        Debug.Log($"Saved completed report for Day {currentDay} to history (final sat={currentMetrics.finalSatisfactionValue:F1}, eff={currentMetrics.finalEfficiencyValue:F1})");
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
        // worker subscores
        SetSectionValueFormatted(idleWorker, metrics.workerIdleSatScore);
        SetSectionValueFormatted(workerWorkingElement, metrics.workerWorkingSatScore);
        SetSectionValueFormatted(workerTrainingBonusElement, metrics.workerTrainingSatScore);

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

        //receipt
        SetSectionCostFormatted(receiptKitchen, metrics.todayKitchenOpenCost);
        SetSectionCostFormatted(receiptShelter, metrics.todayShelterOpenCost);
        SetSectionCostFormatted(receiptCasework, metrics.todayCaseworkOpenCost);
        SetSectionCostFormatted(receiptFastFood, metrics.todayFastFoodCost);
        SetSectionCostFormatted(receiptTransport, metrics.todayTransportCost);
        SetSectionCostFormatted(receiptLodging, metrics.todayLodgingCost);
        SetSectionCostFormatted(receiptWorkerRequest, metrics.todayWorkerRequestCost);
        SetSectionCostFormatted(receiptWorkerTraining, metrics.todayWorkerTrainingCost);
        SetSectionCostFormatted(receiptWorkerReleased, 0f);
        SetSectionCostFormatted(receiptOther, metrics.todayOtherExpenses);
        SetSectionCostFormatted(receiptTotal, metrics.budgetSpent);

        // curr status
        var d = DailyReportData.Instance;
        if (d != null)
        {
            SetSectionLiveFormatted(liveFoodInTransit, d.GetCurrentFoodPacksInTransit());
            SetSectionLiveFormatted(liveKitchenProduction, metrics.foodProduced);
            SetSectionLiveFormatted(liveFoodWaste, metrics.foodWasted);

            SetSectionLiveFormatted(liveNeedLodging, d.GetCurrentPopulationNeedingLodging());

            SetSectionLiveFormatted(liveWorkersWorking, d.GetCurrentWorkingWorkers());
            SetSectionLiveFormatted(liveWorkersWaiting, d.GetCurrentWaitingWorkers());
            SetSectionLiveFormatted(liveWorkersTraining, d.GetCurrentTrainingWorkers());
            SetSectionLiveFormatted(liveWorkersReleasedToday, d.GetTodayWorkersReleased());

            SetSectionLiveFormatted(liveNeedCasework, d.GetCurrentClientsNeedingCasework());
            SetSectionLiveFormatted(liveInTransitToCasework, d.GetCurrentPeopleInTransitToCasework());
        }

        UpdateCurrentBudgetText();
    }

    /// <summary>
    /// Set final satisfaction/efficiency from stored metrics (for historical view)
    /// </summary>
    void SetFinalValuesFromMetrics(DailyReportMetrics metrics)
    {
        // Satisfaction
        if (satisfactionValueText != null)
            // satisfactionValueText.text = $"{metrics.finalSatisfactionValue:F1}";
            satisfactionValueText.text = $"{metrics.finalSatisfactionValue:F0}/1000";
        
        if (satisfactionChangeText != null)
        {
            float change = metrics.satisfactionChangeCalculated;
            // satisfactionChangeText.text = change >= 0 ? $"+{change:F1}" : $"{change:F1}";
            satisfactionChangeText.text = change >= 0 ? $"+{change:F0}" : $"{change:F0}";
            satisfactionChangeText.color = change >= 0 ? positiveChangeColor : negativeChangeColor;
        }
        
        if (satisfactionBar != null)
        {
            satisfactionBar.value = metrics.finalSatisfactionValue / 1000f;
        }
        
        // Efficiency
        if (efficiencyValueText != null)
        {
            // efficiencyValueText.text = $"{metrics.finalEfficiencyValue:F1}";
            efficiencyValueText.text = $"{metrics.finalEfficiencyValue:F0}";
        }
            
        
        if (efficiencyBar != null)
        {
            efficiencyBar.value = metrics.finalEfficiencyValue / 1000f;
        }
        
        // Efficiency change (sum of efficiency components)
        if (efficiencyChangeText != null)
        {
            float effChange = metrics.costEfficiencyChangeCalculated; 
            // efficiencyChangeText.text = effChange >= 0 ? $"+{effChange:F1}" : $"{effChange:F1}";
            efficiencyChangeText.text = effChange >= 0 ? $"+{effChange:F0}" : $"{effChange:F0}";
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
        // element.numberText.text = $"{sign}{value:F1}";
        element.numberText.text = $"{sign}{value:F0}";
        
        // Set color based on positive/negative
        element.numberText.color = value >= 0 ? positiveChangeColor : negativeChangeColor;
        
        // Make visible
        if (element.canvasGroup != null)
            element.canvasGroup.alpha = 1f;
        if (element.layoutObject != null)
            element.layoutObject.SetActive(true);
    }

    //receipt

    /// <summary>
    /// Set a dollar-cost value (no +/- sign, no positive/negative color) on a section element
    /// and make it visible. Used for the "Today's Expenses" receipt.
    /// </summary>
    void SetSectionCostFormatted(SectionElement element, float value)
    {
        if (element == null || element.numberText == null) return;

        element.numberText.text = $"${value:F0}";

        if (element.canvasGroup != null)
            element.canvasGroup.alpha = 1f;
        if (element.layoutObject != null)
            element.layoutObject.SetActive(true);
    }
    //end receipt

    // new curr status
    /// <summary>
    /// Set a plain integer value (no sign, no $, no color coding) on a section element
    /// and make it visible. Used for the live status panel.
    /// </summary>
    void SetSectionLiveFormatted(SectionElement element, int value)
    {
        if (element == null || element.numberText == null) return;

        element.numberText.text = $"{value}";

        if (element.canvasGroup != null)
            element.canvasGroup.alpha = 1f;
        if (element.layoutObject != null)
            element.layoutObject.SetActive(true);
    }
    // end curr status

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
        ShowSectionElement(idleWorker);
        ShowSectionElement(workerWorkingElement);
        ShowSectionElement(workerTrainingBonusElement);

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
        
        //receipt
        ShowSectionElement(receiptKitchen);
        ShowSectionElement(receiptShelter);
        ShowSectionElement(receiptCasework);
        ShowSectionElement(receiptFastFood);
        ShowSectionElement(receiptTransport);
        ShowSectionElement(receiptLodging);
        ShowSectionElement(receiptWorkerRequest);
        ShowSectionElement(receiptWorkerTraining);
        ShowSectionElement(receiptWorkerReleased);
        ShowSectionElement(receiptOther);
        ShowSectionElement(receiptTotal);

        // curr status
        ShowSectionElement(liveFoodInTransit);
        ShowSectionElement(liveKitchenProduction);
        ShowSectionElement(liveFoodWaste);
        ShowSectionElement(liveNeedLodging);
        ShowSectionElement(liveWorkersWorking);
        ShowSectionElement(liveWorkersWaiting);
        ShowSectionElement(liveWorkersTraining);
        ShowSectionElement(liveWorkersReleasedToday);
        ShowSectionElement(liveNeedCasework);
        ShowSectionElement(liveInTransitToCasework);
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
        ResetSectionElement(idleWorker);
        ResetSectionElement(workerWorkingElement);
        ResetSectionElement(workerTrainingBonusElement);

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

        //receipt
        ResetSectionElement(receiptKitchen);
        ResetSectionElement(receiptShelter);
        ResetSectionElement(receiptCasework);
        ResetSectionElement(receiptFastFood);
        ResetSectionElement(receiptTransport);
        ResetSectionElement(receiptLodging);
        ResetSectionElement(receiptWorkerRequest);
        ResetSectionElement(receiptWorkerTraining);
        ResetSectionElement(receiptWorkerReleased);
        ResetSectionElement(receiptOther);
        ResetSectionElement(receiptTotal);

        // new curr status
        ResetSectionElement(liveFoodInTransit);
        ResetSectionElement(liveKitchenProduction);
        ResetSectionElement(liveFoodWaste);
        ResetSectionElement(liveNeedLodging);
        ResetSectionElement(liveWorkersWorking);
        ResetSectionElement(liveWorkersWaiting);
        ResetSectionElement(liveWorkersTraining);
        ResetSectionElement(liveWorkersReleasedToday);
        ResetSectionElement(liveNeedCasework);
        ResetSectionElement(liveInTransitToCasework);

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