using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PendingAllocation
{
    public int amount;
    public int roundsRemaining;
    public string label; // for logging/UI

    public PendingAllocation(int amount, int delayRounds, string label)
    {
        this.amount = amount;
        this.roundsRemaining = delayRounds;
        this.label = label;
    }
}
 
public class BudgetAllocationManager : MonoBehaviour
{
    public static BudgetAllocationManager Instance { get; private set; }

    private List<PendingAllocation> pending = new List<PendingAllocation>();

    // Read-only view for UI (e.g. "incoming funds" display)
    public IReadOnlyList<PendingAllocation> PendingAllocations => pending.AsReadOnly();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void OnEnable()
    {
        // Hook into round transitions - wire this to however GlobalClock fires round changes
        GlobalClock.OnRoundEnd += OnRoundEnd;
    }

    void OnDisable()
    {
        GlobalClock.OnRoundEnd -= OnRoundEnd;
    }

    /// <summary>
    /// Schedule a budget amount to arrive after N rounds.
    /// Pass delayRounds = 0 to apply immediately.
    /// </summary>
    public void ScheduleAllocation(int amount, int delayRounds, string label)
    {
        if (delayRounds <= 0)
        {
            ApplyNow(amount, label);
            return;
        }

        pending.Add(new PendingAllocation(amount, delayRounds, label));

        GameLogPanel.Instance?.LogMetricsChange(
            $"[Budget] ${amount:N0} scheduled — arriving in {delayRounds} round(s) ({label})");
    }

    void OnRoundEnd()
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            pending[i].roundsRemaining--;

            if (pending[i].roundsRemaining <= 0)
            {
                ApplyNow(pending[i].amount, pending[i].label);
                pending.RemoveAt(i);
            }
        }
    }

    void ApplyNow(int amount, string label)
    {
        if (SatisfactionAndBudget.Instance != null)
        {
            SatisfactionAndBudget.Instance.AddBudget(amount, label);
            DailyReportData.Instance?.RecordBudgetReceived(amount);
            GameLogPanel.Instance?.LogMetricsChange(
                $"[Budget] ${amount:N0} arrived — {label}");
            ToastManager.ShowToast($"${amount:N0} funding arrived: {label}", ToastType.Info, true);
        }
    }
}