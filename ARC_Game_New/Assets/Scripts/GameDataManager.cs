using UnityEngine;
using System.Collections;

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }

    [Header("Config Settings")]
    public bool useExternalConfig = true;
    
    public GameConfigLoader configLoader;

    [Header("Defaults")]
    public int defaultBudget = 10000;
    [Range(0f, 100f)]
    public float defaultSatisfaction = 50f;
    public int defaultCommunityNumber= 3;
    public int defaultResidentsPerCommunity = 40;
    public int defaultGameDays = 8;
    public int defaultRoundsPerDay = 4;
    public int defaultTrainedVolunteerCount = 5;
    public int defaultUntrainedVolunteerCount = 5;
    public int defaultDailyBudgetAllocs = 3000;
    public WeatherType defaultInitialWeather = WeatherType.Sunny;
    public int defaultKitchenCapacity = 10;
    public int defaultShelterCapacity = 10;
    public int defaultCaseworkCapacity = 10;
    public int defaultRequiredWorkersPerLoc = 4;
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

                Debug.Log("GameDataManager: extern config success!");
            }
        }
        else
        {
            Debug.Log("GameDataManager: fallback to defaults");
            SetDefaults();
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
    }
}