using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class TaskDetailUI : MonoBehaviour
{
    [Header("Main Panel")]
    public GameObject taskDetailPanel;
    public Button closeButton;
    
    [Header("Left Panel - Task Description")]
    public Image taskImage;
    public TextMeshProUGUI taskTitleText;
    public TextMeshProUGUI taskTypeText;
    public TextMeshProUGUI facilityText;
    public TextMeshProUGUI descriptionText;
    public Transform impactContainer;
    public GameObject impactItemPrefab;
    
    [Header("Right Panel - Agent Conversation")]
    public ScrollRect conversationScrollView;
    public Transform conversationContent;
    public GameObject agentMessagePrefab;
    public GameObject playerChoicePrefab;
    public GameObject numericalInputPrefab;
    
    [Header("Action Buttons")]
    public Button laterButton;
    public Button confirmButton;
    
    [Header("Player Input")]
    public TMP_InputField playerInputField;
    public Button sendButton;
    
    [Header("Typing Effect")]
    public float typingSpeed = 0.05f;
    public AudioClip typingSound;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    private GameTask currentTask;
    private List<GameObject> currentImpactItems = new List<GameObject>();
    private List<GameObject> currentConversationItems = new List<GameObject>();
    private AgentChoice selectedChoice;
    private Dictionary<int, AgentNumericalInput> numericalInputs = new Dictionary<int, AgentNumericalInput>();
    private bool isTyping = false;
    
    void Start()
    {
        SetupUI();
        
        // Hide panel initially
        if (taskDetailPanel != null)
            taskDetailPanel.SetActive(false);
    }
    
    void SetupUI()
    {
        // Setup main buttons
        if (closeButton != null)
            closeButton.onClick.AddListener(CloseTaskDetail);
        
        if (laterButton != null)
            laterButton.onClick.AddListener(OnLaterButtonClicked);
        
        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmButtonClicked);
        
        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendPlayerMessage);
        
        // Setup input field
        if (playerInputField != null)
        {
            playerInputField.onSubmit.AddListener(OnPlayerInputSubmit);
        }
    }
    
    public void ShowTaskDetail(GameTask task)
    {
        currentTask = task;
        
        if (taskDetailPanel != null)
        {
            taskDetailPanel.SetActive(true);
            
            UpdateTaskDescription();
            StartAgentConversation();
            UpdateActionButtons();
            
            if (showDebugInfo)
                Debug.Log($"Showing task detail for: {task.taskTitle}");
        }
    }
    
    public void CloseTaskDetail()
    {
        if (taskDetailPanel != null)
        {
            taskDetailPanel.SetActive(false);
            ClearDisplay();
            
            if (showDebugInfo)
                Debug.Log("Task detail closed");
        }
    }
    
    void UpdateTaskDescription()
    {
        if (currentTask == null) return;
        
        // Update task info
        if (taskImage != null)
            taskImage.sprite = currentTask.taskImage;
        
        if (taskTitleText != null)
            taskTitleText.text = currentTask.taskTitle;
        
        if (taskTypeText != null)
            taskTypeText.text = currentTask.taskType.ToString();
        
        if (facilityText != null)
            facilityText.text = currentTask.affectedFacility;
        
        if (descriptionText != null)
            descriptionText.text = currentTask.description;
        
        // Update impacts
        UpdateImpactDisplay();
    }
    
    void UpdateImpactDisplay()
    {
        if (impactContainer == null || impactItemPrefab == null) return;
        
        // Clear existing impact items
        ClearImpactItems();
        
        // Create impact items
        foreach (TaskImpact impact in currentTask.impacts)
        {
            CreateImpactItem(impact);
        }
    }
    
    void CreateImpactItem(TaskImpact impact)
    {
        GameObject impactItem = Instantiate(impactItemPrefab, impactContainer);
        ImpactItemUI impactUI = impactItem.GetComponent<ImpactItemUI>();
        
        if (impactUI != null)
        {
            impactUI.Initialize(impact);
        }
        
        currentImpactItems.Add(impactItem);
    }
    
    void ClearImpactItems()
    {
        foreach (GameObject item in currentImpactItems)
        {
            if (item != null)
                Destroy(item);
        }
        currentImpactItems.Clear();
    }
    
    void StartAgentConversation()
    {
        if (currentTask == null) return;
        
        // Clear existing conversation
        ClearConversation();
        
        // Start conversation coroutine
        StartCoroutine(PlayAgentConversation());
    }
    
    IEnumerator PlayAgentConversation()
    {
        // Display agent messages with typing effect
        foreach (AgentMessage message in currentTask.agentMessages)
        {
            yield return StartCoroutine(DisplayAgentMessage(message));
            yield return new WaitForSeconds(0.5f); // Brief pause between messages
        }
        
        // Display choices if available
        if (currentTask.agentChoices.Count > 0)
        {
            DisplayAgentChoices();
        }
        
        // Auto-scroll to bottom
        ScrollToBottom();
    }
    
    IEnumerator DisplayAgentMessage(AgentMessage message)
    {
        GameObject messageItem = Instantiate(agentMessagePrefab, conversationContent);
        AgentMessageUI messageUI = messageItem.GetComponent<AgentMessageUI>();
        
        if (messageUI != null)
        {
            messageUI.Initialize(message);
            
            if (message.useTypingEffect)
            {
                yield return StartCoroutine(messageUI.PlayTypingEffect(typingSpeed));
            }
            else
            {
                messageUI.ShowFullMessage();
            }
        }
        
        currentConversationItems.Add(messageItem);
        
        // Auto-scroll as messages appear
        Canvas.ForceUpdateCanvases();
        ScrollToBottom();
    }
    
    void DisplayAgentChoices()
    {
        foreach (AgentChoice choice in currentTask.agentChoices)
        {
            GameObject choiceItem = Instantiate(playerChoicePrefab, conversationContent);
            PlayerChoiceUI choiceUI = choiceItem.GetComponent<PlayerChoiceUI>();
            
            if (choiceUI != null)
            {
                choiceUI.Initialize(choice, this);
            }
            
            currentConversationItems.Add(choiceItem);
        }
        
        ScrollToBottom();
    }
    
    void ClearConversation()
    {
        foreach (GameObject item in currentConversationItems)
        {
            if (item != null)
                Destroy(item);
        }
        currentConversationItems.Clear();
        selectedChoice = null;
    }
    
    void ClearDisplay()
    {
        ClearImpactItems();
        ClearConversation();
        currentTask = null;
        selectedChoice = null;
        numericalInputs.Clear();
    }
    
    void ScrollToBottom()
    {
        if (conversationScrollView != null)
        {
            Canvas.ForceUpdateCanvases();
            conversationScrollView.verticalNormalizedPosition = 0f;
        }
    }
    
    public void OnChoiceSelected(AgentChoice choice)
    {
        // Deselect other choices
        foreach (GameObject item in currentConversationItems)
        {
            PlayerChoiceUI choiceUI = item.GetComponent<PlayerChoiceUI>();
            if (choiceUI != null && choiceUI.GetChoice() != choice)
            {
                choiceUI.SetSelected(false);
            }
        }
        
        selectedChoice = choice;
        UpdateActionButtons();
        
        if (showDebugInfo)
            Debug.Log($"Selected choice: {choice.choiceText}");
    }
    
    void UpdateActionButtons()
    {
        if (currentTask == null) return;
        
        // Later button availability
        if (laterButton != null)
        {
            laterButton.interactable = currentTask.taskType == TaskType.Advisory;
        }
        
        // Confirm button availability
        if (confirmButton != null)
        {
            bool canConfirm = !currentTask.isExpired && 
                             (selectedChoice != null || currentTask.agentChoices.Count == 0);
            confirmButton.interactable = canConfirm;
        }
    }
    
    void OnLaterButtonClicked()
    {
        if (currentTask != null && currentTask.taskType == TaskType.Advisory)
        {
            TaskSystem.Instance?.IgnoreTask(currentTask);
            CloseTaskDetail();
            
            if (showDebugInfo)
                Debug.Log($"Task postponed: {currentTask.taskTitle}");
        }
    }
    
    void OnConfirmButtonClicked()
    {
        if (currentTask == null) return;
        
        if (currentTask.isExpired)
        {
            if (showDebugInfo)
                Debug.LogWarning("Cannot confirm expired task");
            return;
        }
        
        // Apply selected choice impacts
        if (selectedChoice != null)
        {
            ApplyChoiceImpacts(selectedChoice);
        }
        
        // Complete the task
        TaskSystem.Instance?.CompleteTask(currentTask);
        CloseTaskDetail();
        
        if (showDebugInfo)
            Debug.Log($"Task confirmed and completed: {currentTask.taskTitle}");
    }
    
    void ApplyChoiceImpacts(AgentChoice choice)
    {
        foreach (TaskImpact impact in choice.choiceImpacts)
        {
            switch (impact.impactType)
            {
                case ImpactType.Satisfaction:
                    if (SatisfactionAndBudget.Instance != null)
                    {
                        if (impact.value > 0)
                            SatisfactionAndBudget.Instance.AddSatisfaction(impact.value);
                        else
                            SatisfactionAndBudget.Instance.RemoveSatisfaction(-impact.value);
                    }
                    break;
                    
                case ImpactType.Budget:
                    if (SatisfactionAndBudget.Instance != null)
                    {
                        if (impact.value > 0)
                            SatisfactionAndBudget.Instance.AddBudget(impact.value);
                        else
                            SatisfactionAndBudget.Instance.RemoveBudget(-impact.value);
                    }
                    break;
                    
                // Add other impact types as needed
            }
        }
        
        if (showDebugInfo)
            Debug.Log($"Applied impacts for choice: {choice.choiceText}");
    }
    
    void OnSendPlayerMessage()
    {
        if (playerInputField != null && !string.IsNullOrEmpty(playerInputField.text))
        {
            string message = playerInputField.text;
            playerInputField.text = "";
            
            // For now, just log player messages (could be extended for dynamic conversations)
            if (showDebugInfo)
                Debug.Log($"Player message: {message}");
        }
    }
    
    void OnPlayerInputSubmit(string message)
    {
        if (!string.IsNullOrEmpty(message))
        {
            OnSendPlayerMessage();
        }
    }
    
    void Update()
    {
        // Update task status and buttons in real-time
        if (currentTask != null && taskDetailPanel.activeInHierarchy)
        {
            UpdateActionButtons();
        }
    }
}

// Impact Item UI Component
public class ImpactItemUI : MonoBehaviour
{
    [Header("UI Components")]
    public Image iconImage;
    public TextMeshProUGUI labelText;
    public TextMeshProUGUI valueText;
    
    private TaskImpact impact;
    
    public void Initialize(TaskImpact taskImpact)
    {
        impact = taskImpact;
        UpdateDisplay();
    }
    
    void UpdateDisplay()
    {
        if (impact == null) return;
        
        // Set icon (could use sprite instead of text)
        if (iconImage != null)
        {
            // For now, we'll set the icon as text - you can replace with sprite lookup
            string iconText = TaskSystem.GetImpactIcon(impact.impactType);
            // iconImage.sprite = GetImpactSprite(impact.impactType);
        }
        
        // Set label
        if (labelText != null)
        {
            string label = !string.IsNullOrEmpty(impact.customLabel) 
                ? impact.customLabel 
                : TaskSystem.GetImpactLabel(impact.impactType);
            labelText.text = label;
        }
        
        // Set value
        if (valueText != null)
        {
            if (impact.isCountdown)
            {
                valueText.text = FormatCountdown(impact.value);
            }
            else
            {
                string prefix = impact.value > 0 ? "+" : "";
                valueText.text = prefix + impact.value.ToString();
            }
        }
    }
    
    string FormatCountdown(int seconds)
    {
        if (seconds <= 0) return "00:00";
        
        int minutes = seconds / 60;
        int remainingSeconds = seconds % 60;
        return $"{minutes:D2}:{remainingSeconds:D2}";
    }
    
    void Update()
    {
        // Update countdown values in real-time
        if (impact != null && impact.isCountdown)
        {
            UpdateDisplay();
        }
    }
}

// Agent Message UI Component
public class AgentMessageUI : MonoBehaviour
{
    [Header("UI Components")]
    public Image agentAvatar;
    public TextMeshProUGUI messageText;
    public Image speechBubble;
    
    private AgentMessage message;
    private string fullMessage;
    
    public void Initialize(AgentMessage agentMessage)
    {
        message = agentMessage;
        fullMessage = agentMessage.messageText;
        
        // Set avatar
        if (agentAvatar != null && agentMessage.agentAvatar != null)
            agentAvatar.sprite = agentMessage.agentAvatar;
        
        // Initially hide text for typing effect
        if (messageText != null)
            messageText.text = "";
    }
    
    public IEnumerator PlayTypingEffect(float typingSpeed)
    {
        if (messageText == null || string.IsNullOrEmpty(fullMessage))
            yield break;
        
        messageText.text = "";
        
        for (int i = 0; i <= fullMessage.Length; i++)
        {
            messageText.text = fullMessage.Substring(0, i);
            yield return new WaitForSeconds(typingSpeed);
        }
    }
    
    public void ShowFullMessage()
    {
        if (messageText != null)
            messageText.text = fullMessage;
    }
}

// Player Choice UI Component
public class PlayerChoiceUI : MonoBehaviour
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