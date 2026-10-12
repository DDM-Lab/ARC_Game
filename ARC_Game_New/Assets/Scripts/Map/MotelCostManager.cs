using UnityEngine;
using System.Collections;

/// <summary>
/// Charges the Motel's housing cost once per ROUND (GlobalClock.OnRoundEnd), at
/// costPerPersonPerDay / roundsPerDay for every resident present at that round's end.
/// A resident who stays a full day is therefore charged exactly costPerPersonPerDay,
/// the same as the old once-per-day charge, but a resident who only stays part of a day
/// is charged only for the rounds they were actually there.
/// Attach to any persistent GameObject in MainScene (e.g. the Motel itself
/// or a dedicated "Managers" object).
///
/// Inspector:
///   costPerPersonPerDay – dollars charged per motel resident per day (default $200),
///                         split evenly across the day's rounds
///   motel               – drag the Motel PrebuiltBuilding here, or leave null
///                         to auto-find by name on Start
/// </summary>
public class MotelCostManager : MonoBehaviour
{
    [Header("Cost Settings")]
    [Tooltip("Dollars charged per motel resident per day (billed in equal per-round installments)")]
    public float costPerPersonPerDay = 200f;

    [Header("References (auto-found if blank)")]
    public PrebuiltBuilding motel;

    // The budget only takes whole dollars, so a per-round share that isn't a whole number
    // (e.g. $250/day over 4 rounds = $62.50) carries its fraction into the next round's
    // deduction instead of being truncated away — keeps the budget's total equal to the exact
    // per-day figure. The lodging cost-efficiency score is fed the exact float amount directly.
    private float unbilledFraction = 0f;

    void Start()
    {
        GlobalClock.OnRoundEnd += OnRoundEnd;
    }

    void EnsureMotelReference()
    {
        if (motel != null) return;
        foreach (var pb in FindObjectsOfType<PrebuiltBuilding>())
        {
            if (pb.GetPrebuiltType() == PrebuiltBuildingType.Motel)
            {
                motel = pb;
                break;
            }
        }
    }

    void OnDestroy()
    {
        GlobalClock.OnRoundEnd -= OnRoundEnd;
    }

    void OnRoundEnd()
    {
        ChargeMotelCost();
    }

    int GetRoundsPerDay()
    {
        int rounds = GlobalClock.Instance != null ? GlobalClock.Instance.roundsPerDay : 4;
        return Mathf.Max(1, rounds);
    }

    /// <summary>Per-person charge for a single round.</summary>
    public float GetCostPerPersonPerRound() => costPerPersonPerDay / GetRoundsPerDay();

    void ChargeMotelCost()
    {
        EnsureMotelReference();
        if (motel == null || SatisfactionAndBudget.Instance == null) return;

        int residents = motel.GetCurrentPopulation();
        if (residents <= 0) return;

        float costPerRound = GetCostPerPersonPerRound();
        float totalCost = residents * costPerRound;

        float owed = totalCost + unbilledFraction;
        int billed = Mathf.FloorToInt(owed);
        unbilledFraction = owed - billed;

        if (billed > 0)
        {
            SatisfactionAndBudget.Instance.RemoveBudget(
                billed,
                SatisfactionAndBudget.SpendCategory.Lodging,
                $"Motel housing: {residents} residents × ${costPerRound:0.##}/round");
        }

        if (DailyReportData.Instance != null)
        {
            DailyReportData.Instance.RecordLodgingSpendCumulative(totalCost);
            DailyReportData.Instance.RecordLodgingCostToday(totalCost);
        }

        // Game log
        GameLogPanel.Instance?.LogMetricsChange(
            $"Motel round cost charged: ${totalCost:0.##} ({residents} residents × ${costPerRound:0.##}/person/round)");

        Debug.Log($"[MotelCostManager] Charged ${totalCost:0.##} for {residents} motel residents this round.");
        StartCoroutine(ShowToastDelayed(residents, costPerRound, totalCost));
    }

    /// <summary>Returns the cost that would be charged right now (for display in FacilityInfoPanel).</summary>
    public float GetCurrentDailyCost()
    {
        EnsureMotelReference();
        if (motel == null) return 0f;
        return motel.GetCurrentPopulation() * costPerPersonPerDay;
    }

    private IEnumerator ShowToastDelayed(int residents, float costPerRound, float totalCost)
    {
        // Wait until the end of the frame (or yield return null)
        // to let ToastManager finish clearing the old turn's elements.
        yield return new WaitForEndOfFrame();

        ToastManager.ShowToast(
            $"Motel cost: {residents} residents × ${costPerRound:0.##} = ${totalCost:0.##} deducted",
            ToastType.Info, true);
    }
}
