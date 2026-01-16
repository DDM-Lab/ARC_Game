using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class AbandonedSiteHighlight : MonoBehaviour
{
    [Header("Highlight Settings")]
    public float pulseSpeed = 2f;
    public float minAlpha = 0.3f;
    public float maxAlpha = 0.8f;
    public Color highlightColor = new Color(1f, 1f, 0.5f, 0.5f);
    public Vector3 scale = Vector3.one * 1.2f;
    
    private SpriteRenderer spriteRenderer;
    private float pulseTimer = 0f;
    
    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.color = highlightColor;
            spriteRenderer.sortingOrder = 100; // Render on top
        }
        
        transform.localScale = scale;
    }
    
    void Update()
    {
        if (spriteRenderer == null) return;
        
        pulseTimer += Time.unscaledDeltaTime * pulseSpeed;
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, (Mathf.Sin(pulseTimer) + 1f) / 2f);
        
        Color c = spriteRenderer.color;
        c.a = alpha;
        spriteRenderer.color = c;
    }
}
