using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public enum WeatherType
{
    Sunny,
    SmallRain,
    MediumRain,
    HeavyRain,
    Storm
}


[System.Serializable]
public class WeatherData
{
    public WeatherType weatherType;
    public Sprite weatherSprite;
    [Range(0f, 1f)]
    public float probability = 0.2f; // Default 20% chance
}

public class WeatherSystem : MonoBehaviour
{
    [Header("Weather Configuration")]
    public WeatherData[] weatherTypes = new WeatherData[5];
    
    [Header("UI References")]
    public Image weatherIcon;
    
    [Header("Debug Panel")]
    public GameObject debugPanel;
    public TMP_Dropdown weatherDropdown;
    public Button applyWeatherButton;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Current weather state
    private WeatherType currentWeather = WeatherType.Sunny;
    
    // Events
    public event Action<WeatherType> OnWeatherChanged;
    
    // Singleton for easy access
    public static WeatherSystem Instance { get; private set; }
    
    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void Start()
    {
        InitializeWeatherSystem();
        SetupDebugPanelMethods();

        // Subscribe to day changes from GlobalClock
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnDayChanged += OnDayChanged;
        }
        
        // Set initial weather
        GenerateRandomWeather();
        
        if (showDebugInfo)
            Debug.Log("Weather System initialized");
    }
    
    void InitializeWeatherSystem()
    {
        // Validate weather data
        if (weatherTypes.Length != 5)
        {
            Debug.LogError("WeatherSystem: Must have exactly 5 weather types!");
            return;
        }
        
        // Initialize default weather types if not set
        for (int i = 0; i < weatherTypes.Length; i++)
        {
            if (weatherTypes[i] == null)
                weatherTypes[i] = new WeatherData();
            
            weatherTypes[i].weatherType = (WeatherType)i;
        }
    }
    
    void SetupDebugPanelMethods()
    {
        // Setup weather dropdown
        if (weatherDropdown != null)
        {
            weatherDropdown.ClearOptions();
            
            // Add weather options to dropdown
            for (int i = 0; i < weatherTypes.Length; i++)
            {
                weatherDropdown.options.Add(new TMP_Dropdown.OptionData(weatherTypes[i].weatherType.ToString()));
            }
            
            weatherDropdown.value = (int)currentWeather;
            weatherDropdown.RefreshShownValue();
        }
        
        // Setup apply button
        if (applyWeatherButton != null)
        {
            applyWeatherButton.onClick.AddListener(OnApplyWeatherClicked);
        }
        
    }
    
    void OnDayChanged(int newDay)
    {
        // Generate new weather for the new day
        GenerateRandomWeather();
        
        if (showDebugInfo)
            Debug.Log($"New day weather generated for Day {newDay}");
    }
    
    void GenerateRandomWeather()
    {
        // Calculate total probability
        float totalProbability = 0f;
        foreach (WeatherData weather in weatherTypes)
        {
            totalProbability += weather.probability;
        }
        
        // Generate random value
        float randomValue = UnityEngine.Random.Range(0f, totalProbability);
        
        // Select weather based on probability
        float cumulativeProbability = 0f;
        for (int i = 0; i < weatherTypes.Length; i++)
        {
            cumulativeProbability += weatherTypes[i].probability;
            
            if (randomValue <= cumulativeProbability)
            {
                SetWeather(weatherTypes[i].weatherType);
                return;
            }
        }
        
        // Fallback to sunny weather
        SetWeather(WeatherType.Sunny);
    }
    
    public void SetWeather(WeatherType newWeather)
    {
        if (currentWeather == newWeather) return;
        
        WeatherType previousWeather = currentWeather;
        currentWeather = newWeather;
        
        // Update weather icon
        UpdateWeatherIcon();
        
        // Notify other systems
        OnWeatherChanged?.Invoke(currentWeather);
        
        if (showDebugInfo)
            Debug.Log($"Weather changed from {previousWeather} to {currentWeather}");
    }
    
    void UpdateWeatherIcon()
    {
        if (weatherIcon == null) return;
        
        // Find the sprite for current weather
        foreach (WeatherData weather in weatherTypes)
        {
            if (weather.weatherType == currentWeather)
            {
                weatherIcon.sprite = weather.weatherSprite;
                break;
            }
        }
    }
    
    void OnApplyWeatherClicked()
    {
        if (weatherDropdown == null) return;
        
        // Get selected weather from dropdown
        int selectedIndex = weatherDropdown.value;
        
        if (selectedIndex >= 0 && selectedIndex < weatherTypes.Length)
        {
            WeatherType selectedWeather = weatherTypes[selectedIndex].weatherType;
            SetWeather(selectedWeather);
            
            if (showDebugInfo)
                Debug.Log($"Weather manually set to {selectedWeather} via debug panel");
        }
    }
    
    // Public methods for other systems
    public WeatherType GetCurrentWeather()
    {
        return currentWeather;
    }
    
    public bool IsRaining()
    {
        return currentWeather == WeatherType.SmallRain || 
               currentWeather == WeatherType.MediumRain || 
               currentWeather == WeatherType.HeavyRain || 
               currentWeather == WeatherType.Storm;
    }
    
    public bool IsStorm()
    {
        return currentWeather == WeatherType.Storm;
    }
    
    public float GetRainIntensity()
    {
        switch (currentWeather)
        {
            case WeatherType.Sunny: return 0f;
            case WeatherType.SmallRain: return 0.3f;
            case WeatherType.MediumRain: return 0.6f;
            case WeatherType.HeavyRain: return 0.8f;
            case WeatherType.Storm: return 1f;
            default: return 0f;
        }
    }
    
    // Debug methods
    [ContextMenu("Generate Random Weather")]
    public void DebugGenerateRandomWeather()
    {
        GenerateRandomWeather();
    }

    
    [ContextMenu("Print Weather Probabilities")]
    public void PrintWeatherProbabilities()
    {
        Debug.Log("=== WEATHER PROBABILITIES ===");
        float totalProbability = 0f;
        
        foreach (WeatherData weather in weatherTypes)
        {
            float percentage = weather.probability * 100f;
            Debug.Log($"{weather.weatherType}: {percentage:F1}%");
            totalProbability += weather.probability;
        }
        
        Debug.Log($"Total Probability: {totalProbability:F2} (should be ~1.0)");
        
        if (Mathf.Abs(totalProbability - 1f) > 0.1f)
        {
            Debug.LogWarning("Weather probabilities don't add up to 1.0! Consider adjusting values.");
        }
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnDayChanged -= OnDayChanged;
        }
    }
}