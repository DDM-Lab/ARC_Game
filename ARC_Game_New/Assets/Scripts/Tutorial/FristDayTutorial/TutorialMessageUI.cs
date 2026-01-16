using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System;

[System.Serializable]
public class TutorialMessage
{
    public string messageText;
    public TaskOfficer agent;
    
    public TutorialMessage(string text, TaskOfficer agentType)
    {
        messageText = text;
        agent = agentType;
    }
}

public class TutorialMessageUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject messagePanel;
    public GameObject messageBackground;
    public GameObject clickableArea;
    public Image agentIcon;
    public TextMeshProUGUI agentNameText;
    public TextMeshProUGUI messageText;
    public Button nextButton;
    public Button skipTypingButton;
    
    [Header("Dynamic Layout")]
    public RectTransform messageContainer;
    public float minHeight = 100f;
    public float maxHeight = 400f;
    public float padding = 20f;
    public float animationSpeed = 3f;
    
    [Header("Typing Effect Settings")]
    public float typingSpeed = 0.05f;
    public AudioClip typingSoundEffect;
    public AudioSource audioSource;
    
    [Header("Agent Icons")]
    public Sprite defaultAgentSprite;
    public Sprite workforceServiceSprite;
    public Sprite lodgingMassCareSprite;
    public Sprite externalRelationshipSprite;
    public Sprite foodMassCareSprite;
    
    [Header("Agent Names")]
    public string disasterOfficerName = "Disaster Officer";
    public string workforceServiceName = "Workforce Service";
    public string lodgingMassCareName = "Lodging & Mass Care";
    public string externalRelationshipName = "External Relations";
    public string foodMassCareName = "Food & Mass Care";
    
    private List<TutorialMessage> currentMessages;
    private int currentMessageIndex = 0;
    private bool isTyping = false;
    private bool messagesActive = false;
    private Coroutine typingCoroutine;
    private Coroutine heightAnimationCoroutine;
    private Action onComplete;
    
    private float targetHeight;
    private float currentHeight;
    private bool wasGamePaused = false;
    
    public static TutorialMessageUI Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        InitializeUI();
    }
    
    void InitializeUI()
    {
        messagePanel.SetActive(false);
        messageBackground.SetActive(false);
        
        if (nextButton != null)
            nextButton.onClick.AddListener(OnNextButtonClicked);
        
        if (skipTypingButton != null)
            skipTypingButton.onClick.AddListener(OnSkipTypingButtonClicked);
        
        SetupClickableArea();
        
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        
        audioSource.playOnAwake = false;
        audioSource.volume = 0.3f;
        
        if (messageContainer == null)
            messageContainer = messageText.transform.parent.GetComponent<RectTransform>();
        
        RemoveContentSizeFitters();
    }
    
    void SetupClickableArea()
    {
        if (clickableArea == null)
        {
            clickableArea = messageContainer != null ? messageContainer.gameObject : messagePanel;
        }
        
        Button clickAreaButton = clickableArea.GetComponent<Button>();
        if (clickAreaButton == null)
        {
            clickAreaButton = clickableArea.AddComponent<Button>();
            clickAreaButton.targetGraphic = null;
            Image buttonImage = clickableArea.GetComponent<Image>();
            if (buttonImage == null)
            {
                buttonImage = clickableArea.AddComponent<Image>();
                buttonImage.color = new Color(0, 0, 0, 0);
            }
            clickAreaButton.targetGraphic = buttonImage;
        }
        
        clickAreaButton.onClick.RemoveAllListeners();
        clickAreaButton.onClick.AddListener(OnMessageAreaClicked);
    }
    
    void RemoveContentSizeFitters()
    {
        ContentSizeFitter textFitter = messageText.GetComponent<ContentSizeFitter>();
        if (textFitter != null)
            DestroyImmediate(textFitter);
        
        if (messageContainer != null)
        {
            ContentSizeFitter containerFitter = messageContainer.GetComponent<ContentSizeFitter>();
            if (containerFitter != null)
                DestroyImmediate(containerFitter);
        }
        
        ContentSizeFitter panelFitter = messagePanel.GetComponent<ContentSizeFitter>();
        if (panelFitter != null)
            DestroyImmediate(panelFitter);
    }
    
    public void ShowMessages(List<TutorialMessage> messages, Action completionCallback = null)
    {
        currentMessages = messages;
        currentMessageIndex = 0;
        onComplete = completionCallback;
        
        if (currentMessages == null || currentMessages.Count == 0)
        {
            Debug.LogWarning("TutorialMessageUI: No messages to show");
            return;
        }
        
        PauseGame();
        StartCoroutine(ShowMessagesSmooth());
    }
    
    IEnumerator ShowMessagesSmooth()
    {
        messageBackground.SetActive(true);
        messagePanel.SetActive(true);
        
        CanvasGroup panelCanvasGroup = messagePanel.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
            panelCanvasGroup = messagePanel.AddComponent<CanvasGroup>();
        
        panelCanvasGroup.alpha = 0f;
        
        SetupAgentDisplay(currentMessages[0]);
        messageText.text = "";
        
        string fullMessage = currentMessages[0].messageText;
        float calculatedHeight = CalculateTextHeight(fullMessage);
        targetHeight = Mathf.Clamp(calculatedHeight + padding, minHeight, maxHeight);
        
        currentHeight = minHeight;
        SetMessageContainerHeight(currentHeight);
        
        yield return new WaitForEndOfFrame();
        
        messagesActive = true;
        
        while (panelCanvasGroup.alpha < 1f)
        {
            panelCanvasGroup.alpha += Time.unscaledDeltaTime * 5f;
            yield return null;
        }
        
        ShowCurrentMessage();
    }
    
    void SetupAgentDisplay(TutorialMessage message)
    {
        if (agentIcon != null)
            agentIcon.sprite = GetAgentSprite(message.agent);
        
        if (agentNameText != null)
            agentNameText.text = GetAgentName(message.agent);
    }
    
    void ShowCurrentMessage()
    {
        if (currentMessageIndex >= currentMessages.Count)
        {
            CompleteMessages();
            return;
        }
        
        TutorialMessage currentMessage = currentMessages[currentMessageIndex];
        SetupAgentDisplay(currentMessage);
        
        string fullMessage = currentMessage.messageText;
        float calculatedHeight = CalculateTextHeight(fullMessage);
        targetHeight = Mathf.Clamp(calculatedHeight + padding, minHeight, maxHeight);
        
        if (heightAnimationCoroutine != null)
            StopCoroutine(heightAnimationCoroutine);
        heightAnimationCoroutine = StartCoroutine(AnimateHeight());
        
        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeMessage(fullMessage));
    }
    
    IEnumerator TypeMessage(string message)
    {
        isTyping = true;
        messageText.text = "";
        
        if (nextButton != null)
            nextButton.interactable = false;
        
        if (skipTypingButton != null)
            skipTypingButton.interactable = true;
        
        foreach (char c in message)
        {
            messageText.text += c;
            
            if (typingSoundEffect != null && audioSource != null)
            {
                audioSource.PlayOneShot(typingSoundEffect);
            }
            
            yield return new WaitForSecondsRealtime(typingSpeed);
        }
        
        isTyping = false;
        
        if (nextButton != null)
            nextButton.interactable = true;
        
        if (skipTypingButton != null)
            skipTypingButton.interactable = false;
    }
    
    IEnumerator AnimateHeight()
    {
        while (Mathf.Abs(currentHeight - targetHeight) > 1f)
        {
            currentHeight = Mathf.Lerp(currentHeight, targetHeight, Time.unscaledDeltaTime * animationSpeed);
            SetMessageContainerHeight(currentHeight);
            yield return null;
        }
        
        currentHeight = targetHeight;
        SetMessageContainerHeight(currentHeight);
    }
    
    float CalculateTextHeight(string text)
    {
        GameObject tempObj = new GameObject("TempText");
        TextMeshProUGUI tempText = tempObj.AddComponent<TextMeshProUGUI>();
        
        tempText.font = messageText.font;
        tempText.fontSize = messageText.fontSize;
        tempText.fontStyle = messageText.fontStyle;
        tempText.text = text;
        
        RectTransform tempRect = tempText.GetComponent<RectTransform>();
        tempRect.sizeDelta = new Vector2(messageText.rectTransform.sizeDelta.x, 0);
        
        tempText.ForceMeshUpdate();
        
        float height = tempText.preferredHeight;
        
        DestroyImmediate(tempObj);
        
        return height;
    }
    
    void SetMessageContainerHeight(float height)
    {
        if (messageContainer != null)
        {
            Vector2 sizeDelta = messageContainer.sizeDelta;
            sizeDelta.y = height;
            messageContainer.sizeDelta = sizeDelta;
        }
    }
    
    void OnNextButtonClicked()
    {
        if (isTyping)
        {
            SkipTyping();
        }
        else
        {
            currentMessageIndex++;
            ShowCurrentMessage();
        }
    }
    
    void OnMessageAreaClicked()
    {
        if (isTyping)
        {
            SkipTyping();
        }
    }
    
    void OnSkipTypingButtonClicked()
    {
        if (isTyping)
        {
            SkipTyping();
        }
    }
    
    void SkipTyping()
    {
        if (isTyping && typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            
            if (currentMessageIndex < currentMessages.Count)
            {
                messageText.text = currentMessages[currentMessageIndex].messageText;
            }
            
            isTyping = false;
            
            if (nextButton != null)
                nextButton.interactable = true;
            
            if (skipTypingButton != null)
                skipTypingButton.interactable = false;
            
            SetMessageContainerHeight(targetHeight);
            currentHeight = targetHeight;
        }
    }
    
    void CompleteMessages()
    {
        messagesActive = false;
        currentMessages = null;
        currentMessageIndex = 0;
        
        StartCoroutine(HideMessagesSmooth());
    }
    
    IEnumerator HideMessagesSmooth()
    {
        CanvasGroup panelCanvasGroup = messagePanel.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
            panelCanvasGroup = messagePanel.AddComponent<CanvasGroup>();
        
        while (panelCanvasGroup.alpha > 0f)
        {
            panelCanvasGroup.alpha -= Time.unscaledDeltaTime * 5f;
            yield return null;
        }
        
        messagePanel.SetActive(false);
        messageBackground.SetActive(false);
        
        ResumeGame();
        
        onComplete?.Invoke();
    }
    
    void PauseGame()
    {
        wasGamePaused = Time.timeScale == 0f;
        Time.timeScale = 0f;
        DisableGameInteractions();
    }
    
    void ResumeGame()
    {
        if (!wasGamePaused)
        {
            Time.timeScale = 1f;
        }
        EnableGameInteractions();
    }
    
    void DisableGameInteractions()
    {
        Canvas[] allCanvases = FindObjectsOfType<Canvas>();
        foreach (Canvas canvas in allCanvases)
        {
            if (canvas.gameObject != messagePanel.transform.root.gameObject)
            {
                GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                {
                    raycaster.enabled = false;
                }
            }
        }
    }
    
    void EnableGameInteractions()
    {
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
    
    Sprite GetAgentSprite(TaskOfficer officer)
    {
        switch (officer)
        {
            case TaskOfficer.DisasterOfficer:
                return defaultAgentSprite;
            case TaskOfficer.WorkforceService:
                return workforceServiceSprite;
            case TaskOfficer.LodgingMassCare:
                return lodgingMassCareSprite;
            case TaskOfficer.ExternalRelationship:
                return externalRelationshipSprite;
            case TaskOfficer.FoodMassCare:
                return foodMassCareSprite;
            default:
                return defaultAgentSprite;
        }
    }
    
    string GetAgentName(TaskOfficer officer)
    {
        switch (officer)
        {
            case TaskOfficer.DisasterOfficer:
                return disasterOfficerName;
            case TaskOfficer.WorkforceService:
                return workforceServiceName;
            case TaskOfficer.LodgingMassCare:
                return lodgingMassCareName;
            case TaskOfficer.ExternalRelationship:
                return externalRelationshipName;
            case TaskOfficer.FoodMassCare:
                return foodMassCareName;
            default:
                return disasterOfficerName;
        }
    }
    
    public bool IsActive()
    {
        return messagesActive;
    }
}