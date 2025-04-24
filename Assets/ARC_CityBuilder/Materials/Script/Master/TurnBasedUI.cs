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
            _gameManager.OnSimulationTick += UpdateSimulationTimer; // Add this line
            
            // Initial update
            UpdateUI();
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                // Unsubscribe from events
                _gameManager.OnPhaseChanged -= OnPhaseChanged;
                _gameManager.OnSimulationTick -= UpdateSimulationTimer; // Add this line
            }
        }

        // Add this method to handle simulation timer updates
        private void UpdateSimulationTimer(float currentTime)
        {
            if (simulationStatusText != null && _gameManager.CurrentPhase == GlobalEnums.GamePhase.Simulation)
            {
                float timeRemaining = _gameManager.SimulationRemainingTime;
                simulationStatusText.text = $"Simulating: {timeRemaining:F1} sec remaining";
            }
        }
        
        /// <summary>
        /// Called when the game phase changes
        /// </summary>
        private void OnPhaseChanged(GlobalEnums.GamePhase newPhase)
        {
            UpdateUI();
            
            // Handle specific phase updates
            if (newPhase == GlobalEnums.GamePhase.PlayerTurn)
            {
                // Always enable end turn button during player turn
                if (endTurnButton != null)
                {
                    endTurnButton.interactable = true;
                    DebugLog("End Turn button ENABLED due to player turn phase");
                }
            }
            else
            {
                // Disable button during other phases
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
                
            // Update phase text
            if (phaseText != null)
            {
                switch (_gameManager.CurrentPhase)
                {
                    case GlobalEnums.GamePhase.Start:
                        phaseText.text = "Phase: Round Start";
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
                case GlobalEnums.GamePhase.Simulation:
                    float timeRemaining = _gameManager.SimulationRemainingTime;
                    simulationStatusText.text = $"Simulating: {timeRemaining:F1} sec remaining";
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