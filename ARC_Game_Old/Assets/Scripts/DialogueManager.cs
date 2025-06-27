using System.Collections.Generic;
using UnityEngine;
using System;

public class DialogueManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DialoguePanel dialoguePanelPrefab;
    [SerializeField] private Transform canvasTransform;

    private static DialogueManager _instance;
    private DialoguePanel _activePanel;
    private Queue<DialogueData> _pendingDialogues = new Queue<DialogueData>();
    private bool _isDisplayingDialogue = false;

    public static DialogueManager Instance
    {
        get
        {
            if (_instance == null)
                Debug.LogError("DialogueManager instance is null!");
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Show a dialogue popup with the specified parameters
    /// </summary>
    public void ShowDialogue(string agentName, Sprite agentImage, string dialogueText, Action onComplete = null)
    {
        DialogueData data = new DialogueData
        {
            AgentName = agentName,
            AgentImage = agentImage,
            DialogueText = dialogueText,
            OnComplete = onComplete
        };

        _pendingDialogues.Enqueue(data);

        if (!_isDisplayingDialogue)
            DisplayNextDialogue();
    }

    /// <summary>
    /// Show a dialogue popup with typing effect
    /// </summary>
    public void ShowDialogueWithTypingEffect(string agentName, Sprite agentImage, string dialogueText, 
                                            float typingSpeed = 0.05f, Action onComplete = null)
    {
        DialogueData data = new DialogueData
        {
            AgentName = agentName,
            AgentImage = agentImage,
            DialogueText = dialogueText,
            UseTypingEffect = true,
            TypingSpeed = typingSpeed,
            OnComplete = onComplete
        };

        _pendingDialogues.Enqueue(data);

        if (!_isDisplayingDialogue)
            DisplayNextDialogue();
    }

    /// <summary>
    /// Display the next dialogue in the queue
    /// </summary>
    private void DisplayNextDialogue()
    {
        if (_pendingDialogues.Count == 0)
        {
            _isDisplayingDialogue = false;
            return;
        }

        _isDisplayingDialogue = true;
        DialogueData data = _pendingDialogues.Dequeue();

        if (_activePanel == null)
        {
            _activePanel = Instantiate(dialoguePanelPrefab, canvasTransform);
        }
        else
        {
            _activePanel.gameObject.SetActive(true);
        }

        _activePanel.OnDialogueClosed += HandleDialogueClosed;
        
        if (data.UseTypingEffect)
            _activePanel.DisplayWithTypingEffect(data.AgentName, data.AgentImage, data.DialogueText, data.TypingSpeed);
        else
            _activePanel.Display(data.AgentName, data.AgentImage, data.DialogueText);
            
        // Store the callback to be called when this dialogue is closed
        _activePanel.OnCompleteCallback = data.OnComplete;
    }

    /// <summary>
    /// Handle the dialogue being closed
    /// </summary>
    private void HandleDialogueClosed()
    {
        if (_activePanel != null)
        {
            _activePanel.OnDialogueClosed -= HandleDialogueClosed;
            
            // Execute the completion callback if one exists
            _activePanel.OnCompleteCallback?.Invoke();
            _activePanel.OnCompleteCallback = null;
        }
        
        // Display the next dialogue if there are any pending
        DisplayNextDialogue();
    }
}

/// <summary>
/// Structure for dialogue information
/// </summary>
[System.Serializable]
public class DialogueData
{
    public string AgentName;
    public Sprite AgentImage;
    public string DialogueText;
    public bool UseTypingEffect = false;
    public float TypingSpeed = 0.05f;
    public Action OnComplete;
}
