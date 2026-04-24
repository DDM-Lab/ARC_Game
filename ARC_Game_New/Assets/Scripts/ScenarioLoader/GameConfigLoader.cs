using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

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

    // ── CSV parameters (existing) ─────────────────────────────────────────────
    public int loadedInitialBudget;
    public int loadedInitialSatisfaction;
    private bool configLoaded = false;

    // ── Map config (new) ──────────────────────────────────────────────────────
    private MapConfig loadedMapConfig;
    private bool mapConfigLoaded  = false; // true when fetch is done (success OR failure)
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
        if (disableLoader)
        {
            configLoaded    = true;
            mapConfigLoaded = true;
            return;
        }
        StartCoroutine(LoadConfigFromSheet());
        StartCoroutine(LoadMapConfigFromServer());
    }

    // ── Google Sheets CSV (existing, unchanged) ───────────────────────────────

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
            request.timeout = 5;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                ParseCSV(request.downloadHandler.text);
                if (showDebugInfo)
                    Debug.Log("GameConfigLoader: CSV config loaded successfully!");
            }
            else
            {
                Debug.LogWarning($"GameConfigLoader: Failed to load CSV - {request.error}. Using default values.");
                configLoaded = true;
            }
        }
    }

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
            string value     = parts[1].Trim();

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
        }

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
            request.timeout = (int)mapConfigTimeout;
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                try
                {
                    MapConfig parsed = JsonUtility.FromJson<MapConfig>(json);
                    if (parsed != null && parsed.gridWidth > 0 && parsed.gridHeight > 0)
                    {
                        loadedMapConfig  = parsed;
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
    public bool IsConfigLoaded() => configLoaded;

    /// <summary>Map config fetch is done (success or fallback).</summary>
    public bool IsMapConfigLoaded() => mapConfigLoaded;

    /// <summary>True when the server returned a valid MapConfig.</summary>
    public bool HasServerMapConfig() => mapConfigSuccess;

    /// <summary>
    /// Returns the server-loaded MapConfig, or null if unavailable.
    /// Check HasServerMapConfig() first; null means use default scene layout.
    /// </summary>
    public MapConfig GetMapConfig() => loadedMapConfig;

    // Existing getters (unchanged)
    public int GetInitialBudget()      => loadedInitialBudget;
    public int GetInitialSatisfaction() => loadedInitialSatisfaction;
}
