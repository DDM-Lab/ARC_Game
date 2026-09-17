using UnityEngine;
using System.Linq;

/// <summary>
/// Communities have no food consumption rate (see BuildingResourceStorage.enablePopulationBasedConsumption
/// on CommunityPrefab — they're treated as always having enough food internally). Instead, each round
/// there's a chance a community loses a chunk of its food to an unforeseen event, and that directly spawns
/// an emergency request to replace exactly what was lost — that request IS the community's food demand for
/// the day; there is no separate population x rate calculation.
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
        // PARITY BUILD (ledger D18): upstream's synchronous read straight off the loader. Ours
        // waits for GameDataManager so the SHEET's value wins; upstream reads the loader's own
        // field at Start, which is still the fallback then. Different source, different depletion
        // chance, and a depletion hit spawns a food-request task -- so this changes not just an
        // outcome but how many tasks exist to be evaluated on later rounds, and with them how
        // many draws come off the shared stream.
        if (GameConfigLoader.Instance != null)
        {
            float configured = GameConfigLoader.Instance.GetInitialFoodDemandFrequency();
            if (configured >= 0f) depletionChancePerRound = configured;
        }

        if (GlobalClock.Instance != null)
        {
            // PARITY BUILD (ledger D18): no OnDayStarted subscription. It existed because the A1
            // clock fix moved the segment-0 tick onto OnDayStarted; with that fix reverted (D16)
            // the rollover raises OnTimeSegmentChanged(0) again, as upstream does.
            GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;
        }
    }

    System.Collections.IEnumerator ApplyConfiguredChance()
    {
        while (GameDataManager.Instance == null || !GameDataManager.Instance.IsDataReady)
            yield return null;
        float configured = GameDataManager.Instance.InitialFoodDemandFrequency;
        if (configured >= 0f) depletionChancePerRound = configured;
        if (showDebugInfo)
            Debug.Log($"[CommunityFoodDepletionManager] depletion chance per community per round = {depletionChancePerRound}");
    }

    void OnDestroy()
    {
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged -= OnRoundChanged;
        }
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
        }
    }

    void TryDeplete(PrebuiltBuilding community)
    {
        SnapshotDebug.Mark("draw:CommunityFoodDepletion");
        if (UnityEngine.Random.value >= depletionChancePerRound) return; // routine miss — not logged, would fire every community every round

        BuildingResourceStorage storage = community.GetResourceStorage();
        if (storage == null)
        {
            Debug.LogWarning($"[CommunityFoodDepletionManager] {community.name} has no BuildingResourceStorage — cannot deplete/request food.");
            GameLogPanel.Instance?.LogError($"{community.name} has no BuildingResourceStorage — depletion event skipped.");
            return;
        }

        int available = storage.GetResourceAmount(ResourceType.FoodPacks);
        if (available <= 0)
        {
            if (showDebugInfo)
                Debug.Log($"[CommunityFoodDepletionManager] {community.name} rolled a depletion event but had no food packs left to lose.");
            GameLogPanel.Instance?.LogResourceChange($"{community.name} rolled a depletion event but had no food packs left to lose.");
            return;
        }

        // Don't stack a second request while one is already pending for this community.
        bool alreadyRequested = TaskSystem.Instance.GetAllActiveTasks()
            .Any(t => t.taskTitle == communityFoodRequestTask.taskTitle && t.affectedFacility == community.name);
        if (alreadyRequested)
        {
            if (showDebugInfo)
                Debug.Log($"[CommunityFoodDepletionManager] {community.name} rolled a depletion event but already has a pending food request — skipped.");
            GameLogPanel.Instance?.LogTaskEvent($"{community.name} rolled a depletion event but already has a pending food request — skipped.");
            return;
        }

        int lost = storage.RemoveResource(ResourceType.FoodPacks, Mathf.Min(depletionAmount, available));
        if (lost <= 0)
        {
            Debug.LogWarning($"[CommunityFoodDepletionManager] {community.name} depletion event resolved but removed 0 food packs unexpectedly.");
            GameLogPanel.Instance?.LogError($"{community.name} depletion event removed 0 food packs unexpectedly.");
            return;
        }

        SpawnRequestTask(community, lost);
    }

    void SpawnRequestTask(PrebuiltBuilding community, int amount)
    {
        GameTask task = TaskSystem.Instance.CreateTaskFromDatabase(communityFoodRequestTask, community);
        if (task == null)
        {
            Debug.LogWarning($"[CommunityFoodDepletionManager] {community.name} lost {amount} food packs but the replacement task failed to create.");
            GameLogPanel.Instance?.LogError($"{community.name} lost {amount} food packs but the replacement request failed to create.");
            return;
        }

        // Replace exactly what was lost — override every food-delivering choice on this task
        // instance (the template's authored deliveryQuantity is just a placeholder/default).
        foreach (AgentChoice choice in task.agentChoices)
        {
            if (choice.deliveryCargoType == ResourceType.FoodPacks)
                choice.deliveryQuantity = amount;
        }
        task.foodAmount = amount;

        // This request IS the community's entire food demand for today — recorded exactly once,
        // here, at the moment the request is created (not on fulfillment/completion).
        DailyReportData.Instance?.RecordCommunityFoodDemand(amount);

        if (showDebugInfo)
            Debug.Log($"[CommunityFoodDepletionManager] {community.name} lost {amount} food packs — requesting replacement.");
        GameLogPanel.Instance?.LogTaskEvent($"{community.name} lost {amount} food packs to an unforeseen event — requesting replacement.");
    }
}
