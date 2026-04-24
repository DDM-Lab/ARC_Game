using System.Collections.Generic;
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
        public enum RowType { Slider, Dropdown} // for enums like weather type etc.
        [Tooltip("Must match a switch-case in GetValue/SetValue")]
        public string paramName;
        public RowType controlType; 
        public Slider          slider;
        public TMP_Dropdown dropdown;
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
            // Wire callbacks (re-wiring each OnEnable is safe – Unity deduplicates listeners
            // only if added with AddListener the same way; remove first to be safe)
            var capturedRow = row;

            // Sync UI without triggering callbacks
            _syncing = true;

            if (row.controlType == ParameterRow.RowType.Slider)
            {
                // Configure slider bounds
                row.slider.minValue = row.minValue;
                row.slider.maxValue = row.maxValue;
                row.slider.wholeNumbers = row.isInt;
                row.slider.value = val;

                row.slider.onValueChanged.RemoveAllListeners();
                row.slider.onValueChanged.AddListener(v => OnSliderChanged(capturedRow, v));

                // Show slider & hide dropdown
                row.slider.gameObject.SetActive(true);
                if (row.dropdown != null) row.dropdown.gameObject.SetActive(false);
            }
            else // Dropdown
            {
                List<string> enumNames = new List<string>();

                if (row.paramName == "initialWeather")
                {
                    enumNames.AddRange(System.Enum.GetNames(typeof(WeatherType)));
                }
                else if (row.paramName == "shelterFloodComparison")
                {
                    enumNames.AddRange(System.Enum.GetNames(typeof(FloodedFacilityTrigger.ComparisonType)));
                }
                row.dropdown.ClearOptions();
                row.dropdown.AddOptions(enumNames);
                row.dropdown.value = (int)val;
                row.dropdown.RefreshShownValue();
                row.dropdown.onValueChanged.RemoveAllListeners();
                row.dropdown.onValueChanged.AddListener(v => OnSliderChanged(capturedRow, (float)v));

                row.dropdown.gameObject.SetActive(true);
                if (row.slider != null) row.slider.gameObject.SetActive(false);
            }
            row.inputField.text = FormatValue(val, row.isInt);
            row.inputField.onEndEdit.RemoveAllListeners();
            row.inputField.onEndEdit.AddListener(s => OnInputChanged(capturedRow, s));

            _syncing = false;
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
        if (row.controlType == ParameterRow.RowType.Slider)
        {
            row.slider.value = val;
        }
        else
        {
            row.dropdown.value = (int)val;
        }
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

    //static float GetValue(ScenarioParameters p, string name) => name switch
    //{
    //    "initialBudget"          => p.initialBudget, //have 
    //    "dailyBudgetAllocation"  => p.dailyBudgetAllocation, //have
    //    "foodCostPerPerson"      => p.foodCostPerPerson,
    //    "shelterCostPerPerson"   => p.shelterCostPerPerson,
    //    "workerTrainingCost"     => p.workerTrainingCost,
    //    "initialSatisfaction"    => p.initialSatisfaction, // have
    //    "totalPopulation"        => p.totalPopulation,
    //    "numberOfCommunities"    => p.numberOfCommunities, // have
    //    "initialWorkerCount"     => p.initialWorkerCount,
    //    "gameDurationDays"       => p.gameDurationDays, // have
    //    "dayDurationSeconds"     => p.dayDurationSeconds,
    //    _                        => 0f,

    //};
    static float GetValue(ScenarioParameters p, string name) => name switch
    {
        // Economy
        "initialBudget" => p.initialBudget, // h
        "dailyBudgetAllocation" => p.dailyBudgetAllocation, // h
        "foodCostPerPerson" => p.foodCostPerPerson,
        "shelterCostPerPerson" => p.shelterCostPerPerson,
        "workerTrainingCost" => p.workerTrainingCost,

        // Population & Satisfaction
        "initialSatisfaction" => p.initialSatisfaction, // h
        "totalPopulation" => p.totalPopulation,
        "numberOfCommunities" => p.numberOfCommunities, // h
        "residentsPerCommunity" => p.residentsPerCommunity, // h

        // Workers
        "initialWorkerCount" => p.initialWorkerCount,
        "initialTrainedVolunteers" => p.initialTrainedVolunteerCount, // h
        "initialUntrainedVolunteers" => p.initialUntrainedVolunteerCount, // h
        "requiredWorkersPerLoc" => p.requiredWorkerUnitsPerLoc, // h

        // Timing
        "gameDurationDays" => p.gameDurationDays, // h
        "dayDurationSeconds" => p.dayDurationSeconds,
        "roundsPerDay" => p.roundsPerDay, // h

        // Weather (Dropdown)
        "initialWeather" => (int)p.initialWeather, // h

        // Buildings
        "kitchenCapacity" => p.kitchenCapacity, // h
        "shelterCapacity" => p.shelterCapacity, // h
        "caseworkCapacity" => p.caseworkCapacity, // h
        "initialERVCount" => p.initialERVCount, // h

        // Tasks
        "foodDemandProb" => p.foodDemandProbability, // h
        "externalRelationFreq" => p.externalRelationFrequency, // h
        "emergencyTaskFreq" => p.emergencyTaskFrequency, // h

        // Flooding
        "shelterFloodThreshold" => p.shelterFloodThreshold, // h
        "shelterFloodRadius" => p.shelterFloodRadius, // h
        "shelterFloodComparison" => (float)p.shelterFloodComparisonType, // h

        _ => 0f,
    };

    //static void SetValue(ScenarioParameters p, string name, float v)
    //{
    //    switch (name)
    //    {
    //        case "initialBudget":         p.initialBudget         = (int)v; break;
    //        case "dailyBudgetAllocation": p.dailyBudgetAllocation = v;      break;
    //        case "foodCostPerPerson":     p.foodCostPerPerson     = v;      break;
    //        case "shelterCostPerPerson":  p.shelterCostPerPerson  = v;      break;
    //        case "workerTrainingCost":    p.workerTrainingCost    = v;      break;
    //        case "initialSatisfaction":   p.initialSatisfaction   = (int)v; break;
    //        case "totalPopulation":       p.totalPopulation       = (int)v; break;
    //        case "numberOfCommunities":   p.numberOfCommunities   = (int)v; break;
    //        case "initialWorkerCount":    p.initialWorkerCount    = (int)v; break;
    //        case "gameDurationDays":      p.gameDurationDays      = (int)v; break;
    //        case "dayDurationSeconds":    p.dayDurationSeconds    = v;      break;
    //    }
    //}
    static void SetValue(ScenarioParameters p, string name, float v)
    {
        switch (name)
        {
            // Economy
            case "initialBudget": p.initialBudget = (int)v; break;
            case "dailyBudgetAllocation": p.dailyBudgetAllocation = v; break;
            case "foodCostPerPerson": p.foodCostPerPerson = v; break;
            case "shelterCostPerPerson": p.shelterCostPerPerson = v; break;
            case "workerTrainingCost": p.workerTrainingCost = v; break;

            // Population
            case "initialSatisfaction": p.initialSatisfaction = (int)v; break;
            case "totalPopulation": p.totalPopulation = (int)v; break;
            case "numberOfCommunities": p.numberOfCommunities = (int)v; break;
            case "residentsPerCommunity": p.residentsPerCommunity = (int)v; break;

            // Workers
            case "initialWorkerCount": p.initialWorkerCount = (int)v; break;
            case "initialTrainedVolunteers": p.initialTrainedVolunteerCount = (int)v; break;
            case "initialUntrainedVolunteers": p.initialUntrainedVolunteerCount = (int)v; break;
            case "requiredWorkersPerLoc": p.requiredWorkerUnitsPerLoc = (int)v; break;

            // Timing
            case "gameDurationDays": p.gameDurationDays = (int)v; break;
            case "dayDurationSeconds": p.dayDurationSeconds = v; break;
            case "roundsPerDay": p.roundsPerDay = (int)v; break;

            // Weather
            case "initialWeather": p.initialWeather = (WeatherType)(int)v; break;

            // Buildings
            case "kitchenCapacity": p.kitchenCapacity = (int)v; break;
            case "shelterCapacity": p.shelterCapacity = (int)v; break;
            case "caseworkCapacity": p.caseworkCapacity = (int)v; break;
            case "initialERVCount": p.initialERVCount = (int)v; break;

            // Tasks
            case "foodDemandProb": p.foodDemandProbability = v; break;
            case "externalRelationFreq": p.externalRelationFrequency = (int)v; break;
            case "emergencyTaskFreq": p.emergencyTaskFrequency = (int)v; break;

            // Flooding
            case "shelterFloodThreshold": p.shelterFloodThreshold = (int)v; break;
            case "shelterFloodRadius": p.shelterFloodRadius = (int)v; break;
            case "shelterFloodComparison": p.shelterFloodComparisonType = (FloodedFacilityTrigger.ComparisonType)(int)v; break;
        }
    }

    static string FormatValue(float val, bool isInt) =>
        isInt ? ((int)val).ToString() : val.ToString("F1");
}
