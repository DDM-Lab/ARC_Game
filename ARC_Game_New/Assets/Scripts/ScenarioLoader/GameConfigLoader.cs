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
    public int loadedInitialBudget=10000;
    public int loadedInitialSatisfaction=50;
    public int loadedInitialCommunityNumber=3;
    public int loadedInitialCommunityResidents=40;
    public int loadedInitialGameDays=8;
    public int loadedInitialGameRounds=4;
    public int loadedInitialTrainedVols=5;
    public int loadedInitialUntrainedVols=5;
    public int loadedInitialBudgetDailyAllocs=3000;
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

}