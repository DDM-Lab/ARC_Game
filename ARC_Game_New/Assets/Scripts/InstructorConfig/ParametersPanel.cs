using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Parameters tab in the Instructor Config scene.
/// Each parameter has a Slider + TMP_InputField that stay in sync.
/// All values are clamped to min/max defined in the Inspector.
///
/// SCENE SETUP – under ParametersPanel, create one row per parameter:
///   Row (HorizontalLayoutGroup)
///     ├─ Label (TMP)         ← parameter display name
///     ├─ Slider              ← assign to the ParameterRow.slider field
///     └─ ValueInput (TMP_InputField) ← assign to ParameterRow.inputField
///
/// Then assign each row's fields in the Inspector array below.
/// paramName must exactly match a case in GetValue/SetValue switches.
/// </summary>
public class ParametersPanel : MonoBehaviour
{
    [System.Serializable]
    public class ParameterRow
    {
        [Tooltip("Must match a switch-case in GetValue/SetValue")]
        public string paramName;
        public Slider          slider;
        public TMP_InputField  inputField;
        [Space]
        public float minValue = 0f;
        public float maxValue = 100f;
        [Tooltip("True = whole numbers only (int parameter)")]
        public bool  isInt    = true;
    }

    [Header("Parameter Rows")]
    public ParameterRow[] rows;

    // Guard against feedback loops when we programmatically set UI values
    bool _syncing;

    // ─────────────────────────────────────────────────────────────────────────

    void OnEnable()
    {
        LoadFromConfig();
    }

    // ── Load config → UI ──────────────────────────────────────────────────────

    void LoadFromConfig()
    {
        ScenarioParameters p = InstructorConfigManager.Instance.CurrentConfig.parameters;

        foreach (var row in rows)
        {
            float val = GetValue(p, row.paramName);

            // Configure slider bounds
            row.slider.minValue     = row.minValue;
            row.slider.maxValue     = row.maxValue;
            row.slider.wholeNumbers = row.isInt;

            // Sync UI without triggering callbacks
            _syncing = true;
            row.slider.value    = val;
            row.inputField.text = FormatValue(val, row.isInt);
            _syncing = false;

            // Wire callbacks (re-wiring each OnEnable is safe – Unity deduplicates listeners
            // only if added with AddListener the same way; remove first to be safe)
            var capturedRow = row;
            row.slider.onValueChanged.RemoveAllListeners();
            row.slider.onValueChanged.AddListener(v => OnSliderChanged(capturedRow, v));

            row.inputField.onEndEdit.RemoveAllListeners();
            row.inputField.onEndEdit.AddListener(s => OnInputChanged(capturedRow, s));
        }
    }

    // ── Callbacks ─────────────────────────────────────────────────────────────

    void OnSliderChanged(ParameterRow row, float value)
    {
        if (_syncing) return;
        _syncing = true;
        row.inputField.text = FormatValue(value, row.isInt);
        _syncing = false;
        Commit(row.paramName, value);
    }

    void OnInputChanged(ParameterRow row, string text)
    {
        if (_syncing) return;
        if (!float.TryParse(text, out float val)) return;

        val = Mathf.Clamp(val, row.minValue, row.maxValue);
        _syncing = true;
        row.slider.value    = val;
        row.inputField.text = FormatValue(val, row.isInt);
        _syncing = false;
        Commit(row.paramName, val);
    }

    void Commit(string name, float value)
    {
        ScenarioParameters p = InstructorConfigManager.Instance.CurrentConfig.parameters;
        SetValue(p, name, value);
        InstructorConfigManager.Instance.NotifyConfigChanged();
    }

    // ── Parameter get/set ─────────────────────────────────────────────────────
    //  Extend these switches whenever ScenarioParameters gains new fields.

    static float GetValue(ScenarioParameters p, string name) => name switch
    {
        "initialBudget"          => p.initialBudget,
        "dailyBudgetAllocation"  => p.dailyBudgetAllocation,
        "foodCostPerPerson"      => p.foodCostPerPerson,
        "shelterCostPerPerson"   => p.shelterCostPerPerson,
        "workerTrainingCost"     => p.workerTrainingCost,
        "initialSatisfaction"    => p.initialSatisfaction,
        "totalPopulation"        => p.totalPopulation,
        "numberOfCommunities"    => p.numberOfCommunities,
        "initialWorkerCount"     => p.initialWorkerCount,
        "gameDurationDays"       => p.gameDurationDays,
        "dayDurationSeconds"     => p.dayDurationSeconds,
        _                        => 0f
    };

    static void SetValue(ScenarioParameters p, string name, float v)
    {
        switch (name)
        {
            case "initialBudget":         p.initialBudget         = (int)v; break;
            case "dailyBudgetAllocation": p.dailyBudgetAllocation = v;      break;
            case "foodCostPerPerson":     p.foodCostPerPerson     = v;      break;
            case "shelterCostPerPerson":  p.shelterCostPerPerson  = v;      break;
            case "workerTrainingCost":    p.workerTrainingCost    = v;      break;
            case "initialSatisfaction":   p.initialSatisfaction   = (int)v; break;
            case "totalPopulation":       p.totalPopulation       = (int)v; break;
            case "numberOfCommunities":   p.numberOfCommunities   = (int)v; break;
            case "initialWorkerCount":    p.initialWorkerCount    = (int)v; break;
            case "gameDurationDays":      p.gameDurationDays      = (int)v; break;
            case "dayDurationSeconds":    p.dayDurationSeconds    = v;      break;
        }
    }

    static string FormatValue(float val, bool isInt) =>
        isInt ? ((int)val).ToString() : val.ToString("F1");
}
