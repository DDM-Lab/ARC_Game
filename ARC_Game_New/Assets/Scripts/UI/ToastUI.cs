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
    public float fadeInDuration = 0.3f;
    public float fadeOutDuration = 0.5f;

    private void Start()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = 1f; // make sure it's visible in the Scene view
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
        messageText.text = message;

        AdjustToastSize();
    
        // Set color based on type
        Color typeColor = GetTypeColor(type);
        if (backgroundImage != null)
            backgroundImage.color = typeColor;
        
        // Start animation
        StartCoroutine(AnimateToast(displayDuration));
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

    private IEnumerator AnimateToast(float displayDuration)
    {
        // Fade in
        canvasGroup.alpha = 0f;
        float elapsed = 0f;

        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;  // use unscaledDeltaTime since sometimes game pause
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeInDuration);
            yield return null;
        }

        canvasGroup.alpha = 1f;

        // Wait for display time
        float waitTime = displayDuration - fadeInDuration - fadeOutDuration;
        if (waitTime > 0)
        {
            yield return new WaitForSecondsRealtime(waitTime);  // use WaitForSecondsRealtime since sometimes game pause
        }

        // Fade out
        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;  // use unscaledDeltaTime since sometimes game pause
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
    }

}