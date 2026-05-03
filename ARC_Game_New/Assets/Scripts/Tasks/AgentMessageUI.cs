using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

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
    private System.Action<string> onFacilityClick;

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

    public void Initialize(AgentMessage agentMessage, System.Action<string> facilityClickCallback = null)
    {
        message = agentMessage;
        fullMessage = agentMessage.messageText;
        onFacilityClick = facilityClickCallback;

        if (agentAvatar != null && agentMessage.agentAvatar != null)
            agentAvatar.sprite = agentMessage.agentAvatar;

        if (messageText != null)
            messageText.text = "";

        UpdateHeightForText(fullMessage);
    }

    void Update()
    {
        if (onFacilityClick == null || messageText == null || !Input.GetMouseButtonDown(0)) return;

        Camera cam = messageText.canvas?.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : messageText.canvas?.worldCamera;

        int linkIndex = TMP_TextUtilities.FindIntersectingLink(messageText, Input.mousePosition, cam);
        if (linkIndex >= 0)
        {
            string linkId = messageText.textInfo.linkInfo[linkIndex].GetLinkID();
            onFacilityClick.Invoke(linkId);
        }
    }

    public IEnumerator PlayTypingEffect(float typingSpeed)
    {
        if (messageText == null || string.IsNullOrEmpty(fullMessage))
            yield break;

        // Set full text first so TMP can parse tags, then reveal character by character
        messageText.text = fullMessage;
        UpdateHeightForText(fullMessage);
        messageText.ForceMeshUpdate();
        int totalChars = messageText.textInfo.characterCount;
        messageText.maxVisibleCharacters = 0;

        for (int i = 0; i <= totalChars; i++)
        {
            if (isSkipped)
            {
                ShowFullMessage();
                yield break;
            }
            messageText.maxVisibleCharacters = i;
            yield return new WaitForSecondsRealtime(typingSpeed);
        }

        messageText.maxVisibleCharacters = int.MaxValue;
    }

    public void ShowFullMessage()
    {
        if (messageText != null)
        {
            messageText.text = fullMessage;
            messageText.maxVisibleCharacters = int.MaxValue;
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