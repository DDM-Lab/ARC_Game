using UnityEngine;
using System.Collections;

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }

    [Header("Config Settings")]
    public bool useExternalConfig = true;
    
    public GameConfigLoader configLoader;

    [Header("Defaults")]
    public int defaultBudget = 10000; //initialBudget
    [Range(0f, 100f)]
    public float defaultSatisfaction = 0f; // initialSatisfaction
    public int defaultCommunityNumber= 3; // numberOfCommunities
    public int defaultResidentsPerCommunity = 40; // done
    public int defaultGameDays = 8; // gameDurationDays
    public int defaultRoundsPerDay = 4; // done
    public int defaultTrainedVolunteerCount = 5; // done
    public int defaultUntrainedVolunteerCount = 5; // done
    public int defaultDailyBudgetAllocs = 3000; // dailyBudgetAllocation
    public WeatherType defaultInitialWeather = WeatherType.Sunny; // done
    public int defaultKitchenCapacity = 10; // done
    public int defaultShelterCapacity = 10; // done
    public int defaultCaseworkCapacity = 10; // done
    public int defaultKitchenFoodCapacity = 200;
    public int defaultShelterFoodCapacity = 100;
    public int defaultRequiredWorkersPerLoc = 4; // done
    public float defaultSunnyExpansionRate = 0f;
    public float defaultSunnySpreadChanceMultiplier = 0.5f;
    public float defaultSmallRainExpansionRate = 0.5f;
    public float defaultSmallRainSpreadChanceMultiplier = 0.8f;
    public float defaultMediumRainExpansionRate = 1.5f;
    public float defaultMediumRainSpreadChanceMultiplier = 1f;
    public float defaultHeavyRainExpansionRate = 3f;
    public float defaultHeavyRainSpreadChanceMultiplier = 1.2f;
    public float defaultStormExpansionRate = 5f;
    public float defaultStormSpreadChanceMultiplier = 1.5f;
    public float defaultFoodDemandFrequency = -1f; // done
    // new for abv
    public int defaultShelterFloodThreshold = 2;
    public int defaultShelterFloodRadius = 5;
    public FloodedFacilityTrigger.ComparisonType defaultShelterFloodComparison = FloodedFacilityTrigger.ComparisonType.AtLeast;
    // end
    public int defaultERVCount = 3; // done
    public int defaultExternalRelationFrequency = 3; // done
    public int defaultEmergencyTaskFrequency = 4; // done



    public int InitialBudget { get; private set; }
    public float InitialSatisfaction { get; private set; }
    public int InitialCommunityNumber { get; private set; }
    public int InitialResidentsPerCommunityNumber { get; private set; }
    public int InitialGameDays { get; private set; }
    public int InitialRoundsPerDay { get; private set; }
    public int InitialTrainedVolunteerCount { get; private set; }
    public int InitialUntrainedVolunteerCount { get; private set; }
    public int InitialDailyBudgetAddition { get; private set; }
    public WeatherType InitialWeather {get; private set;}
    public int InitialKitchenCapacity { get; private set; }
    public int InitialShelterCapacity { get; private set; }
    public int InitialCaseworkCapacity { get; private set; }
    public int InitialKitchenFoodCapacity { get; private set; }
    public int InitialShelterFoodCapacity { get; private set; }
    public int InitialRequiredWorkersPerLoc {get; private set; }
    public float InitialSunnyExpansionRate {get; private set; }
    public float InitialSunnySpreadChanceMultiplier {get; private set; }
    public float InitialSmallRainExpansionRate {get; private set; }
    public float InitialSmallRainSpreadChanceMultiplier {get; private set; }
    public float InitialMediumRainExpansionRate {get; private set; }
    public float InitialMediumRainSpreadChanceMultiplier {get; private set; }
    public float InitialHeavyRainExpansionRate {get; private set; }
    public float InitialHeavyRainSpreadChanceMultiplier {get; private set; }
    public float InitialStormExpansionRate {get; private set; }
    public float InitialStormSpreadChanceMultiplier {get; private set; }
    public float InitialFoodDemandFrequency {get; private set;}
    public int InitialERVCount {get; private set;}
    public int InitialExternalRelationFrequency {get; private set;}
    public int InitialEmergencyTaskFrequency {get; private set;}

    public int InitialShelterFloodThreshold { get; private set; }
    public int InitialShelterFloodRadius { get; private set; }
    public FloodedFacilityTrigger.ComparisonType InitialShelterFloodComparison { get; private set; }


    public bool IsDataReady { get; private set; } = false;

    /// <summary>The exact parameter set this session is running under, as the JSON that is
    /// also written to the log. A .cora save carries it so a file recorded under a different
    /// sheet is detected at load time instead of quietly loading into different mechanics.</summary>
    public static string ParametersInEffectJson { get; private set; } = "";

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // PARITY BUILD (ledger D7): the auto-find is REMOVED. MainScene does not wire
        // configLoader, so this build -- like upstream -- ignores the parameter sheet entirely
        // and runs on SetDefaults(). That is upstream's behaviour and it is what makes the two
        // builds comparable at all; D7 is the single largest divergence in the ledger, because
        // with it every number in the game comes from a different place.
        StartCoroutine(LoadAllData());
    }

    IEnumerator LoadAllData()
    {
        if (useExternalConfig && configLoader != null)
        {
            while (!configLoader.IsConfigLoaded())
            {
                yield return null;
            }

            InitialBudget = configLoader.GetInitialBudget();
            InitialSatisfaction = (float)configLoader.GetInitialSatisfaction();
            InitialCommunityNumber = configLoader.GetInitialCommunityCount();
            InitialResidentsPerCommunityNumber = configLoader.GetInitialResidentCountPerCommunity();
            InitialGameDays = configLoader.GetInitialNumDays();
            InitialRoundsPerDay = configLoader.GetInitialNumRoundsPerGame();
            InitialTrainedVolunteerCount = configLoader.GetInitialTrainedVolunteerCount();
            InitialUntrainedVolunteerCount = configLoader.GetInitialUntrainedVolunteerCount();
            InitialDailyBudgetAddition = configLoader.GetInitialBudgetDailyAdditions();
            InitialWeather = configLoader.GetInitialWeather();
            InitialKitchenCapacity = configLoader.GetInitialKitchenCapacity();
            InitialShelterCapacity = configLoader.GetInitialShelterCapacity();
            InitialCaseworkCapacity = configLoader.GetInitialCaseworkCapacity();
            InitialKitchenFoodCapacity = configLoader.GetInitialKitchenFoodCapacity();
            InitialShelterFoodCapacity = configLoader.GetInitialShelterFoodCapacity();
            InitialRequiredWorkersPerLoc = configLoader.GetInitialNeededWorkersPerLoc();
            InitialSunnyExpansionRate = configLoader.GetInitialSunnyFloodExpansionRate();
            InitialSunnySpreadChanceMultiplier = configLoader.GetInitialSunnyFloodSpreadChanceMultiplier();
            InitialSmallRainExpansionRate = configLoader.GetInitialSmallRainFloodExpansionRate();
            InitialSmallRainSpreadChanceMultiplier = configLoader.GetInitialSmallRainFloodSpreadChanceMultiplier();
            InitialMediumRainExpansionRate = configLoader.GetInitialMediumRainFloodExpansionRate();
            InitialMediumRainSpreadChanceMultiplier = configLoader.GetInitialMediumRainFloodSpreadChanceMultiplier();
            InitialHeavyRainExpansionRate = configLoader.GetInitialHeavyRainFloodExpansionRate();
            InitialHeavyRainSpreadChanceMultiplier = configLoader.GetInitialHeavyRainFloodSpreadChanceMultiplier();
            InitialStormExpansionRate = configLoader.GetInitialStormFloodExpansionRate();
            InitialStormSpreadChanceMultiplier = configLoader.GetInitialStormFloodSpreadChanceMultiplier();
            InitialFoodDemandFrequency = configLoader.GetInitialFoodDemandFrequency();
            InitialERVCount = configLoader.GetInitialERVCount();
            InitialExternalRelationFrequency = configLoader.GetInitialExternalRelationFrequency();
            InitialEmergencyTaskFrequency = configLoader.GetInitialEmergencyTaskFrequency();
            InitialShelterFloodThreshold = configLoader.GetInitialShelterFloodThreshold();
            InitialShelterFloodRadius = configLoader.GetInitialShelterFloodRadius();
            InitialShelterFloodComparison = configLoader.GetInitialShelterFloodComparison();

            Debug.Log("GameDataManager: Google Sheets loaded successfully.");
        }
        else
        {
            Debug.Log("GameDataManager: External config disabled or missing loader. Using Hardcoded Defaults.");
            SetDefaults();
        }

        if (InstructorConfigManager.Instance != null)
        {
            var config = InstructorConfigManager.Instance.CurrentConfig;
            var p = config.parameters;

            if (configLoader.HasServerMapConfig())
            {
                Debug.Log($"GameDataManager: Overriding Baseline with Instructor Config (v{config.schemaVersion})");

                InitialBudget = p.initialBudget;
                InitialDailyBudgetAddition = (int)p.dailyBudgetAllocation;
                // Initial satisfaction is never overridden from instructor config — always 0.
                InitialCommunityNumber = p.numberOfCommunities;
                InitialGameDays = p.gameDurationDays;
                InitialResidentsPerCommunityNumber = p.residentsPerCommunity;
                InitialRoundsPerDay = p.roundsPerDay;
                InitialWeather = p.initialWeather;
                InitialTrainedVolunteerCount = p.initialTrainedVolunteerCount;
                InitialUntrainedVolunteerCount = p.initialUntrainedVolunteerCount;
                InitialKitchenCapacity = p.kitchenCapacity;
                InitialShelterCapacity = p.shelterCapacity;
                InitialCaseworkCapacity = p.caseworkCapacity;
                InitialRequiredWorkersPerLoc = p.requiredWorkerUnitsPerLoc;
                InitialERVCount = p.initialERVCount;
                InitialFoodDemandFrequency = p.foodDemandProbability;
                InitialExternalRelationFrequency = p.externalRelationFrequency;
                InitialEmergencyTaskFrequency = p.emergencyTaskFrequency;
                InitialShelterFloodThreshold = p.shelterFloodThreshold;
                InitialShelterFloodRadius = p.shelterFloodRadius;
                InitialShelterFloodComparison = p.shelterFloodComparisonType;
            }
            else
            {
                Debug.Log("GameDataManager: No Instructor Overrides found. Initializing Panel with Baseline values.");
                p.initialBudget = InitialBudget;
                p.dailyBudgetAllocation = (float)InitialDailyBudgetAddition;
                p.initialSatisfaction = (int)InitialSatisfaction;
                p.numberOfCommunities = InitialCommunityNumber;
                p.gameDurationDays = InitialGameDays;
                p.residentsPerCommunity = InitialResidentsPerCommunityNumber;
                p.totalPopulation = InitialResidentsPerCommunityNumber * InitialCommunityNumber;
                p.roundsPerDay = InitialRoundsPerDay;
                p.initialWeather = InitialWeather;
                p.initialTrainedVolunteerCount = InitialTrainedVolunteerCount;
                p.initialUntrainedVolunteerCount = InitialUntrainedVolunteerCount;
                p.initialWorkerCount = InitialTrainedVolunteerCount + InitialUntrainedVolunteerCount;
                p.kitchenCapacity = InitialKitchenCapacity;
                p.shelterCapacity = InitialShelterCapacity;
                p.caseworkCapacity = InitialCaseworkCapacity;
                p.requiredWorkerUnitsPerLoc = InitialRequiredWorkersPerLoc;
                p.initialERVCount = InitialERVCount;
                p.foodDemandProbability = InitialFoodDemandFrequency;
                p.externalRelationFrequency = InitialExternalRelationFrequency;
                p.emergencyTaskFrequency = InitialEmergencyTaskFrequency;
                p.shelterFloodThreshold = InitialShelterFloodThreshold;
                p.shelterFloodRadius = InitialShelterFloodRadius;
                p.shelterFloodComparisonType = InitialShelterFloodComparison;

                InstructorConfigManager.Instance.NotifyConfigChanged();
            }
        }

        ApplyConfigToScene();
        IsDataReady = true;
        Debug.Log("GameDataManager: Data initialization complete.");
    }

    /// <summary>
    /// Push the loaded parameters into objects that initialised before the config arrived (scene
    /// prebuilts, anything placed by the map config). Objects created later read GameDataManager in
    /// their own Start. Also logs every value in effect so a capture is self-describing (BUG_REPORTS B35).
    /// </summary>
    void ApplyConfigToScene()
    {
        // IsDataReady is set right after this returns; the apply methods check it, so flag first.
        IsDataReady = true;
        foreach (var storage in FindObjectsOfType<BuildingResourceStorage>(true))
            storage.ApplyConfiguredCapacities();
        foreach (var building in FindObjectsOfType<Building>(true))
            building.ApplyConfiguredWorkforce();
        var map = FindObjectOfType<MapSystem>();
        if (map != null && map.numberOfCommunities != InitialCommunityNumber)
            Debug.LogWarning($"GameDataManager: initialCommunityCount={InitialCommunityNumber} but the map has " +
                             $"{map.numberOfCommunities} communities; the count comes from the map, not the sheet.");
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        string json = "{\"source\":\"" + (configLoader != null ? configLoader.ConfigSource : "none")
            + "\",\"budget\":" + InitialBudget
            + ",\"satisfaction\":" + InitialSatisfaction.ToString(ic)
            + ",\"communityResidents\":" + InitialResidentsPerCommunityNumber
            + ",\"days\":" + InitialGameDays + ",\"roundsPerDay\":" + InitialRoundsPerDay
            + ",\"trained\":" + InitialTrainedVolunteerCount + ",\"untrained\":" + InitialUntrainedVolunteerCount
            + ",\"dailyAddition\":" + InitialDailyBudgetAddition
            + ",\"weather\":\"" + InitialWeather + "\""
            + ",\"shelterCapacity\":" + InitialShelterCapacity + ",\"caseworkCapacity\":" + InitialCaseworkCapacity
            + ",\"kitchenFoodCapacity\":" + InitialKitchenFoodCapacity + ",\"shelterFoodCapacity\":" + InitialShelterFoodCapacity
            + ",\"workersPerLocation\":" + InitialRequiredWorkersPerLoc
            + ",\"foodDemandFrequency\":" + InitialFoodDemandFrequency.ToString(ic)
            + ",\"ervCount\":" + InitialERVCount
            + ",\"externalRelationTotal\":" + InitialExternalRelationFrequency + ",\"emergencyTotal\":" + InitialEmergencyTaskFrequency
            + ",\"shelterFlood\":{\"cmp\":\"" + InitialShelterFloodComparison + "\",\"threshold\":" + InitialShelterFloodThreshold + ",\"radius\":" + InitialShelterFloodRadius + "}"
            + ",\"floodExpansion\":[" + InitialSunnyExpansionRate.ToString(ic) + "," + InitialSmallRainExpansionRate.ToString(ic) + "," + InitialMediumRainExpansionRate.ToString(ic) + "," + InitialHeavyRainExpansionRate.ToString(ic) + "," + InitialStormExpansionRate.ToString(ic) + "]"
            + ",\"floodSpread\":[" + InitialSunnySpreadChanceMultiplier.ToString(ic) + "," + InitialSmallRainSpreadChanceMultiplier.ToString(ic) + "," + InitialMediumRainSpreadChanceMultiplier.ToString(ic) + "," + InitialHeavyRainSpreadChanceMultiplier.ToString(ic) + "," + InitialStormSpreadChanceMultiplier.ToString(ic) + "]}";
        ParametersInEffectJson = json;
        Debug.Log("GameDataManager: parameters in effect " + json);
        SnapshotDebug.MarkContext("config:loaded", json);
    }

    void SetDefaults()
    {
        InitialBudget = defaultBudget;
        // Initial satisfaction always starts at 0 — defaultSatisfaction is intentionally unused here.
        InitialSatisfaction = 0f;
        InitialCommunityNumber =defaultCommunityNumber;
        InitialResidentsPerCommunityNumber =defaultResidentsPerCommunity;
        InitialGameDays =defaultGameDays;
        InitialRoundsPerDay =defaultRoundsPerDay;
        InitialTrainedVolunteerCount =defaultTrainedVolunteerCount;
        InitialUntrainedVolunteerCount =defaultUntrainedVolunteerCount;
        InitialDailyBudgetAddition =defaultDailyBudgetAllocs;
        InitialWeather = defaultInitialWeather;
        InitialKitchenCapacity = defaultKitchenCapacity;
        InitialShelterCapacity = defaultShelterCapacity;
        InitialCaseworkCapacity = defaultCaseworkCapacity;
        // PARITY BUILD (ledger D11): these two ARE still set, unlike the rest of D11. Upstream
        // has no such properties at all and its kitchens and shelters take their food capacity
        // from the PREFAB; `defaultKitchenFoodCapacity = 200` / `defaultShelterFoodCapacity = 100`
        // are those same prefab values. Removing the assignments does not reproduce upstream, it
        // leaves BuildingResourceStorage setting a capacity of ZERO — a divergence introduced by
        // the revert itself. Keeping them is what matches upstream's behaviour.
        InitialKitchenFoodCapacity = defaultKitchenFoodCapacity;
        InitialShelterFoodCapacity = defaultShelterFoodCapacity;
        InitialRequiredWorkersPerLoc = defaultRequiredWorkersPerLoc;
        InitialSunnyExpansionRate = defaultSunnyExpansionRate;
        InitialSunnySpreadChanceMultiplier = defaultSunnySpreadChanceMultiplier;
        InitialSmallRainExpansionRate = defaultSmallRainExpansionRate;
        InitialSmallRainSpreadChanceMultiplier = defaultSmallRainSpreadChanceMultiplier;
        InitialMediumRainExpansionRate = defaultMediumRainExpansionRate;
        InitialMediumRainSpreadChanceMultiplier = defaultMediumRainSpreadChanceMultiplier;
        InitialHeavyRainExpansionRate = defaultHeavyRainExpansionRate;
        InitialHeavyRainSpreadChanceMultiplier = defaultHeavyRainSpreadChanceMultiplier;
        InitialStormExpansionRate = defaultStormExpansionRate;
        InitialStormSpreadChanceMultiplier = defaultStormSpreadChanceMultiplier;
        InitialFoodDemandFrequency = defaultFoodDemandFrequency;
        InitialShelterFloodThreshold = defaultShelterFloodThreshold;
        InitialShelterFloodRadius = defaultShelterFloodRadius;
        InitialShelterFloodComparison = defaultShelterFloodComparison;
        InitialERVCount = defaultERVCount;
        InitialExternalRelationFrequency = defaultExternalRelationFrequency;
        // PARITY BUILD (ledger D11): upstream assigns the emergency frequency to the EXTERNAL
        // RELATION field and never sets InitialEmergencyTaskFrequency. Reproduced deliberately.
        // This one is not cosmetic: because of D7 these defaults are the parameters upstream
        // actually plays on, and a wrong task frequency changes how many tasks generate, how
        // many draws are taken, and therefore the whole RNG stream.
        InitialExternalRelationFrequency = defaultEmergencyTaskFrequency;
    }
}