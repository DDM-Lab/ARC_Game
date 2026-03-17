using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameActions;

public class AgentConversationUI : MonoBehaviour
{
    public static AgentConversationUI Instance { get; private set; }

    [Header("Main Panel")]
    public GameObject mainPanel;
    public Button expandButton;
    
    [Header("Agent Selection Bar")]
    public Button disasterOfficerButton;
    public Button foodMassCareButton;
    public Button lodgingMassCareButton;
    public Button workforceServiceButton;
    public Button externalRelationshipButton;
    public Image agentBarImage;
    public Sprite DefaultAgentBarImage;
    public Sprite ExpandedAgentBarImage;
    
    [Header("Expanded Panel")]
    public RectTransform expandedPanel;
    public float expandedWidth = 600f;
    public float collapsedWidth = 0f;
    public float animationDuration = 0.3f;
    public ScrollRect historicalTasksScrollView;
    public Transform historicalTasksContent;
    public GameObject historicalTaskButtonPrefab;
    
    [Header("Conversation Panel")]
    public ScrollRect conversationScrollView;
    public Transform conversationContent;
    
    [Header("Agent Message Prefabs")]
    public GameObject agentMessagePrefab;
    public GameObject agentChoicePrefab;
    public GameObject numericalInputPrefab;
    public GameObject playerMessagePrefab;
    
    [Header("Player Input")]
    public TMP_InputField playerInputField;
    public Button sendButton;
    
    [Header("Filter Colors")]
    public Color activeAgentColor = Color.green;
    public Color inactiveAgentColor = Color.white;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    private TaskOfficer currentSelectedAgent = TaskOfficer.DisasterOfficer;
    private bool isExpanded = false;
    private bool isAnimating = false;
    private List<GameTask> currentAgentTasks = new List<GameTask>();
    private GameTask currentSelectedTask = null;
    private List<GameObject> currentHistoricalTaskButtons = new List<GameObject>();
    private List<GameObject> currentConversationItems = new List<GameObject>();

    // Store inline choice data for selection
    private Dictionary<int, InlineChoiceData> inlineChoiceDataMap = new Dictionary<int, InlineChoiceData>();

    [System.Serializable]
    private class InlineChoiceData
    {
        public string agentName;
        public ActionPackage[] packages;
        public GameAction[] availableActions;
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("Multiple AgentConversationUI instances found!");
        }
    }

    void Start()
    {
        SetupUI();
        SelectAgent(TaskOfficer.DisasterOfficer);
        
        if (expandedPanel != null)
        {
            expandedPanel.gameObject.SetActive(true);
            expandedPanel.sizeDelta = new Vector2(collapsedWidth, expandedPanel.sizeDelta.y);
        }
    }

    void SetupUI()
    {
        if (expandButton != null)
            expandButton.onClick.AddListener(ToggleExpanded);

        if (disasterOfficerButton != null)
            disasterOfficerButton.onClick.AddListener(() => SelectAgent(TaskOfficer.DisasterOfficer));
        if (foodMassCareButton != null)
            foodMassCareButton.onClick.AddListener(() => SelectAgent(TaskOfficer.FoodMassCare));
        if (lodgingMassCareButton != null)
            lodgingMassCareButton.onClick.AddListener(() => SelectAgent(TaskOfficer.LodgingMassCare));
        if (workforceServiceButton != null)
            workforceServiceButton.onClick.AddListener(() => SelectAgent(TaskOfficer.WorkforceService));
        if (externalRelationshipButton != null)
            externalRelationshipButton.onClick.AddListener(() => SelectAgent(TaskOfficer.ExternalRelationship));

        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendPlayerMessage);
        if (playerInputField != null)
            playerInputField.onSubmit.AddListener(OnPlayerInputSubmit);

        if (agentBarImage != null && DefaultAgentBarImage != null)
            agentBarImage.sprite = DefaultAgentBarImage;
    }
    
    void ToggleExpanded()
    {
        if (isAnimating) return;
        isExpanded = !isExpanded;
        StartCoroutine(AnimateExpand(isExpanded));

        if(isExpanded)
            agentBarImage.sprite = ExpandedAgentBarImage;
        
        if (showDebugInfo)
            Debug.Log($"Agent conversation panel {(isExpanded ? "expanding" : "collapsing")}");
    }

    IEnumerator AnimateExpand(bool expand)
    {
        isAnimating = true;

        float startWidth = expandedPanel.sizeDelta.x;
        float targetWidth = expand ? expandedWidth : collapsedWidth;
        float elapsed = 0f;

        while (elapsed < animationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / animationDuration);
            float easedT = Mathf.SmoothStep(0f, 1f, t);

            float currentWidth = Mathf.Lerp(startWidth, targetWidth, easedT);
            expandedPanel.sizeDelta = new Vector2(currentWidth, expandedPanel.sizeDelta.y);

            yield return null;
        }

        expandedPanel.sizeDelta = new Vector2(targetWidth, expandedPanel.sizeDelta.y);
        isAnimating = false;


        if (expand)
        {
            RefreshHistoricalTasks();
            DisplayLatestConversation();
        }
        else
        {
            agentBarImage.sprite = DefaultAgentBarImage;
        }
    }
    
    void SelectAgent(TaskOfficer agent)
    {
        currentSelectedAgent = agent;
        UpdateAgentButtons();
        
        if (isExpanded)
        {
            RefreshHistoricalTasks();
            DisplayLatestConversation();
        }
        
        if (showDebugInfo)
            Debug.Log($"Selected agent: {agent}");
    }
    
    void UpdateAgentButtons()
    {
        SetButtonColor(disasterOfficerButton, 
            currentSelectedAgent == TaskOfficer.DisasterOfficer ? activeAgentColor : inactiveAgentColor);
        SetButtonColor(foodMassCareButton, 
            currentSelectedAgent == TaskOfficer.FoodMassCare ? activeAgentColor : inactiveAgentColor);
        SetButtonColor(lodgingMassCareButton, 
            currentSelectedAgent == TaskOfficer.LodgingMassCare ? activeAgentColor : inactiveAgentColor);
        SetButtonColor(workforceServiceButton, 
            currentSelectedAgent == TaskOfficer.WorkforceService ? activeAgentColor : inactiveAgentColor);
        SetButtonColor(externalRelationshipButton, 
            currentSelectedAgent == TaskOfficer.ExternalRelationship ? activeAgentColor : inactiveAgentColor);
    }
    
    void SetButtonColor(Button button, Color color)
    {
        if (button != null)
        {
            Image buttonImage = button.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = color;
        }
    }
    
    void RefreshHistoricalTasks()
    {
        if (TaskSystem.Instance == null) return;
        ClearHistoricalTaskButtons();
        currentAgentTasks = GetTasksForAgent(currentSelectedAgent);
        
        foreach (GameTask task in currentAgentTasks)
        {
            CreateHistoricalTaskButton(task);
        }
        
        if (showDebugInfo)
            Debug.Log($"Refreshed historical tasks for {currentSelectedAgent}: {currentAgentTasks.Count} tasks");
    }
    
    List<GameTask> GetTasksForAgent(TaskOfficer agent)
    {
        List<GameTask> agentTasks = new List<GameTask>();
        agentTasks.AddRange(TaskSystem.Instance.GetAllActiveTasks().Where(t => t.taskOfficer == agent));
        agentTasks.AddRange(TaskSystem.Instance.GetTasksByStatus(TaskStatus.Completed).Where(t => t.taskOfficer == agent));
        agentTasks.AddRange(TaskSystem.Instance.GetTasksByStatus(TaskStatus.Incomplete).Where(t => t.taskOfficer == agent));
        agentTasks.AddRange(TaskSystem.Instance.GetTasksByStatus(TaskStatus.Expired).Where(t => t.taskOfficer == agent));
        return agentTasks.OrderByDescending(t => t.timeCreated).ToList();
    }
    
    void CreateHistoricalTaskButton(GameTask task)
    {
        if (historicalTaskButtonPrefab == null || historicalTasksContent == null) return;
        
        GameObject buttonObj = Instantiate(historicalTaskButtonPrefab, historicalTasksContent);
        Button taskButton = buttonObj.GetComponent<Button>();
        TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
        
        if (buttonText != null) buttonText.text = task.taskTitle;
        if (taskButton != null) taskButton.onClick.AddListener(() => SelectHistoricalTask(task));
        
        currentHistoricalTaskButtons.Add(buttonObj);
    }
    
    void ClearHistoricalTaskButtons()
    {
        foreach (GameObject button in currentHistoricalTaskButtons)
            if (button != null) Destroy(button);
        currentHistoricalTaskButtons.Clear();
    }
    
    void SelectHistoricalTask(GameTask task)
    {
        currentSelectedTask = task;
        DisplayTaskConversation(task);
        if (showDebugInfo)
            Debug.Log($"Selected historical task: {task.taskTitle}");
    }
    
    void DisplayLatestConversation()
    {
        if (currentAgentTasks.Count > 0)
        {
            GameTask latestTask = currentAgentTasks[0];
            currentSelectedTask = latestTask;
            DisplayTaskConversation(latestTask);
        }
        else
        {
            ClearConversation();
            DisplayNoTasksMessage();
        }
    }
    
    void DisplayTaskConversation(GameTask task)
    {
        if (task == null) return;
        ClearConversation();
        
        DisplaySystemMessage($"=== {task.taskTitle} ===");
        
        foreach (AgentMessage message in task.agentMessages)
            DisplayAgentMessage(message);
        foreach (AgentChoice choice in task.agentChoices)
            DisplayHistoricalChoice(choice);
        foreach (AgentNumericalInput input in task.numericalInputs)
            DisplayHistoricalNumericalInput(input);
        
        ScrollToBottom();
    }
    
    void DisplaySystemMessage(string message)
    {
        GameObject messageItem = Instantiate(agentMessagePrefab, conversationContent);
        AgentMessageUI messageUI = messageItem.GetComponent<AgentMessageUI>();
        
        if (messageUI != null)
        {
            Sprite agentAvatar = GetOfficerAvatar(currentSelectedAgent);
            AgentMessage systemMessage = new AgentMessage(message, agentAvatar);
            messageUI.Initialize(systemMessage);
            messageUI.ShowFullMessage();
            
            if (messageUI.messageText != null)
            {
                messageUI.messageText.color = Color.gray;
                messageUI.messageText.fontStyle = FontStyles.Italic;
            }
        }
        currentConversationItems.Add(messageItem);
    }
    
    Sprite GetOfficerAvatar(TaskOfficer officer)
    {
        if (TaskSystem.Instance == null) return null;
        
        switch (officer)
        {
            case TaskOfficer.DisasterOfficer: return TaskSystem.Instance.defaultAgentSprite;
            case TaskOfficer.WorkforceService: return TaskSystem.Instance.workforceServiceSprite;
            case TaskOfficer.LodgingMassCare: return TaskSystem.Instance.lodgingMassCareSprite;
            case TaskOfficer.ExternalRelationship: return TaskSystem.Instance.externalRelationshipSprite;
            case TaskOfficer.FoodMassCare: return TaskSystem.Instance.foodMassCareSprite;
            default: return TaskSystem.Instance.defaultAgentSprite;
        }
    }
    
    void DisplayAgentMessage(AgentMessage message)
    {
        GameObject messageItem = Instantiate(agentMessagePrefab, conversationContent);
        AgentMessageUI messageUI = messageItem.GetComponent<AgentMessageUI>();
        
        if (messageUI != null)
        {
            messageUI.Initialize(message);
            messageUI.ShowFullMessage();
        }
        currentConversationItems.Add(messageItem);
    }
    
    void DisplayHistoricalChoice(AgentChoice choice)
    {
        GameObject choiceItem = Instantiate(agentChoicePrefab, conversationContent);
        AgentChoiceUI choiceUI = choiceItem.GetComponent<AgentChoiceUI>();
        
        if (choiceUI != null)
            choiceUI.InitializeAsHistorical(choice);
        currentConversationItems.Add(choiceItem);
    }
    
    void DisplayHistoricalNumericalInput(AgentNumericalInput input)
    {
        GameObject inputItem = Instantiate(numericalInputPrefab, conversationContent);
        NumericalInputUI inputUI = inputItem.GetComponent<NumericalInputUI>();
        
        if (inputUI != null)
            inputUI.InitializeAsHistorical(input);
        currentConversationItems.Add(inputItem);
    }
    
    void DisplayNoTasksMessage()
    {
        DisplaySystemMessage($"No tasks found for {currentSelectedAgent}");
    }
    
    void ClearConversation()
    {
        foreach (GameObject item in currentConversationItems)
            if (item != null) Destroy(item);
        currentConversationItems.Clear();
    }
    
    void ScrollToBottom()
    {
        if (conversationScrollView != null)
            StartCoroutine(ScrollToBottomCoroutine());
    }
    
    IEnumerator ScrollToBottomCoroutine()
    {
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        conversationScrollView.verticalNormalizedPosition = 0f;
    }

    /// <summary>
    /// Add agent conversational message to UI.
    /// Called by WebSocketManager when agent_message is received.
    /// </summary>
    public void AddAgentMessage(TaskOfficer officer, string content, string messageType)
    {
        // Only display if this is the currently selected agent
        if (officer != currentSelectedAgent)
        {
            if (showDebugInfo)
                Debug.Log($"Message from {officer} (not selected), not displaying in UI");
            return;
        }

        // Create agent message in conversation
        if (agentMessagePrefab != null && conversationContent != null)
        {
            GameObject messageItem = Instantiate(agentMessagePrefab, conversationContent);
            AgentMessageUI messageUI = messageItem.GetComponent<AgentMessageUI>();

            if (messageUI != null)
            {
                // Create AgentMessage data for the UI component
                var agentMsg = new AgentMessage(content, null); // Avatar can be null
                messageUI.Initialize(agentMsg);
                StartCoroutine(messageUI.PlayTypingEffect(0.02f));
            }
            else
            {
                // Fallback if AgentMessageUI component not found
                TextMeshProUGUI messageText = messageItem.GetComponentInChildren<TextMeshProUGUI>();
                if (messageText != null) messageText.text = content;
            }

            currentConversationItems.Add(messageItem);
            StartCoroutine(ScrollToBottomCoroutine());

            if (showDebugInfo)
                Debug.Log($"Added {messageType} message from {officer}: {content}");
        }
    }

    public void AddAgentMessageWithChoices(
        TaskOfficer officer,
        string content,
        string messageType,
        string reasoning,
        ActionPackage[] packages,
        GameAction[] availableActions)
    {
        // Only display if this is the currently selected agent
        if (officer != currentSelectedAgent)
        {
            if (showDebugInfo)
                Debug.Log($"Message with choices from {officer} (not selected), not displaying in UI");
            return;
        }

        // Create agent message in conversation
        if (agentMessagePrefab != null && conversationContent != null)
        {
            GameObject messageItem = Instantiate(agentMessagePrefab, conversationContent);
            AgentMessageUI messageUI = messageItem.GetComponent<AgentMessageUI>();

            if (messageUI != null)
            {
                // Create AgentMessage data for the UI component
                var agentMsg = new AgentMessage(content, null);
                messageUI.Initialize(agentMsg);
                StartCoroutine(messageUI.PlayTypingEffect(0.02f));
            }
            else
            {
                // Fallback if AgentMessageUI component not found
                TextMeshProUGUI messageText = messageItem.GetComponentInChildren<TextMeshProUGUI>();
                if (messageText != null) messageText.text = content;
            }

            currentConversationItems.Add(messageItem);

            // Add inline choice cards below the message
            if (agentChoicePrefab != null && packages != null && packages.Length > 0)
            {
                // Store inline choice data
                string agentName = GetCurrentAgentName(officer);
                foreach (var package in packages)
                {
                    // Store data for this choice using package_index
                    inlineChoiceDataMap[package.package_index] = new InlineChoiceData
                    {
                        agentName = agentName,
                        packages = packages,
                        availableActions = availableActions
                    };

                    GameObject choiceItem = Instantiate(agentChoicePrefab, conversationContent);

                    // Set up the choice UI manually for inline display
                    TextMeshProUGUI choiceText = choiceItem.GetComponentInChildren<TextMeshProUGUI>();
                    if (choiceText != null)
                    {
                        choiceText.text = FormatPackageActions(package, availableActions);
                    }

                    // Add click handler for inline choice selection
                    Button choiceButton = choiceItem.GetComponentInChildren<Button>();
                    if (choiceButton != null)
                    {
                        int capturedIndex = package.package_index; // Capture for closure
                        choiceButton.onClick.RemoveAllListeners();
                        choiceButton.onClick.AddListener(() => OnInlineChoiceClicked(capturedIndex));
                    }

                    currentConversationItems.Add(choiceItem);
                }
            }

            StartCoroutine(ScrollToBottomCoroutine());

            if (showDebugInfo)
                Debug.Log($"Added {messageType} message with {packages.Length} choices from {officer}");
        }
    }

    string FormatPackageActions(ActionPackage package, GameAction[] availableActions)
    {
        if (package.action_indices == null || package.action_indices.Length == 0)
            return "No actions";

        var actionNames = new System.Collections.Generic.List<string>();
        foreach (int idx in package.action_indices)
        {
            if (idx >= 0 && idx < availableActions.Length)
            {
                // Use description or action_id as display name
                string actionName = availableActions[idx].description;
                if (string.IsNullOrEmpty(actionName))
                    actionName = availableActions[idx].action_id;
                actionNames.Add(actionName);
            }
        }

        return string.Join(", ", actionNames);
    }

    void OnInlineChoiceClicked(int packageIndex)
    {
        Debug.Log($"[AgentConversationUI] Inline choice clicked: {packageIndex}");

        // Retrieve stored data for this choice
        if (!inlineChoiceDataMap.ContainsKey(packageIndex))
        {
            Debug.LogError($"[AgentConversationUI] No data found for inline choice {packageIndex}");
            return;
        }

        InlineChoiceData data = inlineChoiceDataMap[packageIndex];

        // Find the selected package by matching package_index
        ActionPackage selectedPackage = null;
        for (int i = 0; i < data.packages.Length; i++)
        {
            if (data.packages[i].package_index == packageIndex)
            {
                selectedPackage = data.packages[i];
                break;
            }
        }

        if (selectedPackage == null)
        {
            Debug.LogError($"[AgentConversationUI] Package not found for choice {packageIndex}");
            return;
        }

        Debug.Log($"[AgentConversationUI] Executing inline choice package {packageIndex} with {selectedPackage.action_indices.Length} actions");

        // Execute actions (similar to TaskDetailUI.ExecuteActionPackage)
        StartCoroutine(ExecuteInlineChoicePackage(data.agentName, packageIndex, selectedPackage, data.availableActions));
    }

    System.Collections.IEnumerator ExecuteInlineChoicePackage(
        string agentName,
        int packageIndex,
        ActionPackage package,
        GameAction[] availableActions)
    {
        List<string> executionResults = new List<string>();

        // Execute each action in the package
        foreach (int actionIdx in package.action_indices)
        {
            if (actionIdx >= 0 && actionIdx < availableActions.Length)
            {
                GameAction action = availableActions[actionIdx];
                string actionName = action.description ?? action.action_id;
                Debug.Log($"[AgentConversationUI] Executing inline choice action: {actionName}");

                // Execute action via ActionExecutor instance
                if (ActionExecutor.Instance != null)
                {
                    var result = ActionExecutor.Instance.ExecuteAction(action);
                    executionResults.Add(JsonUtility.ToJson(new {
                        success = result.success,
                        action = actionName,
                        error_message = result.error_message
                    }));
                }
                else
                {
                    Debug.LogError("[AgentConversationUI] ActionExecutor.Instance is null!");
                    executionResults.Add(JsonUtility.ToJson(new {
                        success = false,
                        action = actionName,
                        error_message = "ActionExecutor not found"
                    }));
                }

                yield return new WaitForSeconds(0.1f); // Brief delay between actions
            }
        }

        // Get current game state (simplified - would need proper serialization)
        string gameStateJson = "{}"; // TODO: Serialize current game state

        string executionResultsJson = "[" + string.Join(",", executionResults) + "]";

        // Send choice_made to WebSocket
        if (WebSocketManager.Instance != null)
        {
            WebSocketManager.Instance.SendChoiceMade(agentName, packageIndex, executionResultsJson, gameStateJson);
            Debug.Log($"[AgentConversationUI] Sent choice_made for inline choice {packageIndex}");
        }
    }

    void OnSendPlayerMessage()
    {
        if (playerInputField != null && !string.IsNullOrEmpty(playerInputField.text))
        {
            string message = playerInputField.text;

            // Display player message in UI
            GameObject messageItem = Instantiate(playerMessagePrefab, conversationContent);
            TextMeshProUGUI messageText = messageItem.GetComponentInChildren<TextMeshProUGUI>();

            if (messageText != null) messageText.text = message;

            currentConversationItems.Add(messageItem);
            playerInputField.text = "";
            StartCoroutine(ScrollToBottomCoroutine());

            // Send message to Python backend via WebSocket
            string agentName = GetCurrentAgentName(currentSelectedAgent);
            if (WebSocketManager.Instance != null && !string.IsNullOrEmpty(agentName))
            {
                WebSocketManager.Instance.SendDirectorMessage(agentName, message);
            }

            if (showDebugInfo)
                Debug.Log($"Player sent message to {currentSelectedAgent} ({agentName}): {message}");
        }
    }

    string GetCurrentAgentName(TaskOfficer officer)
    {
        // Look up agent name from config by matching talkinghead_endpoint
        AgentConfigLoader configLoader = FindObjectOfType<AgentConfigLoader>();
        if (configLoader != null && configLoader.IsLoaded)
        {
            foreach (var agent in configLoader.Config.agents)
            {
                if (agent.talkinghead_endpoint == officer.ToString())
                {
                    return agent.subagent_name;
                }
            }
        }
        return officer.ToString(); // Fallback to enum name
    }
    
    void OnPlayerInputSubmit(string message)
    {
        if (!string.IsNullOrEmpty(message))
            OnSendPlayerMessage();
    }
    
    public bool IsUIOpen()
    {
        return mainPanel != null && mainPanel.activeInHierarchy && isExpanded;
    }
    
    public void OpenPanel()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
    }
    
    public void ClosePanel()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
    }
}