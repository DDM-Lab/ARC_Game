using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CityBuilderCore;

/// <summary>
/// Master game manager that extends DefaultGameManager and coordinates all game systems
/// </summary>
public class MasterGameManager : DefaultGameManager
{
    [Header("Game Systems")]
    public RoundManager roundManager;
    public DisasterManager disasterManager;
    public BuildingSystem buildingSystem;
    
    [Header("Turn-Based Settings")]
    [Tooltip("Whether simulation runs continuously or waits for player end-turn")]
    public bool turnBasedMode = true;
    [Tooltip("Number of seconds to simulate when End Turn is clicked")]
    public float simulationSecondsPerRound = 10f;
    
    // Game state
    public static MasterGameManager Instance { get; private set; }
    public GlobalEnums.GamePhase CurrentPhase { get; private set; }
    public int CurrentRound => _currentRound;
    public int CurrentDay => _currentDay;

    // Simulation tracking
    public bool IsSimulating => _isSimulating;
    public float SimulationTimer => _simulationTimer;
    public float SimulationSecondsPerRound => simulationSecondsPerRound;
    public float SimulationRemainingTime => Mathf.Max(0, simulationSecondsPerRound - _simulationTimer);

    // Weather settings
    public GlobalEnums.WeatherType CurrentWeather { get; private set; } = GlobalEnums.WeatherType.Stormy;
    public float SunnyChance = 0.0f;
    public float RainyChance = 0.4f;
    public int RoundsPerDay = 9;
    
    // Events
     public event Action OnRoundStarted;
    public event Action OnSimulationStarted;
    public event Action OnPlayerTurnStarted;
    public event Action OnDisasterPhaseStarted;
    public event Action OnRoundEnded;
    public event Action<int, int> OnRoundAdvanced; // (roundNum, dayNum)
    public event Action<GlobalEnums.GamePhase> OnPhaseChanged; // To track phase changes
    public event Action<float> OnSimulationTick; // To update UI with simulation progress
    public event Action<GlobalEnums.WeatherType> OnWeatherChanged;
    
    
    // Private variables
    private int _currentRound = 1;
    private int _currentDay = 1;
    private bool _isSimulating = false;
    private float _simulationTimer = 0f;
    private Coroutine _roundCoroutine;
    
    protected override void Awake()
    {
        base.Awake();
        
        // Set up singleton pattern
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        // Ensure we have required components
        if (roundManager == null)
            roundManager = GetComponent<RoundManager>() ?? gameObject.AddComponent<RoundManager>();
            
        if (disasterManager == null)
            disasterManager = GetComponent<DisasterManager>() ?? gameObject.AddComponent<DisasterManager>();
            
        if (buildingSystem == null)
            buildingSystem = GetComponent<BuildingSystem>() ?? gameObject.AddComponent<BuildingSystem>();
    }
    
    protected override void Start()
    {
        base.Start();

        // Initialize weather
        UpdateWeather();
        
        // Start the first round
        _roundCoroutine = StartCoroutine(RoundLoop());
        
        // Log initial state
        Debug.Log($"[MasterGameManager] Game started with {simulationSecondsPerRound}s simulation time per turn.");
    }
    
    
    protected override void Update()
    {
        base.Update();
        
        // Handle simulation timer during simulation phase
        if (_isSimulating && CurrentPhase == GlobalEnums.GamePhase.Simulation)
        {
            // Track real time (unaffected by game speed)
            _simulationTimer += Time.unscaledDeltaTime;
            
            // Notify listeners of timer tick
            OnSimulationTick?.Invoke(_simulationTimer);
            
            // Check if simulation time is complete
            if (_simulationTimer >= simulationSecondsPerRound)
            {
                EndSimulation();
            }
        }
    }

    /// <summary>
    /// Sets the current phase and invokes related events
    /// </summary>
    private void SetCurrentPhase(GlobalEnums.GamePhase phase)
    {
        if (CurrentPhase != phase)
        {
            CurrentPhase = phase;
            Debug.Log($"[MasterGameManager] Phase changed to: {phase}");
            OnPhaseChanged?.Invoke(phase);
            
            // Invoke specific phase events
            switch (phase)
            {
                case GlobalEnums.GamePhase.Start:
                    OnRoundStarted?.Invoke();
                    break;
                case GlobalEnums.GamePhase.Simulation:
                    OnSimulationStarted?.Invoke();
                    break;
                case GlobalEnums.GamePhase.PlayerTurn:
                    OnPlayerTurnStarted?.Invoke();
                    break;
                case GlobalEnums.GamePhase.DisasterEvents:
                    OnDisasterPhaseStarted?.Invoke();
                    break;
                case GlobalEnums.GamePhase.End:
                    OnRoundEnded?.Invoke();
                    break;
            }
        }
    }
    
    /// <summary>
    /// Main game loop that manages rounds and phases
    /// </summary>
    private IEnumerator RoundLoop()
    {
        while (true) // Game runs indefinitely unless stopped
        {
            // Start Round Phase
            SetCurrentPhase(GlobalEnums.GamePhase.Start);
            yield return StartCoroutine(roundManager.StartRound(_currentRound, _currentDay));
            
            // Simulation Phase - Run simulation for a fixed amount of real time
            StartSimulation();
            SetCurrentPhase(GlobalEnums.GamePhase.Simulation);
            
            // Let the simulation run (handled in Update)
            yield return new WaitUntil(() => !_isSimulating);
            
            // Pause the game for player turn
            IsPaused = true;
            
            // Player Turn Phase
            SetCurrentPhase(GlobalEnums.GamePhase.PlayerTurn);
            yield return StartCoroutine(WaitForEndTurn());
            
            // Disaster Events Phase
            SetCurrentPhase(GlobalEnums.GamePhase.DisasterEvents);
            yield return StartCoroutine(disasterManager.ProcessDisasters());
            
            // Notify buildings of any changes
            buildingSystem.UpdateAllBuildings();
            
            // End Round Phase
            SetCurrentPhase(GlobalEnums.GamePhase.End);
            yield return StartCoroutine(roundManager.EndRound());
            
            // Advance round counters
            AdvanceRound();
        }
    }
    
    /// <summary>
    /// Wait for player to click the end turn button
    /// </summary>
    private IEnumerator WaitForEndTurn()
    {
        bool endTurnClicked = false;
        
        // Set up temporary event listener
        System.Action endTurnAction = () => { endTurnClicked = true; };
        OnEndTurnRequested += endTurnAction;
        
        // Wait until end turn is clicked
        yield return new WaitUntil(() => endTurnClicked);
        
        // Clean up event listener
        OnEndTurnRequested -= endTurnAction;
    }
    
    // Event for when player clicks end turn
    public event System.Action OnEndTurnRequested;
    
    /// <summary>
    /// Called when player clicks End Turn button
    /// </summary>
    public void EndPlayerTurn()
    {
        Debug.Log("[MasterGameManager] End Turn requested");
        
        if (CurrentPhase != GlobalEnums.GamePhase.PlayerTurn)
        {
            Debug.LogWarning($"[MasterGameManager] Cannot end turn - Current phase: {CurrentPhase}");
            return;
        }
        
        // Unpause the game
        IsPaused = false;
        
        // Notify listeners
        OnEndTurnRequested?.Invoke();
    }
    
    /// <summary>
    /// Start the simulation phase timer
    /// </summary>
    private void StartSimulation()
    {
        _isSimulating = true;
        _simulationTimer = 0f;
        
        // Ensure the game is running (not paused)
        IsPaused = false;
        
        Debug.Log("[MasterGameManager] Starting simulation phase");
    }
    
    /// <summary>
    /// Called when simulation period ends
    /// </summary>
    private void EndSimulation()
    {
        _isSimulating = false;
        
        Debug.Log("[MasterGameManager] Simulation phase completed");
    }
    
    /// <summary>
    /// Advances the round and day counters
    /// </summary>
    private void AdvanceRound()
    {
        _currentRound++;
        
        // Every 4 rounds is a new day (or whatever your game logic dictates)
        if (_currentRound % 4 == 0)
        {
            _currentDay++;
            if (_currentDay  == 2)
            {
                Debug.Log($"[MasterGameManager] Day = {_currentDay}, Demo ends here.");
            }
            UpdateWeather(); // Update weather on new day
        }
        
        Debug.Log($"[MasterGameManager] Advanced to Round {_currentRound}, Day {_currentDay}, Weather: {CurrentWeather}");
        OnRoundAdvanced?.Invoke(_currentRound, _currentDay);
    }
    
    /// <summary>
    /// Set the duration of each simulation round
    /// </summary>
    public void SetSimulationDuration(float seconds)
    {
        simulationSecondsPerRound = Mathf.Max(1f, seconds);
    }
    
    /// <summary>
    /// Manually force end the current round (for debugging)
    /// </summary>
    public void ForceEndRound()
    {
        if (_roundCoroutine != null)
        {
            StopCoroutine(_roundCoroutine);
        }
        
        SetCurrentPhase(GlobalEnums.GamePhase.End);
        AdvanceRound();
        _roundCoroutine = StartCoroutine(RoundLoop());
        
        Debug.Log("[MasterGameManager] Force ended current round");
    }

    /// <summary>
    /// Updates the current weather based on probability settings
    /// </summary>
    private void UpdateWeather()
    {
        GlobalEnums.WeatherType previousWeather = CurrentWeather;
        float randomValue = UnityEngine.Random.value;

        if (randomValue < SunnyChance)
            CurrentWeather = GlobalEnums.WeatherType.Sunny;
        else if (randomValue < SunnyChance + RainyChance)
            CurrentWeather = GlobalEnums.WeatherType.Rainy;
        else
            CurrentWeather = GlobalEnums.WeatherType.Stormy;
        
        // Only trigger event if weather actually changed
        if (previousWeather != CurrentWeather)
        {
            Debug.Log($"[MasterGameManager] Weather changed to {CurrentWeather}");
            OnWeatherChanged?.Invoke(CurrentWeather);
        }
    }

    /// <summary>
    /// Manually set the current weather
    /// </summary>
    public void SetWeather(GlobalEnums.WeatherType newWeather)
    {
        if (CurrentWeather != newWeather)
        {
            CurrentWeather = newWeather;
            Debug.Log($"[MasterGameManager] Weather manually set to {CurrentWeather}");
            OnWeatherChanged?.Invoke(CurrentWeather);
        }
    }
}





