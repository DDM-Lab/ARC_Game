using UnityEngine;
using System.Collections;
public class InfoDisplay : MonoBehaviour
{
    [Header("Display Settings")]
    public bool showInfo = true;
    public Vector3 worldOffset = Vector3.up * 1f;
    public int fontSize = 18;
    public bool showBackground = true;
    public Color backgroundColor = new Color(1f, 1f, 1f, 0.8f); // Semi-transparent white
    
    private string displayText = "";
    private Color displayColor = Color.black; // Changed to black for better contrast on white background
    private GUIStyle textStyle;
    private GUIStyle backgroundStyle;
    void Start()
    {
        SetupGUIStyles();
        // Delayed initialization to ensure other components are ready
        Invoke(nameof(DelayedInitialization), 0.1f);
    }

    void DelayedInitialization()
    {
        // Trigger an update to get the correct initial state
        var prebuiltBuilding = GetComponent<PrebuiltBuilding>();
        if (prebuiltBuilding != null)
        {
            prebuiltBuilding.UpdateInfoDisplay();
        }
        
        var vehicle = GetComponent<Vehicle>();
        if (vehicle != null)
        {
            vehicle.UpdateInfoDisplay();
        }
    }
    
    /// <summary>
    /// Initialize GUI styles for text and background rendering
    /// </summary>
    void SetupGUIStyles()
    {
        // Setup text style
        textStyle = new GUIStyle();
        textStyle.normal.textColor = displayColor;
        textStyle.fontSize = fontSize;
        textStyle.alignment = TextAnchor.MiddleCenter;
        textStyle.fontStyle = FontStyle.Bold;
        
        // Setup background style
        if (showBackground)
        {
            backgroundStyle = new GUIStyle();
            Texture2D backgroundTexture = new Texture2D(1, 1);
            backgroundTexture.SetPixel(0, 0, backgroundColor);
            backgroundTexture.Apply();
            backgroundStyle.normal.background = backgroundTexture;
        }
    }
    
    /// <summary>
    /// Update the display text and color
    /// </summary>
    /// <param name="text">Text to display</param>
    /// <param name="color">Text color</param>
    public void UpdateDisplay(string text, Color color)
    {
        displayText = text;
        displayColor = color;
        
        // Update text color in style
        if (textStyle != null)
        {
            textStyle.normal.textColor = displayColor;
        }
    }
    
    /// <summary>
    /// Set the text color only
    /// </summary>
    /// <param name="color">New text color</param>
    public void SetColor(Color color)
    {
        displayColor = color;
        if (textStyle != null)
        {
            textStyle.normal.textColor = displayColor;
        }
    }
    
    /// <summary>
    /// OnGUI is called for rendering and handling GUI events
    /// This method converts world position to screen coordinates and draws the text
    /// </summary>
    void OnGUI()
    {
        if (!showInfo || string.IsNullOrEmpty(displayText)) return;
        
        // Convert world position to screen coordinates
        Vector3 worldPosition = transform.position + worldOffset;
        Vector3 screenPosition = Camera.main.WorldToScreenPoint(worldPosition);
        
        // Check if position is in front of camera and within screen bounds
        if (screenPosition.z > 0 && screenPosition.x >= 0 && screenPosition.x <= Screen.width && 
            screenPosition.y >= 0 && screenPosition.y <= Screen.height)
        {
            // Convert Y coordinate (GUI coordinate system is flipped vertically)
            screenPosition.y = Screen.height - screenPosition.y;
            
            // Ensure styles are initialized
            if (textStyle == null)
            {
                SetupGUIStyles();
            }
            
            // Update text color
            textStyle.normal.textColor = displayColor;
            
            // Calculate text size
            Vector2 textSize = textStyle.CalcSize(new GUIContent(displayText));
            
            // Add padding to make the box larger
            float padding = 8f; // Increased from 4f to make box bigger
            Rect textRect = new Rect(screenPosition.x - textSize.x/2 - padding, 
                                   screenPosition.y - textSize.y/2 - padding/2, 
                                   textSize.x + padding * 2, 
                                   textSize.y + padding);
            
            // Draw background box
            if (showBackground && backgroundStyle != null)
            {
                GUI.Box(textRect, "", backgroundStyle);
            }
            
            // Draw text on top of background
            GUI.Label(textRect, displayText, textStyle);
        }
    }
    
    /// <summary>
    /// Update display text with current color
    /// </summary>
    /// <param name="text">Text to display</param>
    public void UpdateDisplay(string text)
    {
        UpdateDisplay(text, displayColor);
    }
    
    /// <summary>
    /// Toggle visibility of the info display
    /// </summary>
    /// <param name="visible">Whether to show the display</param>
    public void SetVisible(bool visible)
    {
        showInfo = visible;
    }
    
    /// <summary>
    /// Change the font size of the displayed text
    /// </summary>
    /// <param name="size">New font size</param>
    public void SetFontSize(int size)
    {
        fontSize = size;
        if (textStyle != null)
        {
            textStyle.fontSize = fontSize;
        }
    }
}