using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ToastUI : MonoBehaviour
{
    [Header("UI Components")]
    public TextMeshProUGUI messageText;
    public Image backgroundImage;
    public CanvasGroup canvasGroup;
    public Button closeButton;

    [Header("Layout Settings")]
    public float maxWidth = 525f;
    public float minWidth = 300f;
    public float minHeight = 60f;
    public float paddingHorizontal = 15f; // paddings on left and right on the text
    public float paddingVertical = 5f; // paddings on top and bottom of the text

    [Header("Type Colors")]
    public Color successColor = Color.green;
    public Color warningColor = Color.yellow;
    public Color infoColor = Color.white;
    
    [Header("Animation")]
    public float slideDistance = 10f;
    public float fadeInDuration = 0.4f;
    public float fadeOutDuration = 0.3f;
    public AnimationCurve slideCurve = new AnimationCurve(new Keyframe(0, 0, 0, 2), new Keyframe(1, 1, 0, 0));
    public AnimationCurve scaleCurve = new AnimationCurve(new Keyframe(0, 0, 0, 2), new Keyframe(1, 1, 0, 0));

    private RectTransform rectTransform;
    private Vector2 originalPosition;
    private Vector3 originalScale;
    private bool isAnimating = false;
    private bool isInitialized = false;

    public System.Action OnClose;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        originalScale = transform.localScale;
    }

    private void Start()
    {
        // Hide completely until initialized
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }

        // Hide transform as backup
        if (!isInitialized)
        {
            transform.localScale = Vector3.zero;
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseButtonClicked);
        }

        AdjustToastSize();
    }

    private void AdjustToastSize()
    {
        if (messageText == null) return;

        TextMeshProUGUI tmpText = messageText as TextMeshProUGUI;
        if (tmpText != null)
        {
            // force update for correct size
            tmpText.enableWordWrapping = true;
            tmpText.ForceMeshUpdate();

            // calculate required width
            Vector2 textSize = tmpText.GetPreferredValues();
            float finalWidth = Mathf.Clamp(textSize.x + paddingHorizontal * 2, minWidth, maxWidth);
            float finalHeight = Mathf.Clamp(textSize.y + paddingVertical * 2, 0, float.MaxValue);
            if (finalHeight < minHeight)
            {
                finalHeight = minHeight;
                //change text vertical alignment to middle
                tmpText.alignment = TextAlignmentOptions.Midline;
            }
            // adjust container size
            RectTransform rectTransform = GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(finalWidth, finalHeight);
        }
    }

    public void Initialize(string message, ToastType type, float displayDuration)
    {
        isInitialized = true;
        
        messageText.text = message;
        AdjustToastSize();
        
        // Set color and style based on type
        SetToastStyle(type);
        
        // Ensure proper initial state
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }
        
        transform.localScale = originalScale;
        
        // Start animation after position is set by ToastManager
        StartCoroutine(DelayedInitialize());
    }
    
    private IEnumerator DelayedInitialize() // REMOVED displayDuration parameter
    {
        yield return new WaitForEndOfFrame();
        yield return null;
        
        originalPosition = rectTransform.anchoredPosition;
        
        StartCoroutine(AnimateIn()); // CHANGE 1.26: only animate in, no auto fade out
    }

    private IEnumerator AnimateIn()
    {
        isAnimating = true;
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }
        
        canvasGroup.alpha = 0f;
        transform.localScale = originalScale * 0.8f;
        rectTransform.anchoredPosition = originalPosition + Vector2.right * slideDistance;
        
        float elapsed = 0f;

        while (elapsed < fadeInDuration)
        {
            float progress = elapsed / fadeInDuration;
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, progress);
            
            float slideProgress = slideCurve.Evaluate(progress);
            Vector2 currentPos = Vector2.Lerp(originalPosition + Vector2.right * slideDistance, originalPosition, slideProgress);
            rectTransform.anchoredPosition = currentPos;
            
            float scaleProgress = scaleCurve.Evaluate(progress);
            transform.localScale = Vector3.Lerp(originalScale * 0.8f, originalScale, scaleProgress);
            
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        canvasGroup.alpha = 1f;
        rectTransform.anchoredPosition = originalPosition;
        transform.localScale = originalScale;
        isAnimating = false;
    }
    
    // Public method for ToastManager to update position
    public void UpdateTargetPosition(Vector2 newPosition)
    {
        if (!isAnimating)
        {
            originalPosition = newPosition;
            rectTransform.anchoredPosition = newPosition;
        }
        else
        {
            // If animating, just update the target
            originalPosition = newPosition;
        }
    }
    
    private void SetToastStyle(ToastType type)
    {
        Color typeColor = GetTypeColor(type);
        if (backgroundImage != null)
        {
            backgroundImage.color = typeColor;
        }
        
        // Set text color for better contrast
        /*if (messageText != null)
        {
            messageText.color = type == ToastType.Info ? Color.white : Color.black;
        }*/
    }
    
    private Color GetTypeColor(ToastType type)
    {
        switch (type)
        {
            case ToastType.Success:
                return successColor;
            case ToastType.Warning:
                return warningColor;
            case ToastType.Info:
                return infoColor;
            default:
                return infoColor;
        }
    }

    /*private IEnumerator AnimateToast(float displayDuration)
    {
        isAnimating = true;
        
        // Initialize animation state
        canvasGroup.alpha = 0f;
        transform.localScale = originalScale * 0.8f;
        rectTransform.anchoredPosition = originalPosition + Vector2.right * slideDistance;
        
        float elapsed = 0f;

        // Slide in + Fade in + Scale animation
        while (elapsed < fadeInDuration)
        {
            float progress = elapsed / fadeInDuration;
            
            // Fade
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, progress);
            
            // Slide
            float slideProgress = slideCurve.Evaluate(progress);
            Vector2 currentPos = Vector2.Lerp(originalPosition + Vector2.right * slideDistance, originalPosition, slideProgress);
            rectTransform.anchoredPosition = currentPos;
            
            // Scale
            float scaleProgress = scaleCurve.Evaluate(progress);
            transform.localScale = Vector3.Lerp(originalScale * 0.8f, originalScale, scaleProgress);
            
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Ensure final state
        canvasGroup.alpha = 1f;
        rectTransform.anchoredPosition = originalPosition;
        transform.localScale = originalScale;
        
        isAnimating = false; // Allow position updates during display time

        // Wait for display time
        float waitTime = displayDuration - fadeInDuration - fadeOutDuration;
        if (waitTime > 0)
        {
            yield return new WaitForSecondsRealtime(waitTime);
        }

        isAnimating = true; // Prevent position updates during fade out
        
        // Slide out + Fade out animation
        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            float progress = elapsed / fadeOutDuration;
            
            // Fade
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, progress);
            
            // Slide out to right
            float slideProgress = slideCurve.Evaluate(progress);
            Vector2 currentPos = Vector2.Lerp(originalPosition, originalPosition + Vector2.right * slideDistance, slideProgress);
            rectTransform.anchoredPosition = currentPos;
            
            // Slight scale down
            transform.localScale = Vector3.Lerp(originalScale, originalScale * 0.9f, progress);
            
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        canvasGroup.alpha = 0f;
        isAnimating = false;
    }*/

    private void OnCloseButtonClicked()
    {
        StartCoroutine(FadeOutAndClose());
    }

    private IEnumerator FadeOutAndClose()
    {
        isAnimating = true;
        float elapsed = 0f;
        
        while (elapsed < fadeOutDuration)
        {
            float progress = elapsed / fadeOutDuration;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, progress);
            transform.localScale = Vector3.Lerp(originalScale, originalScale * 0.9f, progress);
            
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        
        OnClose?.Invoke();
    }


}