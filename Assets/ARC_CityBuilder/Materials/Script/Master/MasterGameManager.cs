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
    
    // Events
     public event Action OnRoundStarted;
    public event Action OnSimulationStarted;
    public event Action OnPlayerTurnStarted;
    public event Action OnDisasterPhaseStarted;
    public event Action OnRoundEnded;
    public event Action<int, int> OnRoundAdvanced; // (roundNum, dayNum)
    public event Action<GlobalEnums.GamePhase> OnPhaseChanged; // To track phase changes
    public event Action<float> OnSimulationTick; // To update UI with simulation progress
    
    
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
        }
        
        Debug.Log($"[MasterGameManager] Advanced to Round {_currentRound}, Day {_currentDay}");
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
}

/// <summary>
/// Manages round progression and timing
/// </summary>
public class RoundManager : MonoBehaviour
{
    [Header("Round Settings")]
    public float startRoundDelay = 1f;
    public float endRoundDelay = 1f;
    
    /// <summary>
    /// Handle start of round behaviors
    /// </summary>
    public IEnumerator StartRound(int roundNumber, int dayNumber)
    {
        Debug.Log($"Round {roundNumber} (Day {dayNumber}) begins!");
        
        // Update UI
        var uiManager = FindObjectOfType<UIManager>();
        if (uiManager != null)
        {
            uiManager.UpdateRoundText(roundNumber, dayNumber);
        }
        
        // Add any other start of round logic here
        
        // Pause for transitions
        yield return new WaitForSeconds(startRoundDelay);
    }
    
    /// <summary>
    /// Handle end of round behaviors
    /// </summary>
    public IEnumerator EndRound()
    {
        Debug.Log("Round ends!");
        
        // Add any end of round logic here
        
        // Pause for transitions
        yield return new WaitForSeconds(endRoundDelay);
    }
}

/// <summary>
/// Manages all disaster systems
/// </summary>
public class DisasterManager : MonoBehaviour
{
    [Header("Disaster Systems")]
    public FloodManager floodManager;
    
    [Header("Settings")]
    public float disasterPhaseDelay = 2f;
    
    private void Awake()
    {
        // Get required components
        if (floodManager == null)
            floodManager = FindObjectOfType<FloodManager>();
    }
    
    /// <summary>
    /// Process all disaster events for the current round
    /// </summary>
    public IEnumerator ProcessDisasters()
    {
        Debug.Log("Processing disaster events...");
        
        // Process flooding
        if (floodManager != null)
        {
            floodManager.SimulateFlooding();
        }
        
        // Other disasters can be added here
        
        yield return new WaitForSeconds(disasterPhaseDelay);
    }
}

/// <summary>
/// Manages building-related systems and communication
/// </summary>
public class BuildingSystem : MonoBehaviour
{
    // References to required systems
    private IBuildingManager _buildingManager;
    private List<Building> _communities = new List<Building>();
    private List<Building> _shelters = new List<Building>();
    private List<Building> _kitchens = new List<Building>();
    private List<Building> _otherBuildings = new List<Building>();
    
    private void Start()
    {
        _buildingManager = Dependencies.GetOptional<IBuildingManager>();
        
        // Import existing buildings from GameDatabase if it exists
        var gameDatabase = FindObjectOfType<GameDatabase>();
        if (gameDatabase != null)
        {
            _communities.AddRange(gameDatabase.GetAllCommunities());
            _shelters.AddRange(gameDatabase.GetAllShelters());
            _kitchens.AddRange(gameDatabase.GetAllKitchens());
            _otherBuildings.AddRange(gameDatabase.GetAllGenericBuildings());
        }
    }
    
    /// <summary>
    /// Register a new building in the system
    /// </summary>
    public void RegisterBuilding(Building building, string type)
    {
        switch (type.ToLower())
        {
            case "community":
                if (!_communities.Contains(building))
                    _communities.Add(building);
                break;
            case "shelter":
                if (!_shelters.Contains(building))
                    _shelters.Add(building);
                break;
            case "kitchen":
                if (!_kitchens.Contains(building))
                    _kitchens.Add(building);
                break;
            default:
                if (!_otherBuildings.Contains(building))
                    _otherBuildings.Add(building);
                break;
        }
    }
    
    /// <summary>
    /// Update all buildings after disaster events
    /// </summary>
    public void UpdateAllBuildings()
    {
        // Notify communities of flood changes
        foreach (var community in _communities)
        {
            if (community != null && community.TryGetComponent<CommunityLogic>(out var logic))
            {
                logic.CheckFloodStatusAndUpdateOrder();
            }
        }
        
        // Reset food orders in shelters
        foreach (var shelter in _shelters)
        {
            if (shelter != null && shelter.TryGetComponent<ShelterLogic>(out var logic))
            {
                logic.ClearFoodStorage();
                logic.GenerateFoodOrderDebug();
            }
        }
        
        // Notify kitchens of new orders
        foreach (var kitchen in _kitchens)
        {
            if (kitchen != null && kitchen.TryGetComponent<ProductionWalkerComponent>(out var productionWalker))
            {
                productionWalker.TryRestartDelivery();
            }
        }
    }
    
    /// <summary>
    /// Get all buildings of a specific type
    /// </summary>
    public IReadOnlyList<Building> GetBuildingsByType(string type)
    {
        switch (type.ToLower())
        {
            case "community":
                return _communities;
            case "shelter":
                return _shelters;
            case "kitchen":
                return _kitchens;
            default:
                return _otherBuildings;
        }
    }
}