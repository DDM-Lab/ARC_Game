using UnityEngine;
using System.Collections.Generic;
using System;

// Must run its Start() (and therefore subscribe to GlobalClock.OnTimeSegmentChanged) before
// TaskSystem does, which uses Unity's default order (0) and has no execution-order attribute of
// its own. Both react to the SAME event on Round 1 / Round 3: this component's OnRoundChanged
// (via GenerateFoodNeedIfDue) is what SETS the outstandingFoodNeed ledger for the new cycle and
// immediately reconciles it against whatever's already in storage; TaskSystem's own OnRoundChanged
// (GenerateTasksFromDatabase) then evaluates the NeedsFood resource trigger and sizes the
// Shelter/Motel Follow-up request's initial amount by reading GetFoodNeed() — i.e. that ledger.
// Subscription order determines multicast invocation order for the same event, so this MUST
// subscribe first, or the Follow-up request could be generated (and sized) off last round's
// stale need instead of this round's freshly-set one. Without an explicit order here, that
// ordering is whatever Unity happens to pick for two scripts with no attribute, which is not
// something to rely on. (The confirm-time live-resize in TaskSystem.ValidateBeforeConfirm reads
// the ledger again right before delivery and is unaffected either way — this only matters for
// the number shown/authored at the moment the task is first created.)
[DefaultExecutionOrder(-50)]
public class BuildingResourceStorage : MonoBehaviour
{
    [Header("Storage Configuration")]
    public List<ResourceCapacity> resourceCapacities = new List<ResourceCapacity>();
    
    [Header("Game Start Resource Settings")]
    public List<ResourceAmount> startingResources = new List<ResourceAmount>();
    
    [Header("Daily Food Settings")]
    public int startingFoodPacks = 0; // Separate food allocation per game start
    public bool enableFoodWaste = true;
    [Tooltip("If true, this storage's FoodPacks are topped up to full capacity at the start of each day (e.g. Kitchens), instead of the flat startingFoodPacks amount.")]
    public bool fillFoodToCapacityDaily = false;

    [Header("Population-Based Consumption")]
    public bool enablePopulationBasedConsumption = true;
    public int foodPerPersonPerNRounds  = 1;
    // No longer drives scheduling (see GenerateFoodNeedIfDue, fixed to Round 1 / Round 3) — kept
    // only because GymServerManager still exports it for the surrogate/parity JSON.
    public int consumptionRoundInterval = 2;
    public bool workersConsumeFoodToo = true;

    [Header("Casework Departures (CaseworkSite only)")]
    [Tooltip("How many clients leave this CaseworkSite on their own each round, once their case is resolved — they are simply removed from the population count, with no destination. 0 disables this (default, so other building types are unaffected).")]
    public int caseworkDeparturesPerRound = 0;


    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Current resource storage
    private Dictionary<ResourceType, int> currentResources = new Dictionary<ResourceType, int>();
    private Dictionary<ResourceType, int> maxCapacities = new Dictionary<ResourceType, int>();
    
    // Events
    public event Action<ResourceType, int, int> OnResourceChanged; // type, newAmount, capacity
    public event Action OnStorageUpdated;

    // How many food packs are CURRENTLY creditable — i.e. still owed for the most recently
    // generated feeding cycle. This is the single source of truth for "current need": the ONLY
    // thing GetFoodNeed() reads, the ONLY thing a delivery is ever credited against, and what
    // decides whether an arriving pack is consumed or left untouched in storage.
    //
    // It is SET (not added to) at each of GenerateFoodNeedIfDue's two fixed, player-visible
    // generation points (Round 1 and Round 3 — the same rounds the Food Request tasks use),
    // and only shrinks via ReconcileFoodAgainstOutstandingNeed crediting an actual delivery.
    // Whatever is still unpaid from an OLDER cycle when a newer one is generated is simply no
    // longer creditable from that point on — it was already recorded permanently in the
    // cumulative NEEDED counter (RecordFoodConsumptionCumulative) the moment it was generated,
    // so the score keeps it forever; this ledger only tracks what a delivery landing THIS
    // moment could still be credited for. That is what makes "the follow-up already satisfied
    // the demand, so the original late delivery becomes waste" (and its mirror image) come out
    // right regardless of delivery order — see ReconcileFoodAgainstOutstandingNeed.
    //
    // Nothing else touches this field, so "was this already fed" is never a question of which
    // round a delivery happens to land in (the previous mechanism blocked/over-credited late
    // food purely based on round-number dedup — see the fix this replaces).
    private int outstandingFoodNeed = 0;

    /// <summary>
    /// Snapshot support. currentResources holds population and food packs -- the numbers
    /// that decide whether a relocation or food Demand task gets generated. Leaving them
    /// uncaptured let a restored game generate an EXTRA "Population Relocation From
    /// Community" demand two rounds after load, while every other field matched.
    /// outstandingFoodNeed is the food-consumption ledger and is equally invisible.
    /// maxCapacities is rebuilt from the prefab on scene load, so only the live amounts
    /// and the ledger are carried.
    /// </summary>
    [System.Serializable]
    public class Snapshot
    {
        public List<string> resourceTypes = new List<string>();
        public List<int> resourceAmounts = new List<int>();
        public int outstandingFoodNeed;
    }

    public Snapshot CaptureState()
    {
        var s = new Snapshot { outstandingFoodNeed = outstandingFoodNeed };
        foreach (var kv in currentResources) { s.resourceTypes.Add(kv.Key.ToString()); s.resourceAmounts.Add(kv.Value); }
        return s;
    }

    public void RestoreState(Snapshot s)
    {
        if (s == null) return;
        currentResources.Clear();
        int n = Mathf.Min(s.resourceTypes.Count, s.resourceAmounts.Count);
        for (int i = 0; i < n; i++)
            if (System.Enum.TryParse(s.resourceTypes[i], out ResourceType rt))
                currentResources[rt] = s.resourceAmounts[i];
        outstandingFoodNeed = s.outstandingFoodNeed;
    }

    private int todayFoodPacksConsumed = 0;
    
    void Start()
    {
        InitializeStorage();
        storageInitialized = true;
        SubscribeToEvents();
        ApplyConfiguredCapacities();   // no-op until GameDataManager is ready; it calls back otherwise
    }

    bool storageInitialized = false;
    bool configApplied = false;

    /// <summary>
    /// Sheet parameters -> this storage (BUG_REPORTS B35). Runs once: from Start when the config is
    /// already loaded (buildings constructed during play) or from GameDataManager.ApplyConfigToScene
    /// for objects that initialised first (communities, motel).
    /// </summary>
    public void ApplyConfiguredCapacities()
    {
        return;
#pragma warning disable 0162
        if (!storageInitialized || configApplied) return;
        var gdm = GameDataManager.Instance;
        if (gdm == null || !gdm.IsDataReady) return;
        configApplied = true;

        var prebuilt = GetComponent<PrebuiltBuilding>();
        if (prebuilt != null)
        {
            if (prebuilt.GetPrebuiltType() == PrebuiltBuildingType.Community && gdm.InitialResidentsPerCommunityNumber > 0)
            {
                int residents = gdm.InitialResidentsPerCommunityNumber;
                SetCapacity(ResourceType.Population, Mathf.Max(GetResourceCapacity(ResourceType.Population), residents));
                currentResources[ResourceType.Population] = residents;
                Debug.Log($"{gameObject.name} population set to {residents} (initialCommunityResidentCount)");
                GameLogPanel.Instance?.LogResourceChange($"{gameObject.name} population set to {residents} (initialCommunityResidentCount)");
                OnStorageUpdated?.Invoke();
            }
            return;
        }

        var building = GetComponent<Building>();
        if (building == null) return;
        switch (building.GetBuildingType())
        {
            case BuildingType.Kitchen:
                // Kitchens fill to capacity once per day (main-bugfixes), so the food capacity IS the
                // daily throughput; initialKitchenCapacity no longer has a consumer.
                SetCapacity(ResourceType.FoodPacks, gdm.InitialKitchenFoodCapacity);
                break;
            case BuildingType.Shelter:
                SetCapacity(ResourceType.Population, gdm.InitialShelterCapacity);
                SetCapacity(ResourceType.FoodPacks, gdm.InitialShelterFoodCapacity);
                break;
            case BuildingType.CaseworkSite:
                SetCapacity(ResourceType.Population, gdm.InitialCaseworkCapacity);
                break;
        }
        Debug.Log($"{gameObject.name} ({building.GetBuildingType()}) configured: foodCap={GetResourceCapacity(ResourceType.FoodPacks)} popCap={GetResourceCapacity(ResourceType.Population)}");
        OnStorageUpdated?.Invoke();
#pragma warning restore 0162
    }

    void SetCapacity(ResourceType type, int capacity)
    {
        if (capacity <= 0 || !maxCapacities.ContainsKey(type)) return;
        maxCapacities[type] = capacity;
        if (currentResources.TryGetValue(type, out int current) && current > capacity)
            currentResources[type] = capacity;
    }

    //void SubscribeToEvents()
    //{
    //    // Subscribe to round changes for production and consumption
    //    if (GlobalClock.Instance != null)
    //    {
    //        GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;
    //        GlobalClock.Instance.OnDayChanged += OnDayChanged;
    //    }
    //}

    void SubscribeToEvents()
    {
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;
            GlobalClock.Instance.OnDayChanged += OnDayChanged;
            GlobalClock.Instance.OnSimulationEnded += OnSimulationEndedCheckEndOfDayWaste; // NEW
        }
    }

    private bool wasteRecordedToday = false;

    void OnSimulationEndedCheckEndOfDayWaste()
    {
        if (GlobalClock.Instance == null || !GlobalClock.Instance.isWaitingForReport) return;
        if (wasteRecordedToday) return; 

        bool isCommunity = GetComponent<PrebuiltBuilding>()?.GetPrebuiltType() == PrebuiltBuildingType.Community;
        if (!enableFoodWaste || isCommunity) return;

        int wastedFood = GetResourceAmount(ResourceType.FoodPacks);
        if (wastedFood > 0)
        {
            DailyReportData.Instance.RecordFoodWasted(wastedFood);
            DailyReportData.Instance.RecordFoodWasteCumulative(wastedFood);

            if (showDebugInfo)
                Debug.Log($"{gameObject.name} logged {wastedFood} unused meals as today's waste (still shown in storage until day change)");
            GameLogPanel.Instance.LogResourceChange($"{gameObject.name} logged {wastedFood} unused meals as today's waste");
        }
        wasteRecordedToday = true;
    }

    void InitializeStorage()
    {
        // Initialize capacities
        foreach (ResourceCapacity capacity in resourceCapacities)
        {
            maxCapacities[capacity.resourceType] = capacity.maxCapacity;
            currentResources[capacity.resourceType] = 0;
            Debug.Log($"{gameObject.name} storage initialized to {capacity.maxCapacity} of {capacity.resourceType}");
            GameLogPanel.Instance.LogResourceChange($"{gameObject.name} storage initialized to {capacity.maxCapacity} of {capacity.resourceType}");
        }
        
        // Set daily starting resources
        SetStartingResources();
        FillFoodToCapacityIfConfigured();

        OnStorageUpdated?.Invoke();
    }
    
    void SetStartingResources()
    {
        // Set general game start resources (population, etc.)
        foreach (ResourceAmount resource in startingResources)
        {
            if (maxCapacities.ContainsKey(resource.type))
            {
                int actualAmount = Mathf.Min(resource.amount, maxCapacities[resource.type]);
                currentResources[resource.type] = actualAmount;

                if (showDebugInfo)
                    Debug.Log($"{gameObject.name} game start resource set to {actualAmount} {resource.type}");

                GameLogPanel.Instance.LogResourceChange($"{gameObject.name} game start resource is set to {actualAmount} {resource.type}");
            }
        }
        
        OnStorageUpdated?.Invoke();
    }

    void OnRoundChanged(int newRound)
    {
        if (newRound <= (GlobalClock.Instance != null ? GlobalClock.Instance.roundsPerDay : 4)) // every real round, incl. the last of the day
        {
            GenerateFoodNeedIfDue(newRound);
            HandleCaseworkDepartures();
        }
    }

    /// <summary>
    /// Clients at a CaseworkSite leave on their own, a fixed number per round, once their case is
    /// resolved — they are simply removed from the population count (out of the system), with no
    /// destination or further tracking. Disabled by default (caseworkDeparturesPerRound = 0); set
    /// it on the Casework prefab's BuildingResourceStorage to enable.
    /// </summary>
    void HandleCaseworkDepartures()
    {
        if (caseworkDeparturesPerRound <= 0) return;
        if (GetComponent<Building>()?.GetBuildingType() != BuildingType.CaseworkSite) return;

        int departed = RemoveResource(ResourceType.Population, caseworkDeparturesPerRound);
        if (departed <= 0) return;

        if (showDebugInfo)
            Debug.Log($"{gameObject.name}: {departed} client(s) left casework on their own and exited the system.");
        // LogBuildingStatus, not LogResourceChange (RemoveResource already logs the raw count
        // change there) — matches ClientStayTracker.TriggerNonCaseworkDeparture's convention for
        // "clients left a building on their own" events, so both read the same way in the log.
        GameLogPanel.Instance.LogBuildingStatus($"{departed} client(s) left {gameObject.name} on their own (casework resolved) and exited the system.");
    }

    void OnDayChanged(int newDay)
    {
        HandleDailyReset();
    }

    /// <summary>
    /// Starts a fresh feeding cycle: SETS outstandingFoodNeed to this cycle's need (population ×
    /// foodPerPersonPerNRounds) — replacing, not adding to, whatever was still unpaid from the
    /// previous cycle — at the SAME two rounds the Food Request tasks generate on (Round 1 and
    /// Round 3 — see Shelter_FoodRequest_First/_Second's roundTriggers), so the player-visible
    /// request schedule and the score's need ledger are driven by one and the same clock instead
    /// of two separate ones drifting apart.
    ///
    /// The new need is recorded as "needed" immediately and permanently
    /// (RecordFoodConsumptionCumulative(0, newNeed)) — that record is never touched again,
    /// whatever happens afterward, which is what keeps an old miss visible in the score forever
    /// even though it stops being creditable the moment this runs. Then makes one immediate
    /// attempt to pay the new need down from whatever's already sitting in storage (covers food
    /// that arrived earlier and had nothing to be credited against yet — see the spec's "food
    /// stays in storage until there's a need to satisfy" case).
    /// </summary>
    void GenerateFoodNeedIfDue(int newRound)
    {
        if (!enablePopulationBasedConsumption) return;
        if (newRound != 0 && newRound != 2) return; // Round 1 and Round 3 only
        if (GlobalClock.Instance != null && GlobalClock.Instance.GetCurrentDay() < 2) return;

        int newNeed = GetTotalPeopleCount() * foodPerPersonPerNRounds;
        if (newNeed <= 0) return;

        outstandingFoodNeed = newNeed;
        DailyReportData.Instance?.RecordFoodConsumptionCumulative(0, newNeed);

        if (showDebugInfo)
            Debug.Log($"{gameObject.name}: new feeding cycle needs {newNeed} meals (round {newRound + 1}); {outstandingFoodNeed} currently creditable");
        GameLogPanel.Instance?.LogResourceChange($"{gameObject.name}: new feeding cycle needs {newNeed} meals (round {newRound + 1}); {outstandingFoodNeed} currently creditable");

        ReconcileFoodAgainstOutstandingNeed();
    }

    /// <summary>
    /// Credits whatever's currently in storage against outstandingFoodNeed, capped at exactly
    /// what's still owed — never a fresh full recompute. Called both here (right after new need
    /// is generated, in case food was already banked) and from AddResource on every arrival (see
    /// below), so the outcome depends only on how much food has actually arrived and how much is
    /// actually still owed, never on which round happens to be current. If nothing is owed right
    /// now, the food is left untouched in storage — NOT wasted, NOT credited — exactly as the
    /// spec asks: it is available to satisfy the next cycle's need (Round 3, or the next day's),
    /// and only becomes real waste if it is still sitting there, unconsumed, at end of day
    /// (OnSimulationEndedCheckEndOfDayWaste already sweeps that case, unchanged).
    /// </summary>
    void ReconcileFoodAgainstOutstandingNeed()
    {
        if (outstandingFoodNeed <= 0) return;

        int stock = GetResourceAmount(ResourceType.FoodPacks);
        if (stock <= 0) return;

        int credit = Mathf.Min(outstandingFoodNeed, stock);
        if (credit <= 0) return;

        RemoveResource(ResourceType.FoodPacks, credit);
        outstandingFoodNeed -= credit;
        todayFoodPacksConsumed += credit;
        DailyReportData.Instance?.RecordFoodConsumptionCumulative(credit, 0);

        if (showDebugInfo)
            Debug.Log($"{gameObject.name}: {credit} meals consumed against outstanding need ({outstandingFoodNeed} still outstanding)");
        GameLogPanel.Instance?.LogResourceChange($"{gameObject.name}: {credit} meals consumed against outstanding need ({outstandingFoodNeed} still outstanding)");
    }


    int GetTotalPeopleCount()
    {
        int totalPeople = 0;
        
        // Count population
        totalPeople += GetResourceAmount(ResourceType.Population);
        
        // Count workers if they consume food too
        if (workersConsumeFoodToo)
        {
            Building building = GetComponent<Building>();
            if (building != null)
            {
                // Mouths, not workforce points: a trained worker is one person (BUG_REPORTS B27).
                totalPeople += WorkerSystem.Instance != null
                    ? WorkerSystem.Instance.GetWorkersByBuildingId(building.GetOriginalSiteId()).Count
                    : building.GetAssignedWorkforce();
            }
        }
        
        return totalPeople;
    }

    /// <summary>
    /// How much food is currently owed to this facility — i.e. outstandingFoodNeed, the same
    /// ledger the score is built from. Callers (request sizing, the live-resize-at-confirm
    /// check, the NeedsFood trigger, the facility panel) already treat the return value as "how
    /// much is currently needed," which this still is — it is just no longer a same-instant
    /// population-minus-stock snapshot that forgets about any earlier missed cycle.
    /// </summary>
    public int GetFoodNeed() => outstandingFoodNeed;

    void HandleEndOfDayWaste()
    {
        bool isCommunity = GetComponent<PrebuiltBuilding>()?.GetPrebuiltType() == PrebuiltBuildingType.Community;

        if (enableFoodWaste && !isCommunity)
        {
            int wastedFood = GetResourceAmount(ResourceType.FoodPacks);
            if (wastedFood > 0)
            {
                DailyReportData.Instance.RecordFoodWasted(wastedFood);
                DailyReportData.Instance.RecordFoodWasteCumulative(wastedFood);
                RemoveResource(ResourceType.FoodPacks, wastedFood);

                if (showDebugInfo)
                    Debug.Log($"{gameObject.name} wasted {wastedFood} unused meals at end of day");
                GameLogPanel.Instance.LogResourceChange($"{gameObject.name} wasted {wastedFood} unused meals at end of day");
            }
        }
    }


    void HandleDailyReset()
    {
        if (enableFoodWaste)
        {
            todayFoodPacksConsumed = 0;

            bool isCommunity = GetComponent<PrebuiltBuilding>()?.GetPrebuiltType() == PrebuiltBuildingType.Community;
            if (!isCommunity)
            {
                int leftover = GetResourceAmount(ResourceType.FoodPacks);
                if (leftover > 0)
                    RemoveResource(ResourceType.FoodPacks, leftover);
            }
        }

        wasteRecordedToday = false;

        // A day's unpaid food debt cannot carry a physical delivery into the next day — any
        // vehicle still in flight at day-end is cancelled outright (TaskSystem.CancelIncompleteFoodDeliveries,
        // "food spoils overnight"), and any banked-but-uncredited stock was just swept above.
        // So there is nothing left that a surviving outstandingFoodNeed balance could still be
        // paid against; carrying it forward would only make tomorrow's request balloon by every
        // day's unanswered need compounding forever. The cumulative NEEDED counter already
        // recorded each day's misses permanently (see GenerateFoodNeedIfDue) — resetting THIS
        // ledger only affects what a future delivery can still be credited for, not the score.
        outstandingFoodNeed = 0;

        if (fillFoodToCapacityDaily)
        {
            FillFoodToCapacityIfConfigured();
        }
        else if (startingFoodPacks > 0)
        {
            int actualAdded = AddResource(ResourceType.FoodPacks, startingFoodPacks);
            if (showDebugInfo)
                Debug.Log($"{gameObject.name} received {actualAdded} starting meals at start of day");
            GameLogPanel.Instance.LogResourceChange($"{gameObject.name} received {actualAdded} starting meals at start of day");
        }
    }

    //void HandleDailyReset()
    //{
    //    bool isCommunity = GetComponent<PrebuiltBuilding>()?.GetPrebuiltType() == PrebuiltBuildingType.Community;

    //    if (enableFoodWaste && !isCommunity)
    //    {
    //        todayFoodPacksConsumed = 0;
    //        int wastedFood = GetResourceAmount(ResourceType.FoodPacks);
    //        if (wastedFood > 0)
    //        {
    //            // Report to daily tracking
    //            DailyReportData.Instance.RecordFoodWasted(wastedFood);

    //            DailyReportData.Instance.RecordFoodWasteCumulative(wastedFood);


    //            RemoveResource(ResourceType.FoodPacks, wastedFood);

    //            if (showDebugInfo)
    //                Debug.Log($"{gameObject.name} wasted {wastedFood} unused meals at end of day");
    //            GameLogPanel.Instance.LogResourceChange($"{gameObject.name} wasted {wastedFood} unused meals at end of day");
    //        }
    //    }

    //    // Start the new day fully stocked (e.g. Kitchens), or with a flat starting amount.
    //    if (fillFoodToCapacityDaily)
    //    {
    //        FillFoodToCapacityIfConfigured();
    //    }
    //    else if (startingFoodPacks > 0)
    //    {
    //        int actualAdded = AddResource(ResourceType.FoodPacks, startingFoodPacks);
    //        if (showDebugInfo)
    //            Debug.Log($"{gameObject.name} received {actualAdded} starting meals at start of day");
    //        GameLogPanel.Instance.LogResourceChange($"{gameObject.name} received {actualAdded} starting meals at start of day");
    //    }
    //}

    /// <summary>
    /// Tops FoodPacks up to full capacity (used by storages with fillFoodToCapacityDaily,
    /// e.g. Kitchens) — called at initial game start and at each day's reset.
    /// </summary>
    void FillFoodToCapacityIfConfigured()
    {
        if (!fillFoodToCapacityDaily) return;

        Building building = GetComponent<Building>();
        if (building != null && !building.IsOperational())
        {
            if (showDebugInfo)
                Debug.Log($"{gameObject.name} not stocked - building not operational (Status: {building.GetCurrentStatus()})");
            return;
        }

        int missing = GetResourceCapacity(ResourceType.FoodPacks) - GetResourceAmount(ResourceType.FoodPacks);
        if (missing <= 0) return;

        int actualAdded = AddResource(ResourceType.FoodPacks, missing);
        if (showDebugInfo)
            Debug.Log($"{gameObject.name} stocked to full capacity: +{actualAdded} food packs");
        GameLogPanel.Instance.LogResourceChange($"{gameObject.name} stocked to full capacity: +{actualAdded} food packs");
    }

    /// <summary>
    /// Add resources to storage
    /// </summary>
    public int AddResource(ResourceType type, int amount)
    {
        if (!maxCapacities.ContainsKey(type))
        {
            if (showDebugInfo)
                Debug.LogWarning($"{gameObject.name} cannot store {type}");
            return 0;
        }
        
        int currentAmount = currentResources[type];
        int capacity = maxCapacities[type];
        int spaceAvailable = capacity - currentAmount;
        int actualAdded = Mathf.Min(amount, spaceAvailable);
        
        currentResources[type] = currentAmount + actualAdded;
        
        OnResourceChanged?.Invoke(type, currentResources[type], capacity);
        OnStorageUpdated?.Invoke();
        
        if (showDebugInfo && actualAdded > 0)
            Debug.Log($"{gameObject.name} received {actualAdded} {type} ({currentResources[type]}/{capacity})");
        GameLogPanel.Instance.LogResourceChange($"{gameObject.name} received {actualAdded} {type} ({currentResources[type]}/{capacity})");

        // Clients eat as soon as food arrives. ReconcileFoodAgainstOutstandingNeed is safe to call
        // any number of times per round (from several drop-offs of the same request, or from
        // unrelated deliveries landing close together) — it only ever credits up to whatever's
        // still actually owed, so it can never double-feed the way a naive "recompute need fresh
        // every time" call would.
        if (type == ResourceType.FoodPacks && actualAdded > 0 && enablePopulationBasedConsumption)
        {
            ReconcileFoodAgainstOutstandingNeed();
        }

        return actualAdded;
    }
    
    /// <summary>
    /// Remove resources from storage
    /// </summary>
    public int RemoveResource(ResourceType type, int amount)
    {
        if (!currentResources.ContainsKey(type))
            return 0;
        
        int currentAmount = currentResources[type];
        int actualRemoved = Mathf.Min(amount, currentAmount);
        
        currentResources[type] = currentAmount - actualRemoved;
        
        OnResourceChanged?.Invoke(type, currentResources[type], maxCapacities[type]);
        OnStorageUpdated?.Invoke();
        
        if (showDebugInfo && actualRemoved > 0)
            Debug.Log($"{gameObject.name} lost {actualRemoved} {type} ({currentResources[type]}/{maxCapacities[type]})");
        GameLogPanel.Instance.LogResourceChange($"{gameObject.name} lost {actualRemoved} {type} ({currentResources[type]}/{maxCapacities[type]})");

        return actualRemoved;
    }
    
    /// <summary>
    /// Check if can add specific amount of resource
    /// </summary>
    public bool CanAddResource(ResourceType type, int amount)
    {
        if (!maxCapacities.ContainsKey(type))
            return false;
        
        int currentAmount = currentResources.ContainsKey(type) ? currentResources[type] : 0;
        int capacity = maxCapacities[type];
        
        return (currentAmount + amount) <= capacity;
    }
    
    /// <summary>
    /// Check if has specific amount of resource
    /// </summary>
    public bool HasResource(ResourceType type, int amount)
    {
        if (!currentResources.ContainsKey(type))
            return false;
        
        return currentResources[type] >= amount;
    }
    
    /// <summary>
    /// Get current resource amount
    /// </summary>
    public int GetResourceAmount(ResourceType type)
    {
        return currentResources.ContainsKey(type) ? currentResources[type] : 0;
    }

    public int GetTodayFoodPacksConsumed() => todayFoodPacksConsumed;
    
    /// <summary>
    /// Get resource capacity
    /// </summary>
    public int GetResourceCapacity(ResourceType type)
    {
        return maxCapacities.ContainsKey(type) ? maxCapacities[type] : 0;
    }
    
    /// <summary>
    /// Get available space for resource
    /// </summary>
    public int GetAvailableSpace(ResourceType type)
    {
        if (!maxCapacities.ContainsKey(type))
            return 0;
        
        int currentAmount = currentResources.ContainsKey(type) ? currentResources[type] : 0;
        return maxCapacities[type] - currentAmount;
    }
    
    /// <summary>
    /// Check if storage is full for a resource type
    /// </summary>
    public bool IsResourceFull(ResourceType type)
    {
        return GetAvailableSpace(type) <= 0;
    }
    
    /// <summary>
    /// Check if storage is empty for a resource type
    /// </summary>
    public bool IsResourceEmpty(ResourceType type)
    {
        return GetResourceAmount(type) <= 0;
    }
    
    /// <summary>
    /// Transfer resources to another storage
    /// </summary>
    public int TransferResourceTo(BuildingResourceStorage targetStorage, ResourceType type, int amount)
    {
        if (targetStorage == null || !HasResource(type, amount))
            return 0;
        
        int actualTransferred = targetStorage.AddResource(type, amount);
        RemoveResource(type, actualTransferred);
        
        if (showDebugInfo && actualTransferred > 0)
            Debug.Log($"Transferred {actualTransferred} {type} from {gameObject.name} to {targetStorage.gameObject.name}");
        GameLogPanel.Instance.LogResourceChange($"Transferred {actualTransferred} {type} from {gameObject.name} to {targetStorage.gameObject.name}");
        return actualTransferred;
    }
    
    /// <summary>
    /// Get resource summary for debugging
    /// </summary>
    public string GetResourceSummary()
    {
        List<string> summary = new List<string>();
        
        foreach (var kvp in currentResources)
        {
            int capacity = maxCapacities.ContainsKey(kvp.Key) ? maxCapacities[kvp.Key] : 0;
            summary.Add($"{kvp.Key}: {kvp.Value}/{capacity}");
        }
        
        return string.Join(", ", summary);
    }

    //void OnDestroy()
    //{
    //    // Unsubscribe from events
    //    if (GlobalClock.Instance != null)
    //    {
    //        GlobalClock.Instance.OnTimeSegmentChanged -= OnRoundChanged;
    //        GlobalClock.Instance.OnDayChanged -= OnDayChanged;
    //    }
    //}

    void OnDestroy()
    {
        // Unsubscribe from events
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged -= OnRoundChanged;
            GlobalClock.Instance.OnDayChanged -= OnDayChanged;
            GlobalClock.Instance.OnSimulationEnded -= OnSimulationEndedCheckEndOfDayWaste; // NEW
        }
    }

    public int GetMaxCapacity(ResourceType resourceType)
    {
        return maxCapacities[resourceType];
    }
    

    
    [ContextMenu("Force Daily Reset")]
    public void DebugForceDailyReset()
    {
        HandleDailyReset();
    }
}

[System.Serializable]
public class ResourceCapacity
{
    public ResourceType resourceType;
    public int maxCapacity;
}

[System.Serializable]
public class ResourceAmount
{
    public ResourceType type;
    public int amount;
}

public enum ResourceType
{
    Population,
    FoodPacks
}
