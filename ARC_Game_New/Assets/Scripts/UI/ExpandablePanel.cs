using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class ExpandablePanel : MonoBehaviour
{
    [Header("Panel Components")]
    public RectTransform panelRectTransform;
    public Button toggleButton;
    // contentArea reference kept for potential future use, but not required for scroll view
    
    [Header("Button Icons")]
    public string collapsedIcon = "—"; // When panel is collapsed (bar state)
    public string expandedIcon = "^";  // When panel is expanded
    
    [Header("Panel Heights")]
    public float collapsedHeight = 40f; // Height when showing only title bar
    public float expandedHeight = 200f; // Height when showing full content
    
    [Header("Animation")]
    public bool useAnimation = true;
    public float animationDuration = 0.3f;
    public AnimationCurve animationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Panel state
    private bool isExpanded = false;
    private bool isAnimating = false;
    
    // UI references
    private TextMeshProUGUI buttonText;
    
    void Start()
    {
        InitializePanel();
        SetupButton();
        
        // Start in collapsed state
        SetPanelState(false, false);
        
        if (showDebugInfo)
            Debug.Log("Expandable Panel initialized in collapsed state");
    }
    
    void InitializePanel()
    {
        // Get panel RectTransform if not assigned
        if (panelRectTransform == null)
            panelRectTransform = GetComponent<RectTransform>();
        
        // Validate components
        if (panelRectTransform == null)
        {
            Debug.LogError("ExpandablePanel: Panel RectTransform is required!");
            return;
        }
        
        // Set initial heights based on current panel size if not set
        if (expandedHeight <= collapsedHeight)
        {
            expandedHeight = panelRectTransform.sizeDelta.y;
            if (showDebugInfo)
                Debug.Log($"Auto-detected expanded height: {expandedHeight}");
        }
    }
    
    void SetupButton()
    {
        if (toggleButton == null)
        {
            Debug.LogError("ExpandablePanel: Toggle Button is required!");
            return;
        }
        
        // Get button text component
        buttonText = toggleButton.GetComponentInChildren<TextMeshProUGUI>();
        
        if (buttonText == null)
        {
            Debug.LogError("ExpandablePanel: Button must have a TextMeshProUGUI child for icon display!");
            return;
        }
        
        // Setup button click listener
        toggleButton.onClick.AddListener(TogglePanel);
    }
    
    public void TogglePanel()
    {
        if (isAnimating) return;
        
        SetPanelState(!isExpanded, useAnimation);
        
        if (showDebugInfo)
            Debug.Log($"Panel {(isExpanded ? "expanded" : "collapsed")}");
    }
    
    void SetPanelState(bool expanded, bool animate)
    {
        isExpanded = expanded;
        
        // Update button icon
        UpdateButtonIcon();
        
        // Update panel height (scroll view will handle content visibility automatically)
        if (animate && useAnimation)
        {
            StartCoroutine(AnimatePanelHeight());
        }
        else
        {
            SetPanelHeight(isExpanded ? expandedHeight : collapsedHeight);
        }
    }
    
    void UpdateButtonIcon()
    {
        if (buttonText == null) return;
        
        buttonText.text = isExpanded ? expandedIcon : collapsedIcon;
    }
    
    IEnumerator AnimatePanelHeight()
    {
        isAnimating = true;
        
        float startHeight = panelRectTransform.sizeDelta.y;
        float targetHeight = isExpanded ? expandedHeight : collapsedHeight;
        float elapsedTime = 0f;
        
        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.unscaledDeltaTime; // Use unscaled time in case game is paused
            float progress = elapsedTime / animationDuration;
            
            // Apply animation curve
            float curvedProgress = animationCurve.Evaluate(progress);
            
            // Interpolate height
            float currentHeight = Mathf.Lerp(startHeight, targetHeight, curvedProgress);
            SetPanelHeight(currentHeight);
            
            yield return null;
        }
        
        // Ensure we end at exact target height
        SetPanelHeight(targetHeight);
        isAnimating = false;
    }
    
    void SetPanelHeight(float height)
    {
        if (panelRectTransform == null) return;
        
        Vector2 sizeDelta = panelRectTransform.sizeDelta;
        sizeDelta.y = height;
        panelRectTransform.sizeDelta = sizeDelta;
    }
    
    // Public methods for external control
    public void ExpandPanel()
    {
        if (!isExpanded)
            TogglePanel();
    }
    
    public void CollapsePanel()
    {
        if (isExpanded)
            TogglePanel();
    }
    
    public void SetExpanded(bool expanded)
    {
        if (isExpanded != expanded)
            TogglePanel();
    }
    
    public bool IsExpanded()
    {
        return isExpanded;
    }
    
    public bool IsAnimating()
    {
        return isAnimating;
    }
    
    // Methods to update panel dimensions (useful for dynamic content)
    public void SetCollapsedHeight(float height)
    {
        collapsedHeight = height;
        if (!isExpanded)
            SetPanelHeight(collapsedHeight);
    }
    
    public void SetExpandedHeight(float height)
    {
        expandedHeight = height;
        if (isExpanded)
            SetPanelHeight(expandedHeight);
    }

    public bool IsUIOpen()
    {
        return isExpanded;
    }

    // Debug methods
    [ContextMenu("Toggle Panel")]
    public void DebugTogglePanel()
    {
        TogglePanel();
    }
    
    [ContextMenu("Expand Panel")]
    public void DebugExpandPanel()
    {
        ExpandPanel();
    }
    
    [ContextMenu("Collapse Panel")]
    public void DebugCollapsePanel()
    {
        CollapsePanel();
    }
    
    [ContextMenu("Print Panel Info")]
    public void DebugPrintPanelInfo()
    {
        Debug.Log($"=== EXPANDABLE PANEL INFO ===");
        Debug.Log($"State: {(isExpanded ? "Expanded" : "Collapsed")}");
        Debug.Log($"Current Height: {panelRectTransform.sizeDelta.y}");
        Debug.Log($"Collapsed Height: {collapsedHeight}");
        Debug.Log($"Expanded Height: {expandedHeight}");
        Debug.Log($"Is Animating: {isAnimating}");
        Debug.Log($"Animation Enabled: {useAnimation}");
    }
}