using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ActionTrackingPanel : MonoBehaviour
{
    [Header("Panel Settings")]
    public float expandDuration = 0.3f;
    public float expandedHeight = 400f;
    public float collapsedHeight = 0f;
    public AnimationCurve expandCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    [Header("UI Components")]
    public RectTransform panelContainer;
    public Button toggleButton;
    public Button exitButton;
    public ScrollRect scrollView;
    public Transform contentParent;
    public GameObject messagePrefab;
    
    private bool isExpanded = false;
    private Coroutine animationCoroutine;
    private List<GameObject> messageInstances = new List<GameObject>();

    private void Start()
    {
        // Setup button listener
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(TogglePanel);
        }
        
        // Start collapsed
        if (panelContainer != null)
        {
            panelContainer.sizeDelta = new Vector2(panelContainer.sizeDelta.x, collapsedHeight);
        }
        if (exitButton != null)
        {
            exitButton.onClick.AddListener(CollapsePanel);
        }

        // Load existing messages
        RefreshMessageList();
    }

    public void TogglePanel()
    {
        if (isExpanded)
            CollapsePanel();
        else
            ExpandPanel();
    }

    public void ExpandPanel()
    {
        if (animationCoroutine != null)
            StopCoroutine(animationCoroutine);
            
        isExpanded = true;
        animationCoroutine = StartCoroutine(AnimatePanel(expandedHeight));
        
        // Refresh messages when expanding
        RefreshMessageList();
    }

    public void CollapsePanel()
    {
        if (animationCoroutine != null)
            StopCoroutine(animationCoroutine);
            
        isExpanded = false;
        animationCoroutine = StartCoroutine(AnimatePanel(collapsedHeight));
    }

    private IEnumerator AnimatePanel(float targetHeight)
    {
        if (panelContainer == null) yield break;
        
        float startHeight = panelContainer.sizeDelta.y;
        float elapsed = 0f;
        
        while (elapsed < expandDuration)
        {
            elapsed += Time.unscaledDeltaTime; // Use unscaled time for paused game
            float progress = elapsed / expandDuration;
            float curveValue = expandCurve.Evaluate(progress);
            
            float currentHeight = Mathf.Lerp(startHeight, targetHeight, curveValue);
            panelContainer.sizeDelta = new Vector2(panelContainer.sizeDelta.x, currentHeight);
            
            yield return null;
        }
        
        panelContainer.sizeDelta = new Vector2(panelContainer.sizeDelta.x, targetHeight);
    }

    public void OnNewMessage(ActionTrackingManager.ActionMessage message)
    {
        if (isExpanded && message.day == ActionTrackingManager.Instance.currentDay)
        {
            AddMessageToList(message);
            
            // Auto-scroll to bottom to show new message
            if (scrollView != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollView.verticalNormalizedPosition = 0f;
            }
        }
    }

    private void RefreshMessageList()
    {
        // Clear existing instances
        foreach (var instance in messageInstances)
        {
            if (instance != null)
                Destroy(instance);
        }
        messageInstances.Clear();
        
        // Get all messages from manager
        if (ActionTrackingManager.Instance != null)
        {
            var messages = ActionTrackingManager.Instance.GetAllMessages();
            
            // Filter messages for the current day
            int currentDay = ActionTrackingManager.Instance.currentDay;
            foreach (var message in messages)
            {
                if (message.day == currentDay)
                {
                    AddMessageToList(message);
                }
            }
        
            
            // Scroll to bottom after adding all messages
            if (scrollView != null && messages.Count > 0)
            {
                Canvas.ForceUpdateCanvases();
                scrollView.verticalNormalizedPosition = 0f;
            }
        }
    }

    private void AddMessageToList(ActionTrackingManager.ActionMessage message)
    {
        if (messagePrefab == null || contentParent == null) return;
        
        GameObject messageObj = Instantiate(messagePrefab, contentParent);
        
        // Find and set the two TMP text components
        TextMeshProUGUI[] texts = messageObj.GetComponentsInChildren<TextMeshProUGUI>();
        if (texts.Length >= 2)
        {
            texts[0].text = message.GetDayRoundText();
            texts[1].text = message.message;
        }
        
        messageInstances.Add(messageObj);
    }
}