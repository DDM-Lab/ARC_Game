using UnityEngine;
using UnityEngine.UI;

namespace CityBuilderCore
{
    /// <summary>
    /// UI Controller for the turn-based system
    /// </summary>
    public class TurnBasedUIController : MonoBehaviour
    {
        [Tooltip("Reference to the TurnBasedGameManager")]
        public TurnBasedGameManager GameManager;

        [Header("UI Elements")]
        public Button EndRoundButton;
        public Slider SimulationSpeedSlider;
        public InputField SimulationDurationInput;
        public Text CurrentRoundText;

        private int _currentRound = 1;

        private void Start()
        {
            if (GameManager == null)
                GameManager = FindObjectOfType<TurnBasedGameManager>();

            if (EndRoundButton != null && GameManager != null)
            {
                // Remove any existing listeners and add our own
                EndRoundButton.onClick.RemoveAllListeners();
                EndRoundButton.onClick.AddListener(EndRound);
            }

            if (SimulationSpeedSlider != null && GameManager != null)
            {
                SimulationSpeedSlider.value = GameManager.Speed;
                SimulationSpeedSlider.onValueChanged.AddListener(OnSpeedChanged);
            }

            if (SimulationDurationInput != null && GameManager != null)
            {
                SimulationDurationInput.text = GameManager.SimulationSeconds.ToString();
                SimulationDurationInput.onEndEdit.AddListener(OnDurationChanged);
            }

            UpdateUI();
        }

        private void EndRound()
        {
            if (GameManager != null)
            {
                GameManager.StartSimulation();
                _currentRound++;
                UpdateUI();
            }
        }

        private void OnSpeedChanged(float speed)
        {
            if (GameManager != null)
                GameManager.Speed = speed;
        }

        private void OnDurationChanged(string durationText)
        {
            if (GameManager != null && float.TryParse(durationText, out float duration))
                GameManager.SetSimulationDuration(duration);
        }

        private void UpdateUI()
        {
            if (CurrentRoundText != null)
                CurrentRoundText.text = $"Round: {_currentRound}";
        }
    }
}
