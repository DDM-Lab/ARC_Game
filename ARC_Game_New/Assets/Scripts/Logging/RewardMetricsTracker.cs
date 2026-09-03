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

    /// <summary>Zero every cumulative accumulator for a fresh episode. This object is
    /// auto-instantiated once per process via RuntimeInitializeOnLoadMethod and is
    /// DontDestroyOnLoad, so a gym scene-reload reset does NOT recreate it — the gym
    /// reset path must call this explicitly, or metrics leak across episodes.</summary>
    public void ResetForNewEpisode()
    {
        foodResolved = foodFulfilled = lodgingResolved = lodgingFulfilled = 0;
        caseworkRequested = caseworkProcessed = 0;
        cumWorking = cumTraining = cumIdle = 0;
        roundsCompleted = 0;
    }

    /// <summary>
    /// Snapshot support. These accumulators are private and cumulative, and a gym
    /// reset zeroes them, so a state restore MUST write them back explicitly and AFTER
    /// ResetForNewEpisode() has run -- otherwise every load silently resets cumulative
    /// spend and needs-met, and all post-load scoring is wrong while looking plausible.
    /// </summary>
    [System.Serializable]
    public class Snapshot
    {
        public int foodResolved, foodFulfilled, lodgingResolved, lodgingFulfilled;
        public int caseworkRequested, caseworkProcessed;
        public long cumWorking, cumTraining, cumIdle;
        public int roundsCompleted;
    }

    public Snapshot CaptureState() => new Snapshot
    {
        foodResolved = foodResolved, foodFulfilled = foodFulfilled,
        lodgingResolved = lodgingResolved, lodgingFulfilled = lodgingFulfilled,
        caseworkRequested = caseworkRequested, caseworkProcessed = caseworkProcessed,
        cumWorking = cumWorking, cumTraining = cumTraining, cumIdle = cumIdle,
        roundsCompleted = roundsCompleted,
    };

    public void RestoreState(Snapshot s)
    {
        if (s == null) return;
        foodResolved = s.foodResolved; foodFulfilled = s.foodFulfilled;
        lodgingResolved = s.lodgingResolved; lodgingFulfilled = s.lodgingFulfilled;
        caseworkRequested = s.caseworkRequested; caseworkProcessed = s.caseworkProcessed;
        cumWorking = s.cumWorking; cumTraining = s.cumTraining; cumIdle = s.cumIdle;
        roundsCompleted = s.roundsCompleted;
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

        // Every resolution, with the numbers that produce the counters. The port matches
        // lodgingFulfilled but not lodgingResolved, which means Unity resolves a task the
        // port does not -- this says which one, and whether it delivered anything.
        SnapshotDebug.MarkContext("task:resolved", "{\"id\":" + task.taskId + ",\"title\":\"" + task.taskTitle
            + "\",\"tag\":\"" + task.taskTag
            + "\",\"demand\":" + task.demandQuantity
            + ",\"delivered\":" + task.deliveredQuantity
            + ",\"fulfilled\":" + (fulfilled ? "true" : "false")
            + ",\"status\":\"" + task.status + "\"}");
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

    /// <summary>Retroactively credit ONE fulfilled Food task whose delivery arrived after the task
    /// already closed unfulfilled (Fix 2a). Food is on the legacy per-task metric (demandQuantity==0,
    /// so foodResolved counts TASKS, not packs) — hence +1 task here, NOT AddLateDelivery's pack count,
    /// which would corrupt the rate. Capped so fulfilled never exceeds resolved; caller must fire this
    /// once per task (gated on AreAllLinkedDeliveriesComplete).</summary>
    public void AddLateFoodTask(GameTask task)
    {
        if (task == null || task.taskTag != TaskTag.Food) return;
        foodFulfilled = Mathf.Min(foodResolved, foodFulfilled + 1);
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
