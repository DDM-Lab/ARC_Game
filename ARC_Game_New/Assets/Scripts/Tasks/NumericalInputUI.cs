using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NumericalInputUI : MonoBehaviour
{
    [Header("UI Components")]
    public TextMeshProUGUI labelText;
    public Button decreaseButton;
    public Button increaseButton;
    public TMP_InputField inputField; // NEW: For direct input
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
        
        // NEW: Setup input field if available
        if (inputField != null)
        {
            inputField.contentType = TMP_InputField.ContentType.IntegerNumber;
            inputField.onEndEdit.AddListener(OnInputFieldChanged);
        }

        UpdateDisplay();
    }

    void DecreaseValue()
    {
        numericalInput.currentValue = Mathf.Max(numericalInput.minValue,
            numericalInput.currentValue - numericalInput.stepSize);
        UpdateDisplay();
        
        // Prevent scroll reset
        if (parentUI != null)
            parentUI.PreventScrollReset();
    }

    void IncreaseValue()
    {
        numericalInput.currentValue = Mathf.Min(numericalInput.maxValue,
            numericalInput.currentValue + numericalInput.stepSize);
        UpdateDisplay();
        
        // Prevent scroll reset
        if (parentUI != null)
            parentUI.PreventScrollReset();
    }
    
    void OnInputFieldChanged(string value)
    {
        if (int.TryParse(value, out int newValue))
        {
            numericalInput.currentValue = Mathf.Clamp(newValue, numericalInput.minValue, numericalInput.maxValue);
        }
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        string displayValue = numericalInput.currentValue.ToString();
        
        if (valueText != null)
            valueText.text = displayValue;
            
        if (inputField != null)
            inputField.text = displayValue;
    }

    public void InitializeAsHistorical(AgentNumericalInput input)
    {
        Initialize(input, null);
        
        // Disable all inputs in historical mode
        if (decreaseButton != null) decreaseButton.interactable = false;
        if (increaseButton != null) increaseButton.interactable = false;
        if (inputField != null) inputField.interactable = false;
        
        // Show final value
        if (valueText != null)
            valueText.color = Color.gray;
    }
}