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


    public int InitialBudget { get; private set; }
    public float InitialSatisfaction { get; private set; }
    public int InitialCommunityNumber { get; private set; }
    public int InitialResidentsPerCommunityNumber { get; private set; }
    public int InitialGameDays { get; private set; }
    public int InitialRoundsPerDay { get; private set; }
    public int InitialTrainedVolunteerCount { get; private set; }
    public int InitialUntrainedVolunteerCount { get; private set; }
    public int InitialDailyBudgetAddition { get; private set; }


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
    }
}