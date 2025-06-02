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
        [Header("UI References")]
        public Button endTurnButton;
        public TextMeshProUGUI phaseText;
        public TextMeshProUGUI roundDayText;
        public TextMeshProUGUI simulationStatusText;
        public Slider gameSpeedSlider;
        
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
            
            // Handle specific phase updates - Enable button for interactive phases
            if (newPhase == GlobalEnums.GamePhase.Construction || 
                newPhase == GlobalEnums.GamePhase.WorkerAssignment || 
                newPhase == GlobalEnums.GamePhase.PlayerTurn)
            {
                // Enable end turn button during interactive phases
                if (endTurnButton != null)
                {
                    endTurnButton.interactable = true;
                    
                    // Update button text based on phase
                    var buttonText = endTurnButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonText != null)
                    {
                        switch (newPhase)
                        {
                            case GlobalEnums.GamePhase.Construction:
                                buttonText.text = "Finish Construction";
                                break;
                            case GlobalEnums.GamePhase.WorkerAssignment:
                                buttonText.text = "Assign Workers";
                                break;
                            case GlobalEnums.GamePhase.PlayerTurn:
                                buttonText.text = "End Turn";
                                break;
                        }
                    }
                    
                    DebugLog($"End Turn button ENABLED for {newPhase} phase");
                }
            }
            else
            {
                // Disable button during automatic phases
                if (endTurnButton != null)
                {
                    endTurnButton.interactable = false;
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
                
            switch (_gameManager.CurrentPhase)
            {
                case GlobalEnums.GamePhase.Start:
                    simulationStatusText.text = "Starting new round...";
                    break;
                case GlobalEnums.GamePhase.Construction:
                    simulationStatusText.text = "Build facilities for the community";
                    break;
                case GlobalEnums.GamePhase.WorkerAssignment:
                    simulationStatusText.text = "Assign workers to facilities";
                    break;
                case GlobalEnums.GamePhase.Simulation:
                    float timeRemaining = _gameManager.SimulationRemainingTime;
                    simulationStatusText.text = $"Simulating flood: {timeRemaining:F1} sec remaining";
                    break;
                case GlobalEnums.GamePhase.EmergencyTasks:
                    simulationStatusText.text = "Complete emergency food delivery tasks";
                    break;
                case GlobalEnums.GamePhase.PlayerTurn:
                    simulationStatusText.text = "Your turn - Make your moves";
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
                default:
                    simulationStatusText.text = $"Current phase: {_gameManager.CurrentPhase}";
                    break;
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
