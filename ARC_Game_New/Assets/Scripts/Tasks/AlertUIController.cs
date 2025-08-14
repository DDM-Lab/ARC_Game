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
    public Image agentIcon;
    public TextMeshProUGUI messageText;
    public Button nextButton;
    public Button skipTypingButton; // Optional: separate skip button
    
    [Header("Typing Effect Settings")]
    public float typingSpeed = 0.05f; // Seconds per character
    public AudioClip typingSoundEffect; // Optional typing sound
    public AudioSource audioSource;
    
    [Header("Agent Icons")]
    public Sprite defaultAgentSprite;
    
    // Current alert data
    private GameTask currentAlert;
    private Queue<GameTask> alertQueue = new Queue<GameTask>();
    private List<AgentMessage> alertMessages;
    private int currentMessageIndex = 0;
    private bool isTyping = false;
    private bool alertActive = false;
    private Coroutine typingCoroutine;
    
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
        
        // Setup skip typing functionality - clicking on message text skips typing
        if (messageText.GetComponent<Button>() == null)
        {
            Button messageButton = messageText.gameObject.AddComponent<Button>();
            messageButton.onClick.AddListener(OnMessageClicked);
        }
        
        // Setup audio source
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        
        audioSource.playOnAwake = false;
        audioSource.volume = 0.3f;
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
        alertMessages = currentAlert.agentMessages;
        currentMessageIndex = 0;

        if (alertMessages.Count == 0)
        {
            Debug.LogWarning("Alert task has no messages");
            return;
        }

        // Pause the game
        PauseGame();

        // NEW: Start with UI hidden, show after layout calculation
        StartCoroutine(ShowAlertWithDelay());
        
    }

    IEnumerator ShowAlertWithDelay()
    {
        // Setup UI but keep invisible, except background
        alertPanel.SetActive(false);
        alertBackground.SetActive(true);

        // Make panel transparent initially
        CanvasGroup panelCanvasGroup = alertPanel.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
            panelCanvasGroup = alertPanel.AddComponent<CanvasGroup>();

        panelCanvasGroup.alpha = 0f;

        // Setup content
        SetupAgentIcon(alertMessages[0]);
        messageText.text = alertMessages[0].messageText; // temporary set full text for Content Size Fitter calculation

        // Wait for layout to calculate
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame(); // wait 2 frames till content size is calculated

        // Now show with fade in
        alertActive = true;
        panelCanvasGroup.alpha = 1f;

        // Start typing effect
        ShowCurrentMessage();

        Debug.Log($"Showing alert: {currentAlert.taskTitle}");
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
        
        agentIcon.sprite = iconSprite;
    }
    
    /// <summary>
    /// Show current message with typing effect
    /// </summary>
    void ShowCurrentMessage()
    {
        alertPanel.SetActive(true);
        if (currentMessageIndex >= alertMessages.Count)
        {
            CompleteAlert();
            return;
        }
        
        AgentMessage currentMessage = alertMessages[currentMessageIndex];
        
        // Update agent icon for this message
        SetupAgentIcon(currentMessage);
        
        // Add small delay to prevent visual jumping
        StartCoroutine(DelayedMessageStart(currentMessage.messageText));
        
    }

    IEnumerator DelayedMessageStart(string message)
    {
        // Clear text and wait
        messageText.text = "";
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
    /// Typing effect coroutine
    /// </summary>
    IEnumerator TypeMessage(string message)
    {
        messageText.text = "";
        
        for (int i = 0; i <= message.Length; i++)
        {
            messageText.text = message.Substring(0, i);
            
            // Play typing sound
            if (typingSoundEffect != null && audioSource != null && i < message.Length)
            {
                audioSource.PlayOneShot(typingSoundEffect);
            }
            
            yield return new WaitForSecondsRealtime(typingSpeed);
        }
        
        // Typing completed
        isTyping = false;
        nextButton.interactable = true;
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
    /// Handle message text click (skip typing)
    /// </summary>
    void OnMessageClicked()
    {
        if (isTyping)
        {
            SkipTyping();
        }
    }
    
    /// <summary>
    /// Skip current typing effect
    /// </summary>
    void SkipTyping()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }
        
        // Show full message immediately with proper layout
        if (currentMessageIndex < alertMessages.Count)
        {
            string fullMessage = alertMessages[currentMessageIndex].messageText;
            
            // Temporarily disable Content Size Fitters to prevent flickering
            ContentSizeFitter textFitter = messageText.GetComponent<ContentSizeFitter>();
            ContentSizeFitter panelFitter = messageText.transform.parent.GetComponent<ContentSizeFitter>();
            
            bool textFitterWasEnabled = textFitter != null && textFitter.enabled;
            bool panelFitterWasEnabled = panelFitter != null && panelFitter.enabled;
            
            // Disable fitters
            if (textFitter != null) textFitter.enabled = false;
            if (panelFitter != null) panelFitter.enabled = false;
            
            // Set the text
            messageText.text = fullMessage;
            
            // Wait one frame then re-enable and rebuild
            StartCoroutine(RestoreLayoutAfterSkip(textFitter, panelFitter, textFitterWasEnabled, panelFitterWasEnabled));
        }
        
        isTyping = false;
        nextButton.interactable = true;
    }

    /// <summary>
    /// Restore layout components after skip
    /// </summary>
    IEnumerator RestoreLayoutAfterSkip(ContentSizeFitter textFitter, ContentSizeFitter panelFitter, bool textWasEnabled, bool panelWasEnabled)
    {
        yield return new WaitForEndOfFrame();
        
        // Re-enable fitters
        if (textFitter != null && textWasEnabled) textFitter.enabled = true;
        if (panelFitter != null && panelWasEnabled) panelFitter.enabled = true;
        
        // Force layout rebuild
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(messageText.GetComponent<RectTransform>());
        if (panelFitter != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(messageText.transform.parent.GetComponent<RectTransform>());
        LayoutRebuilder.ForceRebuildLayoutImmediate(alertPanel.GetComponent<RectTransform>());
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
        alertPanel.SetActive(false);
        alertBackground.SetActive(false);

        ResumeGame(); // resume only if no alerts
        Debug.Log("Alert completed");
    }

    // Fade out current alert and transition to next
    IEnumerator TransitionToNextAlert()
    {
        // Fade out current alert
        CanvasGroup panelCanvasGroup = alertPanel.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
            panelCanvasGroup = alertPanel.AddComponent<CanvasGroup>();

        while (panelCanvasGroup.alpha > 0f)
        {
            panelCanvasGroup.alpha -= Time.unscaledDeltaTime * 5f; // Fast fade
            yield return null;
        }

        // Small pause between alerts
        yield return new WaitForSecondsRealtime(0.2f);

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
        
        // Disable camera controls if needed
        // Add specific disabling for your camera controller here
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
        
        // Re-enable camera controls if needed
    }

    public bool IsUIOpen()
    {
        return alertPanel.activeSelf;
    } 

    void OnDestroy()
    {
        if (TaskSystem.Instance != null)
        {
            TaskSystem.Instance.OnTaskCreated -= OnTaskCreated;
        }
    }
}