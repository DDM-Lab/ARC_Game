using UnityEngine;
using System.Linq;

/// <summary>
/// Communities have no food consumption rate (see BuildingResourceStorage.enablePopulationBasedConsumption
/// on CommunityPrefab — they're treated as always having enough food internally). Instead, each round
/// there's a chance a community loses a chunk of its food to an unforeseen event, and that directly spawns
/// an emergency request to replace what was lost. The depletion amount recorded at each successful roll IS
/// the community's food demand for the day; there is no separate population x rate calculation.
///
/// This intentionally bypasses TaskDatabase's normal trigger evaluation (round/day/resource/probability) —
/// Community_FoodRequest's own trigger lists are left empty so the generic per-round generation pass never
/// picks it up. This manager is the only thing that ever creates that task, by calling
/// TaskSystem.CreateTaskFromDatabase directly, so there is exactly one path and no risk of double-spawning.
/// </summary>
public class CommunityFoodDepletionManager : MonoBehaviour
{
    [Header("Task Template")]
    [Tooltip("Community_FoodRequest (or equivalent). Its own round/day/resource/probability triggers are not used — this manager decides when to spawn it.")]
    public TaskData communityFoodRequestTask;

    [Header("Depletion")]
    [Tooltip("Food packs lost when a depletion event occurs.")]
    public int depletionAmount = 100;

    [Tooltip("Chance, per community, per round, that a depletion event occurs. Overwritten at Start by GameConfigLoader's initialFoodDemandFrequency if that's configured (>= 0).")]
    [Range(0f, 1f)]
    public float depletionChancePerRound = 0.2f;

    [Tooltip("Communities only lose/request food during these rounds of the day (1-4, matching the on-screen round number). After this round, no depletion or new requests happen until tomorrow.")]
    public int lastEligibleRound = 3;

    [Tooltip("No depletion/requests before this day (1-indexed, matching the on-screen day number) — Day 1 is excluded by default so nothing fires before the player has had a turn.")]
    public int firstEligibleDay = 2;

    [Header("Debug")]
    public bool showDebugInfo = true;

    public static CommunityFoodDepletionManager Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        if (GameConfigLoader.Instance != null)
        {
            float configured = GameConfigLoader.Instance.GetInitialFoodDemandFrequency();
            if (configured >= 0f) depletionChancePerRound = configured;
        }

        if (GlobalClock.Instance != null)
            GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;

        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCompleted += HandleCommunityFoodRequestEnded;
            TaskSystem.Instance.OnTaskExpired += HandleCommunityFoodRequestEnded;
        }
    }

    void OnDestroy()
    {
        if (GlobalClock.Instance != null)
            GlobalClock.Instance.OnTimeSegmentChanged -= OnRoundChanged;

        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCompleted -= HandleCommunityFoodRequestEnded;
            TaskSystem.Instance.OnTaskExpired -= HandleCommunityFoodRequestEnded;
        }
    }

    /// <summary>
    /// Fires on every task completion/expiry in the game (OnTaskCompleted also covers the
    /// mid-delivery-failure and timed-out-in-progress paths, both of which end a task as
    /// Incomplete just like OnTaskExpired does for an unanswered Demand task) — filtered down to
    /// just this community's food-request task ending as Incomplete. Rather than silently waiting
    /// for the next random depletion roll to happen to notice the shortfall, immediately spawn a
    /// replacement request for whatever's still missing. This can repeat indefinitely if the
    /// follow-up also fails — each attempt is still bounded by the round/day eligibility window
    /// and can never stack with an already-pending request.
    /// </summary>
    void HandleCommunityFoodRequestEnded(GameTask task)
    {
        if (communityFoodRequestTask == null || task == null) return;
        if (task.status != TaskStatus.Incomplete) return;
        if (task.taskTitle != communityFoodRequestTask.taskTitle) return;

        PrebuiltBuilding community = FindObjectsOfType<PrebuiltBuilding>()
            .FirstOrDefault(p => p.GetPrebuiltType() == PrebuiltBuildingType.Community && p.name == task.affectedFacility);
        if (community == null) return;

        if (GlobalClock.Instance != null)
        {
            if (GlobalClock.Instance.GetCurrentDay() < firstEligibleDay) return;
            int currentRoundInDay = GlobalClock.Instance.GetCurrentTimeSegment() + 1;
            if (currentRoundInDay > lastEligibleRound) return;
        }

        if (HasPendingRequest(community)) return; // safety net; shouldn't happen right as this one just ended

        BuildingResourceStorage storage = community.GetResourceStorage();
        if (storage == null) return;

        int requestAmount = storage.GetAvailableSpace(ResourceType.FoodPacks);
        if (requestAmount <= 0) return; // already full

        if (showDebugInfo)
            Debug.Log($"[CommunityFoodDepletionManager] {community.name}'s food request failed — immediately following up.");
        SpawnRequestTask(community, lostAmount: 0, requestAmount: requestAmount);
    }

    void OnRoundChanged(int newSegment)
    {
        if (communityFoodRequestTask == null || TaskSystem.Instance == null) return;

        if (GlobalClock.Instance != null && GlobalClock.Instance.GetCurrentDay() < firstEligibleDay) return;

        int currentRoundInDay = newSegment + 1; // segments are 0-indexed; rounds shown to the player are 1-indexed
        if (currentRoundInDay > lastEligibleRound) return;

        foreach (PrebuiltBuilding community in FindObjectsOfType<PrebuiltBuilding>()
            .Where(p => p.GetPrebuiltType() == PrebuiltBuildingType.Community))
        {
            TryDeplete(community);
            TopUpShortfall(community);
        }
    }

    /// <summary>
    /// A request is only ever created by a successful roll (TryDeplete) or by a failed request's
    /// follow-up, and it is sized once, at creation. A loss that lands while a request is pending
    /// (deduped in TryDeplete), or a request that completes short of the full shortfall, would
    /// otherwise be left uncovered until some later roll happens to succeed. So at the start of every
    /// eligible round (same day/round window as the rolls), any community that is short of food and has
    /// nothing already covering it gets a request for the current shortfall. "Covering it" means a
    /// pending or in-progress request, or food already queued/in transit to it — requesting again then
    /// would double-book the same shortfall.
    /// </summary>
    void TopUpShortfall(PrebuiltBuilding community)
    {
        if (HasPendingRequest(community)) return; // pending or in progress (both stay in the active list)

        BuildingResourceStorage storage = community.GetResourceStorage();
        if (storage == null) return;

        int requestAmount = storage.GetAvailableSpace(ResourceType.FoodPacks);
        if (requestAmount <= 0) return; // not short

        if (DeliverySystem.Instance != null
            && DeliverySystem.Instance.GetReservedIncomingQuantity(community, ResourceType.FoodPacks) > 0)
            return; // food is already on its way

        SpawnRequestTask(community, lostAmount: 0, requestAmount: requestAmount);
    }

    [ContextMenu("Debug: Force Depletion On All Communities")]
    public void DebugForceDepletionAllCommunities()
    {
        if (communityFoodRequestTask == null || TaskSystem.Instance == null)
        {
            Debug.LogWarning("[CommunityFoodDepletionManager] Cannot force depletion — task template or TaskSystem missing.");
            return;
        }

        foreach (PrebuiltBuilding community in FindObjectsOfType<PrebuiltBuilding>()
            .Where(p => p.GetPrebuiltType() == PrebuiltBuildingType.Community))
        {
            TryDeplete(community, force: true);
        }
    }

    void TryDeplete(PrebuiltBuilding community, bool force = false)
    {
        if (!force && Random.value >= depletionChancePerRound) return; // routine miss — not logged, would fire every community every round

        BuildingResourceStorage storage = community.GetResourceStorage();
        if (storage == null)
        {
            Debug.LogWarning($"[CommunityFoodDepletionManager] {community.name} has no BuildingResourceStorage — cannot deplete/request food.");
            GameLogPanel.Instance?.LogError($"{community.name} has no BuildingResourceStorage — depletion event skipped.");
            return;
        }

        // A successful roll IS the community's food demand: record the full depletionAmount here, once,
        // whatever happens next (stock already empty, a request already pending, a request expiring and
        // being re-raised). Requests and follow-ups never record demand themselves — see SpawnRequestTask.
        DailyReportData.Instance?.RecordCommunityFoodDemand(community.name, depletionAmount);

        int available = storage.GetResourceAmount(ResourceType.FoodPacks);
        if (available <= 0)
        {
            if (showDebugInfo)
                Debug.Log($"[CommunityFoodDepletionManager] {community.name} rolled a depletion event but had no food packs left to lose.");
            GameLogPanel.Instance?.LogResourceChange($"{community.name} rolled a depletion event but had no food packs left to lose.");
            return;
        }

        int lost = storage.RemoveResource(ResourceType.FoodPacks, Mathf.Min(depletionAmount, available));
        if (lost <= 0)
        {
            Debug.LogWarning($"[CommunityFoodDepletionManager] {community.name} depletion event resolved but removed 0 food packs unexpectedly.");
            GameLogPanel.Instance?.LogError($"{community.name} depletion event removed 0 food packs unexpectedly.");
            return;
        }

        // Food keeps depleting every eligible round regardless of a pending request — otherwise
        // leaving/letting a task expire would freeze the community's stock in place with nothing
        // further happening. Only the TASK is deduped to one at a time; once the pending one
        // resolves, the follow-up (HandleCommunityFoodRequestEnded) picks up the full, now-larger
        // shortfall via a live GetAvailableSpace() read, so nothing here needs to track or sum it.
        if (HasPendingRequest(community))
        {
            if (showDebugInfo)
                Debug.Log($"[CommunityFoodDepletionManager] {community.name} lost {lost} more food packs, but already has a pending request — not spawning another.");
            GameLogPanel.Instance?.LogResourceChange($"{community.name} lost {lost} more food packs to an unforeseen event while a food request was already pending.");
            return;
        }

        // Request enough to top back up to full capacity, not just what this one event lost —
        // if capacity isn't configured for this storage (GetResourceCapacity <= 0), fall back to
        // replacing exactly what was lost, same as before.
        int requestAmount = storage.GetResourceCapacity(ResourceType.FoodPacks) > 0
            ? storage.GetAvailableSpace(ResourceType.FoodPacks)
            : lost;

        SpawnRequestTask(community, lost, requestAmount);
    }

    void SpawnRequestTask(PrebuiltBuilding community, int lostAmount, int requestAmount)
    {
        // lostAmount == 0 means no new food was lost: this is either an immediate follow-up after a
        // prior request failed, or a top-up of a shortfall left over from earlier losses
        // (TopUpShortfall) — either way we're just asking for whatever's still missing.
        string causeText = lostAmount > 0 ? $"lost {lostAmount} food packs" : "is still short of food";

        GameTask task = TaskSystem.Instance.CreateTaskFromDatabase(communityFoodRequestTask, community);
        if (task == null)
        {
            Debug.LogWarning($"[CommunityFoodDepletionManager] {community.name} {causeText} but the replacement task failed to create.");
            GameLogPanel.Instance?.LogError($"{community.name} {causeText} but the replacement request failed to create.");
            return;
        }

        // Request enough to refill to capacity — override every food-delivering choice on this
        // task instance (the template's authored deliveryQuantity is just a placeholder/default).
        foreach (AgentChoice choice in task.agentChoices)
        {
            if (choice.deliveryCargoType == ResourceType.FoodPacks)
                choice.deliveryQuantity = requestAmount;
        }
        task.foodAmount = requestAmount;

        // Demand is NOT recorded here: it is recorded once per successful roll in TryDeplete, so a
        // follow-up after an expired request (or a request sized to a larger shortfall) can't count
        // the same food twice.

        if (showDebugInfo)
            Debug.Log($"[CommunityFoodDepletionManager] {community.name} {causeText} — requesting {requestAmount} to refill to capacity.");
        GameLogPanel.Instance?.LogTaskEvent($"{community.name} {causeText} — requesting {requestAmount} to refill to capacity.");
    }

    bool HasPendingRequest(PrebuiltBuilding community)
    {
        return TaskSystem.Instance.GetAllActiveTasks()
            .Any(t => t.taskTitle == communityFoodRequestTask.taskTitle && t.affectedFacility == community.name);
    }
}
