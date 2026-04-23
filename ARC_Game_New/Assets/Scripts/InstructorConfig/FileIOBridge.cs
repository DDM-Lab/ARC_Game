using System; 
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Thin C# bridge to the WebGL FileIO.jslib plugin.
///
/// In the Editor / Standalone the jslib functions are unavailable, so we fall
/// back to System.IO.File so you can still test outside the browser.
///
/// </summary>
public class FileIOBridge : MonoBehaviour
{
    public static FileIOBridge Instance { get; private set; }

    /// <summary>Fires when the user has picked a file; payload is the raw JSON string.</summary>
    public event Action<string> OnFileImported;

    // ── jslib imports ─────────────────────────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void DownloadTextFile(string filename, string content);

    [DllImport("__Internal")]
    static extern void OpenFilePicker(string callbackObject, string callbackMethod);
#endif

    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Trigger a browser download of the given JSON string.</summary>
    public void DownloadJson(string filename, string json)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        DownloadTextFile(filename, json);
#elif UNITY_EDITOR
        string defaultName  = System.IO.Path.GetFileNameWithoutExtension(filename);
        string capturedJson = json; // capture before deferred call
        // Defer one frame so Unity restores focus cleanly after any button click
        UnityEditor.EditorApplication.delayCall += () =>
        {
            string path = UnityEditor.EditorUtility.SaveFilePanel(
                "Save Map Config", "", defaultName, "json");
            if (string.IsNullOrEmpty(path)) return; // user cancelled
            System.IO.File.WriteAllText(path, capturedJson);
            Debug.Log($"[FileIOBridge] Saved JSON to: {path}");
        };
#else
        string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
        System.IO.File.WriteAllText(path, json);
        Debug.Log($"[FileIOBridge] Saved JSON to: {path}");
#endif
    }

    /// <summary>Open the browser file picker.  Result arrives via OnFileLoaded callback.</summary>
    public void OpenImportPicker()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // The jslib will call gameObject.SendMessage("FileIOBridge", "OnFileLoaded", text)
        OpenFilePicker(gameObject.name, nameof(OnFileLoaded));
#else
        // Editor / Standalone fallback — use a simple dialog via StandaloneFileBrowser
        // or just try loading from persistentDataPath for testing.
        EditorFallbackImport();
#endif
    }

    // ── Callback from jslib (must be public, no parameters other than string) ─

    /// <summary>Called by jslib after the user picks a file.</summary>
    public void OnFileLoaded(string jsonContent)
    {
        OnFileImported?.Invoke(jsonContent);
    }

    // ── Editor/Standalone fallback ────────────────────────────────────────────

    void EditorFallbackImport()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            string path = UnityEditor.EditorUtility.OpenFilePanel("Import Map Config", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            string text = System.IO.File.ReadAllText(path);
            OnFileImported?.Invoke(text);
        };
#else
        Debug.LogWarning("[FileIOBridge] File picker not supported in Standalone build. " +
                         "Place a config.json in: " + Application.persistentDataPath);
#endif
    }
}
