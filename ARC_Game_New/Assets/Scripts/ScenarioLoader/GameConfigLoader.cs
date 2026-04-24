using System.Collections;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class GameConfigLoader : MonoBehaviour
{
    [Header("Google Sheets Config")]
    [Tooltip("Publish your Google Sheet as CSV and paste the URL here")]
    public string googleSheetsCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vTGcKtnKuRq1dS-ZMMYKmepAEsfeaYhKlt8IMSkZ1xe-5_JApbSfTokI_VHFS8v0g3XIHWHWEPSdSzS/pub?gid=0&single=true&output=csv";

    [Header("Map Config Server")]
    [Tooltip("GET endpoint served by map_config_server.py  (leave blank to skip)")]
    public string mapConfigServerUrl = "http://localhost:8765/config";
    [Tooltip("Seconds before giving up and using the default scene layout")]
    public float mapConfigTimeout = 5f;



    [Header("Settings")]
    public bool disableLoader = false;

    [Header("Debug")]
    public bool showDebugInfo = true;

    // Loaded config 
    public int loadedInitialBudget=10000;
    public int loadedInitialSatisfaction=50;
    public int loadedInitialCommunityNumber=3;
    public int loadedInitialCommunityResidents=40;
    public int loadedInitialGameDays=8;
    public int loadedInitialGameRounds=4;
    public int loadedInitialTrainedVols=5;
    public int loadedInitialUntrainedVols=5;
    public int loadedInitialBudgetDailyAllocs=3000;
    public WeatherType loadedInitialWeather = WeatherType.Sunny;
    public int loadedInitialShelterCapacity = 10;
    public int loadedInitialKitchenCapacity = 10;
    public int loadedInitialCaseworkCapacity = 10;
    public int loadedInitialRequiredWorkers = 4;
    public float loadedInitialSunnyExpansionRate = 0f;
    public float loadedInitialSunnySpreadChanceMultiplier = 0.5f;
    public float loadedInitialSmallRainExpansionRate = 0.5f;
    public float loadedInitialSmallRainSpreadChanceMultiplier = 0.8f;
    public float loadedInitialMediumRainExpansionRate = 1.5f;
    public float loadedInitialMediumRainSpreadChanceMultiplier = 1f;
    public float loadedInitialHeavyRainExpansionRate = 3f;
    public float loadedInitialHeavyRainSpreadChanceMultiplier = 1.2f;
    public float loadedInitialStormExpansionRate = 5f;
    public float loadedInitialStormSpreadChanceMultiplier = 1.5f;

    public float loadedInitialFoodDemandFrequency = -1f; // default dne
    // for struct abv
    public int loadedInitialShelterFloodThreshold = 2;
    public int loadedInitialShelterFloodRadius = 5;
    public FloodedFacilityTrigger.ComparisonType loadedInitialShelterFloodComparison;
    // end

    public int loadedInitialERV = 3;
    public int loadedInitialExternalRelationFrequency = 3; // three per game
    public int loadedInitialEmergencyTaskFrequency = 4;


    private bool configLoaded = false;
    public TaskData dailyBudgetAlloc;
    public TaskData shelterFoodReq; // for food demand frequency lever
    public TaskData shelterFloodDmg; 
    public TaskData budgetAdvisoryER;
    public TaskData budgetEmergencyER;

    // ── Map config (new) ──────────────────────────────────────────────────────
    private MapConfig loadedMapConfig;
    private bool mapConfigLoaded = false; // true when fetch is done (success OR failure)
    private bool mapConfigSuccess = false; // true only when server returned valid data

    public static GameConfigLoader Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────

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
        }
    }
    
    void Start()
    {
        StartCoroutine(LoadConfigFromSheet());
        StartCoroutine(LoadMapConfigFromServer());
    }
    
    /// <summary>
    /// Load config from Google Sheets CSV
    /// </summary>
    IEnumerator LoadConfigFromSheet()
    {
        if (string.IsNullOrEmpty(googleSheetsCsvUrl))
        {
            Debug.LogWarning("GameConfigLoader: No Google Sheets URL provided. Using default values.");
            configLoaded = true;
            yield break;
        }
    
        string urlWithCacheBuster = googleSheetsCsvUrl + "&t=" + System.DateTime.Now.Ticks;
        
        if (showDebugInfo)
            Debug.Log("GameConfigLoader: Fetching config from Google Sheets...");
        
        using (UnityWebRequest request = UnityWebRequest.Get(urlWithCacheBuster))
        {
            // Set timeout
            request.timeout = 5;
            
            yield return request.SendWebRequest();
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                string csvData = request.downloadHandler.text;
                ParseCSV(csvData);
                
                if (showDebugInfo)
                    Debug.Log("GameConfigLoader: Config loaded successfully!");
            }
            else
            {
                Debug.LogWarning($"GameConfigLoader: Failed to load config - {request.error}. Using default values.");
                configLoaded = true;
            }
        }
    }
    
    /// <summary>
    /// Parse CSV data (simple implementation)
    /// Expected format: parameter,value
    /// Example: initialBudget,15000
    /// </summary>
    void ParseCSV(string csvData)
    {
        string[] lines = csvData.Split('\n');
        
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.ToLower().Contains("parameter"))
                continue;
            
            string[] parts = line.Split(',');
            if (parts.Length < 2) continue;
            
            string parameter = parts[0].Trim();
            string value = parts[1].Trim();
            
            if (parameter.Equals("initialBudget", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int budget))
                    loadedInitialBudget = budget;
            }
            else if (parameter.Equals("initialSatisfaction", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int satisfaction))
                    loadedInitialSatisfaction = satisfaction;
            }
            else if (parameter.Equals("initialCommunityCount", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int commCount))
                    loadedInitialCommunityNumber = commCount;
            }
            else if (parameter.Equals("initialCommunityResidentCount", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int resPerComm))
                    loadedInitialCommunityResidents = resPerComm;
            }
            else if (parameter.Equals("initialDaysPerRun", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int gameDays))
                    loadedInitialGameDays = gameDays;
            }
            else if (parameter.Equals("initialRoundsPerGameDay", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int roundsPerDay))
                    loadedInitialGameRounds = roundsPerDay;
            }
            else if (parameter.Equals("initialTrainedVolunteerCount", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int trainedVols))
                    loadedInitialTrainedVols = trainedVols;
            }
            else if (parameter.Equals("initialUntrainedVolunteerCount", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int untrainedVols))
                    loadedInitialUntrainedVols = untrainedVols;
            }
            else if (parameter.Equals("initialDailyBudgetAdditions", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int dailyBudgetAllocs))
                    loadedInitialBudgetDailyAllocs = dailyBudgetAllocs;
            }
            else if (parameter.Equals("initialWeather", System.StringComparison.OrdinalIgnoreCase))
            {
                if (System.Enum.TryParse(value, true, out WeatherType weather))
                {
                    loadedInitialWeather = weather;
                    if (showDebugInfo) Debug.Log($"GameConfigLoader: Weather set to {weather}");
                }
                else
                {
                    Debug.LogWarning($"GameConfigLoader: Could not parse weather type '{value}'. Defaulting to Sunny.");
                }
            }
            else if (parameter.Equals("initialKitchenCapacity", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int kitchenCapac))
                    loadedInitialKitchenCapacity = kitchenCapac;
                    Debug.Log($"gameconfigloader:kitchencpac - {loadedInitialKitchenCapacity}");
            }
            else if (parameter.Equals("initialShelterCapacity", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int shelterCapac))
                    loadedInitialShelterCapacity = shelterCapac;
            }
            else if (parameter.Equals("initialCaseworkCapacity", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int caseworkCapac))
                    loadedInitialCaseworkCapacity = caseworkCapac;
            }
            else if (parameter.Equals("initialWorkerUnitsNeededPerLocation", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int reqWorkers))
                    loadedInitialRequiredWorkers = reqWorkers;
            }
            else if (parameter.Equals("initialSunnyFloodExpansionRateMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, out float sunnyExpRt))
                    loadedInitialSunnyExpansionRate = sunnyExpRt;
            }
            else if (parameter.Equals("initialSunnyFloodSpreadChanceMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, out float sunnySCM))
                    loadedInitialSunnySpreadChanceMultiplier = sunnySCM;
            }
            else if (parameter.Equals("initialSmallRainFloodExpansionRateMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, out float smallRainExpRt))
                    loadedInitialSmallRainExpansionRate = smallRainExpRt;
            }
            else if (parameter.Equals("initialSmallRainFloodSpreadChanceMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, out float smallRainSCM))
                    loadedInitialSmallRainSpreadChanceMultiplier = smallRainSCM;
            }
            else if (parameter.Equals("initialMediumRainFloodExpansionRateMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, out float mediumRainExpRt))
                    loadedInitialMediumRainExpansionRate = mediumRainExpRt;
            }
            else if (parameter.Equals("initialMediumRainFloodSpreadChanceMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, out float mediumRainSCM))
                    loadedInitialMediumRainSpreadChanceMultiplier = mediumRainSCM;
            }
            else if (parameter.Equals("initialHeavyRainFloodExpansionRateMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, out float heavyRainExpRt))
                    loadedInitialHeavyRainExpansionRate = heavyRainExpRt;
            }
            else if (parameter.Equals("initialHeavyRainFloodSpreadChanceMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, out float heavyRainSCM))
                    loadedInitialHeavyRainSpreadChanceMultiplier = heavyRainSCM;
            }
            else if (parameter.Equals("initialStormFloodExpansionRateMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, out float stormExpRt))
                    loadedInitialStormExpansionRate = stormExpRt;
            }
            else if (parameter.Equals("initialStormFloodSpreadChanceMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, out float stormSCM))
                    loadedInitialStormSpreadChanceMultiplier = stormSCM;
            }
            else if (parameter.Equals("initialFoodDemandFrequency", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, out float foodDemandFreq))
                    loadedInitialFoodDemandFrequency = Mathf.Clamp(foodDemandFreq, 0f, 1f);
            }
            else if (parameter.Equals("initialShelterFloodDamageComparison", System.StringComparison.OrdinalIgnoreCase))
            {
                if (System.Enum.TryParse(value, true, out FloodedFacilityTrigger.ComparisonType cmp))
                {
                    loadedInitialShelterFloodComparison = cmp;
                }
                else
                {
                    Debug.LogWarning($"GameConfigLoader: Could not parse comparison type '{value}'.");
                }
            }
            else if (parameter.Equals("initialShelterFloodDamageFloodTileThreshold", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int floodTileThreshold))
                    loadedInitialShelterFloodThreshold = floodTileThreshold;
            }
            else if (parameter.Equals("initialShelterFloodDamageFloodDetectionRange", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int floodDetectionRange))
                    loadedInitialShelterFloodThreshold = floodDetectionRange;
            }
            else if (parameter.Equals("initialERVCount", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int ervCount))
                    loadedInitialERV = ervCount;
            }
            else if (parameter.Equals("initialExternalRelationFrequency", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int erCount))
                    loadedInitialExternalRelationFrequency = erCount;
            }
            else if (parameter.Equals("initialEmergencyTaskFrequency", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int emergencyCount))
                    loadedInitialEmergencyTaskFrequency = emergencyCount;
            }
            
        }
        ApplyInitBudgetAllocation();
        ApplyInitFoodDemandFrequency();
        ApplyInitShelterFloodDamage();
        ApplyInitExternalRelationFrequency();
        
        configLoaded = true;
    }

    // ── Map Config from server (new) ──────────────────────────────────────────

    IEnumerator LoadMapConfigFromServer()
    {
        if (string.IsNullOrEmpty(mapConfigServerUrl))
        {
            if (showDebugInfo)
                Debug.Log("GameConfigLoader: No map config URL set — using default scene layout.");
            mapConfigLoaded = true;
            yield break;
        }

        string urlWithCacheBuster = mapConfigServerUrl + "?t=" + System.DateTime.Now.Ticks;

        if (showDebugInfo)
            Debug.Log("GameConfigLoader: Fetching map config from server...");

        using (UnityWebRequest request = UnityWebRequest.Get(urlWithCacheBuster))
        {
            Debug.Log("GameConfigLoader: using entered");
            request.timeout = (int)mapConfigTimeout;
            yield return request.SendWebRequest();
            Debug.Log("GameConfigLoader: after yield return");

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("GameConfigLoader: unity web req success!");
                string json = request.downloadHandler.text;
                try
                {
                    MapConfig parsed = JsonUtility.FromJson<MapConfig>(json);
                    if (parsed != null && parsed.gridWidth > 0 && parsed.gridHeight > 0)
                    {
                        loadedMapConfig = parsed;
                        mapConfigSuccess = true;
                        if (showDebugInfo)
                            Debug.Log($"GameConfigLoader: Map config loaded (schema v{parsed.schemaVersion}, " +
                                      $"{parsed.objects?.Count ?? 0} objects).");
                    }
                    else
                    {
                        Debug.LogWarning("GameConfigLoader: Map config JSON was empty or invalid. Using default layout.");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"GameConfigLoader: Failed to parse map config JSON — {ex.Message}. Using default layout.");
                }
            }
            else
            {
                Debug.LogWarning($"GameConfigLoader: Could not reach map config server ({request.error}). Using default scene layout.");
            }

            mapConfigLoaded = true;
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>CSV parameters are ready (existing gate).</summary>
    //public bool IsConfigLoaded() => configLoaded;

    /// <summary>Map config fetch is done (success or fallback).</summary>
    public bool IsMapConfigLoaded() => mapConfigLoaded;

    /// <summary>True when the server returned a valid MapConfig.</summary>
    public bool HasServerMapConfig() => mapConfigSuccess;

    /// <summary>
    /// Returns the server-loaded MapConfig, or null if unavailable.
    /// Check HasServerMapConfig() first; null means use default scene layout.
    /// </summary>
    public MapConfig GetMapConfig() => loadedMapConfig;


    void ApplyInitBudgetAllocation()
    {
        if (dailyBudgetAlloc != null)
        {
            dailyBudgetAlloc.impacts[0].value = loadedInitialBudgetDailyAllocs;
            dailyBudgetAlloc.agentMessages[1].messageText = $"We received an additional ${loadedInitialBudgetDailyAllocs} in donations overnight, which can now be allocated to supply procurement or transport..";
            dailyBudgetAlloc.agentChoices[0].choiceImpacts[0].value = loadedInitialBudgetDailyAllocs;
            dailyBudgetAlloc.agentChoices[0].choiceText = $"Receive ${loadedInitialBudgetDailyAllocs} Budget";
            
            if (showDebugInfo)
                Debug.Log($"Applied {loadedInitialBudgetDailyAllocs} to SO");
        }
    }

    void ApplyInitFoodDemandFrequency()
    {
        if (loadedInitialFoodDemandFrequency < 0) return;
        if (shelterFoodReq != null)
        {
            if (shelterFoodReq.probabilityTriggers.Count != 0 )
            {
                shelterFoodReq.probabilityTriggers[0].probability = loadedInitialFoodDemandFrequency;
            } 
            else
            {
                ProbabilityTrigger trigger = new ProbabilityTrigger
                {
                    probability = loadedInitialFoodDemandFrequency
                };
                shelterFoodReq.probabilityTriggers.Add(trigger);
            }
        }
    }

void ApplyInitExternalRelationFrequency()
{
    if (budgetAdvisoryER == null && budgetEmergencyER == null) return;
    int advisoryInterval;
    int emergencyInterval;
    if (budgetAdvisoryER != null && budgetEmergencyER != null)
    {
        bool advisoryGetsLower = new System.Random().Next(0, 2) == 0;
        
        int smallHalf = loadedInitialExternalRelationFrequency / 2;
        int bigHalf = loadedInitialExternalRelationFrequency - smallHalf;

        advisoryInterval = advisoryGetsLower ? smallHalf : bigHalf;
        emergencyInterval = advisoryGetsLower ? bigHalf : smallHalf;
    }
    else
    {
        advisoryInterval = loadedInitialExternalRelationFrequency;
        emergencyInterval = loadedInitialExternalRelationFrequency;
    }
    if (budgetAdvisoryER != null) ApplyTrigger(budgetAdvisoryER, advisoryInterval);
    if (budgetEmergencyER != null) ApplyTrigger(budgetEmergencyER, emergencyInterval);
}

void ApplyTrigger(TaskData task, int interval)
{
    DayTrigger trigger = new DayTrigger
    {
        conditionType = DayTrigger.DayConditionType.DayInterval,
        intervalDays = interval,
        startDay = 2
    };

    if (task.dayTriggers.Count == 0 || task.dayTriggers[0] == null)
    {
        task.dayTriggers.Add(trigger);
    }
    else
    {
        task.dayTriggers[0] = trigger;
    }
}
    void ApplyInitShelterFloodDamage()
    {
        if (shelterFloodDmg == null) return;

        FloodedFacilityTrigger trigger = new FloodedFacilityTrigger
        {
            facilityType = FloodedFacilityTrigger.FacilityFloodType.SpecificBuildingType,
            specificBuildingType = BuildingType.Shelter,
            specificPrebuiltType = PrebuiltBuildingType.Community,

            comparison = loadedInitialShelterFloodComparison,
            floodTileThreshold = loadedInitialShelterFloodThreshold,
            detectionRadius = loadedInitialShelterFloodRadius
        };

        if (shelterFloodDmg.floodedFacilityTriggers.Count != 0)
            shelterFloodDmg.floodedFacilityTriggers[0] = trigger;
        else
            shelterFloodDmg.floodedFacilityTriggers.Add(trigger);
    }

    /// <summary>
    /// Check if config is ready
    /// </summary>
    public bool IsConfigLoaded()
    {
        return configLoaded;
    }
    
    /// <summary>
    /// Get initial budget
    /// </summary>
    public int GetInitialBudget()
    {
        return loadedInitialBudget;
    }

    public int GetInitialSatisfaction()
    {
        return loadedInitialSatisfaction;
    }
     public int GetInitialCommunityCount()
    {
        return loadedInitialCommunityNumber;
    }
     public int GetInitialResidentCountPerCommunity()
    {
        return loadedInitialCommunityResidents;
    }
     public int GetInitialNumDays()
    {
        return loadedInitialGameDays;
    }
     public int GetInitialNumRoundsPerGame()
    {
        return loadedInitialGameRounds;
    }
     public int GetInitialTrainedVolunteerCount()
    {
        return loadedInitialTrainedVols;
    }
     public int GetInitialUntrainedVolunteerCount()
    {
        return loadedInitialUntrainedVols;
    }
    public int GetInitialBudgetDailyAdditions()
    {
        return loadedInitialBudgetDailyAllocs;
    }

    public WeatherType GetInitialWeather()
    {
        return loadedInitialWeather;
    }
    public int GetInitialKitchenCapacity()
    {
        return loadedInitialKitchenCapacity;
    }
    public int GetInitialShelterCapacity()
    {
        return loadedInitialShelterCapacity;
    }
    public int GetInitialCaseworkCapacity()
    {
        return loadedInitialCaseworkCapacity;
    }
    public int GetInitialNeededWorkersPerLoc()
    {
        return loadedInitialRequiredWorkers;
    }
    public float GetInitialSunnyFloodExpansionRate()
    {
        Debug.Log($"sunn exp - {loadedInitialSunnyExpansionRate}");
        return loadedInitialSunnyExpansionRate;
    }
    public float GetInitialSunnyFloodSpreadChanceMultiplier()
    {
        Debug.Log($"sunn spread - {loadedInitialSunnySpreadChanceMultiplier}");
        return loadedInitialSunnySpreadChanceMultiplier;
    }
    public float GetInitialSmallRainFloodExpansionRate()
    {
        Debug.Log($"smallrain exp - {loadedInitialSmallRainExpansionRate}");
        return loadedInitialSmallRainExpansionRate;
    }
    public float GetInitialSmallRainFloodSpreadChanceMultiplier()
    {
        Debug.Log($"smallrain spread - {loadedInitialSmallRainSpreadChanceMultiplier}");
        return loadedInitialSmallRainSpreadChanceMultiplier;
    }
    public float GetInitialMediumRainFloodExpansionRate()
    {
        Debug.Log($"medrain exp - {loadedInitialMediumRainExpansionRate}");
        return loadedInitialMediumRainExpansionRate;
    }
    public float GetInitialMediumRainFloodSpreadChanceMultiplier()
    {
        Debug.Log($"medrain spread - {loadedInitialMediumRainSpreadChanceMultiplier}");
        return loadedInitialMediumRainSpreadChanceMultiplier;
    }
    public float GetInitialHeavyRainFloodExpansionRate()
    {
        Debug.Log($"heavyrain exp - {loadedInitialHeavyRainExpansionRate}");
        return loadedInitialHeavyRainExpansionRate;
    }
    public float GetInitialHeavyRainFloodSpreadChanceMultiplier()
    {
        Debug.Log($"heavyrain spread - {loadedInitialHeavyRainSpreadChanceMultiplier}");
        return loadedInitialHeavyRainSpreadChanceMultiplier;
    }
    public float GetInitialStormFloodExpansionRate()
    {
        Debug.Log($"storm exp - {loadedInitialStormExpansionRate}");
        return loadedInitialStormExpansionRate;
    }
    public float GetInitialStormFloodSpreadChanceMultiplier()
    {
        Debug.Log($"storm spread - {loadedInitialStormSpreadChanceMultiplier}");
        return loadedInitialStormSpreadChanceMultiplier;
    }

    public float GetInitialFoodDemandFrequency()
    {
        return loadedInitialFoodDemandFrequency;
    }

    public int GetInitialERVCount()
    {
        return loadedInitialERV;
    }
    public int GetInitialExternalRelationFrequency()
    {
        return loadedInitialExternalRelationFrequency;
    }
    public int GetInitialEmergencyTaskFrequency()
    {
        return loadedInitialEmergencyTaskFrequency;
    }

    public int GetInitialShelterFloodThreshold() => loadedInitialShelterFloodThreshold;
    public int GetInitialShelterFloodRadius() => loadedInitialShelterFloodRadius;
    public FloodedFacilityTrigger.ComparisonType GetInitialShelterFloodComparison() => loadedInitialShelterFloodComparison;

}