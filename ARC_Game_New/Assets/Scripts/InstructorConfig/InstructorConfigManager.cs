using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Singleton that owns the MapConfig being edited.
/// Lives DontDestroyOnLoad so config survives scene transitions.
/// </summary>
public class InstructorConfigManager : MonoBehaviour
{
    public static InstructorConfigManager Instance { get; private set; }

    [Header("Grid Defaults")]
    public int defaultGridWidth  = 30;
    public int defaultGridHeight = 20;

    [Header("Server (fill in when endpoint is ready)")]
    [Tooltip("POST endpoint that accepts the config JSON body")]
    public string serverSaveUrl = "";

    // ── Current config being edited ───────────────────────────────────────────
    public MapConfig CurrentConfig { get; private set; }
    public bool      IsSaving      { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action          OnConfigChanged;
    /// <param name="success">true = server accepted; false = error/no URL</param>
    /// <param name="message">Human-readable result message</param>
    public event Action<bool, string> OnSaveComplete;

    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            NewConfig();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        StartCoroutine(SyncWithServerCoroutine());
    }

    /// <summary>Reset to a blank config with default grid size.</summary>
    public void NewConfig()
    {
        CurrentConfig = new MapConfig();
        CurrentConfig.Initialize(defaultGridWidth, defaultGridHeight);
    }

    /// <summary>Notify all listeners that the config data changed.</summary>
    public void NotifyConfigChanged() => OnConfigChanged?.Invoke();

    // ── Serialization ─────────────────────────────────────────────────────────

    public string GetConfigJson()
    {
        CurrentConfig.timestamp = DateTime.UtcNow.ToString("o");
        return JsonUtility.ToJson(CurrentConfig, true);
    }

    /// <summary>
    /// POST the current config JSON to the configured server URL.
    /// Fires OnSaveComplete when done (on main thread).
    /// </summary>
    public void SaveToServer(string fileName = "latest_map_config.json")
    {
        if (string.IsNullOrEmpty(serverSaveUrl))
        {
            Debug.LogWarning("InstructorConfigManager: serverSaveUrl not set.");
            OnSaveComplete?.Invoke(false, "No server URL configured. Set InstructorConfigManager.serverSaveUrl.");
            return;
        }

        string reqUrl = $"{serverSaveUrl}?file={fileName}";
        CurrentConfig.timestamp = DateTime.UtcNow.ToString("o");
        StartCoroutine(PostConfigCoroutine(reqUrl, GetConfigJson()));
    }
    IEnumerator SyncWithServerCoroutine()
    {
        if (GameConfigLoader.Instance != null && GameConfigLoader.Instance.HasServerMapConfig())
        {
            CurrentConfig = GameConfigLoader.Instance.GetMapConfig();
            NotifyConfigChanged();
            Debug.Log("InstructorConfigManager: Synced from GameConfigLoader.");
            yield break;
        }

        using (UnityWebRequest req = UnityWebRequest.Get(serverSaveUrl))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                LoadFromJson(req.downloadHandler.text);
                LoadFromJson(req.downloadHandler.text);
                Debug.Log("InstructorConfigManager: Synced from Server.");
            }
            else
            {
                Debug.Log("InstructorConfigManager: No existing config found. Using defaults.");
            }
        }

    }
    // load easy/med/hard
    public void LoadPresetFromServer(string fileName, bool isMapOnly)
    {
        string requestUrl = $"{serverSaveUrl}?file={fileName}";
        StartCoroutine(GetPresetCoroutine(requestUrl, isMapOnly));
    }

    IEnumerator GetPresetCoroutine(string url, bool isMapOnly)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string json = req.downloadHandler.text;
                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogError("InstructorConfigManager: Server ret. empty string");
                    yield break;
                }

                try
                {
                    MapConfig preset = JsonUtility.FromJson<MapConfig>(json);
                    if (isMapOnly)
                    {
                        CurrentConfig.landLayer = preset.landLayer;
                        CurrentConfig.riverLayer = preset.riverLayer;
                        CurrentConfig.blockingLayer = preset.blockingLayer;
                        CurrentConfig.roadLayer = preset.roadLayer;
                        CurrentConfig.objects = preset.objects;
                    }
                    else
                    {
                        CurrentConfig.parameters = preset.parameters;
                    }
                    NotifyConfigChanged();
                }
                catch (Exception e)
                {
                    Debug.LogError($"InstructorConfigManager: Failed to parse config JSON: {e.Message}!");
                }
            }
            else
            {
                Debug.LogWarning($"InstructorConfigManager: Preset not found/server error: {req.error}!");
            }
        }
    }

    IEnumerator PostConfigCoroutine(string url, string json)
    {
        IsSaving = true;

        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        //using (UnityWebRequest req = new UnityWebRequest(serverSaveUrl, "POST"))
        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;

            yield return req.SendWebRequest();

            IsSaving = false;

            bool ok = req.result == UnityWebRequest.Result.Success;
            string msg = ok
                ? "Config saved to server successfully."
                : $"Save failed: {req.error}";

            Debug.Log($"InstructorConfigManager: {msg}");
            OnSaveComplete?.Invoke(ok, msg);
        }
    }

    /// <summary>
    /// Load a config from a JSON string (e.g. fetched from server).
    /// Returns false if parsing fails.
    /// </summary>

    public bool LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning("InstructorConfigManager: Got empty JSON string");
            return false;
        }
        try
        {
            MapConfig loaded = JsonUtility.FromJson<MapConfig>(json);
            if (loaded == null || loaded.landLayer == null) return false;
            CurrentConfig = loaded;
            NotifyConfigChanged();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"InstructorConfigManager: Parse error... {ex.Message}");
            return false;
        }
    }

    // only serialize map/param
    [Serializable]
    private class MapOnlyWrapper
    {
        public int gridWidth;
        public int gridHeight;
        public bool[] landLayer;
        public bool[] riverLayer;
        public bool[] blockingLayer;
        public bool[] roadLayer;
        public List<PlacedObjectData> objects;
    }

    [Serializable]
    private class ParamsOnlyWrapper
    {
        public ScenarioParameters parameters;
    }

    public void SaveMapOnly(string fileName)
    {
        var data = new MapOnlyWrapper
        {
            gridWidth = CurrentConfig.gridWidth,
            gridHeight = CurrentConfig.gridHeight,
            landLayer = CurrentConfig.landLayer,
            riverLayer = CurrentConfig.riverLayer,
            blockingLayer = CurrentConfig.blockingLayer,
            roadLayer = CurrentConfig.roadLayer,
            objects = CurrentConfig.objects
        };
        string json = JsonUtility.ToJson(data);
        StartCoroutine(PostConfigCoroutine($"{serverSaveUrl}?file={fileName}", json));
    }

    public void SaveParamsOnly(string fileName)
    {
        var data = new ParamsOnlyWrapper
        {
            parameters = CurrentConfig.parameters
        };
        string json = JsonUtility.ToJson(data);
        StartCoroutine(PostConfigCoroutine($"{serverSaveUrl}?file={fileName}", json));
    }
}
