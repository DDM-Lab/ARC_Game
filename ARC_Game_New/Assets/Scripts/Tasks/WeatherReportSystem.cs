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
            // PARITY BUILD (ledger D23): upstream subscribes to OnTimeSegmentChanged and generates
            // the report from segment 0, not from OnDayChanged. OnDayChanged fires FIRST at the
            // rollover, so ours opens the daily report BEFORE the segment-0 tick and upstream
            // opens it AFTER — the same work either side of a round boundary.
            GlobalClock.Instance.OnTimeSegmentChanged += OnTimeSegmentChanged;
        }
        
        // Find systems if not assigned
        if (weatherSystem == null)
            weatherSystem = FindObjectOfType<WeatherSystem>();
        
        if (floodSystem == null)
            floodSystem = FindObjectOfType<FloodSystem>();
    }
    
    void OnDayChangedReport(int newDay)
    {
        if (enableDailyReports)
            GenerateDailyReport();
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
            GameLogPanel.Instance?.LogEnvironmentChange("Generated daily weather and disaster report");
        }
    }
    
    GameTask CreateDailyReportAlert()
    {
        GameTask report = TaskSystem.Instance.CreateTask($"Day {GlobalClock.Instance.GetCurrentDay()} Morning Report", TaskType.Alert, "Weather Report", "Today's weather and flood outlook");

        report.taskImage = reportTaskImage;
        report.agentMessages = new List<AgentMessage>();

        report.agentMessages.Add(new AgentMessage(GenerateSituationSummary(), null));
        report.agentMessages.Add(new AgentMessage(GenerateActionableOutlook(), null));

        return report;
    }

    string GenerateSituationSummary()
    {
        string summary = "Morning. ";

        // Weather
        if (weatherSystem != null)
        {
            switch (weatherSystem.GetCurrentWeather())
            {
                case WeatherType.Sunny:
                    summary += "☀️ Skies are clear, no rain expected today.\n";
                    FloodingExpansionText.text = "None";
                    break;
                case WeatherType.SmallRain:
                    summary += "🌦️ Light rain today. A few low areas may flood.\n";
                    FloodingExpansionText.text = "Low";
                    break;
                case WeatherType.MediumRain:
                    summary += "🌧️ Steady rain today. Flooding is likely to spread.\n";
                    FloodingExpansionText.text = "Medium";
                    break;
                case WeatherType.HeavyRain:
                    summary += "🌧️ Heavy rain today. Flooding will get worse.\n";
                    FloodingExpansionText.text = "High";
                    break;
                case WeatherType.Storm:
                    summary += "⛈️ A storm is moving in. Expect severe flooding and new emergencies.\n";
                    FloodingExpansionText.text = "High";
                    break;
            }
        }

        // Flood situation
        if (floodSystem != null)
        {
            int floodTiles = floodSystem.GetFloodTileCount();
            int affectedFacilities = CountFloodAffectedFacilities();
            string shelterWord = affectedFacilities == 1 ? "shelter" : "shelters";

            if (floodTiles == 0)
            {
                summary += "✅ All areas are clear.\n";
                LodgingDemandText.text = "Normal";
                EmergencyPossibilityText.text = "Low";
            }
            else if (floodTiles <= 10)
            {
                summary += "⚠️ Flooding is limited to a small area.";
                if (affectedFacilities > 0)
                {
                    summary += $" {affectedFacilities} {shelterWord} have less capacity.";
                    LodgingDemandText.text = "High";
                }
                else
                {
                    summary += " No shelters are affected.";
                    LodgingDemandText.text = "Normal";
                }
                summary += "\n";
                EmergencyPossibilityText.text = "Low";
            }
            else if (floodTiles <= 20)
            {
                summary += "⚠️ Flooding is spreading through several neighborhoods.";
                if (affectedFacilities > 0)
                {
                    summary += $" {affectedFacilities} {shelterWord} flooded, displaced residents need housing.";
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
                summary += "🚨 Much of the city is underwater.";
                if (affectedFacilities > 0)
                {
                    summary += $" {affectedFacilities} {shelterWord} flooded, many residents need housing now.";
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

        string outlook = "Today's focus:\n";
        float rain = weatherSystem.GetRainIntensity();
        bool flooding = floodSystem != null && floodSystem.GetFloodTileCount() > 0;

        if (rain > 0.6f || (floodSystem != null && floodSystem.GetFloodTileCount() > 20))
        {
            outlook += "• Expect rescue and evacuation calls.\n";
            outlook += "• Open extra capacity if you can.\n";
        }
        else if (rain > 0.3f || flooding)
        {
            outlook += "• Watch shelter capacity closely.\n";
            if (flooding) outlook += "• Some roads may be blocked, plan deliveries around flooded areas.\n";
        }
        else
        {
            outlook += "• Conditions are calm. Good day to train staff or restock supplies.\n";
        }

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