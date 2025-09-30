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
    
    void OnSendPlayerMessage()
    {
        if (playerInputField != null && !string.IsNullOrEmpty(playerInputField.text))
        {
            string message = playerInputField.text;
            GameObject messageItem = Instantiate(playerMessagePrefab, conversationContent);
            TextMeshProUGUI messageText = messageItem.GetComponentInChildren<TextMeshProUGUI>();
            
            if (messageText != null) messageText.text = message;
            
            currentConversationItems.Add(messageItem);
            playerInputField.text = "";
            ScrollToBottom();
            
            if (showDebugInfo)
                Debug.Log($"Player sent message to {currentSelectedAgent}: {message}");
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