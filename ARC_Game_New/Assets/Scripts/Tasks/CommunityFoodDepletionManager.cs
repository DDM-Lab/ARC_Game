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
        if (GameConfigLoader.Instance != null)
        {
            float configured = GameConfigLoader.Instance.GetInitialFoodDemandFrequency();
            if (configured >= 0f) depletionChancePerRound = configured;
        }

        if (GlobalClock.Instance != null)
            GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;
    }

    void OnDestroy()
    {
        if (GlobalClock.Instance != null)
            GlobalClock.Instance.OnTimeSegmentChanged -= OnRoundChanged;
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
        if (Random.value >= depletionChancePerRound) return;

        BuildingResourceStorage storage = community.GetResourceStorage();
        if (storage == null) return;

        int available = storage.GetResourceAmount(ResourceType.FoodPacks);
        if (available <= 0) return; // nothing left to lose

        // Don't stack a second request while one is already pending for this community.
        bool alreadyRequested = TaskSystem.Instance.GetAllActiveTasks()
            .Any(t => t.taskTitle == communityFoodRequestTask.taskTitle && t.affectedFacility == community.name);
        if (alreadyRequested) return;

        int lost = storage.RemoveResource(ResourceType.FoodPacks, Mathf.Min(depletionAmount, available));
        if (lost <= 0) return;

        SpawnRequestTask(community, lost);
    }

    void SpawnRequestTask(PrebuiltBuilding community, int amount)
    {
        GameTask task = TaskSystem.Instance.CreateTaskFromDatabase(communityFoodRequestTask, community);
        if (task == null) return;

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
