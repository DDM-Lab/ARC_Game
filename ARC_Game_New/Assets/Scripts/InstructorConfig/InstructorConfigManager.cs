using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;

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
    public void SaveToServer()
    {
        if (string.IsNullOrEmpty(serverSaveUrl))
        {
            Debug.LogWarning("InstructorConfigManager: serverSaveUrl not set.");
            OnSaveComplete?.Invoke(false, "No server URL configured. Set InstructorConfigManager.serverSaveUrl.");
            return;
        }

        CurrentConfig.timestamp = DateTime.UtcNow.ToString("o");
        StartCoroutine(PostConfigCoroutine(GetConfigJson()));
    }

    IEnumerator PostConfigCoroutine(string json)
    {
        IsSaving = true;

        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        using (UnityWebRequest req = new UnityWebRequest(serverSaveUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(body);
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
        try
        {
            MapConfig loaded = JsonUtility.FromJson<MapConfig>(json);
            if (loaded == null || loaded.tiles == null) return false;
            CurrentConfig = loaded;
            NotifyConfigChanged();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"InstructorConfigManager: Failed to parse config JSON – {ex.Message}");
            return false;
        }
    }
}
