using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// multiplies another score
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_multiplied_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(MultipliedScore))]
    public class MultipliedScore : Score
    {
        [Tooltip("the score that will be multiplied")]
        public Score Score;
        [Tooltip("the multiplier the other score will be multiplied with")]
        public float Multiplier;

        public override int Calculate() => Mathf.RoundToInt(Score.Calculate() * Multiplier);
    }
}