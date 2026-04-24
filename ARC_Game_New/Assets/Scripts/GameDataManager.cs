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
    public float defaultSatisfaction = 50f; // initialSatisfaction
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
    //public FloodedFacilityTrigger defaultShelterFloodDamage = new FloodedFacilityTrigger
    //{
    //    facilityType = FloodedFacilityTrigger.FacilityFloodType.SpecificBuildingType,
    //    comparison = FloodedFacilityTrigger.ComparisonType.AtLeast,
    //    floodTileThreshold = 2,
    //    specificBuildingType = BuildingType.Shelter,
    //    specificPrebuiltType = PrebuiltBuildingType.Community,
    //    detectionRadius = 5
    //};
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
    //public FloodedFacilityTrigger InitialShelterFloodDamange {get; private set;}
    public int InitialERVCount {get; private set;}
    public int InitialExternalRelationFrequency {get; private set;}
    public int InitialEmergencyTaskFrequency {get; private set;}

    public int InitialShelterFloodThreshold { get; private set; }
    public int InitialShelterFloodRadius { get; private set; }
    public FloodedFacilityTrigger.ComparisonType InitialShelterFloodComparison { get; private set; }


    public bool IsDataReady { get; private set; } = false;

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
        
        StartCoroutine(LoadAllData());
    }

    IEnumerator LoadAllData()
    {
        if (useExternalConfig)
        {   
            if (configLoader == null)
            {
                Debug.LogError("GameDataManager: use external confog true but no gameconfigloader");
                SetDefaults();
            }
            else
            {
                while (!configLoader.IsConfigLoaded())
                {
                    yield return null;
                }

                InitialBudget = configLoader.GetInitialBudget();
                InitialSatisfaction = (float)configLoader.GetInitialSatisfaction();
                InitialCommunityNumber =configLoader.GetInitialCommunityCount();
                InitialResidentsPerCommunityNumber =configLoader.GetInitialResidentCountPerCommunity();
                InitialGameDays =configLoader.GetInitialNumDays();
                InitialRoundsPerDay =configLoader.GetInitialNumRoundsPerGame();
                InitialTrainedVolunteerCount =configLoader.GetInitialTrainedVolunteerCount();
                InitialUntrainedVolunteerCount =configLoader.GetInitialUntrainedVolunteerCount();
                InitialDailyBudgetAddition =configLoader.GetInitialBudgetDailyAdditions();
                InitialWeather = configLoader.GetInitialWeather();
                InitialKitchenCapacity = configLoader.GetInitialKitchenCapacity();
                InitialShelterCapacity = configLoader.GetInitialShelterCapacity();
                InitialCaseworkCapacity = configLoader.GetInitialCaseworkCapacity();
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
                //InitialShelterFloodDamange = configLoader.GetInitialShelterFLoodDamage();
                InitialERVCount = configLoader.GetInitialERVCount();
                InitialExternalRelationFrequency = configLoader.GetInitialExternalRelationFrequency();
                InitialEmergencyTaskFrequency = configLoader.GetInitialEmergencyTaskFrequency();
                InitialShelterFloodThreshold = configLoader.GetInitialShelterFloodThreshold();
                InitialShelterFloodRadius = configLoader.GetInitialShelterFloodRadius();
                InitialShelterFloodComparison = configLoader.GetInitialShelterFloodComparison();


                Debug.Log("GameDataManager: extern config success!");
            }
        }
        else
        {
            Debug.Log("GameDataManager: fallback to defaults");
            SetDefaults();
        }
        // Inside LoadAllData() in GameDataManager.cs
        yield return new WaitUntil(() => configLoader.IsConfigLoaded()); // Wait for download

        if (InstructorConfigManager.Instance != null)
        {
            // Reach into the 'Vault' and get the parameters sheet
            var p = InstructorConfigManager.Instance.CurrentConfig.parameters;

            // --- Existing Mappings ---
            p.initialBudget = InitialBudget;
            p.dailyBudgetAllocation = (float)InitialDailyBudgetAddition;
            p.initialSatisfaction = (int)InitialSatisfaction;
            p.numberOfCommunities = InitialCommunityNumber;
            p.gameDurationDays = InitialGameDays;

            // --- Add These New Mappings ---
            p.residentsPerCommunity = InitialResidentsPerCommunityNumber;
            p.totalPopulation = InitialResidentsPerCommunityNumber * InitialCommunityNumber; // fresh
            p.roundsPerDay = InitialRoundsPerDay;
            p.initialWeather = InitialWeather;
            p.initialTrainedVolunteerCount = InitialTrainedVolunteerCount;
            p.initialUntrainedVolunteerCount = InitialUntrainedVolunteerCount;
            p.initialWorkerCount = InitialTrainedVolunteerCount + InitialUntrainedVolunteerCount; // fresh

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
            p.shelterFloodComparisonType = InitialShelterFloodComparison; //23

            // Notify the UI that we have fresh data from the loader
            InstructorConfigManager.Instance.NotifyConfigChanged();
        }

        IsDataReady = true;
    }

    void SetDefaults()
    {
        InitialBudget = defaultBudget;
        InitialSatisfaction = defaultSatisfaction;
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
        //InitialShelterFloodDamange = defaultShelterFloodDamage;
        InitialShelterFloodThreshold = defaultShelterFloodThreshold;
        InitialShelterFloodRadius = defaultShelterFloodRadius;
        InitialShelterFloodComparison = defaultShelterFloodComparison;
        InitialERVCount = defaultERVCount;
        InitialExternalRelationFrequency = defaultExternalRelationFrequency;
        InitialExternalRelationFrequency = defaultEmergencyTaskFrequency;
    }
}