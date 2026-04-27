using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

// Agent Choice UI Component
public class AgentChoiceUI : MonoBehaviour
{
    [Header("UI Components")]
    public Button choiceButton;
    public TextMeshProUGUI choiceText;
    public TextMeshProUGUI descriptionText; // Optional: displays agentReasoning
    public Image selectedIndicator;
    public Button previewButton;

    [Header("Colors")]
    public Color normalColor = Color.white;
    public Color selectedColor = Color.green;

    [Header("Validation Colors")]
    public Color invalidColor = Color.gray;
    public TextMeshProUGUI validationText;

    private AgentChoice choice;
    private TaskDetailUI parentUI;
    private bool isSelected = false;
    private bool isValid = true;
    private string validationMessage = "";

    public void Initialize(AgentChoice agentChoice, TaskDetailUI parent, System.Action<AgentChoice> onPreviewRoute = null)
    {
        if (agentChoice == null)
        {
            Debug.LogError("[AgentChoiceUI] Initialize called with null agentChoice");
            return;
        }

        choice = agentChoice;
        parentUI = parent;

        if (choiceText != null)
            choiceText.text = agentChoice.choiceText ?? "";

        // Display agent reasoning/description if available
        if (descriptionText != null)
        {
            if (!string.IsNullOrEmpty(agentChoice.agentReasoning))
            {
                descriptionText.text = agentChoice.agentReasoning;
                descriptionText.gameObject.SetActive(true);
            }
            else
            {
                descriptionText.gameObject.SetActive(false);
            }
        }

        if (choiceButton != null)
        {
            choiceButton.onClick.RemoveAllListeners();
            choiceButton.onClick.AddListener(OnChoiceClicked);
        }

        if (previewButton != null)
        {
            previewButton.onClick.RemoveAllListeners();
            bool hasDelivery = agentChoice.triggersDelivery || agentChoice.immediateDelivery;
            bool hasHandler  = parent != null || onPreviewRoute != null;
            bool showPreview = hasDelivery && hasHandler;
            previewButton.gameObject.SetActive(showPreview);
            if (showPreview)
            {
                if (onPreviewRoute != null)
                    previewButton.onClick.AddListener(() => onPreviewRoute(choice));
                else
                    previewButton.onClick.AddListener(() => parent.PreviewChoiceRoute(choice));
            }
        }

        SetSelected(false);
    }

    void OnChoiceClicked()
    {
        SetSelected(true);
        parentUI?.OnChoiceSelected(choice);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;

        if (selectedIndicator != null)
            selectedIndicator.gameObject.SetActive(selected);

        if (choiceButton != null)
        {
            Image buttonImage = choiceButton.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = selected ? selectedColor : normalColor;
        }
    }

    public AgentChoice GetChoice() => choice;

    public void SetPreviewVisible(bool visible)
    {
        if (previewButton != null) previewButton.gameObject.SetActive(visible);
    }

    public void SetValidationState(bool valid, string message)
    {
        isValid = valid;
        validationMessage = message;

        // Update button appearance
        if (choiceButton != null)
        {
            //choiceButton.interactable = valid;

            Image buttonImage = choiceButton.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.color = valid ? normalColor : invalidColor;
            }
        }

        // Show validation message
        if (validationText != null)
        {
            validationText.text = valid ? "" : message;
            validationText.color = Color.red;
        }
    }
    
    public void InitializeAsHistorical(AgentChoice choice, bool wasSelected = false)
    {
        Initialize(choice, null); // parent=null disables preview button automatically

        if (choiceButton != null)
        {
            choiceButton.interactable = false;
            Image buttonImage = choiceButton.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = wasSelected ? selectedColor : Color.gray;
        }

        if (selectedIndicator != null)
            selectedIndicator.gameObject.SetActive(wasSelected);
    }
}