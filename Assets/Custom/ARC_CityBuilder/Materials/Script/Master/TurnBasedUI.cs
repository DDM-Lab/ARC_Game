using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CityBuilderCore
{
    /// <summary>
    /// UI controller for the turn-based system
    /// </summary>
    public class TurnBasedUI : MonoBehaviour
    {
        [System.Serializable]
        public class PhaseUIConfig
        {
            public GlobalEnums.GamePhase phase;
            public bool requiresPlayerInput;
            public string buttonText;
            public string statusText;
        }

        [Header("UI References")]
        public Button endTurnButton;
        public TextMeshProUGUI phaseText;
        public TextMeshProUGUI roundDayText;
        public TextMeshProUGUI simulationStatusText;
        public Slider gameSpeedSlider;
        
        [Header("Phase Configuration")]
        public PhaseUIConfig[] phaseConfigs = new PhaseUIConfig[]
        {
            new PhaseUIConfig { phase = GlobalEnums.GamePhase.Construction, requiresPlayerInput = true, buttonText = "Finish Construction", statusText = "Build facilities for the community" },
            new PhaseUIConfig { phase = GlobalEnums.GamePhase.WorkerAssignment, requiresPlayerInput = true, buttonText = "Complete Assignment", statusText = "Assign workers to facilities" },
            new PhaseUIConfig { phase = GlobalEnums.GamePhase.EmergencyTasks, requiresPlayerInput = true, buttonText = "End Turn", statusText = "Complete emergency tasks or end turn" },
            new PhaseUIConfig { phase = GlobalEnums.GamePhase.PlayerTurn, requiresPlayerInput = true, buttonText = "End Turn", statusText = "Your turn - Make your moves" }
        };
        
        [Header("Debug Options")]
        public bool showDebugMessages = true;
        
        private MasterGameManager _gameManager;
        
        private void Start()
        {
            _gameManager = MasterGameManager.Instance;
            
            if (_gameManager == null)
            {
                Debug.LogError("TurnBasedUI: Could not find MasterGameManager!");
                return;
            }
            
            // Set up button listeners
            if (endTurnButton != null)
            {
                endTurnButton.onClick.RemoveAllListeners();
                endTurnButton.onClick.AddListener(OnEndTurnClicked);
                DebugLog($"End Turn button initialized, interactable: {endTurnButton.interactable}");
            }
            
            if (gameSpeedSlider != null)
            {
                gameSpeedSlider.value = _gameManager.Speed;
                gameSpeedSlider.onValueChanged.AddListener(OnSpeedChanged);
            }
            
            // Subscribe to game events
            _gameManager.OnPhaseChanged += OnPhaseChanged;
            _gameManager.OnRoundAdvanced += (round, day) => UpdateRoundDayText(round, day);
            _gameManager.OnSimulationTick += UpdateSimulationTimer;
            
            // Initial update
            UpdateUI();
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                // Unsubscribe from events
                _gameManager.OnPhaseChanged -= OnPhaseChanged;
                _gameManager.OnSimulationTick -= UpdateSimulationTimer;
            }
        }

        // Handle simulation timer updates
        private void UpdateSimulationTimer(float currentTime)
        {
            if (simulationStatusText != null && _gameManager.CurrentPhase == GlobalEnums.GamePhase.Simulation)
            {
                float timeRemaining = _gameManager.SimulationRemainingTime;
                simulationStatusText.text = $"Flood Simulation: {timeRemaining:F1} sec remaining";
            }
        }
        
        /// <summary>
        /// Called when the game phase changes
        /// </summary>
        private void OnPhaseChanged(GlobalEnums.GamePhase newPhase)
        {
            UpdateUI();
            
            // Find configuration for this phase
            var config = System.Array.Find(phaseConfigs, c => c.phase == newPhase);
            
            if (config != null && config.requiresPlayerInput)
            {
                // Enable end turn button for interactive phases
                if (endTurnButton != null)
                {
                    endTurnButton.interactable = true;
                    
                    // Update button text based on configuration
                    var buttonText = endTurnButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonText != null)
                    {
                        buttonText.text = config.buttonText;
                    }
                    
                    DebugLog($"End Turn button ENABLED for {newPhase} phase with text: {config.buttonText}");
                }
            }
            else
            {
                // Disable button during automatic phases
                if (endTurnButton != null)
                {
                    endTurnButton.interactable = false;
                    
                    // Reset button text to default during non-interactive phases
                    var buttonText = endTurnButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonText != null)
                    {
                        buttonText.text = "End Turn";
                    }
                    
                    DebugLog($"End Turn button DISABLED due to {newPhase} phase");
                }
            }
        }
        
        private void OnEndTurnClicked()
        {
            DebugLog("End Turn button clicked");
            
            if (_gameManager != null)
            {
                _gameManager.EndPlayerTurn();
            }
        }
        
        private void OnSpeedChanged(float speed)
        {
            if (_gameManager != null)
            {
                _gameManager.Speed = speed;
            }
        }
        
        private void UpdateUI()
        {
            if (_gameManager == null)
                return;
                
            // Update phase text with all new phases
            if (phaseText != null)
            {
                switch (_gameManager.CurrentPhase)
                {
                    case GlobalEnums.GamePhase.Start:
                        phaseText.text = "Phase: Round Start";
                        break;
                    case GlobalEnums.GamePhase.Construction:
                        phaseText.text = "Phase: Construction";
                        break;
                    case GlobalEnums.GamePhase.WorkerAssignment:
                        phaseText.text = "Phase: Worker Assignment";
                        break;
                    case GlobalEnums.GamePhase.Simulation:
                        phaseText.text = "Phase: Flood Simulation";
                        break;
                    case GlobalEnums.GamePhase.EmergencyTasks:
                        phaseText.text = "Phase: Emergency Tasks";
                        break;
                    case GlobalEnums.GamePhase.PlayerTurn:
                        phaseText.text = "Phase: Player Turn";
                        break;
                    case GlobalEnums.GamePhase.DisasterEvents:
                        phaseText.text = "Phase: Disaster Events";
                        break;
                    case GlobalEnums.GamePhase.End:
                        phaseText.text = "Phase: Round End";
                        break;
                    case GlobalEnums.GamePhase.GameComplete:
                        phaseText.text = "Phase: Game Complete";
                        break;
                    default:
                        phaseText.text = $"Phase: {_gameManager.CurrentPhase}";
                        break;
                }
            }
            
            // Update round/day text
            UpdateRoundDayText(_gameManager.CurrentRound, _gameManager.CurrentDay);
            
            // Update simulation status if available
            UpdateSimulationStatus();
        }
        
        private void UpdateSimulationStatus()
        {
            if (simulationStatusText == null)
                return;
            
            // Try to get status from phase configuration first
            var config = System.Array.Find(phaseConfigs, c => c.phase == _gameManager.CurrentPhase);
            if (config != null && !string.IsNullOrEmpty(config.statusText))
            {
                // For simulation phase, still show the timer
                if (_gameManager.CurrentPhase == GlobalEnums.GamePhase.Simulation)
                {
                    float timeRemaining = _gameManager.SimulationRemainingTime;
                    simulationStatusText.text = $"Simulating flood: {timeRemaining:F1} sec remaining";
                }
                else
                {
                    simulationStatusText.text = config.statusText;
                }
                return;
            }
            
            // Fallback to hardcoded status messages
            switch (_gameManager.CurrentPhase)
            {
                case GlobalEnums.GamePhase.Start:
                    simulationStatusText.text = "Starting new round...";
                    break;
                case GlobalEnums.GamePhase.Simulation:
                    float timeRemaining = _gameManager.SimulationRemainingTime;
                    simulationStatusText.text = $"Simulating flood: {timeRemaining:F1} sec remaining";
                    break;
                case GlobalEnums.GamePhase.DisasterEvents:
                    simulationStatusText.text = "Processing disaster events...";
                    break;
                case GlobalEnums.GamePhase.End:
                    simulationStatusText.text = "Ending round...";
                    break;
                case GlobalEnums.GamePhase.GameComplete:
                    simulationStatusText.text = "Game completed successfully!";
                    break;
                case GlobalEnums.GamePhase.EmergencyTasks:
                    UpdateEmergencyTasksStatus();
                    break;
                default:
                    simulationStatusText.text = $"Current phase: {_gameManager.CurrentPhase}";
                    break;
            }
        }
        
        private void UpdateEmergencyTasksStatus()
        {
            if (_gameManager.CurrentPhase == GlobalEnums.GamePhase.EmergencyTasks && simulationStatusText != null)
            {
                // Get task completion info from TaskManager
                if (_gameManager.taskManager != null)
                {
                    int completedTasks = _gameManager.taskManager.CompletedTaskCount;
                    int totalTasks = _gameManager.taskManager.ActiveTaskCount + completedTasks;
            
                    simulationStatusText.text = $"Emergency Tasks: {completedTasks}/{totalTasks} completed - Click End Turn when ready";
                }
            }
        }
        
        private void UpdateRoundDayText(int round, int day)
        {
            if (roundDayText != null)
            {
                roundDayText.text = $"Day {day}, Round {round}";
            }
        }
        
        private void DebugLog(string message)
        {
            if (showDebugMessages)
            {
                Debug.Log($"[TurnBasedUI] {message}");
            }
        }
    }
}
