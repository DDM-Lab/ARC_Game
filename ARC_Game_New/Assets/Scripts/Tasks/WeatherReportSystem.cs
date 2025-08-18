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
        
        // Generate report messages
        report.agentMessages = new List<AgentMessage>();
        
        // Weather Report and disaster report
        string weatherReport = GenerateWeatherReport();
        string disasterReport = GenerateDisasterReport();  
        report.agentMessages.Add(new AgentMessage(weatherReport + "\n" + disasterReport, null));
        
        // Forecast
        string forecast = GenerateForecast();
        report.agentMessages.Add(new AgentMessage(forecast, null));

        // Emergency preparedness
        string preparednessAdvice = GeneratePreparednessAdvice();
        report.agentMessages.Add(new AgentMessage(preparednessAdvice, null));

        return report;
    }
    
    string GenerateWeatherReport()
    {
        if (weatherSystem == null)
            return "Weather system offline - unable to generate report.";
        
        WeatherType currentWeather = weatherSystem.GetCurrentWeather();
        float rainIntensity = weatherSystem.GetRainIntensity();

        string report = $"Here is your daily weather report:\nCurrent weather: {currentWeather}";

        if (weatherSystem.IsRaining())
        {
            report += $"\nRain intensity: {rainIntensity:F1}";

            if (rainIntensity > 0.7f)
            {
                report += " (Heavy rain - flood risk HIGH)";
                FloodingExpansionText.text = "High";
            }
            else if (rainIntensity > 0.4f)
            {
                report += " (Moderate rain - flood risk MEDIUM)";
                FloodingExpansionText.text = "Medium";
            }
            else
            {
                report += " (Light rain - flood risk LOW)";
                FloodingExpansionText.text = "Low";
            }
        }
        else
        {
            report += "\nNo precipitation expected - flood risk MINIMAL";
            FloodingExpansionText.text = "None";
        }
        
        return report;
    }
    
    string GenerateDisasterReport()
    {
        if (floodSystem == null)
            return "Flood monitoring system offline.";
        
        int currentFloodTiles = floodSystem.GetFloodTileCount();
        string report;

        if (currentFloodTiles == 0)
        {
            report = "✅ No active flood areas detected.\nAll facilities operating at normal capacity.";
        }
        else
        {
            report = $"⚠️ Active flood coverage: {currentFloodTiles} tiles";

            // Check affected facilities
            int affectedFacilities = CountFloodAffectedFacilities();
            if (affectedFacilities > 0)
            {
                report += $"\n🏠 Facilities impacted by flood: {affectedFacilities}";
                report += "\nCapacity reductions may be in effect.";
                LodgingDemandText.text = "High";
            }
            else
            {
                report += "\nNo facilities currently affected by flood.";
                LodgingDemandText.text = "Normal";
            }

            // Flood severity assessment
            if (currentFloodTiles > 20)
            {
                report += "\n🚨 MAJOR flood event - emergency protocols active";
                EmergencyPossibilityText.text = "High";
            }
            else if (currentFloodTiles > 10)
            {
                report += "\n⚠️ MODERATE flood event - monitor closely";
                EmergencyPossibilityText.text = "Medium";
            }
            else
            {
                report += "\n📊 MINOR flood event - manageable impact";
                EmergencyPossibilityText.text = "Low";
            }
        }
        return report;
    }

    string GenerateForecast()
    {
        string forecast = "Expected conditions for today:\n";

        // Weather forecast
        if (weatherSystem != null)
        {
            WeatherType currentWeather = weatherSystem.GetCurrentWeather();
            float rainIntensity = weatherSystem.GetRainIntensity();

            switch (currentWeather)
            {
                case WeatherType.Sunny:
                    forecast += "☀️ Clear skies - optimal conditions for operations\n";
                    break;
                case WeatherType.SmallRain:
                    forecast += "☁️ Overcast conditions - normal operations expected\n";
                    break;
                case WeatherType.MediumRain:
                    forecast += "🌧️ Continued rain - monitor flood development\n";
                    break;
                case WeatherType.HeavyRain:
                    forecast += "🌧️ Severe Rain - monitor flood development\n";
                    break;
                case WeatherType.Storm:
                    forecast += "⛈️ Storm conditions - high flood risk\n";
                    break;
            }

            // Resource demand forecast
            forecast += GenerateResourceDemandForecast();

            return forecast;
        }
        else
        {
            Debug.LogWarning("weatherSystem is null for forecast generation.");
            return "Weather monitoring system offline. No forecast available for today.";
        }
    }
    
    string GenerateResourceDemandForecast()
    {
        string demand = "\n📦 Resource Demand Forecast:\n";
        
        // Get current day for demand patterns
        int currentDay = GlobalClock.Instance != null ? GlobalClock.Instance.GetCurrentDay() : 1;

        // Simple demand patterns based on day
        if (currentDay % 3 == 0)
        {
            demand += "• HIGH demand for food supplies expected\n";
            demand += "• Population movement may increase\n";
            FoodDemandText.text = "High";
        }
        else if (currentDay % 2 == 0)
        {
            demand += "• MODERATE resource consumption expected\n";
            demand += "• Normal population stability\n";
            FoodDemandText.text = "Medium";
        }
        else
        {
            demand += "• LOW to NORMAL demand anticipated\n";
            demand += "• Stable resource requirements\n";
            FoodDemandText.text = "Low";
        }

        // Weather-based adjustments
        if (weatherSystem != null && weatherSystem.IsRaining())
        {
            demand += "• Increased shelter demand due to weather\n";
            demand += "• Emergency supplies may be needed\n";
        }

        return demand;
    }
    
    string GeneratePreparednessAdvice()
    {
        string advice = "💡 Preparedness Recommendations:\n";
        
        // Weather-based advice
        if (weatherSystem != null)
        {
            float rainIntensity = weatherSystem.GetRainIntensity();

            if (rainIntensity > 0.6f)
            {
                advice += "• Prepare for potential evacuations\n";
                advice += "• Ensure emergency vehicles are ready\n";
                advice += "• Monitor shelter capacity closely\n";
                
            }
            else if (rainIntensity > 0.3f)
            {
                advice += "• Keep emergency supplies accessible\n";
                advice += "• Monitor flood-prone areas\n";
            }
            else
            {
                advice += "• Routine maintenance and restocking\n";
                advice += "• Normal operational procedures\n";
            }
        }
        
        // Flood-based advice
        if (floodSystem != null && floodSystem.GetFloodTileCount() > 0)
        {
            advice += "• Review evacuation routes for blocked paths\n";
            advice += "• Consider alternative transportation methods if roads are flooded\n";
        }
        
        return advice;
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