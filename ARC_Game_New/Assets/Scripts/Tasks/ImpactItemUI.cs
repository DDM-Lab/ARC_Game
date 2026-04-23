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
    [Header("Impact Icons")]
    public Sprite SatisfactionImpactIcon;
    public Sprite BudgetImpactIcon;
    public Sprite TotalTimeImpactIcon;
    public Sprite TrainingTimeImpactIcon;
    public Sprite FoodPacksImpactIcon;
    public Sprite ClientsImpactIcon;
    public Sprite WorkforceImpactIcon;
    public Sprite TotalLodgingImpactIcon;
    
    private TaskImpact impact;
    
    public void Initialize(TaskImpact taskImpact)
    {
        impact = taskImpact;
        UpdateDisplay();
    }
    
    void UpdateDisplay()
    {
        if (impact == null) return;
        
        if (iconImage != null)
        {
            iconImage.sprite = GetImpactSprite(impact.impactType);
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
            else if (impact.impactType == ImpactType.Satisfaction || impact.impactType == ImpactType.Budget)
            {
                // only display a trend for satisfaction and budget
                valueText.text = impact.value > 0 ? "Up" : "Down";
                
            }
            else if (impact.impactType == ImpactType.TotalTime)
            {
                // only display a trend for satisfaction and budget
                valueText.text = impact.value.ToString() + " Rounds";
                
            }
            else
            {
                string prefix = impact.value > 0 ? "" : "";
                valueText.text = prefix + impact.value.ToString();
            }
        }
    }

    Sprite GetImpactSprite(ImpactType impactType)
    {
        switch (impactType)
        {
            case ImpactType.Satisfaction:
                return SatisfactionImpactIcon;
            case ImpactType.Budget:
            case ImpactType.TotalCosts:
                return BudgetImpactIcon;
            case ImpactType.TotalTime:
                return TotalTimeImpactIcon;
            case ImpactType.TrainingTime:
                return TrainingTimeImpactIcon;
            case ImpactType.FoodPacks:
                return FoodPacksImpactIcon;
            case ImpactType.Clients:
                return ClientsImpactIcon;
            case ImpactType.Workforce:
                return WorkforceImpactIcon;
            case ImpactType.TotalLodging:
                return TotalLodgingImpactIcon;
            default:
                return null; // or a default sprite
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

