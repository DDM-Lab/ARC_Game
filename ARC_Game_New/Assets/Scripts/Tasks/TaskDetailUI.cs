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
            // Check if panel is still active before each message
            if (taskDetailPanel == null || !taskDetailPanel.activeInHierarchy)
                yield break;
                
            yield return StartCoroutine(DisplayAgentMessage(message));
            //yield return new WaitForSecondsRealtime(0.5f); // Brief pause between messages, use real time
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

    IEnumerator DisplayAgentMessage(AgentMessage message)
    {
        // Check if panel is still active
        if (taskDetailPanel == null || !taskDetailPanel.activeInHierarchy)
            yield break;

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
        // NEW: Stop any typing effects first
        isTyping = false;
        currentTypingMessage = null;
        
        // NEW: Immediate cleanup of all conversation items
        foreach (GameObject item in currentConversationItems)
        {
            if (item != null)
            {
                // Force immediate destruction
                DestroyImmediate(item);
            }
        }
        currentConversationItems.Clear();
        selectedChoice = null;
        
        // NEW: Also clear any orphaned children from conversationContent
        if (conversationContent != null)
        {
            for (int i = conversationContent.childCount - 1; i >= 0; i--)
            {
                Transform child = conversationContent.GetChild(i);
                if (child != null)
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }
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

        // Validate selected choice if it involves any type of delivery
        if (selectedChoice != null && (selectedChoice.triggersDelivery || selectedChoice.immediateDelivery || selectedChoice.enableMultipleDeliveries))
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

            // NEW: Handle delivery execution with strict type checking
            if (selectedChoice.enableMultipleDeliveries)
            {
                Debug.Log("Executing multiple deliveries");
                ExecuteMultipleDeliveries(selectedChoice);
                TaskSystem.Instance.SetTaskInProgress(currentTask);
            }
            else if (selectedChoice.immediateDelivery && !selectedChoice.triggersDelivery) // STRICT: Only immediate, not both
            {
                Debug.Log("Executing immediate delivery");
                ExecuteImmediateDelivery(selectedChoice);
                TaskSystem.Instance.CompleteTask(currentTask);
                
            }
            else if (selectedChoice.triggersDelivery && !selectedChoice.immediateDelivery) // STRICT: Only normal, not both
            {
                Debug.Log("Executing normal delivery");
                CreateChoiceDeliveryTask(selectedChoice);
                TaskSystem.Instance.SetTaskInProgress(currentTask);
            }
            else if (selectedChoice.triggersDelivery && selectedChoice.immediateDelivery) // Both flags set
            {
                Debug.LogWarning("Choice has both triggersDelivery and immediateDelivery - using immediate delivery");
                ExecuteImmediateDelivery(selectedChoice);
                TaskSystem.Instance.CompleteTask(currentTask);
            }
            else
            {
                Debug.Log("No delivery - completing task immediately");
                TaskSystem.Instance.CompleteTask(currentTask);
            }
        
        }
        else
        {
            // No choice selected, complete task normally
            TaskSystem.Instance.CompleteTask(currentTask);
        }

        CloseTaskDetail();
    }

    //  ---------CHOICE DELIVERY VALIDATION ---------
    bool ValidateChoiceDelivery(AgentChoice choice, out string errorMessage)
    {
        errorMessage = "";

        if (!choice.triggersDelivery && !choice.immediateDelivery && !choice.enableMultipleDeliveries)
        return true;

        MonoBehaviour triggeringFacility = TaskSystem.Instance.FindTriggeringFacility(currentTask);
        if (choice.enableMultipleDeliveries)
        {
            Debug.Log("Using multi-delivery validation");
            return ValidateMultipleDeliveries(choice, triggeringFacility, out errorMessage);
        }

        Debug.Log("Using single delivery validation");
        MonoBehaviour source = TaskSystem.Instance.DetermineChoiceDeliverySource(choice, triggeringFacility);
        MonoBehaviour destination = TaskSystem.Instance.DetermineChoiceDeliveryDestination(choice, triggeringFacility);

        if (source == null)
        {
            errorMessage = $"No suitable source found for {choice.deliveryCargoType}";
            return false;
        }

        if (destination == null)
        {
            errorMessage = $"No suitable destination found for {choice.deliveryCargoType}";
            return false;
        }

        // NEW: Check if source and destination are the same
        if (source == destination)
        {
            errorMessage = $"Cannot deliver to the same facility. Need alternative {choice.destinationType}";
            return false;
        }

        // Calculate actual quantity for validation
        int availableAmount = TaskSystem.Instance.CalculateDeliveryQuantity(choice, source);

        if (availableAmount <= 0)
        {
            string quantityText = choice.quantityType == DeliveryQuantityType.Percentage ? 
                $"{choice.deliveryPercentage}%" : "all";
            errorMessage = $"No resources available at {source.name} for {quantityText} delivery";
            return false;
        }

        int availableSpace = GetAvailableSpace(destination, choice.deliveryCargoType);
        if (availableSpace < choice.deliveryQuantity)
        {
            errorMessage = $"Insufficient space at {destination.name}. Required: {choice.deliveryQuantity}, Available space: {availableSpace}";
            return false;
        }

        // Vehicle check only for normal delivery
        if (choice.triggersDelivery && !choice.immediateDelivery)
        {
            // Get detailed route analysis
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

                // Show route information in choice text (optional enhancement)
                if (analysis.isFloodAffected && analysis.hasAlternativeRoute)
                {
                    if (showDebugInfo)
                        Debug.Log($"Choice uses alternative route (+{analysis.routeLengthDifference} tiles) due to flood");
                }
            }
        
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
        }

        return true;
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
    //  -------------  END OF SECTION -------------------------------
    // -------------- SECTION: Multi delivery methods ---------------

    /// <summary>
    /// Execute immediate delivery between specific facilities
    /// </summary>
    void ExecuteImmediateDeliveryBetween(MonoBehaviour source, MonoBehaviour destination, ResourceType cargoType, int quantity)
    {
        // Get resource storages
        BuildingResourceStorage sourceStorage = GetBuildingResourceStorage(source);
        BuildingResourceStorage destStorage = GetBuildingResourceStorage(destination);

        if (sourceStorage == null || destStorage == null)
        {
            Debug.LogError($"Could not find resource storage for immediate delivery: {source.name} → {destination.name}");
            return;
        }

        // Perform immediate transfer
        int actualRemoved = sourceStorage.RemoveResource(cargoType, quantity);
        int actualDelivered = destStorage.AddResource(cargoType, actualRemoved);

        // Handle overflow if destination couldn't accept all
        if (actualDelivered < actualRemoved)
        {
            int overflow = actualRemoved - actualDelivered;
            sourceStorage.AddResource(cargoType, overflow);
            Debug.LogWarning($"Immediate delivery overflow: {overflow} {cargoType} returned to {source.name}");
        }

        // Track client arrivals at shelters
        if (cargoType == ResourceType.Population && ClientStayTracker.Instance != null)
        {
            Building destinationBuilding = destination.GetComponent<Building>();
            if (destinationBuilding != null && destinationBuilding.GetBuildingType() == BuildingType.Shelter)
            {
                string groupName = $"Delivery_Vehicle_{currentTask.taskId}";
                ClientStayTracker.Instance.RegisterClientArrival(destinationBuilding, actualDelivered, groupName);
            }
        }

        if (showDebugInfo)
            Debug.Log($"Immediate delivery: {actualDelivered} {cargoType} from {source.name} to {destination.name}");

        // NEW: Track client arrivals for multi-delivery immediate transfers
        if (cargoType == ResourceType.Population && ClientStayTracker.Instance != null && actualDelivered > 0)
        {
            Building sourceBuilding = source.GetComponent<Building>();
            Building destBuilding = destination.GetComponent<Building>();
            PrebuiltBuilding sourcePrebuilt = source.GetComponent<PrebuiltBuilding>();
            
            if (sourcePrebuilt != null && sourcePrebuilt.GetPrebuiltType() == PrebuiltBuildingType.Community &&
                destBuilding != null && destBuilding.GetBuildingType() == BuildingType.Shelter)
            {
                string groupName = $"Multi_{currentTask.taskId}_{sourcePrebuilt.name}_to_{destBuilding.name}";
                ClientStayTracker.Instance.RegisterClientArrival(destBuilding, actualDelivered, groupName);
            }
            else if (sourceBuilding != null && sourceBuilding.GetBuildingType() == BuildingType.Shelter &&
                    destBuilding != null && destBuilding.GetBuildingType() == BuildingType.Shelter)
            {
                string groupName = $"Multi_{currentTask.taskId}_{sourceBuilding.name}_to_{destBuilding.name}";
                ClientStayTracker.Instance.RegisterClientArrival(destBuilding, actualDelivered, groupName);
            }
            else if (sourceBuilding != null && sourceBuilding.GetBuildingType() == BuildingType.Shelter &&
                    destBuilding != null && destBuilding.GetBuildingType() == BuildingType.CaseworkSite)
            {
                if (showDebugInfo)
                    Debug.Log($"Multi-delivery casework: {actualDelivered} from {sourceBuilding.name}");
            }
        }
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
            currentTask.linkedDeliveryTaskIds.Add(deliveryTask.taskId);
            StartCoroutine(MonitorChoiceDeliveryCompletion());

            if (showDebugInfo)
                Debug.Log($"Created single delivery: {actualQuantity} {choice.deliveryCargoType} from {source.name} to {destination.name}");
        }
    }

    /// <summary>
    /// Execute delivery with multiple sources/destinations
    /// </summary>
    void ExecuteMultipleDeliveries(AgentChoice choice)
    {
        MonoBehaviour triggeringFacility = FindTriggeringFacility(currentTask);
        
        switch (choice.multiDeliveryType)
        {
            case AgentChoice.MultiDeliveryType.SingleSourceMultiDest:
                ExecuteSingleSourceMultiDest(choice, triggeringFacility);
                break;

            case AgentChoice.MultiDeliveryType.MultiSourceSingleDest:
                ExecuteMultiSourceSingleDest(choice, triggeringFacility);
                break;

            case AgentChoice.MultiDeliveryType.MultiSourceMultiDest:
                ExecuteMultiSourceMultiDest(choice, triggeringFacility);
                break;
                
            default:
                // Fall back to single delivery
                ExecuteChoiceDelivery(choice);
                break;
        }
    }

    /// <summary>
    /// One source delivers to multiple destinations
    /// </summary>
    void ExecuteSingleSourceMultiDest(AgentChoice choice, MonoBehaviour triggeringFacility)
    {
        Debug.Log("=== EXECUTING SINGLE SOURCE MULTI DEST ===");
        Debug.Log($"Choice immediate delivery: {choice.immediateDelivery}");
        Debug.Log($"Choice triggers delivery: {choice.triggersDelivery}");

        MonoBehaviour source = DetermineChoiceDeliverySource(choice, triggeringFacility);
        if (source == null) return;
        
        // Find multiple destinations
        List<MonoBehaviour> destinations = FindMultipleDestinations(choice, triggeringFacility, 3)
        .Where(dest => dest != source) // Exclude source
        .ToList();
        
        if (destinations.Count == 0)
        {
            Debug.LogWarning("No suitable destinations found for multi-destination delivery");
            return;
        }
        
        int totalQuantity = CalculateDeliveryQuantity(choice, source);
        int quantityPerDest = Mathf.Max(1, totalQuantity / destinations.Count);
        
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return;
        
        foreach (MonoBehaviour dest in destinations)
        {
            if (choice.immediateDelivery)
            {
                ExecuteImmediateDeliveryBetween(source, dest, choice.deliveryCargoType, quantityPerDest);
            }
            else
            {
                deliverySystem.CreateDeliveryTask(source, dest, choice.deliveryCargoType, quantityPerDest, 3);
            }
            
            if (showDebugInfo)
                Debug.Log($"Multi-delivery: {quantityPerDest} {choice.deliveryCargoType} from {source.name} to {dest.name}");
        }
    }

    /// <summary>
    /// Multiple sources deliver to one destination
    /// </summary>
    void ExecuteMultiSourceSingleDest(AgentChoice choice, MonoBehaviour triggeringFacility)
    {
        MonoBehaviour destination = DetermineChoiceDeliveryDestination(choice, triggeringFacility);
        if (destination == null) return;
        
        // Find multiple sources
        List<MonoBehaviour> sources = FindMultipleSources(choice, triggeringFacility, 3); // Max 3 sources
        
        if (sources.Count == 0)
        {
            Debug.LogWarning("No suitable sources found for multi-source delivery");
            return;
        }
        
        DeliverySystem deliverySystem = FindObjectOfType<DeliverySystem>();
        if (deliverySystem == null) return;
        
        foreach (MonoBehaviour source in sources)
        {
            int availableQuantity = CalculateDeliveryQuantity(choice, source);
            if (availableQuantity <= 0) continue;
            
            if (choice.immediateDelivery)
            {
                ExecuteImmediateDeliveryBetween(source, destination, choice.deliveryCargoType, availableQuantity);
            }
            else
            {
                deliverySystem.CreateDeliveryTask(source, destination, choice.deliveryCargoType, availableQuantity, 3);
            }
            
            if (showDebugInfo)
                Debug.Log($"Multi-source: {availableQuantity} {choice.deliveryCargoType} from {source.name} to {destination.name}");
        }
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
                deliverySystem.CreateDeliveryTask(source, destination, choice.deliveryCargoType, availableQuantity, 3);
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
            currentTask.linkedDeliveryTaskIds.Add(deliveryTask.taskId);

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

        // NEW: Track client arrivals at shelters for immediate delivery
        if (choice.deliveryCargoType == ResourceType.Population && ClientStayTracker.Instance != null && actualDelivered > 0)
        {
            Building sourceBuilding = source.GetComponent<Building>();
            Building destBuilding = destination.GetComponent<Building>();
            PrebuiltBuilding sourcePrebuilt = source.GetComponent<PrebuiltBuilding>();
            PrebuiltBuilding destPrebuilt = destination.GetComponent<PrebuiltBuilding>();
            
            // Case 1: Community to Shelter
            if (sourcePrebuilt != null && sourcePrebuilt.GetPrebuiltType() == PrebuiltBuildingType.Community &&
                destBuilding != null && destBuilding.GetBuildingType() == BuildingType.Shelter)
            {
                string groupName = $"Immediate_{currentTask.taskId}_{sourcePrebuilt.name}_to_{destBuilding.name}";
                ClientStayTracker.Instance.RegisterClientArrival(destBuilding, actualDelivered, groupName);
            }
            // Case 2: Shelter to Shelter
            else if (sourceBuilding != null && sourceBuilding.GetBuildingType() == BuildingType.Shelter &&
                    destBuilding != null && destBuilding.GetBuildingType() == BuildingType.Shelter)
            {
                string groupName = $"Immediate_{currentTask.taskId}_{sourceBuilding.name}_to_{destBuilding.name}";
                ClientStayTracker.Instance.RegisterClientArrival(destBuilding, actualDelivered, groupName);
            }
            // Case 3: Shelter to Casework
            else if (sourceBuilding != null && sourceBuilding.GetBuildingType() == BuildingType.Shelter &&
                    destBuilding != null && destBuilding.GetBuildingType() == BuildingType.CaseworkSite)
            {
                int removed = ClientStayTracker.Instance.RemoveClientsByQuantity(sourceBuilding, actualDelivered);
                if (showDebugInfo)
                    Debug.Log($"Removed {removed} clients from {sourceBuilding.name} for immediate casework");
            }
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


