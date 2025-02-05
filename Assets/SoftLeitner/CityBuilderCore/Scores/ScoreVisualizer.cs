using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes a score in unity ui
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_score_visualizer.html")]
    public class ScoreVisualizer : TooltipOwnerBase
    {
        [Tooltip("the score to visualize")]
        public Score Score;
        [Tooltip("the maximum value used to calculate the ration for the BarTransform")]
        public int Maximum = 100;
        [Tooltip("optional text field that will be filled with the scores name")]
        public TMPro.TMP_Text NameText;
        [Tooltip("optional text field that will be filled with the score value")]
        public TMPro.TMP_Text ScoreText;
        [Tooltip("optional transform that will be scaled to the value divided by the Maximum")]
        public RectTransform BarTransform;

        public override string TooltipName => Score.Name;
        public override string TooltipDescription => $"{_calculator.GetValue(Score)}/{Maximum}";

        private Vector2 _sizeFull;
        private IScoresCalculator _calculator;

        private void Start()
        {
            if (NameText)
                NameText.text = Score.Name;

            if (BarTransform)
                _sizeFull = BarTransform.sizeDelta;

            _calculator = Dependencies.Get<IScoresCalculator>();
            _calculator.Calculated += scoresCalculated;

            scoresCalculated();
        }

        private void scoresCalculated()
        {
            int value = _calculator.GetValue(Score);

            if (BarTransform)
                BarTransform.sizeDelta = Vector2.Lerp(Vector2.zero, _sizeFull, value / (float)Maximum);

            if (ScoreText)
                ScoreText.text = value.ToString();
        }
    }
}