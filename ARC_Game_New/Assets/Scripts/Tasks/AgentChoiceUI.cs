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
    public Image selectedIndicator;

    [Header("Colors")]
    public Color normalColor = Color.white;
    public Color selectedColor = Color.green;

    private AgentChoice choice;
    private TaskDetailUI parentUI;
    private bool isSelected = false;

    public void Initialize(AgentChoice agentChoice, TaskDetailUI parent)
    {
        choice = agentChoice;
        parentUI = parent;

        if (choiceText != null)
            choiceText.text = agentChoice.choiceText;

        if (choiceButton != null)
        {
            choiceButton.onClick.RemoveAllListeners();
            choiceButton.onClick.AddListener(OnChoiceClicked);
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

    public AgentChoice GetChoice()
    {
        return choice;
    }
}