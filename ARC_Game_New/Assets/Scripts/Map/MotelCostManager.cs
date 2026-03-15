using UnityEngine;

/// <summary>
/// Charges the motel's daily housing cost at the start of each new day.
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
        if (motel == null)
        {
            // Find the Motel PrebuiltBuilding in the scene
            foreach (var pb in FindObjectsOfType<PrebuiltBuilding>())
            {
                if (pb.GetPrebuiltType() == PrebuiltBuildingType.Motel)
                {
                    motel = pb;
                    break;
                }
            }
        }

        if (GlobalClock.Instance != null)
            GlobalClock.Instance.OnDayChanged += OnDayChanged;
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
        if (motel == null || SatisfactionAndBudget.Instance == null) return;

        int residents = motel.GetCurrentPopulation();
        if (residents <= 0) return;

        float totalCost = residents * costPerPersonPerDay;

        SatisfactionAndBudget.Instance.RemoveBudget(
            (int)totalCost,
            $"Motel housing: {residents} residents × ${costPerPersonPerDay:F0}/day");

        // Toast notification
        ToastManager.ShowToast(
            $"Motel cost: {residents} residents × ${costPerPersonPerDay:F0} = ${totalCost:F0} deducted",
            ToastType.Info, true);

        // Game log
        GameLogPanel.Instance?.LogMetricsChange(
            $"Motel daily cost charged: ${totalCost:F0} ({residents} residents × ${costPerPersonPerDay:F0}/person)");

        Debug.Log($"[MotelCostManager] Charged ${totalCost:F0} for {residents} motel residents.");
    }

    /// <summary>Returns the cost that would be charged right now (for display in FacilityInfoPanel).</summary>
    public float GetCurrentDailyCost()
    {
        if (motel == null) return 0f;
        return motel.GetCurrentPopulation() * costPerPersonPerDay;
    }
}
