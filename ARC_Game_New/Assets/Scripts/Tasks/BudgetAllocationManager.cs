using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PendingAllocation
{
    // Stable identity for state deltas (see CanonicalState). Restored from snapshots, never reused.
    public int allocId;
    public int amount;
    public int roundsRemaining;
    public string label; // for logging/UI

    static int nextAllocId = 1;

    public PendingAllocation(int amount, int delayRounds, string label)
        : this(nextAllocId++, amount, delayRounds, label) { }

    public PendingAllocation(int allocId, int amount, int delayRounds, string label)
    {
        // Snapshots written before ids existed carry 0; give those a fresh id rather than sharing 0.
        this.allocId = allocId > 0 ? allocId : nextAllocId++;
        this.amount = amount;
        this.roundsRemaining = delayRounds;
        this.label = label;
        if (this.allocId >= nextAllocId) nextAllocId = this.allocId + 1;
    }
}
 
public class BudgetAllocationManager : MonoBehaviour
{
    public static BudgetAllocationManager Instance { get; private set; }

    private List<PendingAllocation> pending = new List<PendingAllocation>();

    // Read-only view for UI (e.g. "incoming funds" display)
    public IReadOnlyList<PendingAllocation> PendingAllocations => pending.AsReadOnly();

    // ── save / restore ────────────────────────────────────────────────────────────────
    // Money already approved but not yet paid. This is the real credit path (DelayedBudget-
    // Manager is display-only -- BUG_REPORTS Part D), so a snapshot that omits it loses
    // funding the player has already earned, and the loss is invisible: the budget simply
    // never goes up on the round it was due.
    [System.Serializable]
    public class Snapshot
    {
        public List<PendingAllocation> pending = new List<PendingAllocation>();
    }

    public Snapshot CaptureState()
    {
        var s = new Snapshot();
        foreach (var p in pending)
            if (p != null) s.pending.Add(new PendingAllocation(p.allocId, p.amount, p.roundsRemaining, p.label));
        return s;
    }

    public void RestoreState(Snapshot s)
    {
        pending.Clear();
        if (s == null || s.pending == null) return;
        foreach (var p in s.pending)
            if (p != null) pending.Add(new PendingAllocation(p.allocId, p.amount, p.roundsRemaining, p.label));
    }

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