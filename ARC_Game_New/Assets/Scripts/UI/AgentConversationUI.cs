using UnityEngine;
using UnityEngine.UI;
using TMPro;
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
    
    [Header("Expanded Panel")]
    public GameObject expandedPanel;
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
    private List<GameTask> currentAgentTasks = new List<GameTask>();
    private GameTask currentSelectedTask = null;
    private List<GameObject> currentHistoricalTaskButtons = new List<GameObject>();
    private List<GameObject> currentConversationItems = new List<GameObject>();
    
    void Start()
    {
        SetupUI();
        
        // Initialize with first agent
        SelectAgent(TaskOfficer.DisasterOfficer);
        
        // Hide expanded panel initially
        if (expandedPanel != null)
            expandedPanel.SetActive(false);
    }
    
    void SetupUI()
    {
        // Setup expand button
        if (expandButton != null)
            expandButton.onClick.AddListener(ToggleExpanded);
            
        // Setup agent buttons
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
            
        // Setup player input
        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendPlayerMessage);
            
        if (playerInputField != null)
        {
            playerInputField.onSubmit.AddListener(OnPlayerInputSubmit);
        }
    }
    
    void ToggleExpanded()
    {
        isExpanded = !isExpanded;
        
        if (expandedPanel != null)
        {
            expandedPanel.SetActive(isExpanded);
            
            if (isExpanded)
            {
                RefreshHistoricalTasks();
                DisplayLatestConversation();
            }
        }
        
        if (showDebugInfo)
            Debug.Log($"Agent conversation panel {(isExpanded ? "expanded" : "collapsed")}");
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
        
        // Clear existing buttons
        ClearHistoricalTaskButtons();
        
        // Get tasks for current agent
        currentAgentTasks = GetTasksForAgent(currentSelectedAgent);
        
        // Create buttons for each task
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
        
        // Get both active and completed tasks
        agentTasks.AddRange(TaskSystem.Instance.GetAllActiveTasks().Where(t => t.taskOfficer == agent));
        agentTasks.AddRange(TaskSystem.Instance.GetTasksByStatus(TaskStatus.Completed).Where(t => t.taskOfficer == agent));
        agentTasks.AddRange(TaskSystem.Instance.GetTasksByStatus(TaskStatus.Incomplete).Where(t => t.taskOfficer == agent));
        agentTasks.AddRange(TaskSystem.Instance.GetTasksByStatus(TaskStatus.Expired).Where(t => t.taskOfficer == agent));
        
        // Sort by creation time (newest first)
        agentTasks = agentTasks.OrderByDescending(t => t.timeCreated).ToList();
        
        return agentTasks;
    }
    
    void CreateHistoricalTaskButton(GameTask task)
    {
        if (historicalTaskButtonPrefab == null || historicalTasksContent == null)
        {
            Debug.LogError("Historical task button prefab or content not assigned!");
            return;
        }
        
        GameObject buttonObj = Instantiate(historicalTaskButtonPrefab, historicalTasksContent);
        Button taskButton = buttonObj.GetComponent<Button>();
        TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
        
        if (buttonText != null)
        {
            buttonText.text = task.taskTitle;
        }
        
        if (taskButton != null)
        {
            taskButton.onClick.AddListener(() => SelectHistoricalTask(task));
        }
        
        currentHistoricalTaskButtons.Add(buttonObj);
    }
    
    void ClearHistoricalTaskButtons()
    {
        foreach (GameObject button in currentHistoricalTaskButtons)
        {
            if (button != null)
                Destroy(button);
        }
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
            // Show the most recent task conversation
            GameTask latestTask = currentAgentTasks[0];
            currentSelectedTask = latestTask;
            DisplayTaskConversation(latestTask);
        }
        else
        {
            // No tasks for this agent
            ClearConversation();
            DisplayNoTasksMessage();
        }
    }
    
    void DisplayTaskConversation(GameTask task)
    {
        if (task == null) return;
        
        ClearConversation();
        
        // Display task title as a system message
        DisplaySystemMessage($"=== {task.taskTitle} ===");
        
        // Display agent messages
        foreach (AgentMessage message in task.agentMessages)
        {
            DisplayAgentMessage(message);
        }
        
        // Display choices (as historical record)
        foreach (AgentChoice choice in task.agentChoices)
        {
            DisplayHistoricalChoice(choice);
        }
        
        // Display numerical inputs (as historical record)
        foreach (AgentNumericalInput input in task.numericalInputs)
        {
            DisplayHistoricalNumericalInput(input);
        }
        
        ScrollToBottom();
    }
    
    void DisplaySystemMessage(string message)
    {
        GameObject messageItem = Instantiate(agentMessagePrefab, conversationContent);
        AgentMessageUI messageUI = messageItem.GetComponent<AgentMessageUI>();
        
        if (messageUI != null)
        {
            // Get the correct avatar for current agent
            Sprite agentAvatar = GetOfficerAvatar(currentSelectedAgent);
            AgentMessage systemMessage = new AgentMessage(message, agentAvatar);
            messageUI.Initialize(systemMessage);
            messageUI.ShowFullMessage();
            
            // Make system message visually distinct
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
            messageUI.ShowFullMessage(); // No typing effect in historical view
        }
        
        currentConversationItems.Add(messageItem);
    }
    
    void DisplayHistoricalChoice(AgentChoice choice)
    {
        GameObject choiceItem = Instantiate(agentChoicePrefab, conversationContent);
        AgentChoiceUI choiceUI = choiceItem.GetComponent<AgentChoiceUI>();
        
        if (choiceUI != null)
        {
            choiceUI.InitializeAsHistorical(choice); // Special historical mode
        }
        
        currentConversationItems.Add(choiceItem);
    }
    
    void DisplayHistoricalNumericalInput(AgentNumericalInput input)
    {
        GameObject inputItem = Instantiate(numericalInputPrefab, conversationContent);
        NumericalInputUI inputUI = inputItem.GetComponent<NumericalInputUI>();
        
        if (inputUI != null)
        {
            inputUI.InitializeAsHistorical(input); // Special historical mode
        }
        
        currentConversationItems.Add(inputItem);
    }
    
    void DisplayNoTasksMessage()
    {
        DisplaySystemMessage($"No tasks found for {currentSelectedAgent}");
    }
    
    void ClearConversation()
    {
        foreach (GameObject item in currentConversationItems)
        {
            if (item != null)
                Destroy(item);
        }
        currentConversationItems.Clear();
    }
    
    void ScrollToBottom()
    {
        if (conversationScrollView != null)
        {
            StartCoroutine(ScrollToBottomCoroutine());
        }
    }
    
    System.Collections.IEnumerator ScrollToBottomCoroutine()
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
            
            // Create player message item
            GameObject messageItem = Instantiate(playerMessagePrefab, conversationContent);
            TextMeshProUGUI messageText = messageItem.GetComponentInChildren<TextMeshProUGUI>();
            
            if (messageText != null)
            {
                messageText.text = message;
            }
            
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
        {
            OnSendPlayerMessage();
        }
    }
    
    public bool IsUIOpen()
    {
        return mainPanel != null && mainPanel.activeInHierarchy && isExpanded;
    }
    
    public void OpenPanel()
    {
        if (mainPanel != null)
            mainPanel.SetActive(true);
    }
    
    public void ClosePanel()
    {
        if (mainPanel != null)
            mainPanel.SetActive(false);
    }
}