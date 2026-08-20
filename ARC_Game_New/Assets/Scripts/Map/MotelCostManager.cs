using UnityEngine;
using System.Collections;

/// <summary>
/// Charges the 
/// 's daily housing cost at the start of each new day.
/// Attach to any persistent GameObject in MainScene (e.g. the Motel itself
/// or a dedicated "Managers" object).
///
/// Inspector:
///   costPerPersonPerDay – dollars charged per motel resident per day (default $200)
///   motel               – drag the Motel PrebuiltBuilding here, or leave null
///                         to auto-find by name on Start
/// </summary>
public class MotelCostManager : MonoBehaviour
{
    [Header("Cost Settings")]
    [Tooltip("Dollars charged per motel resident per day")]
    public float costPerPersonPerDay = 200f;

    [Header("References (auto-found if blank)")]
    public PrebuiltBuilding motel;

    void Start()
    {
        if (GlobalClock.Instance != null)
            GlobalClock.Instance.OnDayChanged += OnDayChanged;
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
        if (GlobalClock.Instance != null)
            GlobalClock.Instance.OnDayChanged -= OnDayChanged;
    }

    void OnDayChanged(int newDay)
    {
        // Don't charge on the very first day transition (day 1 → 2 means day 1 costs apply)
        // OnDayChanged fires after ProceedToNextDay so newDay is already the new day number.
        // We charge for the day that just ended (newDay - 1).
        ChargeMotelCost();
    }

    void ChargeMotelCost()
    {
        EnsureMotelReference();
        if (motel == null || SatisfactionAndBudget.Instance == null) return;

        int residents = motel.GetCurrentPopulation();
        if (residents <= 0) return;

        float totalCost = residents * costPerPersonPerDay;

        SatisfactionAndBudget.Instance.RemoveBudget(
            (int)totalCost,
            $"Motel housing: {residents} residents × ${costPerPersonPerDay:F0}/day");
            
        if (DailyReportData.Instance != null)
        {
            DailyReportData.Instance.RecordLodgingSpendCumulative(totalCost);
            DailyReportData.Instance.RecordLodgingCostToday(totalCost);
        }
        // Toast notification
        //ToastManager.ShowToast(
        //    $"Motel cost: {residents} residents × ${costPerPersonPerDay:F0} = ${totalCost:F0} deducted",
        //    ToastType.Info, true);

        // Game log
        GameLogPanel.Instance?.LogMetricsChange(
            $"Motel daily cost charged: ${totalCost:F0} ({residents} residents × ${costPerPersonPerDay:F0}/person)");

        Debug.Log($"[MotelCostManager] Charged ${totalCost:F0} for {residents} motel residents.");
        StartCoroutine(ShowToastDelayed(residents, totalCost));
    }

    /// <summary>Returns the cost that would be charged right now (for display in FacilityInfoPanel).</summary>
    public float GetCurrentDailyCost()
    {
        EnsureMotelReference();
        if (motel == null) return 0f;
        return motel.GetCurrentPopulation() * costPerPersonPerDay;
    }

    private IEnumerator ShowToastDelayed(int residents, float totalCost)
    {
        // Wait until the end of the frame (or yield return null) 
        // to let ToastManager finish clearing the old turn's elements.
        yield return new WaitForEndOfFrame();

        ToastManager.ShowToast(
            $"Motel cost: {residents} residents × ${costPerPersonPerDay:F0} = ${totalCost:F0} deducted",
            ToastType.Info, true);
    }
}
