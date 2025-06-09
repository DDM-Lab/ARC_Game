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
    public WorkerManager workerManager; // New worker management system
    public TaskManager taskManager; // For emergency tasks
    
    [Header("Turn-Based Settings")]
    [Tooltip("Whether simulation runs continuously or waits for player end-turn")]
    public bool turnBasedMode = true;
    [Tooltip("Number of seconds to simulate when End Turn is clicked")]
    public float simulationSecondsPerRound = 10f;
    
    [Header("Worker Settings")]
    [Tooltip("Total workers available per round")]
    public int totalWorkersPerRound = 20;
    [Tooltip("Workers currently assigned")]
    public int assignedWorkers = 0;
    
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
    
    // Worker and facility management
    public bool AllFacilitiesStaffed => CheckAllFacilitiesStaffed();
    public int AvailableWorkers => totalWorkersPerRound - assignedWorkers;
    
    // Events
    public event Action OnRoundStarted;
    public event Action OnConstructionPhaseStarted;
    public event Action OnWorkerAssignmentPhaseStarted;
    public event Action OnSimulationStarted;
    public event Action OnPlayerTurnStarted;
    public event Action OnDisasterPhaseStarted;
    public event Action OnEmergencyTasksStarted;
    public event Action OnRoundEnded;
    public event Action<int, int> OnRoundAdvanced;
    public event Action<GlobalEnums.GamePhase> OnPhaseChanged;
    public event Action<float> OnSimulationTick;
    public event Action<GlobalEnums.WeatherType> OnWeatherChanged;
    public event Action OnWorkersAssigned;
    public event Action OnAllTasksCompleted;
    
    // Private variables
    private int _currentRound = 1;
    private int _currentDay = 1;
    private bool _isSimulating = false;
    private float _simulationTimer = 0f;
    private Coroutine _roundCoroutine;
    private bool _emergencyTasksActive = false;
    private List<string> _activeEmergencyTasks = new List<string>();
    
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
            
        if (workerManager == null)
            workerManager = GetComponent<WorkerManager>() ?? gameObject.AddComponent<WorkerManager>();
            
        if (taskManager == null)
            taskManager = FindObjectOfType<TaskManager>();
    }
    
    protected override void Start()
    {
        base.Start();

        // Initialize weather
        UpdateWeather();
        
        // Initialize worker system
        InitializeWorkerSystem();
        
        // Start the first round
        _roundCoroutine = StartCoroutine(RoundLoop());
        
        // Log initial state
        Debug.Log($"[MasterGameManager] Game started with {simulationSecondsPerRound}s simulation time per turn.");
    }
    
    private void InitializeWorkerSystem()
    {
        assignedWorkers = 0;
    
        // Subscribe to task completion events from TaskManager
        // TaskManager triggers GameEvents.OnTaskCompleted when users complete tasks
        if (taskManager != null)
        {
            GameEvents.OnTaskCompleted += OnEmergencyTaskCompleted;
            Debug.Log("[MasterGameManager] Subscribed to task completion events");
        }
        else
        {
            Debug.LogError("[MasterGameManager] TaskManager reference not found!");
        }
    }

    
    protected override void Update()
    {
        base.Update();
        
        // Handle simulation timer during simulation phase
        if (_isSimulating && CurrentPhase == GlobalEnums.GamePhase.Simulation)
        {
            _simulationTimer += Time.unscaledDeltaTime;
            OnSimulationTick?.Invoke(_simulationTimer);
            
            if (_simulationTimer >= simulationSecondsPerRound)
            {
                EndSimulation();
            }
        }
    }

    /// <summary>
    /// Main game loop that manages rounds and phases
    /// </summary>
    private IEnumerator RoundLoop()
    {
        while (true)
        {
            // Start Round Phase
            SetCurrentPhase(GlobalEnums.GamePhase.Start);
            yield return StartCoroutine(roundManager.StartRound(_currentRound, _currentDay));
            
            // Phase 1: Construction Phase
            SetCurrentPhase(GlobalEnums.GamePhase.Construction);
            OnConstructionPhaseStarted?.Invoke();
            IsPaused = true; // Pause for player input
            yield return StartCoroutine(WaitForConstructionComplete());
            
            // Phase 2: Worker Assignment Phase
            SetCurrentPhase(GlobalEnums.GamePhase.WorkerAssignment);
            OnWorkerAssignmentPhaseStarted?.Invoke();
            yield return StartCoroutine(WaitForWorkerAssignmentComplete());
            
            // Phase 3: Simulation Phase (Flood simulation)
            StartSimulation();
            SetCurrentPhase(GlobalEnums.GamePhase.Simulation);
            yield return new WaitUntil(() => !_isSimulating);
            
            // Phase 4: Emergency Tasks Phase (Food delivery, etc.)
            SetCurrentPhase(GlobalEnums.GamePhase.EmergencyTasks);
            OnEmergencyTasksStarted?.Invoke();
            yield return StartCoroutine(HandleEmergencyTasks());
            
            // Phase 5: Disaster Events Phase
            SetCurrentPhase(GlobalEnums.GamePhase.DisasterEvents);
            yield return StartCoroutine(disasterManager.ProcessDisasters());
            
            // Update buildings
            if (buildingSystem != null)
            {
                buildingSystem.UpdateAllBuildings();
            }
            
            // End Round Phase
            SetCurrentPhase(GlobalEnums.GamePhase.End);
            yield return StartCoroutine(roundManager.EndRound());
            
            // Check if game should end
            if (ShouldEndGame())
            {
                EndGame();
                break;
            }
            
            // Advance round counters
            AdvanceRound();
        }
    }
    
    /// <summary>
    /// Wait for construction phase to complete
    /// </summary>
    private IEnumerator WaitForConstructionComplete()
    {
        bool constructionComplete = false;
        
        System.Action constructionAction = () => { constructionComplete = true; };
        OnConstructionPhaseCompleted += constructionAction;
        
        yield return new WaitUntil(() => constructionComplete);
        
        OnConstructionPhaseCompleted -= constructionAction;
    }
    
    /// <summary>
    /// Wait for worker assignment phase to complete
    /// </summary>
    private IEnumerator WaitForWorkerAssignmentComplete()
    {
        bool workerAssignmentComplete = false;
        
        System.Action workerAction = () => { workerAssignmentComplete = true; };
        OnWorkerAssignmentCompleted += workerAction;
        
        yield return new WaitUntil(() => workerAssignmentComplete);
        
        OnWorkerAssignmentCompleted -= workerAction;
        
        // Enable facilities once workers are assigned
        EnableFacilitiesWithWorkers();
    }
    
    /// <summary>
    /// Handle emergency tasks phase
    /// </summary>
    private IEnumerator HandleEmergencyTasks()
    {
        _emergencyTasksActive = true;
        _activeEmergencyTasks.Clear();
        
        // Generate food delivery tasks
        GenerateFoodDeliveryTasks();
        
        // Wait for all emergency tasks to be completed
        yield return new WaitUntil(() => _activeEmergencyTasks.Count == 0);
        
        _emergencyTasksActive = false;
        OnAllTasksCompleted?.Invoke();
    }
    
    /// <summary>
    /// Generate food delivery tasks based on active shelters
    /// </summary>
    private void GenerateFoodDeliveryTasks()
    {
        // Safety check for buildingSystem
        if (buildingSystem == null)
        {
            Debug.LogWarning("[MasterGameManager] BuildingSystem not available, creating test task");
            CreateTestFoodDeliveryTask();
            return;
        }

        // Get all active shelters that need food
        var shelters = buildingSystem.GetActiveShelters();
        
        if (shelters == null || shelters.Count == 0)
        {
            Debug.LogWarning("[MasterGameManager] No shelters found, creating test task");
            CreateTestFoodDeliveryTask();
            return;
        }
        
        foreach (var shelter in shelters)
        {
            if (shelter == null) continue;
            
            // Check if shelter needs food (with fallback)
            bool needsFood = CheckIfShelterNeedsFood(shelter);
            bool isStaffed = CheckIfShelterIsStaffed(shelter);
            
            if (isStaffed && needsFood)
            {
                string taskId = $"food_delivery_{shelter.GetInstanceID()}";
                _activeEmergencyTasks.Add(taskId);
                
                // Create task data
                TaskData foodTask = new TaskData
                {
                    taskId = taskId,
                    title = "Food Delivery",
                    description = $"Deliver 10 food packs to {shelter.name}",
                    taskIcon = Resources.Load<Sprite>("Icons/FoodIcon"),
                    status = TaskStatus.Todo,
                    targetLocation = shelter.GetInstanceID().ToString(),
                    requiredAmount = 10
                };
                
                // Add task to task manager
                if (taskManager != null)
                {
                    taskManager.AddTask(foodTask);
                }
                
                Debug.Log($"Generated food delivery task for shelter {shelter.name}");
            }
        }
        
        if (_activeEmergencyTasks.Count == 0)
        {
            Debug.Log("No emergency tasks generated - creating test task for phase testing");
            CreateTestFoodDeliveryTask();
        }
    }

    /// <summary>
    /// Create a test food delivery task for phase transition testing
    /// </summary>
    private void CreateTestFoodDeliveryTask()
    {
        string taskId = "test_food_delivery_001";
        _activeEmergencyTasks.Add(taskId);
        
        TaskData testTask = new TaskData
        {
            taskId = taskId,
            title = "Test Food Delivery",
            description = "Test food delivery task for phase transition testing",
            taskIcon = null, // No icon needed for testing
            status = TaskStatus.Todo,
            targetLocation = "test_shelter",
            requiredAmount = 10
        };
        
        if (taskManager != null)
        {
            taskManager.AddTask(testTask);
        }
        
        Debug.Log("Created test food delivery task for phase testing");
    }

    /// <summary>
    /// Check if shelter needs food with fallback logic
    /// </summary>
    private bool CheckIfShelterNeedsFood(Building shelter)
    {
        if (shelter == null) return false;
        
        // Try to get ShelterLogic component
        var shelterLogic = shelter.GetComponent<ShelterLogic>();
        if (shelterLogic != null)
        {
            try
            {
                return shelterLogic.NeedsFood();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MasterGameManager] Error checking shelter food needs: {e.Message}");
            }
        }
        
        // Fallback: assume shelter needs food for testing
        return true;
    }

    /// <summary>
    /// Check if shelter is staffed with fallback logic
    /// </summary>
    private bool CheckIfShelterIsStaffed(Building shelter)
    {
        if (shelter == null) return false;
        
        // Try to check if building has IsStaffed property
        try
        {
            // Use reflection to check if IsStaffed property exists
            var isStaffedProperty = shelter.GetType().GetProperty("IsStaffed");
            if (isStaffedProperty != null)
            {
                return (bool)isStaffedProperty.GetValue(shelter);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MasterGameManager] Error checking shelter staffing: {e.Message}");
        }
        
        // Fallback: assume shelter is staffed for testing
        return true;
    }
    
    /// <summary>
    /// Called when an emergency task is completed
    /// </summary>
    /// <summary>
    /// Called when an emergency task is completed
    /// </summary>
    private void OnEmergencyTaskCompleted(string taskId)
    {
        Debug.Log($"[MasterGameManager] OnEmergencyTaskCompleted called for: {taskId}");
        Debug.Log($"[MasterGameManager] Active tasks before removal: {_activeEmergencyTasks.Count}");
    
        if (_activeEmergencyTasks.Contains(taskId))
        {
            _activeEmergencyTasks.Remove(taskId);
            Debug.Log($"[MasterGameManager] Task {taskId} removed. Remaining: {_activeEmergencyTasks.Count}");
        
            // // Handle specific task completion logic
            // if (taskId.StartsWith("food_delivery_"))
            // {
            //     string shelterId = taskId.Substring("food_delivery_".Length);
            //     StartCoroutine(DeliverFoodWithAnimation(shelterId, 10));
            // }
            // Instead, just deliver the food directly without animation for now:
            if (taskId.StartsWith("food_delivery_"))
            {
                string shelterId = taskId.Substring("food_delivery_".Length);
                DeliverFoodToShelter(shelterId, 10);
            }
        }
        else
        {
            Debug.LogWarning($"[MasterGameManager] Task {taskId} not found in active tasks!");
        }
    }

    
    /// <summary>
    /// Deliver food with worker animation
    /// </summary>
    /// <summary>
    /// Deliver food with worker animation
    /// </summary>
    private IEnumerator DeliverFoodWithAnimation(string shelterId, int amount)
    {
        Debug.Log($"Starting food delivery animation for shelter {shelterId}");
    
        // Find and trigger the worker animation
        var shelter = buildingSystem?.GetShelterById(shelterId);
        if (shelter != null)
        {
            // Look for animation components on the shelter or nearby workers
            var animators = shelter.GetComponentsInChildren<Animator>();
            foreach (var animator in animators)
            {
                if (animator.gameObject.name.Contains("Worker") || animator.gameObject.name.Contains("ERV"))
                {
                    animator.SetTrigger("DeliverFood"); // Adjust trigger name as needed
                    break;
                }
            }
        
            // Alternative: Trigger kitchen/delivery system animation
            var kitchens = buildingSystem.GetAllKitchens();
            foreach (var kitchen in kitchens)
            {
                var productionWalker = kitchen.GetComponent<ProductionWalkerComponent>();
                if (productionWalker != null)
                {
                    productionWalker.TryRestartDelivery(); // This might trigger the animation
                    break;
                }
            }
        }
    
        // Wait for animation to complete (adjust time based on your animation length)
        yield return new WaitForSeconds(2f);
    
        // Then deliver the food using your existing function
        DeliverFoodToShelter(shelterId, amount);
    }

    
    /// <summary>
    /// Deliver food to a specific shelter with error handling
    /// </summary>
    public void DeliverFoodToShelter(string shelterId, int amount)
    {
        if (buildingSystem == null)
        {
            Debug.LogWarning("[MasterGameManager] BuildingSystem not available for food delivery");
            return;
        }
        
        var shelter = buildingSystem.GetShelterById(shelterId);
        if (shelter != null)
        {
            var shelterLogic = shelter.GetComponent<ShelterLogic>();
            if (shelterLogic != null)
            {
                try
                {
                    shelterLogic.AddFood(amount);
                    Debug.Log($"Delivered {amount} food to shelter {shelterId}");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[MasterGameManager] Error delivering food: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[MasterGameManager] Shelter {shelterId} has no ShelterLogic component");
            }
        }
        else
        {
            Debug.LogWarning($"[MasterGameManager] Shelter {shelterId} not found for food delivery");
        }
    }
    
    /// <summary>
    /// Assign workers to a facility with error handling
    /// </summary>
    public bool AssignWorkersToFacility(string facilityId, int workerCount)
    {
        if (AvailableWorkers < workerCount)
        {
            Debug.LogWarning($"Cannot assign {workerCount} workers to {facilityId}. Available: {AvailableWorkers}");
            return false;
        }
        
        if (buildingSystem == null)
        {
            Debug.LogWarning("[MasterGameManager] BuildingSystem not available for worker assignment");
            // For testing, just update the worker count
            assignedWorkers += workerCount;
            OnWorkersAssigned?.Invoke();
            return true;
        }
        
        try
        {
            var facility = buildingSystem.GetFacilityById(facilityId);
            if (facility != null)
            {
                // Try to assign workers using reflection
                var canAssignMethod = facility.GetType().GetMethod("CanAssignWorkers");
                var assignMethod = facility.GetType().GetMethod("AssignWorkers");
                
                bool canAssign = true;
                if (canAssignMethod != null)
                {
                    canAssign = (bool)canAssignMethod.Invoke(facility, new object[] { workerCount });
                }
                
                if (canAssign)
                {
                    if (assignMethod != null)
                    {
                        assignMethod.Invoke(facility, new object[] { workerCount });
                    }
                    
                    assignedWorkers += workerCount;
                    OnWorkersAssigned?.Invoke();
                    
                    Debug.Log($"Assigned {workerCount} workers to {facilityId}. Available workers: {AvailableWorkers}");
                    return true;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MasterGameManager] Error assigning workers: {e.Message}");
        }
        
        Debug.LogWarning($"Cannot assign {workerCount} workers to {facilityId}");
        return false;
    }
    
    /// <summary>
    /// Check if all facilities are properly staffed with error handling
    /// </summary>
    private bool CheckAllFacilitiesStaffed()
    {
        if (buildingSystem == null)
        {
            Debug.LogWarning("[MasterGameManager] BuildingSystem not available, assuming facilities are staffed");
            return true; // Assume staffed for testing
        }
        
        try
        {
            var facilities = buildingSystem.GetAllFacilities();
            if (facilities == null || facilities.Count == 0)
            {
                return true; // No facilities to check
            }
            
            foreach (var facility in facilities)
            {
                if (facility == null) continue;
                
                // Try to check IsStaffed property with reflection
                var isStaffedProperty = facility.GetType().GetProperty("IsStaffed");
                if (isStaffedProperty != null)
                {
                    bool isStaffed = (bool)isStaffedProperty.GetValue(facility);
                    if (!isStaffed) return false;
                }
            }
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MasterGameManager] Error checking facility staffing: {e.Message}");
            return true; // Assume staffed for testing
        }
    }
    
    /// <summary>
    /// Enable facilities that have workers assigned with error handling
    /// </summary>
    private void EnableFacilitiesWithWorkers()
    {
        if (buildingSystem == null)
        {
            Debug.LogWarning("[MasterGameManager] BuildingSystem not available for enabling facilities");
            return;
        }
        
        try
        {
            var facilities = buildingSystem.GetAllFacilities();
            if (facilities == null) return;
            
            foreach (var facility in facilities)
            {
                if (facility == null) continue;
                
                // Try to check IsStaffed and SetEnabled with reflection
                var isStaffedProperty = facility.GetType().GetProperty("IsStaffed");
                var setEnabledMethod = facility.GetType().GetMethod("SetEnabled");
                
                if (isStaffedProperty != null && setEnabledMethod != null)
                {
                    bool isStaffed = (bool)isStaffedProperty.GetValue(facility);
                    setEnabledMethod.Invoke(facility, new object[] { isStaffed });
                }
            }
            
            Debug.Log("Enabled facilities with assigned workers");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MasterGameManager] Error enabling facilities: {e.Message}");
        }
    }
    
    /// <summary>
    /// Check if the game should end
    /// </summary>
    private bool ShouldEndGame()
    {
        // Game ends when all emergency tasks are completed
        return !_emergencyTasksActive && _activeEmergencyTasks.Count == 0;
    }
    
    /// <summary>
    /// End the game
    /// </summary>
    private void EndGame()
    {
        Debug.Log("Game completed! All emergency tasks finished.");
        SetCurrentPhase(GlobalEnums.GamePhase.GameComplete);
        
        // You can add game completion logic here
        // Show final score, statistics, etc.
    }
    
    /// <summary>
    /// Manual method to complete emergency tasks for testing
    /// </summary>
    [System.Obsolete("This method is for testing purposes only")]
    public void CompleteAllEmergencyTasksForTesting()
    {
        if (_activeEmergencyTasks.Count > 0)
        {
            Debug.Log($"[MasterGameManager] Completing {_activeEmergencyTasks.Count} emergency tasks for testing");
            _activeEmergencyTasks.Clear();
        }
    }
    
    // Events for phase completion
    public event System.Action OnConstructionPhaseCompleted;
    public event System.Action OnWorkerAssignmentCompleted;
    public event System.Action OnEndTurnRequested;
    
    /// <summary>
    /// Called when player completes construction phase
    /// </summary>
    public void CompleteConstructionPhase()
    {
        Debug.Log("[MasterGameManager] Construction phase completed");
        IsPaused = false;
        OnConstructionPhaseCompleted?.Invoke();
    }
    
    /// <summary>
    /// Called when player completes worker assignment phase
    /// </summary>
    public void CompleteWorkerAssignmentPhase()
    {
        Debug.Log("[MasterGameManager] Worker assignment phase completed");
        IsPaused = false;
        OnWorkerAssignmentCompleted?.Invoke();
    }
    
    /// <summary>
    /// Called when player clicks End Turn button (if needed)
    /// </summary>
    public void EndPlayerTurn()
    {
        Debug.Log("[MasterGameManager] End Turn requested");
        
        if (CurrentPhase == GlobalEnums.GamePhase.PlayerTurn)
        {
            IsPaused = false;
            OnEndTurnRequested?.Invoke();
        }
        else if (CurrentPhase == GlobalEnums.GamePhase.Construction)
        {
            CompleteConstructionPhase();
        }
        else if (CurrentPhase == GlobalEnums.GamePhase.WorkerAssignment)
        {
            CompleteWorkerAssignmentPhase();
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
        
            // Ensure proper time scale for animations
            if (phase == GlobalEnums.GamePhase.Simulation)
            {
                Time.timeScale = Speed; // Use game speed during simulation
            }
            else if (phase == GlobalEnums.GamePhase.WorkerAssignment || 
                     phase == GlobalEnums.GamePhase.Construction ||
                     phase == GlobalEnums.GamePhase.EmergencyTasks)
            {
                Time.timeScale = 1f; // Normal time for UI phases
            }
        
            OnPhaseChanged?.Invoke(phase);
        }
    }
    
    /// <summary>
    /// Start the simulation phase timer
    /// </summary>
    private void StartSimulation()
    {
        _isSimulating = true;
        _simulationTimer = 0f;
        IsPaused = false;
    
        // ADD THIS: Trigger flood simulation when simulation phase starts
        if (disasterManager != null && disasterManager.floodManager != null)
        {
            disasterManager.floodManager.SimulateFlooding();
            Debug.Log("[MasterGameManager] Flood simulation triggered");
        }
        else
        {
            Debug.LogWarning("[MasterGameManager] DisasterManager or FloodManager not found!");
        }
    
        Debug.Log("[MasterGameManager] Starting flood simulation phase");
    }
    
    /// <summary>
    /// Called when simulation period ends
    /// </summary>
    private void EndSimulation()
    {
        _isSimulating = false;
        Debug.Log("[MasterGameManager] Flood simulation phase completed");
    }
    
    /// <summary>
    /// Advances the round and day counters
    /// </summary>
    private void AdvanceRound()
    {
        _currentRound++;
        
        // Reset worker assignments for new round
        assignedWorkers = 0;
        
        if (_currentRound % 4 == 0)
        {
            _currentDay++;
            UpdateWeather();
        }
        
        Debug.Log($"[MasterGameManager] Advanced to Round {_currentRound}, Day {_currentDay}");
        OnRoundAdvanced?.Invoke(_currentRound, _currentDay);
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
