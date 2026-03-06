using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Root UI controller for InstructorConfigScene.
/// Manages tab switching (Map Editor ↔ Parameters) and the action bar
/// (Save to server, Clear map, Export JSON, Back to title).
///
/// SCENE SETUP – on a root Canvas:
///   ┌─ TabBar
///   │    ├─ MapEditorTabButton   → OnClick: ShowTab(0)
///   │    └─ ParametersTabButton  → OnClick: ShowTab(1)
///   ├─ MapEditorPanel  (contains MapEditorCanvas script)
///   ├─ ParametersPanel (contains ParametersPanel script)
///   └─ ActionBar
///        ├─ SaveButton           → OnClick: OnSaveClicked()
///        ├─ ClearButton          → OnClick: OnClearClicked()
///        ├─ ExportButton         → OnClick: OnExportJsonClicked()
///        ├─ BackButton           → OnClick: OnBackClicked()
///        └─ StatusLabel (TMP)
///
/// Also place an InstructorConfigManager prefab in the scene
/// (it will self-destruct if another one exists from a previous scene).
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
    public Button             saveButton;
    public Button             clearButton;
    public Button             exportJsonButton;
    public Button             backButton;
    public TextMeshProUGUI    statusLabel;

    [Header("References")]
    public MapEditorCanvas mapEditorCanvas;

    [Header("Scene Names")]
    public string titleSceneName = "TitleScene";

    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        // Tab buttons
        mapEditorTabButton.onClick.AddListener(() => ShowTab(0));
        parametersTabButton.onClick.AddListener(() => ShowTab(1));

        // Action bar
        saveButton.onClick.AddListener(OnSaveClicked);
        clearButton.onClick.AddListener(OnClearClicked);
        exportJsonButton.onClick.AddListener(OnExportJsonClicked);
        backButton.onClick.AddListener(OnBackClicked);

        // Subscribe to manager events
        InstructorConfigManager.Instance.OnSaveComplete += HandleSaveComplete;

        ShowTab(0);
        SetStatus("Config editor ready.");
    }

    void OnDestroy()
    {
        if (InstructorConfigManager.Instance != null)
            InstructorConfigManager.Instance.OnSaveComplete -= HandleSaveComplete;
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    public void ShowTab(int tab)
    {
        mapEditorPanel.SetActive(tab == 0);
        parametersPanel.SetActive(tab == 1);

        mapEditorTabButton.interactable  = tab != 0;
        parametersTabButton.interactable = tab != 1;
    }

    // ── Action handlers ───────────────────────────────────────────────────────

    void OnSaveClicked()
    {
        if (InstructorConfigManager.Instance.IsSaving) return;
        SetStatus("Saving…");
        saveButton.interactable = false;
        InstructorConfigManager.Instance.SaveToServer();
    }

    void HandleSaveComplete(bool success, string message)
    {
        saveButton.interactable = true;
        SetStatus(message);
    }

    void OnClearClicked()
    {
        if (mapEditorCanvas != null)
            mapEditorCanvas.ClearMap();
        SetStatus("Map cleared.");
    }

    void OnExportJsonClicked()
    {
        string json = InstructorConfigManager.Instance.GetConfigJson();
        GUIUtility.systemCopyBuffer = json;
        Debug.Log($"[InstructorConfig] Exported JSON:\n{json}");
        SetStatus("JSON copied to clipboard.");
    }

    void OnBackClicked()
    {
        SceneManager.LoadScene(titleSceneName);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void SetStatus(string msg)
    {
        if (statusLabel != null)
            statusLabel.text = msg;
    }
}
