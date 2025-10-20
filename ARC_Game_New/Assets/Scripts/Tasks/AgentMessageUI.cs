using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

// Agent Message UI Component
public class AgentMessageUI : MonoBehaviour
{
    [Header("UI Components")]
    public Image agentAvatar;
    public TextMeshProUGUI messageText;
    public Image speechBubble;
    
    [Header("Layout Settings")]
    public float paddingTop = 10f;
    public float paddingBottom = 10f;
    public float minHeight = 60f;
    public float additionalHeightBuffer = 5f; // Extra space for text comfort

    private AgentMessage message;
    private string fullMessage;
    private bool isSkipped = false;
    private RectTransform parentRectTransform;
    private LayoutElement layoutElement;

    void Awake()
    {
        // Cache the parent's RectTransform
        parentRectTransform = GetComponent<RectTransform>();
        if (parentRectTransform == null)
            parentRectTransform = transform.parent?.GetComponent<RectTransform>();
            
        // Get or add LayoutElement for height control
        layoutElement = GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = gameObject.AddComponent<LayoutElement>();
    }

    public void Initialize(AgentMessage agentMessage)
    {
        message = agentMessage;
        fullMessage = agentMessage.messageText;

        // Set avatar
        if (agentAvatar != null && agentMessage.agentAvatar != null)
            agentAvatar.sprite = agentMessage.agentAvatar;

        // Initially hide text for typing effect
        if (messageText != null)
            messageText.text = "";
            
        // Calculate and set initial height based on full message
        UpdateHeightForText(fullMessage);
    }

    public IEnumerator PlayTypingEffect(float typingSpeed)
    {
        if (messageText == null || string.IsNullOrEmpty(fullMessage))
            yield break;

        messageText.text = "";
        
        // Set height for full message at start to prevent layout jumping
        UpdateHeightForText(fullMessage);

        for (int i = 0; i <= fullMessage.Length; i++)
        {
            if (isSkipped)
            {
                ShowFullMessage();
                yield break;
            }
            messageText.text = fullMessage.Substring(0, i);
            
            // Optional: Update height dynamically during typing (may cause slight jitter)
            // UpdateHeightForText(messageText.text);
            
            yield return new WaitForSecondsRealtime(typingSpeed);
        }
    }

    public void ShowFullMessage()
    {
        if (messageText != null)
        {
            messageText.text = fullMessage;
            UpdateHeightForText(fullMessage);
        }
    }

    public void SkipTyping()
    {
        AudioManager.Instance.PlaySkipSFX();
        isSkipped = true;
        ShowFullMessage();
    }
    
    private void UpdateHeightForText(string text)
    {
        if (messageText == null) return;
        
        // Temporarily set the text to calculate size
        string originalText = messageText.text;
        messageText.text = text;
        
        // Force update the text mesh to get accurate measurements
        messageText.ForceMeshUpdate();
        
        // Get the preferred height of the text
        float textHeight = messageText.preferredHeight;
        
        // Calculate total height needed
        float totalHeight = textHeight + paddingTop + paddingBottom + additionalHeightBuffer;
        
        // Ensure minimum height
        totalHeight = Mathf.Max(totalHeight, minHeight);
        
        // Apply height to parent object
        if (parentRectTransform != null)
        {
            parentRectTransform.sizeDelta = new Vector2(
                parentRectTransform.sizeDelta.x, 
                totalHeight
            );
        }
        
        // Also set LayoutElement if using layout groups
        if (layoutElement != null)
        {
            layoutElement.preferredHeight = totalHeight;
            layoutElement.minHeight = minHeight;
        }
        
        // Restore original text
        messageText.text = originalText;
        
        // Force layout rebuild if in a layout group
        LayoutRebuilder.ForceRebuildLayoutImmediate(parentRectTransform);
    }
    
    // Public method to manually recalculate height if needed
    public void RecalculateHeight()
    {
        UpdateHeightForText(fullMessage);
    }
    
    // Called when text properties change (font size, etc.)
    void OnValidate()
    {
        if (Application.isPlaying && !string.IsNullOrEmpty(fullMessage))
        {
            UpdateHeightForText(fullMessage);
        }
    }
}