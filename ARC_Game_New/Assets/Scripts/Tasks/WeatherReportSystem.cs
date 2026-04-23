using UnityEngine;
using System.Collections.Generic;
using TMPro;

public class WeatherReportSystem : MonoBehaviour
{
    [Header("System References")]
    public WeatherSystem weatherSystem;
    public FloodSystem floodSystem;

    [Header("Today's Disaster Info UI Reference")]
    public TextMeshProUGUI FoodDemandText;
    public TextMeshProUGUI LodgingDemandText;
    public TextMeshProUGUI EmergencyPossibilityText;
    public TextMeshProUGUI FloodingExpansionText;

    [Header("Report Settings")]
    public Sprite reportTaskImage;
    public bool enableDailyReports = true;
    public bool showDebugInfo = true;
    
    void Start()
    {
        // Subscribe to round changes
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged += OnTimeSegmentChanged;
        }
        
        // Find systems if not assigned
        if (weatherSystem == null)
            weatherSystem = FindObjectOfType<WeatherSystem>();
        
        if (floodSystem == null)
            floodSystem = FindObjectOfType<FloodSystem>();
    }
    
    void OnTimeSegmentChanged(int newRound)
    {
        // Generate daily report at start of each day (round 0)
        if (newRound == 0 && enableDailyReports)
        {
            GenerateDailyReport();
        }
    }
    
    void GenerateDailyReport()
    {
        if (AlertUIController.Instance == null)
        {
            Debug.LogWarning("AlertUIController not found - cannot show daily report");
            return;
        }
        
        // Create daily report alert
        GameTask dailyReport = CreateDailyReportAlert();
        
        if (dailyReport != null)
        {
            AlertUIController.Instance.ShowAlert(dailyReport);
            
            if (showDebugInfo)
                Debug.Log("Generated daily weather and disaster report");
        }
    }
    
    GameTask CreateDailyReportAlert()
    {
        GameTask report = TaskSystem.Instance.CreateTask($"Day {GlobalClock.Instance.GetCurrentDay()} Start of Day Report", TaskType.Alert, "Daily Report", "Daily weather and disaster situation report");

        report.taskImage = reportTaskImage;
        report.agentMessages = new List<AgentMessage>();

        report.agentMessages.Add(new AgentMessage(GenerateSituationSummary(), null));
        report.agentMessages.Add(new AgentMessage(GenerateActionableOutlook(), null));

        return report;
    }

    string GenerateSituationSummary()
    {
        string summary = "Good morning. Here's the situation:\n";

        // Weather
        if (weatherSystem != null)
        {
            switch (weatherSystem.GetCurrentWeather())
            {
                case WeatherType.Sunny:
                    summary += "☀️ Weather: Clear — no rain expected today.\n";
                    FloodingExpansionText.text = "None";
                    break;
                case WeatherType.SmallRain:
                    summary += "🌦️ Weather: Light rain — minor flooding possible in low areas.\n";
                    FloodingExpansionText.text = "Low";
                    break;
                case WeatherType.MediumRain:
                    summary += "🌧️ Weather: Steady rain — flooding likely to spread.\n";
                    FloodingExpansionText.text = "Medium";
                    break;
                case WeatherType.HeavyRain:
                    summary += "🌧️ Weather: Heavy rain — flooding will worsen today.\n";
                    FloodingExpansionText.text = "High";
                    break;
                case WeatherType.Storm:
                    summary += "⛈️ Weather: Storm — severe flooding expected. High risk of new emergencies.\n";
                    FloodingExpansionText.text = "High";
                    break;
            }
        }

        // Flood situation
        if (floodSystem != null)
        {
            int floodTiles = floodSystem.GetFloodTileCount();
            int affectedFacilities = CountFloodAffectedFacilities();

            if (floodTiles == 0)
            {
                summary += "✅ Flooding: None — all areas are clear.\n";
                LodgingDemandText.text = "Normal";
                EmergencyPossibilityText.text = "Low";
            }
            else if (floodTiles <= 10)
            {
                summary += "⚠️ Flooding: Limited to a small area.";
                if (affectedFacilities > 0)
                {
                    summary += $" {affectedFacilities} shelter(s) affected — capacity reduced.";
                    LodgingDemandText.text = "High";
                }
                else
                {
                    summary += " No shelters directly affected.";
                    LodgingDemandText.text = "Normal";
                }
                summary += "\n";
                EmergencyPossibilityText.text = "Low";
            }
            else if (floodTiles <= 20)
            {
                summary += $"⚠️ Flooding: Spreading across several neighborhoods.";
                if (affectedFacilities > 0)
                {
                    summary += $" {affectedFacilities} shelter(s) flooded — displaced residents need housing.";
                    LodgingDemandText.text = "High";
                }
                else
                {
                    LodgingDemandText.text = "Normal";
                }
                summary += "\n";
                EmergencyPossibilityText.text = "Medium";
            }
            else
            {
                summary += $"🚨 Flooding: Large parts of the city are underwater.";
                if (affectedFacilities > 0)
                {
                    summary += $" {affectedFacilities} shelter(s) are flooded — many residents need immediate housing.";
                    LodgingDemandText.text = "High";
                }
                else
                {
                    LodgingDemandText.text = "High";
                }
                summary += "\n";
                EmergencyPossibilityText.text = "High";
            }
        }

        // Food demand (weather-driven)
        if (weatherSystem != null && weatherSystem.IsRaining())
        {
            FoodDemandText.text = weatherSystem.GetRainIntensity() > 0.5f ? "High" : "Medium";
        }
        else
        {
            FoodDemandText.text = "Normal";
        }

        return summary;
    }

    string GenerateActionableOutlook()
    {
        if (weatherSystem == null) return "No forecast available.";

        string outlook = "What to focus on today:\n";
        float rain = weatherSystem.GetRainIntensity();
        bool flooding = floodSystem != null && floodSystem.GetFloodTileCount() > 0;
        int affectedFacilities = flooding ? CountFloodAffectedFacilities() : 0;

        if (rain > 0.6f || (floodSystem != null && floodSystem.GetFloodTileCount() > 20))
        {
            outlook += "• Expect rescue and evacuation requests — keep vehicles ready.\n";
            outlook += "• Shelters may fill up quickly. Open additional capacity if you can.\n";
        }
        else if (rain > 0.3f || flooding)
        {
            outlook += "• Monitor shelter availability — demand may rise.\n";
            if (flooding) outlook += "• Some roads may be blocked. Plan deliveries around flood areas.\n";
        }
        else
        {
            outlook += "• Conditions are stable — good time to train staff or restock supplies.\n";
        }

        if (affectedFacilities > 0)
            outlook += $"• {affectedFacilities} shelter(s) are out of action due to flooding. Relocate residents as soon as possible.\n";

        return outlook;
    }
    
    int CountFloodAffectedFacilities()
    {
        if (floodSystem == null) return 0;
        
        int count = 0;
        
        // Check buildings
        Building[] buildings = FindObjectsOfType<Building>();
        foreach (Building building in buildings)
        {
            if (IsFloodAffected(building.transform.position))
                count++;
        }
        
        // Check prebuilts
        PrebuiltBuilding[] prebuilts = FindObjectsOfType<PrebuiltBuilding>();
        foreach (PrebuiltBuilding prebuilt in prebuilts)
        {
            if (IsFloodAffected(prebuilt.transform.position))
                count++;
        }
        
        return count;
    }
    
    bool IsFloodAffected(Vector3 facilityPos)
    {
        if (floodSystem == null) return false;
        
        // Check 1-tile radius around facility
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                Vector3 checkPos = facilityPos + new Vector3(x, y, 0);
                if (floodSystem.IsFloodedAt(checkPos))
                    return true;
            }
        }
        
        return false;
    }
    
    void OnDestroy()
    {
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged -= OnTimeSegmentChanged;
        }
    }
    
    [ContextMenu("Test: Generate Daily Report")]
    public void TestGenerateDailyReport()
    {
        GenerateDailyReport();
    }
}