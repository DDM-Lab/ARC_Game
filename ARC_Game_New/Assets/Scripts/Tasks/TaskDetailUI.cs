using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using System.Linq;

public class TaskDetailUI : MonoBehaviour
{
    [Header("Main Panel")]
    public GameObject taskDetailPanel;
    public Button closeButton;
    
    [Header("Left Panel - Task Description")]
    public Image taskImage;
    public TextMeshProUGUI taskTitleText;
    public TextMeshProUGUI facilityText;
    public TextMeshProUGUI descriptionText;
    public Transform ImpactHorizontalchoiceLayout1;
    public Transform ImpactHorizontalchoiceLayout2;
    public GameObject impactItemPrefab;

    [Header("Task Type Sprites")]
    public Image taskTypeImage;
    public Sprite emergencySprite;
    public Sprite demandSprite;
    public Sprite advisorySprite;
    public Sprite alertSprite;

    [Header("Right Panel - Agent Conversation")]
    public ScrollRect conversationScrollView;
    public Transform conversationContent;
    public GameObject agentMessagePrefab;
    public GameObject agentChoicePrefab;
    public GameObject numericalInputPrefab;
    public GameObject playerMessagePrefab;
    
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
    private AgentMessageUI currentTypingMessage;
    
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

        if (playerInputField != null)
        {
            playerInputField.onSubmit.AddListener(OnPlayerInputSubmit);
        }
        
        if (conversationScrollView != null)
        {
            // add event trigger directly to scroll view of conversation
            EventTrigger trigger = conversationScrollView.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = conversationScrollView.gameObject.AddComponent<EventTrigger>();
            
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerClick;
            entry.callback.AddListener((data) => OnConversationAreaClicked());
            trigger.triggers.Add(entry);
        }
    }
    
    void OnConversationAreaClicked()
    {
        if (isTyping && currentTypingMessage != null)
        {
            currentTypingMessage.SkipTyping();
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
        
        if (facilityText != null)
            facilityText.text = currentTask.affectedFacility;
        
        if (descriptionText != null)
            descriptionText.text = currentTask.description;

        if (taskTypeImage != null)
        {
            switch (currentTask.taskType)
            {
                case TaskType.Emergency:
                    taskTypeImage.sprite = emergencySprite;
                    break;
                case TaskType.Demand:
                    taskTypeImage.sprite = demandSprite;
                    break;
                case TaskType.Advisory:
                    taskTypeImage.sprite = advisorySprite;
                    break;
                case TaskType.Alert:
                    taskTypeImage.sprite = alertSprite;
                    break;
            }
        }
        // Update impacts
        UpdateImpactDisplay();
    }

    void UpdateImpactDisplay()
    {
        if (ImpactHorizontalchoiceLayout1 == null || ImpactHorizontalchoiceLayout2 == null || impactItemPrefab == null) return;

        // Clear existing impact items
        ClearImpactItems();

        // Create impact items and put them in the correct layout
        for (int i = 0; i < currentTask.impacts.Count; i++)
        {
            TaskImpact impact = currentTask.impacts[i];
            Transform layout = (i % 2 == 0) ? ImpactHorizontalchoiceLayout1 : ImpactHorizontalchoiceLayout2;
            CreateImpactItem(impact, layout);
        }
    }

    void CreateImpactItem(TaskImpact impact, Transform layout)
    {
        GameObject impactItem = Instantiate(impactItemPrefab, layout);
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
            //yield return new WaitForSecondsRealtime(0.5f); // Brief pause between messages, use real time
        }
        
        // Display choices if available
        if (currentTask.agentChoices.Count > 0)
        {
            DisplayAgentChoices();
        }
        // Display numerical inputs if available
        if (currentTask.numericalInputs.Count > 0)
        {
            DisplayNumericalInputs();
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

            if (message.useTypingEffect && currentTask.status == TaskStatus.Active && !currentTask.isExpired)
            {
                isTyping = true;
                currentTypingMessage = messageUI;
                yield return StartCoroutine(messageUI.PlayTypingEffect(typingSpeed));
                isTyping = false;
                currentTypingMessage = null;
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
            GameObject choiceItem = Instantiate(agentChoicePrefab, conversationContent);
            AgentChoiceUI choiceUI = choiceItem.GetComponent<AgentChoiceUI>();
            
            if (choiceUI != null)
            {
                choiceUI.Initialize(choice, this);
            }
            
            currentConversationItems.Add(choiceItem);
        }
        
        ScrollToBottom();
    }

    void DisplayNumericalInputs()
    {
        if (currentTask.numericalInputs.Count > 0)
        {
            foreach (AgentNumericalInput input in currentTask.numericalInputs)
            {
                GameObject inputItem = Instantiate(numericalInputPrefab, conversationContent);
                NumericalInputUI inputUI = inputItem.GetComponent<NumericalInputUI>();
                
                if (inputUI != null)
                {
                    inputUI.Initialize(input, this);
                    numericalInputs[input.inputId] = input;
                }
                
                currentConversationItems.Add(inputItem);
            }
            
            ScrollToBottom();
        }
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
    
    // doesn't work for now
    void ScrollToBottom()
    {
        if (conversationScrollView != null)
        {
            StartCoroutine(ScrollToBottomCoroutine());
        }
    }

    IEnumerator ScrollToBottomCoroutine()
    {
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        conversationScrollView.verticalNormalizedPosition = 0f;
    }
    
    public void OnChoiceSelected(AgentChoice choice)
    {
        // Deselect other choices
        foreach (GameObject item in currentConversationItems)
        {
            AgentChoiceUI choiceUI = item.GetComponent<AgentChoiceUI>();
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
        if (currentTask == null || TaskSystem.Instance == null) return;
        
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
        
        // check if task requires delivery
        if (currentTask.requiresDelivery)
        {
            CreateDeliveryTask();
            TaskSystem.Instance.SetTaskInProgress(currentTask);
        }
        else
        {
            // Complete the task
            TaskSystem.Instance.CompleteTask(currentTask);
        }

        CloseTaskDetail();
        
        if (showDebugInfo)
            Debug.Log($"Task confirmed and completed");
    }

    void CreateDeliveryTask()
    {
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem != null && currentTask.deliverySource != null && currentTask.deliveryDestination != null)
        {
            // create delivery tasks that may involve multiple deliveries
            List<DeliveryTask> deliveryTasks = deliverySystem.CreateDeliveryTask(
                currentTask.deliverySource,
                currentTask.deliveryDestination, 
                currentTask.deliveryCargoType,
                currentTask.deliveryQuantity,
                3 // high priority
            );
            
            if (deliveryTasks.Count > 0)
            {
                // Store all related delivery task IDs
                currentTask.linkedDeliveryTaskIds = deliveryTasks.Select(dt => dt.taskId).ToList();
                StartCoroutine(MonitorDeliveryProgress());
                
                if (showDebugInfo)
                    Debug.Log($"Created {deliveryTasks.Count} delivery tasks for game task: {currentTask.taskTitle}");
            }
            else
            {
                Debug.LogError("Failed to create delivery tasks");
            }
        }
    }

    IEnumerator MonitorDeliveryProgress()
    {
        float timeRemaining = currentTask.deliveryTimeLimit;
        
        while (timeRemaining > 0 && currentTask.status == TaskStatus.InProgress)
        {
            timeRemaining -= Time.unscaledDeltaTime;
            
            // 检查是否所有delivery都完成了
            if (AreAllDeliveriesCompleted())
            {
                TaskSystem.Instance.CompleteTask(currentTask);
                yield break;
            }
            
            yield return null;
        }
        
        // 超时处理
        if (currentTask.status == TaskStatus.InProgress)
        {
            TaskSystem.Instance.HandleDeliveryFailure(currentTask);
        }
    }

    bool AreAllDeliveriesCompleted()
    {
        if (currentTask.linkedDeliveryTaskIds == null || currentTask.linkedDeliveryTaskIds.Count == 0)
            return false;
        
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return false;
        
        List<DeliveryTask> completedTasks = deliverySystem.GetCompletedTasks();
        
        foreach (int taskId in currentTask.linkedDeliveryTaskIds)
        {
            if (!completedTasks.Any(ct => ct.taskId == taskId))
            {
                return false; // 还有未完成的delivery
            }
        }
        
        return true; // 所有delivery都完成了
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
            
            // create player message item
            GameObject messageItem = Instantiate(playerMessagePrefab, conversationContent);
            
            // Set message text
            TextMeshProUGUI messageText = messageItem.GetComponentInChildren<TextMeshProUGUI>();
            if (messageText != null)
            {
                messageText.text = message;
            }

            playerInputField.text = "";
            ScrollToBottom();
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

            // control player input field status
            if (playerInputField != null)
            {
                playerInputField.interactable = !isTyping;
            }
            if (sendButton != null)
            {
                sendButton.interactable = !isTyping;
            }
            if (currentTask != null && !currentTask.status.Equals(TaskStatus.Active))
            {
                if (laterButton != null)
                    laterButton.interactable = false;
                if (confirmButton != null)
                    confirmButton.interactable = false;
                if (playerInputField != null)
                    playerInputField.interactable = false;
                if (sendButton != null)
                    sendButton.interactable = false;
            }

        }
        
        
    }
}


