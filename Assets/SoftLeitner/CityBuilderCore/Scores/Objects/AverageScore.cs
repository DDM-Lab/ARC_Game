using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// calculates the mean average of other scores
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_average_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(AverageScore))]
    public class AverageScore : Score
    {
        [Tooltip("(s1+s2+s3)/3=AverageScore")]
        public Score[] Scores;

        public override int Calculate() => Mathf.RoundToInt((float)Scores.Average(s => s.Calculate()));
    }
}