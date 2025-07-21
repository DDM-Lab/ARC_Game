using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

// Impact Item UI Component
public class ImpactItemUI : MonoBehaviour
{
    [Header("UI Components")]
    public Image iconImage;
    public TextMeshProUGUI labelText;
    public TextMeshProUGUI valueText;
    
    private TaskImpact impact;
    
    public void Initialize(TaskImpact taskImpact)
    {
        impact = taskImpact;
        UpdateDisplay();
    }
    
    void UpdateDisplay()
    {
        if (impact == null) return;
        
        // Set icon (could use sprite instead of text)
        if (iconImage != null)
        {
            // For now, we'll set the icon as text - you can replace with sprite lookup
            string iconText = TaskSystem.GetImpactIcon(impact.impactType);
            // iconImage.sprite = GetImpactSprite(impact.impactType);
        }
        
        // Set label
        if (labelText != null)
        {
            string label = !string.IsNullOrEmpty(impact.customLabel) 
                ? impact.customLabel 
                : TaskSystem.GetImpactLabel(impact.impactType);
            labelText.text = label;
        }
        
        // Set value
        if (valueText != null)
        {
            if (impact.isCountdown)
            {
                valueText.text = FormatCountdown(impact.value);
            }
            else
            {
                string prefix = impact.value > 0 ? "+" : "";
                valueText.text = prefix + impact.value.ToString();
            }
        }
    }
    
    string FormatCountdown(int seconds)
    {
        if (seconds <= 0) return "00:00";
        
        int minutes = seconds / 60;
        int remainingSeconds = seconds % 60;
        return $"{minutes:D2}:{remainingSeconds:D2}";
    }
    
    void Update()
    {
        // Update countdown values in real-time
        if (impact != null && impact.isCountdown)
        {
            UpdateDisplay();
        }
    }
}

