using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// quantity of a certain population
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_population_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(PopulationScore))]
    public class PopulationScore : Score
    {
        [Tooltip("score is the current count of this population")]
        public Population Population;

        public override int Calculate()
        {
            return Dependencies.Get<IPopulationManager>().GetQuantity(Population);
        }
    }
}