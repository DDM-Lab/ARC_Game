using UnityEngine;

// Attach to any UI GameObject (Image, Panel, etc.) to make it blink as a highlight
// by smoothly fading its transparency, without changing its size.
public class UIHighlightBlink : MonoBehaviour
{
    [Header("Alpha Settings")]
    [SerializeField] private float minAlpha = 0.3f;
    [SerializeField] private float maxAlpha = 1f;

    [Header("Timing")]
    [SerializeField] private float speed = 2f;
    [SerializeField] private bool useUnscaledTime = true;

    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void Update()
    {
        float t = (Mathf.Sin((useUnscaledTime ? Time.unscaledTime : Time.time) * speed) + 1f) * 0.5f;
        canvasGroup.alpha = Mathf.Lerp(minAlpha, maxAlpha, t);
    }

    private void OnDisable()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = maxAlpha;
    }
}