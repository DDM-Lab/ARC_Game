using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ActionMessageRotator : MonoBehaviour
{
    [Header("Display Settings")]
    public float rotationInterval = 3f;
    public float fadeDuration = 0.5f;
    
    [Header("UI Components")]
    public TextMeshProUGUI messageText;
    public CanvasGroup canvasGroup;
    
    [Header("Hints")]
    public List<string> hints = new List<string>() 
    { 
        "Click buildings to view details",
        "Prepare for disasters",
        "Check your resources regularly"
    };
    
    private Queue<ActionTrackingManager.ActionMessage> messageQueue = new Queue<ActionTrackingManager.ActionMessage>();
    private int currentHintIndex = 0;
    private Coroutine rotationCoroutine;
    private bool isShowingHint = true;
    private float timeUntilNextRotation = 0f;

    private void Start()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
            
        // Start with first hint or message
        UpdateDisplayedText();
        rotationCoroutine = StartCoroutine(RotateMessages());
    }

    private void OnDestroy()
    {
        if (rotationCoroutine != null)
            StopCoroutine(rotationCoroutine);
    }

    // Called when a new message is added to ActionTrackingManager
    public void OnNewMessage()
    {
        // Get the latest unread message
        if (ActionTrackingManager.Instance != null)
        {
            var newMessage = ActionTrackingManager.Instance.GetNextUnreadMessage();
            if (newMessage != null)
            {
                messageQueue.Enqueue(newMessage);
                
                // If currently showing a hint, immediately switch to the new message
                if (isShowingHint)
                {
                    if (rotationCoroutine != null)
                        StopCoroutine(rotationCoroutine);
                    rotationCoroutine = StartCoroutine(ImmediateShowNewMessage());
                }
            }
        }
    }

    private IEnumerator ImmediateShowNewMessage()
    {
        // Fade out current content
        yield return StartCoroutine(FadeCanvasGroup(canvasGroup, canvasGroup.alpha, 0f, fadeDuration * 0.5f));
        
        // Update to new message
        UpdateDisplayedText();
        
        // Fade in new message
        yield return StartCoroutine(FadeCanvasGroup(canvasGroup, 0f, 1f, fadeDuration * 0.5f));
        
        // Reset rotation cycle
        rotationCoroutine = StartCoroutine(RotateMessages());
    }

    private IEnumerator RotateMessages()
    {
        // Initial delay
        yield return new WaitForSecondsRealtime(rotationInterval);
        
        while (true)
        {
            // Fade out
            yield return StartCoroutine(FadeCanvasGroup(canvasGroup, 1f, 0f, fadeDuration));
            
            // Update text
            UpdateDisplayedText();
            
            // Fade in
            yield return StartCoroutine(FadeCanvasGroup(canvasGroup, 0f, 1f, fadeDuration));
            
            // Wait for rotation interval using real time
            yield return new WaitForSecondsRealtime(rotationInterval);
        }
    }

    private void UpdateDisplayedText()
    {
        string nextText = GetNextDisplayText();
        
        if (messageText != null)
            messageText.text = nextText;
    }

    private string GetNextDisplayText()
    {
        // First check message queue
        if (messageQueue.Count > 0)
        {
            isShowingHint = false;
            var message = messageQueue.Dequeue();
            return message.message;
        }
        
        // If no messages, show hints
        isShowingHint = true;
        if (hints.Count > 0)
        {
            string hint = hints[currentHintIndex];
            currentHintIndex = (currentHintIndex + 1) % hints.Count;
            return hint;
        }
        
        return "No messages";
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup group, float startAlpha, float endAlpha, float duration)
    {
        if (group == null) yield break;
        
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime; // Use unscaled time for paused game
            float progress = elapsed / duration;
            group.alpha = Mathf.Lerp(startAlpha, endAlpha, progress);
            yield return null;
        }
        group.alpha = endAlpha;
    }
}