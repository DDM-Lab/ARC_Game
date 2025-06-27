using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// transform a score by defining thresholds<br/>
    /// for example the HAPEmploymentScore in THREE defines that employment under 50% causes a loss of 8 happiness
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_threshold_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(ThresholdScore))]
    public class ThresholdScore : Score
    {
        [Serializable]
        public class ThresholdItem
        {
            public int Threshold;
            public int Value;
        }

        [Tooltip("value that is returned if none of the thresholds are reached")]
        public int BaseValue;
        [Tooltip("the value of this score will be checked against the thresholds")]
        public Score Score;
        [Tooltip("value of the highest of these that is reached will be returned")]
        public ThresholdItem[] Items;

        public override int Calculate()
        {
            int score = Score.Calculate();
            int value = BaseValue;

            foreach (var item in Items)
            {
                if (score >= item.Threshold)
                    value = item.Value;
            }

            return value;
        }
    }
}