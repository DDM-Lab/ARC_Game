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
    public Sprite expandButtonShrinkSprite;
    public Sprite expandButtonExpandSprite;
    
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
    
    [Header("Agent Notification Dots")]
    public GameObject disasterOfficerDot;
    public TextMeshProUGUI disasterOfficerCount;
    public GameObject foodMassCaresDot;
    public TextMeshProUGUI foodMassCareCount;
    public GameObject lodgingMassCaresDot;
    public TextMeshProUGUI lodgingMassCareCount;
    public GameObject workforceServiceDot;
    public TextMeshProUGUI workforceServiceCount;
    public GameObject externalRelationshipDot;
    public TextMeshProUGUI externalRelationshipCount;

    [Header("Action Buttons")]
    public Button confirmButton;

    [Header("Player Input")]
    public TMP_InputField playerInputField;
    public Button sendButton;
    
    [Header("UI Colors")]
    public Color activeAgentColor = Color.green;
    public Color inactiveAgentColor = Color.white;
    public Color inactiveTaskColor = Color.gray;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    private TaskOfficer currentSelectedAgent = TaskOfficer.DisasterOfficer;
    private bool isExpanded = false;
    private bool isAnimating = false;
    private List<GameTask> currentAgentTasks = new List<GameTask>();
    private GameTask currentSelectedTask = null;
    private AgentChoice localSelectedChoice = null;
    private List<GameObject> currentHistoricalTaskButtons = new List<GameObject>();
    private List<GameObject> currentConversationItems = new List<GameObject>();
    private TaskSystem taskSystem;

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

        if (expandedPanel != null)
        {
            expandedPanel.gameObject.SetActive(true);
            expandedPanel.sizeDelta = new Vector2(collapsedWidth, expandedPanel.sizeDelta.y);
        }

        UpdateExpandButtonSprite(false);

        taskSystem = TaskSystem.Instance;
        if (taskSystem != null)
        {
            taskSystem.OnTaskCreated   += OnTaskChanged;
            taskSystem.OnTaskCompleted += OnTaskChanged;
            taskSystem.OnTaskExpired   += OnTaskChanged;
        }

        UpdateAgentNotifications();
    }

    void Update()
    {
        if (Time.frameCount % 30 == 0)
            UpdateAgentNotifications();
    }

    void OnDestroy()
    {
        if (taskSystem != null)
        {
            taskSystem.OnTaskCreated   -= OnTaskChanged;
            taskSystem.OnTaskCompleted -= OnTaskChanged;
            taskSystem.OnTaskExpired   -= OnTaskChanged;
        }
    }

    void OnTaskChanged(GameTask _)
    {
        UpdateAgentNotifications();
    }

    void UpdateAgentNotifications()
    {
        if (taskSystem == null) return;
        UpdateDot(TaskOfficer.DisasterOfficer,      disasterOfficerDot,    disasterOfficerCount);
        UpdateDot(TaskOfficer.FoodMassCare,         foodMassCaresDot,      foodMassCareCount);
        UpdateDot(TaskOfficer.LodgingMassCare,      lodgingMassCaresDot,   lodgingMassCareCount);
        UpdateDot(TaskOfficer.WorkforceService,     workforceServiceDot,   workforceServiceCount);
        UpdateDot(TaskOfficer.ExternalRelationship, externalRelationshipDot, externalRelationshipCount);
    }

    void UpdateDot(TaskOfficer officer, GameObject dot, TextMeshProUGUI countText)
    {
        int count = taskSystem.GetAllActiveTasks()
            .Count(t => t.taskOfficer == officer && t.status == TaskStatus.Active);

        if (dot != null)      dot.SetActive(count > 0);
        if (countText != null)
        {
            countText.gameObject.SetActive(count > 0);
            if (count > 0) countText.text = count.ToString();
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

        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmButtonClicked);

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
        GameLogPanel.Instance?.LogUIInteraction($"Agent conversation panel {(isExpanded ? "expanded" : "collapsed")} | agent={currentSelectedAgent}");
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


        UpdateExpandButtonSprite(expand);

        if (expand)
        {
            RefreshHistoricalTasks();
            DisplayLatestConversation();
        }
        else
        {
            if (agentBarImage != null) agentBarImage.sprite = DefaultAgentBarImage;
            UpdateAgentButtons();
        }
    }
    
    void SelectAgent(TaskOfficer agent)
    {
        if (isAnimating) return;

        if (isExpanded && currentSelectedAgent == agent)
        {
            // Same agent clicked while expanded → collapse
            isExpanded = false;
            StartCoroutine(AnimateExpand(false));
            GameLogPanel.Instance?.LogUIInteraction($"Agent conversation panel collapsed | agent={agent}");
            return;
        }

        currentSelectedAgent = agent;

        if (!isExpanded)
        {
            isExpanded = true;
            if (agentBarImage != null) agentBarImage.sprite = ExpandedAgentBarImage;
            StartCoroutine(AnimateExpand(true));
        }
        else
        {
            RefreshHistoricalTasks();
            DisplayLatestConversation();
        }

        UpdateAgentButtons();

        if (showDebugInfo)
            Debug.Log($"Selected agent: {agent}");
        GameLogPanel.Instance?.LogUIInteraction($"Agent selected: {agent}");
    }
    
    void UpdateAgentButtons()
    {
        SetButtonColor(disasterOfficerButton,
            isExpanded && currentSelectedAgent == TaskOfficer.DisasterOfficer ? activeAgentColor : inactiveAgentColor);
        SetButtonColor(foodMassCareButton,
            isExpanded && currentSelectedAgent == TaskOfficer.FoodMassCare ? activeAgentColor : inactiveAgentColor);
        SetButtonColor(lodgingMassCareButton,
            isExpanded && currentSelectedAgent == TaskOfficer.LodgingMassCare ? activeAgentColor : inactiveAgentColor);
        SetButtonColor(workforceServiceButton,
            isExpanded && currentSelectedAgent == TaskOfficer.WorkforceService ? activeAgentColor : inactiveAgentColor);
        SetButtonColor(externalRelationshipButton,
            isExpanded && currentSelectedAgent == TaskOfficer.ExternalRelationship ? activeAgentColor : inactiveAgentColor);
    }
    
    void UpdateExpandButtonSprite(bool expanded)
    {
        if (expandButton == null) return;
        Image img = expandButton.GetComponent<Image>();
        if (img == null) return;
        img.sprite = expanded ? expandButtonExpandSprite : expandButtonShrinkSprite;
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
        
        string label = task.taskTitle;
        if      (task.status == TaskStatus.Expired)    label = "[Expired] " + label;
        else if (task.status == TaskStatus.Completed)  label = "[Complete] " + label;
        else if (task.status == TaskStatus.Incomplete) label = "[Incomplete] " + label;
        else if (task.status == TaskStatus.InProgress) label = "[In Progress] " + label;

        if (buttonText != null) buttonText.text = label;

        if (task.status != TaskStatus.Active)
        {
            Image buttonImage = buttonObj.GetComponent<Image>();
            if (buttonImage != null) buttonImage.color = inactiveTaskColor;
            if (buttonText != null)  buttonText.color  = inactiveTaskColor;
        }

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
        GameLogPanel.Instance?.LogUIInteraction(
        $"Historical task viewed | agent={currentSelectedAgent} | task=[{task.taskType}] {task.taskTitle} | status={task.status}");
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
        localSelectedChoice = null;

        bool isActive = task.status == TaskStatus.Active;

        DisplaySystemMessage($"=== {task.taskTitle} ===");

        foreach (AgentMessage message in task.agentMessages)
            DisplayAgentMessage(message);

        foreach (AgentChoice choice in task.agentChoices)
        {
            if (isActive) DisplayInteractiveChoice(choice);
            else          DisplayHistoricalChoice(choice);
        }

        foreach (AgentNumericalInput input in task.numericalInputs)
        {
            if (isActive) DisplayInteractiveNumericalInput(input);
            else          DisplayHistoricalNumericalInput(input);
        }

        if (confirmButton != null)
            confirmButton.gameObject.SetActive(isActive);

        ScrollToBottom();
    }

    void DisplayInteractiveChoice(AgentChoice choice)
    {
        GameObject choiceItem = Instantiate(agentChoicePrefab, conversationContent);
        AgentChoiceUI choiceUI = choiceItem.GetComponent<AgentChoiceUI>();
        if (choiceUI != null)
        {
            choiceUI.Initialize(choice, null);
            choiceUI.choiceButton.onClick.AddListener(() => OnLocalChoiceSelected(choice));
        }
        currentConversationItems.Add(choiceItem);
    }

    void DisplayInteractiveNumericalInput(AgentNumericalInput input)
    {
        GameObject inputItem = Instantiate(numericalInputPrefab, conversationContent);
        NumericalInputUI inputUI = inputItem.GetComponent<NumericalInputUI>();
        if (inputUI != null)
            inputUI.Initialize(input, null);
        currentConversationItems.Add(inputItem);
    }

    void OnLocalChoiceSelected(AgentChoice choice)
    {
        localSelectedChoice = choice;
        foreach (GameObject item in currentConversationItems)
        {
            AgentChoiceUI choiceUI = item.GetComponent<AgentChoiceUI>();
            if (choiceUI != null && choiceUI.GetChoice() != choice)
                choiceUI.SetSelected(false);
        }
    }

    void OnConfirmButtonClicked()
    {
        if (currentSelectedTask == null) return;
        TaskDetailUI tui = FindObjectOfType<TaskDetailUI>();
        if (tui == null) return;

        if (!tui.TryConfirmTask(currentSelectedTask, localSelectedChoice, out string errorMessage))
        {
            DisplaySystemMessage($"Error: {errorMessage}");
            return;
        }

        RefreshHistoricalTasks();
        DisplayLatestConversation();
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
            choiceUI.InitializeAsHistorical(choice, choice.choiceId == currentSelectedTask?.selectedChoiceId);
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
        // Update the task's choices data first
        UpdateTaskChoices(officer, reasoning, packages, availableActions);

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

                    // Use AgentChoiceUI component to properly set up the choice display
                    AgentChoiceUI choiceUI = choiceItem.GetComponent<AgentChoiceUI>();
                    if (choiceUI != null)
                    {
                        // Create AgentChoice with formatted text and description
                        string choiceText = FormatPackageActions(package, availableActions);
                        AgentChoice choice = new AgentChoice(package.package_index, choiceText);
                        choice.agentReasoning = FormatChoiceDescription(package, availableActions);

                        // Initialize without parent (null) since inline choices are handled differently
                        choiceUI.Initialize(choice, null);

                        // Override click handler for inline choice selection
                        if (choiceUI.choiceButton != null)
                        {
                            int capturedIndex = package.package_index; // Capture for closure
                            choiceUI.choiceButton.onClick.RemoveAllListeners();
                            choiceUI.choiceButton.onClick.AddListener(() => OnInlineChoiceClicked(capturedIndex));
                        }
                    }

                    currentConversationItems.Add(choiceItem);
                }
            }

            StartCoroutine(ScrollToBottomCoroutine());

            if (showDebugInfo)
                Debug.Log($"Added {messageType} message with {packages.Length} choices from {officer}");
        }
    }

    void UpdateTaskChoices(TaskOfficer officer, string reasoning, ActionPackage[] packages, GameAction[] availableActions)
    {
        // Find the task for this officer
        if (TaskSystem.Instance == null)
        {
            Debug.LogWarning("[AgentConversationUI] TaskSystem.Instance is null, cannot update task choices");
            return;
        }

        // Get agent name from officer
        string agentName = GetCurrentAgentName(officer);

        // Search through active tasks to find one matching this agent
        GameTask targetTask = null;
        foreach (var task in TaskSystem.Instance.activeTasks)
        {
            if (task.multiAgentProposal != null &&
                task.multiAgentProposal.agent_name == agentName)
            {
                targetTask = task;
                break;
            }
        }

        if (targetTask == null)
        {
            if (showDebugInfo)
                Debug.Log($"[AgentConversationUI] No task with multiAgentProposal found for {agentName}");
            return;
        }

        // Update the task's multiAgentProposal with new choices
        targetTask.multiAgentProposal.reasoning = reasoning;
        targetTask.multiAgentProposal.packages = packages;
        targetTask.multiAgentProposal.available_actions = availableActions;

        // Update the agentChoices list as well
        targetTask.agentChoices.Clear();
        for (int i = 0; i < packages.Length; i++)
        {
            var package = packages[i];
            string choiceText = FormatPackageActions(package, availableActions);
            AgentChoice choice = new AgentChoice(package.package_index, choiceText);

            // Build detailed description: package description + action list
            choice.agentReasoning = FormatChoiceDescription(package, availableActions);

            targetTask.agentChoices.Add(choice);
        }

        if (showDebugInfo)
            Debug.Log($"[AgentConversationUI] Updated task {targetTask.taskId} with {packages.Length} new choices");
    }

    string FormatPackageActions(ActionPackage package, GameAction[] availableActions)
    {
        // Use the package label as the primary choice text (strategy name from LLM)
        if (!string.IsNullOrEmpty(package.label))
        {
            return package.label;
        }

        // Fallback: list action descriptions if no label provided
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

    string FormatChoiceDescription(ActionPackage package, GameAction[] availableActions)
    {
        // Build a detailed description with package description + action list
        System.Text.StringBuilder desc = new System.Text.StringBuilder();

        // Add package description from LLM if available
        if (!string.IsNullOrEmpty(package.description))
        {
            desc.AppendLine(package.description);
        }

        // Add action list
        if (package.action_indices != null && package.action_indices.Length > 0)
        {
            if (desc.Length > 0) desc.AppendLine(); // Add spacing

            desc.AppendLine("Actions:");
            foreach (int idx in package.action_indices)
            {
                if (idx >= 0 && idx < availableActions.Length)
                {
                    string actionName = availableActions[idx].description;
                    if (string.IsNullOrEmpty(actionName))
                        actionName = availableActions[idx].action_id;
                    desc.AppendLine($"• {actionName}");
                }
            }
        }

        return desc.ToString().TrimEnd();
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