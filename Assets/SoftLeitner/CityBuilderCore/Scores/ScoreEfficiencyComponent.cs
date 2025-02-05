using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// derives its building efficiency from a score
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_score_efficiency_component.html")]
    public class ScoreEfficiencyComponent : BuildingComponent, IEfficiencyFactor
    {
        public override string Key => "SCO";

        [Tooltip("the value of this score will be used")]
        public Score Score;
        [Tooltip("the minimum efficiency returned so the building does not stall even in a bad position")]
        public float MinValue = 0.5f;
        [Tooltip("the layer value needed to reach max efficiency")]
        public int MaxScoreValue = 100;
        [Tooltip("whether the efficiency gets clamped to 1")]
        public bool MaxClamped = false;

        public bool IsWorking => true;
        public float Factor
        {
            get
            {
                var val = Dependencies.Get<IScoresCalculator>().GetValue(Score) / (float)MaxScoreValue;

                if (MaxClamped)
                    val = Mathf.Min(1f, val);

                val = Mathf.Max(MinValue, val);

                return val;
            }
        }
    }
}