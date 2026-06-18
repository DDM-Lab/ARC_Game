using UnityEngine;

/// <summary>
/// Accumulates the raw cumulative quantities the (Python-side) reward function
/// needs, and exposes them via GameStatePayload.rewardMetrics. Unity only reports
/// facts; all scoring/weighting/clamping lives in Python so it can be retuned
/// without a rebuild.
///
/// Definitions (v1):
///  - Food/Lodging needs met are measured at task resolution: a Food/Lodging
///    Demand/Emergency task that COMPLETES counts as fulfilled; one that EXPIRES
///    incomplete counts as resolved-but-unfulfilled. Ratio = fulfilled / resolved.
///  - Worker allocation is snapshotted each round (working / training / idle) and
///    summed, giving cumulative person-rounds for the worker-use term and the
///    C(Worker) denominator.
///  - Category spend comes from SatisfactionAndBudget's cumulative accumulators.
///
/// Auto-instantiated (incl. headless/batch) so the gym always has it.
/// </summary>
public class RewardMetricsTracker : MonoBehaviour
{
    public static RewardMetricsTracker Instance { get; private set; }

    // Needs-met (task resolution, Food / Lodging tagged tasks only)
    private int foodResolved, foodFulfilled, lodgingResolved, lodgingFulfilled;
    // Casework / return-home: people who requested casework vs people actually processed home
    private int caseworkRequested, caseworkProcessed;
    // Worker allocation, summed across rounds (person-rounds)
    private long cumWorking, cumTraining, cumIdle;
    private int roundsCompleted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstantiate()
    {
        if (FindObjectOfType<RewardMetricsTracker>() != null) return;
        var go = new GameObject("[RewardMetricsTracker]");
        DontDestroyOnLoad(go);
        go.AddComponent<RewardMetricsTracker>();
    }

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); }
    }

    /// <summary>Called by GlobalClock at the end of each simulated round.</summary>
    public void OnRoundEnded()
    {
        roundsCompleted++;
        if (WorkerSystem.Instance == null) return;
        WorkerStatistics s = WorkerSystem.Instance.GetWorkerStatistics();
        cumWorking  += s.trainedWorking + s.untrainedWorking;
        cumTraining += s.untrainedTraining;
        cumIdle     += s.trainedFree + s.untrainedFree;
    }

    /// <summary>Record a Food/Lodging task being resolved (completed or expired).
    /// PEOPLE-BASED (B1/B2): resolved is credited by the task's demand quantity and fulfilled by
    /// how many were actually delivered/housed (task.deliveredQuantity), so the metric tracks
    /// people served, not choices clicked. A task that "completes" but delivered 0 (e.g. an
    /// immediate evac with no valid destination) adds demand but no fulfillment. If a task carries
    /// no demand quantity (non-quantified Food/Lodging task), fall back to the legacy 1-per-task
    /// count gated on `fulfilled`.</summary>
    public void RecordTaskResolution(GameTask task, bool fulfilled)
    {
        if (task == null) return;
        if (task.taskTag != TaskTag.Food && task.taskTag != TaskTag.Lodging) return;

        int demand = task.demandQuantity;
        int delivered = Mathf.Clamp(task.deliveredQuantity, 0, Mathf.Max(demand, task.deliveredQuantity));
        int resolvedAdd = demand > 0 ? demand : 1;
        int fulfilledAdd = demand > 0 ? Mathf.Min(delivered, demand) : (fulfilled ? 1 : 0);

        if (task.taskTag == TaskTag.Food)
        {
            foodResolved += resolvedAdd;
            foodFulfilled += fulfilledAdd;
        }
        else // Lodging
        {
            lodgingResolved += resolvedAdd;
            lodgingFulfilled += fulfilledAdd;
        }
    }

    /// <summary>Casework demand: N people requested casework (return-home) after their shelter/motel
    /// stay. Called when a casework request is generated.</summary>
    public void RecordCaseworkRequested(int people)
    {
        if (people > 0) caseworkRequested += people;
    }

    /// <summary>Casework throughput: N people were actually processed home via a casework site.</summary>
    public void RecordCaseworkProcessed(int people)
    {
        if (people > 0) caseworkProcessed += people;
    }

    /// <summary>A delivery that arrived AFTER its task already resolved still physically housed
    /// people — credit fulfillment retroactively (D4), capped so fulfilled never exceeds resolved.</summary>
    public void AddLateDelivery(GameTask task, int delivered)
    {
        if (task == null || delivered <= 0) return;
        if (task.taskTag == TaskTag.Food)
            foodFulfilled = Mathf.Min(foodResolved, foodFulfilled + delivered);
        else if (task.taskTag == TaskTag.Lodging)
            lodgingFulfilled = Mathf.Min(lodgingResolved, lodgingFulfilled + delivered);
    }

    public RewardMetrics BuildPayload()
    {
        int presentWorkers = 0;
        if (WorkerSystem.Instance != null)
        {
            WorkerStatistics s = WorkerSystem.Instance.GetWorkerStatistics();
            presentWorkers = s.trainedWorking + s.untrainedWorking
                           + s.untrainedTraining + s.trainedFree + s.untrainedFree;
        }
        var sb = SatisfactionAndBudget.Instance;
        return new RewardMetrics
        {
            foodResolved = foodResolved,
            foodFulfilled = foodFulfilled,
            lodgingResolved = lodgingResolved,
            lodgingFulfilled = lodgingFulfilled,
            caseworkRequested = caseworkRequested,
            caseworkProcessed = caseworkProcessed,
            cumWorkingWorkers = cumWorking,
            cumTrainingWorkers = cumTraining,
            cumIdleWorkers = cumIdle,
            roundsCompleted = roundsCompleted,
            daysCompleted = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1,
            totalWorkers = presentWorkers,
            foodSpend = sb != null ? sb.CumulativeFoodSpend : 0,
            lodgingSpend = sb != null ? sb.CumulativeLodgingSpend : 0,
            workerSpend = sb != null ? sb.CumulativeWorkerSpend : 0,
            caseworkSpend = sb != null ? sb.CumulativeCaseworkSpend : 0,
        };
    }
}
