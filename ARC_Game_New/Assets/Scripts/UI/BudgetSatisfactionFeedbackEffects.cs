using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class BudgetSatisfactionFeedbackEffects : MonoBehaviour
{
    [Header("Fixed Feedback Text Objects")]
    public GameObject budgetFeedbackText; // Fixed text object above budget
    public GameObject satisfactionFeedbackText; // Fixed text object above satisfaction slider
    
    [Header("Popup Sprites")]
    public Sprite positiveBackgroundSprite; // Green background
    public Sprite negativeBackgroundSprite; // Red background
    
    [Header("Animation Settings")]
    public float popupDuration = 2f;
    
    [Header("Slider Animation")]
    public float sliderAnimationDuration = 0.8f;
    public AnimationCurve sliderAnimationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    
    [Header("Text Highlight")]
    public float highlightDuration = 0.5f;
    public Color positiveHighlightColor = Color.green;
    public Color negativeHighlightColor = Color.red;
    public float highlightScaleMultiplier = 1.1f;
    
    [Header("Text Colors")]
    public Color positiveTextColor = Color.white;
    public Color negativeTextColor = Color.white;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // References to UI elements (will be set by GlobalVariables)
    private Slider satisfactionSlider;
    private TextMeshProUGUI budgetText;
    private Color originalBudgetTextColor;
    private Vector3 originalBudgetTextScale;
    
    // Singleton for easy access
    public static BudgetSatisfactionFeedbackEffects Instance { get; private set; }

    // Coroutines for managing feedback text animations
    private Coroutine budgetFeedbackCoroutine;
    private Coroutine satisfactionFeedbackCoroutine;

    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void Start()
    {
        // Initialize feedback text objects
        InitializeFeedbackTexts();
        
        if (showDebugInfo)
            Debug.Log("Feedback Effects system initialized");
    }
    
    void InitializeFeedbackTexts()
    {
        // Hide feedback texts initially
        if (budgetFeedbackText != null)
        {
            budgetFeedbackText.SetActive(false);
        }
        
        if (satisfactionFeedbackText != null)
        {
            satisfactionFeedbackText.SetActive(false);
        }
    }
    
    /// <summary>
    /// Set references to UI elements (called by GlobalVariables)
    /// </summary>
    public void SetUIReferences(Slider slider, TextMeshProUGUI budgetTextComponent)
    {
        satisfactionSlider = slider;
        budgetText = budgetTextComponent;
        
        if (budgetText != null)
        {
            originalBudgetTextColor = budgetText.color;
            originalBudgetTextScale = budgetText.transform.localScale;
        }
    }
    
    /// <summary>
    /// Show satisfaction change feedback
    /// </summary>
    public void ShowSatisfactionChange(float oldValue, float newValue, float maxValue)
    {
        float change = newValue - oldValue;
        if (Mathf.Abs(change) < 0.01f) return; // Skip tiny changes
        
        // Show feedback text
        string feedbackText = FormatSatisfactionText(change);
        bool isPositive = change > 0;
        ShowFeedbackText(satisfactionFeedbackText, feedbackText, isPositive);
        
        // Animate satisfaction slider
        if (satisfactionSlider != null)
        {
            StartCoroutine(AnimateSatisfactionSlider(oldValue, newValue));
        }
        
        if (showDebugInfo)
            Debug.Log($"Satisfaction feedback: {change:+0.0;-0.0}");
    }
    
    /// <summary>
    /// Show budget change feedback
    /// </summary>
    public void ShowBudgetChange(int oldValue, int newValue)
    {
        int change = newValue - oldValue;
        if (change == 0) return;
        
        // Show feedback text
        string feedbackText = FormatBudgetText(change);
        bool isPositive = change > 0;
        ShowFeedbackText(budgetFeedbackText, feedbackText, isPositive);
        
        // Highlight budget text
        if (budgetText != null)
        {
            StartCoroutine(HighlightBudgetText(isPositive));
        }
        
        if (showDebugInfo)
            Debug.Log($"Budget feedback: {change:+0;-0}");
    }
    

    

    void ShowFeedbackText(GameObject feedbackObject, string text, bool isPositive)
    {
        if (feedbackObject == null) return;

        // Stop existing animation for this feedback object
        if (feedbackObject == budgetFeedbackText && budgetFeedbackCoroutine != null)
        {
            StopCoroutine(budgetFeedbackCoroutine);
            feedbackObject.SetActive(false);
        }
        else if (feedbackObject == satisfactionFeedbackText && satisfactionFeedbackCoroutine != null)
        {
            StopCoroutine(satisfactionFeedbackCoroutine);
            feedbackObject.SetActive(false);
        }
        
        // Get components
        Image backgroundImage = feedbackObject.GetComponent<Image>();
        TextMeshProUGUI textComponent = feedbackObject.GetComponentInChildren<TextMeshProUGUI>();
        
        // Set background sprite
        if (backgroundImage != null)
        {
            backgroundImage.sprite = isPositive ? positiveBackgroundSprite : negativeBackgroundSprite;
        }
        
        // Set text
        if (textComponent != null)
        {
            textComponent.text = text;
            textComponent.color = isPositive ? positiveTextColor : negativeTextColor;
        }
        
        // Show and animate - START ONLY ONE COROUTINE
        feedbackObject.SetActive(true);
        
        // Start new animation and store reference
        if (feedbackObject == budgetFeedbackText)
        {
            budgetFeedbackCoroutine = StartCoroutine(AnimateFeedbackText(feedbackObject));
        }
        else if (feedbackObject == satisfactionFeedbackText)
        {
            satisfactionFeedbackCoroutine = StartCoroutine(AnimateFeedbackText(feedbackObject));
        }
        
        if (showDebugInfo)
            Debug.Log($"Showing feedback text: '{text}', positive: {isPositive}");
    }
    
    string FormatSatisfactionText(float change)
    {
        if (change > 0)
            return $"+{change:F1}";
        else
            return $"{change:F1}"; // Negative sign already included
    }
    
    string FormatBudgetText(int change)
    {
        if (change > 0)
            return $"+${change:N0}";
        else
            return $"-${Mathf.Abs(change):N0}";
    }
    
    IEnumerator AnimateFeedbackText(GameObject feedbackObject)
    {
        CanvasGroup canvasGroup = feedbackObject.GetComponent<CanvasGroup>();
        
        // Add CanvasGroup if missing
        if (canvasGroup == null)
            canvasGroup = feedbackObject.AddComponent<CanvasGroup>();
        
        // Start invisible
        canvasGroup.alpha = 0f;
        feedbackObject.transform.localScale = Vector3.one;
        
        float fadeInDuration = popupDuration * 0.2f;  // 20% fade in
        float holdDuration = popupDuration * 0.6f;    // 60% hold
        float fadeOutDuration = popupDuration * 0.2f; // 20% fade out
        
        if (showDebugInfo)
            Debug.Log($"Starting feedback text animation - FadeIn: {fadeInDuration}s, Hold: {holdDuration}s, FadeOut: {fadeOutDuration}s");
        
        // Fade In
        float elapsedTime = 0f;
        while (elapsedTime < fadeInDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            canvasGroup.alpha = elapsedTime / fadeInDuration;
            yield return null;
        }
        canvasGroup.alpha = 1f;
        
        // Hold visible
        yield return new WaitForSecondsRealtime(holdDuration);
        
        // Fade Out
        elapsedTime = 0f;
        while (elapsedTime < fadeOutDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            canvasGroup.alpha = 1f - (elapsedTime / fadeOutDuration);
            yield return null;
        }
        
        // Hide the object
        feedbackObject.SetActive(false);
        
        if (showDebugInfo)
            Debug.Log("Feedback text animation completed");
    }
    
    IEnumerator AnimateSatisfactionSlider(float fromValue, float toValue)
    {
        if (satisfactionSlider == null) yield break;
        
        float elapsedTime = 0f;
        
        while (elapsedTime < sliderAnimationDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = elapsedTime / sliderAnimationDuration;
            float curvedProgress = sliderAnimationCurve.Evaluate(progress);
            
            float currentValue = Mathf.Lerp(fromValue, toValue, curvedProgress);
            satisfactionSlider.value = currentValue;
            
            yield return null;
        }
        
        // Ensure we end at exact target value
        satisfactionSlider.value = toValue;
    }
    
    IEnumerator HighlightBudgetText(bool isPositive)
    {
        if (budgetText == null) yield break;
        
        Color highlightColor = isPositive ? positiveHighlightColor : negativeHighlightColor;
        Vector3 highlightScale = originalBudgetTextScale * highlightScaleMultiplier;
        
        float elapsedTime = 0f;
        float halfDuration = highlightDuration * 0.5f;
        
        // Scale up and change color
        while (elapsedTime < halfDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = elapsedTime / halfDuration;
            
            budgetText.color = Color.Lerp(originalBudgetTextColor, highlightColor, progress);
            budgetText.transform.localScale = Vector3.Lerp(originalBudgetTextScale, highlightScale, progress);
            
            yield return null;
        }
        
        elapsedTime = 0f;
        
        // Scale down and restore color
        while (elapsedTime < halfDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = elapsedTime / halfDuration;
            
            budgetText.color = Color.Lerp(highlightColor, originalBudgetTextColor, progress);
            budgetText.transform.localScale = Vector3.Lerp(highlightScale, originalBudgetTextScale, progress);
            
            yield return null;
        }
        
        // Ensure we end at original values
        budgetText.color = originalBudgetTextColor;
        budgetText.transform.localScale = originalBudgetTextScale;
    }
    
    [ContextMenu("Test Budget Increase")]
    public void DebugTestBudgetIncrease()
    {
        ShowFeedbackText(budgetFeedbackText, "+$2,500", true);
    }
    
    [ContextMenu("Test Budget Decrease")]
    public void DebugTestBudgetDecrease()
    {
        ShowFeedbackText(budgetFeedbackText, "-$1,000", false);
    }
}