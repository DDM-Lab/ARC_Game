using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// StreamingAssets/config.json, read ONCE at startup for code that is not on the LLM connect
/// path. WebSocketManager.LoadedConfig is only filled when connecting to the router, so in the
/// plain human build nothing else ever read keys like logServerUrl or testMode.
///
/// The read is asynchronous (the browser cannot read a file synchronously), so it finishes a
/// moment after the title screen appears, well before MainScene. Code that must decide before
/// that waits on <see cref="WhenLoaded"/>. <see cref="Loaded"/> turns true even when the file
/// is missing or malformed; <see cref="Config"/> is then null and every key takes its default.
/// </summary>
public static class RuntimeConfig
{
    /// <summary>Parsed config.json, or null if it could not be read.</summary>
    public static AppConfig Config { get; private set; }

    /// <summary>The read has finished (successfully or not).</summary>
    public static bool Loaded { get; private set; }

    /// <summary>Human-testing lockdown (config.json "testMode"). False until the file has loaded.</summary>
    public static bool TestMode => Config != null && Config.testMode;

    /// <summary>Upload endpoint from config.json, or null to keep the one saved in the scene.</summary>
    public static string LogServerUrl
    {
        get
        {
            string u = Config?.logServerUrl;
            return !string.IsNullOrWhiteSpace(u) && (u.StartsWith("https://") || u.StartsWith("http://"))
                ? u.Trim() : null;
        }
    }

    static System.Action onLoaded;

    /// <summary>Run <paramref name="action"/> once the read has finished (at once if it has).</summary>
    public static void WhenLoaded(System.Action action)
    {
        if (action == null) return;
        if (Loaded) action(); else onLoaded += action;
    }

    /// <summary>Coroutine form: `yield return RuntimeConfig.Wait();`</summary>
    public static IEnumerator Wait()
    {
        while (!Loaded) yield return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (Loaded || Object.FindObjectOfType<Runner>() != null) return;
        var go = new GameObject("[RuntimeConfig]");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Runner>();
    }

    class Runner : MonoBehaviour
    {
        IEnumerator Start()
        {
            string raw = Application.streamingAssetsPath + "/config.json";
            string path = raw.Contains("://") ? raw : "file://" + raw;
            using (UnityWebRequest req = UnityWebRequest.Get(path))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    try { Config = JsonUtility.FromJson<AppConfig>(req.downloadHandler.text); }
                    catch (System.Exception e) { Debug.LogWarning($"[RuntimeConfig] config.json is malformed: {e.Message}"); }
                }
                else
                {
                    Debug.Log($"[RuntimeConfig] config.json unreadable ({req.error}); using defaults.");
                }
            }
            Loaded = true;
            Debug.Log($"[RuntimeConfig] loaded: testMode={TestMode} logServerUrl={(LogServerUrl ?? "<scene>")}");
            var cb = onLoaded; onLoaded = null;
            try { cb?.Invoke(); }
            catch (System.Exception e) { Debug.LogError($"[RuntimeConfig] a WhenLoaded callback threw: {e}"); }
            Destroy(gameObject);
        }
    }
}
