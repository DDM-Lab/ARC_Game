using System.Collections;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class GameConfigLoader : MonoBehaviour
{
    [Header("Google Sheets Config")]
    [Tooltip("CSV URL. A root-relative path like \"/sheet.csv\" is fetched same-origin " +
             "(no CORS) — served on Talos by an Apache Alias, and locally by the dev proxy.")]
    public string googleSheetsCsvUrl = "/sheet.csv";

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
    public int loadedInitialSatisfaction=0;
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
    public int loadedInitialKitchenFoodCapacity = 200;   // FoodPacks a kitchen can hold (prefab value)
    public int loadedInitialShelterFoodCapacity = 100;   // FoodPacks a shelter can hold (prefab value)
    /// <summary>Which source the parameters in effect came from (BUG_REPORTS B35).</summary>
    public string ConfigSource { get; private set; } = "fallbacks";
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
    // (The sheet rows that used to be pushed into ScriptableObjects from here -- daily allocation,
    // food-demand probability, shelter flood-damage trigger, external-relation frequency -- are now
    // applied where they are consumed: TaskSystem / TaskDatabases read GameDataManager. BUG_REPORTS B35.)

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
    /// Parameter source chain (BUG_REPORTS B35): ARC_PARAM_CONFIG (a CSV path; RL runs vary parameters
    /// per run without a rebuild) -> the sheet URL (a root-relative /sheet.csv is only meaningful inside
    /// a browser, so it is skipped elsewhere) -> StreamingAssets/game_param_config.csv (editor,
    /// headless, any offline build) -> the serialized fallback fields. One source wins; ConfigSource
    /// names it and GameDataManager logs every value in effect.
    /// </summary>
    IEnumerator LoadConfigFromSheet()
    {
        string envPath = System.Environment.GetEnvironmentVariable("ARC_PARAM_CONFIG");
        if (!string.IsNullOrEmpty(envPath))
        {
            string text = null;
            try { text = System.IO.File.ReadAllText(envPath); }
            catch (System.Exception ex)
            {
                Debug.LogError($"GameConfigLoader: ARC_PARAM_CONFIG='{envPath}' could not be read - {ex.Message}");
            }
            if (text != null)
            {
                ParseCSV(text);
                FinishLoad("ARC_PARAM_CONFIG=" + envPath);
                yield break;
            }
        }

        bool rootRelative = !string.IsNullOrEmpty(googleSheetsCsvUrl) && googleSheetsCsvUrl.StartsWith("/");
        bool inBrowser = !string.IsNullOrEmpty(Application.absoluteURL);
        if (!string.IsNullOrEmpty(googleSheetsCsvUrl) && (!rootRelative || inBrowser))
        {
            // A root-relative URL ("/sheet.csv") is fetched same-origin -- no CORS, no hardcoded
            // host; resolve it against the page origin so UnityWebRequest gets a full URL.
            string resolvedUrl = googleSheetsCsvUrl;
            if (rootRelative)
            {
                try
                {
                    var pageUri = new System.Uri(Application.absoluteURL);
                    resolvedUrl = pageUri.GetLeftPart(System.UriPartial.Authority) + resolvedUrl;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"GameConfigLoader: could not resolve relative CSV URL against " +
                                     $"'{Application.absoluteURL}' - {ex.Message}. Using as-is.");
                }
            }
            // "?" when the URL has no query yet, "&" to extend an existing one.
            string cacheBustSep = resolvedUrl.Contains("?") ? "&" : "?";
            string urlWithCacheBuster = resolvedUrl + cacheBustSep + "t=" + System.DateTime.Now.Ticks;
            if (showDebugInfo)
                Debug.Log($"GameConfigLoader: Fetching config from {resolvedUrl} ...");
            using (UnityWebRequest request = UnityWebRequest.Get(urlWithCacheBuster))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    ParseCSV(request.downloadHandler.text);
                    FinishLoad(resolvedUrl);
                    yield break;
                }
                Debug.LogWarning($"GameConfigLoader: Failed to load config from {resolvedUrl} - {request.error}. Trying the local copy.");
            }
        }

        // The copy shipped with the build (kept identical to the deployed sheet).
        string localPath = Application.streamingAssetsPath + "/game_param_config.csv";
        string localUrl = localPath.Contains("://") ? localPath : "file://" + localPath;
        using (UnityWebRequest request = UnityWebRequest.Get(localUrl))
        {
            request.timeout = 5;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                ParseCSV(request.downloadHandler.text);
                FinishLoad(localPath);
                yield break;
            }
            Debug.LogWarning($"GameConfigLoader: Failed to load config - {request.error} - and no readable local copy at {localPath}. Using the serialized fallback values.");
        }
        FinishLoad("fallbacks");
    }

    void FinishLoad(string source)
    {
        ConfigSource = source;
        configLoaded = true;
        Debug.Log($"GameConfigLoader: parameters from {source}");
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
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Split(',')[0].Trim().Equals("parameter", System.StringComparison.OrdinalIgnoreCase))
                continue;   // the header row only (a row whose description merely mentions 'parameter' must not be skipped)
            
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
                // Intentionally ignored: initial satisfaction always starts at 0, never
                // loaded from the sheet. loadedInitialSatisfaction stays at its 0 default.
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
            else if (parameter.Equals("initialKitchenFoodCapacity", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int kitchenFood))
                    loadedInitialKitchenFoodCapacity = kitchenFood;
            }
            else if (parameter.Equals("initialShelterFoodCapacity", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int shelterFood))
                    loadedInitialShelterFoodCapacity = shelterFood;
            }
            else if (parameter.Equals("initialWorkerUnitsNeededPerLocation", System.StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out int reqWorkers))
                    loadedInitialRequiredWorkers = reqWorkers;
            }
            else if (parameter.Equals("initialSunnyFloodExpansionRateMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sunnyExpRt))
                    loadedInitialSunnyExpansionRate = sunnyExpRt;
            }
            else if (parameter.Equals("initialSunnyFloodSpreadChanceMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sunnySCM))
                    loadedInitialSunnySpreadChanceMultiplier = sunnySCM;
            }
            else if (parameter.Equals("initialSmallRainFloodExpansionRateMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float smallRainExpRt))
                    loadedInitialSmallRainExpansionRate = smallRainExpRt;
            }
            else if (parameter.Equals("initialSmallRainFloodSpreadChanceMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float smallRainSCM))
                    loadedInitialSmallRainSpreadChanceMultiplier = smallRainSCM;
            }
            else if (parameter.Equals("initialMediumRainFloodExpansionRateMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mediumRainExpRt))
                    loadedInitialMediumRainExpansionRate = mediumRainExpRt;
            }
            else if (parameter.Equals("initialMediumRainFloodSpreadChanceMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mediumRainSCM))
                    loadedInitialMediumRainSpreadChanceMultiplier = mediumRainSCM;
            }
            else if (parameter.Equals("initialHeavyRainFloodExpansionRateMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float heavyRainExpRt))
                    loadedInitialHeavyRainExpansionRate = heavyRainExpRt;
            }
            else if (parameter.Equals("initialHeavyRainFloodSpreadChanceMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float heavyRainSCM))
                    loadedInitialHeavyRainSpreadChanceMultiplier = heavyRainSCM;
            }
            else if (parameter.Equals("initialStormFloodExpansionRateMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float stormExpRt))
                    loadedInitialStormExpansionRate = stormExpRt;
            }
            else if (parameter.Equals("initialStormFloodSpreadChanceMultiplier", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float stormSCM))
                    loadedInitialStormSpreadChanceMultiplier = stormSCM;
            }
            else if (parameter.Equals("initialFoodDemandFrequency", System.StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float foodDemandFreq))
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
                    loadedInitialShelterFloodRadius = floodDetectionRange;   // was overwriting the threshold (BUG_REPORTS B30)
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
    }

    // ── Map Config from server (new) ──────────────────────────────────────────

    // ── Map provenance (read by WebSocketManager for the hello frame) ─────────
    // Maps are served OUTSIDE the router (see mapConfigServerUrl) so the router stays an
    // LLM/session concern and a partner can expose a map derived from proprietary data.
    // The cost of that separation is that a session log otherwise has NO record of which
    // map was actually in play — and a failed fetch silently falls back to the default
    // scene layout, quietly changing the experimental condition. These fields make the map
    // identity reportable, so the corpus is self-describing when transcripts are merged.
    public static string MapUrl { get; private set; } = "";
    public static string MapHash { get; private set; } = "";
    /// <summary>"loaded" (server map applied) | "default" (no URL configured — intentional)
    /// | "unreachable" (URL set, fetch failed) | "invalid" (fetched but unusable).</summary>
    public static string MapStatus { get; private set; } = "default";
    /// <summary>Set when strictMap is on in config.json AND the map could not be applied.
    /// A study deployment should refuse to run rather than silently use another map.</summary>
    public static bool MapFatal { get; private set; } = false;

    static string ShortHash(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] h = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }
    }

    IEnumerator LoadMapConfigFromServer()
    {
        // Override mapConfigServerUrl from config.json if present
        bool strictMap = false;
        string configPath = Application.streamingAssetsPath + "/config.json";
        using (UnityWebRequest cfgReq = UnityWebRequest.Get(configPath))
        {
            cfgReq.timeout = 3;
            yield return cfgReq.SendWebRequest();
            if (cfgReq.result == UnityWebRequest.Result.Success)
            {
                var cfg = JsonUtility.FromJson<AppConfig>(cfgReq.downloadHandler.text);
                if (!string.IsNullOrEmpty(cfg?.mapConfigUrl))
                    mapConfigServerUrl = cfg.mapConfigUrl;
                strictMap = cfg != null && cfg.strictMap;
            }
        }
        MapUrl = mapConfigServerUrl ?? "";

        if (string.IsNullOrEmpty(mapConfigServerUrl))
        {
            if (showDebugInfo)
                Debug.Log("GameConfigLoader: No map config URL set — using default scene layout.");
            MapStatus = "default";
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
                        MapStatus = "loaded";
                        MapHash = ShortHash(json);
                        Debug.Log($"GameConfigLoader: Map config loaded (schema v{parsed.schemaVersion}, "
                                  + $"{parsed.objects?.Count ?? 0} objects, hash {MapHash}) from {mapConfigServerUrl}");
                    }
                    else
                    {
                        MapStatus = "invalid";
                        Debug.LogWarning("GameConfigLoader: Map config JSON was empty or invalid. Using default layout.");
                    }
                }
                catch (System.Exception ex)
                {
                    MapStatus = "invalid";
                    Debug.LogWarning($"GameConfigLoader: Failed to parse map config JSON — {ex.Message}. Using default layout.");
                }
            }
            else
            {
                MapStatus = "unreachable";
                Debug.LogWarning($"GameConfigLoader: Could not reach map config server ({request.error}). Using default scene layout.");
            }

            // Study mode: a map that was CONFIGURED but could not be applied means this run
            // would silently execute a different condition than intended. Refuse instead.
            if (strictMap && MapStatus != "loaded")
            {
                MapFatal = true;
                Debug.LogError($"[GameConfigLoader] STRICT MAP: configured map '{mapConfigServerUrl}' "
                    + $"could not be applied (status={MapStatus}). This run would use the DEFAULT layout "
                    + "instead of the intended map — refusing to start. Fix the map endpoint, or unset "
                    + "strictMap in config.json.");
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
        // Initial satisfaction is locked to 0 at game start, regardless of what the
        // sheet/instructor config loaded into loadedInitialSatisfaction above.
        return 0;
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
    public int GetInitialKitchenFoodCapacity() => loadedInitialKitchenFoodCapacity;
    public int GetInitialShelterFoodCapacity() => loadedInitialShelterFoodCapacity;
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

    /// <summary>
    /// Used by CommunityFoodDepletionManager as its per-round depletion chance for communities
    /// (-1 = not configured, caller should keep its own Inspector default).
    /// </summary>
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