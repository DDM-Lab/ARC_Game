using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NumericalInputUI : MonoBehaviour
{
    [Header("UI Components")]
    public Image choiceIcon;                    // Icon for the input type
    public TextMeshProUGUI choiceLabel;        // Main label
    public TextMeshProUGUI choiceDescription;  // Description text
    public Button decreaseButton;
    public Button increaseButton;
    public TMP_InputField inputField;
    public TextMeshProUGUI valueText;
    
    [Header("Icon Configuration")]
    public Sprite budgetIcon;
    public Sprite clientsIcon;
    public Sprite untrainedWorkersIcon;
    public Sprite trainedWorkersIcon;
    public Sprite foodPacksIcon;

    [Header("Historical Mode")]
    public Color historyColor = Color.gray; //dark gray for historical display

    private AgentNumericalInput numericalInput;
    private TaskDetailUI parentUI;

    public void Initialize(AgentNumericalInput input, TaskDetailUI parent)
    {
        numericalInput = input;
        parentUI = parent;

        // Set up visual elements based on input type
        ConfigureVisuals();

        // Setup buttons
        decreaseButton?.onClick.AddListener(DecreaseValue);
        increaseButton?.onClick.AddListener(IncreaseValue);
        
        // Setup input field if available
        if (inputField != null)
        {
            inputField.contentType = TMP_InputField.ContentType.IntegerNumber;
            inputField.onEndEdit.AddListener(OnInputFieldChanged);
        }

        UpdateDisplay();
    }
    
    void ConfigureVisuals()
    {
        // Set icon based on type
        if (choiceIcon != null)
        {
            Sprite iconToUse = GetIconForType(numericalInput.inputType);
            if (iconToUse != null)
            {
                choiceIcon.sprite = iconToUse;
                choiceIcon.enabled = true;
            }
            else
            {
                choiceIcon.enabled = false;
            }
            
        }
        
        // Set label
        if (choiceLabel != null)
        {
            choiceLabel.text = GetLabelForType(numericalInput.inputType);
        }
        
        // Set description
        if (choiceDescription != null)
        {
            choiceDescription.text = GetDescriptionForType(numericalInput.inputType);
        }
    
    }
    
    Sprite GetIconForType(NumericalInputType type)
    {
        switch (type)
        {
            case NumericalInputType.Budget:
                return budgetIcon;
            case NumericalInputType.Clients:
                return clientsIcon;
            case NumericalInputType.UntrainedWorkers:
                return untrainedWorkersIcon;
            case NumericalInputType.TrainedWorkers:
                return trainedWorkersIcon;
            case NumericalInputType.FoodPacks:
                return foodPacksIcon;
            default:
                return null;
        }
    }
    
    string GetLabelForType(NumericalInputType type)
    {
        // Use custom label if provided, otherwise use default
        if (!string.IsNullOrEmpty(numericalInput.inputLabel))
            return numericalInput.inputLabel;
            
        switch (type)
        {
            case NumericalInputType.Budget:
                return "Budget Allocation";
            case NumericalInputType.Clients:
                return "Number of Clients";
            case NumericalInputType.UntrainedWorkers:
                return "Untrained Workers";
            case NumericalInputType.TrainedWorkers:
                return "Trained Workers";
            case NumericalInputType.FoodPacks:
                return "Food Packs";
            default:
                return "Value Input";
        }
    }
    
    string GetDescriptionForType(NumericalInputType type)
    {
        // Use custom description if provided
        if (!string.IsNullOrEmpty(numericalInput.customDescription))
            return numericalInput.customDescription;
            
        switch (type)
        {
            case NumericalInputType.Budget:
                return $"Allocate budget between ${numericalInput.minValue:N0} and ${numericalInput.maxValue:N0}";
            case NumericalInputType.Clients:
                return $"Select number of clients to process ({numericalInput.minValue} - {numericalInput.maxValue} people)";
            case NumericalInputType.UntrainedWorkers:
                return $"Assign untrained workers to this task ({numericalInput.minValue} - {numericalInput.maxValue} workers)";
            case NumericalInputType.TrainedWorkers:
                return $"Assign trained specialists ({numericalInput.minValue} - {numericalInput.maxValue} workers)";
            case NumericalInputType.FoodPacks:
                return $"Distribute food packages ({numericalInput.minValue} - {numericalInput.maxValue} packs)";
            default:
                return $"Set value between {numericalInput.minValue} and {numericalInput.maxValue}";
        }
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
            // Round to nearest step size
            int steps = Mathf.RoundToInt((float)(newValue - numericalInput.minValue) / numericalInput.stepSize);
            newValue = numericalInput.minValue + (steps * numericalInput.stepSize);
            
            numericalInput.currentValue = Mathf.Clamp(newValue, numericalInput.minValue, numericalInput.maxValue);
        }
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        string displayValue = GetFormattedValue();
        
        if (valueText != null)
            valueText.text = displayValue;
            
        if (inputField != null)
            inputField.text = numericalInput.currentValue.ToString();
            
        // Update button states
        if (decreaseButton != null)
            decreaseButton.interactable = numericalInput.currentValue > numericalInput.minValue;
            
        if (increaseButton != null)
            increaseButton.interactable = numericalInput.currentValue < numericalInput.maxValue;
    }
    
    string GetFormattedValue()
    {
        return numericalInput.currentValue.ToString();
    }

    public void InitializeAsHistorical(AgentNumericalInput input)
    {
        Initialize(input, null);
        
        // Disable all inputs in historical mode
        if (decreaseButton != null) decreaseButton.interactable = false;
        if (increaseButton != null) increaseButton.interactable = false;
        if (inputField != null) inputField.interactable = false;
        
        // Show final value in historical color
        if (valueText != null)
            valueText.color = historyColor;
            
        // Gray out the icon too
        if (choiceIcon != null)
            choiceIcon.color = historyColor;
    }
    
    // Get the current value for external use
    public int GetCurrentValue()
    {
        return numericalInput.currentValue;
    }
    
    // Get the input type for external use
    public NumericalInputType GetInputType()
    {
        return numericalInput.inputType;
    }
}