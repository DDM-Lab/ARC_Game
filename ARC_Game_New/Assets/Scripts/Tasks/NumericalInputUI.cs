using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NumericalInputUI : MonoBehaviour
{
    [Header("UI Components")]
    public TextMeshProUGUI labelText;
    public Button decreaseButton;
    public Button increaseButton;
    public TextMeshProUGUI valueText;

    private AgentNumericalInput numericalInput;
    private TaskDetailUI parentUI;

    public void Initialize(AgentNumericalInput input, TaskDetailUI parent)
    {
        numericalInput = input;
        parentUI = parent;

        if (labelText != null)
            labelText.text = input.inputLabel;

        decreaseButton?.onClick.AddListener(DecreaseValue);
        increaseButton?.onClick.AddListener(IncreaseValue);

        UpdateDisplay();
    }

    void DecreaseValue()
    {
        numericalInput.currentValue = Mathf.Max(numericalInput.minValue,
            numericalInput.currentValue - numericalInput.stepSize);
        UpdateDisplay();
    }

    void IncreaseValue()
    {
        numericalInput.currentValue = Mathf.Min(numericalInput.maxValue,
            numericalInput.currentValue + numericalInput.stepSize);
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (valueText != null)
            valueText.text = numericalInput.currentValue.ToString();
    }

    public void InitializeAsHistorical(AgentNumericalInput input)
    {
        Initialize(input, null); // null taskDetailUI for historical mode
        
        // Disable all inputs in historical mode
        if (decreaseButton != null) decreaseButton.interactable = false;
        if (increaseButton != null) increaseButton.interactable = false;
        
        // Show final value
        if (valueText != null)
            valueText.color = Color.gray;
    }
}