using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// employment percentage for a certain population
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_employment_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(EmploymentScore))]
    public class EmploymentScore : Score
    {
        [Tooltip("the population for which  for calculate employment percentage")]
        public Population Population;
        [Tooltip("whether the result should be clamped between 0 and 100")]
        public bool Clamped;

        public override int Calculate()
        {
            var value = Mathf.RoundToInt(Dependencies.Get<IEmploymentManager>().GetEmploymentRate(Population) * 100f);
            if (Clamped)
                value = Mathf.Clamp(value, 0, 100);
            return value;
        }
    }
}