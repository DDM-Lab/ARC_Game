using UnityEngine;

// Attach to any UI GameObject (Image, Panel, etc.) to make it pulse as a highlight.
public class UIHighlightPulse : MonoBehaviour
{
    [Header("Effect")]
    [SerializeField] private bool pulseScale = true;
    [SerializeField] private bool pulseAlpha = false;

    [Header("Scale Settings")]
    [SerializeField] private float minScale = 1f;
    [SerializeField] private float maxScale = 1.2f;

    [Header("Alpha Settings")]
    [SerializeField] private float minAlpha = 0.5f;
    [SerializeField] private float maxAlpha = 1f;

    [Header("Timing")]
    [SerializeField] private float speed = 2f;
    [SerializeField] private bool useUnscaledTime = true;

    private CanvasGroup canvasGroup;
    private Vector3 baseScale;

    private void Awake()
    {
        baseScale = transform.localScale;

        if (pulseAlpha)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void Update()
    {
        float t = (Mathf.Sin((useUnscaledTime ? Time.unscaledTime : Time.time) * speed) + 1f) * 0.5f;

        if (pulseScale)
            transform.localScale = baseScale * Mathf.Lerp(minScale, maxScale, t);

        if (pulseAlpha && canvasGroup != null)
            canvasGroup.alpha = Mathf.Lerp(minAlpha, maxAlpha, t);
    }

    private void OnDisable()
    {
        transform.localScale = baseScale;
        if (canvasGroup != null)
            canvasGroup.alpha = maxAlpha;
    }
}