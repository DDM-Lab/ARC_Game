using UnityEngine;
using System;
using System.Collections.Generic;

[System.Serializable]
public class DelayedBudgetItem
{
    public int id;
    public string sourceTaskTitle;
    public int amount;
    public int roundsRemaining;

    public DelayedBudgetItem(int id, string title, int amount, int rounds)
    {
        this.id = id;
        this.sourceTaskTitle = title;
        this.amount = amount;
        this.roundsRemaining = rounds;
    }
}

public class DelayedBudgetManager : MonoBehaviour
{
    public static DelayedBudgetManager Instance { get; private set; }

    public List<DelayedBudgetItem> activeDelayedBudgets = new List<DelayedBudgetItem>();
    public event Action OnBudgetQueueChanged;

    private int nextId = 1;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void OnEnable()
    {
        GlobalClock.OnRoundEnd += AdvanceRound;
    }

    void OnDisable()
    {
        GlobalClock.OnRoundEnd -= AdvanceRound;
    }

    public void AddDelayedBudget(string taskTitle, int amount, int delayRounds)
    {
        if (delayRounds <= 0 || amount == 0) return;

        DelayedBudgetItem item = new DelayedBudgetItem(nextId++, taskTitle, amount, delayRounds);
        activeDelayedBudgets.Add(item);
        OnBudgetQueueChanged?.Invoke();
        DeliveryQueuePanel.Instance?.OnItemAdded(item);
    }

    /// <summary>
    /// Trigger when GLobalClock goes to next round to update rounds remaining for incoming budget
    /// </summary>
    public void AdvanceRound()
    {
        for (int i = activeDelayedBudgets.Count - 1; i >= 0; i--)
        {
            activeDelayedBudgets[i].roundsRemaining--;

            if (activeDelayedBudgets[i].roundsRemaining <= 0)
            {
                activeDelayedBudgets.RemoveAt(i);
            }
        }
        OnBudgetQueueChanged?.Invoke();
    }
}