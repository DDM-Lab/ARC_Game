using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class BudgetSatisfactionFeedbackEffects : MonoBehaviour
{
    [Header("Fixed Feedback Text Objects")]
    public GameObject budgetFeedbackText; // Fixed text object above budget
    public GameObject satisfactionFeedbackText; // Fixed text object above satisfaction slider
    public GameObject efficiencyFeedbackText; // Fixed text object above efficiency display
    
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
    private Slider efficiencySlider;
    private TextMeshProUGUI budgetText;
    private TextMeshProUGUI satisfactionValueText;
    private TextMeshProUGUI efficiencyValueText;
    private Color originalBudgetTextColor;
    private Vector3 originalBudgetTextScale;
    private Color originalSatisfactionTextColor;
    private Vector3 originalSatisfactionTextScale;
    private Color originalEfficiencyTextColor;
    private Vector3 originalEfficiencyTextScale;

    // Singleton for easy access
    public static BudgetSatisfactionFeedbackEffects Instance { get; private set; }

    // Coroutines for managing feedback text animations
    private Coroutine budgetFeedbackCoroutine;
    private Coroutine satisfactionFeedbackCoroutine;
    private Coroutine efficiencyFeedbackCoroutine;
    private Coroutine satisfactionHighlightCoroutine;
    private Coroutine efficiencyHighlightCoroutine;

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

        if (efficiencyFeedbackText != null)
        {
            efficiencyFeedbackText.SetActive(false);
        }
    }
    
    /// <summary>
    /// Set references to UI elements (called by SatisfactionAndBudget)
    /// </summary>
    public void SetUIReferences(Slider slider, TextMeshProUGUI budgetTextComponent, TextMeshProUGUI satisfactionText = null)
    {
        satisfactionSlider = slider;
        budgetText = budgetTextComponent;
        satisfactionValueText = satisfactionText;

        if (budgetText != null)
        {
            originalBudgetTextColor = budgetText.color;
            originalBudgetTextScale = budgetText.transform.localScale;
        }

        if (satisfactionValueText != null)
        {
            originalSatisfactionTextColor = satisfactionValueText.color;
            originalSatisfactionTextScale = satisfactionValueText.transform.localScale;
        }
    }

    /// <summary>
    /// Set efficiency UI references (called by SatisfactionAndBudget)
    /// </summary>
    public void SetEfficiencyUIReferences(Slider slider, TextMeshProUGUI efficiencyText)
    {
        efficiencySlider = slider;
        efficiencyValueText = efficiencyText;

        if (efficiencyValueText != null)
        {
            originalEfficiencyTextColor = efficiencyValueText.color;
            originalEfficiencyTextScale = efficiencyValueText.transform.localScale;
        }
    }
    
    /// <summary>
    /// Show satisfaction change feedback
    /// </summary>
    public void ShowSatisfactionChange(float oldValue, float newValue, float maxValue)
    {
        float change = newValue - oldValue;
        if (Mathf.Abs(change) < 0.01f) return; // Skip tiny changes

        bool isPositive = change > 0;

        // Show feedback popup text
        ShowFeedbackText(satisfactionFeedbackText, FormatChangeText(change), isPositive);

        // Animate satisfaction slider
        if (satisfactionSlider != null)
            StartCoroutine(AnimateSatisfactionSlider(oldValue, newValue));

        // Highlight the value text (same effect as budget)
        if (satisfactionValueText != null)
        {
            if (satisfactionHighlightCoroutine != null) StopCoroutine(satisfactionHighlightCoroutine);
            satisfactionHighlightCoroutine = StartCoroutine(HighlightText(
                satisfactionValueText, originalSatisfactionTextColor, originalSatisfactionTextScale, isPositive));
        }

        if (showDebugInfo)
            Debug.Log($"Satisfaction feedback: {change:+0.0;-0.0}");
    }

    /// <summary>
    /// Show efficiency change feedback
    /// </summary>
    public void ShowEfficiencyChange(float oldValue, float newValue)
    {
        float change = newValue - oldValue;
        if (Mathf.Abs(change) < 0.01f) return;

        bool isPositive = change > 0;

        // Show feedback popup text
        ShowFeedbackText(efficiencyFeedbackText, FormatChangeText(change), isPositive);

        // Animate efficiency slider
        if (efficiencySlider != null)
            StartCoroutine(AnimateEfficiencySlider(oldValue, newValue));

        // Highlight the value text
        if (efficiencyValueText != null)
        {
            if (efficiencyHighlightCoroutine != null) StopCoroutine(efficiencyHighlightCoroutine);
            efficiencyHighlightCoroutine = StartCoroutine(HighlightText(
                efficiencyValueText, originalEfficiencyTextColor, originalEfficiencyTextScale, isPositive));
        }

        if (showDebugInfo)
            Debug.Log($"Efficiency feedback: {change:+0.0;-0.0}");
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
        else if (feedbackObject == efficiencyFeedbackText && efficiencyFeedbackCoroutine != null)
        {
            StopCoroutine(efficiencyFeedbackCoroutine);
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
        else if (feedbackObject == efficiencyFeedbackText)
        {
            efficiencyFeedbackCoroutine = StartCoroutine(AnimateFeedbackText(feedbackObject));
        }
        
        if (showDebugInfo)
            Debug.Log($"Showing feedback text: '{text}', positive: {isPositive}");
    }
    
    string FormatChangeText(float change)
    {
        return change >= 0 ? $"+{change:F1}" : $"{change:F1}";
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
        yield return StartCoroutine(HighlightText(budgetText, originalBudgetTextColor, originalBudgetTextScale, isPositive));
    }

    IEnumerator HighlightText(TextMeshProUGUI text, Color originalColor, Vector3 originalScale, bool isPositive)
    {
        if (text == null) yield break;

        Color highlightColor = isPositive ? positiveHighlightColor : negativeHighlightColor;
        Vector3 highlightScale = originalScale * highlightScaleMultiplier;
        float halfDuration = highlightDuration * 0.5f;
        float elapsedTime = 0f;

        // Scale up and change color
        while (elapsedTime < halfDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = elapsedTime / halfDuration;
            text.color = Color.Lerp(originalColor, highlightColor, progress);
            text.transform.localScale = Vector3.Lerp(originalScale, highlightScale, progress);
            yield return null;
        }

        elapsedTime = 0f;

        // Scale down and restore color
        while (elapsedTime < halfDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = elapsedTime / halfDuration;
            text.color = Color.Lerp(highlightColor, originalColor, progress);
            text.transform.localScale = Vector3.Lerp(highlightScale, originalScale, progress);
            yield return null;
        }

        text.color = originalColor;
        text.transform.localScale = originalScale;
    }

    IEnumerator AnimateEfficiencySlider(float fromValue, float toValue)
    {
        if (efficiencySlider == null) yield break;

        float elapsedTime = 0f;
        while (elapsedTime < sliderAnimationDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float progress = elapsedTime / sliderAnimationDuration;
            float curvedProgress = sliderAnimationCurve.Evaluate(progress);
            efficiencySlider.value = Mathf.Lerp(fromValue, toValue, curvedProgress);
            yield return null;
        }

        efficiencySlider.value = toValue;
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