using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes a score<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_score_panel.html")]
    public class ScorePanel : MonoBehaviour
    {
        [Tooltip("the score that will be shown")]
        public Score Score;
        [Tooltip("optional text field for the scores name")]
        public TMPro.TMP_Text Name;
        [Tooltip("optional text field for additional info(the underlying score for threshold scores)")]
        public TMPro.TMP_Text Info;
        [Tooltip("optional text field for the calculated score value")]
        public TMPro.TMP_Text Value;

        private IScoresCalculator _calculator;

        private void Start()
        {
            _calculator = Dependencies.Get<IScoresCalculator>();

            if (Name)
                Name.text = Score.Name;
        }

        private void Update()
        {
            if (Info)
            {
                if (Score is ThresholdScore threshold)
                    Info.text = _calculator.GetValue(threshold.Score).ToString();
                else
                    Info.text = string.Empty;
            }

            if (Value)
                Value.text = _calculator.GetValue(Score).ToString();
        }
    }
}