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
    public Button buildingStatsButton;
    public Button workerCenterButton;
    public Button taskCenterButton;
    

    public Image[] timeSegmentImages = new Image[4]; // 4 time segments

    [Header("Color References")]
    public Color pastTimeColor;     // Already passed time segment
    public Color currentTimeColor;  // Current time segment
    public Color futureTimeColor;   // Future time segment

    [Header("Day Display")]
    public TextMeshProUGUI dayText;
    public TextMeshProUGUI roundText;
    public TextMeshProUGUI TaskCenterDayRoundText;

    [Header("Clock Animation")]
    public ClockAnimationUI clockAnimationUI;

    [Header("Debug")]
    public bool showDebugInfo = true;
    
    private Color disabledColor = new Color(0.784f, 0.784f, 0.784f, 0.502f);
    
    // Current state
    private TimeState currentState = TimeState.Paused;
    private bool isSimulationRunning = false;
    public bool isWaitingForReport = false; // Track if we're waiting for report
    
    // Events for other systems to listen to
    public event Action OnSimulationStarted;
    public event Action OnSimulationEnded;
    public event Action<int> OnTimeSegmentChanged;
    public event Action<int> OnDayChanged;

    public static event Action OnRoundEnd;
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
            Debug.Log("Global Clock initialized - Game starts paused at Day 1, Time Segment 1");
            
        GameLogPanel.Instance.LogMetricsChange($"Game started - Day {currentDay}, Round {currentTimeSegment + 1}");
        
        // Update ActionTrackingManager at start
        if (ActionTrackingManager.Instance != null)
        {
            ActionTrackingManager.Instance.SetDayAndRound(currentDay, currentTimeSegment + 1);
        }
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
            dayText.text = $"{currentDay}";
        }

        // Update round text
        if (roundText != null)
        {
            roundText.text = $"{currentTimeSegment + 1}";
        }

        if (TaskCenterDayRoundText != null)
        {
            TaskCenterDayRoundText.text = $"Day {currentDay}, Round {currentTimeSegment + 1}";
        }

        // Update time segment sprites
        UpdateTimeSegmentColors();
    }

    void UpdateTimeSegmentColors()
    {
        for (int i = 0; i < timeSegmentImages.Length; i++)
        {
            if (timeSegmentImages[i] == null) continue;

            if (i < currentTimeSegment)
            {
                // Past time segment
                timeSegmentImages[i].color = pastTimeColor;
            }
            else if (i == currentTimeSegment)
            {
                // Current time segment
                timeSegmentImages[i].color = currentTimeColor;
            }
            else
            {
                // Future time segment
                timeSegmentImages[i].color = futureTimeColor;
            }
        }
    }
    
    void OnExecuteButtonClicked()
    {
        if (isSimulationRunning) return;
        
        // If waiting for report, clicking button shows the report
        if (isWaitingForReport)
        {
            // Ask for confirmation to view report
            if (ConfirmationPopup.Instance != null)
            {
                ConfirmationPopup.Instance.ShowPopup(
                    message: "End today and go to the daily report?",
                    onConfirm: () => {
                        if (DailyReportManager.Instance != null)
                        {
                            DailyReportManager.Instance.ShowDailyReport();
                        }
                        isWaitingForReport = false;
                        
                        // Disable button until report is handled
                        if (executeButton != null)
                            executeButton.interactable = false;
                        
                    },
                    title: "View Daily Report?"
                );
                return;
            }

        }
        else
        {
            // for the first time execution, show longer confirmation text
            if (FirstTimeActionTracker.Instance != null && FirstTimeActionTracker.Instance.IsFirstExecute())
            {
                ConfirmationPopup.Instance.ShowPopup(
                    message: $"Opening a facility takes {FindObjectOfType<BuildingSystem>()?.constructionRounds ?? 4} rounds. Only for today, the remaining rounds will be skipped automatically — you won’t be able to make any decisions during this time.\n\nFrom Day 2 onward, each click of this button advances time by 1 round.\n\nDo you want to proceed?",
                    onConfirm: () => {
                        FirstTimeActionTracker.Instance.MarkExecuteCompleted();
                        StartSimulation();
                    },
                    title: "Proceed to Next Round"
                );
                return;
            }
            /*else
            {
                // Ask for confirmation to start simulation
                if (ConfirmationPopup.Instance != null)
                {
                    ConfirmationPopup.Instance.ShowPopup(
                        message: "Are you sure you want to proceed to the next round?",
                        onConfirm: () => {
                            StartSimulation();
                        },
                        title: "Start Simulation Now?"
                    );
                    return;
                }
            }*/
            else
            {
                StartSimulation();
            }
            
        }
    }

    void OnSpeedDropdownChanged(int dropdownValue)
    {
        switch (dropdownValue)
        {
            case 2:
                currentTimeSpeed = TimeSpeed.Normal;
                break;
            case 1:
                currentTimeSpeed = TimeSpeed.Fast;
                break;
            case 0:
                currentTimeSpeed = TimeSpeed.VeryFast;
                break;
            default:
                currentTimeSpeed = TimeSpeed.Normal;
                break;
        }

        if (showDebugInfo)
            Debug.Log($"Time speed changed to {currentTimeSpeed}x");
        GameLogPanel.Instance.LogMetricsChange($"Time speed set to {currentTimeSpeed}x");
    }
    
    void StartSimulation()
    {
        if (isSimulationRunning) return;

        isSimulationRunning = true;
        currentState = TimeState.Simulating;
        DisablePlayerInteractions();
        OnSimulationStarted?.Invoke();

        // Day 1 is construction/intro — step through all 4 rounds with animation
        if (currentDay == 1 && currentTimeSegment == 0)
        {
            Time.timeScale = 0f;
            GameLogPanel.Instance.LogMetricsChange("Day 1: stepping through all rounds for construction/intro.");
            StartCoroutine(Day1SkipCoroutine());
            return;
        }

        RequestLLMAgentDecision();

        int roundNumber = (currentDay - 1) * 4 + currentTimeSegment + 1;
        if (WebSocketManager.Instance != null && WebSocketManager.Instance.isConnected)
            WebSocketManager.Instance.SendBeginRound(roundNumber, currentDay, currentTimeSegment);

        if (!HasActiveDeliveries())
        {
            // No deliveries — skip simulation, just play fast clock animation
            Time.timeScale = 0f;

            if (showDebugInfo)
                Debug.Log("No active deliveries — skipping simulation.");
            GameLogPanel.Instance.LogMetricsChange("No active deliveries — skipping simulation.");

            if (clockAnimationUI != null)
                clockAnimationUI.PlaySkip(EndSimulation);
            else
                EndSimulation();
        }
        else
        {
            float playerWaitTime = simulationDuration / (int)currentTimeSpeed;
            Time.timeScale = (int)currentTimeSpeed;

            if (showDebugInfo)
                Debug.Log($"Simulation started — Player waits {playerWaitTime}s at {currentTimeSpeed}x speed");
            GameLogPanel.Instance.LogMetricsChange($"Simulation started — Player waits {playerWaitTime}s at {currentTimeSpeed}x speed");

            clockAnimationUI?.PlaySynced(playerWaitTime);

            StartCoroutine(SimulationCoroutine(playerWaitTime));
        }
    }

    IEnumerator Day1SkipCoroutine()
    {
        bool hasFacilities = FindObjectsOfType<Building>().Length > 0;
        string openMsg     = clockAnimationUI != null
            ? (hasFacilities ? clockAnimationUI.day1SetupMessage : clockAnimationUI.day1NoFacilitiesMessage)
            : "";
        string completeMsg = clockAnimationUI != null ? clockAnimationUI.day1CompleteMessage : "";

        clockAnimationUI?.Show(openMsg);

        // Step through rounds 1-4: show round number → play clock → fire OnRoundEnd
        for (int round = 0; round < 4; round++)
        {
            currentTimeSegment = round;
            UpdateTimeDisplay();

            if (round == 3 && hasFacilities)
                clockAnimationUI?.SetMessage(completeMsg);

            if (clockAnimationUI != null)
                yield return clockAnimationUI.PlayRoundLoops();
            else
                yield return new WaitForSecondsRealtime(0.1f);

            OnRoundEnd?.Invoke();
        }

        clockAnimationUI?.Hide();

        // Segment stays at 3 so display reads "Round 4"; advance state to end-of-day
        currentTimeSegment  = 4;
        isSimulationRunning = false;
        currentState        = TimeState.Paused;
        Time.timeScale      = 0f;
        isWaitingForReport  = true;

        executeButton?.GetComponentInChildren<TextMeshProUGUI>()?.SetText("End Today");
        EnablePlayerInteractions();
        OnSimulationEnded?.Invoke();

        if (showDebugInfo)
            Debug.Log("Day 1 complete — all 4 rounds stepped through.");
        GameLogPanel.Instance.LogMetricsChange("Day 1 complete — Click 'End Today' when ready.");
    }

    bool HasActiveDeliveries()
    {
        return DeliverySystem.Instance != null && DeliverySystem.Instance.HasPendingOrActiveDeliveries();
    }

    void RequestLLMAgentDecision()
    {
        // Check if WebSocket is connected
        if (WebSocketManager.Instance == null || !WebSocketManager.Instance.IsConnected())
        {
            if (showDebugInfo)
                Debug.Log("WebSocket not connected - skipping LLM agent decision");
            return;
        }

        // Check if TaskSystem is available
        if (TaskSystem.Instance == null)
        {
            Debug.LogWarning("TaskSystem not available - cannot request agent decision");
            return;
        }

        // Get current game state
        GameStatePayload gameState = TaskSystem.Instance.GetCurrentGameState(0);

        // Create request payload using proper serializable class
        AgentDecisionRequest payload = new AgentDecisionRequest
        {
            type = "request_agent_decision",
            game_state = gameState,
            goal = "Maximize satisfaction while maintaining budget and completing tasks",
            timestamp = System.DateTime.UtcNow.ToString("o")
        };

        // Send request via WebSocket
        string json = JsonUtility.ToJson(payload);

        if (showDebugInfo)
        {
            Debug.Log($"📤 Requested LLM agent decision for Day {currentDay}, Round {currentTimeSegment + 1}");
            Debug.Log($"📦 Payload JSON length: {json.Length} characters");
            Debug.Log($"📋 JSON Content (first 500 chars): {json.Substring(0, Mathf.Min(500, json.Length))}");
        }

        WebSocketManager.Instance.SendRawMessage(json);
        GameLogPanel.Instance.LogMetricsChange($"Requested AI decision for Day {currentDay}, Round {currentTimeSegment + 1}");
    }
    
    IEnumerator SimulationCoroutine(float playerWaitTime)
    {
        float elapsed = 0f;
        
        while (elapsed < playerWaitTime)
        {
            // Use unscaledDeltaTime when paused (timeScale = 0), use deltaTime when running
            if (Time.timeScale > 0)
            {
                elapsed += Time.unscaledDeltaTime;
            }
            yield return null;
        }
        
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

        OnRoundEnd?.Invoke();
        
        // Enable player interactions
        EnablePlayerInteractions();
        
        // Check if we just finished round 4
        if (currentTimeSegment >= 4)
        {
            // Change button text to "End Today"
            if (executeButton != null)
            {
                TextMeshProUGUI buttonText = executeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                {
                    buttonText.text = "End Today";
                }
            }
            isWaitingForReport = true;

            // CHECK FOR END GAME (Round 4 of Day 8)
            if (currentDay == 8 && currentTimeSegment >= 4)
            {
                if (EndGamePanel.Instance != null)
                {
                    EndGamePanel.Instance.ShowEndGamePanel();
                    Debug.Log("End game reached - Round 4 of Day 8");
                }
            }

            if (showDebugInfo)
            {
                GameLogPanel.Instance.LogMetricsChange($"Day {currentDay} complete - Click 'End Today' when ready");
                Debug.Log($"Day {currentDay} complete - Click 'End Today' when ready");
            }
        }
        else
        {
            if (showDebugInfo)
            {
                GameLogPanel.Instance.LogMetricsChange($"Simulation ended - Now at Day {currentDay}, Round {currentTimeSegment + 1}");
                Debug.Log($"Simulation ended - Now at Day {currentDay}, Round {currentTimeSegment + 1}");
            }
        }
        
        // Notify other systems
        OnSimulationEnded?.Invoke();
    }
    
    void AdvanceTimeSegment()
    {
        currentTimeSegment++;

        // Check if day is complete (4 rounds = end of day)
        if (currentTimeSegment >= 4)
        {
            // Don't trigger OnDayChanged here anymore - wait for button click
            return; // Exit early, don't update display yet
        }

        // Update ActionTrackingManager for rounds 1-3
        if (ActionTrackingManager.Instance != null)
        {
            ActionTrackingManager.Instance.SetDayAndRound(currentDay, currentTimeSegment + 1);
        }
        
        OnTimeSegmentChanged?.Invoke(currentTimeSegment);
        
        // Update display only if not end of day
        UpdateTimeDisplay();
    }

    // The daily report system will call this to advance the day
    public void ProceedToNextDay()
    {
        // =====================================================
        // FIX: Reset daily tracking data BEFORE advancing the day.
        // This is the correct time to reset — after the report has
        // been displayed and the player has confirmed moving on.
        // Previously, this reset happened when OnDayChanged fired
        // (before the report was shown), causing zeroed data.
        // =====================================================
        if (DailyReportData.Instance != null)
        {
            DailyReportData.Instance.PrepareForNewDay();
        }

        // Actually advance to next day after report confirmation
        currentDay++;
        currentTimeSegment = 0; // Reset to first round (not 1)

        // Update ActionTrackingManager for new day
        if (ActionTrackingManager.Instance != null)
        {
            ActionTrackingManager.Instance.SetDayAndRound(currentDay, currentTimeSegment + 1);
        }

        // Reset button text back to "Proceed"
        if (executeButton != null)
        {
            TextMeshProUGUI buttonText = executeButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = "Proceed";
            }
        }

        // =====================================================
        // FIX: Fire OnDayChanged NOW (after reset, after day 
        // advances) so other systems that need to know about
        // the new day can respond. DailyReportData no longer
        // listens to this event for resetting — it uses
        // PrepareForNewDay() instead (called above).
        // =====================================================
        OnDayChanged?.Invoke(currentDay);
        OnTimeSegmentChanged?.Invoke(currentTimeSegment);

        // Update display
        UpdateTimeDisplay();

        if (showDebugInfo)
            Debug.Log($"Advanced to Day {currentDay}, Round 1");
        GameLogPanel.Instance.LogMetricsChange($"Advanced to Day {currentDay}, Round 1");
    }

    public void PauseSimulation()
    {
        if (isSimulationRunning)
        {
            StopAllCoroutines();
            isSimulationRunning = false;
            currentState = TimeState.Paused;
        }
        Time.timeScale = 0f;

        if (showDebugInfo)
            Debug.Log("Simulation paused by external system");
        GameLogPanel.Instance.LogMetricsChange("Simulation paused by external system");
    }

    public void ResumeSimulation()
    {
        Time.timeScale = 0f; // Keep paused for player interaction
        currentState = TimeState.Paused;
        EnablePlayerInteractions();

        if (showDebugInfo)
            Debug.Log("Simulation resumed - ready for player interaction");
        GameLogPanel.Instance.LogMetricsChange("Simulation resumed - ready for player interaction");
    }

    void DisablePlayerInteractions()
    {
        // Disable execute button
        if (executeButton != null)
        {
            executeButton.interactable = false;
            // Change text color to disabled
            TextMeshProUGUI buttonText = executeButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.color = disabledColor;
            }
        }

        // Disable speed dropdown during simulation
        if (speedDropdown != null)
            speedDropdown.interactable = false;

        if (buildingStatsButton != null)
            buildingStatsButton.interactable = false;

        if (taskCenterButton != null)
            taskCenterButton.interactable = false;

        if (workerCenterButton != null)
            workerCenterButton.interactable = false;

        // ***Can add more UI elements to disable here
    }

    void EnablePlayerInteractions()
    {
        // Enable execute button
        if (executeButton != null)
        {
            executeButton.interactable = true;
            // Restore text color
            TextMeshProUGUI buttonText = executeButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.color = Color.white;
            }
        }

        // Enable speed dropdown
        if (speedDropdown != null)
            speedDropdown.interactable = true;

        if (buildingStatsButton != null)
            buildingStatsButton.interactable = true;

        if (taskCenterButton != null)
            taskCenterButton.interactable = true;

        if (workerCenterButton != null)
            workerCenterButton.interactable = true;

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
        if (currentTimeSegment < timeStrings.Length)
            return timeStrings[currentTimeSegment];
        return "End of Day";
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
            isWaitingForReport = false;
            
            // Reset button text
            if (executeButton != null)
            {
                TextMeshProUGUI buttonText = executeButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                {
                    buttonText.text = "Proceed";
                }
            }
            
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
        Debug.Log($"Waiting for Report: {isWaitingForReport}");
    }

    [ContextMenu("Debug: Jump to Day 8")]
    public void DebugJumpToDay8()
    {
        currentDay = 8;
        currentTimeSegment = 0; // Start of Day 8, Round 1
        UpdateTimeDisplay();
        
        Debug.Log("Jumped to Day 8, Round 1");
    }
        
    void OnDestroy()
    {
        // Reset time scale when destroyed
        Time.timeScale = 1f;
    }
}