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

    /// <summary>Record a Food/Lodging task being resolved (completed or expired).</summary>
    public void RecordTaskResolution(GameTask task, bool fulfilled)
    {
        if (task == null) return;
        if (task.taskTag == TaskTag.Food)
        {
            foodResolved++;
            if (fulfilled) foodFulfilled++;
        }
        else if (task.taskTag == TaskTag.Lodging)
        {
            lodgingResolved++;
            if (fulfilled) lodgingFulfilled++;
        }
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
            cumWorkingWorkers = cumWorking,
            cumTrainingWorkers = cumTraining,
            cumIdleWorkers = cumIdle,
            roundsCompleted = roundsCompleted,
            daysCompleted = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1,
            totalWorkers = presentWorkers,
            foodSpend = sb != null ? sb.CumulativeFoodSpend : 0,
            lodgingSpend = sb != null ? sb.CumulativeLodgingSpend : 0,
            workerSpend = sb != null ? sb.CumulativeWorkerSpend : 0,
        };
    }
}
