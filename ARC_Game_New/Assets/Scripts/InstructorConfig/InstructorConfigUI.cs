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
    public TMP_Dropdown mapPresetDropdown;
    public TMP_Dropdown paramPresetDropdown;
    public Button saveMapPresetBtn;
    public Button saveParamPresetBtn;

    [Header("References")]
    public MapEditorCanvas mapEditorCanvas;
    public FileIOBridge    fileIOBridge;

    [Header("Scene Names")]
    public string titleSceneName = "TitleScene";

    [Header("Validation Rules UI")]
    public Toggle validationToggle;
    public GameObject rulesTextPanel;
    public Button retractRulesBtn;

    [Header("Individual Rule Texts")]
    public TextMeshProUGUI txtTerrain;
    public TextMeshProUGUI txtCommunities;
    public TextMeshProUGUI txtMotel;
    public TextMeshProUGUI txtAbandoned;
    public TextMeshProUGUI txtVehicles;

    private string[] presetNames = { "easy", "medium", "hard" };

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
        saveMapPresetBtn.onClick.AddListener(OnSaveMapPresetClicked);
        saveParamPresetBtn.onClick.AddListener(OnSaveParamPresetClicked);
        mapPresetDropdown.onValueChanged.AddListener(index => OnMapDropdownChanged(index));
        paramPresetDropdown.onValueChanged.AddListener(index => OnParamDropdownChanged(index));


        // File import callback
        if (fileIOBridge != null)
            fileIOBridge.OnFileImported += OnFileImported;

        // Server save callback
        InstructorConfigManager.Instance.OnSaveComplete += HandleSaveComplete;

        ShowTab(0);
        SetStatus("Config editor ready.");

        InstructorConfigManager.Instance.OnConfigChanged += RefreshVisuals;

        RefreshVisuals();

        ShowTab(0);
        SetStatus("Config editor ready.");

        // Rules Validation UI
        validationToggle.onValueChanged.AddListener(OnToggleRules);
        retractRulesBtn.onClick.AddListener(() => {
            validationToggle.isOn = false; 
        });
        rulesTextPanel.SetActive(validationToggle.isOn);
    }

    void OnDestroy()
    {
        if (fileIOBridge != null)
            fileIOBridge.OnFileImported -= OnFileImported;

        if (InstructorConfigManager.Instance != null)
            InstructorConfigManager.Instance.OnSaveComplete -= HandleSaveComplete;
        if (InstructorConfigManager.Instance != null)
            InstructorConfigManager.Instance.OnConfigChanged -= RefreshVisuals;
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    public void ShowTab(int tab)
    {
        mapEditorPanel .SetActive(tab == 0);
        parametersPanel.SetActive(tab == 1);
        mapEditorTabButton .interactable = tab != 0;
        parametersTabButton.interactable = tab != 1;

        if (mapPresetDropdown != null) mapPresetDropdown.gameObject.SetActive(tab == 0);
        if (saveMapPresetBtn != null) saveMapPresetBtn.gameObject.SetActive(tab == 0);

        if (paramPresetDropdown != null) paramPresetDropdown.gameObject.SetActive(tab == 1);
        if (saveParamPresetBtn != null) saveParamPresetBtn.gameObject.SetActive(tab == 1);
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
        bool isValid = RefreshRulesVisuals();
        if (isValid)
        {
            if (InstructorConfigManager.Instance.IsSaving) return;
            saveToServerButton.interactable = false;
            InstructorConfigManager.Instance.SaveToServer("latest_map_config.json");
        }
        else
        {
            validationToggle.isOn = true;
            SetStatus("<color=red>Save Blocked: Invalid Map</color>");
        }
    }

    void HandleSaveComplete(bool success, string message)
    {
        saveToServerButton.interactable = true;
        SetStatus(message);
    }

    // separate easy/med/hard 
    void OnMapDropdownChanged(int index)
    {
        string fileName = $"{presetNames[index]}_map_config.json";
        SetStatus($"InstructorConfigUI: Loading Map Config: {fileName}");
        InstructorConfigManager.Instance.LoadPresetFromServer(fileName, true);
    }

    void OnParamDropdownChanged(int index)
    {
        string fileName = $"{presetNames[index]}_param_config.json";
        SetStatus($"InstructorConfigUI: Loading Param Config: {fileName}");
        InstructorConfigManager.Instance.LoadPresetFromServer(fileName, false);
    }

    void OnSaveMapPresetClicked()
    {
        bool isValid = RefreshRulesVisuals();
        if (isValid)
        {
            string fileName = $"{presetNames[mapPresetDropdown.value]}_map_config.json";
            SetStatus($"InstructorConfigUI: Saving Map Layout to {fileName}");
            InstructorConfigManager.Instance.SaveMapOnly(fileName);
        }
        else
        {
            validationToggle.isOn = true;
            SetStatus("<color=red>Save Blocked: Invalid Map</color>");
        }
    }

    void OnSaveParamPresetClicked()
    {
        string fileName = $"{presetNames[paramPresetDropdown.value]}_param_config.json";
        SetStatus($"InstructorConfigUI: Saving Params to {fileName}");
        InstructorConfigManager.Instance.SaveParamsOnly(fileName);
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
    void RefreshVisuals()
    {
        mapEditorCanvas?.ReloadFromConfig();
        if (validationToggle.isOn)
        {
            RefreshRulesVisuals();
        }
    }
    private bool RefreshRulesVisuals()
    {
        var report = InstructorConfigManager.Instance.CurrentConfig.GetDetailedValidation();

        txtTerrain.color = report.terrainOk ? Color.green : Color.red;
        txtCommunities.color = report.communityOk ? Color.green : Color.red;
        txtMotel.color = report.motelOk ? Color.green : Color.red;
        txtAbandoned.color = report.abandonedOk ? Color.green : Color.red;
        txtVehicles.color = report.vehicleOk ? Color.green : Color.red;

        return report.IsAllValid;
    }
    void OnToggleRules(bool show)
    {
        if (show)
        {
            RefreshRulesVisuals();
            rulesTextPanel.SetActive(true);
        }
        else
        {
            rulesTextPanel.SetActive(false);
        }
    }
}
