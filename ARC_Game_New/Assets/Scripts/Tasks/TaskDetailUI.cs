using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using System.Linq;
using GameActions;

public class TaskDetailUI : MonoBehaviour
{
    [Header("Main Panel")]
    public GameObject taskDetailPanel;
    public Button closeButton;

    [Header("Left Panel - Task Description")]
    public Image taskImage;
    public Sprite defaultTaskImage;
    public TextMeshProUGUI taskTitleText;
    public TextMeshProUGUI facilityText;
    public TextMeshProUGUI descriptionText;
    public Transform ImpactHorizontalchoiceLayout1;
    public Transform ImpactHorizontalchoiceLayout2;
    public GameObject impactItemPrefab_Short;
    public GameObject impactItemPrefab_Long;

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
    public GameTask CurrentTask => currentTask;
    private List<GameObject> currentImpactItems = new List<GameObject>();
    private List<GameObject> currentConversationItems = new List<GameObject>();
    private AgentChoice selectedChoice;
    private Dictionary<int, AgentNumericalInput> numericalInputs = new Dictionary<int, AgentNumericalInput>();
    private bool isTyping = false;
    private AgentMessageUI currentTypingMessage;
    private Vector2 lastScrollPosition;
    
    // NEW: Track which tasks have been shown before
    private HashSet<int> previouslyShownTaskIds = new HashSet<int>();

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

    public void SkipCurrentTyping()
    {
        if (isTyping && currentTypingMessage != null)
        {
            // Stop typing coroutine
            StopAllCoroutines();

            // Show full text immediately
            currentTypingMessage.SkipTyping();

            isTyping = false;

            // Auto-scroll to bottom
            if (conversationScrollView != null)
            {
                Canvas.ForceUpdateCanvases();
                conversationScrollView.verticalNormalizedPosition = 0f;
            }
        }
    }

    public void ShowTaskDetail(GameTask task)
    {
        // Re-check the task's live state before displaying it — a population/food-dependent choice
        // can go stale (or its displayed quantity can drift from what confirming will actually cost)
        // within the same round it's opened, before TaskSystem's next round-end sweep would catch
        // it. If this auto-resolves the task, there's nothing left to show.
        if (TaskSystem.Instance != null && !TaskSystem.Instance.RefreshTaskAgainstLiveState(task))
        {
            if (taskDetailPanel != null && currentTask == task)
                taskDetailPanel.SetActive(false);

            FindObjectOfType<TaskCenterNotification>()?.RefreshNotification();
            FindObjectOfType<CategoryTaskManager>()?.RefreshTaskList();
            return;
        }

        // Check if this task was shown before
        bool isFirstTimeShowing = !previouslyShownTaskIds.Contains(task.taskId);
        
        currentTask = task;

        if (taskDetailPanel != null)
        {
            taskDetailPanel.SetActive(true);

            UpdateTaskDescription();
            
            // Pass the isFirstTimeShowing flag to StartAgentConversation
            StartAgentConversation(isFirstTimeShowing);
            
            UpdateActionButtons();

            // Mark task as shown
            previouslyShownTaskIds.Add(task.taskId);

            if (showDebugInfo)
                Debug.Log($"Showing task detail for: {task.taskTitle} (First time: {isFirstTimeShowing})");

            GameLogPanel.Instance?.LogUIInteraction($"Opened task: [{currentTask.taskType}] {currentTask.taskTitle} at {currentTask.affectedFacility}");
            GameLogPanel.Instance?.LogTaskEvent(SerializeTaskContent(currentTask));

            // Make the repropose affordance discoverable on the choices proposal.
            SetReproposePlaceholder();
        }
    }

    /// <summary>
    /// Re-render this panel's agent conversation if it is currently showing the
    /// multi-agent proposal for <paramref name="officer"/>. Called when a
    /// choices_proposal (initial or reproposed) arrives so the newly proposed
    /// options replace the stale ones in place, instead of only appearing after
    /// the panel is closed and reopened.
    /// </summary>
    public void RefreshProposalIfShowing(TaskOfficer officer)
    {
        if (currentTask == null || taskDetailPanel == null || !taskDetailPanel.activeInHierarchy)
            return;
        if (currentTask.taskId != -1 || currentTask.taskOfficer != officer)
            return;

        // currentTask.agentMessages / agentChoices were already replaced with the new
        // proposal by TaskSystem.GetOrCreateMultiAgentTask + ApplyLLMTaskContent, so a
        // plain re-render (isFirstTimeShowing:false → no typing effect) shows the fresh
        // options immediately.
        StartAgentConversation(false);
        SetReproposePlaceholder();
    }

    /// <summary>
    /// Make the "you can ask me to repropose" affordance discoverable: when the
    /// current task is a multi-agent proposal, prompt the free-text box with an
    /// explicit hint. No-op for regular tasks (keeps their existing placeholder).
    /// </summary>
    void SetReproposePlaceholder()
    {
        if (playerInputField == null) return;
        bool isProposal = currentTask != null && currentTask.taskId == -1
            && currentTask.multiAgentProposal != null;
        if (!isProposal) return;
        var ph = playerInputField.placeholder as TMP_Text;
        if (ph != null)
            ph.text = "Ask me for different options…";
    }

    public static string SerializeTaskContent(GameTask task)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"TASK_DETAIL | id={task.taskId} | title={task.taskTitle} | type={task.taskType} | tag={task.taskTag} | facility={task.affectedFacility} | status={task.status}");

        if (task.impacts != null && task.impacts.Count > 0)
        {
            sb.Append(" | impacts=");
            sb.Append(string.Join(";", task.impacts.Select(i => $"{i.impactType}:{i.value}")));
        }
        if (task.agentMessages != null && task.agentMessages.Count > 0)
        {
            sb.Append(" | messages=");
            sb.Append(string.Join(";", task.agentMessages.Select(m => m.messageText.Replace("|", "/").Replace(";", ","))));
        }
        if (task.agentChoices != null && task.agentChoices.Count > 0)
        {
            sb.Append(" | choices=");
            sb.Append(string.Join(";", task.agentChoices.Select(c => $"[{c.choiceId}]{c.choiceText}")));
        }
        if (task.numericalInputs != null && task.numericalInputs.Count > 0)
        {
            sb.Append(" | inputs=");
            sb.Append(string.Join(";", task.numericalInputs.Select(n => 
                $"[{n.inputId}]{n.inputLabel}:min={n.minValue},max={n.maxValue}")));
        }

        return sb.ToString();
    }

    void OnFacilityLinkClicked(string facilityObjectName)
    {
        GameLogPanel.Instance?.LogUIInteraction(
            $"Facility link clicked in agent message, now highlighting the referred facility on the map. | task={currentTask?.taskTitle ?? "none"} | facility={facilityObjectName}");

        StopAllCoroutines();
        isTyping = false;
        currentTypingMessage = null;
        StartCoroutine(PeekAtFacility(facilityObjectName, currentTask));
    }

    IEnumerator PeekAtFacility(string facilityObjectName, GameTask taskToRestore)
    {
        taskDetailPanel.SetActive(false);
        FacilityHighlightSystem.Instance?.HighlightFacility(facilityObjectName);
        float wait = FacilityHighlightSystem.Instance?.TotalDuration ?? 2f;
        yield return new WaitForSecondsRealtime(wait);

        if (currentTask != taskToRestore) yield break;

        taskDetailPanel.SetActive(true);
    }

    public void PreviewChoiceRoute(AgentChoice choice)
    {
        if (choice == null || currentTask == null || TaskSystem.Instance == null)
        {
            GameLogPanel.Instance?.LogUIInteraction(
                $"Preview route clicked | task={currentTask?.taskTitle ?? "none"} | choice={choice?.choiceText ?? "none"} | source=unresolved | destination=unresolved");
            return;
        }

        if (choice.deliveryCargoType == ResourceType.FoodPacks && choice.triggersDelivery && !choice.immediateDelivery
            && FoodDeliveryHandler.Instance != null)
        {
            // var plan = FoodDeliveryHandler.Instance.PlanSources(currentTask, choice.deliveryQuantity);
            var plan = FoodDeliveryHandler.Instance.PlanSources(currentTask, choice); 
            MonoBehaviour dest = TaskSystem.Instance.FindTriggeringFacility(currentTask);

            GameLogPanel.Instance?.LogUIInteraction(
                $"Preview route clicked | task={currentTask.taskTitle} | choice={choice.choiceText} | " +
                $"sources={(plan.Count > 0 ? string.Join(",", plan.Select(p => p.kitchen.name)) : "unresolved")} | destination={dest?.name ?? "unresolved"}");

            if (plan.Count == 0 || dest == null)
            {
                Debug.LogWarning("[PreviewChoiceRoute] Could not resolve food delivery plan.");
                return;
            }

            StopAllCoroutines();
            isTyping = false;
            currentTypingMessage = null;
            StartCoroutine(PeekForMultiRoute(plan.Select(p => p.kitchen).ToList(), dest, currentTask));
            return;
        }

        // Population relocation to Shelter/Motel doesn't use DetermineChoiceDeliveryDestination at
        // execution time at all — ExecuteClientRelocation routes it through
        // ClientRelocationHandler.Execute/ExecuteImmediate, which picks by shelter-preference then
        // most available space (GetDestinationsSorted), not nearest distance. Previewing via the
        // generic resolver below could show a different building than delivery actually uses, so
        // ask ClientRelocationHandler what it would actually pick instead — same as the FoodPacks
        // branch above does for kitchens. Non-Shelter SpecificBuilding (e.g. CaseworkSite) is
        // unaffected: ExecuteClientRelocation already falls back to the generic resolver for that
        // case too, so preview and execution already agree there.
        if (choice.deliveryCargoType == ResourceType.Population
            && (choice.destinationType != DeliveryDestinationType.SpecificBuilding || choice.destinationBuilding == BuildingType.Shelter)
            && ClientRelocationHandler.Instance != null)
        {
            bool toShelter = choice.destinationType != DeliveryDestinationType.SpecificPrebuilt
                        || choice.destinationPrebuilt != PrebuiltBuildingType.Motel;
            bool toMotel   = choice.destinationType == DeliveryDestinationType.SpecificPrebuilt
                        && choice.destinationPrebuilt == PrebuiltBuildingType.Motel;
            if (!toShelter && !toMotel) { toShelter = true; toMotel = true; }

            MonoBehaviour popSource = TaskSystem.Instance.FindTriggeringFacility(currentTask);
            MonoBehaviour popDest = ClientRelocationHandler.Instance.PeekPrimaryDestination(
                currentTask, toShelter, toMotel, filterByPath: !choice.immediateDelivery);

            GameLogPanel.Instance?.LogUIInteraction(
                $"Preview route clicked | task={currentTask.taskTitle} | choice={choice.choiceText} | " +
                $"source={popSource?.name ?? "unresolved"} | destination={popDest?.name ?? "unresolved"}");

            if (popSource == null || popDest == null)
            {
                Debug.LogWarning("[PreviewChoiceRoute] Could not resolve population relocation destination.");
                return;
            }

            StopAllCoroutines();
            isTyping = false;
            currentTypingMessage = null;
            StartCoroutine(PeekForRoute(popSource, popDest, currentTask));
            return;
        }

        MonoBehaviour triggeringFacility = ResolveTriggeringFacility();
        MonoBehaviour source = TaskSystem.Instance.DetermineChoiceDeliverySource(choice, triggeringFacility);
        MonoBehaviour destination = TaskSystem.Instance.DetermineChoiceDeliveryDestination(choice, triggeringFacility);

        GameLogPanel.Instance?.LogUIInteraction(
            $"Preview route clicked | task={currentTask.taskTitle} | choice={choice.choiceText} | " +
            $"source={source?.name ?? "unresolved"} | destination={destination?.name ?? "unresolved"}");

        if (source == null || destination == null)
        {
            Debug.LogWarning("[PreviewChoiceRoute] Could not resolve source or destination.");
            return;
        }

        StopAllCoroutines();
        isTyping = false;
        currentTypingMessage = null;
        StartCoroutine(PeekForRoute(source, destination, currentTask));
    }

    IEnumerator PeekForRoute(MonoBehaviour source, MonoBehaviour dest, GameTask taskToRestore)
    {
        taskDetailPanel.SetActive(false);
        FacilityHighlightSystem.Instance?.HighlightRoute(source, dest);
        float wait = FacilityHighlightSystem.Instance?.TotalDuration ?? 2f;
        yield return new WaitForSecondsRealtime(wait);

        // Something else already opened/closed the panel for a different task while we were peeking — leave it alone.
        if (currentTask != taskToRestore) yield break;

        taskDetailPanel.SetActive(true);
    }

    IEnumerator PeekForMultiRoute(List<MonoBehaviour> sources, MonoBehaviour dest, GameTask taskToRestore)
    {
        taskDetailPanel.SetActive(false);

        if (FacilityHighlightSystem.Instance == null)
        {
            yield return new WaitForSecondsRealtime(2f);
        }
        else
        {
            bool done = false;
            FacilityHighlightSystem.Instance.HighlightMultiSourceRoute(sources, dest, () => done = true);
            while (!done) yield return null;
        }

        if (currentTask != taskToRestore) yield break;
        taskDetailPanel.SetActive(true);
    }

    public void CloseTaskDetail()
    {
        GameLogPanel.Instance?.LogUIInteraction($"Closed task: {currentTask?.taskTitle}");

        // Auto-discard Other type tasks when closed
        if (currentTask != null && currentTask.taskType == TaskType.Other)
        {
            if (TaskSystem.Instance != null && TaskSystem.Instance.activeTasks != null)
            {
                TaskSystem.Instance.activeTasks.Remove(currentTask); // Completely remove from system
            }
        }

        if (taskDetailPanel != null)
        {
            // Stop all running coroutines before clearing display
            StopAllCoroutines();

            // Reset typing state
            isTyping = false;
            currentTypingMessage = null;

            // Force clear all UI elements immediately (before hiding panel)
            ClearDisplay();

            taskDetailPanel.SetActive(false);

            if (showDebugInfo)
                Debug.Log("Task detail closed");
        }
    }

    void UpdateTaskDescription()
    {
        if (currentTask == null) return;

        // Update task info
        if (taskImage != null)
            taskImage.sprite = currentTask.taskImage ?? defaultTaskImage;

        if (taskTitleText != null)
            taskTitleText.text = currentTask.ResolvePlaceholders(currentTask.taskTitle);

        if (facilityText != null)
            facilityText.text = string.IsNullOrEmpty(currentTask.facilityDisplayName) ? currentTask.affectedFacility : currentTask.facilityDisplayName;

        if (descriptionText != null)
        {
            descriptionText.text = currentTask.ResolvePlaceholders(currentTask.description);
        }

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
        if (ImpactHorizontalchoiceLayout1 == null || ImpactHorizontalchoiceLayout2 == null || impactItemPrefab_Short == null || impactItemPrefab_Long == null) return;

        // Clear existing impact items
        ClearImpactItems();

        // Create impact items and put them in the correct layout
        for (int i = 0; i < currentTask.impacts.Count; i++)
        {
            TaskImpact impact = currentTask.impacts[i];
            Transform layout = (i % 2 == 0) ? ImpactHorizontalchoiceLayout1 : ImpactHorizontalchoiceLayout2;
            bool useLongPrefab = (i % 2 == 0) ? false : true;
            if (currentTask.impacts.Count == 2)
            {
                useLongPrefab = true;
            }else if (currentTask.impacts.Count == 4)
            {
                useLongPrefab = false;
            }
            else if (currentTask.impacts.Count == 1)
            {
                useLongPrefab = true;
            }
            CreateImpactItem(impact, layout, useLongPrefab);
        }
    }

    void CreateImpactItem(TaskImpact impact, Transform layout, bool useLongPrefab)
    {
        GameObject impactItem = Instantiate(useLongPrefab ? impactItemPrefab_Long : impactItemPrefab_Short, layout);
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

    // MODIFIED: Now accepts isFirstTimeShowing parameter
    void StartAgentConversation(bool isFirstTimeShowing)
    {
        if (currentTask == null) return;

        // Clear existing conversation - ALWAYS clear to prevent duplicates
        ClearConversation();

        // Start conversation coroutine with the flag
        StartCoroutine(PlayAgentConversation(isFirstTimeShowing));
    }

    // MODIFIED: Now accepts and uses isFirstTimeShowing parameter
    IEnumerator PlayAgentConversation(bool isFirstTimeShowing)
    {
        // Display agent messages with or without typing effect based on if it's first time
        foreach (AgentMessage message in currentTask.agentMessages)
        {
            // Check if panel is still active before each message
            if (taskDetailPanel == null || !taskDetailPanel.activeInHierarchy)
                yield break;

            AgentMessage resolved = new AgentMessage(currentTask.ResolvePlaceholders(message.messageText), message.agentAvatar);
            resolved.useTypingEffect = message.useTypingEffect;
            resolved.typingSpeed = message.typingSpeed;
            yield return StartCoroutine(DisplayAgentMessage(resolved, isFirstTimeShowing));
        }

        // Check if panel is still active before displaying choices
        if (taskDetailPanel == null || !taskDetailPanel.activeInHierarchy)
            yield break;

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

    // Now accepts and uses isFirstTimeShowing parameter
    IEnumerator DisplayAgentMessage(AgentMessage message, bool isFirstTimeShowing)
    {
        // Check if panel is still active
        if (taskDetailPanel == null || !taskDetailPanel.activeInHierarchy)
            yield break;

        GameObject messageItem = Instantiate(agentMessagePrefab, conversationContent);
        AgentMessageUI messageUI = messageItem.GetComponent<AgentMessageUI>();

        if (messageUI != null)
        {
            messageUI.Initialize(message, OnFacilityLinkClicked);

            // Only show typing effect if it's the first time AND conditions are met AND settings allow it
            if (message.useTypingEffect && currentTask.status == TaskStatus.Active &&
                !currentTask.isExpired && isFirstTimeShowing && !SettingsPanel.SkipTyping)
            {
                isTyping = true;
                currentTypingMessage = messageUI;
                yield return StartCoroutine(messageUI.PlayTypingEffect(typingSpeed));
                isTyping = false;
                currentTypingMessage = null;
            }
            else
            {
                // Show full message immediately if reopening or conditions not met
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
        bool live = currentTask.status == TaskStatus.Active || currentTask.status == TaskStatus.InProgress;
        foreach (AgentChoice choice in currentTask.agentChoices)
        {
            GameObject choiceItem = Instantiate(agentChoicePrefab, conversationContent);
            AgentChoiceUI choiceUI = choiceItem.GetComponent<AgentChoiceUI>();

            if (choiceUI != null)
            {
                if (live)
                {
                    choiceUI.Initialize(choice, this);
                    // Proactively disable a relocation choice that can't be executed right now
                    // (no space, no vehicle, or all routes flood-blocked) and show the reason
                    // inline, instead of letting the player pick it and fail on Confirm.
                    string reason;
                    if (!IsChoiceFeasible(choice, out reason))
                        choiceUI.SetValidationState(false, reason);
                }
                else
                    choiceUI.InitializeAsHistorical(choice, choice.choiceId == currentTask.selectedChoiceId, currentTask);
            }

            currentConversationItems.Add(choiceItem);
        }

        // A 4th "type anything" card lets the director free-text the agent (repropose /
        // clarify / chat) right in the choices list, when the proposal is still live.
        // if (live)
        //     AddFreeTextChoiceCard();

        ScrollToBottom();
    }

    // Clones the persistent playerInputField into the choices list as an inline
    // "type anything" card, so the director can free-text the agent without hunting
    // for the bottom chat bar. Reuses the existing TMP_InputField styling — no new prefab.
    void AddFreeTextChoiceCard()
    {
        if (playerInputField == null || conversationContent == null)
        {
            Debug.LogWarning($"[FreeTextCard] skipped — playerInputField null? {playerInputField == null}, content null? {conversationContent == null}");
            return;
        }

        // Build a row that mirrors a choice card's three-column layout so it lines up
        // column-for-column: [agent-icon gap | beige input panel | checkbox]. Rather than
        // replicate the choice card's HorizontalLayoutGroup internals, we make an equal-width
        // root and copy the live ChoiceSection / checkbox X positions from a sibling card
        // after layout (see AlignFreeTextCard).
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
        // parent with worldPositionStays=false (the parented overload keeps world scale and
        // collapses the clone to an invisible sliver).
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

        Color textColor = new Color(0.239f, 0.184f, 0.176f, 1f);

        TMP_InputField field = panel.GetComponent<TMP_InputField>();
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
            field.onSubmit.AddListener((string msg) => OnFreeTextCardSubmit(field, msg));
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
        checkBtn.onClick.AddListener(() => { if (field != null) OnFreeTextCardSubmit(field, field.text); });

        // Copy the exact ChoiceSection / checkbox styling + X positions from a live choice
        // card once layout has run, so the panel and checkbox share the cards' columns.
        StartCoroutine(AlignFreeTextCard(card, panel, bg, check, checkImg, field));

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

    void OnFreeTextCardSubmit(TMP_InputField field, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // Route through the director_message path (repropose/clarify/chat classifier),
        // exactly like the bottom chat bar for a multi-agent proposal.
        if (WebSocketManager.Instance != null && WebSocketManager.Instance.IsConnected()
            && currentTask != null && currentTask.taskId == -1 && currentTask.multiAgentProposal != null)
        {
            WebSocketManager.Instance.SendDirectorMessage(
                currentTask.multiAgentProposal.agent_name, message);
        }

        GameObject messageItem = Instantiate(playerMessagePrefab, conversationContent);
        TextMeshProUGUI messageText = messageItem.GetComponentInChildren<TextMeshProUGUI>();
        if (messageText != null) messageText.text = message;
        currentConversationItems.Add(messageItem);
        GameLogPanel.Instance?.LogUIInteraction($"Player message sent | message={message}");

        if (field != null) field.text = "";
        ScrollToBottom();
    }

    // Copies a real choice card's beige panel sprite/tint + visible width onto the free-text
    // card, and returns that card's body-text color. No-op (keeps prefab fallbacks) if no
    // sibling choice card is present to sample.
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

        RectTransform sectionRt = section.rectTransform;
        if (rt != null && sectionRt != null && sectionRt.rect.width > 1f)
            rt.sizeDelta = new Vector2(sectionRt.rect.width, rt.sizeDelta.y);
    }

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

    System.Collections.IEnumerator FixCardWidthNextFrame(GameObject card)
    {
        yield return new WaitForEndOfFrame();
        if (card == null) yield break;
        RectTransform rt = card.GetComponent<RectTransform>();
        if (rt != null && rt.rect.width < 1f)
        {
            ApplyCardWidth(card.GetComponent<LayoutElement>());
            RectTransform parentRt = conversationContent as RectTransform;
            if (parentRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
        }
        Debug.Log($"[FreeTextCard/TaskDetail] post-layout size={(rt != null ? rt.rect.size : Vector2.zero)}");
    }

    /// <summary>True (with no reason) if the choice is non-delivery or currently executable;
    /// false + reason when a delivery choice can't be carried out now (so it can be disabled with
    /// an inline explanation). Immediate/helicopter choices airlift externally and stay valid;
    /// deferred (road/kitchen) choices need the infrastructure + a clear route.</summary>
    bool IsChoiceFeasible(AgentChoice choice, out string reason)
    {
        reason = "";
        if (!(choice.triggersDelivery || choice.immediateDelivery))
            return true;

        if (choice.deliveryCargoType == ResourceType.FoodPacks)
        {
            // Immediate food = external airlift (no kitchen needed). Deferred = from kitchens.
            if (choice.immediateDelivery || FoodDeliveryHandler.Instance == null) return true;
            return FoodDeliveryHandler.Instance.CanExecute(currentTask, choice, out reason);
        }

        // "Send to casework site" is return-home processing, not a shelter/motel relocation —
        // don't gate it on shelter space (that wrongly hid the only casework-processing choice).
        if (choice.deliveryCargoType == ResourceType.Population
                && choice.destinationBuilding == BuildingType.CaseworkSite)
            return true;

        if (choice.deliveryCargoType == ResourceType.Population && ClientRelocationHandler.Instance != null)
        {
            bool toShelter = choice.destinationType != DeliveryDestinationType.SpecificPrebuilt
                          || choice.destinationPrebuilt != PrebuiltBuildingType.Motel;
            bool toMotel   = choice.destinationType == DeliveryDestinationType.SpecificPrebuilt
                          && choice.destinationPrebuilt == PrebuiltBuildingType.Motel;
            if (!toShelter && !toMotel) { toShelter = true; toMotel = true; }
            return ClientRelocationHandler.Instance.CheckFeasibility(
                currentTask, toShelter, toMotel, choice.immediateDelivery, out reason);
        }

        return true;
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
        }
    }

    void ClearConversation()
    {
        foreach (GameObject item in currentConversationItems)
        {
            if (item != null)
                Destroy(item);
        }

        // Safety: destroy any orphaned children not tracked in our list
        // (e.g. messages instantiated mid-typing when player exits)
        if (conversationContent != null)
        {
            foreach (Transform child in conversationContent)
            {
                if (child != null)
                    Destroy(child.gameObject);
            }
        }

        currentConversationItems.Clear();
        selectedChoice = null;
    }

    void ClearDisplay()
    {
        // NEW: Stop any remaining coroutines
        StopAllCoroutines();

        // Reset typing state
        isTyping = false;
        currentTypingMessage = null;

        ClearImpactItems();
        ClearConversation();
        currentTask = null;
        selectedChoice = null;
        numericalInputs.Clear();

        if (showDebugInfo)
            Debug.Log("Display cleared completely");
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
        // A switch is selecting a choice when a *different* one was already active.
        AgentChoice previous = selectedChoice;
        bool isSwitch = previous != null && previous != choice;

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
        GameLogPanel.Instance?.LogUIInteraction("choice",
            isSwitch ? "choice_switched" : "choice_selected",
            isSwitch
                ? $"task={currentTask?.taskTitle} | from=[{previous.choiceId}] {previous.choiceText} | to=[{choice.choiceId}] {choice.choiceText}"
                : $"task={currentTask?.taskTitle} | choice=[{choice.choiceId}] {choice.choiceText}");
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

    // Later button not used any more
    void OnLaterButtonClicked()
    {
        if (currentTask != null && currentTask.taskType == TaskType.Advisory)
        {
            TaskSystem.Instance?.IgnoreTask(currentTask);
            CloseTaskDetail();

            // Refresh notification
            TaskCenterNotification notification = FindObjectOfType<TaskCenterNotification>();
            if (notification != null)
                notification.RefreshNotification();

            CategoryTaskManager categoryManager = FindObjectOfType<CategoryTaskManager>();
            if (categoryManager != null)
                categoryManager.RefreshTaskList();
                
            if (showDebugInfo)
                Debug.Log($"Task postponed: {currentTask.taskTitle}");
        }
    }

    public bool TryConfirmTask(GameTask task, AgentChoice choice, out string errorMessage)
    {
        errorMessage = null;
        currentTask = task;
        selectedChoice = choice;
        numericalInputs.Clear();
        foreach (var input in task.numericalInputs)
            numericalInputs[input.inputId] = input;

        // Re-check live state right before acting, not just when the panel was opened — the
        // facility this task depends on may have been drained by a different task confirmed while
        // this panel sat open (see TaskSystem.RefreshTaskAgainstLiveState). If that auto-resolves
        // the task, its own popup already explains why — don't also try to execute a dead choice.
        if (TaskSystem.Instance != null && !TaskSystem.Instance.RefreshTaskAgainstLiveState(task))
        {
            errorMessage = "This task is no longer needed and has been automatically closed — see the popup for details.";
            return false;
        }

        // Same gate every other confirm path uses (expiry, numerical inputs, delivery
        // feasibility, worker rules, budget) — this used to be reimplemented here as a partial
        // subset that skipped delivery/worker/budget validation entirely, so an agent-conversation
        // confirm (the only caller of TryConfirmTask) could queue a delivery — e.g. relocating a
        // community to a Shelter with no capacity — that the UI's own validation text already
        // correctly flagged as invalid. SelectTaskChoiceHeadless and OnConfirmButtonClicked already
        // call ValidateBeforeConfirm for exactly this reason; this brings TryConfirmTask in line.
        if (!ValidateBeforeConfirm(task, choice, out errorMessage))
            return false;

        if (choice != null && (choice.triggersDelivery || choice.immediateDelivery || choice.enableMultipleDeliveries))
            ToastManager.ShowToast($"Delivery for task '{task.ResolvePlaceholders(task.taskTitle, plainFacilityName: true)}' is added to queue.", ToastType.Info, true);
        return CompleteTaskAction(out errorMessage);
    }

    void OnConfirmButtonClicked()
    {
        if (currentTask == null || TaskSystem.Instance == null) return;

        GameLogPanel.Instance?.LogUIInteraction("choice", "choice_confirm_clicked",
            selectedChoice != null
                ? $"task={currentTask.taskTitle} | choice=[{selectedChoice.choiceId}] {selectedChoice.choiceText}"
                : $"task={currentTask.taskTitle} | choice=none");

        string validationError;
        if (!ValidateBeforeConfirm(currentTask, selectedChoice, out validationError))
        {
            ShowAgentErrorMessage(validationError);
            return;
        }

        // Re-check live state right before acting — same reasoning as TryConfirmTask. If this
        // auto-resolves the task, ResolveTaskClientsAlreadyRelocated already showed the explanatory
        // popup, so just close this panel on it instead of also raising a redundant error.
        if (!TaskSystem.Instance.RefreshTaskAgainstLiveState(currentTask))
        {
            CloseTaskDetail();
            FindObjectOfType<TaskCenterNotification>()?.RefreshNotification();
            FindObjectOfType<CategoryTaskManager>()?.RefreshTaskList();
            return;
        }

        if (currentTask.agentChoices != null && currentTask.agentChoices.Count > 0 && selectedChoice == null)
        {
            ShowAgentErrorMessage("Please select a choice before confirming.");
            return;
        }

        // Validate numerical inputs before proceeding
        string numericalValidationError;
        if (!ValidateNumericalInputs(out numericalValidationError))
        {
            ShowAgentErrorMessage(numericalValidationError);
            return;
        }

        // Validate selected choice if it involves any type of delivery
        if (selectedChoice != null && (selectedChoice.triggersDelivery || selectedChoice.immediateDelivery || selectedChoice.enableMultipleDeliveries))
            ToastManager.ShowToast($"Delivery for '{currentTask.ResolvePlaceholders(currentTask.taskTitle, plainFacilityName: true)}' queued.", ToastType.Info, true);

        // Check if this is the first time confirming a task
        /*if (FirstTimeActionTracker.Instance != null && FirstTimeActionTracker.Instance.IsFirstTaskConfirm())
        {
            if (ConfirmationPopup.Instance != null)
            {
                ConfirmationPopup.Instance.ShowPopup(
                    message: "Once confirmed, this decision is irreversible. Are you sure you want to proceed? (This is a one-time tutorial prompt)",
                    onConfirm: () => {
                        FirstTimeActionTracker.Instance.MarkTaskConfirmCompleted();
                        string why;
        if (!CompleteTaskAction(out why))
            ShowAgentErrorMessage($"Action Aborted: {why}");
                    },
                    title: "Confirm Task Decision"
                );
                return;
            }
        }*/
        CompleteTaskAction();

    }

    // The actual task completion logic
    // Returns true if the task action was completed; false if it was rejected before
    // taking effect (e.g. the chosen option's immediate cost exceeds the budget under
    // the no-debt policy). On rejection nothing is applied and the task stays open.
// Inside TaskDetailUI.cs

private bool CompleteTaskAction()
{
    return CompleteTaskAction(out _);
}

// Returns true if the action took effect. On false NOTHING was applied (no impacts, no status
// change) and failReason says why.
private bool CompleteTaskAction(out string failReason)
{
    failReason = null;
    Debug.Log("Complete task action called!");

    if (currentTask != null && currentTask.taskId == -1 && currentTask.multiAgentProposal != null)
    {
        HandleMultiAgentChoiceSelection();
        return true;
    }

    if (selectedChoice != null)
        currentTask.selectedChoiceId = selectedChoice.choiceId;

    if (selectedChoice != null)
    {
        // Budget gate: the same rule ActionExecutor applies to build/hire actions (BUG_REPORTS B12).
        int cost = GetChoiceImmediateCost(selectedChoice);
        if (cost > 0 && SatisfactionAndBudget.Instance != null && !SatisfactionAndBudget.Instance.WouldAllowSpend(cost))
        {
            failReason = $"insufficient budget (option costs ${cost:N0}, budget is ${SatisfactionAndBudget.Instance.GetCurrentBudget():N0})";
            return false;
        }

        // Handle Budget Delays (display mirror; the credit itself is scheduled by ApplyChoiceImpacts)
        if (selectedChoice.budgetDelayRounds > 0)
        {
            var budgetImpact = selectedChoice.choiceImpacts.FirstOrDefault(i => i.impactType == ImpactType.Budget);
            if (budgetImpact != null && budgetImpact.value != 0)
            {
                DelayedBudgetManager.Instance?.AddDelayedBudget(currentTask.taskTitle, budgetImpact.value, selectedChoice.budgetDelayRounds);
            }
        }

        if (selectedChoice.immediateDelivery)
        {
            // Resolve the actual quantity BEFORE executing delivery — ExecuteGeneratorDelivery
            // mutates destination storage (see FoodDeliveryHandler.ExecuteImmediate), which would
            // change GetFoodNeed()'s result if resolved again afterward. This snapshot is what
            // costPerUnit-based scaling below uses to price the delivery.
            int? resolvedQuantity = null;
            if (selectedChoice.costPerUnit > 0 && selectedChoice.deliveryCargoType == ResourceType.FoodPacks
                && FoodDeliveryHandler.Instance != null)
            {
                MonoBehaviour dest = TaskSystem.Instance.FindTriggeringFacility(currentTask);
                if (dest != null)
                    resolvedQuantity = FoodDeliveryHandler.Instance.ResolveQuantity(selectedChoice, dest);
            }
            // Execute FIRST, and charge only on success. Dropping this call (as an earlier
            // resolution of this region did) completes the task and bills the player for a
            // delivery that never moved — and, because ApplyChoiceImpacts then runs
            // unconditionally, prices it differently from the queued path.
            bool success = ExecuteGeneratorDelivery(selectedChoice, immediate: true) != 0;
            if (success)
            {
                ApplyChoiceImpacts(selectedChoice, resolvedQuantity);
                TaskSystem.Instance.CompleteTask(currentTask);
            }
        }
        else if (selectedChoice.triggersDelivery || selectedChoice.enableMultipleDeliveries)
        {
            // QUEUE THE DELIVERY. enableMultipleDeliveries is folded in here because no shipped
            // task asset sets it without triggersDelivery — so this covers the same choices, and
            // removes a second `else if (triggersDelivery)` below that could never be reached.
            bool success = ExecuteGeneratorDelivery(selectedChoice, immediate: false) != 0;
            if (success)
                ApplyChoiceImpacts(selectedChoice);
        }
        else
        {
            ApplyChoiceImpacts(selectedChoice);
            TaskSystem.Instance.CompleteTask(currentTask);
        }
    }
    else
    {
        TaskSystem.Instance.CompleteTask(currentTask);
    }

    CloseTaskDetail();
    return true;
}

// Ensure Food Delivery properly returns success
bool ExecuteFoodDelivery(AgentChoice choice, bool immediate)
{
    if (FoodDeliveryHandler.Instance == null) return false;
    if (immediate)
    {
        int added = FoodDeliveryHandler.Instance.ExecuteImmediate(currentTask, choice);
        if (added <= 0)
            ShowAgentErrorMessage("Could not deliver food: the destination was not found or has no room.");
        return added > 0;
    }
    // This queues the actual delivery tasks in the DeliverySystem
    bool success = FoodDeliveryHandler.Instance.Execute(currentTask, choice);
    if (!success)
        ShowAgentErrorMessage("Could not queue food delivery. No kitchen has spare stock, vehicles are damaged, or the need is already covered by inbound deliveries.");
    return success;
}

    // Immediate (non-deferred) cost of committing this choice: the sum of negative Budget
    // impacts. Positive Budget impacts are incoming funds (scheduled, possibly delayed) and
    // do NOT offset the up-front cost. Used by the no-debt gate to reject unaffordable choices.
    int GetChoiceImmediateCost(AgentChoice choice)
    {
        int cost = 0;
        if (choice != null && choice.choiceImpacts != null)
        {
            foreach (TaskImpact impact in choice.choiceImpacts)
            {
                if (impact.impactType == ImpactType.Budget && impact.value < 0)
                    cost += -(int)impact.value;
            }
        }
        return cost;
    }

    /// <summary>
    /// Headless / gym entry point: select a choice on an active task by id and
    /// complete it, reusing the exact CompleteTaskAction path (apply impacts +
    /// trigger delivery + complete) the UI uses. Returns false if the task or
    /// choice can't be found, or if the choice was rejected (e.g. unaffordable
    /// under the no-debt policy). No UI interaction required.
    /// </summary>
    public bool SelectTaskChoiceHeadless(int taskId, int choiceId)
    {
        return SelectTaskChoiceHeadless(taskId, choiceId, null, out _);
    }

    /// <summary>
    /// Overload that reports why the selection failed (for accurate gym error messages):
    /// "not found" vs "rejected — insufficient budget under the no-debt policy".
    /// </summary>
    public bool SelectTaskChoiceHeadless(int taskId, int choiceId, out string failReason)
    {
        return SelectTaskChoiceHeadless(taskId, choiceId, null, out failReason);
    }

    /// <summary>
    /// Overload that also accepts the stable cross-regeneration task id. Recurring tasks
    /// (e.g. "Daily Budget Allocation") get a fresh transient int taskId each round, so an
    /// officer answering against a frozen observation may send a stale int. If the int lookup
    /// misses and a stableId is supplied, we retry against GameTask.stableTaskId.
    /// </summary>
    public bool SelectTaskChoiceHeadless(int taskId, int choiceId, string stableId, out string failReason)
    {
        failReason = null;
        if (TaskSystem.Instance == null || TaskSystem.Instance.activeTasks == null)
        {
            failReason = "TaskSystem not ready";
            return false;
        }
        GameTask task = TaskSystem.Instance.activeTasks.FirstOrDefault(t => t.taskId == taskId);
        if (task == null && !string.IsNullOrEmpty(stableId))
        {
            task = TaskSystem.Instance.activeTasks.FirstOrDefault(t => t.stableTaskId == stableId);
        }
        if (task == null)
        {
            failReason = $"Task {taskId} (stableId '{stableId}') not found among active tasks " +
                         $"[ids: {string.Join(",", TaskSystem.Instance.activeTasks.Select(t => t.taskId))}]";
            return false;
        }
        AgentChoice choice = task.agentChoices != null
            ? task.agentChoices.FirstOrDefault(c => c.choiceId == choiceId)
            : null;
        if (choice == null)
        {
            failReason = $"Choice {choiceId} not found on task {taskId}";
            return false;
        }

        currentTask = task;
        selectedChoice = choice;
        numericalInputs.Clear();
        foreach (var input in task.numericalInputs)
            numericalInputs[input.inputId] = input;

        // The SAME gate the UI runs (expiry, numerical inputs, delivery feasibility, worker rules,
        // budget), then the SAME execution; the reason for any refusal is reported verbatim so the
        // gym/router never has to guess (it used to say "insufficient budget" for every failure).
        if (!ValidateBeforeConfirm(task, choice, out failReason))
        {
            failReason = $"Choice {choiceId} on task {taskId} rejected: {failReason}";
            return false;
        }
        string why;
        bool ok = CompleteTaskAction(out why);
        if (!ok)
            failReason = $"Choice {choiceId} on task {taskId} rejected: {why}";
        return ok;
    }

    // Returns people MOVED for an immediate population relocation (0 = nothing moved → caller must
    // NOT mark the task fulfilled, B1). Returns -1 for food/other/deferred ("not gated, proceed").
    int ExecuteGeneratorDelivery(AgentChoice choice, bool immediate)
    {
        // Multi-destination deliveries (e.g. "Send to casework site") use a dedicated path that
        // honors destinationBuilding. It now reports whether anything was queued or moved, so a
        // choice that could do nothing is refused instead of charged (BUG_REPORTS B13).
        switch (choice.deliveryCargoType)
        {
            case ResourceType.FoodPacks:
                // FoodDeliveryHandler drains kitchens itself and resolves population-based
                // quantities; the multi-delivery flag on the food assets must not divert it.
                return ExecuteFoodDelivery(choice, immediate) ? -1 : 0;
            case ResourceType.Population:
                return ExecuteClientRelocation(choice, immediate);
            default:
                if (choice.enableMultipleDeliveries)
                    return ExecuteMultipleDeliveries(choice) > 0 ? -1 : 0;
                // Fallback for any other cargo type: single source→destination delivery
                return ExecuteFallbackDelivery(choice, immediate) ? -1 : 0;
        }
    }


    // FOOD DELIVERY via FoodDeliveryHandler
// FOOD DELIVERY via FoodDeliveryHandler. Returns whether it actually succeeded.
    // bool ExecuteFoodDelivery(AgentChoice choice, bool immediate)
    // {
    //     if (FoodDeliveryHandler.Instance == null)
    //     {
    //         Debug.LogError("[TaskDetailUI] FoodDeliveryHandler not found in scene");
    //         return false;
    //     }

    //     if (immediate)
    //     {
    //         FoodDeliveryHandler.Instance.ExecuteImmediate(currentTask, choice.deliveryQuantity);
    //         return true;
    //     }
    //     else
    //     {
    //         bool success = FoodDeliveryHandler.Instance.Execute(currentTask, choice.deliveryQuantity);
    //         if (!success)
    //             ShowAgentErrorMessage("Could not queue food delivery — check kitchen stock and vehicle availability.");
    //         return success;
    //     }
    // }
    // Inside TaskDetailUI.cs
// bool ExecuteFoodDelivery(AgentChoice choice, bool immediate)
// {
//     if (FoodDeliveryHandler.Instance == null)
//     {
//         Debug.LogError("[TaskDetailUI] FoodDeliveryHandler not found. Falling back to standard delivery.");
//         ExecuteFallbackDelivery(choice, immediate); // Use the reliable fallback
//         return true;
//     }

//     if (immediate)
//     {
//         FoodDeliveryHandler.Instance.ExecuteImmediate(currentTask, choice.deliveryQuantity);
//         return true;
//     }
//     else
//     {
//         // CRITICAL: We need to make sure the deliveries created by the handler 
//         // are actually linked to currentTask. 
//         bool success = FoodDeliveryHandler.Instance.Execute(currentTask, choice.deliveryQuantity);
        
//         if (!success)
//         {
//             ShowAgentErrorMessage("Could not queue food delivery. Check kitchen stock and vehicles.");
//             return false;
//         }

//         return true;
//     }
// }

    // CLIENT RELOCATION via ClientRelocationHandler.
    // Returns people moved (immediate), or -1 for the deferred / not-gated path (completion gated later).
    int ExecuteClientRelocation(AgentChoice choice, bool immediate)
    {
        // Non-shelter SpecificBuilding destinations (e.g. CaseworkSite) use the fallback path.
        // Shelter stays in ClientRelocationHandler so it can distribute across multiple shelters.
        if (choice.destinationType == DeliveryDestinationType.SpecificBuilding
            && choice.destinationBuilding != BuildingType.Shelter)
        {
            return ExecuteFallbackDelivery(choice, immediate) ? -1 : 0;
        }

        if (ClientRelocationHandler.Instance == null)
        {
            Debug.LogError("[TaskDetailUI] ClientRelocationHandler not found in scene");
            return -1;
        }

        bool toShelter = choice.destinationType != DeliveryDestinationType.SpecificPrebuilt
                    || choice.destinationPrebuilt != PrebuiltBuildingType.Motel;
        bool toMotel   = choice.destinationType == DeliveryDestinationType.SpecificPrebuilt
                    && choice.destinationPrebuilt == PrebuiltBuildingType.Motel;
        if (!toShelter && !toMotel) { toShelter = true; toMotel = true; }

        if (immediate)
        {
            int moved = ClientRelocationHandler.Instance.ExecuteImmediate(
                currentTask, choice.deliveryQuantity, toShelter, toMotel);
            if (moved <= 0)
                ShowAgentErrorMessage("No shelter/motel space available — no one could be relocated.");
            return moved;
        }
        else
        {
            bool success = ClientRelocationHandler.Instance.Execute(
                currentTask, choice.deliveryQuantity, toShelter, toMotel);
            // if (!success)
            //     ShowAgentErrorMessage("Could not queue client relocation — check shelter/motel capacity.");
            // return -1;
            if (!success) {
            ShowAgentErrorMessage("No shelter or motel space available to receive the population — the relocation could not be scheduled.");
            return 0; // Return 0 to indicate failure
        }
        
        // ADD THIS LINE:
        TaskSystem.Instance.SetTaskInProgress(currentTask);
        return -1; // Return -1 to indicate "success/not gated"
        }
    }

    // FALLBACK for other cargo types
// FALLBACK for other cargo types. Returns whether it actually succeeded.
    bool ExecuteFallbackDelivery(AgentChoice choice, bool immediate)
    {
        MonoBehaviour triggeringFacility = TaskSystem.Instance.FindTriggeringFacility(currentTask);
        MonoBehaviour source      = TaskSystem.Instance.DetermineChoiceDeliverySource(choice, triggeringFacility);
        MonoBehaviour destination = TaskSystem.Instance.DetermineChoiceDeliveryDestination(choice, triggeringFacility);

        if (source == null || destination == null)
        {
            Debug.LogError($"[TaskDetailUI] Fallback delivery: could not resolve source/destination for {choice.deliveryCargoType}");
            ShowAgentErrorMessage($"Could not queue delivery — no available {choice.deliveryCargoType} route.");
            return false;
        }

        if (immediate)
        {
            return ExecuteImmediateDeliveryBetween(source, destination, choice.deliveryCargoType, choice.deliveryQuantity) > 0;
        }
        else if (choice.deliveryCargoType == ResourceType.Population)
        {
            // Clients (e.g. Shelter -> CaseworkSite) relocate on their own — no Vehicle involved.
            bool success = ClientRelocationHandler.Instance != null
                && ClientRelocationHandler.Instance.ExecuteToSpecificDestination(currentTask, source, destination, choice.deliveryQuantity);
            if (!success)
                ShowAgentErrorMessage("Could not relocate clients — check destination capacity.");
            return success;
        }
        else
        {
            DeliverySystem ds = DeliverySystem.Instance ?? FindObjectOfType<DeliverySystem>();
            if (ds == null) return false;
            List<DeliveryTask> deliveries = ds.CreateDeliveryTask(source, destination, choice.deliveryCargoType, choice.deliveryQuantity, 3);
            TaskSystem.Instance.LinkDeliveriesToTask(currentTask, deliveries);
            if (deliveries.Count > 0)
            {
                TaskSystem.Instance.SetTaskInProgress(currentTask);
                return true;
            }
            ShowAgentErrorMessage($"Could not queue delivery — no vehicle/route available for {choice.deliveryCargoType}.");
            return false;
        }
    }

    // ---------NUMERICAL INPUT VALIDATION ---------
    /// <summary>
    /// Validates all numerical inputs in the current task
    /// </summary>
    // ---------ONE VALIDATION FOR EVERY ENTRY POINT (UI confirm, officer confirm, headless/agent) ---------
    /// <summary>
    /// Everything that must hold before a choice takes effect. The UI button, the officer
    /// conversation confirm and the headless/agent path all call this, so an agent can never do
    /// what a human is refused (BUG_REPORTS B11, B12, B16).
    /// </summary>
    bool ValidateBeforeConfirm(GameTask task, AgentChoice choice, out string errorMessage)
    {
        errorMessage = null;
        if (task == null) { errorMessage = "No task selected."; return false; }
        if (task.isExpired) { errorMessage = "This task has expired and can no longer be completed."; return false; }
        // From origin/main-bugfixes 2238e799: a task offering choices cannot be confirmed
        // until one is selected. Folded INTO this helper rather than inlined at each call
        // site (as upstream does) so all three confirm paths get it -- the manual path, the
        // agent path, and the programmatic one -- instead of only the two upstream patched.
        if (task.agentChoices != null && task.agentChoices.Count > 0 && choice == null)
        {
            errorMessage = "Please select a choice before confirming.";
            return false;
        }
        string numError;
        if (!ValidateNumericalInputs(out numError)) { errorMessage = numError; return false; }
        if (choice != null && (choice.triggersDelivery || choice.immediateDelivery || choice.enableMultipleDeliveries))
        {
            string delivError;
            if (!ValidateChoiceDelivery(choice, out delivError)) { errorMessage = delivError; return false; }
        }
        if (WorkerAssignmentHandler.Instance != null)
        {
            string workerError;
            if (!WorkerAssignmentHandler.Instance.ValidateForConfirm(task, out workerError)) { errorMessage = workerError; return false; }
        }
        int cost = GetChoiceImmediateCost(choice);
        if (cost > 0 && SatisfactionAndBudget.Instance != null && !SatisfactionAndBudget.Instance.WouldAllowSpend(cost))
        {
            errorMessage = $"Insufficient budget: this option costs ${cost:N0} and the budget is ${SatisfactionAndBudget.Instance.GetCurrentBudget():N0}.";
            return false;
        }
        return true;
    }

    /// <summary>Stock a source can still give away: what it holds minus what is already promised outbound.</summary>
    int EffectiveStock(MonoBehaviour building, ResourceType cargo, DeliverySystem ds)
    {
        BuildingResourceStorage storage = GetBuildingResourceStorage(building);
        if (storage == null) return 0;
        int reserved = ds != null ? ds.GetReservedOutgoingQuantity(building, cargo) : 0;
        return Mathf.Max(0, storage.GetResourceAmount(cargo) - reserved);
    }

    /// <summary>Room a destination will still have once everything already inbound has arrived.</summary>
    int EffectiveSpace(MonoBehaviour building, ResourceType cargo, DeliverySystem ds)
    {
        BuildingResourceStorage storage = GetBuildingResourceStorage(building);
        if (storage == null) return 0;
        int reserved = ds != null ? ds.GetReservedIncomingQuantity(building, cargo) : 0;
        return Mathf.Max(0, storage.GetAvailableSpace(cargo) - reserved);
    }

    /// <summary>
    /// Validates the task's numerical inputs from the task DATA (the UI keeps
    /// AgentNumericalInput.currentValue in sync), so the check also runs when no panel exists.
    /// </summary>
    bool ValidateNumericalInputs(out string errorMessage)
    {
        errorMessage = "";
        if (currentTask == null || currentTask.numericalInputs == null || currentTask.numericalInputs.Count == 0)
            return true; // No numerical inputs to validate

        foreach (AgentNumericalInput input in currentTask.numericalInputs)
        {
            if (input == null) continue;
            if (input.maxValue > input.minValue && (input.currentValue < input.minValue || input.currentValue > input.maxValue))
            {
                errorMessage = $"{input.inputLabel}: {input.currentValue} is outside the allowed range {input.minValue}-{input.maxValue}.";
                return false;
            }
            string validationError = ValidateSpecificInput(input.inputType, input.currentValue);
            if (!string.IsNullOrEmpty(validationError))
            {
                errorMessage = validationError;
                return false;
            }
        }

        string crossValidationError = ValidateCrossInputConstraints();
        if (!string.IsNullOrEmpty(crossValidationError))
        {
            errorMessage = crossValidationError;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Type-specific validation for individual inputs
    /// </summary>
    string ValidateSpecificInput(NumericalInputType type, int value)
    {
        switch (type)
        {
            case NumericalInputType.Budget:
                // Check if budget value exceeds available funds
                if (SatisfactionAndBudget.Instance != null)
                {
                    int availableBudget = SatisfactionAndBudget.Instance.GetCurrentBudget();
                    if (!SatisfactionAndBudget.Instance.WouldAllowSpend(value))
                    {
                        return $"Insufficient budget. You requested ${value:N0} but only have ${availableBudget:N0} available.";
                    }
                }
                break;
                
            case NumericalInputType.UntrainedWorkers:
                if (WorkerSystem.Instance != null)
                {
                    // For REQUEST tasks, validate budget using untrained cost
                    if (currentTask.taskTitle.Contains("Request"))
                    {
                        if (WorkerRequestSystem.Instance != null && SatisfactionAndBudget.Instance != null)
                        {
                            int costPerWorker = WorkerRequestSystem.Instance.untrainedWorkerCost;
                            int totalCost = value * costPerWorker;
                            int availableBudget = SatisfactionAndBudget.Instance.GetCurrentBudget();

                            //if (totalCost > availableBudget)
                            //{
                            //    return $"Insufficient budget. Requesting {value} workers costs ${totalCost:N0} but you only have ${availableBudget:N0}.";
                            //}
                        }
                        break;
                    }
                    
                    // For TRAINING tasks, validate budget AND worker availability
                    if (currentTask.taskTitle.Contains("Training"))
                    {
                        // Check budget first
                        if (WorkerTrainingSystem.Instance != null && SatisfactionAndBudget.Instance != null)
                        {
                            int totalCost = value * WorkerTrainingSystem.Instance.trainingCostPerWorker;
                            int availableBudget = SatisfactionAndBudget.Instance.GetCurrentBudget();
                            
                            //if (totalCost > availableBudget)
                            //{
                            //    return $"Insufficient budget. Training {value} workers costs ${totalCost:N0} but you only have ${availableBudget:N0}.";
                            //}
                        }
                        
                        // Then check worker availability
                        int availableUntrained = WorkerSystem.Instance.GetAvailableUntrainedWorkers();
                        if (value > availableUntrained)
                        {
                            return $"Not enough untrained workers. You requested {value} but only have {availableUntrained} available.";
                        }
                        break;
                    }
                    
                    // Default: validate worker availability
                    int availableUntrainedDefault = WorkerSystem.Instance.GetAvailableUntrainedWorkers();
                    if (value > availableUntrainedDefault)
                    {
                        return $"Not enough untrained workers. You requested {value} but only have {availableUntrainedDefault} available.";
                    }
                }
                break;
                
            case NumericalInputType.TrainedWorkers:
                if (WorkerSystem.Instance != null)
                {
                    // For REQUEST tasks, validate budget instead of worker availability
                    if (currentTask.taskTitle.Contains("Request"))
                    {
                        if (WorkerRequestSystem.Instance != null && SatisfactionAndBudget.Instance != null)
                        {
                            int costPerWorker = WorkerRequestSystem.Instance.trainedWorkerCost;
                            int totalCost = value * costPerWorker;
                            int availableBudget = SatisfactionAndBudget.Instance.GetCurrentBudget();
                            
                            //if (totalCost > availableBudget)
                            //{
                            //    return $"Insufficient budget. Requesting {value} workers costs ${totalCost:N0} but you only have ${availableBudget:N0}.";
                            //}
                        }
                        break;
                    }
                    
                    // For TRAINING tasks, validate worker availability
                    int availableTrained = WorkerSystem.Instance.GetAvailableTrainedWorkers();
                    if (value > availableTrained)
                    {
                        return $"Not enough trained workers. You requested {value} but only have {availableTrained} available.";
                    }
                }
                break;
                
            case NumericalInputType.FoodPacks:
                // Check food pack availability
                if (value > 0)
                {
                    // Find kitchen with available meals
                    Building[] kitchens = FindObjectsOfType<Building>()
                        .Where(b => b.GetBuildingType() == BuildingType.Kitchen && b.IsOperational())
                        .ToArray();
                        
                    int totalAvailable = 0;
                    foreach (Building kitchen in kitchens)
                    {
                        BuildingResourceStorage storage = kitchen.GetComponent<BuildingResourceStorage>();
                        if (storage != null)
                            totalAvailable += storage.GetResourceAmount(ResourceType.FoodPacks);
                    }
                    
                    if (value > totalAvailable)
                    {
                        return $"Not enough meals. You requested {value} but only have {totalAvailable} available across all kitchens.";
                    }
                }
                break;
                
            case NumericalInputType.Clients:
                // Check if processing more clients than available
                if (value > 0)
                {
                    // Context-specific validation based on task
                    if (currentTask.taskTitle.Contains("Training"))
                    {
                        // For training tasks, clients might mean workers to train
                        return ValidateTrainingCapacity(value);
                    }
                    else if (currentTask.taskTitle.Contains("Transport") || currentTask.taskTitle.Contains("Evacuation"))
                    {
                        // For transport tasks, validate vehicle capacity
                        return ValidateTransportCapacity(value);
                    }
                }
                break;
        }
        
        return ""; // No error
    }

    /// <summary>
    /// Validate constraints across multiple inputs
    /// </summary>
    string ValidateCrossInputConstraints()
    {
        int totalBudgetRequested = 0;
        int totalWorkersRequested = 0;
        if (currentTask != null && currentTask.numericalInputs != null)
        {
            foreach (AgentNumericalInput input in currentTask.numericalInputs)
            {
                if (input == null) continue;
                if (input.inputType == NumericalInputType.Budget)
                    totalBudgetRequested += input.currentValue;
                else if (input.inputType == NumericalInputType.UntrainedWorkers || input.inputType == NumericalInputType.TrainedWorkers)
                    totalWorkersRequested += input.currentValue;
            }
        }

        // Check total budget
        if (totalBudgetRequested > 0 && SatisfactionAndBudget.Instance != null)
        {
            int availableBudget = SatisfactionAndBudget.Instance.GetCurrentBudget();
            if (!SatisfactionAndBudget.Instance.WouldAllowSpend(totalBudgetRequested))
            {
                return $"Total budget requested (${totalBudgetRequested:N0}) exceeds available funds (${availableBudget:N0}).";
            }
        }
        return "";
    }

    /// <summary>
    /// Validate training capacity for worker training tasks
    /// </summary>
    string ValidateTrainingCapacity(int workersToTrain)
    {
        if (WorkerTrainingSystem.Instance != null)
        {
            int trainingCost = WorkerTrainingSystem.Instance.trainingCostPerWorker * workersToTrain;
            
            if (SatisfactionAndBudget.Instance != null)
            {
                int availableBudget = SatisfactionAndBudget.Instance.GetCurrentBudget();
                if (!SatisfactionAndBudget.Instance.WouldAllowSpend(trainingCost))
                {
                    return $"Training {workersToTrain} workers costs ${trainingCost:N0}, but you only have ${availableBudget:N0} available.";
                }
            }
        }
        return "";
    }

    /// <summary>
    /// Validate transport capacity for evacuation tasks
    /// </summary>
    string ValidateTransportCapacity(int peopleToTransport)
    {
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>()
            .Where(v => v.GetCurrentStatus() != VehicleStatus.Damaged)
            .ToArray();
        
        if (vehicles.Length == 0)
        {
            return "No available vehicles for transport.";
        }
        
        int maxCapacity = vehicles.Max(v => v.GetMaxCapacity());
        if (peopleToTransport > maxCapacity)
        {
            return $"Cannot transport {peopleToTransport} people. Maximum vehicle capacity is {maxCapacity}.";
        }
        
        return "";
    }

    //  ---------CHOICE DELIVERY VALIDATION ---------
    bool ValidateChoiceDelivery(AgentChoice choice, out string errorMessage)
    {
        // Cargo decides the path (main-bugfixes): food always goes through FoodDeliveryHandler (it
        // drains several kitchens itself and understands population-based quantities), people
        // always relocate on foot through ClientRelocationHandler. The multi-delivery flag only
        // routes other cargo.
        if (choice != null && choice.enableMultipleDeliveries
            && choice.deliveryCargoType != ResourceType.FoodPacks && choice.deliveryCargoType != ResourceType.Population)
            return ValidateMultipleDeliveries(choice, TaskSystem.Instance.FindTriggeringFacility(currentTask), out errorMessage);
        return ValidateChoiceDelivery(currentTask, choice, out errorMessage);
    }

    public static bool ValidateChoiceDelivery(GameTask task, AgentChoice choice, out string errorMessage)
    {
        errorMessage = "";

        if (!choice.triggersDelivery && !choice.immediateDelivery)
            return true;

        // Immediate delivery bypasses vehicle/reservation logic — just check resources exist
        if (choice.immediateDelivery)
        {
            switch (choice.deliveryCargoType)
            {
                case ResourceType.FoodPacks:
                    // Same validation as queued delivery (including PopulationBased quantity
                    // resolution) — immediate food only differs in bypassing the vehicle at
                    // execution time, not in how much food is actually needed.
                    return FoodDeliveryHandler.Instance != null
                        && FoodDeliveryHandler.Instance.CanExecute(task, choice, out errorMessage);


                case ResourceType.Population:
                {
                    // Non-shelter SpecificBuilding (e.g. CaseworkSite): ExecuteClientRelocation
                    // already routes this to the fallback delivery path (the same one queued
                    // delivery uses) regardless of immediate/queued, so validation must match —
                    // otherwise this would validate against Shelter/Motel capacity while execution
                    // actually targets a completely different building.
                    if (choice.destinationType == DeliveryDestinationType.SpecificBuilding
                        && choice.destinationBuilding != BuildingType.Shelter)
                    {
                        return ValidateSpecificBuildingPopulationDestination(task, choice, out errorMessage);
                    }

                    bool toShelter = choice.destinationType != DeliveryDestinationType.SpecificPrebuilt
                                || choice.destinationPrebuilt != PrebuiltBuildingType.Motel;
                    bool toMotel   = choice.destinationType == DeliveryDestinationType.SpecificPrebuilt
                                && choice.destinationPrebuilt == PrebuiltBuildingType.Motel;
                    if (!toShelter && !toMotel) { toShelter = true; toMotel = true; }
                    return ClientRelocationHandler.Instance != null
                        && ClientRelocationHandler.Instance.CanExecute(
                            task, choice.deliveryQuantity, toShelter, toMotel,
                            out errorMessage, requiresPathCheck: false);
                }

                default:
                    return true; // Let it attempt, fail gracefully at execution
            }
        }

        // Normal (vehicle) delivery — use generators
        switch (choice.deliveryCargoType)
        {
            case ResourceType.FoodPacks:
                return FoodDeliveryHandler.Instance != null
                    && FoodDeliveryHandler.Instance.CanExecute(task, choice, out errorMessage);

            case ResourceType.Population:
                // Non-shelter SpecificBuilding (e.g. CaseworkSite) shares one validation helper
                // with the immediate-delivery branch above, so the two paths can't drift apart —
                // that's exactly what happened before this was factored out (immediate delivery
                // had no CaseworkSite case at all and silently validated against Shelter/Motel
                // capacity instead of the real target). SpecificBuilding+Shelter falls through to
                // ClientRelocationHandler validation below, which already checks all of this for
                // Shelter/Motel destinations.
                if (choice.destinationType == DeliveryDestinationType.SpecificBuilding
                    && choice.destinationBuilding != BuildingType.Shelter)
                {
                    return ValidateSpecificBuildingPopulationDestination(task, choice, out errorMessage);
                }
                bool toShelter = choice.destinationType != DeliveryDestinationType.SpecificPrebuilt
                            || choice.destinationPrebuilt != PrebuiltBuildingType.Motel;
                bool toMotel   = choice.destinationType == DeliveryDestinationType.SpecificPrebuilt
                            && choice.destinationPrebuilt == PrebuiltBuildingType.Motel;
                if (!toShelter && !toMotel) { toShelter = true; toMotel = true; }
                return ClientRelocationHandler.Instance != null
                    && ClientRelocationHandler.Instance.CanExecute(
                        task, choice.deliveryQuantity, toShelter, toMotel, out errorMessage);

            default:
                bool hasVehicle = UnityEngine.Object.FindObjectsOfType<Vehicle>()
                    .Any(v => v.GetAllowedCargoTypes().Contains(choice.deliveryCargoType)
                        && v.GetCurrentStatus() != VehicleStatus.Damaged);
                if (!hasVehicle) errorMessage = $"No undamaged vehicle for {choice.deliveryCargoType}";
                return hasVehicle;
        }
    }

    /// <summary>
    /// Validates a Population choice whose destination is a specific, non-Shelter building type
    /// (currently only CaseworkSite) — used by both the immediate and queued branches of
    /// ValidateChoiceDelivery above so they can't independently drift out of sync with each other
    /// or with what ClientRelocationHandler.ExecuteToSpecificDestination actually does. Checks, in
    /// order: at least one operational building of that type exists; the source facility still has
    /// clients to send; and at least one candidate has enough effective capacity (accounting for
    /// reserved-inbound deliveries and in-flight self-walk relocations, same as
    /// TaskSystem.IsValidDeliveryDestination and Shelter/Motel's GetDestinationsSorted) for the
    /// full quantity this choice would actually try to send.
    /// </summary>
    static bool ValidateSpecificBuildingPopulationDestination(GameTask task, AgentChoice choice, out string errorMessage)
    {
        errorMessage = "";

        Building[] candidates = UnityEngine.Object.FindObjectsOfType<Building>()
            .Where(b => b.GetBuildingType() == choice.destinationBuilding && b.IsOperational())
            .ToArray();
        if (candidates.Length == 0)
        {
            errorMessage = $"There is no {choice.destinationBuilding} currently built on the map. Build one first to use this option.";
            return false;
        }

        MonoBehaviour source = TaskSystem.Instance?.FindTriggeringFacility(task);
        int available = source != null && ClientRelocationHandler.Instance != null
            ? ClientRelocationHandler.Instance.GetPopulation(source) : 0;
        if (available <= 0)
        {
            errorMessage = $"No clients at {(source != null ? source.name : task.affectedFacility)} to relocate";
            return false;
        }

        // How many clients this choice would actually try to send — mirrors
        // ClientRelocationHandler.ExecuteToSpecificDestination's own toSend calculation exactly,
        // so validation agrees with what execution will attempt.
        int requestedQuantity = choice.deliveryQuantity > 0
            ? Mathf.Min(choice.deliveryQuantity, available) : available;

        // Must have room for the FULL requested quantity — ExecuteToSpecificDestination sends to
        // only one destination with no splitting, so partial space isn't good enough.
        bool hasEnoughSpace = ClientRelocationHandler.Instance != null
            && candidates.Any(b => ClientRelocationHandler.Instance.GetEffectiveSpace(b) >= requestedQuantity);
        if (!hasEnoughSpace)
        {
            errorMessage = $"{choice.destinationBuilding} doesn't have enough available capacity to receive {requestedQuantity} clients.";
            return false;
        }

        return true;
    }

    // Helper — reads population from the task's triggering facility
    int GetPopulationAtFacility(GameTask task)
    {
        MonoBehaviour facility = TaskSystem.Instance.FindTriggeringFacility(task);
        if (facility == null) return 0;
        PrebuiltBuilding pb = facility.GetComponent<PrebuiltBuilding>();
        if (pb != null) return pb.GetCurrentPopulation();
        BuildingResourceStorage storage = facility.GetComponent<BuildingResourceStorage>()
            ?? facility.GetComponent<Building>()?.GetComponent<BuildingResourceStorage>();
        return storage?.GetResourceAmount(ResourceType.Population) ?? 0;
    }

    /// <summary>
    /// Validate multiple delivery scenarios
    /// </summary>
    bool ValidateMultipleDeliveries(AgentChoice choice, MonoBehaviour triggeringFacility, out string errorMessage)
    {
        errorMessage = "";

        switch (choice.multiDeliveryType)
        {
            case AgentChoice.MultiDeliveryType.SingleSourceMultiDest:
                return ValidateSingleSourceMultiDest(choice, triggeringFacility, out errorMessage);

            case AgentChoice.MultiDeliveryType.MultiSourceSingleDest:
                return ValidateMultiSourceSingleDest(choice, triggeringFacility, out errorMessage);

            case AgentChoice.MultiDeliveryType.MultiSourceMultiDest:
                return ValidateMultiSourceMultiDest(choice, triggeringFacility, out errorMessage);

            default:
                errorMessage = "Unknown multi-delivery type";
                return false;
        }
    }

    /// <summary>
    /// Validate single source to multiple destinations
    /// </summary>
    bool ValidateSingleSourceMultiDest(AgentChoice choice, MonoBehaviour triggeringFacility, out string errorMessage)
    {
        errorMessage = "";

        // Find source
        MonoBehaviour source = DetermineChoiceDeliverySource(choice, triggeringFacility);
        if (source == null)
        {
            errorMessage = $"No suitable source found for {choice.deliveryCargoType}";
            return false;
        }

        // Find multiple destinations - EXCLUDE SOURCE
        List<MonoBehaviour> destinations = FindMultipleDestinations(choice, triggeringFacility, 3)
            .Where(dest => dest != source) // NEW: Exclude source from destinations
            .ToList();

        if (destinations.Count == 0)
        {
            errorMessage = $"No suitable destinations found for {choice.deliveryCargoType} (excluding source)";
            return false;
        }

        // Check source has enough resources
        int totalAvailable = CalculateDeliveryQuantity(choice, source);
        if (totalAvailable <= 0)
        {
            errorMessage = $"No resources available at {source.name}";
            return false;
        }

        // Check total destination capacity
        int totalDestinationSpace = 0;
        foreach (MonoBehaviour dest in destinations)
        {
            totalDestinationSpace += GetAvailableSpace(dest, choice.deliveryCargoType);
        }

        if (totalDestinationSpace < totalAvailable)
        {
            errorMessage = $"Insufficient total space at destinations. Available: {totalDestinationSpace}, Required: {totalAvailable}";
            return false;
        }

        // Vehicle check only for normal delivery (not immediate)
        if (choice.triggersDelivery && !choice.immediateDelivery)
        {
            // NEW: Only check if there's one available vehicle, since deliveries are queued
            Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
            bool hasAnyCapableVehicle = false;

            foreach (Vehicle vehicle in vehicles)
            {
                if (vehicle.GetAllowedCargoTypes().Contains(choice.deliveryCargoType) &&
                    vehicle.GetCurrentStatus() != VehicleStatus.Damaged)
                {
                    hasAnyCapableVehicle = true;
                    break;
                }
            }

            if (!hasAnyCapableVehicle)
            {
                errorMessage = $"No available undamaged vehicles to transport {choice.deliveryCargoType}";
                return false;
            }

            if (showDebugInfo)
                Debug.Log($"Vehicle validation passed: Found capable vehicles for queued delivery");
        }

        if (showDebugInfo)
            Debug.Log($"Multi-dest validation: {source.name} → {destinations.Count} destinations ({totalAvailable} resources, {totalDestinationSpace} space)");

        return true;
    }

    /// <summary>
    /// Validate multiple sources to single destination  
    /// </summary>
    bool ValidateMultiSourceSingleDest(AgentChoice choice, MonoBehaviour triggeringFacility, out string errorMessage)
    {
        errorMessage = "";

        // Find destination
        MonoBehaviour destination = DetermineChoiceDeliveryDestination(choice, triggeringFacility);
        if (destination == null)
        {
            errorMessage = $"No suitable destination found for {choice.deliveryCargoType}";
            return false;
        }

        // Find multiple sources - EXCLUDE DESTINATION
        List<MonoBehaviour> sources = FindMultipleSources(choice, triggeringFacility, 3)
            .Where(source => source != destination) // NEW: Exclude destination from sources
            .ToList();

        if (sources.Count == 0)
        {
            errorMessage = $"No suitable sources found for {choice.deliveryCargoType} (excluding destination)";
            return false;
        }

        // Check total available resources
        int totalAvailable = 0;
        int sourcesWithResources = 0;
        foreach (MonoBehaviour source in sources)
        {
            int available = CalculateDeliveryQuantity(choice, source);
            if (available > 0)
            {
                totalAvailable += available;
                sourcesWithResources++;
            }
        }

        if (totalAvailable <= 0)
        {
            errorMessage = $"No resources available at any of the {sources.Count} sources";
            return false;
        }

        // Check destination capacity
        int destinationSpace = GetAvailableSpace(destination, choice.deliveryCargoType);
        if (destinationSpace < totalAvailable)
        {
            errorMessage = $"Insufficient space at {destination.name}. Available: {destinationSpace}, Total incoming: {totalAvailable}";
            return false;
        }

        // Vehicle check for normal delivery
        if (choice.triggersDelivery && !choice.immediateDelivery)
        {
            Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
            bool hasAnyCapableVehicle = false;

            foreach (Vehicle vehicle in vehicles)
            {
                if (vehicle.GetAllowedCargoTypes().Contains(choice.deliveryCargoType) &&
                    vehicle.GetCurrentStatus() != VehicleStatus.Damaged)
                {
                    hasAnyCapableVehicle = true;
                    break;
                }
            }

            if (!hasAnyCapableVehicle)
            {
                errorMessage = $"No available undamaged vehicles to transport {choice.deliveryCargoType}";
                return false;
            }
        }


        if (showDebugInfo)
            Debug.Log($"Multi-source validation: {sourcesWithResources} sources → {destination.name} ({totalAvailable} resources, {destinationSpace} space)");

        return true;
    }

    /// <summary>
    /// Validate multiple sources to multiple destinations
    /// </summary>
    bool ValidateMultiSourceMultiDest(AgentChoice choice, MonoBehaviour triggeringFacility, out string errorMessage)
    {
        errorMessage = "";

        // Find sources and destinations
        List<MonoBehaviour> allSources = FindMultipleSources(choice, triggeringFacility, 3);
        List<MonoBehaviour> allDestinations = FindMultipleDestinations(choice, triggeringFacility, 3);

        // Remove overlapping facilities
        List<MonoBehaviour> sources = allSources.Where(s => !allDestinations.Contains(s)).ToList();
        List<MonoBehaviour> destinations = allDestinations.Where(d => !allSources.Contains(d)).ToList();

        if (sources.Count == 0)
        {
            errorMessage = $"No suitable sources found for {choice.deliveryCargoType} (excluding destinations)";
            return false;
        }

        if (destinations.Count == 0)
        {
            errorMessage = $"No suitable destinations found for {choice.deliveryCargoType} (excluding sources)";
            return false;
        }

        // Check total resources vs total capacity
        int totalAvailable = 0;
        int sourcesWithResources = 0;
        foreach (MonoBehaviour source in sources)
        {
            int available = CalculateDeliveryQuantity(choice, source);
            if (available > 0)
            {
                totalAvailable += available;
                sourcesWithResources++;
            }
        }

        if (totalAvailable <= 0)
        {
            errorMessage = $"No resources available at any sources";
            return false;
        }

        int totalDestinationSpace = 0;
        foreach (MonoBehaviour dest in destinations)
        {
            totalDestinationSpace += GetAvailableSpace(dest, choice.deliveryCargoType);
        }

        if (totalDestinationSpace < totalAvailable)
        {
            errorMessage = $"Insufficient total destination space. Available: {totalDestinationSpace}, Required: {totalAvailable}";
            return false;
        }

        // Vehicle check for normal delivery
        if (choice.triggersDelivery && !choice.immediateDelivery)
        {
            Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
            bool hasAnyCapableVehicle = false;

            foreach (Vehicle vehicle in vehicles)
            {
                if (vehicle.GetAllowedCargoTypes().Contains(choice.deliveryCargoType) &&
                    vehicle.GetCurrentStatus() != VehicleStatus.Damaged)
                {
                    hasAnyCapableVehicle = true;
                    break;
                }
            }

            if (!hasAnyCapableVehicle)
            {
                errorMessage = $"No available undamaged vehicles to transport {choice.deliveryCargoType}";
                return false;
            }
        }

        if (showDebugInfo)
            Debug.Log($"Multi-to-multi validation: {sourcesWithResources} sources → {destinations.Count} destinations ({totalAvailable} resources, {totalDestinationSpace} space)");

        return true;
    }

    /// <summary>
    /// Validate vehicle availability for single delivery
    /// </summary>
    bool ValidateVehicleAvailability(AgentChoice choice, MonoBehaviour source, MonoBehaviour destination, out string errorMessage)
    {
        errorMessage = "";

        // Get route analysis
        PathfindingSystem pathfinder = FindObjectOfType<PathfindingSystem>();
        if (pathfinder != null)
        {
            PathAnalysis analysis = pathfinder.AnalyzePath(source.transform.position, destination.transform.position);
            DeliveryTimeEstimate estimate = pathfinder.EstimateDeliveryTime(source.transform.position, destination.transform.position);

            if (!estimate.pathExists)
            {
                if (estimate.isFloodBlocked)
                {
                    errorMessage = $"All routes from {source.name} to {destination.name} are blocked by flood";
                }
                else
                {
                    errorMessage = $"No route available from {source.name} to {destination.name}";
                }
                return false;
            }

            // Show route information
            if (analysis.isFloodAffected && analysis.hasAlternativeRoute)
            {
                if (showDebugInfo)
                    Debug.Log($"Choice uses alternative route (+{analysis.routeLengthDifference} tiles) due to flood");
            }
        }

        // Check vehicle availability
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
        bool hasCapableVehicle = false;

        foreach (Vehicle vehicle in vehicles)
        {
            if (vehicle.GetAllowedCargoTypes().Contains(choice.deliveryCargoType) &&
                vehicle.GetMaxCapacity() >= Mathf.Min(choice.deliveryQuantity, 10) &&
                vehicle.GetCurrentStatus() != VehicleStatus.Damaged)
            {
                hasCapableVehicle = true;
                break;
            }
        }

        if (!hasCapableVehicle)
        {
            errorMessage = $"No available undamaged vehicle to transport {choice.deliveryCargoType}";
            return false;
        }

        return true;
    }

    // Helper methods for resource checking
    int GetAvailableResourceAmount(MonoBehaviour facility, ResourceType resourceType)
    {
        // Check Building
        Building building = facility.GetComponent<Building>();
        if (building != null)
        {
            BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();
            if (storage != null)
                return storage.GetResourceAmount(resourceType);
        }

        // Check PrebuiltBuilding
        PrebuiltBuilding prebuilt = facility.GetComponent<PrebuiltBuilding>();
        if (prebuilt != null)
        {
            if (resourceType == ResourceType.Population)
                return prebuilt.GetCurrentPopulation();

            BuildingResourceStorage storage = prebuilt.GetResourceStorage();
            if (storage != null)
                return storage.GetResourceAmount(resourceType);
        }

        return 0;
    }

    int GetAvailableSpace(MonoBehaviour facility, ResourceType resourceType)
    {
        // Check Building
        Building building = facility.GetComponent<Building>();
        if (building != null)
        {
            BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();
            if (storage != null)
                return storage.GetAvailableSpace(resourceType);
        }

        // Check PrebuiltBuilding
        PrebuiltBuilding prebuilt = facility.GetComponent<PrebuiltBuilding>();
        if (prebuilt != null)
        {
            if (resourceType == ResourceType.Population)
                return prebuilt.GetPopulationCapacity() - prebuilt.GetCurrentPopulation();

            BuildingResourceStorage storage = prebuilt.GetResourceStorage();
            if (storage != null)
                return storage.GetAvailableSpace(resourceType);
        }

        return 0;
    }

    void ShowAgentErrorMessage(string errorText)
    {
        // Headless / gym: don't render conversation UI (typing effects, message
        // prefabs, etc. NRE without an active panel). Log and bail so the error
        // path (e.g. a delivery that can't be sourced) fails gracefully.
        if (Application.isBatchMode || agentMessagePrefab == null || conversationContent == null)
        {
            GameLogPanel.Instance?.LogTaskEvent($"[TaskDetail] {errorText}");
            return;
        }

        // If the task panel is not visible (e.g. called via AgentConversationUI), fall back to a toast
        // (main-bugfixes UX). Runs after the headless guard above.
        // if (taskDetailPanel == null || !taskDetailPanel.activeInHierarchy)
        // {
        //     ToastManager.ShowToast(errorText, ToastType.Warning, true);
        //     if (showDebugInfo) Debug.Log($"ShowAgentErrorMessage (panel inactive, toast fallback): {errorText}");
        //     return;
        // }

        if (taskDetailPanel == null || !taskDetailPanel.activeInHierarchy 
        || Application.isBatchMode || agentMessagePrefab == null || conversationContent == null)
        {
            if (!Application.isBatchMode)
                ToastManager.ShowToast(errorText, ToastType.Warning, true);
            GameLogPanel.Instance?.LogTaskEvent($"[TaskDetail] {errorText}");
            return;
        }

        // Also surface it as a toast — a reliable channel independent of the inline message
        // (which can render blank if a TMP font asset is missing).
        ToastManager.ShowToast(errorText, ToastType.Warning, true);

        // Create a temporary agent message to show the error
        GameObject errorMessageItem = Instantiate(agentMessagePrefab, conversationContent);
        AgentMessageUI messageUI = errorMessageItem.GetComponent<AgentMessageUI>();

        if (messageUI != null)
        {
            Sprite errorAgentSprite = currentTask.taskOfficer switch
            {
                TaskOfficer.WorkforceService => TaskSystem.Instance.workforceServiceSprite,
                TaskOfficer.LodgingMassCare => TaskSystem.Instance.lodgingMassCareSprite,
                TaskOfficer.ExternalRelationship => TaskSystem.Instance.externalRelationshipSprite,
                TaskOfficer.FoodMassCare => TaskSystem.Instance.foodMassCareSprite,
                _ => TaskSystem.Instance.defaultAgentSprite
            };
            AgentMessage errorMessage = new AgentMessage(errorText, errorAgentSprite);
            errorMessage.useTypingEffect = false;
            messageUI.Initialize(errorMessage);
            messageUI.ShowFullMessage();

            // Make the error message visually distinct
            if (messageUI.messageText != null)
            {
                messageUI.messageText.color = Color.red;
            }
        }

        currentConversationItems.Add(errorMessageItem);
        ScrollToBottom();

        if (showDebugInfo)
            Debug.Log($"Showed error message: {errorText}");
    }
    //  -------------  END OF SECTION -------------------------------
    // -------------- SECTION: Multi delivery methods ---------------

    /// <summary>
    /// Execute immediate delivery between specific facilities
    /// </summary>
    /// <summary>Move cargo now, no vehicle. Returns what actually landed; overflow goes back to the source.</summary>
    int ExecuteImmediateDeliveryBetween(MonoBehaviour source, MonoBehaviour destination, ResourceType cargoType, int quantity)
    {
        BuildingResourceStorage sourceStorage = GetBuildingResourceStorage(source);
        BuildingResourceStorage destStorage = GetBuildingResourceStorage(destination);
        if (sourceStorage == null || destStorage == null || quantity <= 0)
            return 0;

        int actualRemoved = sourceStorage.RemoveResource(cargoType, quantity);
        int actualDelivered = destStorage.AddResource(cargoType, actualRemoved);
        if (actualDelivered < actualRemoved)
            sourceStorage.AddResource(cargoType, actualRemoved - actualDelivered);

        // Client tracking happens ONCE, in the tracker (arrival at a shelter/motel, processing
        // home at a casework site, leaving a tracked source). The two registrations that used
        // to live here double-counted community->shelter moves.
        if (cargoType == ResourceType.Population && ClientStayTracker.Instance != null && actualDelivered > 0)
            ClientStayTracker.Instance.HandleImmediateTransfer(source, destination, actualDelivered, currentTask);

        if (showDebugInfo)
            Debug.Log($"Immediate delivery: {actualDelivered} {cargoType} from {source.name} to {destination.name}");
        return actualDelivered;
    }

    /// <summary>
    /// Helper methods that call TaskSystem methods
    /// </summary>
    MonoBehaviour FindTriggeringFacility(GameTask task)
    {
        return TaskSystem.Instance.FindTriggeringFacility(task);
    }

    MonoBehaviour DetermineChoiceDeliverySource(AgentChoice choice, MonoBehaviour triggeringFacility)
    {
        return TaskSystem.Instance.DetermineChoiceDeliverySource(choice, triggeringFacility);
    }

    MonoBehaviour DetermineChoiceDeliveryDestination(AgentChoice choice, MonoBehaviour triggeringFacility)
    {
        return TaskSystem.Instance.DetermineChoiceDeliveryDestination(choice, triggeringFacility);
    }

    int CalculateDeliveryQuantity(AgentChoice choice, MonoBehaviour source)
    {
        return TaskSystem.Instance.CalculateDeliveryQuantity(choice, source);
    }

    bool CanBuildingHandleCargo(Building building, ResourceType cargoType, bool isSource)
    {
        BuildingResourceStorage storage = building.GetComponent<BuildingResourceStorage>();
        if (storage == null) return false;
        if (building.IsOperational() == false) return false;

        if (isSource)
        {
            return storage.GetResourceAmount(cargoType) > 0;
        }
        else
        {
            return storage.GetAvailableSpace(cargoType) > 0;
        }
    }

    bool CanPrebuiltHandleCargo(PrebuiltBuilding prebuilt, ResourceType cargoType, bool isSource)
    {
        if (cargoType == ResourceType.Population)
        {
            if (isSource)
            {
                return prebuilt.GetCurrentPopulation() > 0;
            }
            else
            {
                return prebuilt.CanAcceptPopulation(1);
            }
        }

        // For other resource types, check storage
        BuildingResourceStorage storage = prebuilt.GetResourceStorage();
        if (storage == null) return false;

        if (isSource)
        {
            return storage.GetResourceAmount(cargoType) > 0;
        }
        else
        {
            return storage.GetAvailableSpace(cargoType) > 0;
        }
    }

    /// <summary>
    /// Execute normal delivery choice (existing method, but calling correct source/destination methods)
    /// </summary>
    void ExecuteChoiceDelivery(AgentChoice choice)
    {
        MonoBehaviour triggeringFacility = FindTriggeringFacility(currentTask);
        MonoBehaviour source = DetermineChoiceDeliverySource(choice, triggeringFacility);
        MonoBehaviour destination = DetermineChoiceDeliveryDestination(choice, triggeringFacility);

        if (source == null || destination == null)
        {
            Debug.LogError($"Could not determine delivery route for choice: {choice.choiceText}");
            return;
        }

        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return;

        int actualQuantity = CalculateDeliveryQuantity(choice, source);

        DeliveryTask deliveryTask = deliverySystem.CreateSingleDeliveryTask(
            source, destination, choice.deliveryCargoType, actualQuantity, 3);

        if (deliveryTask != null)
        {
            // Link delivery → parent task so OnDeliveryTaskCompleted can find it
            TaskSystem.Instance.LinkDeliveryToTask(currentTask, deliveryTask);

            if (showDebugInfo)
                Debug.Log($"Created single delivery: {actualQuantity} {choice.deliveryCargoType} from {source.name} to {destination.name}");
        }
    }

    /// <summary>
    /// Execute delivery with multiple sources/destinations
    /// </summary>
    int ExecuteMultipleDeliveries(AgentChoice choice)
    {
        MonoBehaviour triggeringFacility = FindTriggeringFacility(currentTask);

        // Report what this selection actually did: deliveries queued (linked to the parent) or
        // people/meals moved immediately. Zero means the choice must NOT take effect.
        int linkedBefore = (currentTask != null && currentTask.linkedDeliveryTaskIds != null)
            ? currentTask.linkedDeliveryTaskIds.Count : 0;
        int movedImmediately = 0;

        switch (choice.multiDeliveryType)
        {
            case AgentChoice.MultiDeliveryType.SingleSourceMultiDest:
                movedImmediately += ExecuteSingleSourceMultiDest(choice, triggeringFacility);
                break;
            case AgentChoice.MultiDeliveryType.MultiSourceSingleDest:
                movedImmediately += ExecuteMultiSourceSingleDest(choice, triggeringFacility);
                break;
            case AgentChoice.MultiDeliveryType.MultiSourceMultiDest:
                ExecuteMultiSourceMultiDest(choice, triggeringFacility);
                break;
            default:
                // Fall back to single delivery
                ExecuteChoiceDelivery(choice);
                break;
        }

        int linkedAfter = (currentTask != null && currentTask.linkedDeliveryTaskIds != null)
            ? currentTask.linkedDeliveryTaskIds.Count : 0;
        int queued = linkedAfter - linkedBefore;
        if (currentTask != null && queued > 0)
            TaskSystem.Instance.SetTaskInProgress(currentTask);
        return queued + movedImmediately;
    }

    /// <summary>
    /// One source delivers to multiple destinations
    /// </summary>
    /// <summary>One source to up to three destinations. Returns people/meals moved immediately (0 for queued).</summary>
    int ExecuteSingleSourceMultiDest(AgentChoice choice, MonoBehaviour triggeringFacility)
    {
        MonoBehaviour source = DetermineChoiceDeliverySource(choice, triggeringFacility);
        if (source == null) return 0;

        List<MonoBehaviour> destinations = FindMultipleDestinations(choice, triggeringFacility, 3)
            .Where(dest => dest != source)
            .ToList();
        if (destinations.Count == 0)
        {
            Debug.LogWarning("No suitable destinations found for multi-destination delivery");
            return 0;
        }

        DeliverySystem deliverySystem = DeliverySystem.Instance ?? FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return 0;
        ResourceType cargo = choice.deliveryCargoType;

        // ONE budget shared by all destinations, never more than the source can actually give.
        int remaining = Mathf.Min(CalculateDeliveryQuantity(choice, source), EffectiveStock(source, cargo, deliverySystem));
        if (remaining <= 0) return 0;
        int share = Mathf.Max(1, Mathf.CeilToInt((float)remaining / destinations.Count));

        int moved = 0;
        foreach (MonoBehaviour dest in destinations)
        {
            if (remaining <= 0) break;
            int amount = Mathf.Min(share, remaining, EffectiveSpace(dest, cargo, deliverySystem));
            if (amount <= 0) continue;
            if (choice.immediateDelivery)
            {
                int m = ExecuteImmediateDeliveryBetween(source, dest, cargo, amount);
                moved += m;
                remaining -= m;
            }
            else
            {
                List<DeliveryTask> deliveries = deliverySystem.CreateDeliveryTask(source, dest, cargo, amount, 3);
                if (deliveries.Count == 0) continue;
                TaskSystem.Instance.LinkDeliveriesToTask(currentTask, deliveries);
                remaining -= amount;
            }
            if (showDebugInfo)
                Debug.Log($"Multi-dest: {amount} {cargo} from {source.name} to {dest.name}");
        }
        return moved;
    }

    /// <summary>
    /// Multiple sources deliver to one destination
    /// </summary>
    /// <summary>Up to three sources to one destination. Returns people/meals moved immediately (0 for queued).</summary>
    int ExecuteMultiSourceSingleDest(AgentChoice choice, MonoBehaviour triggeringFacility)
    {
        MonoBehaviour destination = DetermineChoiceDeliveryDestination(choice, triggeringFacility);
        if (destination == null) return 0;

        List<MonoBehaviour> sources = FindMultipleSources(choice, triggeringFacility, 3)
            .Where(src => src != destination)
            .ToList();
        if (sources.Count == 0)
        {
            Debug.LogWarning("No suitable sources found for multi-source delivery");
            return 0;
        }

        DeliverySystem deliverySystem = DeliverySystem.Instance ?? FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return 0;
        ResourceType cargo = choice.deliveryCargoType;

        // ONE budget shared by all sources (a Fixed quantity used to be sent from EACH source),
        // never more than the destination can still take.
        int requested = choice.quantityType == DeliveryQuantityType.Fixed
            ? choice.deliveryQuantity
            : sources.Sum(src => CalculateDeliveryQuantity(choice, src));
        int remaining = Mathf.Min(requested, EffectiveSpace(destination, cargo, deliverySystem));
        if (remaining <= 0) return 0;

        int moved = 0;
        foreach (MonoBehaviour source in sources)
        {
            if (remaining <= 0) break;
            int amount = Mathf.Min(remaining, EffectiveStock(source, cargo, deliverySystem), CalculateDeliveryQuantity(choice, source));
            if (amount <= 0) continue;
            if (choice.immediateDelivery)
            {
                int m = ExecuteImmediateDeliveryBetween(source, destination, cargo, amount);
                moved += m;
                remaining -= m;
            }
            else
            {
                List<DeliveryTask> deliveries = deliverySystem.CreateDeliveryTask(source, destination, cargo, amount, 3);
                if (deliveries.Count == 0) continue;
                TaskSystem.Instance.LinkDeliveriesToTask(currentTask, deliveries);
                remaining -= amount;
            }
            if (showDebugInfo)
                Debug.Log($"Multi-source: {amount} {cargo} from {source.name} to {destination.name}");
        }
        return moved;
    }

    /// <summary>
    /// Multiple sources to multiple destinations (distributed)
    /// </summary>
    void ExecuteMultiSourceMultiDest(AgentChoice choice, MonoBehaviour triggeringFacility)
    {
        List<MonoBehaviour> sources = FindMultipleSources(choice, triggeringFacility, 3);
        List<MonoBehaviour> destinations = FindMultipleDestinations(choice, triggeringFacility, 3);

        if (sources.Count == 0 || destinations.Count == 0) return;

        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return;

        // Round-robin distribution
        int destIndex = 0;
        foreach (MonoBehaviour source in sources)
        {
            int availableQuantity = CalculateDeliveryQuantity(choice, source);
            if (availableQuantity <= 0) continue;

            MonoBehaviour destination = destinations[destIndex % destinations.Count];
            destIndex++;

            if (choice.immediateDelivery)
            {
                ExecuteImmediateDeliveryBetween(source, destination, choice.deliveryCargoType, availableQuantity);
            }
            else
            {
                List<DeliveryTask> deliveries = deliverySystem.CreateDeliveryTask(source, destination, choice.deliveryCargoType, availableQuantity, 3);
                // Link all created deliveries → parent task
                TaskSystem.Instance.LinkDeliveriesToTask(currentTask, deliveries);
            }

            if (showDebugInfo)
                Debug.Log($"Multi-to-multi: {availableQuantity} {choice.deliveryCargoType} from {source.name} to {destination.name}");
        }
    }

    /// <summary>
    /// Find multiple sources for delivery
    /// </summary>
    List<MonoBehaviour> FindMultipleSources(AgentChoice choice, MonoBehaviour triggeringFacility, int maxSources)
    {
        List<MonoBehaviour> sources = new List<MonoBehaviour>();

        switch (choice.sourceType)
        {
            case DeliverySourceType.SpecificBuilding:
                Building[] buildings = FindObjectsOfType<Building>()
                    .Where(b => b.GetBuildingType() == choice.sourceBuilding)
                    .Where(b => b != triggeringFacility)
                    .Where(b => CanBuildingHandleCargo(b, choice.deliveryCargoType, true))
                    .Take(maxSources)
                    .ToArray();
                sources.AddRange(buildings.Cast<MonoBehaviour>());
                break;

            case DeliverySourceType.SpecificPrebuilt:
                PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>()
                    .Where(p => p.GetPrebuiltType() == choice.sourcePrebuilt)
                    .Where(p => p != triggeringFacility)
                    .Where(p => CanPrebuiltHandleCargo(p, choice.deliveryCargoType, true))
                    .Take(maxSources)
                    .ToArray();
                sources.AddRange(prebuilts.Cast<MonoBehaviour>());
                break;

            case DeliverySourceType.AutoFind:
                sources.AddRange(FindAllSuitableSources(choice.deliveryCargoType, maxSources));
                break;
        }

        return sources;
    }

    /// <summary>
    /// Find multiple destinations for delivery
    /// </summary>
    List<MonoBehaviour> FindMultipleDestinations(AgentChoice choice, MonoBehaviour triggeringFacility, int maxDestinations)
    {
        List<MonoBehaviour> destinations = new List<MonoBehaviour>();

        switch (choice.destinationType)
        {
            case DeliveryDestinationType.SpecificBuilding:
                Building[] buildings = FindObjectsOfType<Building>()
                    .Where(b => b.GetBuildingType() == choice.destinationBuilding)
                    .Where(b => b != triggeringFacility)
                    .Where(b => CanBuildingHandleCargo(b, choice.deliveryCargoType, false))
                    .Take(maxDestinations)
                    .ToArray();
                destinations.AddRange(buildings.Cast<MonoBehaviour>());
                break;

            case DeliveryDestinationType.SpecificPrebuilt:
                PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>()
                    .Where(p => p.GetPrebuiltType() == choice.destinationPrebuilt)
                    .Where(p => p != triggeringFacility)
                    .Where(p => CanPrebuiltHandleCargo(p, choice.deliveryCargoType, false))
                    .Take(maxDestinations)
                    .ToArray();
                destinations.AddRange(prebuilts.Cast<MonoBehaviour>());
                break;

            case DeliveryDestinationType.AutoFind:
                destinations.AddRange(FindAllSuitableDestinations(choice.deliveryCargoType, maxDestinations));
                break;
        }

        return destinations;
    }

    /// <summary>
    /// Find all suitable sources for a cargo type
    /// </summary>
    List<MonoBehaviour> FindAllSuitableSources(ResourceType cargoType, int maxCount)
    {
        List<MonoBehaviour> sources = new List<MonoBehaviour>();

        switch (cargoType)
        {
            case ResourceType.FoodPacks:
                Building[] kitchens = FindObjectsOfType<Building>()
                    .Where(b => b.GetBuildingType() == BuildingType.Kitchen)
                    .Where(b => CanBuildingHandleCargo(b, cargoType, true))
                    .Take(maxCount)
                    .ToArray();
                sources.AddRange(kitchens.Cast<MonoBehaviour>());
                break;

            case ResourceType.Population:
                PrebuiltBuilding[] communities = FindObjectsOfType<PrebuiltBuilding>()
                    .Where(p => p.GetPrebuiltType() == PrebuiltBuildingType.Community)
                    .Where(p => CanPrebuiltHandleCargo(p, cargoType, true))
                    .Take(maxCount)
                    .ToArray();
                sources.AddRange(communities.Cast<MonoBehaviour>());
                break;
        }

        return sources;
    }

    /// <summary>
    /// Find all suitable destinations for a cargo type
    /// </summary>
    List<MonoBehaviour> FindAllSuitableDestinations(ResourceType cargoType, int maxCount)
    {
        List<MonoBehaviour> destinations = new List<MonoBehaviour>();

        switch (cargoType)
        {
            case ResourceType.FoodPacks:
                Building[] shelters = FindObjectsOfType<Building>()
                    .Where(b => b.GetBuildingType() == BuildingType.Shelter)
                    .Where(b => CanBuildingHandleCargo(b, cargoType, false))
                    .Take(maxCount)
                    .ToArray();
                destinations.AddRange(shelters.Cast<MonoBehaviour>());
                break;

            case ResourceType.Population:
                Building[] shelterDests = FindObjectsOfType<Building>()
                    .Where(b => b.GetBuildingType() == BuildingType.Shelter)
                    .Where(b => CanBuildingHandleCargo(b, cargoType, false))
                    .Take(maxCount)
                    .ToArray();
                destinations.AddRange(shelterDests.Cast<MonoBehaviour>());

                PrebuiltBuilding[] motels = FindObjectsOfType<PrebuiltBuilding>()
                    .Where(p => p.GetPrebuiltType() == PrebuiltBuildingType.Motel)
                    .Where(p => CanPrebuiltHandleCargo(p, cargoType, false))
                    .Take(maxCount - destinations.Count)
                    .ToArray();
                destinations.AddRange(motels.Cast<MonoBehaviour>());
                break;
        }

        return destinations;
    }

    // -------------- END OF SECTION ------------------
    void CreateChoiceDeliveryTask(AgentChoice choice)
    {
        Debug.Log("=== STARTING CreateChoiceDeliveryTask ===");
        Debug.Log($"Choice: {choice.choiceText}");
        Debug.Log($"Triggers Delivery: {choice.triggersDelivery}");
        Debug.Log($"Cargo Type: {choice.deliveryCargoType}");
        Debug.Log($"Quantity: {choice.deliveryQuantity}");
        Debug.Log($"Source Type: {choice.sourceType}");
        Debug.Log($"Destination Type: {choice.destinationType}");
        Debug.Log($"Destination Building: {choice.destinationBuilding}");

        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null)
        {
            Debug.LogError("DeliverySystem not found!");
            return;
        }

        // Find triggering facility
        MonoBehaviour triggeringFacility = TaskSystem.Instance.FindTriggeringFacility(currentTask);
        Debug.Log($"Current Task: {currentTask?.taskTitle}");
        Debug.Log($"Affected Facility: {currentTask?.affectedFacility}");
        Debug.Log($"Triggering facility: {(triggeringFacility != null ? triggeringFacility.name : "NULL")}");

        // Try calling the methods directly to see if they're reached
        Debug.Log("About to call DetermineChoiceDeliverySource...");
        MonoBehaviour source = TaskSystem.Instance.DetermineChoiceDeliverySource(choice, triggeringFacility);
        Debug.Log($"Source result: {(source != null ? source.name : "NULL")}");

        Debug.Log("About to call DetermineChoiceDeliveryDestination...");
        MonoBehaviour destination = TaskSystem.Instance.DetermineChoiceDeliveryDestination(choice, triggeringFacility);
        Debug.Log($"Destination result: {(destination != null ? destination.name : "NULL")}");

        if (source == null || destination == null)
        {
            Debug.LogError($"Could not determine delivery route for choice: {choice.choiceText}");
            Debug.LogError($"Source: {(source != null ? "Found" : "NOT FOUND")}, Destination: {(destination != null ? "Found" : "NOT FOUND")}");
            return;
        }

        // Create delivery tasks
        DeliveryTask deliveryTask = deliverySystem.CreateSingleDeliveryTask(
            source, destination, choice.deliveryCargoType, choice.deliveryQuantity, 3);

        if (deliveryTask != null)
        {
            // Link delivery → parent task so OnDeliveryTaskCompleted can find it
            TaskSystem.Instance.LinkDeliveryToTask(currentTask, deliveryTask);

            if (showDebugInfo)
                Debug.Log($"Created delivery from choice: {source.name} → {destination.name} " +
                        $"({choice.deliveryQuantity} {choice.deliveryCargoType})");
        }
    }

    // Replace the old MonitorDeliveryProgress with this simpler version:
    IEnumerator MonitorChoiceDeliveryCompletion()
    {
        while (currentTask != null && currentTask.status == TaskStatus.InProgress)
        {
            // Check if all deliveries are completed
            if (AreAllDeliveriesCompleted())
            {
                TaskSystem.Instance.CompleteTask(currentTask);
                yield break;
            }

            // Check if task has expired (this will be handled by TaskSystem automatically)
            if (currentTask.isExpired)
            {
                yield break; // TaskSystem will handle the expiration
            }

            yield return new WaitForSeconds(1f); // Check every second
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
                return false; // There are unfinished deliveries
            }
        }

        return true; // All deliveries are completed
    }

    void ExecuteImmediateDelivery(AgentChoice choice)
    {
        MonoBehaviour triggeringFacility = TaskSystem.Instance.FindTriggeringFacility(currentTask);
        MonoBehaviour source = TaskSystem.Instance.DetermineChoiceDeliverySource(choice, triggeringFacility);
        MonoBehaviour destination = TaskSystem.Instance.DetermineChoiceDeliveryDestination(choice, triggeringFacility);

        if (source == null || destination == null)
        {
            Debug.LogError("Could not determine immediate delivery source or destination");
            return;
        }

        // Calculate quantity
        int actualQuantity = TaskSystem.Instance.CalculateDeliveryQuantity(choice, source);

        if (actualQuantity <= 0)
        {
            Debug.LogWarning($"No resources available for immediate delivery from {source.name}");
            return;
        }

        // Get resource storages
        BuildingResourceStorage sourceStorage = GetBuildingResourceStorage(source);
        BuildingResourceStorage destStorage = GetBuildingResourceStorage(destination);

        if (sourceStorage == null || destStorage == null)
        {
            Debug.LogError("Could not find resource storage for immediate delivery");
            return;
        }

        // Perform immediate transfer
        int actualRemoved = sourceStorage.RemoveResource(choice.deliveryCargoType, actualQuantity);
        int actualDelivered = destStorage.AddResource(choice.deliveryCargoType, actualRemoved);

        // Handle overflow if destination couldn't accept all
        if (actualDelivered < actualRemoved)
        {
            int overflow = actualRemoved - actualDelivered;
            sourceStorage.AddResource(choice.deliveryCargoType, overflow);
            Debug.LogWarning($"Immediate delivery overflow: {overflow} {choice.deliveryCargoType} returned to {source.name}");
        }

        if (showDebugInfo)
            Debug.Log($"Immediate delivery completed: {actualDelivered} {choice.deliveryCargoType} from {source.name} to {destination.name}");

        // Track population movement for casework (shelter OR motel arrivals; casework-site
        // deliveries process people home) — centralized, fixes the motel-not-tracked bug.
        if (choice.deliveryCargoType == ResourceType.Population && ClientStayTracker.Instance != null && actualDelivered > 0)
        {
            ClientStayTracker.Instance.HandlePopulationDelivery(source, destination, actualDelivered, currentTask.taskId);
        }
    }

    // Helper method to get resource storage from any building type
    BuildingResourceStorage GetBuildingResourceStorage(MonoBehaviour building)
    {
        // Try Building component first
        Building buildingComponent = building.GetComponent<Building>();
        if (buildingComponent != null)
        {
            return buildingComponent.GetComponent<BuildingResourceStorage>();
        }

        // Try PrebuiltBuilding component
        PrebuiltBuilding prebuiltBuilding = building.GetComponent<PrebuiltBuilding>();
        if (prebuiltBuilding != null)
        {
            return prebuiltBuilding.GetResourceStorage();
        }

        // Try direct BuildingResourceStorage component
        return building.GetComponent<BuildingResourceStorage>();
    }

    void ApplyChoiceImpacts(AgentChoice choice, int? resolvedDeliveryQuantity = null)
    {
        // taskTitle is the raw authored template (e.g. "[facility_name_plain] Flood Damage
        // Relocation") — resolve it once here so every toast/description built from it below
        // shows the actual facility name instead of the literal placeholder text.
        // plainFacilityName: true since none of these destinations (toasts, budget/satisfaction
        // reason strings) render TextMeshPro rich text links.
        string resolvedTaskTitle = currentTask.ResolvePlaceholders(currentTask.taskTitle, plainFacilityName: true);

        foreach (TaskImpact impact in choice.choiceImpacts)
        {
            switch (impact.impactType)
            {
                case ImpactType.Satisfaction:
                    if (SatisfactionAndBudget.Instance != null)
                    {
                        if (impact.value > 0)
                        {
                            SatisfactionAndBudget.Instance.AddSatisfaction(impact.value, $"Task [{resolvedTaskTitle}] satisfaction impact");
                            ToastManager.ShowToast($"Satisfaction increased by {impact.value} due to task completion of [{resolvedTaskTitle}]", ToastType.Info, true);
                        }
                        else
                        {
                            SatisfactionAndBudget.Instance.RemoveSatisfaction(-impact.value, $"Task [{resolvedTaskTitle}] satisfaction impact");
                            ToastManager.ShowToast($"Satisfaction decreased by {-impact.value} due to task completion of [{resolvedTaskTitle}]", ToastType.Info, true);
                        }
                    }
                    break;

                case ImpactType.Budget:
                    if (SatisfactionAndBudget.Instance != null)
                    {
                        int delayRounds = choice.budgetDelayRounds;

                        // costPerUnit scales this cost with the actual resolved delivery quantity
                        // (e.g. population-based food need) instead of the fixed authored value.
                        // Only overrides negative (cost) impacts — positive/incoming-funds impacts
                        // are untouched, same as before.
                        float impactValue = impact.value;
                        if (choice.costPerUnit > 0 && resolvedDeliveryQuantity.HasValue && impactValue < 0)
                        {
                            impactValue = -(choice.costPerUnit * resolvedDeliveryQuantity.Value);
                        }

                        if (impactValue > 0)
                        {
                            // Positive budget = incoming funds — respect delay
                            BudgetAllocationManager.Instance?.ScheduleAllocation(
                                (int)impactValue,
                                delayRounds,
                                $"Task: {resolvedTaskTitle}");
                            // rounds delayed
                            if (delayRounds > 0){
                                ToastManager.ShowToast(
                                    $"${impactValue:N0} funding approved — arrives in {delayRounds} round(s)",
                                    ToastType.Info, true);
                            }
                        }
                        else
                        {
                            // Costs are always immediate. Attribute to the task's
                            // service category (food vs lodging) for cost-efficiency.
                            var choiceCat = currentTask.taskTag == TaskTag.Food ? SatisfactionAndBudget.SpendCategory.Food
                                          : currentTask.taskTag == TaskTag.Lodging ? SatisfactionAndBudget.SpendCategory.Lodging
                                          : SatisfactionAndBudget.SpendCategory.Other;
                            SatisfactionAndBudget.Instance.RemoveBudget(
                                -(int)impactValue,
                                choiceCat,
                                $"Task [{resolvedTaskTitle}] cost");
                            if (DailyReportData.Instance != null)
                            {
                                float costToday = -impactValue;
                                // if (currentTask.taskTag == TaskTag.Food)
                                //     DailyReportData.Instance.RecordFoodSpendCumulative(impact.value);
                                // else if (currentTask.taskTag == TaskTag.Lodging)
                                //     DailyReportData.Instance.RecordLodgingSpendCumulative(impact.value);
                                if (choice.deliveryCargoType == ResourceType.Population)
                                {
                                    DailyReportData.Instance.RecordTransportCostToday(costToday);
                                }
                                else if (currentTask.taskTag == TaskTag.Food)
                                {
                                    DailyReportData.Instance.RecordFoodSpendCumulative(costToday);
                                    DailyReportData.Instance.RecordFastFoodSpendToday(costToday);
                                }
                                else if (currentTask.taskTag == TaskTag.Lodging)
                                {
                                    DailyReportData.Instance.RecordLodgingSpendCumulative(costToday);
                                }
                            }
                            ToastManager.ShowToast(
                                $"Budget decreased by ${-impactValue:N0}",
                                ToastType.Info, true);
                        }
                    }
                    break;
            }
        }

        // Handle vehicle repair completion
        if (currentTask.taskTitle.Contains("Vehicle Repair") && choice.choiceId == 1) // Immediate repair choice
        {
            // Extract vehicle ID from task description
            string[] parts = currentTask.description.Split('|');
            foreach (string part in parts)
            {
                if (part.StartsWith("VEHICLE_ID:"))
                {
                    string vehicleIdStr = part.Replace("VEHICLE_ID:", "");
                    if (int.TryParse(vehicleIdStr, out int vehicleId))
                    {
                        RepairVehicleById(vehicleId);
                    }
                    break;
                }
            }
        }

        if (showDebugInfo)
            Debug.Log($"Applied impacts for choice: {choice.choiceText}");
    }

    void RepairVehicleById(int vehicleId)
    {
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
        Vehicle targetVehicle = vehicles.FirstOrDefault(v => v.GetVehicleId() == vehicleId);

        if (targetVehicle != null)
        {
            targetVehicle.RepairVehicle();

            if (showDebugInfo)
                Debug.Log($"Repaired vehicle: {targetVehicle.GetVehicleName()}");
        }
    }

    void OnSendPlayerMessage()
    {
        if (playerInputField != null && !string.IsNullOrEmpty(playerInputField.text))
        {
            string message = playerInputField.text;

            // Send to vLLM server if WebSocket is connected
            if (WebSocketManager.Instance != null && WebSocketManager.Instance.IsConnected())
            {
                // Multi-agent choice proposals (taskId == -1) must be routed through the
                // director_message path so the router runs its repropose/clarify/chat
                // intent classifier. The legacy SendMessage(...) emits a "task_message"
                // type that has NO server handler and is silently dropped — which is why
                // reproposing from this panel appeared to do nothing.
                if (currentTask.taskId == -1 && currentTask.multiAgentProposal != null)
                {
                    WebSocketManager.Instance.SendDirectorMessage(
                        currentTask.multiAgentProposal.agent_name, message);
                }
                else
                {
                    WebSocketManager.Instance.SendMessage(message, currentTask.taskId);
                }
            }
            else
            {
                if (showDebugInfo)
                    Debug.Log("Playing in offline mode - message not sent to LLM");
            }

            // Always display player's message in UI (works offline!)
            GameObject messageItem = Instantiate(playerMessagePrefab, conversationContent);

            // Set message text
            TextMeshProUGUI messageText = messageItem.GetComponentInChildren<TextMeshProUGUI>();
            if (messageText != null)
            {
                messageText.text = message;
            }

            currentConversationItems.Add(messageItem);
            GameLogPanel.Instance?.LogUIInteraction(
            $"Player message sent | message={message}");

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

    /// <summary>
    /// Receives LLM responses from WebSocket and displays them as agent messages
    /// </summary>
    public void OnReceiveLLMResponse(string responseText)
    {
        if (currentTask == null || taskDetailPanel == null || !taskDetailPanel.activeInHierarchy)
        {
            Debug.LogWarning("Cannot display LLM response - task panel not active");
            return;
        }

        try
        {
            // Create dynamic agent message from LLM response
            AgentMessage llmMessage = new AgentMessage(responseText, GetCurrentTaskOfficerSprite());
            llmMessage.useTypingEffect = true;
            llmMessage.typingSpeed = 0.05f;

            // Add to task's message history
            currentTask.agentMessages.Add(llmMessage);

            // Display with typing effect
            StartCoroutine(DisplayAgentMessage(llmMessage, true));

            if (showDebugInfo)
                Debug.Log($"✅ Displayed LLM response: {responseText}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to display LLM response: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the appropriate sprite for the current task's officer
    /// </summary>
    Sprite GetCurrentTaskOfficerSprite()
    {
        if (currentTask == null || TaskSystem.Instance == null)
            return TaskSystem.Instance?.defaultAgentSprite;

        return currentTask.taskOfficer switch
        {
            TaskOfficer.WorkforceService => TaskSystem.Instance.workforceServiceSprite,
            TaskOfficer.LodgingMassCare => TaskSystem.Instance.lodgingMassCareSprite,
            TaskOfficer.ExternalRelationship => TaskSystem.Instance.externalRelationshipSprite,
            TaskOfficer.FoodMassCare => TaskSystem.Instance.foodMassCareSprite,
            _ => TaskSystem.Instance.defaultAgentSprite
        };
    }

    void Update()
    {
        // Update task status and buttons in real-time
        if (currentTask != null && taskDetailPanel.activeInHierarchy)
        {
            UpdateActionButtons();
            UpdateChoiceValidation();

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

    void UpdateChoiceValidation()
    {
        MonoBehaviour triggeringFacility = ResolveTriggeringFacility();

        foreach (GameObject item in currentConversationItems)
        {
            AgentChoiceUI choiceUI = item.GetComponent<AgentChoiceUI>();
            if (choiceUI == null) continue;

            AgentChoice choice = choiceUI.GetChoice();
            bool hasDelivery = choice.triggersDelivery || choice.immediateDelivery;
            if (!hasDelivery) continue;

            string errorMessage = "";
            bool isValid = ValidateChoiceDelivery(currentTask, choice, out errorMessage);
            
            choiceUI.SetValidationState(isValid, errorMessage);

            bool isImmediateFoodOrder = choice.immediateDelivery && choice.deliveryCargoType == ResourceType.FoodPacks;
            bool canPreview;
            if (choice.deliveryCargoType == ResourceType.FoodPacks && choice.triggersDelivery && !choice.immediateDelivery)
            {
                canPreview = isValid && FoodDeliveryHandler.Instance != null
                    && FoodDeliveryHandler.Instance.PlanSources(currentTask, choice).Count > 0;
            }
            else if (choice.deliveryCargoType == ResourceType.Population
                && (choice.destinationType != DeliveryDestinationType.SpecificBuilding || choice.destinationBuilding == BuildingType.Shelter))
            {
                // Shelter/Motel relocation can succeed by splitting across several destinations
                // (ClientRelocationHandler.Execute), so gating preview on a single destination
                // holding the FULL amount — like the generic resolver in the else branch below
                // does — would wrongly hide a valid, executable choice whenever no single shelter
                // has room but several combined do. isValid already reflects the same
                // aggregate-capacity check CanExecute/Execute use, so just reuse it.
                canPreview = isValid && TaskSystem.Instance != null
                    && TaskSystem.Instance.FindTriggeringFacility(currentTask) != null;
            }
            else
            {
                canPreview = isValid
                    && !isImmediateFoodOrder
                    && TaskSystem.Instance != null
                    && TaskSystem.Instance.DetermineChoiceDeliverySource(choice, triggeringFacility) != null
                    && TaskSystem.Instance.DetermineChoiceDeliveryDestination(choice, triggeringFacility) != null;
            }
            choiceUI.SetPreviewVisible(canPreview);
            }
    }

    MonoBehaviour ResolveTriggeringFacility()
    {
        if (currentTask == null || string.IsNullOrEmpty(currentTask.affectedFacility)) return null;
        var go = GameObject.Find(currentTask.affectedFacility);
        if (go == null) return null;
        return (MonoBehaviour)go.GetComponent<Building>() ?? go.GetComponent<PrebuiltBuilding>();
    }

    public void PreventScrollReset()
    {
        if (conversationScrollView != null)
        {
            lastScrollPosition = conversationScrollView.normalizedPosition;
            StartCoroutine(RestoreScrollPosition());
        }
    }

    IEnumerator RestoreScrollPosition()
    {
        yield return new WaitForEndOfFrame();
        if (conversationScrollView != null)
            conversationScrollView.normalizedPosition = lastScrollPosition;
    }

    public bool IsUIOpen()
    {
        return taskDetailPanel != null && taskDetailPanel.activeInHierarchy;
    }

    // NEW: Method to reset task shown history (useful for scene reloads)
    public void ResetTaskHistory()
    {
        previouslyShownTaskIds.Clear();
        if (showDebugInfo)
            Debug.Log("Task history reset - all tasks will show typing effects again");
    }

    // NEW: Method to manually mark a task as shown (useful for pre-loaded tasks)
    public void MarkTaskAsShown(int taskId)
    {
        previouslyShownTaskIds.Add(taskId);
        if (showDebugInfo)
            Debug.Log($"Task {taskId} marked as previously shown");
    }

    /// <summary>
    /// Handle when director selects a choice from a multi-agent proposal.
    /// Executes the action package and sends choice_made back to the router.
    /// </summary>
    private void HandleMultiAgentChoiceSelection()
    {
        if (selectedChoice == null)
        {
            ShowAgentErrorMessage("Please select a recommendation before confirming.");
            return;
        }

        var proposal = currentTask.multiAgentProposal;
        int packageIndex = selectedChoice.choiceId;

        // Find the selected package
        ActionPackage selectedPackage = null;
        if (proposal.packages != null && packageIndex >= 0 && packageIndex < proposal.packages.Length)
        {
            selectedPackage = proposal.packages[packageIndex];
        }

        if (selectedPackage == null)
        {
            ShowAgentErrorMessage("Selected package not found.");
            return;
        }

        Debug.Log($"[MultiAgent] Executing package {packageIndex}: {selectedPackage.label}");

        // Get available actions from the proposal
        GameActions.GameAction[] availableActions = proposal.available_actions;
        if (availableActions == null || availableActions.Length == 0)
        {
            ShowAgentErrorMessage("No actions available in proposal.");
            return;
        }

        // Execute each action in the package via ActionExecutor
        var executionResults = new System.Collections.Generic.List<ActionExecutionResultData>();

        if (selectedPackage.action_indices != null)
        {
            foreach (int actionIndex in selectedPackage.action_indices)
            {
                // Validate index bounds
                if (actionIndex < 0 || actionIndex >= availableActions.Length)
                {
                    Debug.LogWarning($"[MultiAgent] Action index {actionIndex} out of bounds (available: {availableActions.Length})");
                    executionResults.Add(new ActionExecutionResultData
                    {
                        action_index = actionIndex,
                        success = false,
                        error = "Action index out of bounds"
                    });
                    continue;
                }

                // Get the action object
                GameActions.GameAction action = availableActions[actionIndex];

                // Execute via ActionExecutor (sole responsibility for game actions)
                if (ActionExecutor.Instance != null)
                {
                    Debug.Log($"[MultiAgent] Executing action {actionIndex}: {action.description}");
                    ActionExecutionResult result = ActionExecutor.Instance.ExecuteAction(action);

                    executionResults.Add(new ActionExecutionResultData
                    {
                        action_index = actionIndex,
                        action_id = action.action_id,
                        success = result.success,
                        error = result.error_message
                    });

                    if (result.success)
                    {
                        Debug.Log($"✅ [MultiAgent] Action {actionIndex} succeeded");
                    }
                    else
                    {
                        Debug.LogWarning($"❌ [MultiAgent] Action {actionIndex} failed: {result.error_message}");
                    }
                }
                else
                {
                    Debug.LogError("[MultiAgent] ActionExecutor not found!");
                    executionResults.Add(new ActionExecutionResultData
                    {
                        action_index = actionIndex,
                        success = false,
                        error = "ActionExecutor not available"
                    });
                }
            }
        }

        // Get updated game state after execution
        GameStatePayload updatedGameState = TaskSystem.Instance.GetCurrentGameState();

        // Send choice_made back to router
        if (WebSocketManager.Instance != null)
        {
            // Convert execution results to JSON array format
            string resultsJson = "[" + string.Join(",", executionResults.ConvertAll(r =>
                $"{{\"action_index\":{r.action_index}," +
                $"\"action_id\":\"{r.action_id}\"," +
                $"\"success\":{(r.success ? "true" : "false")}," +
                $"\"error\":{(r.error != null ? "\"" + r.error.Replace("\"", "\\\"") + "\"" : "null")}}}"
            )) + "]";

            string stateJson = JsonUtility.ToJson(updatedGameState);

            WebSocketManager.Instance.SendChoiceMade(
                proposal.agent_name,
                packageIndex,
                resultsJson,
                stateJson
            );
        }

        // Complete the multi-agent task
        TaskSystem.Instance.CompleteTask(currentTask);
        CloseTaskDetail();

        Debug.Log($"✅ [MultiAgent] Choice confirmed and sent to router");
    }
}

// Helper class for multi-agent action execution results
[System.Serializable]
public class ActionExecutionResultData
{
    public int action_index;
    public string action_id;
    public bool success;
    public string error;
}