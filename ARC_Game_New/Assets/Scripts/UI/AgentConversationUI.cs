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

    // ── "officer is generating" waiting indicator ────────────────────────
    // Transient UI state (NOT conversation history): which officers currently
    // have an LLM turn in flight. The bubble is an ephemeral GameObject appended
    // below the conversation for the displayed officer only; on tab switch the
    // panel re-renders from history and RefreshTypingIndicator re-adds it if that
    // officer is still generating. Cleared when the officer's response frame
    // arrives, on director_turn (round end), or by a timeout backstop.
    private readonly HashSet<TaskOfficer> generatingOfficers = new HashSet<TaskOfficer>();
    private readonly Dictionary<TaskOfficer, float> generatingDeadline = new Dictionary<TaskOfficer, float>();
    private GameObject typingIndicatorItem;
    private Coroutine generatingWatchdog;
    // Backstop: an officer can end its turn without sending any client frame
    // (a silent turn). director_turn clears begin_round bubbles, but a lone
    // director_message to a silent officer has no round-end signal, so auto-clear.
    private const float GeneratingTimeoutSeconds = 90f;

    // Store inline choice data for selection
    private Dictionary<int, InlineChoiceData> inlineChoiceDataMap = new Dictionary<int, InlineChoiceData>();

    // Select-then-confirm state for the live inline proposal. Clicking a choice
    // card only highlights it (records inlineSelectedPackageIndex + deselects the
    // sibling cards tracked in inlineChoiceCardUIs); the shared panel confirmButton
    // (-> OnConfirmButtonClicked) is what actually executes the selected package.
    // Mirrors the task path (TaskDetailUI: OnChoiceSelected highlights, the same
    // confirmButton executes on confirm).
    private int inlineSelectedPackageIndex = -1;
    private List<AgentChoiceUI> inlineChoiceCardUIs = new List<AgentChoiceUI>();

    // Per-officer history of conversation entries: agent messages, player
    // messages, and archived historical choice cards. These are not tied to
    // the currently active GameTask, so they must be replayed manually when
    // the user switches to a tab.
    private enum EntryKind { AgentMessage, PlayerMessage, HistoricalChoice, InlineProposal }
    private class ConversationEntry
    {
        public EntryKind kind;
        public string content;           // text content for AgentMessage / PlayerMessage
        public Sprite avatar;            // officer sprite for AgentMessage
        public AgentChoice archivedChoice; // populated when kind == HistoricalChoice

        // Populated when kind == InlineProposal. A continuous agent's proposal is
        // rendered as choice cards inline in the chat timeline (no GameTask), so it
        // must be replayed in posted order on tab switch / reopen. Only the most
        // recent proposal for an officer stays interactive (proposalLive); earlier
        // ones render as historical (non-clickable) cards.
        public ActionPackage[] proposalPackages;
        public GameAction[] proposalActions;
        public string proposalAgentName;
        public bool proposalLive;
    }
    private Dictionary<TaskOfficer, List<ConversationEntry>> conversationHistory = new Dictionary<TaskOfficer, List<ConversationEntry>>();

    // Per-officer chronological insertion point for the next archived proposal.
    // Whenever a new choices_proposal arrives, we record "the current proposal lives
    // at this index in history" and insert the OLD proposal's archived entries there
    // when it gets replaced. Without this, chat messages that arrive between
    // proposals would visually jump above the archive on reproposal.
    private Dictionary<TaskOfficer, int> proposalInsertIndex = new Dictionary<TaskOfficer, int>();

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

        if (newMessagePopup != null) newMessagePopup.SetActive(false);
        if (conversationScrollView != null)
            conversationScrollView.onValueChanged.AddListener(OnConversationScrollChanged);
    }

    void Update()
    {
        if (Time.frameCount % 30 == 0)
            UpdateAgentNotifications();

        if (isExpanded && currentSelectedTask != null && currentSelectedTask.status == TaskStatus.Active)
            UpdateChoiceValidation();
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
        {
            playerInputField.onSubmit.AddListener(OnPlayerInputSubmit);
            // Discoverability: the conversation box is how the director asks for a
            // fresh set of options, so make that explicit in the prompt text.
            if (playerInputField.placeholder is TMP_Text ph)
                ph.text = "Ask me for different options…";
        }

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
        GameLogPanel.Instance?.LogUIInteraction("agent_info",
            isExpanded ? "conversation_expanded" : "conversation_collapsed",
            $"agent={currentSelectedAgent}");
    }

    IEnumerator AnimateExpand(bool expand)
    {
        isAnimating = true;

        if (expand)
        {
            Debug.Log("Expand animating!");
        }
        else
        {
            Debug.Log("Contract animating!");
        }

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
            GameLogPanel.Instance?.LogUIInteraction("agent_info",
                "conversation_collapsed", $"agent={agent}");
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
        GameLogPanel.Instance?.LogUIInteraction("agent_info",
            "agent_selected", $"agent={agent}");
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
        GameLogPanel.Instance?.LogUIInteraction("agent_info", "historical_task_viewed",
            $"agent={currentSelectedAgent} | task=[{task.taskType}] {task.taskTitle} | status={task.status}");
    }
    
    void DisplayLatestConversation()
    {
        ClearConversation();

        // Render free-form chat history first (player messages, auto summaries,
        // classifier acks). The current task — which holds the latest choice
        // cards — renders below, keeping the active click target at the bottom.
        DisplayConversationHistory(currentSelectedAgent);

        if (currentAgentTasks.Count > 0)
        {
            GameTask latestTask = currentAgentTasks[0];
            currentSelectedTask = latestTask;
            UpdateSelectedTaskHighlight();
            DisplayTaskConversation(latestTask, clearFirst: false);
        }
        else if (!HasConversationHistory(currentSelectedAgent))
        {
            DisplayNoTasksMessage();
        }

        // Re-add the waiting bubble last if this officer is still generating.
        RefreshTypingIndicator();
    }

    // ── waiting-indicator API (called by WebSocketManager) ───────────────

    /// <summary>Mark one officer as generating (true) or done (false) and, if it
    /// is the officer on screen, show/hide the waiting bubble immediately.</summary>
    public void SetOfficerGenerating(TaskOfficer officer, bool generating)
    {
        if (generating)
        {
            generatingOfficers.Add(officer);
            generatingDeadline[officer] = Time.realtimeSinceStartup + GeneratingTimeoutSeconds;
            EnsureGeneratingWatchdog();
        }
        else
        {
            generatingOfficers.Remove(officer);
            generatingDeadline.Remove(officer);
        }
        if (officer == currentSelectedAgent) RefreshTypingIndicator();
    }

    /// <summary>Mark every configured (non-director) officer as generating — used
    /// on begin_round, where all continuous officers are dispatched at once.</summary>
    public void MarkRoundGenerating()
    {
        AgentConfigLoader loader = FindObjectOfType<AgentConfigLoader>();
        if (loader == null || !loader.IsLoaded || loader.Config?.agents == null) return;
        foreach (var agent in loader.Config.agents)
        {
            // The human director's endpoint (e.g. "Player") does not parse to a
            // TaskOfficer, so TryParse naturally skips it.
            if (System.Enum.TryParse(agent.talkinghead_endpoint, out TaskOfficer officer))
                SetOfficerGenerating(officer, true);
        }
    }

    /// <summary>Clear all waiting bubbles — used on director_turn (round end).</summary>
    public void ClearAllGenerating()
    {
        generatingOfficers.Clear();
        generatingDeadline.Clear();
        RefreshTypingIndicator();
    }

    // Destroy any live indicator, then re-add one at the bottom if the displayed
    // officer is still generating and the panel is open.
    void RefreshTypingIndicator()
    {
        if (typingIndicatorItem != null)
        {
            Destroy(typingIndicatorItem);
            typingIndicatorItem = null;
        }
        if (!isExpanded || !generatingOfficers.Contains(currentSelectedAgent)) return;

        typingIndicatorItem = CreateTypingIndicator(currentSelectedAgent);
        StartCoroutine(ScrollToBottomCoroutine());
    }

    // Build the waiting bubble by reusing the agent-message prefab (so it matches
    // real bubbles: avatar + speech bubble), but drive it with TypingIndicatorUI
    // instead of AgentMessageUI's text/height logic.
    GameObject CreateTypingIndicator(TaskOfficer officer)
    {
        if (agentMessagePrefab == null || conversationContent == null) return null;

        GameObject item = Instantiate(agentMessagePrefab, conversationContent);
        NormalizeBubbleRect(item);

        TextMeshProUGUI text = null;
        AgentMessageUI messageUI = item.GetComponent<AgentMessageUI>();
        if (messageUI != null)
        {
            if (messageUI.agentAvatar != null)
                messageUI.agentAvatar.sprite = GetOfficerAvatar(officer);
            text = messageUI.messageText;
            messageUI.enabled = false; // we own the text; don't run its height/link logic
        }
        if (text == null) text = item.GetComponentInChildren<TextMeshProUGUI>();

        LayoutElement le = item.GetComponent<LayoutElement>();
        if (le != null) { le.minHeight = 60f; le.preferredHeight = 60f; }

        item.AddComponent<TypingIndicatorUI>().Begin(text);
        return item;
    }

    void EnsureGeneratingWatchdog()
    {
        if (generatingWatchdog == null)
            generatingWatchdog = StartCoroutine(GeneratingWatchdogLoop());
    }

    // Backstop only: clears bubbles whose officer never sent a terminating frame.
    IEnumerator GeneratingWatchdogLoop()
    {
        var wait = new WaitForSecondsRealtime(1f);
        while (generatingOfficers.Count > 0)
        {
            float now = Time.realtimeSinceStartup;
            List<TaskOfficer> expired = null;
            foreach (var kv in generatingDeadline)
                if (now >= kv.Value) (expired ??= new List<TaskOfficer>()).Add(kv.Key);
            if (expired != null)
                foreach (var off in expired) SetOfficerGenerating(off, false);
            yield return wait;
        }
        generatingWatchdog = null;
    }

    void RecordAgentMessage(TaskOfficer officer, string content)
    {
        AppendHistory(officer, new ConversationEntry
        {
            kind = EntryKind.AgentMessage,
            content = content,
            avatar = GetOfficerAvatar(officer),
        });
    }

    void RecordPlayerMessage(TaskOfficer officer, string content)
    {
        AppendHistory(officer, new ConversationEntry
        {
            kind = EntryKind.PlayerMessage,
            content = content,
        });
    }

    void RecordArchivedChoice(TaskOfficer officer, AgentChoice choice)
    {
        AppendHistory(officer, new ConversationEntry
        {
            kind = EntryKind.HistoricalChoice,
            archivedChoice = choice,
        });
    }

    // Record a continuous agent's inline proposal at its posted position in the
    // timeline. Any prior inline proposal for this officer is demoted to historical
    // so only the latest set of cards stays clickable.
    void RecordInlineProposal(TaskOfficer officer, ActionPackage[] packages,
                              GameAction[] availableActions, string agentName)
    {
        if (conversationHistory.TryGetValue(officer, out var existing))
        {
            foreach (var e in existing)
                if (e.kind == EntryKind.InlineProposal) e.proposalLive = false;
        }
        AppendHistory(officer, new ConversationEntry
        {
            kind = EntryKind.InlineProposal,
            proposalPackages = packages,
            proposalActions = availableActions,
            proposalAgentName = agentName,
            proposalLive = true,
        });
    }

    void AppendHistory(TaskOfficer officer, ConversationEntry entry)
    {
        if (!conversationHistory.TryGetValue(officer, out var list))
        {
            list = new List<ConversationEntry>();
            conversationHistory[officer] = list;
        }
        list.Add(entry);
    }

    bool HasConversationHistory(TaskOfficer officer)
    {
        return conversationHistory.TryGetValue(officer, out var list) && list.Count > 0;
    }

    void DisplayConversationHistory(TaskOfficer officer)
    {
        if (!conversationHistory.TryGetValue(officer, out var entries) || entries.Count == 0)
            return;
        foreach (var entry in entries)
        {
            switch (entry.kind)
            {
                case EntryKind.PlayerMessage:
                    InstantiatePlayerMessage(entry.content);
                    break;
                case EntryKind.HistoricalChoice:
                    if (entry.archivedChoice != null)
                        DisplayHistoricalChoice(entry.archivedChoice);
                    break;
                case EntryKind.InlineProposal:
                    RenderInlineProposal(officer, entry.proposalPackages,
                        entry.proposalActions, entry.proposalAgentName, entry.proposalLive);
                    break;
                case EntryKind.AgentMessage:
                default:
                    DisplayAgentMessage(new AgentMessage(entry.content, entry.avatar));
                    break;
            }
        }
        ScrollToBottom();
    }

    void InstantiatePlayerMessage(string content)
    {
        if (playerMessagePrefab == null || conversationContent == null) return;
        GameObject item = Instantiate(playerMessagePrefab, conversationContent);
        TextMeshProUGUI text = item.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null) text.text = content;
        currentConversationItems.Add(item);
    }

    /// <summary>
    /// Called by WebSocketManager.HandleChoicesProposal after task data has been
    /// refreshed via ApplyLLMTaskContent. Forces a re-render of the conversation
    /// panel if the user is currently viewing that officer's tab so newly
    /// reproposed choices show up immediately instead of only after a tab switch.
    /// </summary>
    public void OnChoicesProposalApplied(TaskOfficer officer)
    {
        if (officer != currentSelectedAgent || !isExpanded) return;
        RefreshHistoricalTasks();
        DisplayLatestConversation();
    }

    /// <summary>
    /// Called by WebSocketManager.HandleChoicesProposal BEFORE GetOrCreateMultiAgentTask
    /// clears the current proposal. Inserts the existing proposal's agent messages and
    /// choice cards into per-officer conversation history at the chronological position
    /// where that proposal arrived — NOT at the end. This keeps subsequent player
    /// messages and acks in their original visual position when a reproposal lands.
    /// After archiving, advances the insertion point to the current end of history so
    /// the just-arriving proposal will, in turn, be archived at its own arrival point
    /// the next time around.
    /// </summary>
    public void ArchiveExistingProposal(TaskOfficer officer)
    {
        if (TaskSystem.Instance == null) return;
        GameTask existing = TaskSystem.Instance.activeTasks.FirstOrDefault(
            t => t.taskId == -1 && t.taskOfficer == officer);

        int insertAt = proposalInsertIndex.TryGetValue(officer, out var idx) ? idx : 0;

        if (existing != null && (existing.agentMessages.Count > 0 || existing.agentChoices.Count > 0))
        {
            // Build the entries we want to insert (reasoning bubbles + each choice card).
            var entries = new List<ConversationEntry>();
            foreach (AgentMessage msg in existing.agentMessages)
            {
                if (string.IsNullOrEmpty(msg.messageText)) continue;
                entries.Add(new ConversationEntry
                {
                    kind = EntryKind.AgentMessage,
                    content = msg.messageText,
                    avatar = msg.agentAvatar != null ? msg.agentAvatar : GetOfficerAvatar(officer),
                });
            }
            foreach (AgentChoice choice in existing.agentChoices)
            {
                entries.Add(new ConversationEntry
                {
                    kind = EntryKind.HistoricalChoice,
                    archivedChoice = choice,
                });
            }

            if (entries.Count > 0)
            {
                if (!conversationHistory.TryGetValue(officer, out var list))
                {
                    list = new List<ConversationEntry>();
                    conversationHistory[officer] = list;
                }
                insertAt = Mathf.Clamp(insertAt, 0, list.Count);
                list.InsertRange(insertAt, entries);
            }
        }

        // The newly-arriving proposal "lives" at the current end of history.
        // The next archive (whenever the user reproposes again) inserts there.
        int newCount = conversationHistory.TryGetValue(officer, out var current) ? current.Count : 0;
        proposalInsertIndex[officer] = newCount;
    }
    
    void DisplayTaskConversation(GameTask task, bool clearFirst = true)
    {
        if (task == null) return;
        if (clearFirst) ClearConversation();
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

        // Keep the "type anything" card visible on tab-switch replay of an
        // active proposal, matching the initial live render.
        if (isActive && task.agentChoices.Count > 0)
            AddFreeTextChoiceCard(currentSelectedAgent);

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
            choiceUI.Initialize(choice, null, PreviewChoiceRoute);
            //choiceUI.Initialize(choice, null, null);
            choiceUI.choiceButton.onClick.AddListener(() => OnLocalChoiceSelected(choice));
        }
        currentConversationItems.Add(choiceItem);
    }

    MonoBehaviour ResolveFacility(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return null;
        var go = GameObject.Find(objectName);
        if (go == null) return null;
        return (MonoBehaviour)go.GetComponent<Building>() ?? go.GetComponent<PrebuiltBuilding>();
    }

    void UpdateChoiceValidation()
    {
        MonoBehaviour triggeringFacility = ResolveFacility(currentSelectedTask?.affectedFacility);

        foreach (GameObject item in currentConversationItems)
        {
            AgentChoiceUI choiceUI = item.GetComponent<AgentChoiceUI>();
            if (choiceUI == null) continue;

            AgentChoice choice = choiceUI.GetChoice();
            bool hasDelivery = choice.triggersDelivery || choice.immediateDelivery;
            if (!hasDelivery) continue;

            string errorMessage = "";
            bool isValid = !choice.triggersDelivery
                || TaskDetailUI.ValidateChoiceDelivery(currentSelectedTask, choice, out errorMessage);
            choiceUI.SetValidationState(isValid, errorMessage);
            bool isImmediateFoodOrder = choice.immediateDelivery && choice.deliveryCargoType == ResourceType.FoodPacks;

            bool canPreview = isValid
                && !isImmediateFoodOrder
                && TaskSystem.Instance != null
                && TaskSystem.Instance.DetermineChoiceDeliverySource(choice, triggeringFacility) != null
                && TaskSystem.Instance.DetermineChoiceDeliveryDestination(choice, triggeringFacility) != null;
            choiceUI.SetPreviewVisible(canPreview);
        }
    }

    void PreviewChoiceRoute(AgentChoice choice)
    {
        Debug.Log("RET RET HERE");
        if (choice == null || currentSelectedTask == null || TaskSystem.Instance == null) return;

        MonoBehaviour triggeringFacility = null;
        if (!string.IsNullOrEmpty(currentSelectedTask.affectedFacility))
        {
            var go = GameObject.Find(currentSelectedTask.affectedFacility);
            if (go != null)
                triggeringFacility = (MonoBehaviour)go.GetComponent<Building>() ?? go.GetComponent<PrebuiltBuilding>();
        }

        MonoBehaviour source = TaskSystem.Instance.DetermineChoiceDeliverySource(choice, triggeringFacility);
        MonoBehaviour dest   = TaskSystem.Instance.DetermineChoiceDeliveryDestination(choice, triggeringFacility);

        if (source == null || dest == null)
        {
            Debug.Log("RET HERERERE");
            Debug.LogWarning("[AgentConversationUI] Could not resolve route source or destination.");
            return;
        }

        Debug.Log("HERERERERE");
        GameTask taskToRestore = currentSelectedTask;
        StartCoroutine(PeekForRoute(source, dest, taskToRestore));
    }


    IEnumerator PeekForRoute(MonoBehaviour source, MonoBehaviour dest, GameTask taskToRestore)
    {
        bool wasExpanded = isExpanded;

        if (wasExpanded)
        {
            isExpanded = false;
            yield return StartCoroutine(AnimateExpand(false));
        }

        // callback to FacilityHighlightSystem to notify AgentConversationUI panel when done
        FacilityHighlightSystem.Instance?.PreviewRouteAndCallback(source, dest, () =>
        {
            if (!gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[AgentConversationUI] Panel inactive when restore callback fired — skipping.");
                return;
            }
            StartCoroutine(RestoreUIAfterPreview(wasExpanded, taskToRestore));
        });
    }

    private IEnumerator RestoreUIAfterPreview(bool wasExpanded, GameTask taskToRestore)
    {
        if (wasExpanded)
        {
            isExpanded = true;
            yield return StartCoroutine(AnimateExpand(true));
        }
        if (taskToRestore != null)
            DisplayTaskConversation(taskToRestore);
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
        // Continuous-agent inline proposals have no backing GameTask — if one is
        // selected, the panel Confirm executes it directly.
        if (TryInlineConfirm())
            return;

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
        NormalizeBubbleRect(messageItem);
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
        NormalizeBubbleRect(messageItem);
        AgentMessageUI messageUI = messageItem.GetComponent<AgentMessageUI>();

        if (messageUI != null)
        {
            messageUI.Initialize(message, OnFacilityLinkClicked);
            messageUI.ShowFullMessage();
        }
        currentConversationItems.Add(messageItem);
    }

    /// <summary>
    /// The agent message prefab ships with a baked-in offset (anchoredPosition.x ~= 312)
    /// and a zero-width root that the conversation panel's VerticalLayoutGroup does NOT
    /// expand (Child Force Expand Width = false). Long auto-agent summaries render off
    /// the right edge of the panel. Force the root to top-stretch within the parent so
    /// VLG can place it correctly and width tracks the panel.
    /// </summary>
    void NormalizeBubbleRect(GameObject bubble)
    {
        if (bubble == null) return;
        RectTransform rt = bubble.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, rt.sizeDelta.y);
    }

    void OnFacilityLinkClicked(string facilityObjectName)
    {
        StartCoroutine(PeekAtFacility(facilityObjectName, currentSelectedTask));
    }

    IEnumerator PeekAtFacility(string facilityObjectName, GameTask taskToRestore)
    {
        bool wasExpanded = isExpanded;

        if (wasExpanded)
        {
            isExpanded = false;
            yield return StartCoroutine(AnimateExpand(false));
        }
        FacilityHighlightSystem.Instance?.HighlightFacilityWithCallback(facilityObjectName, () =>
        {
            if (!gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[AgentConversationUI] Panel inactive when restore callback fired — skipping.");
                return;
            }
            StartCoroutine(RestoreUIAfterFacilityPeek(wasExpanded, taskToRestore));
        });
    }

    private IEnumerator RestoreUIAfterFacilityPeek(bool wasExpanded, GameTask taskToRestore)
    {
        if (wasExpanded)
        {
            isExpanded = true;
            yield return StartCoroutine(AnimateExpand(true));
        }
        if (taskToRestore != null)
        {
            DisplayTaskConversation(taskToRestore);
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

        // The waiting bubble is tracked separately from currentConversationItems;
        // drop it here so a stale one can't linger after a re-render. If the
        // officer is still generating, RefreshTypingIndicator re-adds it below.
        if (typingIndicatorItem != null)
        {
            Destroy(typingIndicatorItem);
            typingIndicatorItem = null;
        }

        // Drop any live inline-proposal selection tied to the cards we just destroyed,
        // so a stale pick can't hijack the panel Confirm after a tab switch. A live
        // proposal for the newly displayed officer re-populates this via RenderInlineProposal.
        inlineChoiceCardUIs.Clear();
        inlineSelectedPackageIndex = -1;
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

    /// <summary>
    /// Add agent conversational message to UI.
    /// Called by WebSocketManager when agent_message is received.
    /// </summary>
    public void AddAgentMessage(TaskOfficer officer, string content, string messageType)
    {
        // Persist to per-officer history first so tab switches can replay it.
        RecordAgentMessage(officer, content);

        // Only display now if this is the currently selected agent
        if (officer != currentSelectedAgent || !isExpanded)
        {
            if (showDebugInfo)
                Debug.Log($"Message from {officer} stored (not currently displayed)");
            return;
        }

        // Create agent message in conversation
        if (agentMessagePrefab != null && conversationContent != null)
        {
            GameObject messageItem = Instantiate(agentMessagePrefab, conversationContent);
            NormalizeBubbleRect(messageItem);
            AgentMessageUI messageUI = messageItem.GetComponent<AgentMessageUI>();

            if (messageUI != null)
            {
                // Use the correct officer avatar so the live render matches the replay.
                var agentMsg = new AgentMessage(content, GetOfficerAvatar(officer));
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
        bool hasPackages = packages != null && packages.Length > 0;

        // Continuous agents render proposals INLINE in the chat timeline — no
        // GameTask is created. Persist the lead-in text and the proposal itself as
        // ordered timeline entries so they replay in posted order on tab switch /
        // reopen (the split "history first, task last" render is what reordered them).
        RecordAgentMessage(officer, content);
        string agentName = GetCurrentAgentName(officer);
        if (hasPackages)
            RecordInlineProposal(officer, packages, availableActions, agentName);

        // Only display now if this is the currently selected agent
        if (officer != currentSelectedAgent || !isExpanded)
        {
            if (showDebugInfo)
                Debug.Log($"Message with choices from {officer} stored (not currently displayed)");
            return;
        }

        // Create agent message in conversation
        if (agentMessagePrefab != null && conversationContent != null)
        {
            GameObject messageItem = Instantiate(agentMessagePrefab, conversationContent);
            NormalizeBubbleRect(messageItem);
            AgentMessageUI messageUI = messageItem.GetComponent<AgentMessageUI>();

            if (messageUI != null)
            {
                // Use the correct officer avatar so the live render matches the replay.
                var agentMsg = new AgentMessage(content, GetOfficerAvatar(officer));
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

            // Add inline choice cards below the message (interactive — this is the
            // latest live proposal).
            if (hasPackages)
                RenderInlineProposal(officer, packages, availableActions, agentName, interactive: true);

            StartCoroutine(ScrollToBottomCoroutine());

            if (showDebugInfo)
                Debug.Log($"Added {messageType} message with {(packages != null ? packages.Length : 0)} choices from {officer}");
        }
    }

    /// <summary>
    /// Render a continuous agent's proposal as inline choice cards under the current
    /// conversation flow. Used both for the live proposal and for in-order replay on
    /// tab switch. When interactive, each card is clickable (executes the package and
    /// sends choice_made) and a free-text "type anything" card is appended; otherwise
    /// the cards render as historical (non-clickable) so only the latest proposal is live.
    /// </summary>
    void RenderInlineProposal(TaskOfficer officer, ActionPackage[] packages,
                              GameAction[] availableActions, string agentName, bool interactive)
    {
        if (agentChoicePrefab == null || conversationContent == null
            || packages == null || packages.Length == 0)
            return;

        // Fresh select-then-confirm state for this live proposal render. Only one
        // proposal is live/visible at a time (RecordInlineProposal demotes prior
        // ones), so the tracking lists are rebuilt per interactive render.
        if (interactive)
        {
            inlineChoiceCardUIs.Clear();
            inlineSelectedPackageIndex = -1;
            // A live inline proposal is confirmed via the shared panel Confirm button;
            // make sure it's visible (a prior historical-task view may have hidden it).
            if (confirmButton != null)
                confirmButton.gameObject.SetActive(true);
        }

        foreach (var package in packages)
        {
            if (interactive)
            {
                // Store data so the confirm handler can execute this package.
                inlineChoiceDataMap[package.package_index] = new InlineChoiceData
                {
                    agentName = agentName,
                    packages = packages,
                    availableActions = availableActions
                };
            }

            GameObject choiceItem = Instantiate(agentChoicePrefab, conversationContent);
            AgentChoiceUI choiceUI = choiceItem.GetComponent<AgentChoiceUI>();
            if (choiceUI != null)
            {
                string choiceText = FormatPackageActions(package, availableActions);
                AgentChoice choice = new AgentChoice(package.package_index, choiceText);
                choice.agentReasoning = FormatChoiceDescription(package, availableActions);

                if (interactive)
                {
                    choiceUI.Initialize(choice, null);
                    inlineChoiceCardUIs.Add(choiceUI);
                    if (choiceUI.choiceButton != null)
                    {
                        int capturedIndex = package.package_index; // Capture for closure
                        // Clicking a card only SELECTS (highlights) it — the inline
                        // confirm button below is what executes. Mirrors the task path.
                        choiceUI.choiceButton.onClick.RemoveAllListeners();
                        choiceUI.choiceButton.onClick.AddListener(() => OnInlineChoiceSelected(capturedIndex));
                    }
                }
                else
                {
                    // Superseded proposal — render as a non-clickable historical card.
                    choiceUI.InitializeAsHistorical(choice, false);
                }
            }

            currentConversationItems.Add(choiceItem);
        }

        // A "type anything" card lets the director free-text the agent (repropose /
        // clarify / chat) right in the choices list — only for the live proposal.
        if (interactive)
            AddFreeTextChoiceCard(officer);
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

    // Clicking a choice card only SELECTS it: highlight this card, un-highlight the
    // siblings, and record the pick. Nothing executes until the panel Confirm button
    // is pressed (OnConfirmButtonClicked -> TryInlineConfirm). Mirrors the task path's
    // OnChoiceSelected.
    void OnInlineChoiceSelected(int packageIndex)
    {
        Debug.Log($"[AgentConversationUI] Inline choice selected: {packageIndex}");
        GameLogPanel.Instance?.LogUIInteraction("choice", "choice_selected",
            $"agent={currentSelectedAgent} | package_index={packageIndex}");

        // Only selectable while the proposal is still live (present in the data map).
        if (!inlineChoiceDataMap.ContainsKey(packageIndex))
        {
            Debug.LogWarning($"[AgentConversationUI] Inline choice {packageIndex} is no longer selectable");
            return;
        }

        inlineSelectedPackageIndex = packageIndex;

        // Highlight the picked card, clear the rest (single-select). The shared panel
        // confirmButton (OnConfirmButtonClicked) executes it.
        foreach (var card in inlineChoiceCardUIs)
        {
            if (card == null) continue;
            var c = card.GetChoice();
            card.SetSelected(c != null && c.choiceId == packageIndex);
        }
    }

    // Execute the currently-selected inline package. Invoked from OnConfirmButtonClicked
    // (the shared panel Confirm button) when a live inline proposal is selected.
    // Returns true if it handled the confirm, false if there was nothing to confirm.
    bool TryInlineConfirm()
    {
        int packageIndex = inlineSelectedPackageIndex;
        if (packageIndex < 0 || !inlineChoiceDataMap.ContainsKey(packageIndex))
            return false;

        Debug.Log($"[AgentConversationUI] Inline confirm for package {packageIndex}");
        GameLogPanel.Instance?.LogUIInteraction("choice", "choice_confirm_clicked",
            $"agent={currentSelectedAgent} | package_index={packageIndex}");

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
            return false;
        }

        Debug.Log($"[AgentConversationUI] Executing inline choice package {packageIndex} with {selectedPackage.action_indices.Length} actions");

        // Consume the whole proposal on confirm: clear every package index that shared
        // this InlineChoiceData so a second confirm can't re-run ExecuteInlineChoicePackage
        // and double-execute. The router's _pending_choice is single-shot too, so later
        // confirms are dead anyway.
        foreach (var pkg in data.packages)
            inlineChoiceDataMap.Remove(pkg.package_index);
        inlineSelectedPackageIndex = -1;

        // Mark the latest inline proposal historical so a tab-reopen replays it as
        // non-clickable rather than resurrecting live cards.
        if (conversationHistory.TryGetValue(currentSelectedAgent, out var hist))
        {
            foreach (var e in hist)
                if (e.kind == EntryKind.InlineProposal) e.proposalLive = false;
        }

        // Execute actions (similar to TaskDetailUI.ExecuteActionPackage)
        StartCoroutine(ExecuteInlineChoicePackage(data.agentName, packageIndex, selectedPackage, data.availableActions));
        return true;
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
                    // Build the result JSON by hand (mirrors TaskDetailUI): Unity's
                    // JsonUtility.ToJson silently serializes an anonymous type to "{}"
                    // because it only emits public *fields* of [Serializable] types,
                    // not the properties of an anon object. That produced [{},{},...],
                    // so the router read every action as success=null → "FAILED" and
                    // the continuous agent re-did the whole package. Emit the exact
                    // shape the router reads: action_index, action_id, success, error.
                    executionResults.Add(
                        $"{{\"action_index\":{actionIdx}," +
                        $"\"action_id\":\"{action.action_id}\"," +
                        $"\"success\":{(result.success ? "true" : "false")}," +
                        $"\"error\":{(result.error_message != null ? "\"" + result.error_message.Replace("\"", "\\\"") + "\"" : "null")}}}");
                }
                else
                {
                    Debug.LogError("[AgentConversationUI] ActionExecutor.Instance is null!");
                    executionResults.Add(
                        $"{{\"action_index\":{actionIdx}," +
                        $"\"action_id\":\"{action.action_id}\"," +
                        $"\"success\":false," +
                        $"\"error\":\"ActionExecutor not found\"}}");
                }

                // Use REALTIME wait: the game is paused (Time.timeScale = 0) during the
                // planning/proposal phase, so WaitForSeconds (scaled) would hang forever
                // after the first action and SendChoiceMade below would never fire,
                // leaving the router's _pending_choice blocked. The task-confirm path
                // (TaskDetailUI) executes actions with no wait at all, so a tiny realtime
                // delay is more than enough.
                yield return new WaitForSecondsRealtime(0.02f); // Brief delay between actions
            }
        }

        // Serialize the post-execution game state so the router can re-enumerate
        // actions from the authoritative state (the continuous agent's
        // _continuous_propose continues its turn from this). Mirrors the
        // task-confirm path (TaskDetailUI); without it the router would adopt an
        // empty {} state and corrupt the agent's continuation.
        string gameStateJson = "{}";
        if (TaskSystem.Instance != null)
        {
            var gs = TaskSystem.Instance.GetCurrentGameState();
            if (gs != null) gameStateJson = JsonUtility.ToJson(gs);
        }

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

            // Persist before render so tab switches replay it.
            RecordPlayerMessage(currentSelectedAgent, message);

            // Display player message in UI
            InstantiatePlayerMessage(message);

            playerInputField.text = "";
            StartCoroutine(ScrollToBottomCoroutine());

            // Send message to Python backend via WebSocket
            string agentName = GetCurrentAgentName(currentSelectedAgent);
            if (WebSocketManager.Instance != null && !string.IsNullOrEmpty(agentName))
            {
                WebSocketManager.Instance.SendDirectorMessage(agentName, message);
                // Show the waiting bubble until this officer responds.
                SetOfficerGenerating(currentSelectedAgent, true);
            }

            if (showDebugInfo)
                Debug.Log($"Player sent message to {currentSelectedAgent} ({agentName}): {message}");
        }
    }

    // Adds a live, inline "type anything" input card below the choice buttons so the
    // director can free-text the agent (repropose / clarify / chat) without leaving the
    // choices list. Cloned from the persistent playerInputField so it inherits the
    // project's TMP_InputField styling — no editor wiring or new prefab required.
    void AddFreeTextChoiceCard(TaskOfficer officer)
    {
        if (playerInputField == null || conversationContent == null)
        {
            Debug.LogWarning($"[FreeTextCard] skipped — playerInputField null? {playerInputField == null}, content null? {conversationContent == null}");
            return;
        }

        // Build a row that mirrors a choice card's three-column layout so it lines up
        // column-for-column: [agent-icon gap | beige input panel | checkbox]. The choice
        // card root is a 550-wide HorizontalLayoutGroup; rather than replicate its layout
        // internals, we make an equal-width root and copy the live ChoiceSection / checkbox
        // X-positions from a sibling card after layout (see AlignFreeTextCard).
        GameObject card = new GameObject("FreeTextChoiceCard", typeof(RectTransform));
        card.transform.SetParent(conversationContent, false);
        card.transform.localScale = Vector3.one;
        RectTransform rt = card.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(550f, 56f); // root width == choice card; refined below
        LayoutElement cardLE = card.AddComponent<LayoutElement>();
        cardLE.minHeight = 56f;
        cardLE.preferredHeight = 56f;
        cardLE.minWidth = 550f;
        cardLE.preferredWidth = 550f;

        // The input panel — a clone of the chat input field. Instantiate detached, then
        // parent with worldPositionStays=false so the clone takes the layout group's local
        // space (the parented overload keeps world scale and collapses it to invisible).
        GameObject panel = Instantiate(playerInputField.gameObject);
        panel.name = "FreeTextPanel";
        panel.transform.SetParent(card.transform, false);
        panel.transform.localScale = Vector3.one;
        panel.SetActive(true);
        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0f, 1f);
        prt.anchorMax = new Vector2(0f, 1f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(400f, 46f);      // aligned to ChoiceSection below
        prt.anchoredPosition = new Vector2(277f, -28f);

        Image bg = panel.GetComponent<Image>();
        if (bg == null) bg = panel.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 1f);
        bg.raycastTarget = true;

        Color textColor = new Color(0.239f, 0.184f, 0.176f, 1f); // descriptionText baked color

        TMP_InputField field = panel.GetComponent<TMP_InputField>();
        TaskOfficer captured = officer;
        if (field != null)
        {
            field.text = "";
            field.interactable = true;
            if (field.placeholder is TMP_Text ph)
            {
                ph.text = "Type anything here — ask me to repropose or clarify…";
                ph.color = new Color(textColor.r, textColor.g, textColor.b, 0.55f);
                ph.enabled = true;
            }
            if (field.textComponent != null)
                field.textComponent.color = textColor;

            field.onSubmit.RemoveAllListeners();
            field.onSubmit.AddListener((string msg) => OnFreeTextCardSubmit(captured, field, msg));
        }

        // Checkbox column — sits where the choice cards' checkbox is (far right). Clicking it
        // submits the typed text, mirroring "select this option" on the other cards.
        GameObject check = new GameObject("FreeTextCheckbox", typeof(RectTransform), typeof(Image));
        check.transform.SetParent(card.transform, false);
        check.transform.localScale = Vector3.one;
        RectTransform crt = check.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 1f);
        crt.anchorMax = new Vector2(0f, 1f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(30f, 30f);
        crt.anchoredPosition = new Vector2(513f, -28f);
        Image checkImg = check.GetComponent<Image>();
        Button checkBtn = check.AddComponent<Button>();
        checkBtn.onClick.RemoveAllListeners();
        checkBtn.onClick.AddListener(() => { if (field != null) OnFreeTextCardSubmit(captured, field, field.text); });

        // Copy the exact ChoiceSection / checkbox styling + X positions from a live choice
        // card once layout has run, so the panel and checkbox share the cards' columns.
        StartCoroutine(AlignFreeTextCard(card, panel, bg, check, checkImg, field));

        Debug.Log($"[FreeTextCard] added for {officer}; field null? {field == null}; "
                  + $"sibling#{card.transform.GetSiblingIndex()}");

        currentConversationItems.Add(card);
    }

    // Align the free-text card's input panel and checkbox to a live choice card's columns.
    // Runs post-layout so the choice card's HorizontalLayoutGroup has already positioned its
    // ChoiceSection / ButtonSection; we copy those X positions (root widths are equal, so an
    // equal anchoredPosition.x lands in the same column) plus the beige sprite/color.
    System.Collections.IEnumerator AlignFreeTextCard(GameObject card, GameObject panel,
        Image bg, GameObject check, Image checkImg, TMP_InputField field)
    {
        yield return new WaitForEndOfFrame();
        if (card == null || conversationContent == null) yield break;

        AgentChoiceUI sample = null;
        foreach (Transform child in conversationContent)
        {
            var ui = child.GetComponent<AgentChoiceUI>();
            if (ui != null) { sample = ui; break; }
        }
        if (sample == null) yield break;

        RectTransform rt = card.GetComponent<RectTransform>();
        float cardH = rt != null ? rt.rect.height : 56f;

        // Match the choice card root width so equal child X positions line up.
        RectTransform srt = sample.transform as RectTransform;
        if (rt != null && srt != null && srt.rect.width > 1f)
            rt.sizeDelta = new Vector2(srt.rect.width, rt.sizeDelta.y);

        // Panel <- ChoiceSection (the beige background: descriptionText -> statLayout -> section).
        TMP_Text body = sample.descriptionText != null ? sample.descriptionText : sample.choiceText;
        Image section = (body != null && body.transform.parent != null && body.transform.parent.parent != null)
            ? body.transform.parent.parent.GetComponent<Image>() : null;
        if (section != null && panel != null)
        {
            RectTransform sectRT = section.rectTransform;
            RectTransform prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 1f);
            prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            if (sectRT.rect.width > 1f)
                prt.sizeDelta = new Vector2(sectRT.rect.width, prt.sizeDelta.y);
            prt.anchoredPosition = new Vector2(sectRT.anchoredPosition.x, -cardH * 0.5f);
            if (bg != null)
            {
                bg.sprite = section.sprite;
                bg.type = section.type;
                bg.color = section.color;
                bg.fillCenter = section.fillCenter;
                bg.pixelsPerUnitMultiplier = section.pixelsPerUnitMultiplier;
            }
        }
        if (body != null && field != null)
        {
            if (field.textComponent != null) field.textComponent.color = body.color;
            if (field.placeholder is TMP_Text ph2)
                ph2.color = new Color(body.color.r, body.color.g, body.color.b, 0.55f);
        }

        // Checkbox <- the choice card's checkbox column (choiceButton lives in ButtonSection).
        if (sample.choiceButton != null && check != null)
        {
            RectTransform brt = sample.choiceButton.transform as RectTransform;
            RectTransform bsec = brt != null ? brt.parent as RectTransform : null;
            float checkX = bsec != null ? bsec.anchoredPosition.x
                         : (brt != null ? brt.anchoredPosition.x : 513f);
            RectTransform crt = check.GetComponent<RectTransform>();
            crt.anchoredPosition = new Vector2(checkX, -cardH * 0.5f);
            Image bimg = sample.choiceButton.GetComponent<Image>();
            if (bimg != null && checkImg != null)
            {
                checkImg.sprite = bimg.sprite;
                checkImg.type = bimg.type;
                checkImg.color = bimg.color;
                checkImg.fillCenter = bimg.fillCenter;
                checkImg.pixelsPerUnitMultiplier = bimg.pixelsPerUnitMultiplier;
            }
        }
    }

    void OnFreeTextCardSubmit(TaskOfficer officer, TMP_InputField field, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        RecordPlayerMessage(officer, message);
        InstantiatePlayerMessage(message);
        if (field != null) field.text = "";
        StartCoroutine(ScrollToBottomCoroutine());

        string agentName = GetCurrentAgentName(officer);
        if (WebSocketManager.Instance != null && !string.IsNullOrEmpty(agentName))
        {
            WebSocketManager.Instance.SendDirectorMessage(agentName, message);
            SetOfficerGenerating(officer, true);
        }

        if (showDebugInfo)
            Debug.Log($"Free-text card → {officer} ({agentName}): {message}");
    }

    // Copies the visible look of a real choice card onto the free-text card: its beige
    // panel sprite/tint, its narrower visible width (the ChoiceSection panel is ~400px,
    // inset within the 550px card root), and returns that card's body-text color via
    // `textColor`. No-op (keeps the baked prefab fallbacks) if no sibling card is present.
    void MatchChoiceCardStyle(RectTransform rt, Image bg, ref Color textColor)
    {
        if (conversationContent == null) return;

        AgentChoiceUI sample = null;
        foreach (Transform child in conversationContent)
        {
            var ui = child.GetComponent<AgentChoiceUI>();
            if (ui != null) { sample = ui; break; }
        }
        if (sample == null) return;

        // Body text color — descriptionText is the field the card actually renders into.
        TMP_Text bodyText = sample.descriptionText != null ? sample.descriptionText : sample.choiceText;
        if (bodyText != null) textColor = bodyText.color;

        // The beige panel is ChoiceSection = descriptionText -> statLayout -> ChoiceSection.
        Image section = null;
        if (bodyText != null && bodyText.transform.parent != null && bodyText.transform.parent.parent != null)
            section = bodyText.transform.parent.parent.GetComponent<Image>();
        if (section == null || bg == null) return;

        bg.sprite = section.sprite;
        bg.type = section.type;
        bg.color = section.color;
        bg.fillCenter = section.fillCenter;
        bg.pixelsPerUnitMultiplier = section.pixelsPerUnitMultiplier;

        // Match the panel's visible width (fixed-anchor, so its sizeDelta.x is reliable).
        RectTransform sectionRt = section.rectTransform;
        if (rt != null && sectionRt != null && sectionRt.rect.width > 1f)
            rt.sizeDelta = new Vector2(sectionRt.rect.width, rt.sizeDelta.y);
    }

    // Sets the card's LayoutElement width to the content column width so the vertical
    // layout group renders it full-width instead of collapsing it to a zero-width sliver.
    void ApplyCardWidth(LayoutElement le)
    {
        RectTransform parentRt = conversationContent as RectTransform;
        if (parentRt == null || le == null) return;
        float w = parentRt.rect.width;
        var vlg = conversationContent.GetComponent<VerticalLayoutGroup>();
        if (vlg != null) w -= (vlg.padding.left + vlg.padding.right);
        if (w > 1f)
        {
            le.minWidth = w;
            le.preferredWidth = w;
        }
    }

    System.Collections.IEnumerator LogCardSizeNextFrame(GameObject card)
    {
        yield return new WaitForEndOfFrame();
        if (card == null) yield break;
        RectTransform rt = card.GetComponent<RectTransform>();
        // If layout still shows zero width (parent not resolved when first applied),
        // recompute now that the column width is final and force a rebuild.
        if (rt != null && rt.rect.width < 1f)
        {
            ApplyCardWidth(card.GetComponent<LayoutElement>());
            RectTransform parentRt = conversationContent as RectTransform;
            if (parentRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
        }
        Vector2 size = rt != null ? rt.rect.size : Vector2.zero;
        Debug.Log($"[FreeTextCard] post-layout size={size} activeInHierarchy={card.activeInHierarchy} "
                  + $"worldPos={card.transform.position}");
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