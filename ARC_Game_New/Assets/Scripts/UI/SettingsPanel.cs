using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class SettingsPanel : MonoBehaviour
{
    [Header("Panel")]
    public GameObject settingsPanel;
    public Button exitButton;
    
    [Header("Settings Button")]
    public Button settingsButton; // Button to open settings panel
    
    [Header("Toggles")]
    public Toggle skipAlertsToggle;
    public Toggle skipTypingToggle;
    
    [Header("Volume Sliders")]
    public Slider sfxVolumeSlider;
    public Slider bgmVolumeSlider;
    public TextMeshProUGUI sfxVolumeText;
    public TextMeshProUGUI bgmVolumeText;
    
    [Header("Main Menu")]
    public Button mainMenuButton;
    
    // Settings keys for PlayerPrefs
    private const string SKIP_ALERTS_KEY = "SkipAlerts";
    private const string SKIP_TYPING_KEY = "SkipTyping";
    private const string SFX_VOLUME_KEY = "SFXVolume";
    private const string BGM_VOLUME_KEY = "BGMVolume";
    
    // Static properties for other scripts to check
    public static bool SkipAlerts { get; private set; }
    public static bool SkipTyping { get; private set; }
    
    // Singleton for easy access
    public static SettingsPanel Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        LoadSettings();
        SetupUI();
        
        // Ensure panel is inactive at start
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }
    
    void SetupUI()
    {
        // Settings button to open panel
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OpenSettings);
        
        // Exit button
        if (exitButton != null)
            exitButton.onClick.AddListener(CloseSettings);
        
        // Main Menu button
        if (mainMenuButton != null)
            mainMenuButton.onClick.AddListener(ReturnToMainMenu);
        
        // Skip Alerts toggle
        if (skipAlertsToggle != null)
        {
            skipAlertsToggle.onValueChanged.AddListener(OnSkipAlertsChanged);
        }
        
        // Skip Typing toggle
        if (skipTypingToggle != null)
        {
            skipTypingToggle.onValueChanged.AddListener(OnSkipTypingChanged);
        }
        
        // SFX Volume slider
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
            sfxVolumeSlider.minValue = 0f;
            sfxVolumeSlider.maxValue = 1f;
        }
        
        // BGM Volume slider  
        if (bgmVolumeSlider != null)
        {
            bgmVolumeSlider.onValueChanged.AddListener(OnBGMVolumeChanged);
            bgmVolumeSlider.minValue = 0f;
            bgmVolumeSlider.maxValue = 1f;
        }
    }
    
    void LoadSettings()
    {
        // Load saved settings or use defaults
        SkipAlerts = PlayerPrefs.GetInt(SKIP_ALERTS_KEY, 0) == 1;
        SkipTyping = PlayerPrefs.GetInt(SKIP_TYPING_KEY, 0) == 1;
        float sfxVolume = PlayerPrefs.GetFloat(SFX_VOLUME_KEY, 1f); // Default 100%
        float bgmVolume = PlayerPrefs.GetFloat(BGM_VOLUME_KEY, 0.5f); // Default 50%
        
        // Apply loaded settings to UI
        if (skipAlertsToggle != null)
            skipAlertsToggle.isOn = SkipAlerts;
            
        if (skipTypingToggle != null)
            skipTypingToggle.isOn = SkipTyping;
            
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.value = sfxVolume;
            
        if (bgmVolumeSlider != null)
            bgmVolumeSlider.value = bgmVolume;
        
        // Apply to AudioManager
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetSFXVolume(sfxVolume);
            AudioManager.Instance.SetBGMVolume(bgmVolume);
            AudioManager.Instance.SetAmbienceVolume(bgmVolume); // Ambience uses BGM slider
        }
        
        UpdateVolumeTexts();
    }
    
    void SaveSettings()
    {
        PlayerPrefs.SetInt(SKIP_ALERTS_KEY, SkipAlerts ? 1 : 0);
        PlayerPrefs.SetInt(SKIP_TYPING_KEY, SkipTyping ? 1 : 0);
        PlayerPrefs.SetFloat(SFX_VOLUME_KEY, sfxVolumeSlider.value);
        PlayerPrefs.SetFloat(BGM_VOLUME_KEY, bgmVolumeSlider.value);
        PlayerPrefs.Save();
    }
    
    public void OpenSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
            
            // Pause game
            Time.timeScale = 0f;
            
            // Play UI sound
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(SFXType.UIClick);
        }
    }
    
    public void CloseSettings()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
            
            // Save settings when closing
            SaveSettings();
            
            // Resume game (but check if GlobalClock is paused)
            if (GlobalClock.Instance != null && !GlobalClock.Instance.IsSimulationRunning())
            {
                Time.timeScale = 0f; // Keep paused if game is in paused state
            }
            else
            {
                Time.timeScale = 1f;
            }
            
            // Play UI sound
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(SFXType.UICancel);
        }
    }
    
    void OnSkipAlertsChanged(bool value)
    {
        SkipAlerts = value;
        
        // Play toggle sound
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(SFXType.Switch);
    }
    
    void OnSkipTypingChanged(bool value)
    {
        SkipTyping = value;
        
        // If typing is currently happening, skip it
        if (value)
        {
            // Skip AlertUIController typing
            AlertUIController alertController = FindObjectOfType<AlertUIController>();
            if (alertController != null)
            {
                alertController.SkipTyping();
            }
            
            // Skip TaskDetailUI typing
            TaskDetailUI taskDetailUI = FindObjectOfType<TaskDetailUI>();
            if (taskDetailUI != null)
            {
                taskDetailUI.SkipCurrentTyping();
            }
        }
        
        // Play toggle sound
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(SFXType.Switch);
    }
    
    void OnSFXVolumeChanged(float value)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetSFXVolume(value);
            
            // Play a test sound
            AudioManager.Instance.PlaySFX(SFXType.UIClick);
        }
        
        UpdateVolumeTexts();
    }
    
    void OnBGMVolumeChanged(float value)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetBGMVolume(value);
            AudioManager.Instance.SetAmbienceVolume(value); // Ambience follows BGM
        }
        
        UpdateVolumeTexts();
    }
    
    void UpdateVolumeTexts()
    {
        if (sfxVolumeText != null && sfxVolumeSlider != null)
        {
            sfxVolumeText.text = $"{Mathf.RoundToInt(sfxVolumeSlider.value * 100)}%";
        }
        
        if (bgmVolumeText != null && bgmVolumeSlider != null)
        {
            bgmVolumeText.text = $"{Mathf.RoundToInt(bgmVolumeSlider.value * 100)}%";
        }
    }
    
    void ReturnToMainMenu()
    {
        // Save settings before leaving
        SaveSettings();
        
        // Reset time scale
        Time.timeScale = 1f;
        
        // Load main menu scene (you need to set your main menu scene name)
        SceneManager.LoadScene("MainMenu"); // Change "MainMenu" to your actual scene name
    }
    
    // Optional: Add keyboard shortcut to open settings
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (settingsPanel != null && settingsPanel.activeSelf)
            {
                CloseSettings();
            }
            else
            {
                OpenSettings();
            }
        }
    }
}