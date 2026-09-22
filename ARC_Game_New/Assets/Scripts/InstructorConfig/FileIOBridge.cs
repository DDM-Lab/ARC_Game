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
    static extern void OpenFilePicker(string callbackObject, string callbackMethod, string accept);
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
        DownloadText(filename, json, "Save Map Config", "json");
    }

    /// <summary>
    /// Save arbitrary text under any extension. `extension` is WITHOUT the dot and is what the
    /// Editor save panel filters on; the WebGL path takes the extension from `filename`.
    /// </summary>
    public void DownloadText(string filename, string text, string dialogTitle, string extension)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        DownloadTextFile(filename, text);
#elif UNITY_EDITOR
        string defaultName  = System.IO.Path.GetFileNameWithoutExtension(filename);
        string capturedText = text; // capture before deferred call
        // Defer one frame so Unity restores focus cleanly after any button click
        UnityEditor.EditorApplication.delayCall += () =>
        {
            string path = UnityEditor.EditorUtility.SaveFilePanel(
                dialogTitle, "", defaultName, extension);
            if (string.IsNullOrEmpty(path)) return; // user cancelled
            System.IO.File.WriteAllText(path, capturedText);
            Debug.Log($"[FileIOBridge] Saved file to: {path}");
        };
#else
        string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
        System.IO.File.WriteAllText(path, text);
        Debug.Log($"[FileIOBridge] Saved file to: {path}");
#endif
    }

    /// <summary>Open the browser file picker.  Result arrives via OnFileLoaded callback.</summary>
    public void OpenImportPicker()
    {
        OpenImportPicker("Import Map Config", "json", ".json,application/json");
    }

    /// <summary>
    /// Open a picker restricted to one extension. `accept` is the browser filter and must
    /// name the extension too, or the file cannot be selected at all in WebGL.
    /// </summary>
    public void OpenImportPicker(string dialogTitle, string extension, string accept)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // The jslib will call gameObject.SendMessage("FileIOBridge", "OnFileLoaded", text)
        OpenFilePicker(gameObject.name, nameof(OnFileLoaded), accept);
#else
        // Editor / Standalone fallback — use a simple dialog via StandaloneFileBrowser
        // or just try loading from persistentDataPath for testing.
        EditorFallbackImport(dialogTitle, extension);
#endif
    }

    // ── Callback from jslib (must be public, no parameters other than string) ─

    /// <summary>Called by jslib after the user picks a file.</summary>
    public void OnFileLoaded(string jsonContent)
    {
        OnFileImported?.Invoke(jsonContent);
    }

    // ── Editor/Standalone fallback ────────────────────────────────────────────

    void EditorFallbackImport(string dialogTitle, string extension)
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            string path = UnityEditor.EditorUtility.OpenFilePanel(dialogTitle, "", extension);
            if (string.IsNullOrEmpty(path)) return;
            string text = System.IO.File.ReadAllText(path);
            OnFileImported?.Invoke(text);
        };
#else
        // STANDALONE HAS NO PICKER, so "load" used to log a warning and do nothing at all --
        // the player clicked and the game sat there. Fall back to the NEWEST matching file in
        // persistentDataPath, which is also where DownloadText saves on this platform, so
        // save-then-load round-trips without a dialog.
        try
        {
            string dir = Application.persistentDataPath;
            string newest = null;
            System.DateTime newestAt = System.DateTime.MinValue;
            foreach (string path in System.IO.Directory.GetFiles(dir, "*." + extension))
            {
                System.DateTime at = System.IO.File.GetLastWriteTimeUtc(path);
                if (at > newestAt) { newestAt = at; newest = path; }
            }
            if (newest == null)
            {
                Debug.LogWarning($"[FileIOBridge] No .{extension} file found in {dir}");
                return;
            }
            Debug.Log($"[FileIOBridge] Standalone: loading newest .{extension} — {newest}");
            OnFileImported?.Invoke(System.IO.File.ReadAllText(newest));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FileIOBridge] Standalone import failed: {e}");
        }
#endif
    }
}
