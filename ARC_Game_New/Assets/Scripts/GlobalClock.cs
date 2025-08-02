using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System;

public enum TimeState
{
    Paused,     // Player can interact
    Simulating  // Simulation running, no interaction allowed
}

public enum TimeSpeed
{
    Normal = 1,  // 1x speed
    Fast = 2,    // 2x speed
    VeryFast = 4 // 4x speed
}

public class GlobalClock : MonoBehaviour
{
    [Header("Time Control")]
    public TimeSpeed currentTimeSpeed = TimeSpeed.Normal;
    public float simulationDuration = 10f; // Base simulation time in seconds
    
    [Header("Day/Time Management")]
    public int currentDay = 1;
    public int currentTimeSegment = 0; // 0-3 for 9:00, 12:00, 15:00, 18:00
    
    [Header("UI References")]
    public Button executeButton;    
    public TMP_Dropdown speedDropdown;
    public Image[] timeSegmentImages = new Image[4]; // 4 time segments
    
    [Header("Sprite References")]
    public Sprite pastTimeSprite;     // Already passed time segment
    public Sprite currentTimeSprite;  // Current time segment
    public Sprite futureTimeSprite;   // Future time segment
    
    [Header("Day Display")]
    public TextMeshProUGUI dayText;
    public TextMeshProUGUI roundText;
    public TextMeshProUGUI TaskCenterDayRoundText;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Current state
    private TimeState currentState = TimeState.Paused;
    private bool isSimulationRunning = false;
    
    // Events for other systems to listen to
    public event Action OnSimulationStarted;
    public event Action OnSimulationEnded;
    public event Action<int> OnTimeSegmentChanged;
    public event Action<int> OnDayChanged;
    
    // Singleton for easy access
    public static GlobalClock Instance { get; private set; }
    
    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void Start()
    {
        InitializeTimeSystem();
        SetupUI();
        UpdateTimeDisplay();
        
        if (showDebugInfo)
            Debug.Log("Global Clock initialized - Game starts paused at Day 1, Time Segment 1 (9:00)");
    }
    
    void InitializeTimeSystem()
    {
        // Start game in paused state
        currentState = TimeState.Paused;
        Time.timeScale = 0f; // Pause Unity's time
        
        // Initialize day and time segment
        currentDay = 1;
        currentTimeSegment = 0;
    }
    
    void SetupUI()
    {
        // Setup execute button
        if (executeButton != null)
        {
            executeButton.onClick.AddListener(OnExecuteButtonClicked);
        }
        
        // Setup speed dropdown
        if (speedDropdown != null)
        {
            speedDropdown.onValueChanged.AddListener(OnSpeedDropdownChanged);
            speedDropdown.value = 0; // Default to 1x speed
        }
        
        // Validate time segment images
        if (timeSegmentImages.Length != 4)
        {
            Debug.LogError("GlobalClock: Must have exactly 4 time segment images!");
        }
    }
    
    void UpdateTimeDisplay()
    {
        // Update day text
        if (dayText != null)
        {
            dayText.text = $"Day   {currentDay}";
        }

        // Update round text
        if (roundText != null)
        {
            roundText.text = $"Round   {currentTimeSegment + 1}";
        }

        if (TaskCenterDayRoundText != null)
        {
            TaskCenterDayRoundText.text = $"Day {currentDay} Round {currentTimeSegment + 1}";
        }

        // Update time segment sprites
        UpdateTimeSegmentSprites();
    }
    
    void UpdateTimeSegmentSprites()
    {
        for (int i = 0; i < timeSegmentImages.Length; i++)
        {
            if (timeSegmentImages[i] == null) continue;
            
            if (i < currentTimeSegment)
            {
                // Past time segment
                timeSegmentImages[i].sprite = pastTimeSprite;
            }
            else if (i == currentTimeSegment)
            {
                // Current time segment
                timeSegmentImages[i].sprite = currentTimeSprite;
            }
            else
            {
                // Future time segment
                timeSegmentImages[i].sprite = futureTimeSprite;
            }
        }
    }
    
    void OnExecuteButtonClicked()
    {
        if (isSimulationRunning) return;
        
        StartSimulation();
    }
    
    void OnSpeedDropdownChanged(int dropdownValue)
    {
        switch (dropdownValue)
        {
            case 0:
                currentTimeSpeed = TimeSpeed.Normal;
                break;
            case 1:
                currentTimeSpeed = TimeSpeed.Fast;
                break;
            case 2:
                currentTimeSpeed = TimeSpeed.VeryFast;
                break;
            default:
                currentTimeSpeed = TimeSpeed.Normal;
                break;
        }
        
        if (showDebugInfo)
            Debug.Log($"Time speed changed to {currentTimeSpeed}x");
    }
    
    void StartSimulation()
    {
        if (isSimulationRunning) return;
        
        isSimulationRunning = true;
        currentState = TimeState.Simulating;
        
        // Disable player interactions
        DisablePlayerInteractions();
        
        // Calculate player wait time based on speed (shorter wait for higher speeds)
        float playerWaitTime = simulationDuration / (int)currentTimeSpeed;
        
        // Set time scale to speed up game content
        Time.timeScale = (int)currentTimeSpeed;
        
        // Notify other systems
        OnSimulationStarted?.Invoke();
        
        if (showDebugInfo)
            Debug.Log($"Simulation started - Player waits {playerWaitTime}s, game content runs at {currentTimeSpeed}x speed");
        
        // Start simulation coroutine
        StartCoroutine(SimulationCoroutine(playerWaitTime));
    }
    
    IEnumerator SimulationCoroutine(float playerWaitTime)
    {
        // Wait using unscaled time so player wait time is accurate regardless of Time.timeScale
        yield return new WaitForSecondsRealtime(playerWaitTime);
        
        EndSimulation();
    }
    
    void EndSimulation()
    {
        isSimulationRunning = false;
        currentState = TimeState.Paused;
        
        // Pause Unity's time again for player interaction phase
        Time.timeScale = 0f;
        
        // Advance to next time segment
        AdvanceTimeSegment();
        
        // Enable player interactions
        EnablePlayerInteractions();
        
        // Notify other systems
        OnSimulationEnded?.Invoke();
        
        if (showDebugInfo)
            Debug.Log($"Simulation ended - Now at Day {currentDay}, Time Segment {currentTimeSegment + 1}");
    }
    
    void AdvanceTimeSegment()
    {
        currentTimeSegment++;
        
        // Check if day is complete
        if (currentTimeSegment >= 4)
        {
            // Advance to next day
            currentDay++;
            currentTimeSegment = 0;
            
            OnDayChanged?.Invoke(currentDay);
            
            if (showDebugInfo)
                Debug.Log($"Day advanced to Day {currentDay}");
        }
        
        OnTimeSegmentChanged?.Invoke(currentTimeSegment);
        
        // Update display
        UpdateTimeDisplay();
    }
    
    void DisablePlayerInteractions()
    {
        // Disable execute button
        if (executeButton != null)
            executeButton.interactable = false;
        
        // Disable speed dropdown during simulation
        if (speedDropdown != null)
            speedDropdown.interactable = false;
        
        // You can add more UI elements to disable here
        // For example: building placement, worker assignment UI, etc.
    }
    
    void EnablePlayerInteractions()
    {
        // Enable execute button
        if (executeButton != null)
            executeButton.interactable = true;
        
        // Enable speed dropdown
        if (speedDropdown != null)
            speedDropdown.interactable = true;
        
        // Re-enable other UI elements here
    }
    
    // Public methods for other systems to query time state
    public bool IsSimulationRunning()
    {
        return isSimulationRunning;
    }
    
    public bool CanPlayerInteract()
    {
        return currentState == TimeState.Paused && !isSimulationRunning;
    }
    
    public int GetCurrentDay()
    {
        return currentDay;
    }
    
    public int GetCurrentTimeSegment()
    {
        return currentTimeSegment;
    }
    
    public string GetCurrentTimeString()
    {
        string[] timeStrings = { "9:00", "12:00", "15:00", "18:00" };
        return timeStrings[currentTimeSegment];
    }
    
    public TimeSpeed GetCurrentTimeSpeed()
    {
        return currentTimeSpeed;
    }
    
    public TimeState GetCurrentState()
    {
        return currentState;
    }
    
    // Manual control methods (for debugging or special cases)
    [ContextMenu("Force Next Time Segment")]
    public void ForceAdvanceTimeSegment()
    {
        if (!isSimulationRunning)
        {
            AdvanceTimeSegment();
        }
    }
    
    [ContextMenu("Reset to Day 1")]
    public void ResetToDay1()
    {
        if (!isSimulationRunning)
        {
            currentDay = 1;
            currentTimeSegment = 0;
            UpdateTimeDisplay();
            
            if (showDebugInfo)
                Debug.Log("Time reset to Day 1, Time Segment 1");
        }
    }
    
    [ContextMenu("Print Current Time")]
    public void PrintCurrentTime()
    {
        Debug.Log($"Current Time: Day {currentDay}, {GetCurrentTimeString()} (Segment {currentTimeSegment + 1}/4)");
        Debug.Log($"State: {currentState}, Speed: {currentTimeSpeed}x, Can Interact: {CanPlayerInteract()}");
    }
    
    void OnDestroy()
    {
        // Reset time scale when destroyed
        Time.timeScale = 1f;
    }
}