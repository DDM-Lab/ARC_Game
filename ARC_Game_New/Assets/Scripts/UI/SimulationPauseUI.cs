using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.EventSystems;

public class SimulationPauseUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject pausePanel;
    public Button pauseResumeButton;
    public Image buttonImage;
    public TMP_Dropdown speedDropdown;
    public TextMeshProUGUI hoverText;
    
    [Header("Button Sprites")]
    public Sprite pauseSprite;
    public Sprite playSprite;
    
    [Header("Messages")]
    public string playerActionMessage = "Game paused for decisions. Click Execute to begin simulation.";
    public string speedLockedMessage = "Cannot change speed during active simulation.";
    
    [Header("Popup Settings")]
    public float displayDuration = 3f;
    public float fadeInDuration = 0.3f;
    public float fadeOutDuration = 0.3f;
    
    private bool isPaused = false;
    private bool isInSimulation = false;
    private Coroutine popupCoroutine;
    
    void Start()
    {
        if (pauseResumeButton != null)
        {
            pauseResumeButton.onClick.AddListener(OnPauseResumeClicked);
            
            EventTrigger pauseTrigger = pauseResumeButton.gameObject.GetComponent<EventTrigger>();
            if (pauseTrigger == null)
                pauseTrigger = pauseResumeButton.gameObject.AddComponent<EventTrigger>();
            
            EventTrigger.Entry pauseEntry = new EventTrigger.Entry();
            pauseEntry.eventID = EventTriggerType.PointerClick;
            pauseEntry.callback.AddListener((data) => { OnPauseButtonClicked(); });
            pauseTrigger.triggers.Add(pauseEntry);
        }
        
        if (speedDropdown != null)
        {
            speedDropdown.onValueChanged.AddListener(OnSpeedChanged);
            
            EventTrigger speedTrigger = speedDropdown.gameObject.GetComponent<EventTrigger>();
            if (speedTrigger == null)
                speedTrigger = speedDropdown.gameObject.AddComponent<EventTrigger>();
            
            EventTrigger.Entry speedEntry = new EventTrigger.Entry();
            speedEntry.eventID = EventTriggerType.PointerClick;
            speedEntry.callback.AddListener((data) => { OnSpeedDropdownClicked(); });
            speedTrigger.triggers.Add(speedEntry);
        }
        
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnSimulationStarted += OnSimulationStarted;
            GlobalClock.Instance.OnSimulationEnded += OnSimulationEnded;
        }
        
        if (hoverText != null)
        {
            Color c = hoverText.color;
            c.a = 0f;
            hoverText.color = c;
            hoverText.gameObject.SetActive(false);
        }
        
        SetPlayerActionState();
    }
    
    void SetPlayerActionState()
    {
        isInSimulation = false;
        isPaused = false;
        
        if (pausePanel != null)
            pausePanel.SetActive(true);
        
        if (pauseResumeButton != null)
            pauseResumeButton.interactable = false;
        
        if (buttonImage != null)
            buttonImage.sprite = pauseSprite;
        
        if (speedDropdown != null)
            speedDropdown.interactable = true;
    }
    
    void OnSimulationStarted()
    {
        isInSimulation = true;
        isPaused = false;
        
        if (pauseResumeButton != null)
            pauseResumeButton.interactable = true;
        
        if (speedDropdown != null)
            speedDropdown.interactable = false;
        
        UpdateButtonSprite();
        
        if (pausePanel != null)
            pausePanel.SetActive(true);
    }
    
    void OnSimulationEnded()
    {
        SetPlayerActionState();
    }
    
    void OnPauseButtonClicked()
    {
        if (!isInSimulation)
        {
            ShowHoverText(playerActionMessage);
        }
    }
    
    void OnPauseResumeClicked()
    {
        if (!isInSimulation || GlobalClock.Instance == null) return;
        
        isPaused = !isPaused;
        
        if (isPaused)
        {
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = (int)GlobalClock.Instance.currentTimeSpeed;
        }
        
        UpdateButtonSprite();
    }
    
    void OnSpeedChanged(int value)
    {
        if (speedDropdown != null && speedDropdown.interactable)
        {
            if (GlobalClock.Instance != null && isInSimulation && !isPaused)
            {
                Time.timeScale = (int)GlobalClock.Instance.currentTimeSpeed;
            }
        }
    }
    
    void OnSpeedDropdownClicked()
    {
        if (speedDropdown != null && !speedDropdown.interactable && isInSimulation)
        {
            ShowHoverText(speedLockedMessage);
        }
    }
    
    void UpdateButtonSprite()
    {
        if (buttonImage != null)
        {
            buttonImage.sprite = isPaused ? playSprite : pauseSprite;
        }
    }
    
    void ShowHoverText(string message)
    {
        if (hoverText == null) return;
        
        if (popupCoroutine != null)
            StopCoroutine(popupCoroutine);
        
        popupCoroutine = StartCoroutine(PopupTextCoroutine(message));
    }
    
    IEnumerator PopupTextCoroutine(string message)
    {
        hoverText.text = message;
        hoverText.gameObject.SetActive(true);
        
        // Fade in
        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(elapsed / fadeInDuration);
            Color c = hoverText.color;
            c.a = alpha;
            hoverText.color = c;
            yield return null;
        }
        
        // Ensure fully visible
        Color fullColor = hoverText.color;
        fullColor.a = 1f;
        hoverText.color = fullColor;
        
        // Wait for display duration
        yield return new WaitForSecondsRealtime(displayDuration);
        
        // Fade out
        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / fadeOutDuration);
            Color c = hoverText.color;
            c.a = alpha;
            hoverText.color = c;
            yield return null;
        }
        
        // Ensure fully transparent
        Color transparentColor = hoverText.color;
        transparentColor.a = 0f;
        hoverText.color = transparentColor;
        
        hoverText.gameObject.SetActive(false);
    }
    
    void OnDestroy()
    {
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnSimulationStarted -= OnSimulationStarted;
            GlobalClock.Instance.OnSimulationEnded -= OnSimulationEnded;
        }
        
        if (speedDropdown != null)
            speedDropdown.onValueChanged.RemoveListener(OnSpeedChanged);
    }
}