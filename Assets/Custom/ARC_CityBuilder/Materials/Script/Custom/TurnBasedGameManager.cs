using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CityBuilderCore
{
    /// <summary>
    /// Extension of DefaultGameManager that adds turn-based functionality
    /// </summary>
    public class TurnBasedGameManager : DefaultGameManager
    {
        [Header("Turn-Based Settings")]
        [Tooltip("Number of seconds to simulate when the End Round button is clicked")]
        public float SimulationSeconds = 30f;
        [Tooltip("Reference to the End Round button")]
        public Button EndRoundButton;
        [Tooltip("Whether the game should start in paused mode")]
        public bool StartPaused = true;

        [Header("UI Feedback")]
        [Tooltip("Optional UI element to display the current simulation status")]
        public Text SimulationStatusText;

        private bool _isSimulating = false;
        private float _simulationTimer = 0f;

        protected override void Start()
        {
            base.Start();

            if (EndRoundButton != null)
                EndRoundButton.onClick.AddListener(StartSimulation);

            if (StartPaused)
                IsPaused = true;

            UpdateUI();
        }

        protected override void Update()
        {
            base.Update();

            if (_isSimulating)
            {
                // Keep track of actual time elapsed (accounting for game speed)
                _simulationTimer += Time.deltaTime * Speed;

                // Check if simulation time is complete
                if (_simulationTimer >= SimulationSeconds)
                {
                    EndSimulation();
                }

                UpdateUI();
            }
        }

        /// <summary>
        /// Starts the simulation for the specified number of seconds
        /// </summary>
        public void StartSimulation()
        {
            if (_isSimulating || IsSaving || IsLoading)
                return;

            _isSimulating = true;
            _simulationTimer = 0f;

            // Make sure the game is running
            IsPaused = false;

            UpdateUI();
        }

        /// <summary>
        /// Ends the current simulation and pauses the game
        /// </summary>
        private void EndSimulation()
        {
            _isSimulating = false;
            _simulationTimer = 0f;

            // Pause the game until the next round
            IsPaused = true;

            UpdateUI();
        }

        /// <summary>
        /// Updates UI elements to show current simulation status
        /// </summary>
        private void UpdateUI()
        {
            if (SimulationStatusText != null)
            {
                if (_isSimulating)
                {
                    float remainingTime = SimulationSeconds - _simulationTimer;
                    SimulationStatusText.text = $"Simulating: {remainingTime:F1} seconds remaining";
                }
                else
                {
                    SimulationStatusText.text = "Waiting for end round...";
                }
            }

            if (EndRoundButton != null)
                EndRoundButton.interactable = !_isSimulating;
        }

        /// <summary>
        /// Manually end the current simulation (useful for testing or emergency stops)
        /// </summary>
        public void ForceEndSimulation()
        {
            if (_isSimulating)
                EndSimulation();
        }

        /// <summary>
        /// Set the duration of each simulation round
        /// </summary>
        public void SetSimulationDuration(float seconds)
        {
            SimulationSeconds = Mathf.Max(1f, seconds);
        }
    }
}
