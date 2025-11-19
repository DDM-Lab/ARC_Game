using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class RainEffectController : MonoBehaviour
{
    [Header("Particle System")]
    private ParticleSystem rainParticles;
    
    [Header("Emission Rates by Weather")]
    public float sunnyEmission = 0f; 
    public float smallRainEmission = 50f;
    public float mediumRainEmission = 150f;
    public float heavyRainEmission = 300f;
    public float stormEmission = 500f;
    
    void Start()
    {
        rainParticles = GetComponent<ParticleSystem>();
        
        // Subscribe to weather changes
        if (WeatherSystem.Instance != null)
        {
            WeatherSystem.Instance.OnWeatherChanged += OnWeatherChanged;
            
            // Set initial emission based on current weather
            OnWeatherChanged(WeatherSystem.Instance.GetCurrentWeather());
        }
    }
    
    void OnWeatherChanged(WeatherType newWeather)
    {
        float emissionRate = newWeather switch
        {
            WeatherType.Sunny => sunnyEmission,
            WeatherType.SmallRain => smallRainEmission,
            WeatherType.MediumRain => mediumRainEmission,
            WeatherType.HeavyRain => heavyRainEmission,
            WeatherType.Storm => stormEmission,
            _ => 0f
        };
        
        SetEmissionRate(emissionRate);
    }
    
    void SetEmissionRate(float rate)
    {
        var emission = rainParticles.emission;
        emission.rateOverTime = rate;
        
        // If no rain (sunny), clear existing particles immediately
        if (rate <= 0f)
        {
            rainParticles.Clear();
        }
    }

    void OnDestroy()
    {
        if (WeatherSystem.Instance != null)
        {
            WeatherSystem.Instance.OnWeatherChanged -= OnWeatherChanged;
        }
    }
}