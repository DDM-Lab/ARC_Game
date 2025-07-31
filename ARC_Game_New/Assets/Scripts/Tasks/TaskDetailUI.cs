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
            ShowAgentErrorMessage("This task has expired and can no longer be completed.");
            return;
        }

        // Validate selected choice if it involves delivery
        if (selectedChoice != null && selectedChoice.triggersDelivery)
        {
            string errorMessage;
            if (!ValidateChoiceDelivery(selectedChoice, out errorMessage))
            {
                ShowAgentErrorMessage($"Cannot complete this action: {errorMessage}");
                return;
            }
        }

        // Apply choice impacts first
        if (selectedChoice != null)
        {
            ApplyChoiceImpacts(selectedChoice);

            // Check if this choice triggers a delivery
            if (selectedChoice.triggersDelivery)
            {
                CreateChoiceDeliveryTask(selectedChoice);
                TaskSystem.Instance.SetTaskInProgress(currentTask);
            }
            else
            {
                TaskSystem.Instance.CompleteTask(currentTask);
            }
        }
        else
        {
            // No choice selected, complete task normally
            TaskSystem.Instance.CompleteTask(currentTask);
        }

        CloseTaskDetail();

        if (showDebugInfo)
            Debug.Log($"Task confirmed: {currentTask.taskTitle}");
    }

    //  ---------CHOICE DELIVERY VALIDATION ---------
    bool ValidateChoiceDelivery(AgentChoice choice, out string errorMessage)
    {
        errorMessage = "";

        if (!choice.triggersDelivery)
            return true; // Non-delivery choices are always valid

        // Find source and destination
        MonoBehaviour triggeringFacility = TaskSystem.Instance.FindTriggeringFacility(currentTask);
        MonoBehaviour source = TaskSystem.Instance.DetermineChoiceDeliverySource(choice, triggeringFacility);
        MonoBehaviour destination = TaskSystem.Instance.DetermineChoiceDeliveryDestination(choice, triggeringFacility);

        if (source == null)
        {
            Debug.Log($"No source found for choice: {choice.choiceText}");
            errorMessage = $"No suitable source found for {choice.deliveryCargoType}";
            return false;
        }

        if (destination == null)
        {
            Debug.Log($"No destination found for choice: {choice.choiceText}");
            errorMessage = $"No suitable destination found for {choice.deliveryCargoType}";
            return false;
        }

        // Check if source has enough resources
        int availableAmount = GetAvailableResourceAmount(source, choice.deliveryCargoType);
        if (availableAmount < choice.deliveryQuantity)
        {
            Debug.Log($"Insufficient resources at source: {source.name} for {choice.deliveryCargoType}");
            string sourceName = source.name;
            errorMessage = $"Insufficient resources at {sourceName}. Required: {choice.deliveryQuantity}, Available: {availableAmount}";
            return false;
        }

        // Check if destination has enough space
        int availableSpace = GetAvailableSpace(destination, choice.deliveryCargoType);
        if (availableSpace < choice.deliveryQuantity)
        {
            Debug.Log($"Insufficient space at destination: {destination.name} for {choice.deliveryCargoType}");
            string destName = destination.name;
            errorMessage = $"Insufficient space at {destName}. Required: {choice.deliveryQuantity}, Available space: {availableSpace}";
            return false;
        }

        // Check if any vehicle can handle this cargo type and quantity
        Vehicle[] vehicles = FindObjectsOfType<Vehicle>();
        bool hasCapableVehicle = false;

        foreach (Vehicle vehicle in vehicles)
        {
            if (vehicle.GetAllowedCargoTypes().Contains(choice.deliveryCargoType) &&
                vehicle.GetMaxCapacity() >= Mathf.Min(choice.deliveryQuantity, 10)) // Check for reasonable batch size
            {
                hasCapableVehicle = true;
                break;
            }
        }

        if (!hasCapableVehicle)
        {
            Debug.Log($"No vehicle available to transport {choice.deliveryCargoType}");
            errorMessage = $"No vehicle available to transport {choice.deliveryCargoType}";
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
        // Create a temporary agent message to show the error
        GameObject errorMessageItem = Instantiate(agentMessagePrefab, conversationContent);
        AgentMessageUI messageUI = errorMessageItem.GetComponent<AgentMessageUI>();

        if (messageUI != null)
        {
            AgentMessage errorMessage = new AgentMessage(errorText);
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
    //  --------- END OF SECTION ---------

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
            currentTask.linkedDeliveryTaskIds.Add(deliveryTask.taskId);

            // Use task's default time limit
            currentTask.deliveryTimeLimit = currentTask.deliveryTimeLimit;

            // Start monitoring delivery completion (without time segment monitoring)
            StartCoroutine(MonitorChoiceDeliveryCompletion());

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
        foreach (GameObject item in currentConversationItems)
        {
            AgentChoiceUI choiceUI = item.GetComponent<AgentChoiceUI>();
            if (choiceUI != null)
            {
                AgentChoice choice = choiceUI.GetChoice();
                if (choice.triggersDelivery)
                {
                    string errorMessage;
                    bool isValid = ValidateChoiceDelivery(choice, out errorMessage);
                    
                    // Update choice appearance based on validity
                    choiceUI.SetValidationState(isValid, errorMessage);
                }
            }
        }
    }
    
}


