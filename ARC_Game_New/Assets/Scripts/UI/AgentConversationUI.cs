using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class AgentConversationUI : MonoBehaviour
{
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
    public Color selectedTaskColor = new Color(0.3f, 0.6f, 1f);
    
    [Header("New Message Popup")]
    public GameObject newMessagePopup;
    public TextMeshProUGUI newMessageCountText;

    [Header("Debug")]
    public bool showDebugInfo = true;

    private TaskOfficer currentSelectedAgent = TaskOfficer.DisasterOfficer;
    private bool isExpanded = false;
    private bool isAnimating = false;
    private int newMessageCount = 0;
    private bool suppressScrollToBottom = false;
    private List<GameTask> currentAgentTasks = new List<GameTask>();
    private GameTask currentSelectedTask = null;
    private AgentChoice localSelectedChoice = null;
    private List<GameObject> currentHistoricalTaskButtons = new List<GameObject>();
    private Dictionary<GameTask, GameObject> taskButtonMap = new Dictionary<GameTask, GameObject>();
    private List<GameObject> currentConversationItems = new List<GameObject>();
    private TaskSystem taskSystem;
    
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

        if (newMessagePopup != null) newMessagePopup.SetActive(false);
        if (conversationScrollView != null)
            conversationScrollView.onValueChanged.AddListener(OnConversationScrollChanged);
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
        if (conversationScrollView != null)
            conversationScrollView.onValueChanged.RemoveListener(OnConversationScrollChanged);
    }

    void OnTaskChanged(GameTask _)
    {
        UpdateAgentNotifications();
        RefreshHistoricalTasks();
        if (!isExpanded) return;

        bool wasAtBottom = IsAtScrollBottom();
        int prevCount = currentConversationItems.Count;

        suppressScrollToBottom = !wasAtBottom;
        if (currentSelectedTask != null && currentSelectedTask.status == TaskStatus.Active)
            DisplayTaskConversation(currentSelectedTask);
        else
            DisplayLatestConversation();
        suppressScrollToBottom = false;

        if (!wasAtBottom)
        {
            int delta = currentConversationItems.Count - prevCount;
            if (delta > 0) ShowNewMessagePopup(delta);
        }
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
            HideNewMessagePopup();
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
        HideNewMessagePopup();

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
        taskButtonMap[task] = buttonObj;
    }
    
    void ClearHistoricalTaskButtons()
    {
        foreach (GameObject button in currentHistoricalTaskButtons)
            if (button != null) Destroy(button);
        currentHistoricalTaskButtons.Clear();
        taskButtonMap.Clear();
    }

    void UpdateSelectedTaskHighlight()
    {
        foreach (var kvp in taskButtonMap)
        {
            GameTask task = kvp.Key;
            GameObject buttonObj = kvp.Value;
            if (buttonObj == null) continue;

            Image buttonImage = buttonObj.GetComponent<Image>();
            TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();

            bool isSelected = task == currentSelectedTask;
            if (isSelected)
            {
                if (buttonImage != null) buttonImage.color = selectedTaskColor;
                if (buttonText != null)  buttonText.color  = Color.white;
            }
            else if (task.status != TaskStatus.Active)
            {
                if (buttonImage != null) buttonImage.color = inactiveTaskColor;
                if (buttonText != null)  buttonText.color  = inactiveTaskColor;
            }
            else
            {
                if (buttonImage != null) buttonImage.color = inactiveAgentColor;
                if (buttonText != null)  buttonText.color  = Color.black;
            }
        }
    }
    
    void SelectHistoricalTask(GameTask task)
    {
        currentSelectedTask = task;
        UpdateSelectedTaskHighlight();
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
            UpdateSelectedTaskHighlight();
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
        {
            AgentMessage resolved = new AgentMessage(task.ResolveFacilityName(message.messageText), message.agentAvatar);
            resolved.useTypingEffect = message.useTypingEffect;
            resolved.typingSpeed = message.typingSpeed;
            DisplayAgentMessage(resolved);
        }

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
            bool wasAtBottom = IsAtScrollBottom();
            DisplaySystemMessage($"Error: {errorMessage}");
            if (wasAtBottom)
                ScrollToBottom();
            else
                ShowNewMessagePopup(1);
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
            messageUI.Initialize(message, OnFacilityLinkClicked);
            messageUI.ShowFullMessage();
        }
        currentConversationItems.Add(messageItem);
    }

    void OnFacilityLinkClicked(string facilityObjectName)
    {
        StartCoroutine(PeekAtFacility(facilityObjectName));
    }

    IEnumerator PeekAtFacility(string facilityObjectName)
    {
        bool wasExpanded = isExpanded;
        if (wasExpanded)
        {
            isExpanded = false;
            yield return StartCoroutine(AnimateExpand(false));
        }

        FacilityHighlightSystem.Instance?.HighlightFacility(facilityObjectName);
        float wait = FacilityHighlightSystem.Instance?.TotalDuration ?? 2f;
        yield return new WaitForSecondsRealtime(wait);

        if (wasExpanded)
        {
            isExpanded = true;
            yield return StartCoroutine(AnimateExpand(true));
        }
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
    
    bool IsAtScrollBottom() =>
        conversationScrollView == null || conversationScrollView.verticalNormalizedPosition <= 0.05f;

    void OnConversationScrollChanged(Vector2 _)
    {
        if (IsAtScrollBottom()) HideNewMessagePopup();
    }

    void ShowNewMessagePopup(int delta)
    {
        newMessageCount += delta;
        if (newMessagePopup != null)
        {
            newMessagePopup.SetActive(true);
            if (newMessageCountText != null)
                newMessageCountText.text = $"{newMessageCount} new message{(newMessageCount == 1 ? "" : "s")}";
        }
    }

    void HideNewMessagePopup()
    {
        newMessageCount = 0;
        if (newMessagePopup != null) newMessagePopup.SetActive(false);
    }

    public void OnNewMessagePopupClicked()
    {
        HideNewMessagePopup();
        ScrollToBottom();
    }

    void ScrollToBottom()
    {
        if (suppressScrollToBottom) return;
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
    
    void OnSendPlayerMessage()
    {
        if (playerInputField != null && !string.IsNullOrEmpty(playerInputField.text))
        {
            string message = playerInputField.text;
            GameObject messageItem = Instantiate(playerMessagePrefab, conversationContent);
            TextMeshProUGUI messageText = messageItem.GetComponentInChildren<TextMeshProUGUI>();
            
            if (messageText != null) messageText.text = message;
            
            currentConversationItems.Add(messageItem);
            
            if (showDebugInfo)
                Debug.Log($"Player sent message to {currentSelectedAgent}: {message}");
                GameLogPanel.Instance?.LogUIInteraction(
            $"Player message sent | agent={currentSelectedAgent} | task={currentSelectedTask?.taskTitle ?? "none"} | message={message}");

            playerInputField.text = "";
            ScrollToBottom();

        }
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