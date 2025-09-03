using UnityEngine;
using System.Collections;

public enum SFXType
{
    UIClick,
    UICancel,
    Success,
    Fail,
    MessagePopup,
    Switch,
    Skip
}

public class AudioManager : MonoBehaviour
{
    [Header("Audio Sources")]
    public AudioSource bgmSource;
    public AudioSource sfxSource;
    public AudioSource ambienceSource;
    
    [Header("Background Music")]
    public AudioClip mainBGM;
    
    [Header("Sound Effects")]
    public AudioClip uiClickSFX;
    public AudioClip uiCancelSFX;
    public AudioClip successSFX;
    public AudioClip failSFX;
    public AudioClip messagePopupSFX;
    public AudioClip switchSFX;
    public AudioClip skipSFX;

    [Header("Weather Ambience")]
    public AudioClip sunnyAmbience;
    public AudioClip smallRainAmbience;
    public AudioClip mediumRainAmbience;
    public AudioClip heavyRainAmbience;
    public AudioClip stormAmbience;
    
    [Header("Volume Settings")]
    [Range(0f, 1f)]
    public float bgmVolume = 0.7f;
    [Range(0f, 1f)]
    public float sfxVolume = 1f;
    [Range(0f, 1f)]
    public float ambienceVolume = 0.5f;
    
    [Header("Fade Settings")]
    public float ambienceFadeDuration = 2f;
    
    private WeatherType currentWeather = WeatherType.Sunny;
    private Coroutine ambienceFadeCoroutine;
    
    // Singleton
    public static AudioManager Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SetupAudioSources();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        StartBGM();
        StartAmbience(WeatherType.Sunny);
    }
    
    void SetupAudioSources()
    {
        // Create audio sources if not assigned
        if (bgmSource == null)
        {
            GameObject bgmObj = new GameObject("BGM Source");
            bgmObj.transform.SetParent(transform);
            bgmSource = bgmObj.AddComponent<AudioSource>();
            bgmSource.loop = true;
            bgmSource.volume = bgmVolume;
        }
        
        if (sfxSource == null)
        {
            GameObject sfxObj = new GameObject("SFX Source");
            sfxObj.transform.SetParent(transform);
            sfxSource = sfxObj.AddComponent<AudioSource>();
            sfxSource.volume = sfxVolume;
        }
        
        if (ambienceSource == null)
        {
            GameObject ambienceObj = new GameObject("Ambience Source");
            ambienceObj.transform.SetParent(transform);
            ambienceSource = ambienceObj.AddComponent<AudioSource>();
            ambienceSource.loop = true;
            ambienceSource.volume = ambienceVolume;
        }
    }
    
    public void StartBGM()
    {
        if (bgmSource != null && mainBGM != null)
        {
            bgmSource.clip = mainBGM;
            bgmSource.volume = bgmVolume;
            bgmSource.Play();
            Debug.Log("BGM started");
        }
    }
    
    public void StopBGM()
    {
        if (bgmSource != null)
        {
            bgmSource.Stop();
        }
    }
    
    public void PlaySFX(SFXType sfxType)
    {
        if (sfxSource == null) return;
        
        AudioClip clipToPlay = GetSFXClip(sfxType);
        if (clipToPlay != null)
        {
            sfxSource.volume = sfxVolume;
            sfxSource.PlayOneShot(clipToPlay);
        }
    }

    public void PlayClickSFX()
    {
        PlaySFX(SFXType.UIClick);
    }

    public void PlayCancelSFX()
    {
        PlaySFX(SFXType.UICancel);
    }

    public void PlaySwitchSFX()
    {
        PlaySFX(SFXType.Switch);
    }
    public void PlaySkipSFX()
    {
        PlaySFX(SFXType.Skip);
    }

    AudioClip GetSFXClip(SFXType sfxType)
    {
        switch (sfxType)
        {
            case SFXType.UIClick: return uiClickSFX;
            case SFXType.UICancel: return uiCancelSFX;
            case SFXType.Success: return successSFX;
            case SFXType.Fail: return failSFX;
            case SFXType.MessagePopup: return messagePopupSFX;
            case SFXType.Switch: return switchSFX;
            case SFXType.Skip: return skipSFX;
            default: return null;
        }
    }
    
    public void StartAmbience(WeatherType weatherType)
    {
        if (currentWeather == weatherType) return;
        
        AudioClip newAmbienceClip = GetAmbienceClip(weatherType);
        if (newAmbienceClip != null)
        {
            if (ambienceFadeCoroutine != null)
            {
                StopCoroutine(ambienceFadeCoroutine);
            }
            
            ambienceFadeCoroutine = StartCoroutine(FadeToNewAmbience(newAmbienceClip));
            currentWeather = weatherType;
            
            Debug.Log($"Switching to {weatherType} ambience");
        }
    }
    
    AudioClip GetAmbienceClip(WeatherType weatherType)
    {
        switch (weatherType)
        {
            case WeatherType.Sunny: return sunnyAmbience;
            case WeatherType.SmallRain: return smallRainAmbience;
            case WeatherType.MediumRain: return mediumRainAmbience;
            case WeatherType.HeavyRain: return heavyRainAmbience;
            case WeatherType.Storm: return stormAmbience;
            default: return sunnyAmbience;
        }
    }
    
    IEnumerator FadeToNewAmbience(AudioClip newClip)
    {
        float startVolume = ambienceSource.volume;
        
        // Fade out current ambience
        float elapsed = 0f;
        while (elapsed < ambienceFadeDuration / 2)
        {
            elapsed += Time.unscaledDeltaTime;
            ambienceSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / (ambienceFadeDuration / 2));
            yield return null;
        }
        
        // Change clip
        ambienceSource.clip = newClip;
        ambienceSource.Play();
        
        // Fade in new ambience
        elapsed = 0f;
        while (elapsed < ambienceFadeDuration / 2)
        {
            elapsed += Time.unscaledDeltaTime;
            ambienceSource.volume = Mathf.Lerp(0f, ambienceVolume, elapsed / (ambienceFadeDuration / 2));
            yield return null;
        }
        
        ambienceSource.volume = ambienceVolume;
    }
    
    public void SetBGMVolume(float volume)
    {
        bgmVolume = Mathf.Clamp01(volume);
        if (bgmSource != null)
            bgmSource.volume = bgmVolume;
    }
    
    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        if (sfxSource != null)
            sfxSource.volume = sfxVolume;
    }
    
    public void SetAmbienceVolume(float volume)
    {
        ambienceVolume = Mathf.Clamp01(volume);
        if (ambienceSource != null)
            ambienceSource.volume = ambienceVolume;
    }
    
    // Method to be called by weather system
    public void OnWeatherChanged(WeatherType newWeather)
    {
        StartAmbience(newWeather);
    }
    
    // Context menu methods for testing
    [ContextMenu("Test UI Click")]
    public void TestUIClick() => PlaySFX(SFXType.UIClick);
    
    [ContextMenu("Test Success")]
    public void TestSuccess() => PlaySFX(SFXType.Success);
    
    [ContextMenu("Test Small Rainy Weather")]
    public void TestRainyWeather() => StartAmbience(WeatherType.SmallRain);
}