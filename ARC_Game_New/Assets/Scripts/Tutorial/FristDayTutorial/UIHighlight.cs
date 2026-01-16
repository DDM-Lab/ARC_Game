using UnityEngine;
using UnityEngine.UI;

public class UIHighlight : MonoBehaviour
{
    [Header("Highlight Settings")]
    public float pulseSpeed = 2f;
    public float minAlpha = 0.3f;
    public float maxAlpha = 1f;
    public Color highlightColor = Color.yellow;
    
    private Image highlightImage;
    private float pulseTimer = 0f;
    
    void Awake()
    {
        highlightImage = GetComponent<Image>();
        if (highlightImage != null)
        {
            highlightImage.color = highlightColor;
        }
    }
    
    void Update()
    {
        if (highlightImage == null) return;
        
        pulseTimer += Time.unscaledDeltaTime * pulseSpeed;
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, (Mathf.Sin(pulseTimer) + 1f) / 2f);
        
        Color c = highlightImage.color;
        c.a = alpha;
        highlightImage.color = c;
    }
}
