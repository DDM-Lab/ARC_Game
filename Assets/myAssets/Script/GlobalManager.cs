using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Now, GlobalManager is accessible from anywhere in the project using
// ex. GlobalManager.Instance.roundCount
public class GlobalManager : MonoBehaviour
{
    public static GlobalManager Instance { get; private set; }  // Singleton

    // Global Variables
    public int roundCount = 1;
    public int round2Day = 9;
    public int dayCount = 1;
    public bool debugMode = false;
    public GlobalEnums.WeatherType currentWeather = GlobalEnums.WeatherType.Stormy;
    public float SunnyChance = 0.0f;
    public float RainyChance = 0.4f;


    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            UpdateWeather(); // Initialize weather
            DontDestroyOnLoad(gameObject); // Persist across scenes
        }
        else
        {
            Destroy(gameObject); // Prevent duplicates
        }
    }

    public void AdvanceRound()
    {
        roundCount++;
        if (roundCount % round2Day == 0) // Every round2Day rounds is a new day
        {
            dayCount++;
            UpdateWeather();
        }

        Debug.Log($"Round {roundCount}, Day {dayCount}, {round2Day} rounds for a day, Weather: {currentWeather}");
    }
    private void UpdateWeather()
    {
        float randomValue = Random.value;

        if (randomValue < SunnyChance)
            currentWeather = GlobalEnums.WeatherType.Rainy;
        else if (randomValue < SunnyChance + RainyChance)
            currentWeather = GlobalEnums.WeatherType.Rainy;
        else
            currentWeather = GlobalEnums.WeatherType.Stormy;

        Debug.Log($"Weather changed to {currentWeather}");
    }

    public void ToggleDebug()
    {
        debugMode = !debugMode;
        Debug.Log($"Debug mode is now: {debugMode}");
    }
}
