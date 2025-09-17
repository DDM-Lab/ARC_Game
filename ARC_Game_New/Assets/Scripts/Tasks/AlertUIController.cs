using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System;

public class AlertUIController : MonoBehaviour
{
    [Header("UI References")]
    public GameObject alertPanel;
    public GameObject alertBackground;
    public GameObject clickableArea; // NEW: Entire clickable area for skip
    public Image agentIcon;
    public TextMeshProUGUI agentNameText;
    public TextMeshProUGUI messageText;
    public Button nextButton;
    public Button skipTypingButton; // Optional: separate skip button
    
    [Header("Dynamic Layout")]
    public RectTransform messageContainer; // Container for the message text
    public float minHeight = 100f; // Minimum height for message container
    public float maxHeight = 400f; // Maximum height for message container
    public float padding = 20f; // Padding around text
    public float animationSpeed = 3f; // Speed of height animation
    
    [Header("Typing Effect Settings")]
    public float typingSpeed = 0.05f; // Seconds per character
    public AudioClip typingSoundEffect; // Optional typing sound
    public AudioSource audioSource;
    
    [Header("Agent Icons")]
    public Sprite defaultAgentSprite;
    public Sprite workforceServiceSprite;
    public Sprite lodgingMassCareSprite;
    public Sprite externalRelationshipSprite;
    public Sprite foodMassCareSprite;

    // Current alert data
    private GameTask currentAlert;
    private Queue<GameTask> alertQueue = new Queue<GameTask>();
    private List<AgentMessage> alertMessages;
    private int currentMessageIndex = 0;
    private bool isTyping = false;
    private bool alertActive = false;
    private bool isAnimatingHeight = false;
    private Coroutine typingCoroutine;
    private Coroutine heightAnimationCoroutine;
    
    // Dynamic height calculation
    private float targetHeight;
    private float currentHeight;
    
    // Game pause state
    private bool wasGamePaused = false;
    
    // Events
    public event Action<GameTask> OnAlertCompleted;
    
    // Singleton
    public static AlertUIController Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void Start()
    {
        InitializeUI();
        
        // Subscribe to task system events
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCreated += OnTaskCreated;
        }
    }
    
    void InitializeUI()
    {
        // Hide alert UI initially
        alertPanel.SetActive(false);
        alertBackground.SetActive(false);

        // Setup button events
        nextButton.onClick.AddListener(OnNextButtonClicked);
        
        // NEW: Setup clickable area for entire panel skip functionality
        SetupClickableArea();
        
        // Setup audio source
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        
        audioSource.playOnAwake = false;
        audioSource.volume = 0.3f;
        
        // Initialize message container references
        if (messageContainer == null)
            messageContainer = messageText.transform.parent.GetComponent<RectTransform>();
            
        // Remove any Content Size Fitter components to prevent conflicts
        RemoveContentSizeFitters();
    }
    
    /// <summary>
    /// Setup clickable area for the entire message panel
    /// </summary>
    void SetupClickableArea()
    {
        // If no clickable area is assigned, use the message container
        if (clickableArea == null)
        {
            clickableArea = messageContainer != null ? messageContainer.gameObject : alertPanel;
        }
        
        // Add or get Button component for clickable area
        Button clickAreaButton = clickableArea.GetComponent<Button>();
        if (clickAreaButton == null)
        {
            clickAreaButton = clickableArea.AddComponent<Button>();
            
            // Make button invisible but interactable
            clickAreaButton.targetGraphic = null;
            Image buttonImage = clickableArea.GetComponent<Image>();
            if (buttonImage == null)
            {
                buttonImage = clickableArea.AddComponent<Image>();
                buttonImage.color = new Color(0, 0, 0, 0); // Transparent
            }
            clickAreaButton.targetGraphic = buttonImage;
        }
        
        // Setup click handler
        clickAreaButton.onClick.RemoveAllListeners();
        clickAreaButton.onClick.AddListener(OnMessageAreaClicked);
    }
    
    /// <summary>
    /// Remove Content Size Fitter components that might cause conflicts
    /// </summary>
    void RemoveContentSizeFitters()
    {
        // Remove from message text
        ContentSizeFitter textFitter = messageText.GetComponent<ContentSizeFitter>();
        if (textFitter != null)
        {
            DestroyImmediate(textFitter);
        }
        
        // Remove from message container
        if (messageContainer != null)
        {
            ContentSizeFitter containerFitter = messageContainer.GetComponent<ContentSizeFitter>();
            if (containerFitter != null)
            {
                DestroyImmediate(containerFitter);
            }
        }
        
        // Remove from alert panel
        ContentSizeFitter panelFitter = alertPanel.GetComponent<ContentSizeFitter>();
        if (panelFitter != null)
        {
            DestroyImmediate(panelFitter);
        }
    }
    
    void OnTaskCreated(GameTask task)
    {
        // Only handle alert type tasks
        if (task.taskType == TaskType.Alert)
        {
            ShowAlert(task);
        }
    }

    /// <summary>
    /// Show alert dialog for the given task
    /// </summary>
    public void ShowAlert(GameTask alertTask)
    {
        alertQueue.Enqueue(alertTask);
        Debug.Log($"Alert queued: {alertTask.taskTitle}");

        if (!alertActive)
        {
            ProcessNextAlert();
        }
    }

    void ProcessNextAlert()
    {
        if (alertQueue.Count == 0) return;

        currentAlert = alertQueue.Dequeue();
        Debug.Log($"Processing alert: {currentAlert.taskTitle}");
        GameLogPanel.Instance.LogTaskEvent($"Processing alert: {currentAlert.taskTitle}");
        alertMessages = currentAlert.agentMessages;
        currentMessageIndex = 0;

        if (alertMessages.Count == 0)
        {
            Debug.LogWarning("Alert task has no messages");
            return;
        }

        // Pause the game
        PauseGame();

        // Show alert with smooth setup
        StartCoroutine(ShowAlertSmooth());
    }

    IEnumerator ShowAlertSmooth()
    {
        // Setup UI but keep invisible
        alertBackground.SetActive(true);
        alertPanel.SetActive(true);
        
        // Make panel transparent initially
        CanvasGroup panelCanvasGroup = alertPanel.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
            panelCanvasGroup = alertPanel.AddComponent<CanvasGroup>();

        panelCanvasGroup.alpha = 0f;

        // Setup initial content
        SetupAgentIcon(alertMessages[0]);
        messageText.text = ""; // Start with empty text
        
        // Calculate target height for the full message
        string fullMessage = alertMessages[0].messageText;
        float calculatedHeight = CalculateTextHeight(fullMessage);
        targetHeight = Mathf.Clamp(calculatedHeight + padding, minHeight, maxHeight);
        
        // Set initial height to minimum
        currentHeight = minHeight;
        SetMessageContainerHeight(currentHeight);

        // Wait for layout
        yield return new WaitForEndOfFrame();

        // Fade in panel
        alertActive = true;
        while (panelCanvasGroup.alpha < 1f)
        {
            panelCanvasGroup.alpha += Time.unscaledDeltaTime * 5f;
            yield return null;
        }
        panelCanvasGroup.alpha = 1f;

        // Start typing effect
        ShowCurrentMessage();

        Debug.Log($"Showing alert: {currentAlert.taskTitle}");
    }
    
    /// <summary>
    /// Calculate the height needed for text
    /// </summary>
    float CalculateTextHeight(string text)
    {
        // Create a temporary text component for measurement
        GameObject tempObj = new GameObject("TempText");
        TextMeshProUGUI tempText = tempObj.AddComponent<TextMeshProUGUI>();
        
        // Copy settings from main text component
        tempText.font = messageText.font;
        tempText.fontSize = messageText.fontSize;
        tempText.fontStyle = messageText.fontStyle;
        tempText.text = text;
        
        // Set the width to match our message text
        RectTransform tempRect = tempText.GetComponent<RectTransform>();
        tempRect.sizeDelta = new Vector2(messageText.rectTransform.sizeDelta.x, 0);
        
        // Force text generation
        tempText.ForceMeshUpdate();
        
        // Get the preferred height
        float height = tempText.preferredHeight;
        
        // Clean up
        DestroyImmediate(tempObj);
        
        return height;
    }
    
    /// <summary>
    /// Set message container height
    /// </summary>
    void SetMessageContainerHeight(float height)
    {
        if (messageContainer != null)
        {
            Vector2 sizeDelta = messageContainer.sizeDelta;
            sizeDelta.y = height;
            messageContainer.sizeDelta = sizeDelta;
        }
    }
    
    /// <summary>
    /// Animate container height smoothly
    /// </summary>
    IEnumerator AnimateContainerHeight(float fromHeight, float toHeight)
    {
        isAnimatingHeight = true;
        float elapsed = 0f;
        float duration = Mathf.Abs(toHeight - fromHeight) / (animationSpeed * 100f); // Dynamic duration based on height difference
        duration = Mathf.Clamp(duration, 0.1f, 1f); // Clamp between 0.1 and 1 second
        
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            
            // Use smooth curve
            t = Mathf.SmoothStep(0f, 1f, t);
            
            currentHeight = Mathf.Lerp(fromHeight, toHeight, t);
            SetMessageContainerHeight(currentHeight);
            
            yield return null;
        }
        
        currentHeight = toHeight;
        SetMessageContainerHeight(currentHeight);
        isAnimatingHeight = false;
    }
    
    /// <summary>
    /// Setup agent icon based on message
    /// </summary>
    void SetupAgentIcon(AgentMessage message)
    {
        Sprite iconSprite = defaultAgentSprite;

        // Use message-specific avatar if available
        if (message.agentAvatar != null)
        {
            iconSprite = message.agentAvatar;
        }
        else if (currentAlert != null)
        {
            // Use task officer avatar if not assigned
            iconSprite = GetOfficerAvatar(currentAlert.taskOfficer);
        }

        // Set up Agent name text
        if (agentNameText != null && currentAlert != null)
        {
            agentNameText.text = GetOfficerName(currentAlert.taskOfficer);
        }

        agentIcon.sprite = iconSprite;
    }

    Sprite GetOfficerAvatar(TaskOfficer officer)
    {
        switch (officer)
        {
            case TaskOfficer.DisasterOfficer: return defaultAgentSprite;
            case TaskOfficer.WorkforceService: return workforceServiceSprite;
            case TaskOfficer.LodgingMassCare: return lodgingMassCareSprite;
            case TaskOfficer.ExternalRelationship: return externalRelationshipSprite;
            case TaskOfficer.FoodMassCare: return foodMassCareSprite;
            default: return defaultAgentSprite;
        }
    }

    string GetOfficerName(TaskOfficer officer)
    {
        switch (officer)
        {
            case TaskOfficer.DisasterOfficer: return "Disaster Officer";
            case TaskOfficer.WorkforceService: return "Workforce Service";
            case TaskOfficer.LodgingMassCare: return "Lodging Mass Care";
            case TaskOfficer.ExternalRelationship: return "External Relationship";
            case TaskOfficer.FoodMassCare: return "Food Mass Care";
            default: return "Officer";
        }
    }

    /// <summary>
    /// Show current message with typing effect
    /// </summary>
    void ShowCurrentMessage()
    {
        if (currentMessageIndex >= alertMessages.Count)
        {
            CompleteAlert();
            return;
        }
        
        AgentMessage currentMessage = alertMessages[currentMessageIndex];
        
        // Update agent icon for this message
        SetupAgentIcon(currentMessage);
        
        // Calculate new target height for this message
        float newTargetHeight = CalculateTextHeight(currentMessage.messageText);
        targetHeight = Mathf.Clamp(newTargetHeight + padding, minHeight, maxHeight);
        
        // Start typing with height animation
        StartCoroutine(ShowMessageWithAnimation(currentMessage.messageText));
    }

    IEnumerator ShowMessageWithAnimation(string message)
    {
        // Clear text
        messageText.text = "";
        
        // Animate to target height if needed
        if (Mathf.Abs(currentHeight - targetHeight) > 5f)
        {
            if (heightAnimationCoroutine != null)
                StopCoroutine(heightAnimationCoroutine);
            heightAnimationCoroutine = StartCoroutine(AnimateContainerHeight(currentHeight, targetHeight));
        }
        
        // Small delay before typing starts
        yield return new WaitForSecondsRealtime(0.1f);

        // Start typing
        StartTyping(message);
    }
    
    /// <summary>
    /// Start typing effect for message
    /// </summary>
    void StartTyping(string message)
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }
        
        isTyping = true;
        nextButton.interactable = false;
        messageText.text = "";
        
        typingCoroutine = StartCoroutine(TypeMessage(message));
    }
    
    /// <summary>
    /// Typing effect coroutine with dynamic height adjustment
    /// </summary>
    IEnumerator TypeMessage(string message)
    {
        messageText.text = "";
        int lastLineCount = 1;
        
        for (int i = 0; i <= message.Length; i++)
        {
            messageText.text = message.Substring(0, i);
            
            // Check if we need to adjust height based on line count
            int currentLineCount = messageText.textInfo.lineCount;
            if (currentLineCount != lastLineCount && !isAnimatingHeight)
            {
                float currentTextHeight = CalculateTextHeight(messageText.text);
                float adjustedHeight = Mathf.Clamp(currentTextHeight + padding, minHeight, targetHeight);
                
                if (Mathf.Abs(currentHeight - adjustedHeight) > 10f)
                {
                    // Quick micro-adjustment during typing
                    StartCoroutine(QuickHeightAdjustment(adjustedHeight));
                }
                
                lastLineCount = currentLineCount;
            }
            
            // Play typing sound
            if (typingSoundEffect != null && audioSource != null && i < message.Length)
            {
                audioSource.PlayOneShot(typingSoundEffect);
            }
            
            yield return new WaitForSecondsRealtime(typingSpeed);
        }
        
        // Ensure final height is correct
        if (Mathf.Abs(currentHeight - targetHeight) > 2f)
        {
            if (heightAnimationCoroutine != null)
                StopCoroutine(heightAnimationCoroutine);
            heightAnimationCoroutine = StartCoroutine(AnimateContainerHeight(currentHeight, targetHeight));
        }
        
        // Typing completed
        isTyping = false;
        nextButton.interactable = true;
    }
    
    /// <summary>
    /// Quick height adjustment during typing (smaller movements)
    /// </summary>
    IEnumerator QuickHeightAdjustment(float newHeight)
    {
        float startHeight = currentHeight;
        float elapsed = 0f;
        float duration = 0.1f; // Very quick adjustment
        
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            currentHeight = Mathf.Lerp(startHeight, newHeight, t);
            SetMessageContainerHeight(currentHeight);
            yield return null;
        }
        
        currentHeight = newHeight;
        SetMessageContainerHeight(currentHeight);
    }
    
    /// <summary>
    /// Handle next button click
    /// </summary>
    void OnNextButtonClicked()
    {
        if (isTyping)
        {
            // Skip typing effect
            SkipTyping();
        }
        else
        {
            // Go to next message
            currentMessageIndex++;
            ShowCurrentMessage();
        }
    }
    
    /// <summary>
    /// Handle message area click (skip typing) - NEW: Works for entire panel area
    /// </summary>
    void OnMessageAreaClicked()
    {
        if (isTyping)
        {
            SkipTyping();
        }
    }
    
    /// <summary>
    /// Skip current typing effect - IMPROVED: Smooth without glitching
    /// </summary>
    void SkipTyping()
    {
        AudioManager.Instance.PlaySkipSFX();

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }
        
        // Show full message immediately
        if (currentMessageIndex < alertMessages.Count)
        {
            string fullMessage = alertMessages[currentMessageIndex].messageText;
            messageText.text = fullMessage;
            
            // Ensure height is correct for full message
            if (Mathf.Abs(currentHeight - targetHeight) > 2f)
            {
                if (heightAnimationCoroutine != null)
                    StopCoroutine(heightAnimationCoroutine);
                // Quick snap to final height when skipping
                currentHeight = targetHeight;
                SetMessageContainerHeight(currentHeight);
            }
        }
        
        isTyping = false;
        nextButton.interactable = true;
    }
    
    /// <summary>
    /// Complete the current alert
    /// </summary>
    void CompleteAlert()
    {
        // Mark task as completed (alerts don't require choices)
        if (currentAlert != null && TaskSystem.Instance != null)
        {
            TaskSystem.Instance.CompleteAlertTask(currentAlert);
            OnAlertCompleted?.Invoke(currentAlert);
        }

        // Reset state
        alertActive = false;
        currentAlert = null;
        alertMessages = null;
        currentMessageIndex = 0;

        // Queue remaining alerts
        if (TaskSystem.Instance != null)
        {
            var activeTasks = TaskSystem.Instance.GetAllActiveTasks();
            foreach (var task in activeTasks)
            {
                if (task.taskType == TaskType.Alert && !alertQueue.Contains(task))
                {
                    alertQueue.Enqueue(task);
                    Debug.Log($"Queued remaining alert: {task.taskTitle}");
                }
            }
        }

        // Check queue for next alert
        if (alertQueue.Count > 0)
        {
            StartCoroutine(TransitionToNextAlert());
            return; // Don't resume game yet
        }
        
        // Hide UI
        StartCoroutine(HideAlertSmooth());
    }

    /// <summary>
    /// Smooth alert hiding
    /// </summary>
    IEnumerator HideAlertSmooth()
    {
        CanvasGroup panelCanvasGroup = alertPanel.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
            panelCanvasGroup = alertPanel.AddComponent<CanvasGroup>();

        while (panelCanvasGroup.alpha > 0f)
        {
            panelCanvasGroup.alpha -= Time.unscaledDeltaTime * 5f;
            yield return null;
        }

        alertPanel.SetActive(false);
        alertBackground.SetActive(false);

        ResumeGame();
        Debug.Log("Alert completed and hidden");
    }

    /// <summary>
    /// Fade out current alert and transition to next
    /// </summary>
    IEnumerator TransitionToNextAlert()
    {
        // Fade out current alert
        CanvasGroup panelCanvasGroup = alertPanel.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
            panelCanvasGroup = alertPanel.AddComponent<CanvasGroup>();

        while (panelCanvasGroup.alpha > 0f)
        {
            panelCanvasGroup.alpha -= Time.unscaledDeltaTime * 5f;
            yield return null;
        }

        // Small pause between alerts
        yield return new WaitForSecondsRealtime(0.3f);

        // Process next alert
        ProcessNextAlert();
    }

    /// <summary>
    /// Pause the game
    /// </summary>
    void PauseGame()
    {
        wasGamePaused = Time.timeScale == 0f;
        Time.timeScale = 0f;
        
        // Disable other UI interactions
        DisableGameInteractions();
        
        Debug.Log("Game paused for alert");
    }
    
    /// <summary>
    /// Resume the game
    /// </summary>
    void ResumeGame()
    {
        if (!wasGamePaused)
        {
            Time.timeScale = 1f;
        }
        
        // Re-enable other UI interactions
        EnableGameInteractions();
        
        Debug.Log("Game resumed after alert");
    }
    
    /// <summary>
    /// Disable game interactions during alert
    /// </summary>
    void DisableGameInteractions()
    {
        // Find and disable other UI canvases
        Canvas[] allCanvases = FindObjectsOfType<Canvas>();
        foreach (Canvas canvas in allCanvases)
        {
            if (canvas.gameObject != alertPanel.transform.root.gameObject)
            {
                GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                {
                    raycaster.enabled = false;
                }
            }
        }
    }
    
    /// <summary>
    /// Re-enable game interactions after alert
    /// </summary>
    void EnableGameInteractions()
    {
        // Re-enable UI canvases
        Canvas[] allCanvases = FindObjectsOfType<Canvas>();
        foreach (Canvas canvas in allCanvases)
        {
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = true;
            }
        }
    }

    public bool IsUIOpen()
    {
        return alertActive;
    }

    void OnDestroy()
    {
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCreated -= OnTaskCreated;
        }
    }
}