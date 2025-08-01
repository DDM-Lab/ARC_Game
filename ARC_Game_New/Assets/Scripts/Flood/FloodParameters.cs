using UnityEngine;

[CreateAssetMenu(fileName = "FloodParameters", menuName = "Disaster Game/Flood Parameters")]
public class FloodParameters : ScriptableObject
{
    [Header("Weather-Based Flood Rates")]
    [Tooltip("Flood expansion rate for each weather type (tiles per round)")]
    public WeatherFloodData[] weatherFloodRates = new WeatherFloodData[5];
    
    [Header("Spread Mechanics")]
    [Range(0f, 1f)]
    [Tooltip("Base chance for flood to spread to adjacent tile")]
    public float baseSpreadChance = 0.7f;
    
    [Range(0f, 1f)]
    [Tooltip("Chance for random flood expansion beyond normal spread")]
    public float randomExpansionChance = 0.15f;
    
    [Range(1, 5)]
    [Tooltip("Maximum random expansion distance")]
    public int maxRandomExpansionDistance = 2;
    
    [Header("Terrain Interaction")]
    [Range(0f, 1f)]
    [Tooltip("Spread chance multiplier when spreading over land")]
    public float landSpreadMultiplier = 1f;
    
    [Range(0f, 1f)]
    [Tooltip("Spread chance multiplier when blocked by terrain (forests, mountains)")]
    public float terrainBlockMultiplier = 0.2f;
    
    [Header("Shrinkage Parameters")]
    [Range(0f, 1f)]
    [Tooltip("Base chance for flood tiles to recede during dry weather")]
    public float baseShrinkageChance = 0.1f;
    
    [Range(0f, 1f)]
    [Tooltip("Additional shrinkage chance for edge flood tiles")]
    public float edgeShrinkageBonus = 0.2f;
    
    [Header("Debug")]
    public bool enableDebugLogs = true;
    public bool showFloodSpreadVisualization = false;
    
    void OnValidate()
    {
        // Ensure we have exactly 5 weather entries
        if (weatherFloodRates.Length != 5)
        {
            System.Array.Resize(ref weatherFloodRates, 5);
        }
        
        // Initialize weather types if not set
        for (int i = 0; i < weatherFloodRates.Length; i++)
        {
            if (weatherFloodRates[i] == null)
                weatherFloodRates[i] = new WeatherFloodData();
            
            weatherFloodRates[i].weatherType = (WeatherType)i;
        }
    }
}

[System.Serializable]
public class WeatherFloodData
{
    public WeatherType weatherType;
    
    [Range(0f, 10f)]
    [Tooltip("Number of tiles flood expands per round")]
    public float expansionRate = 1f;
    
    [Range(0f, 1f)]
    [Tooltip("Multiplier for spread chance during this weather")]
    public float spreadChanceMultiplier = 1f;
    
    [Range(0f, 1f)]
    [Tooltip("Chance for flood to shrink during this weather")]
    public float shrinkageChance = 0f;
    
    public WeatherFloodData()
    {
        // Set default values based on weather type
        switch (weatherType)
        {
            case WeatherType.Sunny:
                expansionRate = 0f;
                spreadChanceMultiplier = 0.5f;
                shrinkageChance = 0.3f;
                break;
            case WeatherType.SmallRain:
                expansionRate = 0.5f;
                spreadChanceMultiplier = 0.8f;
                shrinkageChance = 0.1f;
                break;
            case WeatherType.MediumRain:
                expansionRate = 1.5f;
                spreadChanceMultiplier = 1f;
                shrinkageChance = 0f;
                break;
            case WeatherType.HeavyRain:
                expansionRate = 3f;
                spreadChanceMultiplier = 1.2f;
                shrinkageChance = 0f;
                break;
            case WeatherType.Storm:
                expansionRate = 5f;
                spreadChanceMultiplier = 1.5f;
                shrinkageChance = 0f;
                break;
        }
    }
}