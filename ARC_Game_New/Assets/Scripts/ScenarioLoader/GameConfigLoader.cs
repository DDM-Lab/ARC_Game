using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class GameConfigLoader : MonoBehaviour
{
    [Header("Google Sheets Config")]
    [Tooltip("Publish your Google Sheet as CSV and paste the URL here")]
    public string googleSheetsCsvUrl = "https://docs.google.com/spreadsheets/d/e/2PACX-1vTGcKtnKuRq1dS-ZMMYKmepAEsfeaYhKlt8IMSkZ1xe-5_JApbSfTokI_VHFS8v0g3XIHWHWEPSdSzS/pub?gid=0&single=true&output=csv";
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Loaded config 
    public int loadedInitialBudget;
    public int loadedInitialSatisfaction;
    private bool configLoaded = false;
    
    public static GameConfigLoader Instance { get; private set; }
    
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
        }
        
        configLoaded = true;
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

}