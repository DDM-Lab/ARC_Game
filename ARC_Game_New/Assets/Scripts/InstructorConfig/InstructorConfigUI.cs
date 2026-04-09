using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Root UI controller for InstructorConfigScene.
///
/// ACTION BAR BUTTONS:
///   SaveToFileButton  → downloads current config as a .json file
///   ImportButton      → opens file picker to load a saved .json
///   SaveToServerButton→ POSTs to remote server (when URL is configured)
///   ClearButton       → wipes the map
///   BackButton        → returns to TitleScene
///
/// SCENE SETUP:
///   • Add a GameObject named "FileIOBridge" with the FileIOBridge script
///   • Wire all buttons in the Inspector (see fields below)
/// </summary>
public class InstructorConfigUI : MonoBehaviour
{
    [Header("Tab Buttons")]
    public Button mapEditorTabButton;
    public Button parametersTabButton;

    [Header("Tab Panels")]
    public GameObject mapEditorPanel;
    public GameObject parametersPanel;

    [Header("Action Bar")]
    public Button          saveToFileButton;    // download JSON to disk
    public Button          importButton;        // load JSON from disk
    public Button          saveToServerButton;  // POST to remote server
    public Button          clearButton;
    public Button          backButton;
    public TextMeshProUGUI statusLabel;

    [Header("References")]
    public MapEditorCanvas mapEditorCanvas;
    public FileIOBridge    fileIOBridge;

    [Header("Scene Names")]
    public string titleSceneName = "TitleScene";

    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        // Tab buttons
        mapEditorTabButton .onClick.AddListener(() => ShowTab(0));
        parametersTabButton.onClick.AddListener(() => ShowTab(1));

        // Action bar
        saveToFileButton  .onClick.AddListener(OnSaveToFileClicked);
        importButton      .onClick.AddListener(OnImportClicked);
        saveToServerButton.onClick.AddListener(OnSaveToServerClicked);
        clearButton       .onClick.AddListener(OnClearClicked);
        backButton        .onClick.AddListener(OnBackClicked);

        // File import callback
        if (fileIOBridge != null)
            fileIOBridge.OnFileImported += OnFileImported;

        // Server save callback
        InstructorConfigManager.Instance.OnSaveComplete += HandleSaveComplete;

        ShowTab(0);
        SetStatus("Config editor ready.");
    }

    void OnDestroy()
    {
        if (fileIOBridge != null)
            fileIOBridge.OnFileImported -= OnFileImported;

        if (InstructorConfigManager.Instance != null)
            InstructorConfigManager.Instance.OnSaveComplete -= HandleSaveComplete;
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    public void ShowTab(int tab)
    {
        mapEditorPanel .SetActive(tab == 0);
        parametersPanel.SetActive(tab == 1);
        mapEditorTabButton .interactable = tab != 0;
        parametersTabButton.interactable = tab != 1;
    }

    // ── Save to file (browser download) ──────────────────────────────────────

    void OnSaveToFileClicked()
    {
        string json     = InstructorConfigManager.Instance.GetConfigJson();
        string filename = $"map_config_{System.DateTime.Now:yyyyMMdd_HHmmss}.json";

        if (fileIOBridge != null)
            fileIOBridge.DownloadJson(filename, json);
        else
            GUIUtility.systemCopyBuffer = json; // fallback: copy to clipboard

        SetStatus($"Saved as {filename}");
    }

    // ── Import from file ──────────────────────────────────────────────────────

    void OnImportClicked()
    {
        if (fileIOBridge == null)
        {
            SetStatus("FileIOBridge not assigned.");
            return;
        }
        SetStatus("Opening file picker…");
        fileIOBridge.OpenImportPicker();
    }

    void OnFileImported(string json)
    {
        bool ok = InstructorConfigManager.Instance.LoadFromJson(json);
        if (ok)
        {
            mapEditorCanvas?.ReloadFromConfig();
            SetStatus("Config imported successfully.");
        }
        else
        {
            SetStatus("Import failed: invalid JSON.");
        }
    }

    // ── Save to server ────────────────────────────────────────────────────────

    void OnSaveToServerClicked()
    {
        if (InstructorConfigManager.Instance.IsSaving) return;
        SetStatus("Saving to server…");
        saveToServerButton.interactable = false;
        InstructorConfigManager.Instance.SaveToServer();
    }

    void HandleSaveComplete(bool success, string message)
    {
        saveToServerButton.interactable = true;
        SetStatus(message);
    }

    // ── Clear / Back ──────────────────────────────────────────────────────────

    void OnClearClicked()
    {
        mapEditorCanvas?.ClearMap();
        SetStatus("Map cleared.");
    }

    void OnBackClicked() => SceneManager.LoadScene(titleSceneName);

    // ── Helpers ───────────────────────────────────────────────────────────────

    void SetStatus(string msg)
    {
        if (statusLabel != null) statusLabel.text = msg;
    }
}
