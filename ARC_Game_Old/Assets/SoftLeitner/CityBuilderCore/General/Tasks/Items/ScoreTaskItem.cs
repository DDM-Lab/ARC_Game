using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// task that completes when a score reaches a certain value
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_score_task_item.html")]
    public class ScoreTaskItem : TaskItem
    {
        [Tooltip("the score that needs to reach a certain value for the task to complete")]
        public Score Score;
        [Tooltip("the minimum value the score needs to reach to complete the task")]
        public int Value;
        [Tooltip("optional text that can be used to display the current progress(for example '5/10')")]
        public TMPro.TMP_Text Text;

        private IScoresCalculator _calculator;

        public override bool IsFinished => State > 0;

        private void Start()
        {
            _calculator = Dependencies.Get<IScoresCalculator>();
            OnEnable();
        }

        private void OnEnable()
        {
            if (_calculator == null)
                return;

            if (IsFinished)
            {
                if (Text)
                    Text.text = $"{Value}/{Value}";

                Set?.Invoke();
            }
            else
            {
                if (Text)
                    Text.text = $"0/{Value}";

                _calculator.Calculated += calculated;
            }
        }
        private void OnDisable()
        {
            _calculator.Calculated -= calculated;
        }

        private void calculated()
        {
            var value = _calculator.GetValue(Score);
            if (value < Value)
            {
                if (Text)
                    Text.text = $"{value}/{Value}";
            }
            else
            {
                State = 1;
                if (Text)
                    Text.text = $"{Value}/{Value}";

                _calculator.Calculated -= calculated;

                Finished?.Invoke();
            }
        }
    }
}
